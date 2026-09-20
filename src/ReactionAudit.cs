using System;
using System.Text;

namespace DieYing
{
    internal static class ReactionAudit
    {
        internal static string Run()
        {
            var report = new StringBuilder("AUTOMATIC REACTION AUDIT\r\n");
            var state = new AutoReaction(); var frame = new InputFrame();
            state.Update(frame,false,true,0);
            for (int i=1;i<=20;i++)
            {
                frame.keySequence=i; frame.pressedSinceSnapshot=new int[]{65}; frame.lastActivity=i*.06; state.Update(frame,false,true,i*.06);
                if(i==12)Check(report,state.expression==-1,"12 presses no longer trigger dizzy");
                if(i==19)Check(report,state.expression==-1,"19 presses remain below threshold");
            }
            Check(report,state.expression==4,"rapid physical key edges enter dizzy");
            frame.pressedSinceSnapshot=new int[0];
            state.Update(frame,false,true,2.7); Check(report,state.expression==4,"rate drop has hysteresis");
            state.Update(frame,false,true,4); Check(report,state.expression==-1 && state.returnedToNatural,"slower typing returns natural");
            state.Update(frame,false,true,121.19); Check(report,state.expression==-1,"not asleep before 120 idle seconds");
            state.Update(frame,false,true,121.21); Check(report,state.expression==5,"sleep after 120 idle seconds");
            frame.lastActivity=122; state.Update(frame,false,true,122);
            Check(report,state.expression==-1 && state.returnedToNatural,"raw mouse activity wakes immediately");
            for(int kind=0;kind<4;kind++)
            {
                state=new AutoReaction(); frame=new InputFrame(); state.Update(frame,false,true,0); state.Update(frame,false,true,121);
                if(kind==0){frame.keySequence=1;frame.pressedSinceSnapshot=new int[]{66};}
                if(kind==1)frame.leftButton=true;
                if(kind==2)frame.wheelDelta=120;
                if(kind==3)frame.x1Button=true;
                state.Update(frame,false,true,122);
                Check(report,state.expression==-1 && state.returnedToNatural,"wake input kind "+kind);
            }
            state=new AutoReaction(); frame=new InputFrame(); state.Update(frame,false,true,0);
            frame.keySequence=1;frame.pressedSinceSnapshot=new int[]{65};frame.heldKeys[65]=true;
            for(int i=1;i<=150;i++)state.Update(frame,false,true,i);
            Check(report,state.expression==-1,"held key neither dizzy nor sleeping; repeated snapshots not recounted");
            state=new AutoReaction();frame=new InputFrame();state.Update(frame,false,true,0);
            for(int i=1;i<=20;i++){frame.keySequence=i;frame.pressedSinceSnapshot=new int[]{162};state.Update(frame,false,true,i*.05);}
            Check(report,state.expression==-1,"modifier-only input not typing speed");
            state.Update(frame,false,true,122); Check(report,state.expression==5,"modifier activity still postpones sleep");
            state.Update(frame,true,true,123);state.Update(frame,false,true,400);
            Check(report,state.expression==-1,"resume starts a fresh idle period");
            state.Update(frame,false,true,1);Check(report,state.expression==-1,"clock reset does not strand sleep");
            state=new AutoReaction();frame=new InputFrame();state.Update(frame,false,false,0);
            for(int i=1;i<=20;i++){frame.keySequence=i;frame.pressedSinceSnapshot=new int[]{65};state.Update(frame,false,false,i*.05);}
            Check(report,state.expression==-1,"disabled keyboard mapping has no dizzy burst");
            frame.lastActivity=1000;state.Update(frame,false,false,2);frame.lastActivity=0;state.Update(frame,false,false,123);
            Check(report,state.expression==5,"future event timestamps are clamped rather than preventing sleep");
            var settings=new AppSettings{expression=3};var pose=new MotionState();var keyboard=new KeyboardModel();frame=new InputFrame();
            pose.Update(frame,settings,keyboard,0);pose.Update(frame,settings,keyboard,121);
            Check(report,pose.Sleeping && settings.expression==3,"sleep is transient and never written as manual expression");
            frame.lastActivity=122;pose.Update(frame,settings,keyboard,122);
            Check(report,!pose.Sleeping && settings.expression==0,"wake returns natural even after manual expression");
            return report.ToString();
        }
        private static void Check(StringBuilder report,bool ok,string name){report.AppendLine((ok?"PASS ":"FAIL ")+name);}
    }
}
