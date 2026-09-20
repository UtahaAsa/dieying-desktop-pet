using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace DieYing
{
    /** <summary>朱慧月的独立图层。共享 MotionState、SurfaceRig 和 ArmRig 的动作规则；仅绘制线程使用。</summary> */
    internal sealed partial class LayeredCharacterRig : IDisposable
    {
        private sealed class Layer
        {
            internal uint texture;
            internal RectangleF bounds;
            internal PointF pivot;
            internal int kind;
            internal PointF rest;
            internal float headShift=42,deskTop=474;
        }
        private sealed class Costume
        {
            internal Layer body, mouse;
            internal Layer blinkEyes;
            internal Layer[] mouseDown=new Layer[3];
            internal Layer[] iris=new Layer[2], brows=new Layer[2], arms=new Layer[2], moods=new Layer[6];
            internal List<Layer> ornaments=new List<Layer>();
            internal PointF[] eyeCenters={Map(565,583),Map(791,583)};
            internal RectangleF keyboardBounds=Map(new RectangleF(661,861,405,62));
            internal bool yellow;
        }
        private readonly GpuCanvas canvas=new GpuCanvas(800,680);
        private readonly Costume[] costumes=new Costume[4];
        private float loadingShift=42;
        private Layer shine;
        internal string Device { get { return canvas.device; } }
        private readonly Bitmap keyOverlay=new Bitmap(800,680,PixelFormat.Format32bppPArgb);
        private static PointF Map(float x,float y){return new PointF(3+x*.6f,-30+y*.6f);}
        private static RectangleF Map(RectangleF r){PointF p=Map(r.X,r.Y);return new RectangleF(p.X,p.Y,r.Width*.6f,r.Height*.6f);}
        internal LayeredCharacterRig(string directory)
        {
            try
            {
                for(int i=0;i<2;i++)costumes[i+2]=Load(directory,i==1);
                loadingShift=100;
                for(int i=0;i<2;i++)costumes[i]=LoadHuang(Path.Combine(directory,"..","huang"),i==1);
                loadingShift=42;
                using(var b=new Bitmap(24,24,PixelFormat.Format32bppPArgb))using(var g=Graphics.FromImage(b))
                {g.SmoothingMode=SmoothingMode.AntiAlias;g.FillEllipse(Brushes.White,7,7,10,10);shine=Upload(b,new RectangleF(0,0,6,6),5,PointF.Empty);}
            }
            catch{canvas.Dispose();throw;}
        }
        private Layer Upload(Bitmap b,RectangleF bounds,int kind,PointF pivot)
        {return new Layer{texture=canvas.Upload(b),bounds=bounds,kind=kind,pivot=pivot,headShift=loadingShift};}
        private Layer Cut(Bitmap source,Rectangle region,RectangleF destination,int kind,PointF pivot,GraphicsPath mask=null)
        {
            using(var b=new Bitmap(region.Width,region.Height,PixelFormat.Format32bppPArgb))using(var g=Graphics.FromImage(b))
            {
                g.SmoothingMode=SmoothingMode.AntiAlias;g.TranslateTransform(-region.X,-region.Y);
                if(mask!=null)g.SetClip(mask);
                g.DrawImageUnscaled(source,0,0);
                return Upload(b,Map(destination),kind,Map(pivot.X,pivot.Y));
            }
        }
        private Layer OriginalCut(Bitmap source,int kind,PointF pivot,params PointF[] vertices)
        {
            using(var path=new GraphicsPath())
            {path.AddPolygon(vertices);Rectangle r=Rectangle.Ceiling(path.GetBounds());return Cut(source,r,r,kind,pivot,path);}
        }
        private Costume Load(string directory,bool stage)
        {
            string name=stage?"stage":"casual";var c=new Costume();
            using(var original=new Bitmap(Path.Combine(directory,name+"-master.png")))
            using(var plate=new Bitmap(Path.Combine(directory,name+"-plate.png")))
            using(var parts=new Bitmap(Path.Combine(directory,name+"-parts.png")))
            using(var closed=new Bitmap(Path.Combine(directory,name+"-closed.png")))
            using(var starEyes=new Bitmap(Path.Combine(directory,"star-eyes.png")))
            {
                if(original.Width!=1323||original.Height!=1189||plate.Size!=original.Size||closed.Size!=original.Size)throw new InvalidDataException("朱慧月图层尺寸不匹配");
                c.body=Upload(plate,Map(new RectangleF(0,0,1323,1189)),0,PointF.Empty);
                if(stage)
                {
                    c.ornaments.Add(Cut(parts,new Rectangle(110,110,411,452),new RectangleF(194,141,312,353),1,new PointF(399,255)));
                    c.ornaments.Add(Cut(parts,new Rectangle(865,105,423,458),new RectangleF(835,132,322,360),3,new PointF(947,252)));
                }
                else
                {
                    using(var mask=new GraphicsPath())
                    {mask.AddPolygon(new PointF[]{new PointF(130,40),new PointF(437,40),new PointF(437,232),new PointF(225,232),new PointF(225,170),new PointF(130,170)});c.ornaments.Add(Cut(parts,new Rectangle(130,40,307,192),new RectangleF(299,123,217,152),1,new PointF(476,190),mask));}
                    using(var mask=new GraphicsPath())
                    {mask.AddPolygon(new PointF[]{new PointF(891,38),new PointF(1194,38),new PointF(1194,171),new PointF(1100,171),new PointF(1100,235),new PointF(891,235)});c.ornaments.Add(Cut(parts,new Rectangle(891,38,303,197),new RectangleF(802,122,237,151),3,new PointF(861,190),mask));}
                    c.ornaments.Add(Cut(parts,new Rectangle(8,171,266,449),new RectangleF(174,222,238,379),1,new PointF(476,190)));
                    c.ornaments.Add(Cut(parts,new Rectangle(1071,173,250,446),new RectangleF(952,221,218,373),3,new PointF(861,190)));
                }
                for(int eye=0;eye<2;eye++)
                {
                    int x=515,targetX=eye==0?515:743;
                    using(var p=new GraphicsPath())
                    {
                        p.AddBezier(x+4,558,x+25,550,x+68,548,x+86,559);
                        p.AddBezier(x+86,559,x+99,584,x+95,628,x+69,633);
                        p.AddBezier(x+69,633,x+49,639,x+18,636,x+9,618);
                        p.AddBezier(x+9,618,x-2,598,x-2,573,x+4,558);
                        c.iris[eye]=Cut(starEyes,new Rectangle(x-2,548,103,91),new RectangleF(targetX-2,548,103,91),5,PointF.Empty,p);
                    }
                    float bx=eye==0?500:747;
                    using(var p=new GraphicsPath())
                    {
                        p.AddBezier(bx,eye==0?479:459,bx+29,eye==0?463:451,bx+59,eye==0?456:451,bx+85,eye==0?459:464);
                        using(var pen=new Pen(Color.Black,6))p.Widen(pen);
                        Rectangle r=Rectangle.Ceiling(p.GetBounds());c.brows[eye]=Cut(original,r,r,6,new PointF(bx+40,462),p);
                    }
                }
                // 原稿的肩袖和圆手一起裁出，避免生成素材改变手型或袖口。
                c.arms[0]=OriginalCut(original,7,new PointF(557,728),new PointF(350,817),new PointF(393,778),new PointF(464,750),new PointF(540,722),new PointF(570,729),new PointF(581,918),new PointF(553,925),new PointF(495,869),new PointF(475,876),new PointF(391,878),new PointF(355,856));
                c.arms[1]=OriginalCut(original,8,new PointF(807,729),new PointF(774,846),new PointF(799,766),new PointF(837,726),new PointF(863,728),new PointF(944,777),new PointF(986,810),new PointF(1012,845),new PointF(989,858),new PointF(975,852),new PointF(951,867),new PointF(883,868),new PointF(850,847));
                c.arms[0].rest=Map(423,838);c.arms[1].rest=Map(916,838);
                // 共用同一个独立完整鼠标；颜色与两套红色衣装一致。
                using(var mouseSheet=new Bitmap(Path.Combine(directory,"casual-parts.png")))
                {
                    var region=new Rectangle(113,948,306,194);var destination=new RectangleF(339,835,163,101);
                    c.mouse=Cut(mouseSheet,region,destination,9,PointF.Empty);
                    for(int mask=1;mask<=3;mask++)using(var down=(Bitmap)mouseSheet.Clone())using(var g=Graphics.FromImage(down))using(var attributes=new ImageAttributes())
                    {
                        var color=new ColorMatrix();color.Matrix00=color.Matrix11=color.Matrix22=.60f;attributes.SetColorMatrix(color);
                        // 从角色视角看鼠标：左键对应画面右半边，右键对应左半边。
                        g.SetClip(mask==3?region:new Rectangle(region.X+(mask==1?153:0),region.Y,153,region.Height));
                        g.CompositingMode=CompositingMode.SourceCopy;g.DrawImage(mouseSheet,region,region.X,region.Y,region.Width,region.Height,GraphicsUnit.Pixel,attributes);
                        c.mouseDown[mask-1]=Cut(down,region,destination,9,PointF.Empty);
                    }
                }
                c.blinkEyes=Cut(closed,new Rectangle(434,513,469,129),new RectangleF(434,513,469,129),4,PointF.Empty);
                for(int mood=1;mood<6;mood++)
                using(var face=(Bitmap)plate.Clone())
                using(var g=Graphics.FromImage(face))
                {
                    g.SmoothingMode=SmoothingMode.AntiAlias;
                    if(mood==1||mood==2||mood==5)
                    {g.SetClip(new Rectangle(434,510,469,133));g.DrawImageUnscaled(closed,0,0);g.ResetClip();}
                    else if(mood==4)
                    {
                        foreach(float cx in new float[]{565,791})
                        {
                            var points=new PointF[65];for(int n=0;n<65;n++){float a=n*.20f,r=1+n*.45f;points[n]=new PointF(cx+(float)Math.Cos(a)*r,591+(float)Math.Sin(a)*r*.85f);}
                            using(var pen=new Pen(Color.FromArgb(146,102,123),3))g.DrawLines(pen,points);
                        }
                    }
                    if(mood!=2)
                    {
                        using(var fill=new SolidBrush(original.GetPixel(665,644)))g.FillRectangle(fill,632,648,80,54);
                        using(var pen=new Pen(Color.FromArgb(185,87,80),2.6f){StartCap=LineCap.Round,EndCap=LineCap.Round})
                        {
                            if(mood==3||mood==5)g.DrawEllipse(pen,660,663,16,22);
                            else if(mood==4)g.DrawCurve(pen,new PointF[]{new PointF(648,672),new PointF(658,667),new PointF(668,675),new PointF(678,667),new PointF(687,672)});
                            else g.DrawBezier(pen,648,670,660,678,677,678,688,670);
                        }
                    }
                    // 表情不覆盖独立眉毛、刘海和麦克风。
                    using(var mask=new GraphicsPath())
                    {mask.AddRectangle(new Rectangle(434,513,469,129));mask.AddRectangle(new Rectangle(631,647,82,57));c.moods[mood]=Cut(face,new Rectangle(434,513,469,192),new RectangleF(434,513,469,192),4,PointF.Empty,mask);}
                }
            }
            return c;
        }
        internal void Draw(Graphics g,int skin,MotionState pose,int mood,KeyboardModel keyboard=null,bool labels=false)
        {
            float[] matrix;using(Matrix m=g.Transform)matrix=m.Elements;float scale=(float)Math.Sqrt(matrix[0]*matrix[0]+matrix[1]*matrix[1]);
            canvas.ResizeTarget(Math.Max(1,(int)Math.Round(800*scale)),Math.Max(1,(int)Math.Round(680*scale)),800,680);
            canvas.Begin();Costume c=costumes[skin];mood=Math.Max(0,Math.Min(5,mood));
            DrawLayer(c.body,pose,0,PointF.Empty);
            foreach(var layer in c.ornaments)DrawLayer(layer,pose,layer.kind==1?pose.flowerLeft:pose.flowerRight,PointF.Empty);
            bool closed=mood==1||mood==2||mood==5||pose.blinkAmount>=.5f;
            if(mood>0)DrawLayer(c.moods[mood],pose,0,PointF.Empty);
            if((mood==0||mood==3)&&!closed)
            {
                for(int i=0;i<2;i++)
                {
                    DrawLayer(c.iris[i],pose,0,new PointF(pose.eyeX,pose.eyeY));
                    PointF center=c.eyeCenters[i];float pulse=.5f+.5f*(float)Math.Sin(pose.time*3.2+i*.8)*pose.idleWeight;shine.headShift=c.body.headShift;
                    shine.bounds=new RectangleF(center.X+pose.eyeX+10,center.Y+pose.eyeY-7,3+2*pulse,3+2*pulse);DrawLayer(shine,pose,0,PointF.Empty);
                }
            }
            else if((mood==0||mood==3)&&closed)DrawLayer(c.blinkEyes,pose,0,PointF.Empty);
            DrawLayer(c.brows[0],pose,pose.browLeftTilt,new PointF(0,pose.browLeftRise));DrawLayer(c.brows[1],pose,pose.browRightTilt,new PointF(0,pose.browRightRise));
            if(keyboard!=null)DrawKeys(keyboard,pose,labels,c);
            PointF mouseOffset=new PointF(V.Clamp(pose.mouseCenter.X-240,-18,18),V.Clamp(pose.mouseCenter.Y-555,-10,8));
            int buttonMask=(pose.leftButton?1:0)|(pose.rightButton?2:0);DrawLayer(buttonMask==0?c.mouse:c.mouseDown[buttonMask-1],pose,0,mouseOffset);
            DrawLayer(c.arms[0],pose,0,new PointF(mouseOffset.X,mouseOffset.Y+pose.mousePress*1.3f));
            DrawLayer(c.arms[1],pose,0,new PointF(0,pose.keyPress*2));
            canvas.Present(g,scale);
        }
        private void DrawKeys(KeyboardModel keyboard,MotionState pose,bool labels,Costume c)
        {
            using(var g=Graphics.FromImage(keyOverlay))
            {
                g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;
                RectangleF target=c.keyboardBounds;g.TranslateTransform(target.X,target.Y);g.ScaleTransform(target.Width/256,target.Height/38);g.TranslateTransform(-398,-542);
                keyboard.Draw(g,pose.litKeys,labels,false,!c.yellow);
            }
            canvas.DrawOverlay(keyOverlay,new RectangleF(0,0,800,680));
        }
        private void DrawLayer(Layer layer,MotionState pose,float degrees,PointF offset)
        {
            int count=layer.kind==0?32:12;float radians=degrees*(float)Math.PI/180,sin=(float)Math.Sin(radians),cos=(float)Math.Cos(radians);
            // 分行提交，遵守 GPU 顶点缓冲容量。
            for(int y=0;y<count;y++)
            {
                canvas.StartMesh(layer.texture,1);
                for(int x=0;x<count;x++)
                {Vertex(layer,pose,x,y,count,sin,cos,offset);Vertex(layer,pose,x+1,y,count,sin,cos,offset);Vertex(layer,pose,x,y+1,count,sin,cos,offset);Vertex(layer,pose,x+1,y,count,sin,cos,offset);Vertex(layer,pose,x+1,y+1,count,sin,cos,offset);Vertex(layer,pose,x,y+1,count,sin,cos,offset);}
                canvas.EndMesh();
            }
        }
        private void Vertex(Layer layer,MotionState pose,int x,int y,int count,float sin,float cos,PointF offset)
        {
            float u=x/(float)count,v=y/(float)count;PointF p=new PointF(layer.bounds.X+u*layer.bounds.Width,layer.bounds.Y+v*layer.bounds.Height);
            if(layer.kind==7||layer.kind==8)
            {
                PointF rest=layer.rest;p=ArmRig.TransformBound(p,layer.pivot,rest,V.Add(rest,offset),PointF.Empty);
            }
            else if(layer.kind==9)p=V.Add(p,offset);
            else
            {
                if(layer.kind==1||layer.kind==3||layer.kind==6)
                {float dx=p.X-layer.pivot.X,dy=p.Y-layer.pivot.Y;p=new PointF(layer.pivot.X+dx*cos-dy*sin,layer.pivot.Y+dx*sin+dy*cos);}
                p=V.Add(p,offset);
                // 角色映射到黄玲琳使用的头部坐标，复用相同的转头/呼吸/发梢函数。
                float sourceY=(p.Y+30)/.6f,sourceX=(p.X-3)/.6f;
                bool desk=layer.kind==0&&p.Y>=layer.deskTop&&sourceX>=159&&sourceX<=1165;
                if(!desk)
                {
                    PointF reference=new PointF(p.X,p.Y+layer.headShift);PointF moved=SurfaceRig.Transform(reference,pose);p=V.Add(p,V.Sub(moved,reference));
                }
            }
            canvas.Vertex(u,v,p);
        }
        public void Dispose(){keyOverlay.Dispose();canvas.Dispose();}
    }
}
