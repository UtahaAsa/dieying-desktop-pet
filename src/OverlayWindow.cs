using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Collections.Generic;

namespace DieYing
{
    /** <summary>透明桌宠宿主。渲染、输入和设置均在 UI 线程运行，不激活挂件窗口。</summary> */
    internal sealed class OverlayWindow : Form
    {
        private readonly string settingsPath, smokeFolder;
        private readonly AppSettings settings;
        private readonly Stopwatch clock = new Stopwatch();
        private readonly Timer trayClickTimer = new Timer();
        private FrameClock frameTimer;
        private SceneRenderer renderer;
        private MotionState motion = new MotionState();
        private InputSource input;
        private Bitmap sceneBitmap;
        private Icon applicationIcon;
        private NotifyIcon tray;
        private ContextMenuStrip menu;
        private MenuDismissGuard menuGuard;
        private ToolStripMenuItem startupItem;
        private ToolStripMenuItem pauseItem, throughItem, skinItem, topItem, labelsItem;
        private readonly ToolStripMenuItem[] skins = new ToolStripMenuItem[2], expressions = new ToolStripMenuItem[5];
        private ToolStripLabel menuHeader;
        private int iconSkin = -1;
        private SettingsWindow settingsWindow;
        private bool settingsOpenPending;
        private bool checkingUpdate;
        private ToolStripMenuItem updateItem, autoUpdateItem;
        private bool initialized, disposed, painting, dragging, applying, lastPaused, lastKeyboard, lastMouse;
        private Point dragCursor, dragOrigin;
        private int frames, smokePhase = -1, smokeStartHandles;
        private uint smokeStartGdi, smokeStartUser, smokePeakGdi, smokePeakUser;
        private long smokeStartBytes;
        private bool smokeBaseline, smokeHadFocus, smokeWasVisible;
        private int lastTrayDoubleClick = Int32.MinValue;
        private double lastFrameTime;
        private readonly List<double> frameIntervals = new List<double>(), renderTimes = new List<double>();
        private double sceneMs, scaleMs, presentMs;

        /** <summary>创建桌宠。smokeFolder 非空时执行不保存用户设置、最长约 8.5 秒的透明窗口测试。</summary> */
        internal OverlayWindow(string smokeFolder)
        {
            this.smokeFolder = smokeFolder;
            settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "settings.ini");
            settings = AppSettings.Load(settingsPath);
            if (!String.IsNullOrEmpty(smokeFolder)) { settings.paused = false; settings.keyboardEnabled = true; settings.mouseEnabled = true; }
            Text = "蝶应";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = false;
            SetStyle(ControlStyles.Selectable, false);
            try
            {
                renderer = new SceneRenderer(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets"));
                input = new InputSource();
                if (!input.Status.StartsWith("已连接", StringComparison.Ordinal)) throw new InvalidOperationException(input.Status);
                applicationIcon = TrayArt.MakeIcon(settings.skin); iconSkin = settings.skin;
                Icon = applicationIcon;
                BuildMenu();
                tray = new NotifyIcon { Text = "蝶应", Icon = applicationIcon, Visible = true, ContextMenuStrip = menu };
                tray.MouseClick += TrayMouseClick;
                tray.MouseDoubleClick += delegate(object sender, MouseEventArgs args)
                {
                    if (args.Button == MouseButtons.Left) { lastTrayDoubleClick = Environment.TickCount; trayClickTimer.Stop(); SwitchSkin(); }
                };
                trayClickTimer.Interval = SystemInformation.DoubleClickTime;
                trayClickTimer.Tick += delegate { trayClickTimer.Stop(); OpenSettings(); };
                frameTimer = new FrameClock(this,delegate { OnFrame(this,EventArgs.Empty); });
                lastPaused = settings.paused; lastKeyboard = settings.keyboardEnabled; lastMouse = settings.mouseEnabled;
                SetDisplaySize(false);
                if (settings.posX == Int32.MinValue || settings.posY == Int32.MinValue) ResetPositionCore();
                else Location = ClampLocation(new Point(settings.posX, settings.posY), Size);
                initialized = true;
            }
            catch { Dispose(); throw; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams value = base.CreateParams;
                value.ExStyle |= Native.ExLayered | Native.ExToolWindow | Native.ExNoActivate;
                if (settings != null && settings.clickThrough) value.ExStyle |= Native.ExTransparent;
                return value;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            clock.Start();
            ApplySettings(false);
            RenderFrame();
            frameTimer.Start();
            if (String.IsNullOrEmpty(smokeFolder) && settings.autoCheckUpdates) CheckForUpdates(false);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0021) { message.Result = new IntPtr(3); return; } // MA_NOACTIVATE
            if (message.Msg == 0x0084 && settings != null && settings.clickThrough) { message.Result = new IntPtr(-1); return; }
            base.WndProc(ref message);
            if (message.Msg == 0x007E && initialized) // 显示器增减、分辨率或布局变化。
            {
                Location = ClampLocation(Location, Size);
                if (settingsWindow != null && !settingsWindow.IsDisposed) settingsWindow.RefreshValues();
                SaveSettings();
            }
        }

        private void BuildMenu()
        {
            menu = new ContextMenuStrip { Renderer = new ToolStripProfessionalRenderer(new PetMenuColors()) { RoundedEdges = true },
                Font = new Font("Microsoft YaHei UI", 9.5f), ForeColor = Color.FromArgb(87, 66, 54), Padding = new Padding(5, 4, 5, 5), ImageScalingSize = new Size(24, 24) };
            menuHeader = new ToolStripLabel("蝶应", TrayArt.Portrait(settings.skin, 48)) { ImageScaling = ToolStripItemImageScaling.None,
                TextAlign = ContentAlignment.MiddleLeft, ImageAlign = ContentAlignment.MiddleLeft,
                TextImageRelation = TextImageRelation.ImageBeforeText, AutoSize = false, Size = new Size(248, 70), Padding = new Padding(7, 5, 7, 5) };
            menu.Items.Add(menuHeader);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("打开设置…", null, delegate { OpenSettingsFromMenu(); });
            skinItem = new ToolStripMenuItem("衣装");
            string[] names = { "常服", "打歌服" };
            for (int i = 0; i < 2; i++)
            {
                int skin = i;
                skins[i] = new ToolStripMenuItem(names[i], TrayArt.Portrait(i, 24), delegate { settings.skin = skin; ApplySettings(true); });
                skinItem.DropDownItems.Add(skins[i]);
            }
            menu.Items.Add(skinItem);
            ToolStripMenuItem expressionItem = new ToolStripMenuItem("表情");
            string[] moods = { "自然", "闭眼", "开心", "惊讶", "晕乎乎" };
            for (int i = 0; i < moods.Length; i++)
            {
                int value = i;
                expressions[i] = new ToolStripMenuItem(moods[i], null, delegate { settings.expression = value; ApplySettings(true); });
                expressionItem.DropDownItems.Add(expressions[i]);
            }
            menu.Items.Add(expressionItem);
            menu.Items.Add(new ToolStripSeparator());
            pauseItem = new ToolStripMenuItem("暂停动作", null, delegate { settings.paused = !settings.paused; ApplySettings(true); });
            menu.Items.Add(pauseItem);
            throughItem = new ToolStripMenuItem("鼠标穿透", null, delegate { settings.clickThrough = !settings.clickThrough; ApplySettings(true); });
            menu.Items.Add(throughItem);
            topItem = new ToolStripMenuItem("保持置顶", null, delegate { settings.topMost = !settings.topMost; ApplySettings(true); });
            menu.Items.Add(topItem);
            labelsItem = new ToolStripMenuItem("显示键帽文字", null, delegate { settings.keyLabels = !settings.keyLabels; ApplySettings(true); });
            menu.Items.Add(labelsItem);
            ToolStripMenuItem sizeItem = new ToolStripMenuItem("桌宠大小");
            foreach (int size in new int[] { 360, 480, 640 })
            {
                int value = size;
                sizeItem.DropDownItems.Add(size + " px", null, delegate { settings.size = value; ApplySettings(true); });
            }
            menu.Items.Add(sizeItem);
            startupItem=new ToolStripMenuItem("开机自启",null,delegate
            {
                StartupRegistration.SetEnabled(!StartupRegistration.Enabled);
                RefreshMenu();
                if(settingsWindow!=null && !settingsWindow.IsDisposed)settingsWindow.RefreshValues();
            });
            menu.Items.Add(startupItem);
            menu.Items.Add("恢复位置", null, delegate { ResetPositionCore(); ApplySettings(true); });
            updateItem = new ToolStripMenuItem("检查更新…", null, delegate { BeginInvoke((MethodInvoker)delegate { CheckForUpdates(true); }); });
            menu.Items.Add(updateItem);
            autoUpdateItem = new ToolStripMenuItem("自动检查更新", null, delegate { settings.autoCheckUpdates = !settings.autoCheckUpdates; SaveSettings(); RefreshMenu(); });
            menu.Items.Add(autoUpdateItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出蝶应", null, delegate { Close(); });
            foreach (ToolStripItem item in menu.Items) if (item is ToolStripMenuItem) item.Padding = new Padding(4, 5, 10, 5);
            menu.Opening += delegate { RefreshMenu(); };
            menuGuard=new MenuDismissGuard(menu,this);
        }

        private void RefreshMenu()
        {
            pauseItem.Checked = settings.paused;
            startupItem.Checked=StartupRegistration.Enabled;
            autoUpdateItem.Checked = settings.autoCheckUpdates;
            pauseItem.Text = settings.paused ? "继续陪伴" : "暂停动作";
            throughItem.Checked = settings.clickThrough;
            topItem.Checked = settings.topMost; labelsItem.Checked = settings.keyLabels;
            for (int i = 0; i < skins.Length; i++) skins[i].Checked = settings.skin == i;
            for (int i = 0; i < expressions.Length; i++) expressions[i].Checked = settings.expression == i;
            skinItem.Text = "衣装 · " + (settings.skin == 0 ? "常服" : "打歌服");
            if (tray != null) tray.Text = "蝶应 · " + (settings.skin == 0 ? "常服" : "打歌服") + (settings.paused ? " · 已暂停" : "");
            if (iconSkin != settings.skin)
            {
                Icon old = applicationIcon; applicationIcon = TrayArt.MakeIcon(settings.skin); iconSkin = settings.skin;
                Icon = applicationIcon; if (tray != null) tray.Icon = applicationIcon;
                if (settingsWindow != null && !settingsWindow.IsDisposed) settingsWindow.Icon = applicationIcon;
                if (old != null) old.Dispose();
                if (menuHeader != null) { Image previous = menuHeader.Image; menuHeader.Image = TrayArt.Portrait(settings.skin,48); if(previous != null) previous.Dispose(); }
            }
            if (menuHeader != null) menuHeader.Text = "蝶应\n" + (settings.skin == 0 ? "常服" : "打歌服") + " · " + (settings.paused ? "休息中" : "陪伴中");
        }

        private void TrayMouseClick(object sender, MouseEventArgs args)
        {
            if (args.Button == MouseButtons.Left && unchecked((uint)(Environment.TickCount - lastTrayDoubleClick)) > (uint)SystemInformation.DoubleClickTime)
            { trayClickTimer.Stop(); trayClickTimer.Start(); }
        }

        private void SwitchSkin() { settings.skin = 1 - settings.skin; ApplySettings(true); }

        private async void CheckForUpdates(bool manual)
        {
            if (checkingUpdate || disposed) return;
            checkingUpdate = true;
            updateItem.Enabled = false; updateItem.Text = "正在检查更新…";
            try
            {
                ReleaseInfo release = await UpdateService.CheckAsync();
                if (disposed) return;
                if (release == null)
                {
                    if (manual) MessageBox.Show("当前已是最新版本 " + UpdateService.VersionText, "蝶应更新");
                    return;
                }
                if (!manual)
                {
                    updateItem.Text = "发现新版 " + release.version + "，点击更新…";
                    tray.ShowBalloonTip(6000, "蝶应有新版本", "右键菜单中点击“检查更新”即可下载安装。", ToolTipIcon.Info);
                    return;
                }
                if (MessageBox.Show("发现新版 " + release.version + "。\n下载安装后会重启蝶应，原来的设置会保留。", "蝶应更新", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                updateItem.Text = "正在下载安装包…";
                string stage = await UpdateService.DownloadAsync(release);
                if (disposed) return;
                await System.Threading.Tasks.Task.Run(delegate { UpdateService.LaunchInstaller(stage); });
                Close();
            }
            catch (Exception error)
            {
                if (!disposed && manual) MessageBox.Show("更新未完成：" + error.Message + "\n当前版本仍可使用。", "蝶应更新");
            }
            finally
            {
                checkingUpdate = false;
                if (!disposed) { updateItem.Enabled = true; if (!updateItem.Text.StartsWith("发现新版")) updateItem.Text = "检查更新…"; }
            }
        }

        private void OpenSettingsFromMenu()
        {
            if (disposed || Disposing || settingsOpenPending) return;
            trayClickTimer.Stop();
            settingsOpenPending = true;
            try
            {
                // 菜单关闭会恢复旧前台窗口；先完成它，再由下一次消息循环激活设置。
                // 不在菜单的鼠标释放处理栈里创建窗口，避免后续焦点回收盖住设置。
                menu.Close(ToolStripDropDownCloseReason.ItemClicked);
                if (!IsHandleCreated) CreateControl();
                BeginInvoke((MethodInvoker)delegate
                {
                    settingsOpenPending = false;
                    if (!disposed && !Disposing) OpenSettings();
                });
            }
            catch { settingsOpenPending = false; throw; }
        }

        /** <summary>打开可正常激活的设置窗口；本方法由菜单、托盘或集成验证入口调用。</summary> */
        internal void OpenSettings()
        {
            if (disposed) return;
            if (settingsWindow == null || settingsWindow.IsDisposed)
            {
                settingsWindow = new SettingsWindow(settings, delegate { ApplySettings(true); }, ResetPositionCore, CreatePreview);
                settingsWindow.Icon = applicationIcon;
                settingsWindow.TopMost = settings.topMost;
                settingsWindow.FormClosed += delegate { settingsWindow = null; };
                settingsWindow.Show();
            }
            else
            {
                settingsWindow.RefreshValues();
                if (settingsWindow.WindowState == FormWindowState.Minimized) settingsWindow.WindowState = FormWindowState.Normal;
                settingsWindow.Show();
            }
            settingsWindow.BringToFront();
            settingsWindow.Activate();
            Native.SetForegroundWindow(settingsWindow.Handle);
        }

        private Bitmap CreatePreview(int skin)
        {
            Bitmap preview = new Bitmap(800, 720, PixelFormat.Format32bppPArgb);
            AppSettings previewSettings = new AppSettings { skin = skin, expression = settings.expression, mirror = settings.mirror, idleMotion = false, showKeyBubble = false, keyLabels = settings.keyLabels };
            MotionState previewMotion = new MotionState();
            int previewKey = renderer.keyboard.Find(74) != null ? 74 : renderer.keyboard.Keys[0].id;
            previewMotion.SetReviewPose(renderer.keyboard, previewKey, PointF.Empty, false, 0);
            previewMotion.blink = settings.expression == 1;
            try { using (Graphics graphics = Graphics.FromImage(preview)) { graphics.Clear(Color.Transparent); renderer.Draw(graphics, previewSettings, previewMotion); } }
            catch { preview.Dispose(); throw; }
            return preview;
        }

        private void ApplySettings(bool save)
        {
            if (!initialized || disposed || applying) return;
            applying = true;
            try
            {
                settings.Normalize();
                if (lastPaused != settings.paused || lastKeyboard != settings.keyboardEnabled || lastMouse != settings.mouseEnabled)
                {
                    input.Clear();
                    Array.Clear(motion.litKeys, 0, motion.litKeys.Length);
                    motion.keyPress = motion.mousePress = motion.wheel = 0;
                    motion.leftButton = motion.rightButton = motion.middleButton = false;
                    motion.bubble = String.Empty;
                    lastPaused = settings.paused; lastKeyboard = settings.keyboardEnabled; lastMouse = settings.mouseEnabled;
                }
                SetDisplaySize(true);
                Location = ClampLocation(Location, Size);
                frameTimer.Rate = settings.frameRate;
                if (IsHandleCreated)
                {
                    Native.SetClickThrough(Handle, settings.clickThrough);
                    Native.SetTopMost(Handle, settings.topMost);
                }
                if (settingsWindow != null && !settingsWindow.IsDisposed)
                {
                    settingsWindow.TopMost = settings.topMost;
                    settingsWindow.RefreshValues();
                }
                RefreshMenu();
                if (save) SaveSettings();
                if (Visible && !painting) RenderFrame();
            }
            finally { applying = false; }
        }

        private void SetDisplaySize(bool preserveBottomRight)
        {
            int width = Math.Max(300, Math.Min(900, settings.size));
            int height = (int)Math.Round(width * 720.0 / 800.0);
            if (sceneBitmap != null && sceneBitmap.Width == width && sceneBitmap.Height == height) return;
            Point anchor = new Point(Right, Bottom);
            Bitmap next = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            if(sceneBitmap!=null)sceneBitmap.Dispose();
            sceneBitmap=next;
            Size = new Size(width, height);
            if (preserveBottomRight) Location = new Point(anchor.X - width, anchor.Y - height);
        }

        private Rectangle MappingBounds()
        {
            Screen[] screens = Screen.AllScreens;
            if (settings.screenIndex < 0 || screens.Length == 0) return SystemInformation.VirtualScreen;
            return screens[Math.Min(settings.screenIndex, screens.Length - 1)].Bounds;
        }

        private void OnFrame(object sender, EventArgs args)
        {
            if (painting || disposed) return;
            if (!String.IsNullOrEmpty(smokeFolder) && (frames >= 240 || clock.Elapsed.TotalSeconds >= 8.5))
            {
                WriteSmokeReport();
                Close();
                return;
            }
            RenderFrame();
        }

        private void RenderFrame()
        {
            if (painting || disposed || !IsHandleCreated) return;
            painting = true;
            try
            {
                double now = clock.Elapsed.TotalSeconds;
                if(!String.IsNullOrEmpty(smokeFolder) && frames>10)frameIntervals.Add((now-lastFrameTime)*1000);
                lastFrameTime = now;
                InputFrame frame = input.Snapshot(MappingBounds(), now, settings.keyboardEnabled && !settings.paused, settings.mouseEnabled && !settings.paused);
                if (!String.IsNullOrEmpty(smokeFolder)) frame = SmokeFrame(now);
                motion.Update(frame, settings, renderer.keyboard, now);
                if (motion.reaction.returnedToNatural)
                {
                    RefreshMenu();
                    if (settingsWindow != null && !settingsWindow.IsDisposed) settingsWindow.RefreshValues();
                }
                // 暂停期间仍消费输入快照；视觉上不保留任何“持续按住”的状态。
                if (settings.paused)
                {
                    Array.Clear(motion.litKeys, 0, motion.litKeys.Length);
                    motion.keyPress = motion.mousePress = motion.wheel = 0;
                    motion.leftButton = motion.rightButton = motion.middleButton = false;
                    motion.bubble = String.Empty;
                    motion.blink = settings.expression == 1;
                    motion.blinkAmount = settings.expression == 1 ? 1 : 0;
                }
                double stageStart = clock.Elapsed.TotalSeconds;
                using (Graphics graphics = Graphics.FromImage(sceneBitmap))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.ScaleTransform(sceneBitmap.Width/800f,sceneBitmap.Width/800f);
                    renderer.Draw(graphics, settings, motion);
                }
                double stageNow = clock.Elapsed.TotalSeconds; sceneMs += (stageNow-stageStart)*1000;stageStart=stageNow;
                // 场景直接按窗口像素绘制，无需整幅二次放大。
                stageNow = clock.Elapsed.TotalSeconds;scaleMs+=(stageNow-stageStart)*1000;stageStart=stageNow;
                byte alpha = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(settings.opacity * 255)));
                Native.Present(Handle, sceneBitmap, Location, alpha);
                presentMs+=(clock.Elapsed.TotalSeconds-stageStart)*1000;
                frames++;
                if(!String.IsNullOrEmpty(smokeFolder) && frames>10)renderTimes.Add((clock.Elapsed.TotalSeconds-now)*1000);
                if (!String.IsNullOrEmpty(smokeFolder)) SampleSmokeResources();
            }
            finally { painting = false; }
        }

        private InputFrame SmokeFrame(double now)
        {
            int phase = (int)(now / .42);
            var keys = renderer.keyboard.Keys;
            int key = keys[phase % keys.Count].id;
            settings.skin = (int)(now / 1.65) % 2;
            bool held = now % .42 < .29;
            InputFrame frame = new InputFrame
            {
                targetKey = key, lastPressedKey = key, keySequence = phase + 1, hasKeyboardTarget = held,
                cursorX = (float)Math.Sin(now * 2.3), cursorY = (float)Math.Cos(now * 1.7),
                leftButton = phase % 3 == 0 && held, rightButton = phase % 3 == 1 && held,
                middleButton = phase % 3 == 2 && held,
                wheelDelta = phase != smokePhase && phase % 4 == 0 ? 120 : 0,
                bubble = held ? "窗口验证 · " + (phase + 1) : "", lastActivity = now
            };
            frame.heldKeys[key] = held;
            frame.pressedSinceSnapshot = phase == smokePhase ? new int[0] : new int[] { key };
            smokePhase = phase;
            return frame;
        }

        private void SampleSmokeResources()
        {
            using (Process process = Process.GetCurrentProcess())
            {
                uint gdi = Native.GetGuiResources(process.Handle, 0), user = Native.GetGuiResources(process.Handle, 1);
                smokePeakGdi = Math.Max(smokePeakGdi, gdi); smokePeakUser = Math.Max(smokePeakUser, user);
                if (!smokeBaseline && frames >= 20)
                {
                    smokeStartGdi = gdi; smokeStartUser = user; smokeStartHandles = process.HandleCount;
                    smokeStartBytes = process.PrivateMemorySize64; smokeBaseline = true;
                }
            }
            smokeHadFocus |= Native.GetForegroundWindow() == Handle;
            smokeWasVisible |= Native.IsWindowVisible(Handle);
        }

        private void WriteSmokeReport()
        {
            frameTimer.Stop();
            StringBuilder report = new StringBuilder();
            using (Process process = Process.GetCurrentProcess())
            {
                uint gdi = Native.GetGuiResources(process.Handle, 0), user = Native.GetGuiResources(process.Handle, 1);
                long gdiGrowth = (long)gdi - smokeStartGdi, userGrowth = (long)user - smokeStartUser;
                report.AppendLine("蝶应 · 透明窗口集成冒烟验证");
                report.AppendLine("UTC=" + DateTime.UtcNow.ToString("o"));
                report.AppendLine("Frames=" + frames + "; seconds=" + clock.Elapsed.TotalSeconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
                report.AppendLine("Renderer="+renderer.characters[0].RendererDevice);
                report.AppendLine("Frame interval ms median="+Percentile(frameIntervals,.5).ToString("F2")+"; p95="+Percentile(frameIntervals,.95).ToString("F2"));
                report.AppendLine("Render ms median="+Percentile(renderTimes,.5).ToString("F2")+"; p95="+Percentile(renderTimes,.95).ToString("F2"));
                report.AppendLine("Stage mean ms scene="+(sceneMs/frames).ToString("F2")+"; scale="+(scaleMs/frames).ToString("F2")+"; present="+(presentMs/frames).ToString("F2"));
                report.AppendLine("InputSource=" + input.Status);
                report.AppendLine("Visible=" + smokeWasVisible + "; overlayAcquiredForeground=" + smokeHadFocus);
                report.AppendLine("GDI baseline=" + smokeStartGdi + "; final=" + gdi + "; peak=" + smokePeakGdi + "; growth=" + gdiGrowth);
                report.AppendLine("USER baseline=" + smokeStartUser + "; final=" + user + "; peak=" + smokePeakUser + "; growth=" + userGrowth);
                int handleGrowth = process.HandleCount - smokeStartHandles;
                long privateByteGrowth = process.PrivateMemorySize64 - smokeStartBytes;
                report.AppendLine("Process handles baseline=" + smokeStartHandles + "; final=" + process.HandleCount + "; growth=" + handleGrowth);
                report.AppendLine("Private bytes baseline=" + smokeStartBytes + "; final=" + process.PrivateMemorySize64 + "; growth=" + privateByteGrowth);
                bool passed = smokeBaseline && frames >= 30 && smokeWasVisible && !smokeHadFocus && gdiGrowth <= 20 && userGrowth <= 8 && handleGrowth <= 16 && privateByteGrowth <= 128 * 1024 * 1024;
                report.AppendLine("WindowResourceCheck=" + (passed ? "PASS" : "FAIL"));
                report.AppendLine("已真实创建分层透明窗口、注册后台输入并连续呈现两套衣装；测试动作由内部 InputFrame 构造。");
                report.AppendLine("未向 Windows 注入键鼠输入，未保存或更改用户设置；本测试不证明真实输入动作集成或动画视觉质量已达标。");
            }
            Directory.CreateDirectory(smokeFolder);
            File.WriteAllText(Path.Combine(smokeFolder, "smoke.txt"), report.ToString(), new UTF8Encoding(false));
        }

        private static double Percentile(List<double> values,double fraction)
        {
            if(values.Count==0)return 0;
            double[] copy=values.ToArray();Array.Sort(copy);
            return copy[Math.Min(copy.Length-1,(int)(copy.Length*fraction))];
        }

        private void ResetPositionCore()
        {
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = ClampLocation(new Point(area.Right - Width - 24, area.Bottom - Height - 20), Size);
        }

        private static Point ClampLocation(Point location, Size size)
        {
            Rectangle bounds = new Rectangle(location, size);
            Rectangle area = Screen.FromRectangle(bounds).WorkingArea;
            return new Point(Math.Max(area.Left, Math.Min(location.X, Math.Max(area.Left, area.Right - size.Width))),
                Math.Max(area.Top, Math.Min(location.Y, Math.Max(area.Top, area.Bottom - size.Height))));
        }

        private void SaveSettings()
        {
            if (!String.IsNullOrEmpty(smokeFolder) || !initialized || disposed) return;
            settings.posX = Left; settings.posY = Top;
            settings.Save(settingsPath);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (settings.clickThrough) return;
            if (e.Button == MouseButtons.Left)
            {
                dragging = true; dragCursor = Cursor.Position; dragOrigin = Location; Capture = true;
            }
            else if (e.Button == MouseButtons.Right) menu.Show(this,PointToClient(Cursor.Position));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging || settings.clickThrough) return;
            Point cursor = Cursor.Position;
            Location = ClampLocation(new Point(dragOrigin.X + cursor.X - dragCursor.X, dragOrigin.Y + cursor.Y - dragCursor.Y), Size);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && dragging) { dragging = false; Capture = false; SaveSettings(); }
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (dragging && !Capture) { dragging = false; SaveSettings(); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (settings.clickThrough) return;
            settings.size = Math.Max(300, Math.Min(900, settings.size + Math.Sign(e.Delta) * 24));
            ApplySettings(true);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            frameTimer.Stop(); trayClickTimer.Stop();
            SaveSettings();
            base.OnFormClosing(e);
        }

        private static Icon LoadApplicationIcon()
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            string[] files = { Path.Combine(root, "app.ico"), Path.Combine(root, "assets", "app.ico") };
            foreach (string file in files) if (File.Exists(file)) return new Icon(file);
            return (Icon)SystemIcons.Application.Clone();
        }

        /** <summary>导出菜单与小尺寸图标预览；不打开桌宠主窗、不保存用户设置。</summary> */
        internal void ExportMenuPreview(string folder)
        {
            RefreshMenu(); menu.CreateControl(); menu.PerformLayout();
            menu.Size = menu.GetPreferredSize(Size.Empty);
            using (Bitmap bitmap = new Bitmap(menu.Width, menu.Height))
            {
                menu.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                bitmap.Save(Path.Combine(folder,"tray-menu.png"),ImageFormat.Png);
            }
            using(Bitmap gallery=new Bitmap(640,220))
            using(Graphics g=Graphics.FromImage(gallery))
            using(Font font=new Font("Segoe UI",9))
            {
                g.Clear(Color.FromArgb(242,242,242));
                for(int skin=0;skin<2;skin++)
                {
                    int x=20;
                    foreach(int size in new int[]{16,20,24,32,48,64})
                    {
                        using(Bitmap p=TrayArt.Portrait(skin,size)) g.DrawImageUnscaled(p,x,skin*105+12);
                        g.DrawString(size+"px",font,Brushes.DimGray,x,skin*105+80); x+=100;
                    }
                }
                gallery.Save(Path.Combine(folder,"tray-icons.png"),ImageFormat.Png);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true;
                if(frameTimer!=null){frameTimer.Stop();frameTimer.Dispose();}
                trayClickTimer.Stop(); trayClickTimer.Dispose();
                if (settingsWindow != null && !settingsWindow.IsDisposed) settingsWindow.Close();
                if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
                if(menuGuard!=null){menuGuard.Dispose();menuGuard=null;}
                if (menu != null) { foreach (ToolStripMenuItem item in skins) if (item != null && item.Image != null) item.Image.Dispose(); if(menuHeader != null && menuHeader.Image != null) menuHeader.Image.Dispose(); Font font = menu.Font; menu.Dispose(); font.Dispose(); menu = null; }
                if (input != null) { input.Dispose(); input = null; }
                if (renderer != null) { renderer.Dispose(); renderer = null; }
                if (sceneBitmap != null) { sceneBitmap.Dispose(); sceneBitmap = null; }
                if (applicationIcon != null) { applicationIcon.Dispose(); applicationIcon = null; }
            }
            base.Dispose(disposing);
        }
    }
}
