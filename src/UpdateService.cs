using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using System.IO.Compression;

namespace DieYing
{
    internal sealed class ReleaseInfo
    {
        internal Version version;
        internal string url, digest;
        internal long size;
    }

    /** <summary>从固定 GitHub 仓库检查稳定版，验证包并暂存；不接收凭据，不自动关闭桌宠。</summary> */
    internal static class UpdateService
    {
        internal const string Repository = "UtahaAsa/dieying-desktop-pet";
        internal const string PackageName = "DieShuDesktopPet-Windows.zip";
        internal const string LegacyPackageName = "DieYing-Windows.zip";
        internal const string VersionText = "0.3.7";
        internal const long MaxPackageBytes = 512L * 1024 * 1024;
        internal static readonly Version CurrentVersion = new Version(VersionText);

        internal static Version ParseVersion(string value)
        {
            if (value == null || !Regex.IsMatch(value, @"^v?\d+\.\d+\.\d+$"))
                throw new InvalidDataException("版本号格式无效。");
            return new Version(value.TrimStart('v'));
        }

        internal static ReleaseInfo ParseRelease(string json)
        {
            var parser = new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 };
            var release = parser.Deserialize<Dictionary<string, object>>(json);
            if (Convert.ToBoolean(release["draft"]) || Convert.ToBoolean(release["prerelease"])) return null;
            Version version = ParseVersion((string)release["tag_name"]);
            if (version <= CurrentVersion) return null;
            var assets = release["assets"] as System.Collections.ArrayList;
            if (assets == null) throw new InvalidDataException("新版没有可用的安装包。");
            foreach (Dictionary<string, object> asset in assets)
            {
                string packageName = (string)asset["name"];
                if (packageName != PackageName && packageName != LegacyPackageName) continue;
                string url = (string)asset["browser_download_url"];
                Uri address;
                string prefix = "/" + Repository + "/releases/download/";
                if (!Uri.TryCreate(url, UriKind.Absolute, out address) || address.Scheme != "https" ||
                    address.Host != "github.com" || !address.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal) ||
                    !address.AbsolutePath.EndsWith("/" + packageName, StringComparison.Ordinal) ||
                    address.UserInfo.Length != 0 || address.Query.Length != 0)
                    throw new InvalidDataException("更新包地址不属于蝶鼠桌宠发布仓库。");
                object rawDigest;
                string digest = asset.TryGetValue("digest", out rawDigest) ? rawDigest as string : null;
                if (digest == null || !Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$"))
                    throw new InvalidDataException("新版安装包缺少完整性校验，请稍后重试。");
                long size = Convert.ToInt64(asset["size"]);
                if (size <= 0 || size > MaxPackageBytes) throw new InvalidDataException("更新包大小无效。");
                return new ReleaseInfo { version = version, url = url, digest = digest.Substring(7), size = size };
            }
            throw new InvalidDataException("新版尚未上传 Windows 安装包。");
        }

        private static HttpWebRequest Request(string url)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "DieYing/" + VersionText;
            request.Accept = "application/vnd.github+json";
            request.Timeout = 15000; request.ReadWriteTimeout = 30000;
            return request;
        }

        internal static Task<ReleaseInfo> CheckAsync()
        {
            return Task.Run(delegate
            {
                try
                {
                    using (var response = Request("https://api.github.com/repos/" + Repository + "/releases/latest").GetResponse())
                    using (var reader = new StreamReader(response.GetResponseStream()))
                    {
                        char[] buffer = new char[2 * 1024 * 1024 + 1];
                        int count = reader.ReadBlock(buffer, 0, buffer.Length);
                        if (count == buffer.Length) throw new InvalidDataException("更新信息过大。");
                        return ParseRelease(new string(buffer, 0, count));
                    }
                }
                catch (WebException error)
                {
                    var response = error.Response as HttpWebResponse;
                    bool missing = response != null && response.StatusCode == HttpStatusCode.NotFound;
                    if (response != null) response.Dispose();
                    if (missing) throw new InvalidOperationException("发布仓库尚未开放，或还没有正式版本。");
                    throw new InvalidOperationException("暂时无法连接 GitHub，请检查网络后重试。", error);
                }
            });
        }

        internal static string Hash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        internal static Task<string> DownloadAsync(ReleaseInfo release)
        {
            return Task.Run(delegate
            {
                string stage = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "updates", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stage);
                string archive = Path.Combine(stage, PackageName);
                using (var response = Request(release.url).GetResponse())
                using (var input = response.GetResponseStream())
                using (var output = File.Create(archive))
                {
                    byte[] buffer = new byte[65536]; int count; long total = 0;
                    while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        total += count;
                        if (total > release.size || total > MaxPackageBytes) throw new InvalidDataException("下载大小不匹配。");
                        output.Write(buffer, 0, count);
                    }
                    if (total != release.size) throw new InvalidDataException("下载不完整，请重新下载。");
                }
                if (!String.Equals(Hash(archive), release.digest, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("安装包校验失败，未安装任何文件。");
                ExtractPackage(archive, Path.Combine(stage, "package"));
                string versionFile = Path.Combine(stage, "package", "version.txt");
                if (ParseVersion(File.ReadAllText(versionFile).Trim()) != release.version)
                    throw new InvalidDataException("安装包版本与发布信息不一致。");
                return stage;
            });
        }

        internal static void ExtractPackage(string archive, string destination)
        {
            string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            using (var zip = ZipFile.OpenRead(archive))
            {
                foreach (var entry in zip.Entries)
                {
                    string name = entry.FullName.Replace('\\', '/');
                    if (name.EndsWith("/", StringComparison.Ordinal)) continue;
                    if (!(name == "DieYing.exe" || name == "version.txt" || name.StartsWith("assets/", StringComparison.Ordinal)) ||
                        name.IndexOf(':') >= 0 || name.Split('/').AnyUnsafeSegment())
                        throw new InvalidDataException("安装包包含不允许的路径。");
                    string path = Path.GetFullPath(Path.Combine(destination, name));
                    if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !paths.Add(path))
                        throw new InvalidDataException("安装包路径重复或越界。");
                    total += entry.Length;
                    if (total > 1024L * 1024 * 1024 || zip.Entries.Count > 5000)
                        throw new InvalidDataException("安装包展开大小超出限制。");
                }
                if (!paths.Contains(Path.Combine(destination, "DieYing.exe")) || !paths.Contains(Path.Combine(destination, "version.txt")) ||
                    !paths.Contains(Path.Combine(destination, "assets", "app.ico")))
                    throw new InvalidDataException("安装包缺少程序或素材。");
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
                    string path = Path.GetFullPath(Path.Combine(destination, entry.FullName));
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    entry.ExtractToFile(path, false);
                }
            }
        }

        private static bool AnyUnsafeSegment(this string[] parts)
        {
            foreach (string part in parts)
                if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(" ") || part.EndsWith(".")) return true;
            return false;
        }

        internal static void LaunchInstaller(string stage)
        {
            string helper = Path.Combine(stage, "UpdateHost.exe");
            File.Copy(System.Windows.Forms.Application.ExecutablePath, helper, false);
            using (var process = Process.Start(new ProcessStartInfo(helper, "--apply-update " + Process.GetCurrentProcess().Id)
            { WorkingDirectory = stage, UseShellExecute = false, CreateNoWindow = true }))
            {
                var wait = Stopwatch.StartNew();
                while (!File.Exists(Path.Combine(stage, "ready")))
                {
                    if (process.HasExited || wait.ElapsedMilliseconds > 10000) throw new InvalidOperationException("更新助手未能启动。");
                    System.Threading.Thread.Sleep(50);
                }
            }
        }

        /** <summary>在独立临时进程中等待旧进程退出再替换程序与素材；失败则恢复备份，不改 data 设置。</summary> */
        internal static int ApplyUpdate(int processId)
        {
            string stage = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            string target = Path.GetFullPath(Path.Combine(stage, "..", "..", ".."));
            string source = Path.Combine(stage, "package");
            string exeName;
            using (var process = Process.GetProcessById(processId))
            {
                string executable = process.MainModule.FileName;
                if (!String.Equals(Path.GetDirectoryName(executable), target, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("更新目标与运行程序不一致。");
                exeName = Path.GetFileName(executable);
                File.WriteAllText(Path.Combine(stage, "ready"), "ready");
                if (!process.WaitForExit(30000)) throw new InvalidOperationException("桌宠尚未退出，更新取消。");
            }
            return ReplacePayload(target, source, stage, exeName, delegate(string executable)
            { Process.Start(new ProcessStartInfo(executable) { WorkingDirectory = target }); });
        }

        internal static int ReplacePayload(string target, string source, string stage, string exeName, Action<string> restart)
        {
            string backup = Path.Combine(stage, "backup");
            string targetExe = Path.Combine(target, exeName), targetAssets = Path.Combine(target, "assets");
            Directory.CreateDirectory(backup);
            bool oldExeMoved = false, oldAssetsMoved = false, newExeMoved = false, newAssetsMoved = false;
            try
            {
                File.Move(targetExe, Path.Combine(backup, exeName)); oldExeMoved = true;
                Directory.Move(targetAssets, Path.Combine(backup, "assets")); oldAssetsMoved = true;
                File.Move(Path.Combine(source, "DieYing.exe"), targetExe); newExeMoved = true;
                Directory.Move(Path.Combine(source, "assets"), targetAssets); newAssetsMoved = true;
                restart(targetExe);
                return 0;
            }
            catch
            {
                // 全部使用同一卷内移动；保留失败版本以便排查，不删除用户文件。
                if (newAssetsMoved) Directory.Move(targetAssets, Path.Combine(stage, "failed-assets"));
                if (newExeMoved) File.Move(targetExe, Path.Combine(stage, "failed.exe"));
                if (oldAssetsMoved) Directory.Move(Path.Combine(backup, "assets"), targetAssets);
                if (oldExeMoved) File.Move(Path.Combine(backup, exeName), targetExe);
                restart(targetExe);
                throw;
            }
        }
    }
}
