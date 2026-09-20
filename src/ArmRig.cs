using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace DieYing
{
    /** <summary>肩袖、前臂和圆手共享连续网格；左右手采用各自的肩点与运动权重。仅在绘制线程使用。</summary> */
    internal sealed class ArmRig
    {
        private readonly GpuCanvas canvas;
        private readonly uint[] textures=new uint[2];
        private readonly RectangleF[] bounds={new RectangleF(180,430,190,175),new RectangleF(440,430,180,175)};
        private readonly bool butterfly;
        private const int columns=24,rows=18;
        private readonly PointF[] mesh=new PointF[(columns+1)*(rows+1)];
        internal ArmRig(string directory,bool butterfly,GpuCanvas sharedCanvas)
        {
            this.butterfly=butterfly;canvas=sharedCanvas;
            using(var source=new Bitmap(Path.Combine(directory,"layered",butterfly?"butterfly-sleeves.png":"classic-bound-arms.png")))
            for(int i=0;i<(butterfly?2:1);i++)
            using(var cut=new Bitmap((int)bounds[0].Width*2,(int)bounds[0].Height*2,PixelFormat.Format32bppPArgb))
            using(var g=Graphics.FromImage(cut))
            {
                g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                g.ScaleTransform(2,2);g.TranslateTransform(-bounds[0].X,-bounds[0].Y);
                RectangleF region=i==0?new RectangleF(240,725,435,380):new RectangleF(850,725,395,380);
                PointF sourceRoot=i==0?new PointF(596,784):new PointF(925,786);
                PointF sourceTip=i==0?new PointF(350,1084):new PointF(1136,1084);
                PointF targetRoot=Shoulder(true);
                PointF targetTip=i==0?new PointF(240,548):new PointF(549,548);
                if(butterfly)
                {
                    region=new RectangleF(60,270,550,600);
                    sourceRoot=new PointF(534,404);
                    sourceTip=new PointF(229,751);
                    targetTip=new PointF(286,497);
                }
                PointF[] corners={new PointF(region.Left,region.Top),new PointF(region.Right,region.Top),new PointF(region.Left,region.Bottom)};
                for(int j=0;j<3;j++)corners[j]=Place(corners[j],sourceRoot,sourceTip,targetRoot,targetTip,butterfly?.14f:.40f);
                if(butterfly)
                using(var forearm=new GraphicsPath())
                using(var fill=new LinearGradientBrush(new PointF(262,487),new PointF(276,526),Color.FromArgb(255,234,225),Color.FromArgb(250,207,198)))
                using(var outline=new Pen(Color.FromArgb(104,67,61),2.3f))
                {
                    // 短前臂从袖口内侧伸出；末端埋进圆手，袖口覆盖肩侧连接。
                    forearm.AddBezier(295,480,281,487,255,501,237,508);
                    forearm.AddBezier(237,508,224,516,236,536,249,531);
                    forearm.AddBezier(249,531,268,521,291,513,305,504);
                    forearm.CloseFigure();g.SmoothingMode=SmoothingMode.AntiAlias;
                    g.FillPath(fill,forearm);g.DrawPath(outline,forearm);
                }
                var sleeveState=g.Save();
                if(!butterfly)
                using(var oldHand=new GraphicsPath())
                {
                    PointF[] outline={new PointF(235,914),new PointF(334,914),new PointF(420,952),new PointF(449,1001),new PointF(394,1110),new PointF(235,1110)};
                    for(int j=0;j<outline.Length;j++)outline[j]=Place(outline[j],sourceRoot,sourceTip,targetRoot,targetTip,.40f);
                    oldHand.AddPolygon(outline);g.SetClip(oldHand,CombineMode.Exclude);
                }
                g.DrawImage(source,corners,region,GraphicsUnit.Pixel);g.Restore(sleeveState);
                using(var paws=new Bitmap(Path.Combine(directory,"approved","paws-mouse.png")))
                {
                    Rectangle paw=butterfly?(i==0?new Rectangle(722,193,447,301):new Rectangle(96,820,452,301)):new Rectangle(96,193,452,301);
                    // 先在静止坐标合成成一张连续纹理，再一起绑定；袖口藏进手套后侧。
                    g.DrawImage(paws,new RectangleF(196.5f,490,87,58),paw,GraphicsUnit.Pixel);
                }
                textures[i]=canvas.Upload(cut);
            }
        }
        private static PointF Place(PointF p,PointF sourceRoot,PointF sourceTip,PointF targetRoot,PointF targetTip,float widthScale)
        {
            PointF from=V.Sub(sourceTip,sourceRoot),to=V.Sub(targetTip,targetRoot),offset=V.Sub(p,sourceRoot);
            float t=V.Dot(offset,from)/V.Dot(from,from),side=V.Dot(offset,V.Perp(V.Unit(from)));
            return V.Add(V.Add(targetRoot,V.Mul(to,t)),V.Mul(V.Perp(V.Unit(to)),side*widthScale));
        }
        internal static PointF Shoulder(bool mouseHand){return mouseHand?new PointF(327,477):new PointF(462,477);}
        /** <summary>同一变形函数用于肩袖、前臂与手掌顶点，保证部件之间没有独立拼接边界。</summary> */
        internal static PointF Transform(PointF point,PointF contact,bool mouseHand,bool butterfly,MotionState pose)
        {
            PointF root=Shoulder(mouseHand);
            PointF rest=mouseHand?new PointF(240,548):new PointF(549,548);
            PointF shoulderOffset=V.Sub(SurfaceRig.Transform(root,pose),root);
            return TransformBound(point,root,rest,contact,shoulderOffset);
        }
        /** <summary>共用肩腕绑定；不同衣装提供原画肩点和手掌中心，掌面只平移。</summary> */
        internal static PointF TransformBound(PointF point,PointF root,PointF rest,PointF contact,PointF shoulderOffset)
        {
            PointF baseAxis=V.Sub(rest,root), local=V.Sub(point,root);
            float along=V.Dot(local,baseAxis)/V.Dot(baseAxis,baseAxis);
            PointF translation=V.Sub(contact,rest);
            const float handStart=.70f;
            if(along>=handStart)return V.Add(point,translation);
            // 鼠标仅有小范围平移。肩点跟随身体，袖口跟随手腕；不旋转掌面或扭曲袖管。
            // 肩头前20%固定，主要在袖管中段过渡，避免泡泡袖随掌心整块挤扁。
            float weight=V.Smooth((along-.20f)/(handStart-.20f));
            return V.Add(point,V.Lerp(shoulderOffset,translation,weight));
        }
        internal void Draw(MotionState pose,PointF mouseContact,PointF keyContact)
        {
            for(int i=0;i<(butterfly?2:1);i++)
            {
                canvas.StartMesh(textures[i],1);
                // 每个共享顶点只变形一次，三角形重复引用缓存，避免六次计算相同关节。
                for(int y=0;y<=rows;y++)for(int x=0;x<=columns;x++)
                {
                    PointF sourcePoint=new PointF(bounds[0].X+x/(float)columns*bounds[0].Width,bounds[0].Y+y/(float)rows*bounds[0].Height);
                    if(i==1)sourcePoint.X=789-sourcePoint.X;
                    PointF p=Transform(sourcePoint,i==0?mouseContact:keyContact,i==0,butterfly,pose);
                    mesh[y*(columns+1)+x]=p;
                }
                for(int y=0;y<rows;y++)for(int x=0;x<columns;x++)
                {
                    Emit(i,x,y,columns,rows,pose,i==0?mouseContact:keyContact);
                    Emit(i,x+1,y,columns,rows,pose,i==0?mouseContact:keyContact);
                    Emit(i,x,y+1,columns,rows,pose,i==0?mouseContact:keyContact);
                    Emit(i,x+1,y,columns,rows,pose,i==0?mouseContact:keyContact);
                    Emit(i,x+1,y+1,columns,rows,pose,i==0?mouseContact:keyContact);
                    Emit(i,x,y+1,columns,rows,pose,i==0?mouseContact:keyContact);
                }
                canvas.EndMesh();
            }
        }
        private void Emit(int arm,int x,int y,int columns,int rows,MotionState pose,PointF contact)
        {
            float u=x/(float)columns,v=y/(float)rows;
            canvas.Vertex(u,v,mesh[y*(columns+1)+x]);
        }
    }
}
