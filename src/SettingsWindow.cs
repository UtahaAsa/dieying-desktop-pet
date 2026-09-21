using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DieYing
{
    /// <summary>蝶鼠桌宠设置窗口；设置变更在 UI 线程同步通知宿主，预览图片由本窗口管理。</summary>
    internal sealed class SettingsWindow : Form
    {
        private static readonly Color Ink = Color.FromArgb(66, 49, 43);
        private static readonly Color Muted = Color.FromArgb(128, 113, 98);
        private static readonly Color Cream = Color.FromArgb(250, 247, 239);
        private static readonly Color Gold = Color.FromArgb(234, 193, 83);
        private static readonly Color Mint = Color.FromArgb(216, 238, 226);
        private static readonly Color Line = Color.FromArgb(231, 225, 211);
        private readonly AppSettings settings;
        private readonly Action changed;
        private readonly Action resetPosition;
        private readonly Action checkForUpdates;
        private readonly Func<int, Bitmap> previewFactory;
        private readonly List<Action> refreshers = new List<Action>();
        private readonly List<Font> ownedFonts = new List<Font>();
        private readonly List<Bitmap> ownedPreviews = new List<Bitmap>();
        private readonly List<PictureBox> previewBoxes = new List<PictureBox>();
        private readonly List<Button> navigation = new List<Button>();
        private readonly List<Panel> pages = new List<Panel>();
        private readonly Panel pageHost = new Panel();
        private readonly Label statusLabel = new Label();
        private bool refreshing;
        private bool previewsInitialized, refreshingPreviews, previewMirror, previewKeyLabels;
        private int previewExpression;
        private int selectedPage;

        /// <summary>建立设置面板。所有回调均在当前 UI 线程执行；宿主负责应用和保存设置。</summary>
        /// <param name="settings">与桌宠共享的设置对象。</param>
        /// <param name="changed">每次编辑后的应用与保存回调。</param>
        /// <param name="resetPosition">将桌宠移回可见位置的回调。</param>
        /// <param name="previewFactory">按形态索引生成预览；返回的图片所有权交给窗口。</param>
        internal SettingsWindow(AppSettings settings, Action changed, Action resetPosition, Func<int, Bitmap> previewFactory, Action checkForUpdates)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            this.settings = settings;
            this.changed = changed;
            this.resetPosition = resetPosition;
            this.previewFactory = previewFactory;
            this.checkForUpdates = checkForUpdates;
            Text = "蝶鼠桌宠 · 设置";
            Font = MakeFont(9.5f, FontStyle.Regular);
            ForeColor = Ink;
            BackColor = Cream;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            ClientSize = new Size(940, 700);
            MinimumSize = new Size(820, 590);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            ShowInTaskbar = true;
            KeyPreview = true;

            TableLayoutPanel shell = new TableLayoutPanel();
            shell.Dock = DockStyle.Fill;
            shell.ColumnCount = 2;
            shell.RowCount = 1;
            shell.Margin = Padding.Empty;
            shell.Padding = Padding.Empty;
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(shell);

            Panel sidebar = new Panel();
            sidebar.Dock = DockStyle.Fill;
            sidebar.BackColor = Color.FromArgb(242, 236, 222);
            sidebar.Margin = Padding.Empty;
            shell.Controls.Add(sidebar, 0, 0);
            TableLayoutPanel sidebarFlow = Stack();
            sidebarFlow.Padding = new Padding(18, 30, 18, 0);
            sidebarFlow.BackColor = sidebar.BackColor;
            sidebar.Controls.Add(sidebarFlow);
            Label logo = LabelOf("蝶鼠桌宠", 25, FontStyle.Bold, Ink);
            logo.Margin = new Padding(5, 0, 0, 0);
            AddRow(sidebarFlow, logo);
            Label tagline = LabelOf("指尖有声，身边有她", 8.5f, FontStyle.Regular, Muted);
            tagline.Margin = new Padding(7, 2, 0, 30);
            AddRow(sidebarFlow, tagline);

            string[] names = { "角色", "键鼠", "动作", "显示", "使用说明", "更新与关于" };
            for (int i = 0; i < names.Length; ++i)
            {
                int pageIndex = i;
                Button button = PlainButton(names[i]);
                button.TextAlign = ContentAlignment.MiddleLeft;
                button.Padding = new Padding(17, 0, 0, 0);
                button.Height = 46;
                button.Dock = DockStyle.Top;
                button.Margin = new Padding(0, 0, 0, 7);
                button.AccessibleName = names[i] + "设置";
                button.Click += delegate { SelectPage(pageIndex); };
                navigation.Add(button);
                AddRow(sidebarFlow, button);
            }

            Label privacy = LabelOf("只响应输入动作\n不记录输入文本\n不上传键鼠数据", 8.5f, FontStyle.Regular, Muted);
            privacy.AutoSize = false;
            privacy.Dock = DockStyle.Bottom;
            privacy.Height = 100;
            privacy.Padding = new Padding(25, 10, 15, 12);
            privacy.TextAlign = ContentAlignment.MiddleLeft;
            sidebar.Controls.Add(privacy);

            TableLayoutPanel right = new TableLayoutPanel();
            right.Dock = DockStyle.Fill;
            right.Margin = Padding.Empty;
            right.ColumnCount = 1;
            right.RowCount = 2;
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
            shell.Controls.Add(right, 1, 0);
            pageHost.Dock = DockStyle.Fill;
            pageHost.Margin = Padding.Empty;
            right.Controls.Add(pageHost, 0, 0);
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.Margin = Padding.Empty;
            statusLabel.Padding = new Padding(28, 0, 12, 0);
            statusLabel.ForeColor = Muted;
            statusLabel.Font = MakeFont(8.5f, FontStyle.Regular);
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.Text = "更改即时生效 · 设置由程序保存在本地";
            right.Controls.Add(statusLabel, 0, 1);

            try
            {
                BuildCharacterPage();
                BuildInputPage();
                BuildMotionPage();
                BuildDisplayPage();
                BuildHelpPage();
                BuildAboutPage();
                SelectPage(0);
                RefreshValues();
            }
            catch { Dispose(); throw; }
        }

        /// <summary>将共享设置同步到控件，不触发修改或保存回调。仅可由 UI 线程调用。</summary>
        internal void RefreshValues()
        {
            if (IsDisposed) return;
            bool previous = refreshing;
            refreshing = true;
            try
            {
                foreach (Action refresh in refreshers) refresh();
                RefreshPreviews();
                statusLabel.Text = settings.paused ? "已暂停动作 · 设置更改仍会即时保存" : "更改即时生效 · 设置由程序保存在本地";
            }
            finally { refreshing = previous; }
        }

        private void BuildCharacterPage()
        {
            TableLayoutPanel body = NewPage("和她一起，轻轻敲下常用键", "两位角色、四套衣装，共享键鼠与显示设置。", "角色");
            TableLayoutPanel card = Card("选择衣装", "点击下方卡片切换角色形态。");
            TableLayoutPanel choices = new TableLayoutPanel();
            choices.AutoSize = true;
            choices.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            choices.Dock = DockStyle.Top;
            choices.ColumnCount = 2;
            choices.RowCount = 2;
            choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            choices.Margin = new Padding(0, 9, 0, 0);
            string[] skinNames = SkinCatalog.Names;
            string[] skinDetails = { "朱慧月的灵魂 · 红橙瞳", "朱慧月的灵魂 · 红橙瞳", "黄玲琳的灵魂 · 金瞳", "黄玲琳的灵魂 · 金瞳" };
            for (int i = 0; i < SkinCatalog.Count; ++i)
            {
                int skinIndex = i;
                Panel tile = new Panel();
                tile.Height = 277;
                tile.Dock = DockStyle.Fill;
                tile.Padding = new Padding(9);
                tile.BackColor = Cream;
                tile.Margin = new Padding(i == 0 ? 0 : 6, 0, i == 0 ? 6 : 0, 0);
                PictureBox picture = new PictureBox();
                picture.Dock = DockStyle.Top;
                picture.Height = 180;
                picture.SizeMode = PictureBoxSizeMode.Zoom;
                picture.BackColor = Cream;
                picture.Cursor = Cursors.Hand;
                picture.AccessibleName = skinNames[i] + "角色预览";
                previewBoxes.Add(picture);
                picture.Click += delegate { settings.skin = skinIndex; ApplyChange(); };
                Label caption = LabelOf(skinDetails[i], 8.5f, FontStyle.Regular, Muted);
                caption.AutoSize = false;
                caption.Dock = DockStyle.Bottom;
                caption.Height = 28;
                caption.TextAlign = ContentAlignment.MiddleCenter;
                Button choose = PlainButton(skinNames[i]);
                choose.Dock = DockStyle.Bottom;
                choose.Height = 40;
                choose.AccessibleName = "切换至" + skinNames[i];
                choose.Click += delegate { settings.skin = skinIndex; ApplyChange(); };
                tile.Controls.Add(picture);
                tile.Controls.Add(caption);
                tile.Controls.Add(choose);
                choices.Controls.Add(tile, i % 2, i / 2);
                refreshers.Add(delegate
                {
                    bool active = settings.skin == skinIndex;
                    choose.Text = skinNames[skinIndex] + (active ? "  ·  当前衣装" : "  ·  使用");
                    choose.BackColor = active ? Mint : Color.White;
                    choose.FlatAppearance.BorderColor = active ? Color.FromArgb(152, 188, 166) : Line;
                    choose.FlatAppearance.BorderSize = 1;
                });
            }
            AddRow(card, choices);
            AddRow(body, card);

            TableLayoutPanel expressionCard = Card("表情", "自然、闭眼、开心、惊讶和晕乎乎；也可以从托盘菜单直接切换。");
            ComboBox expressionBox = ChoiceBox();
            expressionBox.Items.AddRange(new object[] { "自然", "闭眼", "开心", "惊讶", "晕乎乎" });
            expressionBox.SelectedIndexChanged += delegate
            {
                if (!refreshing && expressionBox.SelectedIndex >= 0) { settings.expression = expressionBox.SelectedIndex; ApplyChange(); }
            };
            refreshers.Add(delegate { expressionBox.SelectedIndex = Math.Max(0, Math.Min(4, settings.expression)); });
            AddChoice(expressionCard, "角色表情", expressionBox);
            AddRow(body, expressionCard);
        }

        private void BuildInputPage()
        {
            TableLayoutPanel body = NewPage("让动作跟随你的输入", "14 个常用键独立响应，圆手轻按键盘；鼠标位置来自所选屏幕。", "键鼠");
            TableLayoutPanel keyboard = Card("常用键响应", "同时按下多个常用键时，对应键帽分别反馈，圆手随着输入轻轻按动。");
            AddToggle(keyboard, "响应键盘", "开启圆手轻按和常用键的独立按压反馈。", delegate { return settings.keyboardEnabled; }, delegate(bool value) { settings.keyboardEnabled = value; });
            AddToggle(keyboard, "按键气泡", "在角色旁显示当前按键提示。", delegate { return settings.showKeyBubble; }, delegate(bool value) { settings.showKeyBubble = value; });
            AddToggle(keyboard, "键帽文字", "为常用键显示标记；默认隐藏，保持画面简洁。", delegate { return settings.keyLabels; }, delegate(bool value) { settings.keyLabels = value; });
            AddRow(body, keyboard);

            TableLayoutPanel mouse = Card("鼠标映射", "鼠标和圆手在垫面上小幅共同移动。停止移动后，会停留在映射位置。");
            AddToggle(mouse, "响应鼠标", "响应光标移动、左右键和滚轮。", delegate { return settings.mouseEnabled; }, delegate(bool value) { settings.mouseEnabled = value; });
            ComboBox screenBox = ChoiceBox();
            screenBox.SelectedIndexChanged += delegate
            {
                ScreenChoice selected = screenBox.SelectedItem as ScreenChoice;
                if (!refreshing && selected != null) { settings.screenIndex = selected.index; ApplyChange(); }
            };
            refreshers.Add(delegate
            {
                Screen[] screens = Screen.AllScreens;
                screenBox.BeginUpdate();
                try
                {
                    screenBox.Items.Clear();
                    screenBox.Items.Add(new ScreenChoice(-1, "全桌面 · 所有显示器"));
                    for (int i = 0; i < screens.Length; ++i)
                    {
                        Rectangle bounds = screens[i].Bounds;
                        string label = "显示器 " + (i + 1) + (screens[i].Primary ? " · 主显示器" : "") + "  (" + bounds.Width + " × " + bounds.Height + ")";
                        screenBox.Items.Add(new ScreenChoice(i, label));
                    }
                    screenBox.SelectedIndex = settings.screenIndex < 0 || screens.Length == 0 ? 0 : Math.Min(settings.screenIndex, screens.Length - 1) + 1;
                }
                finally { screenBox.EndUpdate(); }
            });
            AddChoice(mouse, "映射屏幕", screenBox);
            AddHint(mouse, "“全桌面”使用所有显示器组成的完整区域；单个显示器只使用该屏幕的坐标范围。");
            AddRange(mouse, "鼠标活动范围", "调整鼠标与圆手的小幅移动，保持手腕自然衔接。", 25, 150,
                delegate { return (int)Math.Round(settings.mouseRange * 100); },
                delegate(int value) { settings.mouseRange = value / 100f; },
                delegate(int value) { return (value / 100f).ToString("0.00") + " 倍"; });
            AddRow(body, mouse);
        }

        private void BuildMotionPage()
        {
            TableLayoutPanel body = NewPage("让动作轻巧自然", "调整跟手速度与待机动作，也可以临时暂停动作。", "动作");
            TableLayoutPanel movement = Card("动作节奏", "头部随鼠标轻倾，发梢稍后跟随；圆手保留原来的轻按动作。");
            AddRange(movement, "跟手速度", "数值越大，圆手轻按和鼠标跟随的响应越快。", 5, 40,
                delegate { return (int)Math.Round(settings.motionSpeed); },
                delegate(int value) { settings.motionSpeed = value; },
                delegate(int value) { return value.ToString(); });
            AddToggle(movement, "待机动作与眨眼", "轻微呼吸、发梢摆动和自然眨眼；关闭后仍响应键鼠动作。", delegate { return settings.idleMotion; }, delegate(bool value) { settings.idleMotion = value; });
            AddToggle(movement, "暂停动作", "暂时停止角色动作，再次关闭即可恢复。", delegate { return settings.paused; }, delegate(bool value) { settings.paused = value; });
            AddRow(body, movement);
            TableLayoutPanel behavior = Card("多键输入如何表现", null);
            AddHint(behavior, "按住多个常用键时，对应键帽分别保持按压反馈，圆手会随着输入轻按键盘。");
            AddHint(behavior, "快速敲击时，圆手做轻柔短促的按压动作。角色只展示输入动作，不会替你发送按键或改变正在使用的程序。");
            AddRow(body, behavior);
        }

        private void BuildDisplayPage()
        {
            TableLayoutPanel body = NewPage("找到她在桌面上的位置", "大小、透明度和窗口行为都可以随时调整。", "显示");
            TableLayoutPanel appearance = Card("外观大小", "桌宠窗口保持透明背景，可放置在桌面上的合适位置。");
            AddRange(appearance, "显示大小", "以桌宠画面的宽度为基准。", 300, 900,
                delegate { return settings.size; }, delegate(int value) { settings.size = value; },
                delegate(int value) { return value + " px"; });
            AddRange(appearance, "不透明度", "调低后，角色与桌面会一起变淡。", 35, 100,
                delegate { return (int)Math.Round(settings.opacity * 100); }, delegate(int value) { settings.opacity = value / 100f; },
                delegate(int value) { return value + "%"; });
            ComboBox frameBox = ChoiceBox();
            frameBox.Items.AddRange(new object[] { "目标 30 FPS", "目标 60 FPS" });
            frameBox.SelectedIndexChanged += delegate
            {
                if (!refreshing && frameBox.SelectedIndex >= 0) { settings.frameRate = frameBox.SelectedIndex == 0 ? 30 : 60; ApplyChange(); }
            };
            refreshers.Add(delegate { frameBox.SelectedIndex = settings.frameRate <= 30 ? 0 : 1; });
            AddChoice(appearance, "刷新频率", frameBox);
            AddRow(body, appearance);

            TableLayoutPanel window = Card("窗口行为", null);
            AddToggle(window, "始终置顶", "保持在普通窗口上方。", delegate { return settings.topMost; }, delegate(bool value) { settings.topMost = value; });
            AddToggle(window, "鼠标穿透", "点击会穿过桌宠；请从托盘图标恢复操作。", delegate { return settings.clickThrough; }, delegate(bool value) { settings.clickThrough = value; });
            AddToggle(window, "镜像显示", "翻转整套角色和桌面；默认方向为鼠标在左、键盘在右。", delegate { return settings.mirror; }, delegate(bool value) { settings.mirror = value; });
            AddToggle(window,"开机自启","登录 Windows 后自动打开蝶鼠桌宠；关闭即可取消。",delegate{return StartupRegistration.Enabled;},delegate(bool value){StartupRegistration.SetEnabled(value);});
            Button restore = PlainButton("恢复桌宠位置");
            restore.BackColor = Mint;
            restore.Width = 170;
            restore.Height = 39;
            restore.Anchor = AnchorStyles.Left;
            restore.Margin = new Padding(0, 13, 0, 0);
            restore.Click += delegate
            {
                if (resetPosition != null) resetPosition();
                ApplyChange();
            };
            AddRow(window, restore);
            AddHint(window, "找不到角色时，恢复位置会将桌宠移回可见区域。开启穿透后，设置面板仍可正常操作。");
            AddRow(body, window);
        }

        private void BuildHelpPage()
        {
            TableLayoutPanel body = NewPage("认识你的桌面伙伴", "从托盘进入设置，随时调整角色与输入动作。", "使用说明");
            TableLayoutPanel basics = Card("日常操作", null);
            AddInstruction(basics, "移动桌宠", "关闭鼠标穿透后，按住角色并拖动到喜欢的位置。");
            AddInstruction(basics, "切换衣装", "在“角色”页选择常服或打歌服。");
            AddInstruction(basics, "调整大小", "在“显示”页拖动显示大小滑块，支持 300—900 px。");
            AddInstruction(basics, "恢复操作", "开启鼠标穿透后，从系统托盘的蝶鼠桌宠图标重新打开设置并关闭穿透。");
            AddInstruction(basics, "退出程序", "在系统托盘菜单选择退出。关闭本设置窗口不会退出桌宠。");
            AddInstruction(basics,"开机自启","在“显示”页或托盘菜单开启；登录当前 Windows 用户后自动启动。移动程序目录后请重新开启。");
            AddRow(body, basics);
            TableLayoutPanel mapping = Card("输入映射", null);
            AddHint(mapping, "默认以观看者视角排列：鼠标在左，键盘在右。开启镜像后，整套画面一起翻转。");
            AddHint(mapping, "14 个常用键独立响应，圆手轻按键盘。鼠标使用所选显示区域的光标坐标，在垫面上小幅映射移动。");
            AddRow(body, mapping);
            TableLayoutPanel local = Card("本地运行", null);
            AddHint(local, "只响应当前键鼠状态，不记录输入文本，也不上传键鼠数据。设置保存在程序所在工作目录，不会为你添加开机启动或修改系统设置。");
            AddRow(body, local);
        }

        private void BuildAboutPage()
        {
            TableLayoutPanel body = NewPage("蝶鼠桌宠，保持最新", "查看当前版本、检查更新，并管理后台更新提示。", "更新与关于");
            TableLayoutPanel update = Card("版本与更新", "更新只替换程序文件，已有角色、位置和偏好设置会保留。");
            AddInstruction(update, "当前版本", "v" + UpdateService.VersionText);
            AddToggle(update, "自动检查更新", "启动时检查 GitHub 正式版；发现新版本后由你确认下载。", delegate { return settings.autoCheckUpdates; }, delegate(bool value) { settings.autoCheckUpdates = value; });
            Button check = PlainButton("检查更新");
            check.Height = 39;
            check.Anchor = AnchorStyles.Left;
            check.Margin = new Padding(0, 13, 0, 0);
            check.Click += delegate { if (checkForUpdates != null) checkForUpdates(); };
            AddRow(update, check);
            AddHint(update, "如果更新提示被其他窗口遮挡，程序会把提示窗口带到前台。下载失败时当前版本仍可继续使用。");
            AddRow(body, update);

            TableLayoutPanel info = Card("程序信息", null);
            AddInstruction(info, "应用名称", "蝶鼠桌宠");
            AddInstruction(info, "项目主页", "dieying-desktop-pet");
            AddRow(body, info);
        }

        private TableLayoutPanel NewPage(string title, string subtitle, string accessibleName)
        {
            Panel page = new Panel();
            page.Dock = DockStyle.Fill;
            page.AutoScroll = true;
            page.BackColor = Cream;
            page.Visible = false;
            page.AccessibleName = accessibleName + "设置页面";
            TableLayoutPanel body = Stack();
            body.Padding = new Padding(26, 25, 26, 25);
            Label heading = LabelOf(title, 19, FontStyle.Bold, Ink);
            heading.Margin = new Padding(0, 0, 0, 8);
            AddRow(body, heading);
            Label description = LabelOf(subtitle, 9, FontStyle.Regular, Muted);
            description.Margin = new Padding(0, 0, 0, 23);
            AddRow(body, description);
            page.Controls.Add(body);
            pageHost.Controls.Add(page);
            pages.Add(page);
            return body;
        }

        private TableLayoutPanel Card(string title, string description)
        {
            TableLayoutPanel card = new SettingsCard();
            card.ColumnCount = 1;
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            card.Dock = DockStyle.Top;
            card.AutoSize = true;
            card.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            card.BackColor = Color.White;
            card.Padding = new Padding(19, 17, 19, 18);
            card.Margin = new Padding(0, 0, 0, 17);
            Label heading = LabelOf(title, 11.5f, FontStyle.Bold, Ink);
            heading.Margin = new Padding(0, 0, 0, description == null ? 9 : 5);
            AddRow(card, heading);
            if (description != null)
            {
                Label explanation = LabelOf(description, 8.8f, FontStyle.Regular, Muted);
                explanation.Margin = new Padding(0, 0, 0, 10);
                AddRow(card, explanation);
            }
            return card;
        }

        private void AddToggle(TableLayoutPanel card, string title, string description, Func<bool> get, Action<bool> set)
        {
            TableLayoutPanel row = new TableLayoutPanel();
            row.Dock = DockStyle.Top;
            row.AutoSize = true;
            row.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            row.ColumnCount = 2;
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 67));
            row.Margin = new Padding(0, 8, 0, 8);
            TableLayoutPanel text = Stack();
            Label name = LabelOf(title, 9.5f, FontStyle.Bold, Ink);
            name.Margin = new Padding(0, 0, 0, 4);
            AddRow(text, name);
            Label detail = LabelOf(description, 8.5f, FontStyle.Regular, Muted);
            detail.Margin = Padding.Empty;
            AddRow(text, detail);
            row.Controls.Add(text, 0, 0);
            SettingsSwitch toggle = new SettingsSwitch();
            toggle.Size = new Size(58, 29);
            toggle.Anchor = AnchorStyles.Right;
            toggle.Margin = new Padding(8, 6, 0, 6);
            toggle.AccessibleName = title;
            toggle.AccessibleDescription = description;
            toggle.CheckedChanged += delegate
            {
                if (!refreshing) { set(toggle.Checked); ApplyChange(); }
            };
            refreshers.Add(delegate { toggle.Checked = get(); });
            row.Controls.Add(toggle, 1, 0);
            AddRow(card, row);
        }

        private void AddRange(TableLayoutPanel card, string title, string description, int min, int max, Func<int> get, Action<int> set, Func<int, string> format)
        {
            TableLayoutPanel row = new TableLayoutPanel();
            row.Dock = DockStyle.Top;
            row.AutoSize = true;
            row.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            row.ColumnCount = 2;
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
            row.Margin = new Padding(0, 10, 0, 5);
            Label heading = LabelOf(title, 9.5f, FontStyle.Bold, Ink);
            heading.Margin = new Padding(0, 0, 0, 5);
            row.Controls.Add(heading, 0, 0);
            Label valueLabel = LabelOf("", 9.5f, FontStyle.Bold, Ink);
            valueLabel.TextAlign = ContentAlignment.MiddleRight;
            valueLabel.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            valueLabel.Margin = new Padding(0, 0, 0, 5);
            row.Controls.Add(valueLabel, 1, 0);
            Label detail = LabelOf(description, 8.5f, FontStyle.Regular, Muted);
            detail.Margin = new Padding(0, 0, 0, 5);
            row.Controls.Add(detail, 0, 1);
            row.SetColumnSpan(detail, 2);
            TrackBar slider = new TrackBar();
            slider.Minimum = min;
            slider.Maximum = max;
            slider.SmallChange = 1;
            slider.LargeChange = Math.Max(1, (max - min) / 10);
            slider.TickStyle = TickStyle.None;
            slider.AutoSize = false;
            slider.Height = 33;
            slider.Dock = DockStyle.Top;
            slider.Margin = Padding.Empty;
            slider.BackColor = Color.White;
            slider.AccessibleName = title;
            slider.AccessibleDescription = description;
            slider.ValueChanged += delegate
            {
                valueLabel.Text = format(slider.Value);
                if (!refreshing) { set(slider.Value); ApplyChange(); }
            };
            refreshers.Add(delegate
            {
                slider.Value = Math.Max(min, Math.Min(max, get()));
                valueLabel.Text = format(slider.Value);
            });
            row.Controls.Add(slider, 0, 2);
            row.SetColumnSpan(slider, 2);
            AddRow(card, row);
        }

        private void AddChoice(TableLayoutPanel card, string title, ComboBox box)
        {
            Label heading = LabelOf(title, 9.5f, FontStyle.Bold, Ink);
            heading.Margin = new Padding(0, 13, 0, 7);
            AddRow(card, heading);
            box.AccessibleName = title;
            AddRow(card, box);
        }

        private void AddHint(TableLayoutPanel card, string text)
        {
            Label hint = LabelOf(text, 8.8f, FontStyle.Regular, Muted);
            hint.Margin = new Padding(0, 8, 0, 3);
            AddRow(card, hint);
        }

        private void AddInstruction(TableLayoutPanel card, string title, string text)
        {
            Label name = LabelOf(title, 9.5f, FontStyle.Bold, Ink);
            name.Margin = new Padding(0, 9, 0, 2);
            AddRow(card, name);
            AddHint(card, text);
        }

        private ComboBox ChoiceBox()
        {
            ComboBox box = new ComboBox();
            box.Dock = DockStyle.Top;
            box.DropDownStyle = ComboBoxStyle.DropDownList;
            box.FlatStyle = FlatStyle.Flat;
            box.BackColor = Cream;
            box.ForeColor = Ink;
            box.Margin = new Padding(0, 0, 0, 5);
            box.IntegralHeight = false;
            box.DropDownHeight = 220;
            return box;
        }

        private Label LabelOf(string text, float size, FontStyle style, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Dock = DockStyle.Top;
            label.Font = MakeFont(size, style);
            label.ForeColor = color;
            label.UseMnemonic = false;
            label.Margin = Padding.Empty;
            return label;
        }

        private Font MakeFont(float size, FontStyle style)
        {
            Font font = new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point);
            ownedFonts.Add(font);
            return font;
        }

        private Button PlainButton(string text)
        {
            Button button = new Button();
            button.Text = text;
            button.ForeColor = Ink;
            button.BackColor = Color.White;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(249, 227, 173);
            button.FlatAppearance.MouseDownBackColor = Gold;
            button.Font = MakeFont(9.5f, FontStyle.Bold);
            button.Cursor = Cursors.Hand;
            button.UseVisualStyleBackColor = false;
            return button;
        }

        private static TableLayoutPanel Stack()
        {
            TableLayoutPanel stack = new TableLayoutPanel();
            stack.ColumnCount = 1;
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            stack.Dock = DockStyle.Top;
            stack.AutoSize = true;
            stack.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            stack.Margin = Padding.Empty;
            stack.Padding = Padding.Empty;
            return stack;
        }

        private static void AddRow(TableLayoutPanel panel, Control control)
        {
            int row = panel.RowCount++;
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.Controls.Add(control, 0, row);
        }

        private void ApplyChange()
        {
            if (refreshing) return;
            if (changed != null) changed();
            RefreshValues();
        }

        private void RefreshPreviews()
        {
            if (previewFactory == null || refreshingPreviews || previewBoxes.Count == 0) return;
            if (previewsInitialized && previewExpression == settings.expression && previewMirror == settings.mirror && previewKeyLabels == settings.keyLabels) return;
            refreshingPreviews = true;
            List<Bitmap> nextOwned = new List<Bitmap>();
            Bitmap[] nextImages = new Bitmap[previewBoxes.Count];
            try
            {
                // 先完整生成两张新预览；任一张失败时保留原图，并释放这次已生成的图片。
                for (int i = 0; i < nextImages.Length; i++)
                {
                    Bitmap next = previewFactory(i);
                    nextImages[i] = next;
                    if (next != null && !nextOwned.Contains(next)) nextOwned.Add(next);
                }
                for (int i = 0; i < nextImages.Length; i++) previewBoxes[i].Image = nextImages[i];
                foreach (Bitmap previous in ownedPreviews) if (!nextOwned.Contains(previous)) previous.Dispose();
                ownedPreviews.Clear();
                ownedPreviews.AddRange(nextOwned);
                previewExpression = settings.expression;
                previewMirror = settings.mirror;
                previewKeyLabels = settings.keyLabels;
                previewsInitialized = true;
            }
            catch
            {
                foreach (Bitmap next in nextOwned) if (!ownedPreviews.Contains(next)) next.Dispose();
                throw;
            }
            finally { refreshingPreviews = false; }
        }

        private void SelectPage(int pageIndex)
        {
            selectedPage = pageIndex;
            for (int i = 0; i < pages.Count; ++i)
            {
                bool active = i == pageIndex;
                pages[i].Visible = active;
                navigation[i].BackColor = active ? Color.FromArgb(250, 226, 158) : Color.FromArgb(242, 236, 222);
            }
            pages[pageIndex].BringToFront();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            if (keyData == (Keys.Control | Keys.Tab)) { SelectPage((selectedPage + 1) % pages.Count); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.Tab)) { SelectPage((selectedPage + pages.Count - 1) % pages.Count); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (PictureBox picture in previewBoxes) picture.Image = null;
                foreach (Bitmap preview in ownedPreviews) preview.Dispose();
                ownedPreviews.Clear();
                previewBoxes.Clear();
            }
            base.Dispose(disposing);
            if (disposing)
            {
                foreach (Font font in ownedFonts) font.Dispose();
                ownedFonts.Clear();
            }
        }

        private sealed class ScreenChoice
        {
            internal readonly int index;
            private readonly string label;
            internal ScreenChoice(int index, string label) { this.index = index; this.label = label; }
            public override string ToString() { return label; }
        }

        private sealed class SettingsCard : TableLayoutPanel
        {
            internal SettingsCard() { DoubleBuffered = true; }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                using (Pen pen = new Pen(Line))
                    e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            }
        }

        private sealed class SettingsSwitch : CheckBox
        {
            internal SettingsSwitch()
            {
                Appearance = Appearance.Button;
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                Cursor = Cursors.Hand;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Parent == null ? Color.White : Parent.BackColor);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                RectangleF bounds = new RectangleF(1, 1, Width - 3, Height - 3);
                float diameter = bounds.Height;
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddArc(bounds.X, bounds.Y, diameter, diameter, 90, 180);
                    path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 180);
                    path.CloseFigure();
                    using (Brush track = new SolidBrush(Checked ? Color.FromArgb(176, 205, 187) : Color.FromArgb(221, 217, 207))) e.Graphics.FillPath(track, path);
                    if (Focused) using (Pen outline = new Pen(Ink, 1.4f)) e.Graphics.DrawPath(outline, path);
                }
                float knob = diameter - 6;
                float x = Checked ? bounds.Right - knob - 3 : bounds.X + 3;
                using (Brush shadow = new SolidBrush(Color.FromArgb(45, 91, 80, 57))) e.Graphics.FillEllipse(shadow, x, bounds.Y + 4, knob, knob);
                using (Brush dot = new SolidBrush(Color.White)) e.Graphics.FillEllipse(dot, x, bounds.Y + 3, knob, knob);
            }
        }
    }
}
