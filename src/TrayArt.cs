using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace DieYing
{
    /** <summary>托盘与菜单共用的迷你角色图案。所有返回图像由调用者释放。</summary> */
    internal static class TrayArt
    {
        internal static Bitmap Portrait(int skin, int size)
        {
            string portraitFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets",skin == 0 ? "classic-mascot.png" : "butterfly-mascot.png");
            if(skin>=2)portraitFile=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","zhu",skin==2?"casual-master.png":"stage-master.png");
            else portraitFile=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","huang",skin==0?"classic-master.png":"stage-master.png");
            bool standalone = File.Exists(portraitFile);
            using (Bitmap atlas = new Bitmap(standalone ? portraitFile : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "approved", "animation-parts.png")))
            {
                if(skin>=2)
                using(var eyes=new Bitmap(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","zhu","star-eyes-small.png")))
                using(var paint=Graphics.FromImage(atlas))
                {
                    foreach(int x in new int[]{515,743})using(var mask=new GraphicsPath())
                    {
                        mask.AddBezier(x+4,558,x+25,550,x+68,548,x+86,559);
                        mask.AddBezier(x+86,559,x+99,584,x+95,628,x+69,633);
                        mask.AddBezier(x+69,633,x+49,639,x+18,636,x+9,618);
                        mask.AddBezier(x+9,618,x-2,598,x-2,573,x+4,558);
                        paint.SetClip(mask);paint.DrawImage(eyes,new Rectangle(x-2,548,103,91),513,548,103,91,GraphicsUnit.Pixel);
                    }
                }
                Bitmap result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(result))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    Rectangle source = standalone ? new Rectangle(0,0,atlas.Width,atlas.Height) : skin == 0 ? new Rectangle(0, 631, 633, 565) : new Rectangle(634, 623, 620, 580);
                    if(skin>=2)source=new Rectangle(100,65,1120,685);
                    else source=new Rectangle(290,0,1000,625);
                    float scale=Math.Min(size/(float)source.Width,size/(float)source.Height);
                    var destination=new RectangleF((size-source.Width*scale)/2,(size-source.Height*scale)/2,source.Width*scale,source.Height*scale);
                    g.DrawImage(atlas, destination, source, GraphicsUnit.Pixel);
                }
                return result;
            }
        }

        internal static Bitmap MenuDecoration(int skin, int frame)
        {
            Bitmap result = new Bitmap(88, 50, PixelFormat.Format32bppArgb);
            using (Bitmap portrait = Portrait(skin, 42))
            using (Graphics g = Graphics.FromImage(result))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (Brush shadow = new SolidBrush(Color.FromArgb(35, 177, 137, 89)))
                    g.FillEllipse(shadow, 4, 37, 45, 8);
                GraphicsState state = g.Save();
                g.TranslateTransform(26, 28);
                // 菜单展开时像小人趴在菜单边缘，第二帧微微抬头形成轻量动态。
                g.RotateTransform(frame == 0 ? -78f : -62f);
                g.DrawImage(portrait, -21, -21, 42, 42);
                g.Restore(state);
                using (Pen gold = new Pen(Color.FromArgb(224, 177, 91), 1.4f))
                using (Brush cream = new SolidBrush(Color.FromArgb(248, 214, 135)))
                {
                    float y = frame == 0 ? 12 : 9;
                    g.DrawLine(gold, 61, y - 4, 61, y + 4);
                    g.DrawLine(gold, 57, y, 65, y);
                    g.FillEllipse(cream, 73, frame == 0 ? 18 : 15, 4, 4);
                    g.FillEllipse(cream, 80, frame == 0 ? 10 : 13, 3, 3);
                }
            }
            return result;
        }

        internal static Bitmap EdgeMascot(int skin, int frame)
        {
            Bitmap result = new Bitmap(42, 46, PixelFormat.Format32bppArgb);
            using (Bitmap portrait = Portrait(skin, 42))
            using (Graphics g = Graphics.FromImage(result))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                GraphicsState state = g.Save();
                g.TranslateTransform(21, 23);
                g.RotateTransform(frame == 0 ? 8f : -3f);
                g.DrawImage(portrait, -21, -21, 42, 42);
                g.Restore(state);
            }
            return result;
        }
        internal static Icon MakeIcon(int skin)
        {
            using (Bitmap image = Portrait(skin, 32))
            {
                IntPtr handle = image.GetHicon();
                try { using (Icon borrowed = Icon.FromHandle(handle)) return (Icon)borrowed.Clone(); }
                finally { Native.DestroyIcon(handle); }
            }
        }
        /** <summary>导出含16—256像素图层的应用图标；仅写入调用方指定的目录。</summary> */
        internal static void Export(string directory)
        {
            Directory.CreateDirectory(directory);
            for (int skin = 0; skin < 2; skin++)
            {
                using(Bitmap portrait = Portrait(skin,256)) portrait.Save(Path.Combine(directory,skin == 0 ? "classic-mascot.png" : "butterfly-mascot.png"),ImageFormat.Png);
                int[] sizes = { 16, 20, 24, 32, 48, 64, 128, 256 };
                List<byte[]> layers = new List<byte[]>();
                foreach (int size in sizes)
                    using (Bitmap bitmap = Portrait(skin, size))
                    using (MemoryStream stream = new MemoryStream()) { bitmap.Save(stream, ImageFormat.Png); layers.Add(stream.ToArray()); }
                using (BinaryWriter writer = new BinaryWriter(File.Create(Path.Combine(directory, skin == 0 ? "classic.ico" : "app.ico"))))
                {
                    writer.Write((short)0); writer.Write((short)1); writer.Write((short)sizes.Length);
                    int offset = 6 + 16 * sizes.Length;
                    for (int i = 0; i < sizes.Length; i++)
                    {
                        writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                        writer.Write((byte)0); writer.Write((byte)0); writer.Write((short)1); writer.Write((short)32);
                        writer.Write(layers[i].Length); writer.Write(offset); offset += layers[i].Length;
                    }
                    foreach (byte[] data in layers) writer.Write(data);
                }
            }
        }
    }

    internal sealed class PetMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(255, 251, 241); } }
        public override Color ImageMarginGradientBegin { get { return ToolStripDropDownBackground; } }
        public override Color ImageMarginGradientMiddle { get { return ToolStripDropDownBackground; } }
        public override Color ImageMarginGradientEnd { get { return ToolStripDropDownBackground; } }
        public override Color MenuItemSelected { get { return Color.FromArgb(248, 230, 184); } }
        public override Color MenuItemBorder { get { return Color.FromArgb(226, 192, 126); } }
        public override Color MenuBorder { get { return Color.FromArgb(215, 187, 142); } }
        public override Color SeparatorDark { get { return Color.FromArgb(234, 219, 190); } }
        public override Color SeparatorLight { get { return ToolStripDropDownBackground; } }
        public override Color CheckBackground { get { return Color.FromArgb(204, 230, 215); } }
        public override Color CheckSelectedBackground { get { return CheckBackground; } }
        public override Color CheckPressedBackground { get { return CheckBackground; } }
    }

    internal sealed class PetMenuRenderer : ToolStripProfessionalRenderer
    {
        internal Image EdgeMascot { get; set; }

        internal PetMenuRenderer() : base(new PetMenuColors())
        {
            RoundedEdges = true;
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            // 勾选状态由 OnRenderItemText 绘制，禁止 WinForms 默认的方框背景。
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            Rectangle area = e.ArrowRectangle;
            int size = 17;
            Rectangle bubble = new Rectangle(area.Left + (area.Width - size) / 2, area.Top + (area.Height - size) / 2, size, size);
            using (Brush bubbleBrush = new SolidBrush(e.Item.Selected ? Color.FromArgb(211, 145, 145) : Color.FromArgb(226, 176, 157)))
            using (Brush arrowBrush = new SolidBrush(Color.FromArgb(255, 249, 237)))
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.FillEllipse(bubbleBrush, bubble);
                using (GraphicsPath arrow = new GraphicsPath())
                {
                    arrow.AddBezier(bubble.Left + 6, bubble.Top + 4, bubble.Left + 10, bubble.Top + 5, bubble.Left + 11, bubble.Top + 7, bubble.Left + 12, bubble.Top + 8);
                    arrow.AddBezier(bubble.Left + 12, bubble.Top + 8, bubble.Left + 10, bubble.Top + 10, bubble.Left + 8, bubble.Top + 12, bubble.Left + 6, bubble.Top + 13);
                    arrow.AddBezier(bubble.Left + 7, bubble.Top + 10, bubble.Left + 7, bubble.Top + 8, bubble.Left + 7, bubble.Top + 6, bubble.Left + 6, bubble.Top + 4);
                    arrow.CloseFigure();
                    e.Graphics.FillPath(arrowBrush, arrow);
                }
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            base.OnRenderItemText(e);
            ToolStripMenuItem item = e.Item as ToolStripMenuItem;
            if (item == null || !item.Checked) return;
            Rectangle box = new Rectangle(e.TextRectangle.Left - 29, e.TextRectangle.Top + (e.TextRectangle.Height - 17) / 2, 17, 17);
            using (Brush fill = new SolidBrush(Color.FromArgb(190, 224, 204)))
            using (Pen border = new Pen(Color.FromArgb(111, 169, 137), 1f))
            using (Pen check = new Pen(Color.FromArgb(63, 117, 82), 1.8f))
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.FillEllipse(fill, box);
                e.Graphics.DrawEllipse(border, box);
                check.StartCap = LineCap.Round;
                check.EndCap = LineCap.Round;
                e.Graphics.DrawLines(check, new Point[] { new Point(box.Left + 4, box.Top + 8), new Point(box.Left + 7, box.Top + 11), new Point(box.Right - 4, box.Top + 5) });
            }
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            base.OnRenderToolStripBackground(e);
            ContextMenuStrip context = e.ToolStrip as ContextMenuStrip;
            if (context == null || EdgeMascot == null) return;
            int x = context.Width - 22;
            e.Graphics.DrawImage(EdgeMascot, new Rectangle(x, 7, EdgeMascot.Width, EdgeMascot.Height));
        }

        private static GraphicsPath Rounded(Rectangle rectangle, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = radius * 2;
            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

}
