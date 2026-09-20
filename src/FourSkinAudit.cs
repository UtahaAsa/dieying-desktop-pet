using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

namespace DieYing
{
    /** <summary>验证四套实际渲染的眨眼、鼠标反馈与键盘，并输出审阅图；只写指定目录。</summary> */
    internal static class FourSkinAudit
    {
        private static Bitmap Render(SceneRenderer scene,int skin,MotionState pose,int mood)
        {
            Bitmap b=new Bitmap(800,680,PixelFormat.Format32bppArgb);
            using(Graphics g=Graphics.FromImage(b)){g.Clear(Color.FromArgb(250,247,238));scene.Draw(g,new AppSettings{skin=skin,expression=mood,showKeyBubble=false,keyLabels=true},pose);}
            return b;
        }
        private static MotionState Pose(){return new MotionState{mouseCenter=new PointF(240,555)};}
        private static int Diff(Bitmap a,Bitmap b,Rectangle r)
        {int n=0;for(int y=r.Top;y<r.Bottom;y++)for(int x=r.Left;x<r.Right;x++)if(a.GetPixel(x,y)!=b.GetPixel(x,y))n++;return n;}
        private static double Light(Bitmap b,Rectangle r)
        {double n=0;for(int y=r.Top;y<r.Bottom;y++)for(int x=r.Left;x<r.Right;x++){Color c=b.GetPixel(x,y);n+=c.R+c.G+c.B;}return n/(r.Width*r.Height);}
        private static void Check(StringBuilder text,bool ok,string name){text.AppendLine((ok?"PASS ":"FAIL ")+name);}
        internal static void Run(string folder)
        {
            var report=new StringBuilder("FOUR SKIN RENDER AUDIT\r\nProgrammed input, not physical hardware evidence.\r\n");
            using(var scene=new SceneRenderer(Path.Combine(Program.root,"assets")))
            using(var sheet=new Bitmap(1600,1360))using(var gallery=Graphics.FromImage(sheet))
            {
                for(int skin=0;skin<4;skin++)
                {
                    var left=Pose();left.leftButton=true;var right=Pose();right.rightButton=true;var blink=Pose();blink.blinkAmount=1;
                    Rectangle mouth=skin==0?new Rectangle(352,299,35,23):skin==1?new Rectangle(373,299,35,23):new Rectangle(382,360,47,30);
                    Rectangle ml=skin==0?new Rectangle(233,433,15,8):skin==1?new Rectangle(242,437,15,8):new Rectangle(219,511,15,8);
                    Rectangle mr=skin==0?new Rectangle(275,433,15,8):skin==1?new Rectangle(287,437,15,8):new Rectangle(279,511,15,8);
                    using(var natural=Render(scene,skin,Pose(),0))using(var closed=Render(scene,skin,blink,0))
                    using(var ld=Render(scene,skin,left,0))using(var rd=Render(scene,skin,right,0))
                    using(var released=Render(scene,skin,Pose(),0))
                    {
                        Check(report,Diff(natural,closed,mouth)==0,"automatic blink preserves natural mouth skin="+skin);
                        Check(report,Light(ld,mr)<Light(natural,mr)-15,"left button darkens character-left half skin="+skin);
                        Check(report,Light(rd,ml)<Light(natural,ml)-15,"right button darkens character-right half skin="+skin);
                        Check(report,Diff(natural,ld,ml)==0&&Diff(natural,rd,mr)==0,"other mouse half stays bright skin="+skin);
                        Check(report,Diff(natural,released,new Rectangle(0,0,800,680))==0,"release restores original skin="+skin);
                        gallery.DrawImageUnscaled(natural,skin%2*800,skin/2*680);
                        natural.Save(Path.Combine(folder,"skin"+skin+"-natural.png"));ld.Save(Path.Combine(folder,"skin"+skin+"-left.png"));rd.Save(Path.Combine(folder,"skin"+skin+"-right.png"));
                    }
                    using(var surprise=Render(scene,skin,Pose(),3))using(var surpriseBlink=Render(scene,skin,blink,3))
                        Check(report,Diff(surprise,surpriseBlink,mouth)==0,"automatic blink preserves surprised mouth skin="+skin);
                    using(var keys=new Bitmap(1600,680))using(var kg=Graphics.FromImage(keys))
                    {
                        int i=0;foreach(var cap in scene.keyboard.Keys)
                        {
                            var p=Pose();p.litKeys[cap.id]=true;
                            using(var b=Render(scene,skin,p,0))using(var neutral=Render(scene,skin,Pose(),0))
                            {Rectangle area=skin>=2?new Rectangle(393,481,258,51):skin==0?new Rectangle(344,401,207,51):new Rectangle(360,405,215,51);Check(report,Diff(neutral,b,area)>5,"visible key feedback skin="+skin+" key="+cap.label);kg.DrawImage(b,new Rectangle(i%7*228,i/7*340,228,194));}
                            i++;
                        }
                        keys.Save(Path.Combine(folder,"skin"+skin+"-keys.png"));
                    }
                    for(int mood=1;mood<6;mood++)using(var b=Render(scene,skin,Pose(),mood))b.Save(Path.Combine(folder,"skin"+skin+"-mood"+mood+".png"));
                    for(int corner=0;corner<4;corner++)
                    {var p=Pose();p.mouseCenter=new PointF(240+(corner%2==0?-18:18),555+(corner<2?-10:8));p.flowerLeft=7;p.flowerRight=-7;p.headTilt=2;using(var b=Render(scene,skin,p,0))b.Save(Path.Combine(folder,"skin"+skin+"-corner"+corner+".png"));}
                }
                sheet.Save(Path.Combine(folder,"four-skins.png"));
            }
            File.WriteAllText(Path.Combine(folder,"four-skin-checks.txt"),report.ToString());
            if(report.ToString().Contains("FAIL"))throw new InvalidOperationException("Four skin render checks failed");
        }
    }
}
