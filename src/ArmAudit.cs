using System;
using System.Drawing;
using System.Text;
namespace DieYing
{
    /** <summary>检查肩袖绑定的接触点、袖管宽度和袖手过渡连续性；不替代视觉验收。</summary> */
    internal static class ArmAudit
    {
        internal static string Run(KeyboardModel keyboard)
        {
            var report=new StringBuilder("SHOULDER SLEEVE BINDING AUDIT\r\n");
            for(int skin=0;skin<2;skin++)for(int hand=0;hand<1;hand++)
            {
                bool mouse=hand==0,contactOk=true,widthOk=true,continuous=true;
                PointF root=ArmRig.Shoulder(mouse),rest=mouse?new PointF(240,548):new PointF(549,548);
                PointF axis=V.Sub(rest,root),normal=V.Perp(V.Unit(axis));
                var targets=new System.Collections.Generic.List<PointF>();
                if(mouse){for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)targets.Add(new PointF(240+x*24,547+y*14));}
                else foreach(KeyCap cap in keyboard.Keys)targets.Add(cap.center);
                foreach(PointF target in targets)
                {
                    var pose=new MotionState{headTilt=3,leanX=2,leanY=1};
                    contactOk &= V.Len(V.Sub(ArmRig.Transform(rest,target,mouse,skin==1,pose),target))<.01f;
                    for(int step=1;step<8;step++)
                    {
                        PointF center=V.Add(root,V.Mul(axis,step*.1f));
                        PointF a=ArmRig.Transform(V.Add(center,V.Mul(normal,12)),target,mouse,skin==1,pose);
                        PointF b=ArmRig.Transform(V.Add(center,V.Mul(normal,-12)),target,mouse,skin==1,pose);
                        widthOk &= Math.Abs(V.Len(V.Sub(a,b))-24)<.05f;
                    }
                    for(int edge=-1;edge<=1;edge++)
                    {
                        PointF a=V.Add(V.Add(root,V.Mul(axis,.7799f)),V.Mul(normal,edge*15));
                        PointF b=V.Add(V.Add(root,V.Mul(axis,.7801f)),V.Mul(normal,edge*15));
                        continuous &= V.Len(V.Sub(ArmRig.Transform(a,target,mouse,skin==1,pose),ArmRig.Transform(b,target,mouse,skin==1,pose)))<.3f;
                    }
                }
                string name=" skin="+skin+" "+(mouse?"mouse":"keyboard");
                Check(report,contactOk,"hand contact follows actual target"+name);
                Check(report,widthOk,"sleeve cross section does not collapse at extreme targets"+name);
                Check(report,continuous,"cuff to hand mesh has continuous edges"+name);
                PointF sleeve=V.Add(root,V.Mul(axis,.4f));
                float travel=V.Len(V.Sub(ArmRig.Transform(sleeve,targets[0],mouse,skin==1,new MotionState()),ArmRig.Transform(sleeve,targets[targets.Count-1],mouse,skin==1,new MotionState())));
                Check(report,travel>2,"sleeve moves with hand, travel="+travel.ToString("F2")+name);
            }
            return report.ToString();
        }
        private static void Check(StringBuilder text,bool ok,string name){text.AppendLine((ok?"PASS ":"FAIL ")+name);}
    }
}
