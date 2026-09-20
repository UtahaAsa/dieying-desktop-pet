using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DieYing
{
    /** <summary>单帧输入快照；只含当前状态和短期动作边沿，不保存输入文本或历史。</summary> */
    internal sealed class InputFrame
    {
        internal bool[] heldKeys = new bool[512];
        internal int[] pressedSinceSnapshot = new int[0];
        internal bool leftButton, rightButton, middleButton, x1Button, x2Button;
        internal int targetKey, lastPressedKey;
        internal long keySequence;
        internal float cursorX, cursorY;
        internal int wheelDelta;
        internal string bubble = String.Empty;
        internal bool hasKeyboardTarget;
        internal double lastActivity;
    }

    /**
     * <summary>仅观察的后台键鼠输入源。须在同一 UI 线程创建、读取及释放，并运行 Windows 消息循环。</summary>
     * <remarks>不注册 NOLEGACY、不拦截输入、不修改鼠标位置、不记录按键文本。数字区 Enter 使用 269。</remarks>
     */
    internal sealed class InputSource : IDisposable
    {
        internal const int NumpadEnter = 269;
        private const uint RidInput = 0x10000003, RidevInputSink = 0x100, RidevDevNotify = 0x2000;
        private const int WmInput = 0x00FF, WmInputDeviceChange = 0x00FE, WmWtsSessionChange = 0x02B1;
        private const int KeyCount = 512;
        private readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        private readonly bool native;
        private readonly bool[] heldKeys = new bool[KeyCount];
        private readonly long[] pressOrder = new long[KeyCount];
        private readonly int[] asynchronousKeys = new int[KeyCount];
        private readonly double[] pressedAt = new double[KeyCount];
        private readonly int[] pendingPresses = new int[128];
        private int pendingPressStart, pendingPressCount;
        private readonly bool[] buttons = new bool[5], buttonPulses = new bool[5];
        private readonly double[] buttonPressedAt = new double[5];
        private RawWindow window;
        private IntPtr rawBuffer;
        private uint rawBufferSize;
        private bool registered, sessionRegistered, disposed, desktopUnavailable;
        private bool snapshotClockBound, hasNativeActivity, hasNativeBubble;
        private string status = "未连接", desktopName = String.Empty, bubble = String.Empty;
        private bool bubbleIsKeyboard;
        private long keySequence, snapshotTicks = Stopwatch.GetTimestamp();
        private double snapshotTime, lastActivity, bubbleUntil, nextDesktopCheck, nextReleaseCheck;
        private int targetKey, lastPressedKey, wheelDelta;
        private float cursorX, cursorY;
        private NativePoint lastCursor;
        private bool hasCursor;

        /** <summary>创建仅接收消息的窗口并注册后台输入。注册失败通过 Status 返回；不抢占焦点。</summary> */
        internal InputSource() : this(true) { }

        private InputSource(bool createNativeWindow)
        {
            native = createNativeWindow;
            if (!native) { status = "测试输入源"; return; }
            try
            {
                desktopName = ReadDesktopName(GetThreadDesktop(GetCurrentThreadId()));
                window = new RawWindow(this);
                RawInputDevice[] devices = Devices(RidevInputSink | RidevDevNotify, window.Handle);
                registered = RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf(typeof(RawInputDevice)));
                if (!registered)
                {
                    status = "后台输入注册失败（Windows 错误 " + Marshal.GetLastWin32Error() + "）";
                    window.Destroy(); window = null;
                    return;
                }
                sessionRegistered = WTSRegisterSessionNotification(window.Handle, 0);
                status = "已连接：Windows Raw Input 后台键鼠";
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                status = "输入窗口创建失败（Windows 错误 " + ex.NativeErrorCode + "）";
                if (window != null) { window.Destroy(); window = null; }
            }
        }

        /** <summary>当前输入源状态。不包含任何用户输入内容。</summary> */
        internal string Status { get { return status; } }

        /**
         * <summary>获得独立快照并消耗滚轮及短点击脉冲。</summary>
         * <param name="mappingBounds">屏幕真实像素范围，允许负坐标；光标在范围之外时夹紧。</param>
         * <param name="now">调用方持续递增的秒数。原始消息以最近一次快照时间校准。</param>
         * <param name="keyboardEnabled">是否输出键盘状态。</param>
         * <param name="mouseEnabled">是否输出鼠标状态；禁用时保持最后映射位置。</param>
         * <returns>可由调用方修改而不影响输入源的快照。</returns>
         */
        internal InputFrame Snapshot(Rectangle mappingBounds, double now, bool keyboardEnabled, bool mouseEnabled)
        {
            CheckThread(); ThrowIfDisposed();
            if (Double.IsNaN(now) || Double.IsInfinity(now)) throw new ArgumentOutOfRangeException("now");
            long ticks = Stopwatch.GetTimestamp();
            if (!snapshotClockBound)
            {
                double adjustment = now - EventTime(ticks);
                if (hasNativeActivity) lastActivity += adjustment;
                if (hasNativeBubble) bubbleUntil += adjustment;
                if (native)
                {
                    for (int key = 1; key < KeyCount; key++) if (heldKeys[key]) pressedAt[key] += adjustment;
                    for (int index = 0; index < buttons.Length; index++) if (buttons[index]) buttonPressedAt[index] += adjustment;
                }
                snapshotClockBound = true;
            }
            snapshotTime = now; snapshotTicks = ticks;
            if (native && registered)
            {
                GuardDesktop(now);
                if (!desktopUnavailable) ReconcileReleasedInputs(now);
            }
            if (native && !desktopUnavailable && mouseEnabled)
            {
                NativePoint cursor;
                if (GetCursorPos(out cursor))
                {
                    if (hasCursor && (cursor.x != lastCursor.x || cursor.y != lastCursor.y)) lastActivity = now;
                    hasCursor = true; lastCursor = cursor;
                    cursorX = Normalize(cursor.x, mappingBounds.Left, mappingBounds.Width, cursorX);
                    cursorY = Normalize(cursor.y, mappingBounds.Top, mappingBounds.Height, cursorY);
                }
            }
            InputFrame frame = new InputFrame();
            if (keyboardEnabled)
            {
                Array.Copy(heldKeys, frame.heldKeys, heldKeys.Length);
                frame.targetKey = targetKey; frame.lastPressedKey = lastPressedKey;
                frame.hasKeyboardTarget = MostRecentHeld() != 0;
                frame.pressedSinceSnapshot = new int[pendingPressCount];
                for (int index = 0; index < pendingPressCount; index++)
                    frame.pressedSinceSnapshot[index] = pendingPresses[(pendingPressStart + index) % pendingPresses.Length];
            }
            frame.keySequence = keySequence;
            frame.cursorX = cursorX; frame.cursorY = cursorY; frame.lastActivity = lastActivity;
            if (mouseEnabled)
            {
                frame.leftButton = buttons[0] || buttonPulses[0]; frame.rightButton = buttons[1] || buttonPulses[1];
                frame.middleButton = buttons[2] || buttonPulses[2]; frame.x1Button = buttons[3] || buttonPulses[3];
                frame.x2Button = buttons[4] || buttonPulses[4]; frame.wheelDelta = wheelDelta;
            }
            frame.bubble = now < bubbleUntil && (bubbleIsKeyboard ? keyboardEnabled : mouseEnabled) ? bubble : String.Empty;
            wheelDelta = 0; pendingPressStart = pendingPressCount = 0; Array.Clear(buttonPulses, 0, buttonPulses.Length);
            return frame;
        }

        /** <summary>清空保持状态、目标、滚轮和气泡。动作序号保持单调，避免被解释为新的按压。</summary> */
        internal void Clear()
        {
            CheckThread();
            Array.Clear(heldKeys, 0, heldKeys.Length); Array.Clear(pressOrder, 0, pressOrder.Length);
            Array.Clear(asynchronousKeys, 0, asynchronousKeys.Length); Array.Clear(pressedAt, 0, pressedAt.Length);
            Array.Clear(buttons, 0, buttons.Length); Array.Clear(buttonPulses, 0, buttonPulses.Length);
            pendingPressStart = pendingPressCount = 0;
            targetKey = lastPressedKey = wheelDelta = 0; bubble = String.Empty; bubbleUntil = 0;
        }

        /**
         * <summary>供状态测试及原始消息共用的物理按键边沿入口。自动重复不重复增加动作序号。</summary>
         * <param name="vk">虚拟键码，或数字区 Enter 的 269；通用修饰键归到左侧。</param>
         * <param name="down">按下为 true，释放为 false。</param>
         * <param name="now">与快照相同基准的秒数。</param>
         */
        internal void FeedKey(int vk, bool down, double now)
        {
            CheckThread(); ThrowIfDisposed();
            if (vk == 16) vk = 160; else if (vk == 17) vk = 162; else if (vk == 18) vk = 164;
            if (vk <= 0 || (vk >= 255 && vk != NumpadEnter)) return;
            if (heldKeys[vk] == down) return;
            heldKeys[vk] = down; lastActivity = now;
            heldKeys[16] = heldKeys[160] || heldKeys[161]; heldKeys[17] = heldKeys[162] || heldKeys[163];
            heldKeys[18] = heldKeys[164] || heldKeys[165];
            if (down)
            {
                keySequence++; pressOrder[vk] = keySequence; pressedAt[vk] = now;
                // 队列仅存在至下一帧；异常长帧最多保留最新 128 个边沿，序号仍准确累计。
                if (pendingPressCount == pendingPresses.Length)
                { pendingPressStart = (pendingPressStart + 1) % pendingPresses.Length; pendingPressCount--; }
                pendingPresses[(pendingPressStart + pendingPressCount) % pendingPresses.Length] = vk; pendingPressCount++;
                asynchronousKeys[vk] = vk == NumpadEnter ? 13 : vk;
                targetKey = lastPressedKey = vk; bubble = BuildChord(); bubbleUntil = now + 0.75; bubbleIsKeyboard = true;
            }
            else
            {
                pressOrder[vk] = 0;
                int fallback = MostRecentHeld(); targetKey = fallback != 0 ? fallback : lastPressedKey;
            }
        }

        /** <summary>鼠标边沿入口。index 为左、右、中、侧键 1、侧键 2 的 0..4；保留不足一帧的点击。</summary> */
        internal void FeedMouseButton(int index, bool down, double now)
        {
            CheckThread(); ThrowIfDisposed();
            if (index < 0 || index >= buttons.Length || buttons[index] == down) return;
            buttons[index] = down; lastActivity = now;
            if (down)
            {
                buttonPulses[index] = true; buttonPressedAt[index] = now;
                string[] names = { "鼠标左键", "鼠标右键", "鼠标中键", "鼠标侧键 1", "鼠标侧键 2" };
                bubble = names[index]; bubbleUntil = now + 0.75; bubbleIsKeyboard = false;
            }
        }

        /** <summary>累计滚轮原始增量（常规一格为 120）；下次快照后归零。</summary> */
        internal void FeedWheel(int delta, double now)
        {
            CheckThread(); ThrowIfDisposed();
            if (delta == 0) return;
            wheelDelta = (int)Math.Max(Int32.MinValue, Math.Min(Int32.MaxValue, (long)wheelDelta + delta));
            lastActivity = now; bubble = delta > 0 ? "滚轮 ↑" : "滚轮 ↓"; bubbleUntil = now + 0.75; bubbleIsKeyboard = false;
        }

        /** <summary>在创建线程注销输入并释放句柄；重复调用安全。</summary> */
        public void Dispose()
        {
            CheckThread(); if (disposed) return;
            Clear();
            if (sessionRegistered && window != null) WTSUnRegisterSessionNotification(window.Handle);
            if (registered)
            {
                RawInputDevice[] devices = Devices(1, IntPtr.Zero);
                RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf(typeof(RawInputDevice)));
            }
            if (window != null) { window.Destroy(); window = null; }
            if (rawBuffer != IntPtr.Zero) { Marshal.FreeHGlobal(rawBuffer); rawBuffer = IntPtr.Zero; }
            registered = false; disposed = true; status = "输入源已关闭";
        }

        private void ReadRawInput(IntPtr handle)
        {
            if (disposed || !registered || desktopUnavailable) return;
            uint size = 0, headerSize = (uint)(8 + IntPtr.Size * 2);
            if (GetRawInputData(handle, RidInput, IntPtr.Zero, ref size, headerSize) == UInt32.MaxValue || size < headerSize || size > 1024 * 1024) return;
            if (size > rawBufferSize)
            {
                IntPtr next = Marshal.AllocHGlobal((int)size);
                if (rawBuffer != IntPtr.Zero) Marshal.FreeHGlobal(rawBuffer);
                rawBuffer = next; rawBufferSize = size;
            }
            uint read = GetRawInputData(handle, RidInput, rawBuffer, ref size, headerSize);
            if (read == UInt32.MaxValue || read < headerSize || read > rawBufferSize) return;
            double now = EventTime(Stopwatch.GetTimestamp());
            DecodeRawPacket(rawBuffer, read, (int)headerSize, now);
            hasNativeActivity = true; hasNativeBubble = true;
        }

        private void DecodeRawPacket(IntPtr data, uint bytes, int headerSize, double now)
        {
            if (bytes < headerSize) return;
            uint declaredSize = unchecked((uint)Marshal.ReadInt32(data, 4));
            if (declaredSize > bytes || declaredSize < headerSize) return;
            int type = Marshal.ReadInt32(data), offset = headerSize;
            if (type == 1 && declaredSize >= headerSize + 16)
            {
                int scanCode = (ushort)Marshal.ReadInt16(data, offset), flags = (ushort)Marshal.ReadInt16(data, offset + 2);
                int vk = (ushort)Marshal.ReadInt16(data, offset + 6);
                if (scanCode == 0xFF || vk == 0 || vk >= 255) return;
                int key = NormalizeRawKey(vk, scanCode, flags);
                if (key == 0) return;
                FeedKey(key, (flags & 1) == 0, now);
                asynchronousKeys[key] = key == NumpadEnter ? 13 : ((key >= 96 && key <= 105) || key == 110 ? vk : key);
            }
            else if (type == 0 && declaredSize >= headerSize + 24)
            {
                // RAWMOUSE 的 ULONG 联合体从偏移 4 开始，不能紧接 USHORT flags 读取。
                int buttonFlags = (ushort)Marshal.ReadInt16(data, offset + 4);
                for (int index = 0; index < buttons.Length; index++)
                {
                    if ((buttonFlags & (1 << (index * 2))) != 0) FeedMouseButton(index, true, now);
                    if ((buttonFlags & (2 << (index * 2))) != 0) FeedMouseButton(index, false, now);
                }
                if ((buttonFlags & 0x400) != 0) FeedWheel(Marshal.ReadInt16(data, offset + 6), now);
                if ((buttonFlags & 0x800) != 0)
                {
                    short delta = Marshal.ReadInt16(data, offset + 6);
                    if (delta != 0) { bubble = delta > 0 ? "横向滚轮 →" : "横向滚轮 ←"; bubbleUntil = now + 0.75; bubbleIsKeyboard = false; lastActivity = now; }
                }
                if (Marshal.ReadInt32(data, offset + 12) != 0 || Marshal.ReadInt32(data, offset + 16) != 0) lastActivity = now;
            }
        }

        private static int NormalizeRawKey(int vk, int scanCode, int flags)
        {
            bool extended = (flags & 2) != 0;
            if (vk == 16)
            {
                // PrintScreen 的 E0 2A/E0 36 是虚拟 Shift 片段，不能留下修饰键。
                if (extended) return 0;
                return scanCode == 0x36 ? 161 : 160;
            }
            if (vk == 17) return extended ? 163 : 162;
            if (vk == 18) return extended ? 165 : 164;
            if (vk == 13 && extended) return NumpadEnter;
            if (!extended)
            {
                switch (vk)
                {
                    case 45: return 96; case 35: return 97; case 40: return 98; case 34: return 99;
                    case 37: return 100; case 12: return 101; case 39: return 102; case 36: return 103;
                    case 38: return 104; case 33: return 105; case 46: return 110;
                }
            }
            return vk;
        }

        private int MostRecentHeld()
        {
            long newest = 0; int key = 0;
            for (int candidate = 1; candidate < KeyCount; candidate++)
                if (heldKeys[candidate] && pressOrder[candidate] > newest) { newest = pressOrder[candidate]; key = candidate; }
            return key;
        }

        private string BuildChord()
        {
            StringBuilder text = new StringBuilder();
            int[] modifiers = { 162, 163, 160, 161, 164, 165, 91, 92 };
            foreach (int key in modifiers) if (heldKeys[key]) AppendKey(text, KeyName(key));
            int count = 0;
            for (int key = 1; key < KeyCount; key++)
            {
                if (!heldKeys[key] || pressOrder[key] == 0 || IsModifier(key)) continue;
                if (count++ < 5) AppendKey(text, KeyName(key));
            }
            if (count > 5) AppendKey(text, "…");
            return text.ToString();
        }

        private static bool IsModifier(int key) { return key == 91 || key == 92 || (key >= 160 && key <= 165); }
        private static void AppendKey(StringBuilder text, string key) { if (text.Length > 0) text.Append(" + "); text.Append(key); }
        private static string KeyName(int key)
        {
            if (key >= 65 && key <= 90) return ((char)key).ToString();
            if (key >= 48 && key <= 57) return ((char)key).ToString();
            if (key >= 112 && key <= 135) return "F" + (key - 111);
            if (key >= 96 && key <= 105) return "Num " + (key - 96);
            switch (key)
            {
                case 8: return "Backspace"; case 9: return "Tab"; case 13: return "Enter"; case NumpadEnter: return "Num Enter";
                case 19: return "Pause"; case 20: return "Caps Lock"; case 27: return "Esc"; case 32: return "Space";
                case 33: return "Page Up"; case 34: return "Page Down"; case 35: return "End"; case 36: return "Home";
                case 37: return "←"; case 38: return "↑"; case 39: return "→"; case 40: return "↓";
                case 44: return "Print Screen"; case 45: return "Insert"; case 46: return "Delete";
                case 91: return "L Win"; case 92: return "R Win"; case 93: return "Menu";
                case 106: return "Num *"; case 107: return "Num +"; case 109: return "Num -"; case 110: return "Num ."; case 111: return "Num /";
                case 144: return "Num Lock"; case 145: return "Scroll Lock";
                case 160: return "L Shift"; case 161: return "R Shift"; case 162: return "L Ctrl"; case 163: return "R Ctrl";
                case 164: return "L Alt"; case 165: return "R Alt";
                case 186: return ";"; case 187: return "="; case 188: return ","; case 189: return "-"; case 190: return ".";
                case 191: return "/"; case 192: return "`"; case 219: return "["; case 220: return "\\"; case 221: return "]"; case 222: return "'";
                default: return "Key " + key;
            }
        }

        private void GuardDesktop(double now)
        {
            if (now < nextDesktopCheck) return;
            nextDesktopCheck = now + 0.25;
            IntPtr inputDesktop = OpenInputDesktop(0, false, 1);
            bool unavailable = inputDesktop == IntPtr.Zero;
            if (inputDesktop != IntPtr.Zero)
            {
                try
                {
                    string current = ReadDesktopName(inputDesktop);
                    unavailable = current.Length == 0 || (desktopName.Length > 0 && !String.Equals(current, desktopName, StringComparison.Ordinal));
                }
                finally { CloseDesktop(inputDesktop); }
            }
            if (unavailable != desktopUnavailable)
            {
                desktopUnavailable = unavailable; Clear();
                status = unavailable ? "输入桌面暂不可用，已清除按住状态" : "已连接：Windows Raw Input 后台键鼠";
            }
        }

        private void ReconcileReleasedInputs(double now)
        {
            if (now < nextReleaseCheck) return;
            nextReleaseCheck = now + 0.1;
            // 只修复遗漏的释放，不用轮询生成按压，因此短按仍由 Raw Input 的边沿驱动。
            for (int key = 1; key < KeyCount; key++)
            {
                if (!heldKeys[key] || pressOrder[key] == 0 || now - pressedAt[key] < 0.1) continue;
                int asynchronousKey = asynchronousKeys[key];
                if (asynchronousKey > 0 && (GetAsyncKeyState(asynchronousKey) & 0x8000) == 0) FeedKey(key, false, now);
            }
            int[] mouseKeys = { 1, 2, 4, 5, 6 };
            for (int index = 0; index < buttons.Length; index++)
                if (buttons[index] && now - buttonPressedAt[index] >= 0.1 && (GetAsyncKeyState(mouseKeys[index]) & 0x8000) == 0)
                    FeedMouseButton(index, false, now);
        }

        private void HandleSessionChange(int reason)
        {
            // 锁屏、解锁、登录/注销和会话连接变动都可能丢失释放；均清除暂存状态。
            Clear(); nextDesktopCheck = 0;
            if (reason == 7 || reason == 3 || reason == 4 || reason == 6)
            { desktopUnavailable = true; status = "会话不可用，已清除按住状态"; }
        }

        private static string ReadDesktopName(IntPtr desktop)
        {
            if (desktop == IntPtr.Zero) return String.Empty;
            uint needed; StringBuilder name = new StringBuilder(256);
            return GetUserObjectInformation(desktop, 2, name, name.Capacity * 2, out needed) ? name.ToString() : String.Empty;
        }

        private static float Normalize(int coordinate, int start, int length, float fallback)
        {
            if (length <= 1) return fallback;
            return (float)Math.Max(-1, Math.Min(1, ((double)coordinate - start) / (length - 1) * 2 - 1));
        }
        private double EventTime(long ticks) { return snapshotTime + (ticks - snapshotTicks) / (double)Stopwatch.Frequency; }
        private void CheckThread() { if (Thread.CurrentThread.ManagedThreadId != ownerThread) throw new InvalidOperationException("InputSource 必须在创建它的 UI 线程使用。"); }
        private void ThrowIfDisposed() { if (disposed) throw new ObjectDisposedException("InputSource"); }
        private static RawInputDevice[] Devices(uint flags, IntPtr handle)
        {
            return new RawInputDevice[] {
                new RawInputDevice { usagePage = 1, usage = 6, flags = flags, target = handle },
                new RawInputDevice { usagePage = 1, usage = 2, flags = flags, target = handle }
            };
        }

        private sealed class RawWindow : NativeWindow
        {
            private readonly InputSource source;
            internal RawWindow(InputSource source)
            {
                this.source = source;
                CreateHandle(new CreateParams { Caption = "DieYing.RawInput", Parent = new IntPtr(-3) });
            }
            internal void Destroy() { DestroyHandle(); }
            protected override void WndProc(ref Message message)
            {
                if (message.Msg == WmInput) source.ReadRawInput(message.LParam);
                else if (message.Msg == WmInputDeviceChange) source.Clear();
                else if (message.Msg == WmWtsSessionChange) source.HandleSessionChange(message.WParam.ToInt32());
                // 前台 WM_INPUT 仍交由 DefWindowProc 清理系统数据，未吞掉键鼠消息。
                base.WndProc(ref message);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawInputDevice { internal ushort usagePage, usage; internal uint flags; internal IntPtr target; }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { internal int x, y; }
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterRawInputDevices([In] RawInputDevice[] devices, uint number, uint size);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputData(IntPtr handle, uint command, IntPtr data, ref uint size, uint headerSize);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out NativePoint point);
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")]
        private static extern IntPtr GetThreadDesktop(uint thread);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseDesktop(IntPtr desktop);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder information, int length, out uint needed);
        [DllImport("wtsapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WTSRegisterSessionNotification(IntPtr handle, int flags);
        [DllImport("wtsapi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WTSUnRegisterSessionNotification(IntPtr handle);

        /** <summary>运行确定性状态与数据布局自检；不注册真实设备、不修改系统输入。失败抛出异常。</summary> */
        internal static string RunSelfTests()
        {
            int passed = 0; StringBuilder report = new StringBuilder();
            Action<bool, string> check = delegate(bool condition, string name)
            {
                if (!condition) throw new InvalidOperationException("Input self-test FAILED: " + name);
                passed++; report.AppendLine("PASS " + name);
            };
            Rectangle bounds = new Rectangle(0, 0, 1920, 1080);
            using (InputSource source = new InputSource(false))
            {
                source.FeedKey(65, true, 1); InputFrame frame = source.Snapshot(bounds, 1, true, true);
                check(frame.heldKeys[65] && frame.targetKey == 65 && frame.lastPressedKey == 65 && frame.keySequence == 1 && frame.hasKeyboardTarget, "press");
                frame.heldKeys[65] = false;
                check(source.Snapshot(bounds, 1.01, true, true).heldKeys[65], "snapshot-isolation");
                source.FeedKey(65, true, 1.02);
                check(source.Snapshot(bounds, 1.02, true, true).keySequence == 1, "autorepeat-does-not-restrike");
                source.FeedKey(65, false, 1.03); frame = source.Snapshot(bounds, 1.04, true, true);
                check(!frame.heldKeys[65] && !frame.hasKeyboardTarget && frame.targetKey == 65, "release-keeps-return-target");
                source.FeedKey(66, true, 1.05); source.FeedKey(66, false, 1.051); frame = source.Snapshot(bounds, 1.06, true, true);
                check(!frame.heldKeys[66] && frame.keySequence == 2 && frame.lastPressedKey == 66 && frame.targetKey == 66, "fast-tap-edge");
                source.FeedKey(69, true, 1.07); source.FeedKey(69, false, 1.071); source.FeedKey(70, true, 1.072); source.FeedKey(70, false, 1.073);
                frame = source.Snapshot(bounds, 1.08, true, true);
                check(frame.pressedSinceSnapshot.Length == 2 && frame.pressedSinceSnapshot[0] == 69 && frame.pressedSinceSnapshot[1] == 70 && source.Snapshot(bounds, 1.09, true, true).pressedSinceSnapshot.Length == 0, "multi-fast-tap-edges-consumed-once");
                source.Clear(); source.FeedKey(162, true, 2); source.FeedKey(160, true, 2.01); source.FeedKey(67, true, 2.02);
                frame = source.Snapshot(bounds, 2.03, true, true);
                check(frame.heldKeys[162] && frame.heldKeys[160] && frame.heldKeys[67] && frame.heldKeys[17] && frame.heldKeys[16] && frame.targetKey == 67 && frame.bubble == "L Ctrl + L Shift + C", "held-chord");
                source.FeedKey(68, true, 2.04); source.FeedKey(68, false, 2.05);
                check(source.Snapshot(bounds, 2.06, true, true).targetKey == 67, "fallback-to-last-held");
                source.FeedKey(67, false, 2.07); source.FeedKey(160, false, 2.08); source.FeedKey(162, false, 2.09); frame = source.Snapshot(bounds, 2.1, true, true);
                check(!frame.hasKeyboardTarget && !frame.heldKeys[16] && !frame.heldKeys[17] && frame.targetKey == 68, "all-up-keeps-latest-strike");
                check(source.Snapshot(bounds, 2.80, true, true).bubble.Length == 0, "bubble-expires-within-800ms");
                source.FeedWheel(120, 3); source.FeedWheel(-30, 3.01);
                check(source.Snapshot(bounds, 3.02, true, true).wheelDelta == 90 && source.Snapshot(bounds, 3.03, true, true).wheelDelta == 0, "wheel-consumed-once");
                source.FeedMouseButton(0, true, 3.1); source.FeedMouseButton(0, false, 3.101);
                check(source.Snapshot(bounds, 3.11, true, true).leftButton && !source.Snapshot(bounds, 3.12, true, true).leftButton, "fast-mouse-click-one-frame");
                source.FeedKey(163, true, 4); source.FeedKey(NumpadEnter, true, 4.01); frame = source.Snapshot(bounds, 4.02, true, true);
                check(frame.heldKeys.Length == 512 && frame.heldKeys[269] && !frame.heldKeys[13] && frame.targetKey == 269 && frame.bubble.Contains("Num Enter"), "distinct-numpad-enter");
                frame = source.Snapshot(bounds, 4.03, false, false);
                check(!frame.heldKeys[269] && !frame.hasKeyboardTarget && frame.targetKey == 0 && frame.bubble.Length == 0 && !frame.leftButton, "disabled-channels");
                long sequence = frame.keySequence; source.FeedMouseButton(4, true, 4.1); source.FeedWheel(120, 4.1); source.Clear(); frame = source.Snapshot(bounds, 4.2, true, true);
                check(frame.targetKey == 0 && frame.lastPressedKey == 0 && !frame.hasKeyboardTarget && !frame.x2Button && frame.wheelDelta == 0 && frame.bubble.Length == 0 && frame.keySequence == sequence, "reset-does-not-replay");
                check(Normalize(-100, -100, 201, 0) == -1 && Normalize(0, -100, 201, 0) == 0 && Normalize(100, -100, 201, 0) == 1 && Normalize(999, -100, 201, 0) == 1 && Normalize(1, 0, 0, .3f) == .3f, "negative-screen-coordinate-clamp");
                check(NormalizeRawKey(16, 0x2A, 0) == 160 && NormalizeRawKey(16, 0x36, 0) == 161 && NormalizeRawKey(16, 0x2A, 2) == 0 && NormalizeRawKey(17, 0x1D, 2) == 163 && NormalizeRawKey(18, 0x38, 2) == 165, "raw-left-right-modifiers");
                check(NormalizeRawKey(13, 0x1C, 2) == 269 && NormalizeRawKey(13, 0x1C, 0) == 13 && NormalizeRawKey(35, 0x4F, 0) == 97 && NormalizeRawKey(35, 0x4F, 2) == 35, "raw-physical-numpad");
                foreach (int headerSize in new int[] { 16, 24 })
                {
                    byte[] packet = new byte[headerSize + 24]; IntPtr pointer = Marshal.AllocHGlobal(packet.Length);
                    try
                    {
                        BitConverter.GetBytes(1).CopyTo(packet, 0); BitConverter.GetBytes(headerSize + 16).CopyTo(packet, 4);
                        BitConverter.GetBytes((ushort)0x1C).CopyTo(packet, headerSize); BitConverter.GetBytes((ushort)2).CopyTo(packet, headerSize + 2);
                        BitConverter.GetBytes((ushort)13).CopyTo(packet, headerSize + 6); Marshal.Copy(packet, 0, pointer, packet.Length);
                        source.Clear(); source.DecodeRawPacket(pointer, (uint)packet.Length, headerSize, 5);
                        check(source.Snapshot(bounds, 5.01, true, true).heldKeys[269], "raw-keyboard-layout-" + (headerSize == 16 ? "x86" : "x64"));
                        Array.Clear(packet, 0, packet.Length); BitConverter.GetBytes(packet.Length).CopyTo(packet, 4);
                        BitConverter.GetBytes((ushort)0x405).CopyTo(packet, headerSize + 4); BitConverter.GetBytes((short)-120).CopyTo(packet, headerSize + 6);
                        Marshal.Copy(packet, 0, pointer, packet.Length); source.Clear(); source.DecodeRawPacket(pointer, (uint)packet.Length, headerSize, 6);
                        frame = source.Snapshot(bounds, 6.01, true, true);
                        check(frame.leftButton && frame.rightButton && frame.wheelDelta == -120, "raw-mouse-layout-" + (headerSize == 16 ? "x86" : "x64"));
                    }
                    finally { Marshal.FreeHGlobal(pointer); }
                }
            }
            report.AppendLine("PASS " + passed + " input checks; no native input was generated.");
            return report.ToString();
        }
    }
}
