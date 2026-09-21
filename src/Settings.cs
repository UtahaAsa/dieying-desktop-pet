using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace DieYing
{
    internal sealed class AppSettings
    {
        internal int skin = 1, expression, size = 480, posX = int.MinValue, posY = int.MinValue, screenIndex = -1, frameRate = 60;
        internal bool keyboardEnabled = true, mouseEnabled = true, showKeyBubble = true, topMost = true, clickThrough, paused, mirror, idleMotion = true, keyLabels;
        internal float opacity = 1, mouseRange = 1, motionSpeed = 18;
        internal bool autoCheckUpdates = true;
        internal string hotkeySettings = "", hotkeyPause = "", hotkeyTopMost = "", hotkeyClickThrough = "", hotkeyUpdate = "", hotkeyResetPosition = "";

        /** <summary>读取本地设置；未知或格式错误的条目忽略，数值边界统一归一化。文件系统错误交由宿主处理。</summary> */
        internal static AppSettings Load(string path)
        {
            AppSettings value = new AppSettings();
            if (!File.Exists(path)) return value;
            foreach (string sourceLine in File.ReadLines(path))
            {
                string line = sourceLine.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                string[] pair = line.Split(new char[] {'='}, 2); if (pair.Length != 2) continue;
                var field = typeof(AppSettings).GetField(pair[0].Trim(), BindingFlags.Instance | BindingFlags.NonPublic);
                if (field == null) continue;
                string serialized = pair[1].Trim();
                int integer;
                bool boolean;
                float number;
                if (field.FieldType == typeof(int) && Int32.TryParse(serialized, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer)) field.SetValue(value, integer);
                else if (field.FieldType == typeof(bool) && Boolean.TryParse(serialized, out boolean)) field.SetValue(value, boolean);
                else if (field.FieldType == typeof(float) && Single.TryParse(serialized, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) field.SetValue(value, number);
                else if (field.FieldType == typeof(string)) field.SetValue(value, serialized);
            }
            value.Normalize();
            return value;
        }

        /** <summary>在同一目录写临时文件并原子替换设置；失败时原文件保持可用，不要求管理员权限。</summary> */
        internal void Save(string path)
        {
            Normalize();
            string fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            StringBuilder text = new StringBuilder();
            foreach (var field in typeof(AppSettings).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                object value = field.GetValue(this);
                string serialized = value is float ? ((float)value).ToString("R", CultureInfo.InvariantCulture) : Convert.ToString(value, CultureInfo.InvariantCulture);
                text.AppendLine(field.Name + "=" + serialized);
            }
            string temp = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temp, text.ToString(), new UTF8Encoding(false));
                if (File.Exists(fullPath)) File.Replace(temp, fullPath, null); else File.Move(temp, fullPath);
            }
            finally
            {
                // 仅清理本次保存创建的临时文件；清理失败不覆盖原始保存异常。
                try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        /** <summary>归一化共享设置，允许宿主在应用 UI 或磁盘变更前调用；不读写文件，不改变窗口位置。</summary> */
        internal void Normalize()
        {
            skin = Math.Max(0, Math.Min(SkinCatalog.Count-1, skin)); expression = Math.Max(0, Math.Min(4, expression));
            size = Math.Max(300, Math.Min(900, size)); frameRate = frameRate <= 30 ? 30 : 60;
            screenIndex = Math.Max(-1, screenIndex);
            mouseRange = Clamp(mouseRange, 0.25f, 1.5f, 1); motionSpeed = Clamp(motionSpeed, 5, 40, 18); opacity = Clamp(opacity, 0.35f, 1, 1);
            hotkeySettings = CleanHotkey(hotkeySettings); hotkeyPause = CleanHotkey(hotkeyPause); hotkeyTopMost = CleanHotkey(hotkeyTopMost);
            hotkeyClickThrough = CleanHotkey(hotkeyClickThrough); hotkeyUpdate = CleanHotkey(hotkeyUpdate); hotkeyResetPosition = CleanHotkey(hotkeyResetPosition);
        }

        private static string CleanHotkey(string value) { return value == null ? "" : value.Trim(); }

        private static float Clamp(float value, float min, float max, float fallback)
        {
            if (Single.IsNaN(value) || Single.IsInfinity(value)) return fallback;
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
