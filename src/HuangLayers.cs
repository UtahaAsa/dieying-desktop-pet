using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace DieYing
{
    internal sealed partial class LayeredCharacterRig
    {
        private const float HuangScale=1323f/1536;
        private static PointF H(float x,float y){return new PointF(x*HuangScale,80+y*HuangScale);}
        private static RectangleF H(RectangleF r){PointF p=H(r.X,r.Y);return new RectangleF(p.X,p.Y,r.Width*HuangScale,r.Height*HuangScale);}
        private static Bitmap HuangImage(string path)
        {
            using(var source=new Bitmap(path))
            {
                if(source.Width!=1536||source.Height!=1024)throw new InvalidDataException("黄玲琳素材尺寸必须为1536×1024");
                var result=new Bitmap(1323,1189,PixelFormat.Format32bppPArgb);
                using(var g=Graphics.FromImage(result)){g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(source,new RectangleF(0,80,1323,1024*HuangScale));}
                return result;
            }
        }
        private Layer HuangPolygon(Bitmap b,int kind,PointF pivot,params PointF[] raw)
        {for(int i=0;i<raw.Length;i++)raw[i]=H(raw[i].X,raw[i].Y);return OriginalCut(b,kind,H(pivot.X,pivot.Y),raw);}
        private Costume LoadHuang(string directory,bool stage)
        {
            string name=stage?"stage":"classic";var c=new Costume{yellow=true};
            using(var original=HuangImage(Path.Combine(directory,name+"-master.png")))
            using(var plate=HuangImage(Path.Combine(directory,name+"-plate.png")))
            using(var closed=HuangImage(Path.Combine(directory,name+"-closed.png")))
            using(var ornaments=HuangImage(Path.Combine(directory,name+"-ornaments.png")))
            {
                c.body=Upload(plate,Map(new RectangleF(0,0,1323,1189)),0,PointF.Empty);c.body.deskTop=Map(H(0,735).X,H(0,735).Y).Y;
                if(stage)
                {
                    c.ornaments.Add(Cut(ornaments,Rectangle.Ceiling(H(new RectangleF(316,144,264,481))),H(new RectangleF(409,158,196,340)),1,H(535,220)));
                    c.ornaments.Add(Cut(ornaments,Rectangle.Ceiling(H(new RectangleF(989,238,225,510))),H(new RectangleF(990,224,136,353)),3,H(1036,273)));
                }
                else
                {
                    c.ornaments.Add(Cut(ornaments,Rectangle.Ceiling(H(new RectangleF(330,2,397,359))),H(new RectangleF(383,11,312,292)),1,H(542,154)));
                    c.ornaments.Add(Cut(ornaments,Rectangle.Ceiling(H(new RectangleF(867,35,390,372))),H(new RectangleF(847,28,348,356)),3,H(961,163)));
                }
                RectangleF[] iris=stage?new RectangleF[]{new RectangleF(616,451,89,77),new RectangleF(817,475,99,71)}:new RectangleF[]{new RectangleF(570,449,91,75),new RectangleF(784,450,100,75)};
                for(int i=0;i<2;i++)
                {
                    RectangleF r=H(iris[i]);using(var mask=new GraphicsPath())
                    {
                        mask.AddBezier(r.Left+4,r.Top+4,r.Left+20,r.Top-1,r.Right-13,r.Top,r.Right-5,r.Top+8);
                        mask.AddBezier(r.Right-5,r.Top+8,r.Right+2,r.Bottom-15,r.Right-5,r.Bottom,r.Right-18,r.Bottom);
                        mask.AddBezier(r.Right-18,r.Bottom,r.Left+28,r.Bottom+1,r.Left+10,r.Bottom,r.Left+6,r.Bottom-12);
                        mask.AddBezier(r.Left+6,r.Bottom-12,r.Left-2,r.Top+20,r.Left+1,r.Top+11,r.Left+4,r.Top+4);
                        c.iris[i]=Cut(original,Rectangle.Ceiling(r),r,5,PointF.Empty,mask);
                    }
                    c.eyeCenters[i]=Map(r.X+r.Width/2,r.Y+r.Height*.4f);
                }
                LoadCleanHuangParts(c,directory,stage);
                // 衣襟位于活动肩袖之前，接缝按服装轮廓遮挡，鼠标向内移动不能覆盖胸口。
                if(!stage)c.chestCover=HuangPolygon(plate,0,PointF.Empty,new PointF(615,594),new PointF(869,600),new PointF(813,741),new PointF(664,741));
                PointF left=H(stage?522:505,747),right=H(stage?977:918,743);c.arms[0].rest=Map(left.X,left.Y);c.arms[1].rest=Map(right.X,right.Y);
                RectangleF mouseRaw=stage?new RectangleF(445,762,157,80):new RectangleF(430,757,154,81);
                RectangleF mr=H(mouseRaw);using(var mask=new GraphicsPath())
                {
                    mask.AddEllipse(mr);c.mouse=Cut(original,Rectangle.Ceiling(mr),mr,9,PointF.Empty,mask);
                    for(int bits=1;bits<=3;bits++)using(var b=(Bitmap)original.Clone())using(var g=Graphics.FromImage(b))using(var attributes=new ImageAttributes())
                    {
                        var matrix=new ColorMatrix();matrix.Matrix00=matrix.Matrix11=matrix.Matrix22=.6f;attributes.SetColorMatrix(matrix);
                        g.SetClip(bits==3?mr:new RectangleF(mr.X+(bits==1?mr.Width/2:0),mr.Y,mr.Width/2,mr.Height));g.CompositingMode=CompositingMode.SourceCopy;
                        g.DrawImage(original,Rectangle.Ceiling(mr),mr.X,mr.Y,mr.Width,mr.Height,GraphicsUnit.Pixel,attributes);c.mouseDown[bits-1]=Cut(b,Rectangle.Ceiling(mr),mr,9,PointF.Empty,mask);
                    }
                }
                c.keyboardBounds=Map(H(stage?new RectangleF(704,762,397,60):new RectangleF(674,754,369,63)));
                RectangleF eyeLeft=stage?new RectangleF(543,433,191,109):new RectangleF(485,431,199,103);
                RectangleF eyeRight=stage?new RectangleF(784,446,207,112):new RectangleF(744,428,204,109);
                float mouthX=stage?749:708,mouthY=559;
                using(var eyesMask=HuangEyeMask(stage))using(var faceMask=new GraphicsPath())
                {
                    Rectangle eyesBounds=Rectangle.Ceiling(eyesMask.GetBounds());
                    c.blinkEyes=Cut(closed,eyesBounds,eyesBounds,4,PointF.Empty,eyesMask);
                    faceMask.AddPath(eyesMask,false);faceMask.AddRectangle(H(new RectangleF(mouthX-31,mouthY-18,62,43)));Rectangle faceBounds=Rectangle.Ceiling(faceMask.GetBounds());
                    for(int mood=1;mood<6;mood++)using(var face=(Bitmap)plate.Clone())using(var g=Graphics.FromImage(face))
                    {
                        if(mood==1||mood==2||mood==5){g.SetClip(eyesMask);g.DrawImageUnscaled(closed,0,0);g.ResetClip();}
                        g.SmoothingMode=SmoothingMode.AntiAlias;
                        if(mood==4)foreach(var raw in iris)
                        {
                            PointF center=H(raw.X+raw.Width/2,raw.Y+raw.Height/2);var points=new PointF[65];for(int n=0;n<65;n++){float a=n*.2f,r=1+n*.39f;points[n]=new PointF(center.X+(float)Math.Cos(a)*r,center.Y+(float)Math.Sin(a)*r*.85f);}using(var pen=new Pen(Color.FromArgb(146,102,123),2.4f))g.DrawLines(pen,points);
                        }
                        if(mood!=1)
                        {
                            PointF center=H(mouthX,mouthY);Color skin=original.GetPixel((int)center.X,(int)center.Y-15);using(var fill=new SolidBrush(skin))g.FillRectangle(fill,H(new RectangleF(mouthX-28,mouthY-14,56,35)));
                            using(var pen=new Pen(Color.FromArgb(199,86,83),2.3f){StartCap=LineCap.Round,EndCap=LineCap.Round})
                            {
                                if(mood==2){using(var p=new GraphicsPath()){p.AddBezier(center.X-13,center.Y-2,center.X-5,center.Y+3,center.X+5,center.Y+3,center.X+13,center.Y-2);p.AddBezier(center.X+13,center.Y-2,center.X+8,center.Y+17,center.X-8,center.Y+17,center.X-13,center.Y-2);using(var fill=new SolidBrush(Color.FromArgb(242,151,153)))g.FillPath(fill,p);g.DrawPath(pen,p);}}
                                else if(mood==3||mood==5)g.DrawEllipse(pen,center.X-5,center.Y-4,10,16);
                                else g.DrawCurve(pen,new PointF[]{new PointF(center.X-13,center.Y),new PointF(center.X-6,center.Y-3),new PointF(center.X,center.Y+3),new PointF(center.X+6,center.Y-3),new PointF(center.X+13,center.Y)});
                            }
                        }
                        c.moods[mood]=Cut(face,faceBounds,faceBounds,4,PointF.Empty,faceMask);
                    }
                }
            }
            return c;
        }
    }
}
