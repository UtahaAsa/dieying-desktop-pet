using System;
using System.Drawing;
using System.Text;

namespace DieYing
{
    /** <summary>检查本轮新增的头部、头发、表情及固定桌面；手部沿用原有审计。</summary> */
    internal static class DynamicsAudit
    {
        internal static string Run(SceneRenderer renderer)
        {
            StringBuilder report = new StringBuilder("DYNAMICS AUDIT\r\n");
            AppSettings settings = new AppSettings(); MotionState pose = new MotionState();
            InputFrame input = new InputFrame {cursorX=1,cursorY=-1};
            for(int i=0;i<180;i++) pose.Update(input,settings,renderer.keyboard,i/120.0);
            Check(report,pose.leanX>5 && pose.headTilt>1 && Math.Abs(pose.hairSwing)>.1,"head/hair react to cursor");
            PointF head=SurfaceRig.Transform(new PointF(400,200),pose);
            Check(report,V.Len(V.Sub(head,new PointF(400,200)))>3,"head actually changes geometry");
            foreach(PointF p in new PointF[]{new PointF(249,515),new PointF(550,515),new PointF(400,600)})
                Check(report,V.Len(V.Sub(SurfaceRig.Transform(p,pose),p))<.001f,"body underpaint/desk remains fixed at "+p);
            settings.paused=true; float lean=pose.leanX,tilt=pose.headTilt,hair=pose.hairSwing; double time=pose.time;
            input.cursorX=-1; pose.Update(input,settings,renderer.keyboard,3);
            Check(report,lean==pose.leanX && tilt==pose.headTilt && hair==pose.hairSwing && time==pose.time,"pause freezes dynamics clock");
            settings.paused=false; settings.idleMotion=false; settings.mouseEnabled=false;
            for(int i=0;i<600;i++) pose.Update(input,settings,renderer.keyboard,3.1+i/120.0);
            Check(report,Math.Abs(pose.leanX)<.01 && Math.Abs(pose.headTilt)<.01 && Math.Abs(pose.hairSwing)<.01,"disabled mouse and idle settle at neutral");
            for(int skin=0;skin<2;skin++)
            {
                using(Bitmap neutral=Render(renderer,skin,0,new MotionState()))
                {
                    MotionState moving=new MotionState{leanX=8,headTilt=2,leanY=2,hairSwing=-5};
                    using(Bitmap changed=Render(renderer,skin,0,moving))
                    {
                        Check(report,Difference(neutral,changed,new Rectangle(130,70,540,350))>1000,"rendered head changes skin "+skin);
                        Check(report,Difference(neutral,changed,new Rectangle(220,615,340,24))==0,"rendered desk stable skin "+skin);
                    }
                    for(int mood=1;mood<5;mood++)
                        using(Bitmap changed=Render(renderer,skin,mood,new MotionState()))
                            Check(report,Difference(neutral,changed,new Rectangle(250,320,300,130))>60,"visible expression "+skin+"/"+mood);
                }
            }
            using(Icon icon=TrayArt.MakeIcon(0)) Check(report,icon.Width==32 && icon.Height==32,"tray icon handle is usable");
            return report.ToString();
        }
        private static Bitmap Render(SceneRenderer scene,int skin,int mood,MotionState pose)
        {
            pose.SetReviewPose(scene.keyboard,65,PointF.Empty,false,0);
            Bitmap b=new Bitmap(800,720);
            using(Graphics g=Graphics.FromImage(b)) scene.Draw(g,new AppSettings{skin=skin,expression=mood},pose);
            return b;
        }
        private static int Difference(Bitmap a,Bitmap b,Rectangle region)
        {
            int count=0;
            for(int y=region.Top;y<region.Bottom;y+=2) for(int x=region.Left;x<region.Right;x+=2)
                if(a.GetPixel(x,y)!=b.GetPixel(x,y)) count++;
            return count;
        }
        private static void Check(StringBuilder report,bool okay,string name) { report.AppendLine((okay?"PASS ":"FAIL ")+name); }
    }
}
