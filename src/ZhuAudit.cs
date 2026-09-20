using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
namespace DieYing
{
    internal static class ZhuAudit
    {
        private static Bitmap Render(LayeredCharacterRig rig,int skin,MotionState pose,int mood)
        {var b=new Bitmap(800,680,PixelFormat.Format32bppArgb);using(var g=Graphics.FromImage(b))rig.Draw(g,skin,pose,mood);return b;}
        private static MotionState Pose(){return new MotionState{mouseCenter=new PointF(240,555)};}
        private static int Difference(Bitmap a,Bitmap b,Rectangle r)
        {int n=0;for(int y=r.Top;y<r.Bottom;y++)for(int x=r.Left;x<r.Right;x++)if(a.GetPixel(x,y)!=b.GetPixel(x,y))n++;return n;}
        private static void Check(StringBuilder report,bool ok,string text)
        {report.AppendLine((ok?"PASS ":"FAIL ")+text);}
        internal static void Run(string folder)
        {
            var log=new StringBuilder();using(var rig=new LayeredCharacterRig(Path.Combine(Program.root,"assets","zhu")))
            {
                log.AppendLine(rig.Device);
                for(int skin=2;skin<4;skin++)using(var neutral=Render(rig,skin,Pose(),0))
                {
                    neutral.Save(Path.Combine(folder,"skin"+skin+"-neutral.png"));
                    var p=Pose();p.flowerLeft=8;p.flowerRight=-8;
                    using(var b=Render(rig,skin,p,0))
                    {b.Save(Path.Combine(folder,"skin"+skin+"-ornaments.png"));Check(log,Difference(neutral,b,new Rectangle(65,35,675,245))>200,"independent ornaments "+skin);Check(log,Difference(neutral,b,new Rectangle(280,295,260,110))==0,"ornaments preserve face "+skin);Check(log,Difference(neutral,b,new Rectangle(170,410,460,205))==0,"ornaments preserve hands and desk "+skin);}
                    p=Pose();p.eyeX=3;p.eyeY=1;
                    using(var b=Render(rig,skin,p,0))
                    {b.Save(Path.Combine(folder,"skin"+skin+"-eyes.png"));Check(log,Difference(neutral,b,new Rectangle(300,298,215,61))>100,"independent gaze "+skin);Check(log,Difference(neutral,b,new Rectangle(70,30,660,240))==0,"gaze preserves ornaments "+skin);}
                    p=Pose();p.mouseCenter=new PointF(264,569);
                    using(var b=Render(rig,skin,p,0))
                    {b.Save(Path.Combine(folder,"skin"+skin+"-mouse.png"));Check(log,Difference(neutral,b,new Rectangle(190,403,190,145))>100,"mouse and bound sleeve move "+skin);Check(log,Difference(neutral,b,new Rectangle(470,401,145,110))==0,"mouse leaves keyboard hand unchanged "+skin);Check(log,Difference(neutral,b,new Rectangle(110,550,570,50))==0,"mouse leaves desk trim unchanged "+skin);}
                    p=Pose();p.browLeftRise=-3;p.browLeftTilt=4;
                    using(var b=Render(rig,skin,p,0))
                    {Check(log,Difference(neutral,b,new Rectangle(290,230,75,35))>10,"left eyebrow independently moves "+skin);Check(log,Difference(neutral,b,new Rectangle(435,230,75,35))==0,"left brow preserves right brow "+skin);Check(log,Difference(neutral,b,new Rectangle(290,290,230,70))==0,"brow preserves eyes "+skin);}
                    p=Pose();p.keyPress=1;
                    using(var b=Render(rig,skin,p,0))
                    {Check(log,Difference(neutral,b,new Rectangle(470,401,145,110))>50,"keyboard hand taps "+skin);Check(log,Difference(neutral,b,new Rectangle(190,403,190,145))==0,"keyboard tap preserves mouse hand "+skin);}
                    Bitmap previous=null;
                    try{for(int mood=1;mood<6;mood++)
                    {var b=Render(rig,skin,Pose(),mood);b.Save(Path.Combine(folder,"skin"+skin+"-mood"+mood+".png"));Check(log,Difference(neutral,b,new Rectangle(263,278,281,119))>50,"mood differs from natural "+skin+"/"+mood);if(previous!=null){Check(log,Difference(previous,b,new Rectangle(263,278,281,119))>20,"adjacent moods distinct "+skin+"/"+mood);previous.Dispose();}previous=b;}}finally{if(previous!=null)previous.Dispose();}
                }
                using(var b=new Bitmap(900,765))using(var g=Graphics.FromImage(b))
                {g.ScaleTransform(1.125f,1.125f);var timer=Stopwatch.StartNew();for(int n=0;n<120;n++){g.Clear(Color.Transparent);var p=Pose();p.time=n/60.0;p.flowerLeft=(float)Math.Sin(n*.1)*5;p.eyeX=2;rig.Draw(g,2+n%2,p,0);}log.AppendLine("900px offscreen average ms="+(timer.Elapsed.TotalMilliseconds/120).ToString("F2"));}
            }
            File.WriteAllText(Path.Combine(folder,"zhu-checks.txt"),log.ToString(),Encoding.UTF8);
            if(log.ToString().Contains("FAIL"))throw new InvalidOperationException("Zhu behavioral checks failed");
        }
    }
}
