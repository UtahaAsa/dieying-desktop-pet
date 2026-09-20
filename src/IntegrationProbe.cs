using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DieYing
{
    /** <summary>仅由显式诊断入口调用的 Windows 注入输入链路检查；不代表真实键鼠硬件验证。</summary> */
    internal static class IntegrationProbe
    {
        /** <summary>创建独立测试窗口和消息循环；有其它主循环、失焦或用户介入时拒绝继续输入。</summary> */
        internal static string Run()
        {
            if (Application.MessageLoop) throw new InvalidOperationException("输入链路检查不能嵌套已有 Windows 消息循环。");
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
                throw new InvalidOperationException("输入链路检查须在 STA 线程运行。");
            using (ProbeForm form = new ProbeForm())
            {
                Application.Run(form);
                if (form.Failure != null) throw new InvalidOperationException(form.Report, form.Failure);
                return form.Report;
            }
        }

        private sealed class ProbeStep
        {
            internal string name;
            internal Action inject;
            internal Func<InputFrame, bool> verify;
            internal int rawType;
        }

        private sealed class ProbeForm : Form, IMessageFilter
        {
            private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
            private readonly Stopwatch clock = new Stopwatch();
            private readonly List<ProbeStep> steps = new List<ProbeStep>();
            private readonly Dictionary<int, ushort> ownedKeys = new Dictionary<int, ushort>();
            private readonly StringBuilder report = new StringBuilder("Windows注入输入链路检查\r\n");
            private readonly byte[] rawHeader = new byte[24];
            private InputSource source;
            private bool active, finished, interfered, filterAdded, ownsLeft, ownsRight, cursorMoved;
            private Point originalCursor, expectedCursor;
            private int stepIndex, rawKeyCount, rawMouseCount, rawBeforeStep;
            private double nextCheck;
            private InputFrame firstMouseFrame;
            internal Exception Failure { get; private set; }
            internal string Report { get { return report.ToString(); } }

            internal ProbeForm()
            {
                Text = "蝶应 · Windows 输入链路检查";
                ClientSize = new Size(480, 210);
                StartPosition = FormStartPosition.CenterScreen;
                FormBorderStyle = FormBorderStyle.FixedSingle;
                MaximizeBox = false;
                MinimizeBox = false;
                KeyPreview = true;
                BackColor = Color.FromArgb(255, 249, 233);
                Font = new Font("Microsoft YaHei UI", 10);
                timer.Interval = 20;
                timer.Tick += TickProbe;
                Shown += StartProbe;
                Deactivate += delegate { if (active && !finished) Cancel("测试窗口失去焦点，已取消后续注入。"); };
                FormClosing += delegate { if (!finished) Cancel("测试窗口已关闭，检查取消。"); };
                KeyPress += delegate(object sender, KeyPressEventArgs e) { e.Handled = true; };
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                using (Brush ink = new SolidBrush(Color.FromArgb(92, 66, 40)))
                {
                    e.Graphics.DrawString("正在检查 Windows 注入输入链路（约 4 秒）", Font, ink, 25, 30);
                    e.Graphics.DrawString("测试输入只在本窗口前台时发送。\n请暂时不要操作键鼠；切换窗口即取消。", Font, ink, 25, 70);
                    string status = active && stepIndex < steps.Count ? steps[stepIndex].name : "等待本窗口成为前台";
                    e.Graphics.DrawString(status, Font, ink, 25, 140);
                }
            }

            private void StartProbe(object sender, EventArgs args)
            {
                try
                {
                    source = new InputSource();
                    if (!source.Status.StartsWith("已连接", StringComparison.Ordinal)) throw new InvalidOperationException(source.Status);
                    Application.AddMessageFilter(this);
                    filterAdded = true;
                    clock.Start();
                    timer.Start();
                }
                catch (Exception ex) { Fail(ex); }
            }

            private void AddStep(string name, Action inject, Func<InputFrame, bool> verify, int rawType)
            {
                steps.Add(new ProbeStep { name = name, inject = inject, verify = verify, rawType = rawType });
            }

            private void BuildSteps()
            {
                AddStep("A 按下", delegate { Key(65, 0x1E, true); }, delegate(InputFrame f) { return f.heldKeys[65] && HasEdge(f, 65); }, 1);
                AddStep("A 释放", delegate { Key(65, 0x1E, false); }, delegate(InputFrame f) { return !f.heldKeys[65]; }, 1);
                AddStep("左 Shift 按下", delegate { Key(160, 0x2A, true); }, delegate(InputFrame f) { return f.heldKeys[160] && !f.heldKeys[161]; }, 1);
                AddStep("左 Shift + J 组合", delegate { Key(74, 0x24, true); }, delegate(InputFrame f) { return f.heldKeys[160] && f.heldKeys[74] && f.bubble.Contains("L Shift") && f.bubble.Contains("J"); }, 1);
                AddStep("J 释放保留 Shift", delegate { Key(74, 0x24, false); }, delegate(InputFrame f) { return !f.heldKeys[74] && f.heldKeys[160]; }, 1);
                AddStep("左 Shift 释放", delegate { Key(160, 0x2A, false); }, delegate(InputFrame f) { return !f.heldKeys[160] && !f.heldKeys[16]; }, 1);
                AddStep("右 Shift 独立识别", delegate { Key(161, 0x36, true); }, delegate(InputFrame f) { return f.heldKeys[161] && !f.heldKeys[160]; }, 1);
                AddStep("右 Shift 释放", delegate { Key(161, 0x36, false); }, delegate(InputFrame f) { return !f.heldKeys[161] && !f.heldKeys[16]; }, 1);
                AddStep("数字区 Enter 独立识别", delegate { Key(269, 0x1C, true); }, delegate(InputFrame f) { return f.heldKeys[269] && !f.heldKeys[13]; }, 1);
                AddStep("数字区 Enter 释放", delegate { Key(269, 0x1C, false); }, delegate(InputFrame f) { return !f.heldKeys[269]; }, 1);
                AddStep("鼠标客户区第一坐标", delegate { MoveInside(new Point(100, 165)); }, delegate(InputFrame f) { firstMouseFrame = f; return MatchesCursor(f); }, 0);
                AddStep("鼠标客户区第二坐标", delegate { MoveInside(new Point(355, 165)); }, delegate(InputFrame f) { return MatchesCursor(f) && firstMouseFrame != null && Math.Abs(f.cursorX - firstMouseFrame.cursorX) > 0.5f; }, 0);
                AddStep("鼠标左键按下", delegate { MouseButton(true, true); }, delegate(InputFrame f) { return f.leftButton && !f.rightButton; }, 0);
                AddStep("鼠标左键释放", delegate { MouseButton(true, false); }, delegate(InputFrame f) { return !f.leftButton; }, 0);
                AddStep("鼠标右键按下", delegate { MouseButton(false, true); }, delegate(InputFrame f) { return f.rightButton && !f.leftButton; }, 0);
                AddStep("鼠标右键释放", delegate { MouseButton(false, false); }, delegate(InputFrame f) { return !f.rightButton; }, 0);
                AddStep("滚轮向上", delegate { Wheel(120); }, delegate(InputFrame f) { return f.wheelDelta == 120; }, 0);
                AddStep("滚轮向下", delegate { Wheel(-120); }, delegate(InputFrame f) { return f.wheelDelta == -120; }, 0);
                AddStep("滚轮消费与释放状态", delegate { }, delegate(InputFrame f) { return f.wheelDelta == 0 && !f.leftButton && !f.rightButton && !f.hasKeyboardTarget; }, -1);
            }

            private void TickProbe(object sender, EventArgs args)
            {
                if (finished) return;
                try
                {
                    if (clock.Elapsed.TotalSeconds > 6) throw new TimeoutException("Windows 输入链路检查超过6秒。");
                    if (!active)
                    {
                        if (clock.Elapsed.TotalSeconds < 0.55) return;
                        if (GetForegroundWindow() != Handle)
                        {
                            if (clock.Elapsed.TotalSeconds > 1.2) Cancel("测试窗口未获得前台，未发送输入。");
                            return;
                        }
                        for (int vk = 1; vk < 255; vk++)
                            if ((GetAsyncKeyState(vk) & 0x8000) != 0) throw new InvalidOperationException("有键或鼠标按钮仍处于按下状态，未开始注入。");
                        originalCursor = Cursor.Position;
                        source.Snapshot(ClientScreenBounds(), clock.Elapsed.TotalSeconds, true, true);
                        BuildSteps();
                        active = true;
                        BeginStep();
                        return;
                    }
                    RequireForeground();
                    CheckUserActivity();
                    if (clock.Elapsed.TotalSeconds < nextCheck) return;
                    ProbeStep step = steps[stepIndex];
                    InputFrame frame = source.Snapshot(ClientScreenBounds(), clock.Elapsed.TotalSeconds, true, true);
                    int count = step.rawType == 1 ? rawKeyCount : rawMouseCount;
                    if (step.rawType >= 0 && count <= rawBeforeStep)
                        throw new InvalidOperationException(step.name + "：SendInput 后未收到对应 WM_INPUT；本系统的注入结果不能证明 Raw Input 链路。");
                    if (!step.verify(frame)) throw new InvalidOperationException(step.name + "：InputSource.Snapshot 与预期不一致。");
                    report.AppendLine("PASS " + step.name);
                    stepIndex++;
                    if (stepIndex == steps.Count)
                    {
                        report.AppendLine("PASS 原生 WM_INPUT 消息及 InputSource.Snapshot，键盘消息 " + rawKeyCount + "，鼠标消息 " + rawMouseCount + "。");
                        report.AppendLine("范围：Windows注入输入链路；未验证真实硬件输入、游戏兼容性或角色动画手感。");
                        Finish();
                    }
                    else BeginStep();
                }
                catch (Exception ex) { Fail(ex); }
            }

            private void BeginStep()
            {
                RequireForeground();
                ProbeStep step = steps[stepIndex];
                rawBeforeStep = step.rawType == 1 ? rawKeyCount : rawMouseCount;
                step.inject();
                nextCheck = clock.Elapsed.TotalSeconds + 0.14;
                Invalidate();
            }

            private void RequireForeground()
            {
                uint process;
                IntPtr foreground = GetForegroundWindow();
                GetWindowThreadProcessId(foreground, out process);
                if (foreground != Handle || process != (uint)Process.GetCurrentProcess().Id)
                {
                    interfered = true;
                    throw new InvalidOperationException("前台不再属于测试窗口，停止后续注入。");
                }
            }

            private void CheckUserActivity()
            {
                for (int vk = 1; vk < 255; vk++)
                {
                    if ((GetAsyncKeyState(vk) & 0x8000) == 0) continue;
                    bool ours = ownedKeys.ContainsKey(vk) || (vk == 13 && ownedKeys.ContainsKey(269)) ||
                                (vk == 16 && (ownedKeys.ContainsKey(160) || ownedKeys.ContainsKey(161))) ||
                                (vk == 1 && ownsLeft) || (vk == 2 && ownsRight);
                    if (!ours) { interfered = true; throw new InvalidOperationException("检测到额外键鼠操作，检查取消。"); }
                }
                if (cursorMoved)
                {
                    Point actual = Cursor.Position;
                    if (Math.Abs(actual.X - expectedCursor.X) > 2 || Math.Abs(actual.Y - expectedCursor.Y) > 2)
                    { interfered = true; throw new InvalidOperationException("检测到用户移动鼠标，检查取消且不恢复光标位置。"); }
                }
            }

            private Rectangle ClientScreenBounds() { return RectangleToScreen(ClientRectangle); }

            private bool MatchesCursor(InputFrame frame)
            {
                Rectangle bounds = ClientScreenBounds();
                float x = (expectedCursor.X - bounds.Left) * 2f / (bounds.Width - 1) - 1;
                float y = (expectedCursor.Y - bounds.Top) * 2f / (bounds.Height - 1) - 1;
                return Math.Abs(frame.cursorX - x) < 0.03 && Math.Abs(frame.cursorY - y) < 0.03;
            }

            private static bool HasEdge(InputFrame frame, int id)
            {
                foreach (int key in frame.pressedSinceSnapshot) if (key == id) return true;
                return false;
            }

            private void Key(int id, ushort scan, bool down)
            {
                RequireForeground();
                SendChecked(KeyboardEvent(id, scan, down));
                if (down) ownedKeys[id] = scan; else ownedKeys.Remove(id);
            }

            private void MoveInside(Point clientPoint)
            {
                RequireForeground();
                if (!ClientRectangle.Contains(clientPoint)) throw new InvalidOperationException("测试鼠标目标超出客户区。");
                expectedCursor = PointToScreen(clientPoint);
                Rectangle screen = SystemInformation.VirtualScreen;
                NativeInput input = MouseEvent(0x8000 | 0x4000 | 0x0001, 0);
                input.data.mouse.dx = (int)Math.Round((expectedCursor.X - screen.Left) * 65535.0 / (screen.Width - 1));
                input.data.mouse.dy = (int)Math.Round((expectedCursor.Y - screen.Top) * 65535.0 / (screen.Height - 1));
                SendChecked(input);
                cursorMoved = true;
            }

            private void RequireMouseInside()
            {
                RequireForeground();
                Point cursor = Cursor.Position;
                if (!ClientScreenBounds().Contains(cursor) || WindowFromPoint(new NativePoint { x = cursor.X, y = cursor.Y }) != Handle)
                { interfered = true; throw new InvalidOperationException("鼠标不在本测试客户区，停止点击或滚轮注入。"); }
            }

            private void MouseButton(bool left, bool down)
            {
                RequireMouseInside();
                uint flags = left ? (down ? 0x0002u : 0x0004u) : (down ? 0x0008u : 0x0010u);
                SendChecked(MouseEvent(flags, 0));
                if (left) ownsLeft = down; else ownsRight = down;
            }

            private void Wheel(int delta)
            {
                RequireMouseInside();
                SendChecked(MouseEvent(0x0800, unchecked((uint)delta)));
            }

            private void SendChecked(NativeInput input)
            {
                RequireForeground();
                if (SendInput(1, new NativeInput[] { input }, Marshal.SizeOf(typeof(NativeInput))) != 1)
                    throw new InvalidOperationException("Windows 拒绝注入测试输入，错误 " + Marshal.GetLastWin32Error() + "。");
            }

            public bool PreFilterMessage(ref Message message)
            {
                if (message.Msg == 0x00FF)
                {
                    uint size = (uint)(8 + IntPtr.Size * 2);
                    uint result = GetRawInputData(message.LParam, 0x10000005, rawHeader, ref size, size);
                    if (result != UInt32.MaxValue && result >= 8)
                    {
                        int type = BitConverter.ToInt32(rawHeader, 0);
                        if (type == 1) rawKeyCount++; else if (type == 0) rawMouseCount++;
                    }
                }
                return false;
            }

            protected override void WndProc(ref Message message)
            {
                int msg = message.Msg;
                bool inputDown = msg == 0x0100 || msg == 0x0104 || msg == 0x0201 || msg == 0x0204 || msg == 0x0207 || msg == 0x020A;
                if (active && !finished && inputDown && GetMessageExtraInfo().ToInt64() != InputTag)
                {
                    interfered = true;
                    BeginInvoke(new Action(delegate { Cancel("检测到非测试输入，检查取消。"); }));
                }
                base.WndProc(ref message);
            }

            private void Cancel(string reason)
            {
                interfered = true;
                Fail(new OperationCanceledException(reason));
            }

            private void Fail(Exception ex)
            {
                if (finished) return;
                Failure = ex;
                report.AppendLine("FAIL " + ex.Message);
                Finish();
            }

            private void Finish()
            {
                if (finished) return;
                finished = true;
                timer.Stop();
                // 清理仅发送本测试尚未释放的 key-up/button-up，绝不补发 down 或点击。
                // SendInput 是全局队列；若焦点已经改变，释放可能经过新前台，但不能留下系统粘键。
                foreach (KeyValuePair<int, ushort> key in ownedKeys)
                    ReleaseBestEffort(KeyboardEvent(key.Key, key.Value, false));
                ownedKeys.Clear();
                if (ownsLeft) ReleaseBestEffort(MouseEvent(0x0004, 0));
                if (ownsRight) ReleaseBestEffort(MouseEvent(0x0010, 0));
                ownsLeft = ownsRight = false;
                if (cursorMoved && !interfered && GetForegroundWindow() == Handle)
                    SetCursorPos(originalCursor.X, originalCursor.Y);
                if (filterAdded) { Application.RemoveMessageFilter(this); filterAdded = false; }
                if (source != null) { source.Dispose(); source = null; }
                Close();
            }

            private void ReleaseBestEffort(NativeInput input)
            {
                if (SendInput(1, new NativeInput[] { input }, Marshal.SizeOf(typeof(NativeInput))) != 1)
                    report.AppendLine("WARN Windows 未确认测试释放事件，错误 " + Marshal.GetLastWin32Error() + "。");
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    timer.Dispose();
                    if (filterAdded) Application.RemoveMessageFilter(this);
                    if (source != null) { source.Dispose(); source = null; }
                }
                base.Dispose(disposing);
            }
        }

        private const long InputTag = 0x44495950;

        private static NativeInput KeyboardEvent(int id, ushort scan, bool down)
        {
            NativeInput input = new NativeInput();
            input.type = 1;
            input.data.keyboard.scan = scan;
            input.data.keyboard.flags = 0x0008u | (id == 269 ? 0x0001u : 0) | (down ? 0 : 0x0002u);
            input.data.keyboard.extra = new UIntPtr((uint)InputTag);
            return input;
        }

        private static NativeInput MouseEvent(uint flags, uint data)
        {
            NativeInput input = new NativeInput();
            input.type = 0;
            input.data.mouse.flags = flags;
            input.data.mouse.mouseData = data;
            input.data.mouse.extra = new UIntPtr((uint)InputTag);
            return input;
        }

        [StructLayout(LayoutKind.Sequential)] private struct NativeInput { internal uint type; internal InputUnion data; }
        [StructLayout(LayoutKind.Explicit)] private struct InputUnion
        {
            [FieldOffset(0)] internal MouseInput mouse;
            [FieldOffset(0)] internal KeyboardInput keyboard;
        }
        [StructLayout(LayoutKind.Sequential)] private struct MouseInput
        {
            internal int dx, dy;
            internal uint mouseData, flags, time;
            internal UIntPtr extra;
        }
        [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput
        {
            internal ushort vk, scan;
            internal uint flags, time;
            internal UIntPtr extra;
        }
        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { internal int x, y; }
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, NativeInput[] inputs, int size);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] private static extern IntPtr GetMessageExtraInfo();
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputData(IntPtr input, uint command, byte[] data, ref uint size, uint headerSize);
    }
}
