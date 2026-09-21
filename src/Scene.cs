using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DieYing
{
    internal sealed class MotionState
    {
        internal PointF keyTip, mouseCenter;
        internal float keyPress, mousePress, leanX, leanY, headTilt, hairSwing, hairLift;
        internal float eyeX, eyeY, blinkAmount, flowerLeft, flowerRight, idleWeight;
        internal float browLeftRise, browRightRise, browLeftTilt, browRightTilt;
        private float flowerVelocityLeft, flowerVelocityRight, previousTilt;
        internal bool blink;
        internal readonly AutoReaction reaction = new AutoReaction();
        internal float sleepBreath;
        internal bool Sleeping { get { return reaction.expression == AutoReaction.SleepExpression; } }
        internal double time;
        internal string bubble = "";
        internal int targetKey;
        internal readonly bool[] litKeys = new bool[512];
        internal bool leftButton, rightButton, middleButton;
        internal float wheel;
        private PointF start, destination;
        private double moveStart = -1, moveDuration, pressUntil, lastInput, blinkStart = 2.5, previousTime;
        private readonly double[] lightUntil = new double[512];
        private long sequence = -1;
        private bool initialized, resting, wasPaused, keyboardWasEnabled = true;
        private const int MaxPendingStrikes = 6;
        private const double MaxPendingAge = .45;
        private readonly Queue<PendingStrike> pendingStrikes = new Queue<PendingStrike>();
        private double activeStrikeTime;

        private struct PendingStrike
        {
            internal int key;
            internal double time;
            internal PendingStrike(int key, double time) { this.key = key; this.time = time; }
        }

        internal void Update(InputFrame frame, AppSettings settings, KeyboardModel keyboard, double now)
        {
            reaction.Update(frame, settings.paused, settings.keyboardEnabled, now);
            if (reaction.returnedToNatural) settings.expression = 0;
            float dt = (float)Math.Max(.001, Math.Min(.06, now - previousTime)); previousTime = now;
            if (!initialized) { keyTip = Home(); mouseCenter = new PointF(240, 555); initialized = true; resting = true; }
            if (settings.paused)
            {
                pendingStrikes.Clear(); Array.Clear(litKeys, 0, litKeys.Length); Array.Clear(lightUntil, 0, lightUntil.Length);
                keyPress = mousePress = wheel = 0; leftButton = rightButton = middleButton = false; bubble = "";
                sequence = frame.keySequence; moveStart = -1; pressUntil = 0; lastInput = now; wasPaused = true;
                keyboardWasEnabled = settings.keyboardEnabled; return;
            }
            time += dt;
            int mood=reaction.expression>=0?reaction.expression:settings.expression;
            float browAim=mood==3?-3:mood==2?-1.5f:mood==5?1:0;
            float browTilt=mood==5?(settings.skin==0?8:3):mood==4?3:0;
            float browBlend=1-(float)Math.Exp(-dt*8);
            browLeftRise+=(browAim-browLeftRise)*browBlend;
            browRightRise+=(browAim-browRightRise)*browBlend;
            browLeftTilt+=(-browTilt-browLeftTilt)*browBlend;
            browRightTilt+=(browTilt-browRightTilt)*browBlend;
            bool channelChanged = wasPaused || settings.keyboardEnabled != keyboardWasEnabled;
            if (channelChanged)
            {
                pendingStrikes.Clear(); sequence = frame.keySequence; keyPress = 0; pressUntil = 0;
                Array.Clear(lightUntil, 0, lightUntil.Length);
                StartMove(Home(), 0, now, settings.motionSpeed, true, false);
                wasPaused = false;
            }
            keyboardWasEnabled = settings.keyboardEnabled;
            if (!settings.keyboardEnabled) sequence = frame.keySequence;
            if (settings.keyboardEnabled && frame.keySequence != sequence)
            {
                bool any = false;
                if (frame.pressedSinceSnapshot != null)
                {
                    // 输入层仅提供新 down 边沿；同一序号的重复快照不能再排队。
                    // 相同键的真实 down/up/down 仍保留，不能按键号去重而吞掉双击。
                    foreach (int key in frame.pressedSinceSnapshot)
                    {
                        int actionKey = keyboard.ResolveActionKey(key);
                        if (actionKey == 0) continue;
                        lightUntil[actionKey] = now + .13;
                        if (pendingStrikes.Count >= MaxPendingStrikes) pendingStrikes.Dequeue();
                        pendingStrikes.Enqueue(new PendingStrike(actionKey, now)); any = true;
                    }
                }
                else if (sequence >= 0)
                {
                    // 兼容未提供边沿数组的调用方；正常 Raw Input 路径始终使用上面的数组。
                    int actionKey = keyboard.ResolveActionKey(frame.lastPressedKey);
                    if (actionKey == 0) { sequence = frame.keySequence; }
                    else
                    {
                    if (pendingStrikes.Count >= MaxPendingStrikes) pendingStrikes.Dequeue();
                    pendingStrikes.Enqueue(new PendingStrike(actionKey, now));
                    lightUntil[actionKey] = now + .13; any = true;
                    }
                }
                sequence = frame.keySequence;
                if (any) lastInput = now;
            }

            while (pendingStrikes.Count > 0 && now - pendingStrikes.Peek().time > MaxPendingAge) pendingStrikes.Dequeue();
            int heldTarget = FindHeldTarget(frame, keyboard);
            if (settings.keyboardEnabled && resting && (pendingStrikes.Count > 0 || heldTarget != 0))
            { moveStart = -1; pressUntil = 0; }
            bool held = IsHeld(frame, keyboard, targetKey);
            // UI 长帧后不播放早已释放的陈旧落键；仍按住的目标必须保持。
            if (!resting && moveStart >= 0 && now - activeStrikeTime > MaxPendingAge + .24 && !held)
            { moveStart = -1; pressUntil = 0; keyPress = 0; }

            if (moveStart >= 0)
            {
                float progress = V.Clamp((float)((now - moveStart) / moveDuration), 0, 1);
                keyTip = V.Lerp(start, destination, V.Smooth(progress));
                keyTip.Y = Math.Max(547, keyTip.Y - (float)Math.Sin(progress * Math.PI) * (resting ? 1 : 1.5f));
                if (progress >= 1)
                {
                    moveStart = -1;
                    pressUntil = resting ? 0 : now + (pendingStrikes.Count > 0 ? .04 : .07);
                    if (!resting) lightUntil[targetKey] = Math.Max(lightUntil[targetKey], pressUntil);
                }
            }

            // 一个落键必须先完成抬手、移动和短暂接触，才开始下一个；鼠标更新不等待此队列。
            if (settings.keyboardEnabled && moveStart < 0 && now >= pressUntil)
            {
                if (pendingStrikes.Count > 0)
                {
                    PendingStrike next = pendingStrikes.Dequeue();
                    StartMove(HandTarget(keyboard, next.key), next.key, now, settings.motionSpeed, false, pendingStrikes.Count > 0);
                    activeStrikeTime = next.time;
                }
                else if (heldTarget != 0 && (heldTarget != targetKey || resting))
                {
                    StartMove(HandTarget(keyboard, heldTarget), heldTarget, now, settings.motionSpeed, false, false);
                    activeStrikeTime = now;
                }
            }
            if (heldTarget == 0 && pendingStrikes.Count == 0 && moveStart < 0 && now >= pressUntil && now - lastInput > .4 && !resting)
                StartMove(Home(), 0, now, settings.motionSpeed, true, false);

            held = IsHeld(frame, keyboard, targetKey);
            float pressAim = !resting && moveStart < 0 && settings.keyboardEnabled && (held || now < pressUntil) ? 1 : 0;
            keyPress += (pressAim - keyPress) * Math.Min(1, dt * 32);
            PointF mouseAim = new PointF(240 + V.Clamp(frame.cursorX * 18 * settings.mouseRange, -24, 24), 555 + V.Clamp(frame.cursorY * 10 * settings.mouseRange, -14, 14));
            if (settings.mouseEnabled) mouseCenter = V.Lerp(mouseCenter, mouseAim, 1 - (float)Math.Exp(-settings.motionSpeed * dt));
            leftButton = settings.mouseEnabled && frame.leftButton; rightButton = settings.mouseEnabled && frame.rightButton; middleButton = settings.mouseEnabled && frame.middleButton;
            mousePress += (((leftButton || rightButton) ? 1 : 0) - mousePress) * Math.Min(1, dt * 35);
            wheel = settings.mouseEnabled ? wheel * (float)Math.Exp(-dt * 9) + frame.wheelDelta / 120f : 0;
            float follow = settings.mouseEnabled ? V.Clamp(frame.cursorX, -1, 1) : 0;
            idleWeight = settings.idleMotion ? 1 : 0;
            eyeX += (follow * 3.2f - eyeX) * (1 - (float)Math.Exp(-dt * 9));
            eyeY += ((settings.mouseEnabled ? V.Clamp(frame.cursorY,-1,1) * 1.7f : 0) - eyeY) * (1 - (float)Math.Exp(-dt * 9));
            float torsoAim = follow * 8;
            leanX += (torsoAim - leanX) * (1 - (float)Math.Exp(-dt * 6));
            sleepBreath = (float)Math.Sin(time * 1.15);
            float breathe = Sleeping ? sleepBreath : settings.idleMotion ? (float)Math.Sin(time * 1.65) : 0;
            float yAim = breathe * (Sleeping ? 4.2f : 2.8f) + (settings.mouseEnabled ? frame.cursorY * 2.4f : 0) + keyPress * 1.4f;
            leanY += (yAim - leanY) * (1 - (float)Math.Exp(-dt * 7));
            float tiltAim = Sleeping ? 1.8f + sleepBreath * .5f : follow * 1.8f + (settings.idleMotion ? (float)Math.Sin(time * .85) * .6f : 0);
            headTilt += (tiltAim - headTilt) * (1 - (float)Math.Exp(-dt * 5));
            float tiltSpeed = (headTilt - previousTilt) / dt; previousTilt = headTilt;
            Spring(ref flowerLeft, ref flowerVelocityLeft, -tiltSpeed * .75f + idleWeight * (float)Math.Sin(time * 2.2) * 2.4f, dt);
            Spring(ref flowerRight, ref flowerVelocityRight, -tiltSpeed * .60f + idleWeight * (float)Math.Sin(time * 2.05 + .8) * 2.4f, dt);
            hairSwing += ((-leanX * .7f + (settings.idleMotion ? (float)Math.Sin(time * 1.65 - .65) * 4 : 0)) - hairSwing) * (1 - (float)Math.Exp(-dt * 3));
            hairLift = breathe * 1.5f;
            if (now - blinkStart > .17) blinkStart = now + 3.1 + .7 * Math.Sin(now * .31);
            blink = settings.expression == 1 || settings.idleMotion && now >= blinkStart && now < blinkStart + .17;
            float blinkPhase = (float)((now - blinkStart) / .17);
            blinkAmount = settings.expression == 1 ? 1 : blink && blinkPhase >= 0 && blinkPhase <= 1 ? (float)Math.Sin(blinkPhase * Math.PI) : 0;
            bubble = settings.showKeyBubble ? frame.bubble : "";
            Array.Clear(litKeys, 0, litKeys.Length);
            if (settings.keyboardEnabled)
                for (int i = 0; i < litKeys.Length; ++i)
                {
                    if (!(i < frame.heldKeys.Length && frame.heldKeys[i]) && now >= lightUntil[i]) continue;
                    KeyCap cap = keyboard.Find(i);
                    if (cap != null) { litKeys[i] = true; litKeys[cap.id] = true; }
                }
        }

        private static void Spring(ref float angle, ref float velocity, float target, float dt)
        {
            // 分步积分避免窗口短暂阻塞后吊饰突然弹飞；阻尼保留一次轻微回弹。
            int steps = Math.Max(1, (int)Math.Ceiling(dt / .008)); float step = dt / steps;
            for(int i=0;i<steps;i++) { velocity += ((target-angle)*65 - velocity*9)*step; angle += velocity*step; }
            angle = V.Clamp(angle,-7,7);
        }

        private static bool IsHeld(InputFrame frame, KeyboardModel keyboard, int key)
        {
            if (key <= 0) return false;
            int wanted = keyboard.ResolveActionKey(key);
            for (int i = 1; i < frame.heldKeys.Length; i++)
            {
                if (!frame.heldKeys[i]) continue;
                if (keyboard.ResolveActionKey(i) == wanted) return true;
            }
            return false;
        }
        private int FindHeldTarget(InputFrame frame, KeyboardModel keyboard)
        {
            int latest = keyboard.ResolveActionKey(frame.targetKey);
            if (latest != 0 && IsHeld(frame, keyboard, latest)) return latest;
            if (IsHeld(frame, keyboard, targetKey)) return targetKey;
            for (int key = 1; key < frame.heldKeys.Length; key++)
                if (frame.heldKeys[key]) return keyboard.ResolveActionKey(key);
            return 0;
        }
        private static PointF Home() { return new PointF(549, 547); }
        private static PointF HandTarget(KeyboardModel keyboard, int key)
        {
            KeyCap cap = keyboard.Find(key);
            if (cap == null) return Home();
            // 键盘手只在原位轻敲，键位反馈由键帽独立显示。
            return new PointF(549 + V.Clamp((cap.center.X-549)*.12f,-10,10),552 + V.Clamp((cap.center.Y-552)*.1f,-2,2));
        }

        private void StartMove(PointF point, int key, double now, float speed, bool isRest, bool catchingUp)
        {
            start = keyTip; destination = point; moveStart = now; targetKey = key; resting = isRest;
            // 有积压时缩短单步，但始终给接触阶段独立的可见时间，不能跳过前面的键。
            moveDuration = Math.Min(catchingUp ? .105 : .24, Math.Max(catchingUp ? .045 : .065, V.Len(V.Sub(point, start)) / (speed * 75)));
            lastInput = now;
        }
        internal void SetReviewPose(KeyboardModel keyboard, int key, PointF cursor, bool pressed, double now)
        {
            KeyCap cap = keyboard.Find(key);
            time = now; targetKey = cap == null ? 0 : cap.id; keyTip = pressed && cap != null ? HandTarget(keyboard, key) : Home(); keyPress = pressed && cap != null ? 1 : 0;
            mouseCenter = new PointF(240 + V.Clamp(cursor.X * 18, -24, 24), 555 + V.Clamp(cursor.Y * 10, -14, 14)); leftButton = pressed; mousePress = pressed ? 1 : 0;
            for (int i = 0; i < litKeys.Length; ++i) litKeys[i] = pressed && cap != null && i == cap.id;
        }
    }

    /** <summary>完整角色构图，叠加独立键鼠、原位轻敲和鼠标握持动画。</summary> */
    internal sealed class SceneRenderer : IDisposable
    {
        internal readonly KeyboardModel keyboard = new KeyboardModel();
        private readonly LayeredCharacterRig characterRig;
        internal int SkinCount { get { return SkinCatalog.Count; } }
        internal string RendererDevice { get { return characterRig.Device; } }
        internal bool debugRig;
        internal SceneRenderer(string assetDirectory)
        {
            characterRig=new LayeredCharacterRig(System.IO.Path.Combine(assetDirectory,"zhu"));
        }
        internal void Draw(Graphics g, AppSettings settings, MotionState pose)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            GraphicsState scene = g.Save();
            if (settings.mirror) { g.TranslateTransform(800, 0); g.ScaleTransform(-1, 1); }
            PointF mouseContact = new PointF(pose.mouseCenter.X, pose.mouseCenter.Y - 8 + pose.mousePress * 1.3f);
            PointF keyContact = new PointF(pose.keyTip.X, pose.keyTip.Y + pose.keyPress * 2);
            characterRig.Draw(g,settings.skin,pose,pose.reaction.expression>=0?pose.reaction.expression:settings.expression,keyboard,settings.keyLabels);
            if (debugRig)
            {
                using (Pen marker = new Pen(Color.Magenta, 1.3f))
                    foreach (PointF p in new PointF[] { mouseContact, keyContact })
                    { g.DrawLine(marker, p.X - 6, p.Y, p.X + 6, p.Y); g.DrawLine(marker, p.X, p.Y - 6, p.X, p.Y + 6); }
            }
            if (!String.IsNullOrEmpty(pose.bubble) && settings.showKeyBubble) DrawBubble(g, pose.bubble);
            g.Restore(scene);
            if (pose.Sleeping) DrawSleep(g, pose, settings.mirror);
        }
        private static void DrawSleep(Graphics g, MotionState pose, bool mirror)
        {
            // 与身体共用呼吸相位；镜像只改变位置，Z 字形保持正向。
            PointF anchor = SurfaceRig.Transform(new PointF(650, 170), pose);
            if (mirror) anchor.X = 800 - anchor.X - 70;
            GraphicsState state = g.Save();
            g.TranslateTransform(anchor.X, anchor.Y + pose.sleepBreath * 7);
            using (FontFamily family = new FontFamily("Comic Sans MS"))
            using (Pen edge = new Pen(Color.FromArgb(217, 185, 129), 10) { LineJoin = LineJoin.Round })
            using (Pen outline = new Pen(Color.FromArgb(255, 247, 220), 8) { LineJoin = LineJoin.Round })
                for (int i = 0; i < 3; i++)
                    using (GraphicsPath glyph = new GraphicsPath())
                    {
                        glyph.AddString("Z", family, (int)FontStyle.Bold, 26 + i * 11,
                            new PointF(i * 26, -i * 31), StringFormat.GenericTypographic);
                        RectangleF bounds = glyph.GetBounds();
                        using (Matrix tilt = new Matrix())
                        {
                            tilt.RotateAt(i == 1 ? 8 : -8, new PointF(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2));
                            glyph.Transform(tilt);
                        }
                        bounds = glyph.GetBounds(); bounds.Inflate(4, 4);
                        using (var blue = new LinearGradientBrush(bounds, Color.FromArgb(189, 225, 241), Color.FromArgb(119, 169, 215), 90f))
                        using (var weight = new Pen(blue, 4.5f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
                        {
                            g.DrawPath(edge, glyph); g.DrawPath(outline, glyph);
                            g.DrawPath(weight, glyph); g.FillPath(blue, glyph);
                        }
                    }
            g.Restore(state);
        }
        private static void DrawBubble(Graphics g, string text)
        {
            using (Font font = new Font("Microsoft YaHei UI", 14, FontStyle.Bold))
            using (Brush ink = new SolidBrush(Color.FromArgb(89, 62, 50)))
            using (Brush fill = new SolidBrush(Color.FromArgb(245, 255, 250, 231)))
            using (Pen outline = new Pen(Color.FromArgb(208, 169, 103), 2))
            {
                SizeF measured = g.MeasureString(text, font); RectangleF rect = new RectangleF(68, 54, Math.Min(250, measured.Width + 28), 44);
                using (var path = RoundRect(rect, 17)) { g.FillPath(fill, path); g.DrawPath(outline, path); }
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) g.DrawString(text, font, ink, rect, format);
            }
        }
        internal static GraphicsPath RoundRect(RectangleF rect, float radius)
        {
            var p = new GraphicsPath(); float d = Math.Min(Math.Min(rect.Width, rect.Height), radius * 2);
            p.AddArc(rect.X, rect.Y, d, d, 180, 90); p.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            p.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90); p.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
        }
        public void Dispose() { characterRig.Dispose(); }
    }
}
