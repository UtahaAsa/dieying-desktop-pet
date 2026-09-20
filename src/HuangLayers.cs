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
        private Layer HuangCut(Bitmap b,RectangleF raw,int kind,PointF pivot,GraphicsPath mask=null)
        {Rectangle r=Rectangle.Ceiling(H(raw));return Cut(b,r,r,kind,H(pivot.X,pivot.Y),mask);}
        private Layer HuangPolygon(Bitmap b,int kind,PointF pivot,params PointF[] raw)
        {for(int i=0;i<raw.Length;i++)raw[i]=H(raw[i].X,raw[i].Y);return OriginalCut(b,kind,H(pivot.X,pivot.Y),raw);}
        private Layer HuangBrow(Bitmap b,PointF a,PointF b1,PointF c,PointF d)
        {
            using(var p=new GraphicsPath())using(var pen=new Pen(Color.Black,7*HuangScale))
            {p.AddBezier(H(a.X,a.Y),H(b1.X,b1.Y),H(c.X,c.Y),H(d.X,d.Y));p.Widen(pen);Rectangle r=Rectangle.Ceiling(p.GetBounds());return Cut(b,r,r,6,H((a.X+d.X)/2,(a.Y+d.Y)/2),p);}
        }
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
                if(stage)
                {
                    c.brows[0]=HuangBrow(original,new PointF(618,386),new PointF(660,408),new PointF(703,433),new PointF(728,419));
                    c.brows[1]=HuangBrow(original,new PointF(783,423),new PointF(797,441),new PointF(865,407),new PointF(900,398));
                    c.arms[0]=HuangPolygon(original,7,new PointF(622,628),new PointF(346,781),new PointF(367,727),new PointF(410,680),new PointF(447,651),new PointF(505,643),new PointF(546,612),new PointF(602,618),new PointF(633,667),new PointF(627,733),new PointF(601,744),new PointF(597,774),new PointF(549,794),new PointF(461,791),new PointF(445,779),new PointF(428,794),new PointF(407,789),new PointF(390,796),new PointF(375,786),new PointF(359,788));
                    c.arms[1]=HuangPolygon(original,8,new PointF(900,632),new PointF(855,661),new PointF(880,625),new PointF(926,626),new PointF(964,645),new PointF(1013,654),new PointF(1061,680),new PointF(1112,718),new PointF(1158,774),new PointF(1150,788),new PointF(1133,786),new PointF(1117,796),new PointF(1102,787),new PointF(1086,791),new PointF(1062,778),new PointF(1034,793),new PointF(958,795),new PointF(909,779),new PointF(891,731));
                }
                else
                {
                    c.brows[0]=HuangBrow(original,new PointF(542,393),new PointF(589,409),new PointF(638,437),new PointF(675,421));
                    c.brows[1]=HuangBrow(original,new PointF(726,420),new PointF(750,444),new PointF(811,404),new PointF(858,388));
                    c.arms[0]=HuangPolygon(original,7,new PointF(629,614),new PointF(314,822),new PointF(328,774),new PointF(366,714),new PointF(404,675),new PointF(459,635),new PointF(512,610),new PointF(568,597),new PointF(610,605),new PointF(642,641),new PointF(627,694),new PointF(629,732),new PointF(611,752),new PointF(574,770),new PointF(539,785),new PointF(472,788),new PointF(442,772),new PointF(432,795),new PointF(397,800),new PointF(365,817),new PointF(332,823));
                    c.arms[1]=HuangPolygon(original,8,new PointF(850,613),new PointF(818,733),new PointF(834,651),new PointF(853,610),new PointF(897,607),new PointF(947,627),new PointF(990,650),new PointF(1060,697),new PointF(1134,764),new PointF(1100,773),new PointF(999,752),new PointF(965,775),new PointF(891,782),new PointF(851,773));
                }
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
                using(var eyesMask=new GraphicsPath())using(var faceMask=new GraphicsPath())
                {
                    eyesMask.AddRectangle(H(eyeLeft));eyesMask.AddRectangle(H(eyeRight));Rectangle eyesBounds=Rectangle.Ceiling(eyesMask.GetBounds());
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
