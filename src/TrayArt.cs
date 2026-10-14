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

    /** <summary>菜单与设置共用的颜色。只读值可跨线程读取，不持有绘图资源。</summary> */
    internal static class PetUiTheme
    {
        internal static readonly Color Ink = Color.FromArgb(66, 49, 43);
        internal static readonly Color Muted = Color.FromArgb(119, 104, 91);
        internal static readonly Color Cream = Color.FromArgb(250, 247, 239);
        internal static readonly Color Surface = Color.FromArgb(255, 253, 248);
        internal static readonly Color Sidebar = Color.FromArgb(243, 237, 225);
        internal static readonly Color Gold = Color.FromArgb(234, 193, 83);
        internal static readonly Color Hover = Color.FromArgb(247, 237, 214);
        internal static readonly Color Selected = Color.FromArgb(247, 226, 166);
        internal static readonly Color Mint = Color.FromArgb(216, 238, 226);
        internal static readonly Color Success = Color.FromArgb(63, 117, 82);
        internal static readonly Color Line = Color.FromArgb(229, 220, 201);
        internal static readonly Color Focus = Color.FromArgb(145, 104, 40);
    }

    internal sealed class PetMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return PetUiTheme.Surface; } }
        public override Color ImageMarginGradientBegin { get { return ToolStripDropDownBackground; } }
        public override Color ImageMarginGradientMiddle { get { return ToolStripDropDownBackground; } }
        public override Color ImageMarginGradientEnd { get { return ToolStripDropDownBackground; } }
        public override Color MenuItemSelected { get { return PetUiTheme.Hover; } }
        public override Color MenuItemBorder { get { return PetUiTheme.Line; } }
        public override Color MenuBorder { get { return PetUiTheme.Line; } }
        public override Color SeparatorDark { get { return PetUiTheme.Line; } }
        public override Color SeparatorLight { get { return ToolStripDropDownBackground; } }
        public override Color CheckBackground { get { return PetUiTheme.Mint; } }
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
            float scale = e.Graphics.DpiX / 96f;
            float size = Math.Min(16 * scale, Math.Min(e.ImageRectangle.Width, e.ImageRectangle.Height));
            RectangleF box = new RectangleF(e.ImageRectangle.Left + (e.ImageRectangle.Width - size) / 2,
                e.ImageRectangle.Top + (e.ImageRectangle.Height - size) / 2, size, size);
            GraphicsState state = e.Graphics.Save();
            try
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (Brush fill = new SolidBrush(PetUiTheme.Success)) e.Graphics.FillEllipse(fill, box);
                using (Pen check = new Pen(Color.White, 1.6f * scale))
                {
                    check.StartCap = LineCap.Round; check.EndCap = LineCap.Round;
                    e.Graphics.DrawLines(check, new PointF[] {
                        new PointF(box.Left + size * .25f, box.Top + size * .5f),
                        new PointF(box.Left + size * .43f, box.Top + size * .68f),
                        new PointF(box.Left + size * .76f, box.Top + size * .33f) });
                }
            }
            finally { e.Graphics.Restore(state); }
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item.Enabled ? PetUiTheme.Muted : SystemColors.GrayText;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? PetUiTheme.Ink : PetUiTheme.Muted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            Rectangle bounds = new Rectangle(2, 1, Math.Max(1, e.Item.Width - 4), Math.Max(1, e.Item.Height - 2));
            GraphicsState state = e.Graphics.Save();
            try
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = Rounded(bounds, Math.Max(2, (int)(5 * e.Graphics.DpiX / 96))))
                using (Brush fill = new SolidBrush(PetUiTheme.Hover)) e.Graphics.FillPath(fill, path);
            }
            finally { e.Graphics.Restore(state); }
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            base.OnRenderToolStripBackground(e);
            ContextMenuStrip context = e.ToolStrip as ContextMenuStrip;
            if (context == null || EdgeMascot == null) return;
            float scale = e.Graphics.DpiX / 96f;
            int width = (int)(EdgeMascot.Width * scale), height = (int)(EdgeMascot.Height * scale);
            e.Graphics.DrawImage(EdgeMascot, new Rectangle(context.Width - width - 12, (int)(12 * scale), width, height));
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
