using System;
using System.Collections.Generic;

namespace DieYing
{
    /** <summary>只保存按键时间，不保存输入内容；由 UI 线程推进的自动表情状态机。</summary> */
    internal sealed class AutoReaction
    {
        internal const int SleepExpression = 5;
        internal const double IdleSeconds = 120;
        internal const int DizzyPressCount = 20;
        internal const int RecoveryPressCount = 10;
        internal int expression = -1;
        internal bool returnedToNatural;
        private readonly Queue<double> presses = new Queue<double>();
        private double lastActive, previousTime, slowSince = -1;
        private long sequence = -1;
        private bool initialized, paused;

        internal void Update(InputFrame frame, bool isPaused, bool keyboardEnabled, double now)
        {
            returnedToNatural = false;
            if (!initialized || now < previousTime || isPaused || paused)
            {
                initialized = true; lastActive = now; presses.Clear(); slowSince = -1;
                if (expression >= 0) returnedToNatural = true;
                expression = -1; sequence = frame.keySequence;
                paused = isPaused; previousTime = now; return;
            }
            previousTime = now;
            bool held = frame.leftButton || frame.rightButton || frame.middleButton || frame.x1Button || frame.x2Button;
            foreach (bool key in frame.heldKeys) held |= key;
            bool newKeys = frame.keySequence != sequence;
            if (held || newKeys || frame.wheelDelta != 0) lastActive = now;
            lastActive = Math.Max(lastActive, Math.Min(now, frame.lastActivity));
            if (keyboardEnabled && newKeys && frame.pressedSinceSnapshot != null)
                foreach (int key in frame.pressedSinceSnapshot)
                {
                    // 修饰键不算打字速度；长按自动重复已由输入层去除。
                    if (key == 16 || key == 17 || key == 18 || key >= 160 && key <= 165 || key == 91 || key == 92) continue;
                    if (presses.Count == 128) presses.Dequeue();
                    presses.Enqueue(now);
                }
            sequence = frame.keySequence;
            while (presses.Count > 0 && now - presses.Peek() >= 2) presses.Dequeue();
            if (!keyboardEnabled) presses.Clear();
            if (expression == SleepExpression && now - lastActive < IdleSeconds)
            {
                expression = -1; returnedToNatural = true; presses.Clear(); slowSince = -1;
                return; // 唤醒这一帧明确显示自然，不被积累按键立即覆盖。
            }
            if (now - lastActive >= IdleSeconds)
            {
                expression = SleepExpression; presses.Clear(); slowSince = -1; return;
            }
            if (expression == 4)
            {
                if (presses.Count < RecoveryPressCount)
                {
                    if (slowSince < 0) slowSince = now;
                    if (now - slowSince >= 1.2) { expression = -1; returnedToNatural = true; slowSince = -1; }
                }
                else slowSince = -1;
            }
            else if (presses.Count >= DizzyPressCount) { expression = 4; slowSince = -1; }
        }
    }
}
