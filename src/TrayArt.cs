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

}
