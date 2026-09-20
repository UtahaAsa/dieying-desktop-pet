using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace DieYing
{
    internal sealed class KeyCap
    {
        internal int id;
        internal string label;
        internal PointF[] polygon;
        internal PointF center;
        internal RectangleF logicalRect;
    }

    /// <summary>贴近角色的小型14键键盘；键帽、按压及输入别名共用同一模型。</summary>
    internal sealed class KeyboardModel
    {
        private const float logicalWidth = 5f;
        private const float logicalHeight = 3f;
        private readonly List<KeyCap> keys = new List<KeyCap>();
        private readonly Dictionary<int, KeyCap> byId = new Dictionary<int, KeyCap>();
        private readonly IList<KeyCap> readOnlyKeys;

        internal KeyboardModel()
        {
            Add(27, "Esc", 0, 0, 1, 1);
            AddLetters("QWER", 1, 0);
            Add(9, "Tab", 0, 1, 1, 1);
            AddLetters("ASDF", 1, 1);
            Add(162, "Ctrl", 0, 2, 1, 1);
            Add(160, "Shift", 1, 2, 1, 1);
            Add(32, "Space", 2, 2, 2, 1);
            Add(13, "Enter", 4, 2, 1, 1);
            byId.Add(163, byId[162]);
            byId.Add(17, byId[162]);
            byId.Add(161, byId[160]);
            byId.Add(16, byId[160]);
            byId.Add(269, byId[13]);
            readOnlyKeys = keys.AsReadOnly();
        }

        internal IList<KeyCap> Keys { get { return readOnlyKeys; } }

        internal KeyCap Find(int keyId)
        {
            KeyCap cap;
            return byId.TryGetValue(keyId, out cap) ? cap : null;
        }

        /// <summary>返回键帽表面的手部目标点；未知键号不得静默映射至其它键。</summary>
        internal PointF GetTarget(int keyId)
        {
            KeyCap cap = Find(keyId);
            if (cap == null) throw new ArgumentOutOfRangeException("keyId", "此键号不属于常用14键布局。");
            return cap.center;
        }

        private void AddLetters(string letters, float x, float y)
        {
            for (int i = 0; i < letters.Length; i++)
                Add((int)letters[i], letters[i].ToString(), x + i, y, 1, 1);
        }

        private void Add(int id, string label, float x, float y, float width, float height)
        {
            const float gap = 0.065f;
            RectangleF rect = new RectangleF(x + gap, y + gap, width - 2 * gap, height - 2 * gap);
            KeyCap cap = new KeyCap
            {
                id = id,
                label = label,
                logicalRect = rect,
                polygon = ProjectRect(rect),
                center = Project(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2)
            };
            keys.Add(cap);
            byId.Add(id, cap);
        }

        private static PointF Project(float x, float y)
        {
            float u = x / logicalWidth;
            float v = y / logicalHeight;
            // 角色视角旋转180度：Esc行靠观众，空格行靠角色。
            float frontX = 654 + (398 - 654) * u;
            float frontY = 580;
            float backX = 639 + (410 - 639) * u;
            float backY = 542;
            return new PointF(frontX + (backX - frontX) * v, frontY + (backY - frontY) * v);
        }

        private static PointF[] ProjectRect(RectangleF rect)
        {
            return new PointF[]
            {
                Project(rect.Left, rect.Top), Project(rect.Right, rect.Top),
                Project(rect.Right, rect.Bottom), Project(rect.Left, rect.Bottom)
            };
        }

        private static PointF[] Offset(PointF[] points, float dy)
        {
            PointF[] result = new PointF[points.Length];
            for (int i = 0; i < points.Length; i++) result[i] = new PointF(points[i].X, points[i].Y + dy);
            return result;
        }

        private static GraphicsPath RoundedPolygon(PointF[] points, float radius)
        {
            PointF[] enter = new PointF[points.Length];
            PointF[] leave = new PointF[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                PointF corner = points[i];
                PointF previous = points[(i + points.Length - 1) % points.Length];
                PointF next = points[(i + 1) % points.Length];
                float previousLength = Distance(corner, previous), nextLength = Distance(corner, next);
                float trim = Math.Min(radius, Math.Min(previousLength, nextLength) * 0.25f);
                enter[i] = Blend(corner, previous, trim / previousLength);
                leave[i] = Blend(corner, next, trim / nextLength);
            }
            GraphicsPath path = new GraphicsPath();
            for (int i = 0; i < points.Length; i++)
            {
                path.AddBezier(enter[i], Blend(enter[i], points[i], 2f / 3), Blend(leave[i], points[i], 2f / 3), leave[i]);
                path.AddLine(leave[i], enter[(i + 1) % points.Length]);
            }
            path.CloseFigure();
            return path;
        }

        private static PointF Blend(PointF a, PointF b, float fraction)
        {
            return new PointF(a.X + (b.X - a.X) * fraction, a.Y + (b.Y - a.Y) * fraction);
        }

        private static float Distance(PointF a, PointF b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static bool Held(bool[] heldKeys, int keyId)
        {
            return heldKeys != null && keyId >= 0 && keyId < heldKeys.Length && heldKeys[keyId];
        }

        private static bool IsHeld(bool[] heldKeys, KeyCap cap)
        {
            if (Held(heldKeys, cap.id)) return true;
            if (cap.id == 162) return Held(heldKeys, 17) || Held(heldKeys, 163);
            if (cap.id == 160) return Held(heldKeys, 16) || Held(heldKeys, 161);
            if (cap.id == 13) return Held(heldKeys, 269);
            return false;
        }

        internal void Draw(Graphics g, bool[] heldKeys, bool showLabels, bool butterfly)
        {
            if (g == null) throw new ArgumentNullException("g");
            GraphicsState state = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                PointF[] board = ProjectRect(new RectangleF(-0.08f, -0.20f, logicalWidth + 0.16f, logicalHeight + 0.40f));
                using (GraphicsPath shadowPath = RoundedPolygon(Offset(board, 5), 7))
                using (SolidBrush shadow = new SolidBrush(Color.FromArgb(56, 94, 61, 30)))
                    g.FillPath(shadow, shadowPath);
                using (GraphicsPath edgePath = RoundedPolygon(Offset(board, 3), 7))
                using (SolidBrush edge = new SolidBrush(Color.FromArgb(210, 160, 84)))
                    g.FillPath(edge, edgePath);
                using (GraphicsPath boardPath = RoundedPolygon(board, 7))
                using (LinearGradientBrush boardFill = new LinearGradientBrush(new PointF(0, 538), new PointF(0, 586), Color.FromArgb(255, 235, 177), Color.FromArgb(246, 207, 125)))
                using (Pen outline = new Pen(Color.FromArgb(117, 78, 51), 3))
                {
                    g.FillPath(boardFill, boardPath);
                    g.DrawPath(outline, boardPath);
                }
                using (Pen light = new Pen(Color.FromArgb(255, 249, 218), 1.3f))
                    g.DrawLine(light, Blend(board[2], board[3], 0.035f), Blend(board[3], board[2], 0.035f));

                using (Font normalFont = new Font("Segoe UI", 7.2f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Font smallFont = new Font("Segoe UI", 6.5f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush letterBrush = new SolidBrush(Color.FromArgb(114, 79, 35)))
                using (SolidBrush downLetterBrush = new SolidBrush(Color.FromArgb(92, 55, 28)))
                using (SolidBrush keySide = new SolidBrush(Color.FromArgb(216, 177, 113)))
                using (Pen border = new Pen(Color.FromArgb(220, 185, 120), 0.85f))
                using (Pen capLight = new Pen(Color.FromArgb(255, 254, 235), 0.9f))
                using (StringFormat format = new StringFormat())
                {
                    format.Alignment = StringAlignment.Center;
                    format.LineAlignment = StringAlignment.Center;
                    format.FormatFlags = StringFormatFlags.NoWrap;
                    foreach (KeyCap cap in keys)
                    {
                        bool down = IsHeld(heldKeys, cap);
                        float pressOffset = down ? 2f : 0;
                        PointF[] top = down ? Offset(cap.polygon, pressOffset) : cap.polygon;
                        using (GraphicsPath sidePath = RoundedPolygon(Offset(cap.polygon, 2.8f), 2.8f))
                            g.FillPath(keySide, sidePath);
                        Color topColor = down ? (butterfly ? Color.FromArgb(247, 174, 169) : Color.FromArgb(249, 192, 86)) : Color.FromArgb(255, 249, 224);
                        Color bottomColor = down ? (butterfly ? Color.FromArgb(241, 141, 152) : Color.FromArgb(236, 161, 51)) : Color.FromArgb(249, 232, 189);
                        float minY = Math.Min(top[2].Y, top[3].Y);
                        float maxY = Math.Max(top[0].Y, top[1].Y);
                        using (GraphicsPath topPath = RoundedPolygon(top, 2.8f))
                        using (LinearGradientBrush fill = new LinearGradientBrush(new PointF(0, minY), new PointF(0, maxY + 0.1f), topColor, bottomColor))
                        {
                            g.FillPath(fill, topPath);
                            g.DrawPath(border, topPath);
                        }
                        if (!down) g.DrawLine(capLight, Blend(top[2], top[3], 0.2f), Blend(top[3], top[2], 0.2f));

                        if (showLabels)
                        {
                            GraphicsState labelState = g.Save();
                            try
                            {
                                g.TranslateTransform(cap.center.X, cap.center.Y + pressOffset);
                                g.RotateTransform(180);
                                Font font = cap.label.Length > 3 ? smallFont : normalFont;
                                g.DrawString(cap.label, font, down ? downLetterBrush : letterBrush, new PointF(0, 0), format);
                            }
                            finally { g.Restore(labelState); }
                        }
                    }
                }
            }
            finally { g.Restore(state); }
        }

        private static bool InsideConvex(PointF point, PointF[] polygon)
        {
            float sign = 0;
            for (int i = 0; i < polygon.Length; i++)
            {
                PointF a = polygon[i];
                PointF b = polygon[(i + 1) % polygon.Length];
                float cross = (b.X - a.X) * (point.Y - a.Y) - (b.Y - a.Y) * (point.X - a.X);
                if (Math.Abs(cross) < 0.001f) continue;
                if (sign != 0 && sign * cross < 0) return false;
                sign = cross;
            }
            return true;
        }

        internal static string RunSelfTests()
        {
            KeyboardModel model = new KeyboardModel();
            if (model.Keys.Count != 14) throw new InvalidOperationException("小键盘应包含14个键帽。");
            HashSet<int> ids = new HashSet<int>();
            PointF[] outer = ProjectRect(new RectangleF(0, 0, logicalWidth, logicalHeight));
            foreach (KeyCap cap in model.Keys)
            {
                if (!ids.Add(cap.id)) throw new InvalidOperationException("存在重复键号。");
                if (!InsideConvex(cap.center, cap.polygon) || !InsideConvex(cap.center, outer))
                    throw new InvalidOperationException("键帽中心超出投影范围。");
                PointF target = model.GetTarget(cap.id);
                if (target != cap.center || !InsideConvex(target, cap.polygon))
                    throw new InvalidOperationException("输入目标与实际绘制键帽不一致。");
                using (GraphicsPath rounded = RoundedPolygon(cap.polygon, 2.8f))
                    if (!rounded.IsVisible(target)) throw new InvalidOperationException("目标不在圆角键帽内。");
                using (GraphicsPath pressed = RoundedPolygon(Offset(cap.polygon, 2), 2.8f))
                    if (!pressed.IsVisible(new PointF(target.X, target.Y + 2))) throw new InvalidOperationException("按压目标与下沉键帽不一致。");
                RectangleF r = cap.logicalRect;
                if (r.Left < 0 || r.Right > logicalWidth || r.Top < 0 || r.Bottom > logicalHeight)
                    throw new InvalidOperationException("键帽逻辑矩形超出布局范围。");
                for (int j = 0; j < cap.polygon.Length; j++)
                    if (!InsideConvex(cap.polygon[j], outer)) throw new InvalidOperationException("键帽顶点超出模型。");
            }

            int[] sampleIds = { 27, 81, 87, 69, 82, 9, 65, 83, 68, 70, 162, 160, 32, 13 };
            foreach (int key in sampleIds)
                if (model.Find(key) == null || model.Find(key).id != key) throw new InvalidOperationException("常用14键清单不完整。");
            for (int i = 0; i < sampleIds.Length; i++)
                for (int j = i + 1; j < sampleIds.Length; j++)
                {
                    PointF a = model.GetTarget(sampleIds[i]);
                    PointF b = model.GetTarget(sampleIds[j]);
                    float dx = a.X - b.X, dy = a.Y - b.Y;
                    if (dx * dx + dy * dy < 16) throw new InvalidOperationException("不同按键被映射到了相同目标。");
                }

            if (!(model.GetTarget(32).Y < model.GetTarget(65).Y && model.GetTarget(65).Y < model.GetTarget(81).Y &&
                  model.GetTarget(27).X > model.GetTarget(82).X && model.GetTarget(9).X > model.GetTarget(70).X &&
                  model.GetTarget(162).X > model.GetTarget(160).X && model.GetTarget(160).X > model.GetTarget(32).X &&
                  model.GetTarget(32).X > model.GetTarget(13).X))
                throw new InvalidOperationException("14键键盘朝向或三行排列错误。");
            int[][] aliases = { new int[] { 162, 163, 17 }, new int[] { 160, 161, 16 }, new int[] { 13, 269 } };
            foreach (int[] group in aliases)
            {
                KeyCap canonical = model.Find(group[0]);
                foreach (int alias in group)
                {
                    if (!Object.ReferenceEquals(model.Find(alias), canonical) || model.GetTarget(alias) != canonical.center)
                        throw new InvalidOperationException("修饰键或回车别名未映射同一键帽。");
                    bool[] flags = new bool[512];
                    flags[alias] = true;
                    foreach (KeyCap cap in model.Keys)
                        if (IsHeld(flags, cap) != Object.ReferenceEquals(cap, canonical))
                            throw new InvalidOperationException("别名按下未点亮正确键帽。");
                }
            }
            if (IsHeld(null, model.Find(65)) || IsHeld(new bool[1], model.Find(162)))
                throw new InvalidOperationException("空输入状态处理错误。");
            if (model.Find(-1) != null || model.Find(270) != null || model.Find(112) != null || model.Find(74) != null)
                throw new InvalidOperationException("未知键号查找行为错误。");
            bool rejected = false;
            try { model.GetTarget(112); } catch (ArgumentOutOfRangeException) { rejected = true; }
            if (!rejected) throw new InvalidOperationException("未知键号未明确拒绝。");
            return "PASS: 14 compact keys; shared rounded geometry; three-row character-facing layout; Ctrl/Shift/Enter aliases share targets and pressed caps.";
        }
    }
}
