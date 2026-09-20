using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace DieYing
{
    /** <summary>花饰、眼睛和无外袖身体底图的二维网格；活动肩袖另由 ArmRig 绘制。仅由绘制线程调用。</summary> */
    internal sealed class LayeredRig : IDisposable
    {
        private sealed class Layer
        {
            internal uint texture;
            internal RectangleF bounds;
            internal PointF pivot;
            internal int kind;
        }
        private readonly GpuCanvas canvas;
        private readonly List<Layer> ornaments = new List<Layer>();
        private readonly Layer[] eyes = new Layer[2], faces = new Layer[6];
        private readonly Layer[] brows = new Layer[2];
        private readonly Layer body, shine;
        private readonly PointF[] eyeCenters;
        internal string Device { get { return canvas.device; } }
        internal GpuCanvas Canvas { get { return canvas; } }

        internal LayeredRig(string assetDirectory, bool butterfly, Bitmap[] expressions)
        {
            canvas = new GpuCanvas(800, 680);
            try
            {
                using (Bitmap source = new Bitmap(Path.Combine(assetDirectory, "layered", (butterfly ? "butterfly" : "classic") + "-plate.png")))
                using (Bitmap plate = new Bitmap(1600, 1360, PixelFormat.Format32bppPArgb))
                {
                    using (Graphics g = Graphics.FromImage(plate))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(source, new RectangleF(0, 0, 1600, 1600f * source.Height / source.Width));
                        // 面部使用完整的无眉底板，避免拼接边界切过上眼睑。
                        using(Bitmap cleanBrows=new Bitmap(Path.Combine(assetDirectory,"layered",(butterfly?"butterfly":"classic")+"-brow-base.png")))
                        {
                            GraphicsState saved=g.Save();
                            g.SetClip(new RectangleF(460,550,680,350));
                            g.DrawImage(cleanBrows,new RectangleF(0,0,1600,1600f*cleanBrows.Height/cleanBrows.Width));
                            g.Restore(saved);
                        }
                        // 保留原桌面，再以补绘内衬替换旧静态肩袖区域。
                        g.SetClip(new Rectangle(0, 908, 1600, 452));
                        g.CompositingMode = CompositingMode.SourceCopy; g.DrawImageUnscaled(expressions[0], 0, 0);
                        // 身体底图不再保留静态肩袖。袖子、前臂与手由同一连续网格绘制。
                        using(var underpaint=new Bitmap(Path.Combine(assetDirectory,"layered",(butterfly?"butterfly":"classic")+"-sleeveless.png")))
                        {
                            g.SetClip(new RectangleF(350,880,390,240));
                            g.DrawImage(underpaint,new RectangleF(0,0,1600,1600f*underpaint.Height/underpaint.Width));
                            if(butterfly)
                            {
                                g.SetClip(new RectangleF(830,880,390,240));
                                g.DrawImage(underpaint,new RectangleF(0,0,1600,1600f*underpaint.Height/underpaint.Width));
                            }
                        }
                    }
                    body = Whole(plate, 0);
                }
                using(Bitmap source=new Bitmap(Path.Combine(assetDirectory,"layered",(butterfly?"butterfly":"classic")+"-brows.png")))
                using(Bitmap sheet=new Bitmap(1600,1360,PixelFormat.Format32bppPArgb))
                using(Graphics g=Graphics.FromImage(sheet))
                {
                    g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                    g.DrawImage(source,new RectangleF(0,0,1600,1600f*source.Height/source.Width));
                    for(int i=0;i<2;i++)using(GraphicsPath path=new GraphicsPath())
                    {
                        path.AddRectangle(i==0?new RectangleF(230,275,155,68):new RectangleF(385,275,175,68));
                        brows[i]=Cut(sheet,path,6,i==0?new PointF(330,317):new PointF(455,320));
                    }
                }
                using(Bitmap originalOrnaments=new Bitmap(Path.Combine(assetDirectory,"layered",(butterfly ? "butterfly" : "classic")+"-ornaments.png")))
                using(Bitmap sheet=new Bitmap(1600,1360,PixelFormat.Format32bppPArgb))
                {
                    using(Graphics g=Graphics.FromImage(sheet))
                    {
                        g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                        g.DrawImage(originalOrnaments,new RectangleF(0,0,1600,1600f*originalOrnaments.Height/originalOrnaments.Width));
                    }
                    AddOrnament(sheet,butterfly ? new RectangleF(164,134,125,260) : new RectangleF(155,10,238,233),
                        butterfly ? new PointF(238,187) : new PointF(276,129),1);
                    AddOrnament(sheet,butterfly ? new RectangleF(550,165,127,275) : new RectangleF(478,33,214,278),
                        butterfly ? new PointF(604,215) : new PointF(562,151),3);
                }
                if (butterfly)
                {
                    // 眼球与眼白分层，保留原画虹膜；高光在另一个纹理层中绘制。
                    eyeCenters = new PointF[] {new PointF(334,374), new PointF(483,386)};
                    eyes[0] = Iris(expressions[0], new RectangleF(300,345,65,57), -7);
                    eyes[1] = Iris(expressions[0], new RectangleF(448,360,73,52), -1);
                }
                else
                {
                    eyeCenters = new PointF[] {new PointF(321,376), new PointF(474,376)};
                    eyes[0] = Iris(expressions[0], new RectangleF(285,351,68,56), 0);
                    eyes[1] = Iris(expressions[0], new RectangleF(435,351,76,56), 0);
                }
                for (int i = 1; i < expressions.Length; i++)
                {
                    using (GraphicsPath region = new GraphicsPath())
                    {
                        // 完整覆盖眼睑和眼尾；输入表情已去眉，再叠加独立眉毛。
                        region.AddRectangle(new RectangleF(230,320,340,130));
                        faces[i] = Cut(expressions[i], region, 0, PointF.Empty);
                    }
                }
                using (Bitmap sparkle = new Bitmap(48,48,PixelFormat.Format32bppPArgb))
                {
                    using(Graphics g=Graphics.FromImage(sparkle))
                    {
                        g.SmoothingMode=SmoothingMode.AntiAlias;
                        using(GraphicsPath diamond=new GraphicsPath())
                        {
                            diamond.AddPolygon(new PointF[]{new PointF(24,2),new PointF(28,19),new PointF(44,24),new PointF(28,28),new PointF(24,45),new PointF(19,28),new PointF(3,24),new PointF(19,19)});
                            using(Brush glow=new SolidBrush(Color.FromArgb(65,255,229,165)))g.FillEllipse(glow,6,6,36,36);
                            g.FillPath(Brushes.White,diamond);
                        }
                    }
                    shine=Whole(sparkle,0,1);
                }
            }
            catch { canvas.Dispose(); throw; }
        }

        private Layer Whole(Bitmap bitmap, int kind, float pixelScale=2)
        {
            return new Layer { texture=canvas.Upload(bitmap), bounds=new RectangleF(0,0,bitmap.Width/pixelScale,bitmap.Height/pixelScale), kind=kind };
        }
        private void AddOrnament(Bitmap sheet, RectangleF bounds, PointF pivot, int kind)
        {
            using(GraphicsPath path=new GraphicsPath())
            {
                path.AddRectangle(bounds);ornaments.Add(Cut(sheet,path,kind,pivot));
            }
        }
        private Layer Iris(Bitmap source, RectangleF rect, float slant)
        {
            using(GraphicsPath path=new GraphicsPath())
            {
                path.AddBezier(rect.Left+5,rect.Top,rect.Left+22,rect.Top-2+slant,rect.Right-12,rect.Top+2,rect.Right-4,rect.Top+7);
                path.AddBezier(rect.Right-4,rect.Top+7,rect.Right+2,rect.Bottom-22,rect.Right-3,rect.Bottom-3,rect.Right-14,rect.Bottom-2);
                path.AddBezier(rect.Right-14,rect.Bottom-2,rect.Left+35,rect.Bottom+1,rect.Left+19,rect.Bottom+1,rect.Left+12,rect.Bottom-4);
                path.AddBezier(rect.Left+12,rect.Bottom-4,rect.Left+2,rect.Bottom-13,rect.Left,rect.Top+19,rect.Left+5,rect.Top);
                return Cut(source,path,5,new PointF(rect.X+rect.Width/2,rect.Y+rect.Height/2));
            }
        }
        private Layer Cut(Bitmap source, GraphicsPath path, int kind, PointF pivot)
        {
            Rectangle bounds=Rectangle.Ceiling(path.GetBounds()); bounds.Inflate(kind>=1 && kind<=4 ? 9 : 2,kind>=1 && kind<=4 ? 9 : 2);
            using(Bitmap cut=new Bitmap(bounds.Width*2,bounds.Height*2,PixelFormat.Format32bppPArgb))
            using(Graphics g=Graphics.FromImage(cut))
            {
                g.SmoothingMode=SmoothingMode.AntiAlias;
                g.ScaleTransform(2,2);
                g.TranslateTransform(-bounds.X,-bounds.Y);
                using(TextureBrush brush=new TextureBrush(source,WrapMode.Clamp))
                {
                    brush.ScaleTransform(.5f,.5f);
                    // 花瓣尖和深色描边也属于部件，留出轮廓余量，防止多边形裁掉边尖。
                    if(kind>=1 && kind<=4)using(Pen margin=new Pen(brush,10){LineJoin=LineJoin.Round})g.DrawPath(margin,path);
                    g.FillPath(brush,path);
                }
                return new Layer {texture=canvas.Upload(cut),bounds=bounds,kind=kind,pivot=pivot};
            }
        }

        internal void Draw(Graphics g, MotionState pose, int expression,Bitmap desk,Bitmap mouseLayer,ArmRig arms,PointF mouseContact,PointF keyContact)
        {
            float[] transform;
            using(Matrix matrix=g.Transform)transform=matrix.Elements;
            float renderScale=(float)Math.Sqrt(transform[0]*transform[0]+transform[1]*transform[1]);
            canvas.ResizeTarget(Math.Max(1,(int)Math.Round(800*renderScale)),Math.Max(1,(int)Math.Round(680*renderScale)),800,680);
            canvas.Begin(); DrawLayer(body,pose,1,0,PointF.Empty);
            foreach(Layer layer in ornaments)DrawOrnament(layer,pose);
            // 整帧切换闭眼，避免淡入淡出把两组睫毛同时画在脸上。
            float closed=expression==1 || expression==2 || pose.blinkAmount>=.5f ? 1 : 0;
            if(expression==0 || expression==3)
            {
                for(int i=0;i<eyes.Length;i++)
                {
                    DrawLayer(eyes[i],pose,1-closed,0,new PointF(pose.eyeX,pose.eyeY));
                    PointF center=eyeCenters[i];
                    float pulse=.5f+.5f*(float)Math.Sin(pose.time*3.2+i*.8)*pose.idleWeight;
                    // 高光有独立位置、大小和透明度，仅在睁眼虹膜内部显示。
                    RectangleF saved=shine.bounds;
                    float size=7+3*pulse;
                    shine.bounds=new RectangleF(center.X+12+pose.eyeX-size/2,center.Y-8+pose.eyeY-size/2,size,size);
                    DrawLayer(shine,pose,(.45f+.5f*pulse)*(1-closed),0,PointF.Empty);
                    shine.bounds=new RectangleF(center.X-8+pose.eyeX-2.5f,center.Y+9+pose.eyeY-2.5f,5,5);
                    DrawLayer(shine,pose,(.85f-.4f*pulse)*(1-closed),0,PointF.Empty); shine.bounds=saved;
                }
            }
            if(expression==0 && closed>0)DrawLayer(faces[1],pose,closed,0,PointF.Empty);
            else if(expression>0)DrawLayer(faces[expression],pose,1,0,PointF.Empty);
            DrawLayer(brows[0],pose,1,pose.browLeftTilt,new PointF(0,pose.browLeftRise));
            DrawLayer(brows[1],pose,1,pose.browRightTilt,new PointF(0,pose.browRightRise));
            canvas.DrawOverlay(desk,new RectangleF(0,430,800,180));
            canvas.DrawOverlay(mouseLayer,new RectangleF(pose.mouseCenter.X-70,pose.mouseCenter.Y-40,140,100));
            arms.Draw(pose,mouseContact,keyContact);
            canvas.Present(g,renderScale);
        }
        private void DrawOrnament(Layer layer,MotionState pose)
        {
            bool left=layer.kind<3;
            float angle=left ? pose.flowerLeft : pose.flowerRight;
            DrawLayer(layer,pose,1,angle,PointF.Empty);
        }
        private void DrawLayer(Layer layer,MotionState pose,float opacity,float angle,PointF offset)
        {
            if(layer==null || opacity<=.001f)return;
            int columns=layer==body ? 32 : 8, rows=layer==body ? 32 : 8;
            float radians=angle*(float)Math.PI/180, sin=(float)Math.Sin(radians), cos=(float)Math.Cos(radians);
            canvas.StartMesh(layer.texture,opacity);
            for(int y=0;y<rows;y++)for(int x=0;x<columns;x++)
            {
                Emit(layer,pose,x,y,columns,rows,sin,cos,offset);
                Emit(layer,pose,x+1,y,columns,rows,sin,cos,offset);
                Emit(layer,pose,x,y+1,columns,rows,sin,cos,offset);
                Emit(layer,pose,x+1,y,columns,rows,sin,cos,offset);
                Emit(layer,pose,x+1,y+1,columns,rows,sin,cos,offset);
                Emit(layer,pose,x,y+1,columns,rows,sin,cos,offset);
            }
            canvas.EndMesh();
        }
        private void Emit(Layer layer,MotionState pose,int x,int y,int columns,int rows,float sin,float cos,PointF offset)
        {
            float u=x/(float)columns,v=y/(float)rows;
            PointF p=new PointF(layer.bounds.X+u*layer.bounds.Width,layer.bounds.Y+v*layer.bounds.Height);
            if(layer.kind>=1 && layer.kind<=4)
            {
                float dx=p.X-layer.pivot.X,dy=p.Y-layer.pivot.Y;
                float tail=V.Smooth(dy/125);
                dx += tail * (float)Math.Sin(pose.time*2.5+(layer.kind==1?0:1)) * pose.idleWeight * 2.2f;
                p=new PointF(layer.pivot.X+dx*cos-dy*sin,layer.pivot.Y+dx*sin+dy*cos);
            }
            else if(layer.kind==6)
            {
                float dx=p.X-layer.pivot.X,dy=p.Y-layer.pivot.Y;
                p=new PointF(layer.pivot.X+dx*cos-dy*sin,layer.pivot.Y+dx*sin+dy*cos);
            }
            p.X+=offset.X;p.Y+=offset.Y;
            p=SurfaceRig.Transform(p,pose);
            canvas.Vertex(u,v,p);
        }
        public void Dispose() { canvas.Dispose(); }
    }
}
