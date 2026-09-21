using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace DieYing
{
    internal sealed class LivePose
    {
        internal bool valid;
        internal float yaw, pitch, roll;
        internal int expression = -1;
        internal long tick;
    }

    /** <summary>通过本地 Python/OpenCV 子进程读取摄像头姿态；识别进程失败时不影响桌宠主循环。</summary> */
    internal sealed class LiveTracking : IDisposable
    {
        private readonly object gate = new object();
        private Process process;
        private Thread reader;
        private LivePose latest;
        private bool stopping;
        internal string Status { get; private set; }

        internal LiveTracking() { Status = "未启动"; }

        internal bool Start(string scriptPath)
        {
            Stop();
            if (!File.Exists(scriptPath)) { Status = "缺少 live_tracker.py"; return false; }
            try
            {
                ProcessStartInfo info = new ProcessStartInfo("python", "-u \"" + scriptPath + "\"")
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                    RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(scriptPath)
                };
                process = Process.Start(info);
                stopping = false;
                reader = new Thread(ReadLoop) { IsBackground = true, Name = "DieYing Live Camera" };
                reader.Start();
                Status = "摄像头启动中";
                return true;
            }
            catch (Exception e) { Status = "摄像头启动失败：" + e.Message; Stop(); return false; }
        }

        private void ReadLoop()
        {
            try
            {
                while (!stopping && process != null && !process.HasExited)
                {
                    string line = process.StandardOutput.ReadLine();
                    if (line == null) break;
                    string[] p = line.Split('|');
                    if (p.Length < 5 || p[0] != "POSE") continue;
                    float yaw, pitch, roll;
                    int expression;
                    if (!Single.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out yaw) ||
                        !Single.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out pitch) ||
                        !Single.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out roll) ||
                        !Int32.TryParse(p[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out expression)) continue;
                    lock (gate) latest = new LivePose { valid = true, yaw = yaw, pitch = pitch, roll = roll, expression = expression, tick = Environment.TickCount };
                }
            }
            catch { }
            if (!stopping) Status = "摄像头已断开";
        }

        internal LivePose Snapshot()
        {
            lock (gate)
            {
                if (latest == null || unchecked(Environment.TickCount - latest.tick) > 700) return null;
                return new LivePose { valid = latest.valid, yaw = latest.yaw, pitch = latest.pitch, roll = latest.roll, expression = latest.expression, tick = latest.tick };
            }
        }

        internal void Stop()
        {
            stopping = true;
            if (process != null)
            {
                try { if (!process.HasExited) process.Kill(); } catch { }
                try { process.Dispose(); } catch { }
                process = null;
            }
            latest = null;
            Status = "已关闭";
        }

        public void Dispose() { Stop(); }
    }
}
