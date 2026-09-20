using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DieYing
{
    internal static class Program
    {
        internal static readonly string root = AppDomain.CurrentDomain.BaseDirectory;
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Native.SetProcessDPIAware();
                if (args.Length == 2 && args[0] == "--apply-update") return UpdateService.ApplyUpdate(Int32.Parse(args[1]));
                if (args.Length >= 2)
                {
                    Directory.CreateDirectory(args[1]);
                    if (args[0] == "--update-test") { UpdateAudit.Run(args[1]); return 0; }
                    if (args[0] == "--reaction-preview" && args.Length>=3) { LayeredAudit.Preview(args[1],args[2],true);return 0; }
                    if (args[0] == "--layered-preview" && args.Length>=3) { LayeredAudit.Preview(args[1],args[2]);return 0; }
                    if (args[0] == "--export-icons") { TrayArt.Export(args[1]); return 0; }
                    if (args[0] == "--ui-review") { using (OverlayWindow window = new OverlayWindow(null)) window.ExportMenuPreview(args[1]); return 0; }
                    if (args[0] == "--expression-review") { ExpressionReview(args[1]); return 0; }
                    if (args[0] == "--review") { Review(args[1], false); return 0; }
                    if (args[0] == "--motion-review") { Review(args[1], true); return 0; }
                    if (args[0] == "--self-test") { Check(args[1]); return 0; }
                    if (args[0] == "--integration") { File.WriteAllText(Path.Combine(args[1], "integration.txt"), CheckModule("IntegrationProbe", new object[0]), Encoding.UTF8); return 0; }
                }
                bool created;
                string smoke = args.Length == 2 && args[0] == "--smoke" ? args[1] : null;
                using (Mutex mutex = new Mutex(true, "Local\\DieYing-Development-61C16E" + (smoke == null ? "" : "-Diagnostics"), out created))
                {
                    if (!created) return 0;
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                    using (OverlayWindow window = new OverlayWindow(smoke)) Application.Run(window);
                    mutex.ReleaseMutex();
                }
                return 0;
            }
            catch (Exception e)
            {
                if (args.Length > 0 && args[0] == "--apply-update")
                {
                    File.WriteAllText(Path.Combine(root, "update-error.txt"), e.ToString(), Encoding.UTF8);
                    MessageBox.Show("更新没有完成：" + e.Message + "\n旧版本及设置备份保留在更新目录中。", "蝶应更新");
                    return 1;
                }
                string errorFolder = args.Length >= 2 ? args[1] : Path.Combine(root, "data");
                Directory.CreateDirectory(errorFolder); File.WriteAllText(Path.Combine(errorFolder, "last-error.txt"), e.ToString(), Encoding.UTF8);
                if (args.Length == 0) MessageBox.Show("蝶应未能启动：" + e.Message, "蝶应");
                return 1;
            }
        }

        private static void Check(string folder)
        {
            var report = new StringBuilder();
            report.AppendLine(InputSource.RunSelfTests()); report.AppendLine(KeyboardModel.RunSelfTests());
            report.AppendLine(ReactionAudit.Run());
            using (SceneRenderer scene = new SceneRenderer(Path.Combine(root, "assets")))
            {
                report.AppendLine(CheckModule("ModelAudit", new object[] { scene }));
                report.AppendLine(DynamicsAudit.Run(scene));
                report.AppendLine(LayeredAudit.Run(scene,folder));
                report.AppendLine(ArmAudit.Run(scene.keyboard));
            }
            File.WriteAllText(Path.Combine(folder, "checks.txt"), report.ToString(), Encoding.UTF8);
            if (report.ToString().IndexOf("FAIL", StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException("自动检查出现失败，详见 checks.txt。");
        }
        private static string CheckModule(string name, object[] parameters)
        {
            Type module = typeof(Program).Assembly.GetType("DieYing." + name);
            if (module == null) throw new InvalidOperationException("验证模块尚未就绪：" + name);
            return (string)module.GetMethod("Run", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public).Invoke(null, parameters);
        }

        private static void Review(string folder, bool animation)
        {
            using (SceneRenderer scene = new SceneRenderer(Path.Combine(root, "assets")))
            {
                if (animation) { MotionReview(folder, scene); return; }
                int[] keys = { 65, 87, 32, 81, 162, 13, 27, 68 };
                PointF[] corners = { new PointF(-1, -1), new PointF(1, -1), new PointF(-1, 1), new PointF(1, 1) };
                for (int skin = 0; skin < 2; ++skin)
                {
                    AppSettings settings = new AppSettings { skin = skin, idleMotion = false };
                    using (Bitmap contact = new Bitmap(1600, 1440))
                    using (Graphics gallery = Graphics.FromImage(contact))
                    using (Font font = new Font("Microsoft YaHei UI", 13, FontStyle.Bold))
                    {
                        gallery.Clear(Color.FromArgb(243, 242, 238));
                        for (int index = 0; index < keys.Length; ++index)
                        {
                            MotionState pose = new MotionState(); pose.SetReviewPose(scene.keyboard, keys[index], corners[index % 4], true, index);
                            using (Bitmap image = new Bitmap(800, 720, PixelFormat.Format32bppArgb))
                            {
                                using (Graphics g = Graphics.FromImage(image)) scene.Draw(g, settings, pose);
                                image.Save(Path.Combine(folder, (skin == 0 ? "classic" : "butterfly") + "-key-" + keys[index] + ".png"), ImageFormat.Png);
                                int x = index % 4 * 400, y = index / 4 * 720;
                                gallery.DrawImage(image, new Rectangle(x, y + 22, 400, 360));
                                gallery.DrawString("键位 " + scene.keyboard.Find(keys[index]).label, font, Brushes.DimGray, x + 12, y + 7);
                                scene.debugRig = true;
                                using (Graphics g = Graphics.FromImage(image)) { g.Clear(Color.Transparent); scene.Draw(g, settings, pose); }
                                gallery.DrawImage(image, new Rectangle(x, y + 380, 400, 360)); scene.debugRig = false;
                            }
                        }
                        contact.Save(Path.Combine(folder, (skin == 0 ? "classic" : "butterfly") + "-contact.png"), ImageFormat.Png);
                    }
                }
                using (Bitmap cover = new Bitmap(1600, 800))
                using (Graphics g = Graphics.FromImage(cover))
                {
                    g.Clear(Color.FromArgb(251, 247, 235));
                    for (int skin = 0; skin < 2; ++skin)
                    {
                        var state = g.Save(); g.TranslateTransform(skin * 800, 50);
                        var pose = new MotionState(); pose.SetReviewPose(scene.keyboard, 87, new PointF(0, 0), false, 0);
                        scene.Draw(g, new AppSettings { skin = skin }, pose); g.Restore(state);
                    }
                    cover.Save(Path.Combine(folder, "assembled.png"), ImageFormat.Png);
                }
            }
        }

        private static void MotionReview(string folder, SceneRenderer scene)
        {
            int[] keys = { 65, 87, 32, 81, 162, 269, 27, 68, 70, 13, 162, 161 };
            var stopwatch = Stopwatch.StartNew();
            for (int skin = 0; skin < 2; ++skin)
            {
                var settings = new AppSettings { skin = skin }; var pose = new MotionState();
                int previous = -1;
                for (int frameIndex = 0; frameIndex < 360; ++frameIndex)
                {
                    double t = frameIndex / 30.0; int index = frameIndex / 30; int key = keys[index];
                    settings.expression = t < 5 ? 0 : t < 7 ? 2 : t < 9 ? 3 : t < 11 ? 4 : 0;
                    bool down = frameIndex % 30 < 20;
                    var frame = new InputFrame {
                        heldKeys = new bool[512], targetKey = key, lastPressedKey = key, hasKeyboardTarget = down,
                        keySequence = index + 1, pressedSinceSnapshot = previous != index ? new int[] { key } : new int[0],
                        cursorX = (float)Math.Sin(t * 1.1), cursorY = (float)Math.Cos(t * .7),
                        leftButton = frameIndex % 55 < 7, rightButton = frameIndex % 71 < 5,
                        wheelDelta = frameIndex % 80 == 0 ? 120 : 0, bubble = scene.keyboard.Find(key).label
                    };
                    frame.heldKeys[key] = down; pose.Update(frame, settings, scene.keyboard, t);
                    using (Bitmap bitmap = new Bitmap(800, 720, PixelFormat.Format32bppArgb))
                    {
                        using (Graphics g = Graphics.FromImage(bitmap)) { g.Clear(Color.FromArgb(250, 247, 238)); scene.Draw(g, settings, pose); }
                        bitmap.Save(Path.Combine(folder, "skin" + skin + "-" + frameIndex.ToString("D4") + ".png"), ImageFormat.Png);
                    }
                    previous = index;
                }
            }
            File.WriteAllText(Path.Combine(folder, "render-time.txt"), "720 frames rendered in " + stopwatch.Elapsed.TotalSeconds.ToString("F2") + " seconds\r\nProgrammed input and expression replay for visual review; not hardware evidence.", Encoding.UTF8);
        }

        private static void ExpressionReview(string folder)
        {
            using (SceneRenderer scene = new SceneRenderer(Path.Combine(root,"assets")))
            using (Bitmap sheet = new Bitmap(2000,780))
            using (Graphics gallery = Graphics.FromImage(sheet))
            using (Font font = new Font("Microsoft YaHei UI",14,FontStyle.Bold))
            {
                gallery.Clear(Color.FromArgb(250,247,238));
                string[] names = {"自然","闭眼","开心","惊讶","晕乎乎"};
                for(int skin=0;skin<2;skin++) for(int mood=0;mood<5;mood++)
                {
                    MotionState pose=new MotionState(); pose.SetReviewPose(scene.keyboard,65,PointF.Empty,false,0);
                    using(Bitmap b=new Bitmap(800,720))
                    {
                        using(Graphics g=Graphics.FromImage(b)) scene.Draw(g,new AppSettings {skin=skin,expression=mood},pose);
                        gallery.DrawImage(b,new Rectangle(mood*400,skin*390+25,400,360));
                    }
                    gallery.DrawString(names[mood],font,Brushes.DimGray,mood*400+14,skin*390+5);
                }
                sheet.Save(Path.Combine(folder,"expressions.png"),ImageFormat.Png);
            }
        }
    }
}
