using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;

namespace DieYing
{
    /** <summary>按已批准概念稿检查小键盘、固定圆爪活动区和真实鼠标映射。不证明美术或动态视频验收通过。</summary> */
    internal static class ModelAudit
    {
        private sealed class Report
        {
            internal readonly StringBuilder text = new StringBuilder();
            internal int passed, failed;
            internal void Check(bool condition, string name, string evidence)
            {
                if (condition) passed++; else failed++;
                text.AppendLine((condition ? "PASS " : "FAIL ") + name + ": " + evidence);
            }
            internal void Note(string value) { text.AppendLine("NOTE " + value); }
            internal string Finish(string name) { return name + " " + (failed == 0 ? "PASS" : "FAILED") + ": " + passed + " passed, " + failed + " failed." + Environment.NewLine + text; }
        }

        /**
         * <summary>检查两套角色存在、14 个常用键及固定圆爪动作、真实鼠标映射。</summary>
         * <param name="renderer">实际场景；不修改其美术资源。</param>
         * <returns>逐项失败报告；不能据此宣称角色还原或完整视频效果达标。</returns>
         */
        internal static string Run(SceneRenderer renderer)
        {
            if (renderer == null) throw new ArgumentNullException("renderer");
            Report report = new Report();
            report.Check(renderer.SkinCount == 4, "four-costume-forms", "count=" + renderer.SkinCount);
            AuditMotion(renderer.keyboard, report);
            report.Note("本次语义遵循已批准概念稿：14个常用键独立反馈，圆爪只在固定位置小幅轻敲，不承诺逐键触达。鼠标按绝对位置小范围映射。旧104键及长手臂审计已撤销。");
            report.Note("本报告仅检查动作状态与活动范围；圆爪实际覆盖位置、人物还原、透明边缘及完整动态视频仍须独立核对。");
            return report.Finish("MODEL AUDIT");
        }

        /** <summary>单独运行动作状态审计，不加载角色素材。</summary> */
        internal static string RunMotionTests()
        {
            Report report = new Report();
            AuditMotion(new KeyboardModel(), report);
            return report.Finish("MOTION AUDIT");
        }

        private static void AuditMotion(KeyboardModel keyboard, Report report)
        {
            int[] expectedCaps = { 27, 81, 87, 69, 82, 9, 65, 83, 68, 70, 162, 160, 32, 13 };
            HashSet<int> actual = new HashSet<int>();
            foreach (KeyCap cap in keyboard.Keys) actual.Add(cap.id);
            bool expectedLayout = keyboard.Keys.Count == 14 && actual.SetEquals(expectedCaps);
            report.Check(expectedLayout, "keyboard.14-common-caps", "count=" + keyboard.Keys.Count + "; Esc/Q/W/E/R/Tab/A/S/D/F/Ctrl/Shift/Space/Enter");
            if (!expectedLayout) report.Note("布局未完成，后续检查会按当前实际键帽运行，不以旧104键目标代替新要求。");

            AppSettings settings = new AppSettings { idleMotion = false, motionSpeed = 18 };
            MotionState idle = new MotionState(); idle.Update(Frame(0, 0, 0, new int[0], new int[0]), settings, keyboard, 0);
            report.Check(Distance(idle.keyTip, new PointF(549, 547)) < .001 && idle.keyPress == 0, "paw.idle-contact", "actual=" + Point(idle.keyTip) + "; expected=(549,547)");

            bool finite = true, inEnvelope = true, allTap = true, allRelease = true, exactCapFeedback = true;
            double largestX = 0, lowestY = Double.MaxValue, highestY = Double.MinValue;
            foreach (KeyCap cap in keyboard.Keys)
            {
                MotionState motion = new MotionState(); motion.Update(Frame(0, 0, 0, new int[0], new int[0]), settings, keyboard, 0);
                InputFrame frame = Frame(1, cap.id, cap.id, new int[] { cap.id }, new int[] { cap.id });
                bool tapped = false;
                for (int step = 1; step <= 72; step++)
                {
                    motion.Update(frame, settings, keyboard, step / 120.0); frame.pressedSinceSnapshot = new int[0];
                    float renderedY = motion.keyTip.Y + 2 * motion.keyPress;
                    finite &= Finite(motion.keyTip) && !Single.IsNaN(motion.keyPress);
                    largestX = Math.Max(largestX, Math.Abs(motion.keyTip.X - 549)); lowestY = Math.Min(lowestY, renderedY); highestY = Math.Max(highestY, renderedY);
                    inEnvelope &= motion.keyTip.X >= 539 - .001 && motion.keyTip.X <= 559 + .001 && renderedY >= 547 - .001 && renderedY <= 556 + .001;
                    tapped |= motion.targetKey == cap.id && motion.keyPress > .5;
                    foreach (KeyCap other in keyboard.Keys) if (motion.litKeys[other.id] != (other.id == cap.id)) exactCapFeedback = false;
                }
                allTap &= tapped;
                frame = Frame(1, cap.id, cap.id, new int[0], new int[0]);
                for (int step = 73; step <= 168; step++) motion.Update(frame, settings, keyboard, step / 120.0);
                allRelease &= motion.keyPress < .01 && Distance(motion.keyTip, new PointF(549, 547)) < .1;
                foreach (KeyCap other in keyboard.Keys) if (motion.litKeys[other.id]) allRelease = false;
            }
            report.Check(finite && inEnvelope, "paw.fixed-small-envelope", "max |x-549|=" + F(largestX) + "; rendered y=" + F(lowestY) + ".." + F(highestY) + "; permitted x539..559,y547..556");
            report.Check(allTap, "paw.common-keys-produce-small-tap", "每个可见常用键均触发轻敲；不检查逐键触达");
            report.Check(exactCapFeedback, "keyboard.only-physical-cap-lights", "逐个按住时仅对应独立键帽亮起");
            report.Check(allRelease, "paw.release-restores-idle", "松开后轻敲和键帽恢复，圆爪回(549,547)");

            bool aliasesCorrect = true;
            foreach (int alias in new int[] { 16, 161, 17, 163, 269 })
            {
                KeyCap cap = keyboard.Find(alias); int expected = alias == 16 || alias == 161 ? 160 : alias == 269 ? 13 : 162;
                if (cap == null || cap.id != expected) { aliasesCorrect = false; continue; }
                MotionState motion = new MotionState(); motion.Update(Frame(0, 0, 0, new int[0], new int[0]), settings, keyboard, 0);
                InputFrame frame = Frame(1, alias, alias, new int[] { alias }, new int[] { alias });
                for (int step = 1; step <= 48; step++) { motion.Update(frame, settings, keyboard, step / 120.0); frame.pressedSinceSnapshot = new int[0]; }
                aliasesCorrect &= motion.litKeys[expected] && motion.targetKey == expected && motion.keyPress > .5;
            }
            report.Check(aliasesCorrect, "keyboard.modifier-and-enter-aliases", "左右Ctrl/Shift和数字区Enter映射到各自已有键帽");

            MotionState unknown = new MotionState(); unknown.Update(Frame(0, 0, 0, new int[0], new int[0]), settings, keyboard, 0);
            InputFrame unknownFrame = Frame(1, 112, 112, new int[] { 112 }, new int[] { 112 }); unknownFrame.bubble = "F1";
            bool unknownStill = keyboard.Find(112) == null, unknownActed = false, unknownLit = false, bubbleVisible = false;
            int unknownAction = keyboard.ResolveActionKey(112);
            for (int step = 1; step <= 90; step++)
            {
                unknown.Update(unknownFrame, settings, keyboard, step / 120.0); unknownFrame.pressedSinceSnapshot = new int[0];
                unknownActed |= unknown.keyPress > .001 || unknown.targetKey == unknownAction;
                unknownStill &= Distance(unknown.keyTip, new PointF(549, 547)) < 30;
                unknownLit |= unknown.litKeys[unknownAction];
                bubbleVisible |= unknown.bubble == "F1";
            }
            report.Check(unknownStill && unknownActed && unknownLit && bubbleVisible, "keyboard.unlisted-key-taps", "F1等未单独绘制键帽的按键也会触发圆爪动作，并保留气泡提示");

            foreach (int fps in new int[] { 30, 60, 120 })
            {
                MotionState tap = new MotionState(); tap.Update(Frame(0, 0, 0, new int[0], new int[0]), settings, keyboard, 0);
                InputFrame frame = Frame(3, 13, 13, new int[0], new int[] { 65, 32, 13 });
                HashSet<int> tapped = new HashSet<int>(); bool initialLights = false;
                for (int step = 1; step <= fps; step++)
                {
                    tap.Update(frame, settings, keyboard, step / (double)fps); frame.pressedSinceSnapshot = new int[0];
                    if (step == 1) initialLights = tap.litKeys[65] && tap.litKeys[32] && tap.litKeys[13];
                    if (tap.keyPress > .5 && tap.keyTip.Y >= 549) tapped.Add(tap.targetKey);
                }
                report.Check(initialLights && tapped.Contains(65) && tapped.Contains(32) && tapped.Contains(13), "paw.short-edge-feedback-" + fps + "fps", "A/Space/Enter短按各有键帽反馈和轻敲，不要求爪追到键帽坐标");
            }

            MotionState chord = new MotionState(); chord.Update(Frame(0, 0, 0, new int[0], new int[0]), settings, keyboard, 0);
            InputFrame chordFrame = Frame(2, 83, 83, new int[] { 65, 83 }, new int[] { 65, 83 });
            for (int step = 1; step <= 72; step++) { chord.Update(chordFrame, settings, keyboard, step / 120.0); chordFrame.pressedSinceSnapshot = new int[0]; }
            bool bothLit = chord.litKeys[65] && chord.litKeys[83];
            chordFrame = Frame(2, 83, 65, new int[] { 65 }, new int[0]);
            for (int step = 73; step <= 120; step++) chord.Update(chordFrame, settings, keyboard, step / 120.0);
            report.Check(bothLit && chord.targetKey == 65 && chord.litKeys[65] && !chord.litKeys[83] && chord.keyPress > .5, "paw.common-chord-fallback", "A+S均亮；松开S后仍响应A");

            foreach (bool pause in new bool[] { false, true })
            {
                MotionState motion = new MotionState(); motion.Update(Frame(0, 0, 0, new int[0], new int[0]), settings, keyboard, 0);
                InputFrame frame = Frame(3, 13, 13, new int[0], new int[] { 65, 32, 13 }); motion.Update(frame, settings, keyboard, .01);
                if (pause) settings.paused = true; else settings.keyboardEnabled = false;
                frame.pressedSinceSnapshot = new int[0]; motion.Update(frame, settings, keyboard, .02);
                settings.paused = false; settings.keyboardEnabled = true;
                double maximum = 0;
                for (int step = 4; step <= 120; step++) { motion.Update(frame, settings, keyboard, step / 120.0); maximum = Math.Max(maximum, motion.keyPress); }
                report.Check(maximum < .01, pause ? "paw.pause-discards-old-taps" : "paw.disable-discards-old-taps", "恢复后无新输入，最大轻敲=" + F(maximum));
            }

            bool mouseMapped = true, mouseHeld = true, mouseInRange = true;
            double largestMouseError = 0;
            float[] ranges = { .25f, 1, 1.5f }, xExtent = { 4.5f, 18, 24 }, yExtent = { 2.5f, 10, 14 };
            for (int rangeIndex = 0; rangeIndex < ranges.Length; rangeIndex++)
            {
                settings.mouseRange = ranges[rangeIndex];
                for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++)
                {
                    MotionState motion = new MotionState(); InputFrame frame = Frame(0, 0, 0, new int[0], new int[0]); frame.cursorX = x; frame.cursorY = y;
                    for (int step = 0; step <= 120; step++)
                    {
                        motion.Update(frame, settings, keyboard, step / 120.0);
                        mouseInRange &= motion.mouseCenter.X >= 216 - .001 && motion.mouseCenter.X <= 264 + .001 && motion.mouseCenter.Y >= 541 - .001 && motion.mouseCenter.Y <= 569 + .001;
                    }
                    PointF expected = new PointF(240 + x * xExtent[rangeIndex], 555 + y * yExtent[rangeIndex]);
                    double error = Distance(motion.mouseCenter, expected); largestMouseError = Math.Max(largestMouseError, error); mouseMapped &= error <= .05;
                    PointF stopped = motion.mouseCenter;
                    for (int step = 121; step <= 240; step++) motion.Update(frame, settings, keyboard, step / 120.0);
                    mouseHeld &= Distance(motion.mouseCenter, stopped) <= .05;
                }
            }
            report.Check(mouseMapped && mouseInRange, "mouse.real-absolute-small-mapping", "27个中心/边缘/角点，三种范围；最大误差=" + F(largestMouseError) + "px；x216..264,y541..569");
            report.Check(mouseHeld, "mouse.stop-does-not-recenter", "原地保持一秒不回中");

            settings.mouseRange = 1;
            MotionState mouse = new MotionState(); InputFrame mouseFrame = Frame(3, 13, 13, new int[0], new int[] { 65, 32, 13 });
            mouseFrame.cursorX = 1; mouseFrame.cursorY = -1; mouseFrame.leftButton = true; mouseFrame.rightButton = true; mouseFrame.middleButton = true; mouseFrame.wheelDelta = 120;
            mouse.Update(mouseFrame, settings, keyboard, 0); mouse.Update(mouseFrame, settings, keyboard, 1 / 60.0);
            bool buttons = mouse.leftButton && mouse.rightButton && mouse.middleButton && mouse.wheel > .5;
            mouseFrame.wheelDelta = 0; mouseFrame.pressedSinceSnapshot = new int[0];
            for (int step = 2; step <= 60; step++) mouse.Update(mouseFrame, settings, keyboard, step / 60.0);
            report.Check(buttons && Distance(mouse.mouseCenter, new PointF(258, 545)) < .05, "mouse.concurrent-buttons-wheel-and-motion", "键盘轻敲期间按钮、滚轮和移动独立响应");
            PointF frozen = mouse.mouseCenter; settings.mouseEnabled = false; mouseFrame.cursorX = -1; mouseFrame.wheelDelta = 120;
            mouse.Update(mouseFrame, settings, keyboard, 1.1);
            report.Check(!mouse.leftButton && !mouse.rightButton && !mouse.middleButton && mouse.wheel == 0 && Distance(mouse.mouseCenter, frozen) < .001,
                "mouse.disable-freezes-without-feedback", "禁用后位置保持、按钮和滚轮停止");

            report.Note("鼠标圆手的绘制底缘应为 mouseCenter+(0,-8)，这属于场景渲染校验；本状态测试不把字段计算冒充实际像素位置证明。");
        }

        private static InputFrame Frame(long sequence, int last, int target, int[] held, int[] pressed)
        {
            InputFrame frame = new InputFrame { keySequence = sequence, lastPressedKey = last, targetKey = target, pressedSinceSnapshot = pressed };
            foreach (int key in held) frame.heldKeys[key] = true;
            frame.hasKeyboardTarget = held.Length != 0; return frame;
        }
        private static bool Finite(PointF point) { return !Single.IsNaN(point.X) && !Single.IsNaN(point.Y) && !Single.IsInfinity(point.X) && !Single.IsInfinity(point.Y); }
        private static double Distance(PointF a, PointF b) { return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)); }
        private static string F(double value) { return value.ToString("0.000", CultureInfo.InvariantCulture); }
        private static string Point(PointF point) { return "(" + F(point.X) + "," + F(point.Y) + ")"; }
    }
}
