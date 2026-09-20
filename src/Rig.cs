using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace DieYing
{
    internal static class V
    {
        internal static PointF Add(PointF a, PointF b) { return new PointF(a.X + b.X, a.Y + b.Y); }
        internal static PointF Sub(PointF a, PointF b) { return new PointF(a.X - b.X, a.Y - b.Y); }
        internal static PointF Mul(PointF a, float n) { return new PointF(a.X * n, a.Y * n); }
        internal static float Dot(PointF a, PointF b) { return a.X * b.X + a.Y * b.Y; }
        internal static float Len(PointF a) { return (float)Math.Sqrt(Dot(a, a)); }
        internal static PointF Unit(PointF a) { return Mul(a, 1 / Math.Max(.0001f, Len(a))); }
        internal static PointF Perp(PointF a) { return new PointF(-a.Y, a.X); }
        internal static PointF Lerp(PointF a, PointF b, float t) { return Add(Mul(a, 1 - t), Mul(b, t)); }
        internal static float Clamp(float n, float a, float b) { return Math.Max(a, Math.Min(b, n)); }
        internal static float Smooth(float t) { t = Clamp(t, 0, 1); return t * t * (3 - 2 * t); }
    }

    /** <summary>角色表情与独立键鼠；肩袖和手掌由 ArmRig 连续绑定。</summary> */
    internal sealed class CharacterModel : IDisposable
    {
        internal readonly bool butterfly;
        private readonly Bitmap openBody, blinkBody, sprites;
        private readonly Bitmap mouseSprite, keyPawSprite;
        private readonly ArmRig arms;
        private Bitmap deskLayer;
        private readonly Bitmap mouseLayer=new Bitmap(280,200,PixelFormat.Format32bppPArgb);
        private readonly LayeredRig surface;
        internal string RendererDevice { get { return surface.Device; } }
        private static readonly Rectangle mouse = new Rectangle(706, 829, 472, 273);
        internal CharacterModel(string assetDirectory, int skin)
        {
            butterfly = skin == 1;
            string folder = Path.Combine(assetDirectory, "approved"), name = butterfly ? "butterfly" : "classic";
            using (Bitmap original = Load(Path.Combine(folder, name + "-open.png")))
            using (Bitmap closed = Load(Path.Combine(assetDirectory, "layered", name + "-closed-clean.png")))
            {
                int height = (int)Math.Round(1600f * original.Height / original.Width);
                openBody = new Bitmap(1600, height, PixelFormat.Format32bppPArgb);
                using (Graphics cache = Graphics.FromImage(openBody))
                { cache.InterpolationMode = InterpolationMode.HighQualityBicubic; cache.DrawImage(original, new Rectangle(0, 0, 1600, height)); }
                blinkBody = openBody.Clone(new Rectangle(0, 0, 1600, height), PixelFormat.Format32bppPArgb);
                using (Graphics cache = Graphics.FromImage(blinkBody))
                {
                    cache.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    cache.DrawImage(closed, new Rectangle(0, 0, 1600, height));
                }
            }
            ClearBrowBand(openBody, assetDirectory, name);
            ClearBrowBand(blinkBody, assetDirectory, name);
            sprites = Load(Path.Combine(folder, "paws-mouse.png"));
            mouseSprite = CacheSprite(mouse, 102, 59);
            keyPawSprite = CacheSprite(butterfly ? new Rectangle(96,820,452,301) : new Rectangle(96,193,452,301),87,58);
            keyPawSprite.RotateFlip(RotateFlipType.RotateNoneFlipX);
            using (Bitmap happy = Expression(2))
            using (Bitmap surprised = Expression(3))
            using (Bitmap dizzy = Expression(4))
            using (Bitmap sleepSource = Load(Path.Combine(assetDirectory, "layered", name + "-sleep.png")))
            using (Bitmap sleep = new Bitmap(openBody.Width, openBody.Height, PixelFormat.Format32bppPArgb))
            {
                using (Graphics g = Graphics.FromImage(sleep))
                { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(sleepSource, new Rectangle(0, 0, sleep.Width, sleep.Height)); }
                ClearBrowBand(sleep, assetDirectory, name);
                surface = new LayeredRig(assetDirectory, butterfly, new Bitmap[] { openBody, blinkBody, happy, surprised, dizzy, sleep });
                arms = new ArmRig(assetDirectory, butterfly, surface.Canvas);
            }
        }
        private static void ClearBrowBand(Bitmap target, string assetDirectory, string name)
        {
            using(var clean=new Bitmap(Path.Combine(assetDirectory,"layered",name+"-brow-base.png")))
            using(var g=Graphics.FromImage(target))
            {
                // 眉毛底板止于眼睑上方，不能把睁眼睫毛带进闭眼素材。
                g.SetClip(new RectangleF(460,550,680,90));
                g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                g.DrawImage(clean,new RectangleF(0,0,1600,1600f*clean.Height/clean.Width));
            }
        }
        private Bitmap CacheSprite(Rectangle source, int width, int height)
        {
            Bitmap result = new Bitmap(width*2,height*2,PixelFormat.Format32bppPArgb);
            using(Graphics g=Graphics.FromImage(result))
            {
                g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                g.DrawImage(sprites,new Rectangle(0,0,width*2,height*2),source,GraphicsUnit.Pixel);
            }
            return result;
        }
        private static Bitmap Load(string file)
        {
            using (var source = new Bitmap(file)) return source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);
        }
        private Bitmap Expression(int kind)
        {
            Bitmap result = (Bitmap)(kind == 2 ? blinkBody : openBody).Clone();
            float x = butterfly ? 398 : 384, y = butterfly ? 425 : 432;
            using (Graphics g = Graphics.FromImage(result))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.ScaleTransform(2,2);
                Color skin = openBody.GetPixel((int)x*2, ((int)y - 13)*2);
                using (SolidBrush fill = new SolidBrush(skin)) g.FillEllipse(fill, x - 26, y - 13, 52, 26);
                using (Pen ink = new Pen(Color.FromArgb(185, 87, 80), 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    if (kind == 2)
                    {
                        using (GraphicsPath mouthPath = new GraphicsPath())
                        {
                            mouthPath.AddBezier(x - 13, y - 2, x - 4, y + 3, x + 4, y + 3, x + 13, y - 2);
                            mouthPath.AddBezier(x + 13, y - 2, x + 8, y + 15, x - 7, y + 15, x - 13, y - 2);
                            using (Brush inside = new SolidBrush(Color.FromArgb(242, 151, 153))) g.FillPath(inside, mouthPath);
                            g.DrawPath(ink, mouthPath);
                        }
                    }
                    else if (kind == 3)
                    {
                        using (Brush inside = new SolidBrush(Color.FromArgb(222, 135, 139))) g.FillEllipse(inside, x - 6, y - 5, 12, 16);
                        g.DrawEllipse(ink, x - 6, y - 5, 12, 16);
                    }
                    else g.DrawCurve(ink, new PointF[] { new PointF(x - 13, y + 1), new PointF(x - 7, y - 2), new PointF(x, y + 3), new PointF(x + 7, y - 2), new PointF(x + 13, y + 1) });
                }
                if (kind == 4)
                {
                    RectangleF[] irises = butterfly ? new RectangleF[] { new RectangleF(301,347,62,54), new RectangleF(449,362,68,51) } :
                        new RectangleF[] { new RectangleF(288,352,63,55), new RectangleF(439,352,72,55) };
                    foreach (RectangleF iris in irises)
                    {
                        PointF eye = new PointF(iris.X + iris.Width / 2, iris.Y + iris.Height / 2);
                        using (GraphicsPath eyePath = new GraphicsPath())
                        using (Brush white = new SolidBrush(Color.FromArgb(255, 242, 224)))
                        using (Pen edge = new Pen(Color.FromArgb(99,67,70),1.8f))
                        {
                            eyePath.AddBezier(iris.Left+4,iris.Top+2,iris.Left+20,iris.Top-2,iris.Right-15,iris.Top+2,iris.Right-4,iris.Top+8);
                            eyePath.AddBezier(iris.Right-4,iris.Top+8,iris.Right+3,iris.Bottom-18,iris.Right-6,iris.Bottom-2,iris.Right-15,iris.Bottom);
                            eyePath.AddBezier(iris.Right-15,iris.Bottom,iris.Right-28,iris.Bottom+1,iris.Left+18,iris.Bottom+1,iris.Left+12,iris.Bottom-4);
                            eyePath.AddBezier(iris.Left+12,iris.Bottom-4,iris.Left+1,iris.Bottom-12,iris.Left-2,iris.Top+20,iris.Left+4,iris.Top+2);
                            g.FillPath(white,eyePath); g.DrawPath(edge,eyePath);
                        }
                        PointF[] spiral = new PointF[65];
                        for (int i = 0; i < spiral.Length; i++) { float a = i * .20f, r = 1 + i * .29f; spiral[i] = new PointF(eye.X + (float)Math.Cos(a) * r, eye.Y + (float)Math.Sin(a) * r * .85f); }
                        using (Pen pen = new Pen(Color.FromArgb(146, 102, 123), 2.2f)) g.DrawLines(pen, spiral);
                    }
                }
            }
            return result;
        }
        internal void DrawScene(Graphics g, MotionState pose, int expression,KeyboardModel keyboard,bool labels,PointF mouseContact,PointF keyContact)
        {
            float[] values;using(var matrix=g.Transform)values=matrix.Elements;
            float scale=(float)Math.Sqrt(values[0]*values[0]+values[1]*values[1]);
            int width=Math.Max(1,(int)Math.Round(800*scale)),height=Math.Max(1,(int)Math.Round(180*scale));
            if(deskLayer==null || deskLayer.Width!=width || deskLayer.Height!=height)
            {if(deskLayer!=null)deskLayer.Dispose();deskLayer=new Bitmap(width,height,PixelFormat.Format32bppPArgb);}
            using(var overlay=Graphics.FromImage(deskLayer))
            {
                overlay.Clear(Color.Transparent);overlay.ScaleTransform(scale,scale);overlay.TranslateTransform(0,-430);
                overlay.SmoothingMode=SmoothingMode.AntiAlias;overlay.InterpolationMode=InterpolationMode.HighQualityBicubic;
                overlay.PixelOffsetMode=PixelOffsetMode.HighQuality;
                overlay.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                keyboard.Draw(overlay,pose.litKeys,labels,butterfly);
                float pawWidth=87+pose.keyPress*2, pawHeight=58-pose.keyPress*3;
                if(!butterfly)overlay.DrawImage(keyPawSprite,new RectangleF(keyContact.X-pawWidth/2,keyContact.Y-pawHeight,pawWidth,pawHeight));
            }
            using(var mouseGraphics=Graphics.FromImage(mouseLayer))
            {
                mouseGraphics.Clear(Color.Transparent);mouseGraphics.ScaleTransform(2,2);
                mouseGraphics.SmoothingMode=SmoothingMode.AntiAlias;
                mouseGraphics.InterpolationMode=InterpolationMode.HighQualityBicubic;
                // 固定坐标栅格化，再由 GPU 按浮点位置移动，避免慢移时逐像素跳动。
                mouseGraphics.TranslateTransform(70,40);DrawMouse(mouseGraphics,pose);
            }
            surface.Draw(g,pose,expression,deskLayer,mouseLayer,arms,mouseContact,keyContact);
        }
        internal void DrawMouse(Graphics g, MotionState pose)
        {
            RectangleF destination = new RectangleF(-51,-25,102,59);
            g.DrawImage(mouseSprite, destination);
            if (pose.leftButton || pose.rightButton || pose.middleButton)
            {
                using (var attributes = new ImageAttributes())
                {
                    var color = new ColorMatrix(); color.Matrix00 = 1.09f; color.Matrix11 = .86f; color.Matrix22 = .65f; color.Matrix33 = .6f;
                    attributes.SetColorMatrix(color);
                    GraphicsState state = g.Save();
                    RectangleF clip = pose.leftButton && pose.rightButton ? destination :
                        new RectangleF(pose.leftButton ? 0 : -51, destination.Y, 51, destination.Height);
                    g.SetClip(clip, CombineMode.Intersect);
                    g.DrawImage(mouseSprite, Rectangle.Round(destination), 0, 0, mouseSprite.Width, mouseSprite.Height, GraphicsUnit.Pixel, attributes);
                    g.Restore(state);
                }
            }
            if (Math.Abs(pose.wheel) > .025 || pose.middleButton)
            {
                float y = 18;
                using (Pen line = new Pen(Color.FromArgb(255, 213, 115), 2.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    float direction = pose.wheel < 0 ? -1 : 1;
                    g.DrawLines(line, new PointF[] { new PointF(-8, y + 2 * direction), new PointF(-3, y - 3 * direction), new PointF(2, y + 2 * direction) });
                }
            }
        }
        public void Dispose() { surface.Dispose(); if(deskLayer!=null)deskLayer.Dispose(); mouseLayer.Dispose(); openBody.Dispose(); blinkBody.Dispose(); sprites.Dispose(); mouseSprite.Dispose(); keyPawSprite.Dispose(); }
    }
}
