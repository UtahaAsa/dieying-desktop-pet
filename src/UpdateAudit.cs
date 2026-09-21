using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Web.Script.Serialization;

namespace DieYing
{
    internal static class UpdateAudit
    {
        internal static string Run(string folder)
        {
            Directory.CreateDirectory(folder);
            var log = new StringBuilder();
            Check(UpdateService.ParseVersion("v1.10.0") > UpdateService.ParseVersion("1.9.9"), "numeric-version", log);
            Reject(delegate { UpdateService.ParseVersion("v2.0.0-beta"); }, "reject-prerelease-tag", log);
            string json = new JavaScriptSerializer().Serialize(new {
                draft = false, prerelease = false, tag_name = "v1.0.0",
                assets = new[] { new { name = UpdateService.PackageName, size = 100,
                    browser_download_url = "https://github.com/" + UpdateService.Repository + "/releases/download/v1.0.0/" + UpdateService.PackageName,
                    digest = "sha256:" + new string('a',64) } }
            });
            Check(UpdateService.ParseRelease(json).version == new Version(1,0,0), "release-parse", log);
            string legacyJson = json.Replace(UpdateService.PackageName, UpdateService.LegacyPackageName);
            Check(UpdateService.ParseRelease(legacyJson).version == new Version(1,0,0), "legacy-package-parse", log);
            Check(UpdateService.ParseRelease(json.Replace("v1.0.0", "v0.1.0")) == null, "ignore-downgrade", log);
            Check(UpdateService.ParseRelease(json.Replace("\"draft\":false", "\"draft\":true")) == null, "ignore-draft", log);
            Reject(delegate { UpdateService.ParseRelease(json.Replace("github.com/", "github.com.evil.example/")); }, "reject-other-host", log);
            Reject(delegate { UpdateService.ParseRelease(json.Replace("sha256:", "md5:")); }, "require-sha256", log);
            string archive = Path.Combine(folder,"valid.zip");
            using (var zip = ZipFile.Open(archive,ZipArchiveMode.Create))
            {
                Add(zip,"DieYing.exe","fixture"); Add(zip,"assets/app.ico","icon"); Add(zip,"version.txt","1.0.0");
            }
            string target = Path.GetFullPath(Path.Combine(folder,"extracted"));
            UpdateService.ExtractPackage(archive,target);
            Check(File.Exists(Path.Combine(target,"assets","app.ico")),"extract-valid",log);
            string[] badNames = { "assets/../../escape.txt", "data/settings.ini", "assets/x:stream", "assets/a./bad", "assets//bad" };
            for (int i=0;i<badNames.Length;i++)
            {
                string bad = Path.Combine(folder,"invalid-"+i+".zip");
                using (var zip=ZipFile.Open(bad,ZipArchiveMode.Create)) Add(zip,badNames[i],"bad");
                string dest = Path.Combine(folder,"rejected-"+i);
                Reject(delegate { UpdateService.ExtractPackage(bad,dest); },"reject-path-"+i,log);
                Check(!Directory.Exists(dest),"validate-before-extract-"+i,log);
            }
            string textPath=Path.Combine(folder,"hash.txt"); File.WriteAllText(textPath,"abc",new UTF8Encoding(false));
            Check(UpdateService.Hash(textPath)=="ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad","sha256-known-vector",log);
            for(int mode=0;mode<2;mode++)
            {
                string install=Path.Combine(folder,"install-"+mode), stage=Path.Combine(folder,"stage-"+mode), payload=Path.Combine(stage,"package");
                Directory.CreateDirectory(Path.Combine(install,"assets")); Directory.CreateDirectory(Path.Combine(install,"data"));
                Directory.CreateDirectory(Path.Combine(payload,"assets"));
                File.WriteAllText(Path.Combine(install,"蝶应.exe"),"old"); File.WriteAllText(Path.Combine(install,"assets","a.txt"),"old-art");
                File.WriteAllText(Path.Combine(install,"data","settings.ini"),"size=480");
                File.WriteAllText(Path.Combine(payload,"DieYing.exe"),"new"); File.WriteAllText(Path.Combine(payload,"assets","a.txt"),"new-art");
                int starts=0; bool failed=false;
                try { UpdateService.ReplacePayload(install,payload,stage,"蝶应.exe",delegate(string path)
                    { starts++; if(mode==1 && starts==1)throw new IOException("simulated restart failure"); }); }
                catch(IOException) { failed=true; }
                Check(failed==(mode==1),"install-outcome-"+mode,log);
                Check(File.ReadAllText(Path.Combine(install,"蝶应.exe"))==(mode==0?"new":"old"),"install-exe-"+mode,log);
                Check(File.ReadAllText(Path.Combine(install,"assets","a.txt"))==(mode==0?"new-art":"old-art"),"install-assets-"+mode,log);
                Check(File.ReadAllText(Path.Combine(install,"data","settings.ini"))=="size=480","preserve-settings-"+mode,log);
            }
            File.WriteAllText(Path.Combine(folder,"update-checks.txt"),log.ToString());
            return log.ToString();
        }
        private static void Add(ZipArchive zip,string path,string value)
        { using(var writer=new StreamWriter(zip.CreateEntry(path).Open()))writer.Write(value); }
        private static void Reject(Action action,string name,StringBuilder log)
        {
            bool rejected=false; try { action(); } catch(InvalidDataException) { rejected=true; }
            Check(rejected,name,log);
        }
        private static void Check(bool result,string name,StringBuilder log)
        { if(!result)throw new InvalidOperationException("FAIL "+name); log.AppendLine("PASS "+name); }
    }
}
