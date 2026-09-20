using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DieYing
{
    /** <summary>通过渲染差分检查眼睛、花饰彼此独立，导出可审阅的极限姿态；只写指定验证目录。</summary> */
    internal static class LayeredAudit
    {
        internal static string Run(SceneRenderer renderer,string folder)
        {
            StringBuilder report=new StringBuilder("LAYERED RENDER AUDIT\r\n");
            report.AppendLine("Device="+renderer.characters[0].RendererDevice);
            using(Bitmap sheet=new Bitmap(1600,1440))
            using(Graphics gallery=Graphics.FromImage(sheet))
            {
                gallery.Clear(Color.FromArgb(250,247,238));
                for(int skin=0;skin<2;skin++)
                {
                    using(Bitmap neutral=Render(renderer,skin,new MotionState()))
                    using(Bitmap flowers=Render(renderer,skin,new MotionState{flowerLeft=7,flowerRight=-7}))
                    using(Bitmap eyes=Render(renderer,skin,new MotionState{eyeX=3.2f,eyeY=1.7f}))
                    using(Bitmap blink=Render(renderer,skin,new MotionState{blinkAmount=1}))
                    using(Bitmap leftBrow=Render(renderer,skin,new MotionState{browLeftRise=-4,browLeftTilt=5}))
                    using(Bitmap rightBrow=Render(renderer,skin,new MotionState{browRightTilt=-6}))
                    {
                        int inkChanges=InkDifference(neutral,blink,new Rectangle(230,280,330,40));
                        Check(report,inkChanges<10,"blink preserves brow ink (ignore small texture sampling differences) "+skin+" changed="+inkChanges);
                        Check(report,Difference(neutral,leftBrow,new Rectangle(230,275,155,68))>30,"left brow independently translates and rotates "+skin);
                        Check(report,Difference(neutral,leftBrow,new Rectangle(385,275,175,68))==0,"left brow leaves right brow unchanged "+skin);
                        Check(report,Difference(neutral,leftBrow,new Rectangle(250,345,290,77))==0,"brow motion leaves eyes unchanged "+skin);
                        Check(report,Difference(neutral,rightBrow,new Rectangle(385,275,175,68))>30,"right brow independently rotates "+skin);
                        Check(report,Difference(neutral,flowers,new Rectangle(130,20,570,295))>100,"independent ornament motion "+skin);
                        Check(report,Difference(neutral,flowers,new Rectangle(240,320,310,130))==0,"ornaments do not move eyes/face "+skin);
                        Check(report,Difference(neutral,eyes,new Rectangle(250,340,290,77))>100,"eyes move independently "+skin);
                        Check(report,Difference(neutral,eyes,new Rectangle(130,20,570,295))==0,"eye motion does not move flowers "+skin);
                        Check(report,Difference(neutral,blink,new Rectangle(250,340,290,77))>100,"blink covers pupils and sparkle "+skin);
                        Check(report,Difference(neutral,flowers,new Rectangle(180,485,440,170))==0,"ornaments leave hands and desk intact "+skin);
                        Bitmap[] images={neutral,flowers,eyes,blink};
                        for(int i=0;i<images.Length;i++)gallery.DrawImageUnscaled(images[i],(i%2)*800,(skin*2+i/2)*360-180);
                        // 单独保存全尺寸，便于检查花瓣、吊饰和眼睛边缘。
                        neutral.Save(Path.Combine(folder,"skin"+skin+"-neutral.png"));
                        flowers.Save(Path.Combine(folder,"skin"+skin+"-flowers.png"));
                        eyes.Save(Path.Combine(folder,"skin"+skin+"-eyes.png"));
                        blink.Save(Path.Combine(folder,"skin"+skin+"-blink.png"));
                    }
                }
            }
            var settings=new AppSettings();var pose=new MotionState();var frame=new InputFrame{cursorX=1,cursorY=-1};
            for(int i=0;i<120;i++)pose.Update(frame,settings,renderer.keyboard,i/60.0);
            float flower=pose.flowerLeft,eye=pose.eyeX;double time=pose.time;
            settings.paused=true;frame.cursorX=-1;pose.Update(frame,settings,renderer.keyboard,3);
            Check(report,pose.flowerLeft==flower && pose.eyeX==eye && pose.time==time,"pause freezes flowers eyes and sparkle clock");
            settings.paused=false;settings.idleMotion=false;settings.mouseEnabled=false;
            for(int i=0;i<600;i++)pose.Update(frame,settings,renderer.keyboard,3.1+i/120.0);
            Check(report,Math.Abs(pose.eyeX)<.01 && Math.Abs(pose.flowerLeft)<.01 && Math.Abs(pose.flowerRight)<.01,"disabled tracking/idle settles without residual oscillation");
            return report.ToString();
        }
        private static Bitmap Render(SceneRenderer renderer,int skin,MotionState pose)
        {
            pose.SetReviewPose(renderer.keyboard,65,PointF.Empty,false,0);
            Bitmap b=new Bitmap(800,720,PixelFormat.Format32bppPArgb);
            using(Graphics g=Graphics.FromImage(b)){g.Clear(Color.FromArgb(250,247,238));renderer.Draw(g,new AppSettings{skin=skin,showKeyBubble=false},pose);}
            return b;
        }
        private static int Difference(Bitmap a,Bitmap b,Rectangle region)
        {
            int count=0;for(int y=region.Top;y<region.Bottom;y++)for(int x=region.Left;x<region.Right;x++)if(a.GetPixel(x,y)!=b.GetPixel(x,y))count++;
            return count;
        }
        private static int InkDifference(Bitmap a,Bitmap b,Rectangle region)
        {
            int count=0;
            for(int y=region.Top;y<region.Bottom;y++)for(int x=region.Left;x<region.Right;x++)
            {
                Color p=a.GetPixel(x,y),q=b.GetPixel(x,y);
                if((p.R<145 || q.R<145) && Math.Abs(p.R-q.R)+Math.Abs(p.G-q.G)+Math.Abs(p.B-q.B)>60)count++;
            }
            return count;
        }
        private static void Check(StringBuilder report,bool ok,string name){report.AppendLine((ok?"PASS ":"FAIL ")+name);}

        /** <summary>以真实渲染器导出 60fps 双衣装演示，输入为预设轨迹，不代表硬件输入测试。ffmpeg 只用于此离线导出。</summary> */
        internal static void Preview(string folder,string ffmpeg,bool reactions = false)
        {
            string path=Path.Combine(folder,reactions ? "自动表情预览.mp4" : "分层动态预览.mp4");
            var start=new ProcessStartInfo(ffmpeg,"-hide_banner -loglevel error -y -f rawvideo -pixel_format bgra -video_size 1120x540 -framerate 60 -i pipe:0 -an -c:v libx264 -preset fast -crf 18 -pix_fmt yuv420p -movflags +faststart \""+path+"\"")
            {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardError=true};
            using(Process encoder=Process.Start(start))
            using(SceneRenderer renderer=new SceneRenderer(Path.Combine(Program.root,"assets")))
            using(Bitmap full=new Bitmap(1600,760,PixelFormat.Format32bppPArgb))
            using(Bitmap resized=new Bitmap(1120,540,PixelFormat.Format32bppPArgb))
            using(Font font=new Font("Microsoft YaHei UI",18,FontStyle.Bold))
            using(Font hint=new Font("Microsoft YaHei UI",14))
            {
                var stderr=encoder.StandardError.ReadToEndAsync();
                var poses=new MotionState[]{new MotionState(),new MotionState()};
                byte[] bytes=new byte[1120*540*4];
                long reactionSequence = 0;
                for(int index=0;index<(reactions ? 840 : 600);index++)
                {
                    double t=index/60.0;
                    bool tap = reactions && t < 2 && index % 5 == 0;
                    if (tap) reactionSequence++;
                    using(Graphics g=Graphics.FromImage(full))
                    {
                        g.Clear(Color.FromArgb(250,247,238));
                        for(int skin=0;skin<2;skin++)
                        {
                            var config=new AppSettings{skin=skin,showKeyBubble=false,expression=t>=7 && t<8 ? 1 : t>=8 && t<9 ? 2 : 0};
                            int key=renderer.keyboard.Keys[(index/30)%renderer.keyboard.Keys.Count].id;
                            var input=new InputFrame{cursorX=(float)Math.Sin(t*1.5),cursorY=(float)Math.Sin(t*.85),keySequence=index/30,pressedSinceSnapshot=index%30==0?new int[]{key}:new int[0],leftButton=index%90<9};
                            input.heldKeys[key]=index%30<15;
                            double now = t;
                            if (reactions)
                            {
                                config.expression = 0;
                                now = t >= 6 ? t + 120 : t;
                                input = new InputFrame {keySequence=reactionSequence,
                                    pressedSinceSnapshot=tap ? new int[]{65} : new int[0],
                                    lastActivity=t<2 ? t : t<12 ? 2 : now,
                                    cursorX=t>=12 ? (float)Math.Sin(t*2) : 0};
                            }
                            poses[skin].Update(input,config,renderer.keyboard,now);
                            GraphicsState state=g.Save();g.TranslateTransform(skin*800,0);renderer.Draw(g,config,poses[skin]);g.Restore(state);
                            g.DrawString(skin==0?"常服":"打歌服",font,Brushes.SaddleBrown,skin*800+330,671);
                        }
                        string caption = reactions ? t<2 ? "快速打字 → 晕乎乎" : t<6 ? "降低频率 → 恢复自然" : t<12 ? "静默两分钟 → 打瞌睡（演示已快进等待时间）" : "移动鼠标 → 唤醒，恢复自然" : "鼠标袖口与手腕跟随 · 键盘原位轻敲";
                        g.DrawString(caption,hint,Brushes.DimGray,445,718);
                    }
                    using(Graphics g=Graphics.FromImage(resized)){g.Clear(Color.FromArgb(250,247,238));g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(full,new Rectangle(0,0,1120,532));}
                    BitmapData data=resized.LockBits(new Rectangle(0,0,1120,540),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
                    try{Marshal.Copy(data.Scan0,bytes,0,bytes.Length);}finally{resized.UnlockBits(data);}
                    encoder.StandardInput.BaseStream.Write(bytes,0,bytes.Length);
                    if(index==(reactions ? 490 : 110))resized.Save(Path.Combine(folder,reactions ? "瞌睡预览.png" : "分层版预览.png"));
                }
                encoder.StandardInput.Close();encoder.WaitForExit();
                string error=stderr.Result;
                if(encoder.ExitCode!=0)throw new InvalidOperationException("视频导出失败："+error);
            }
        }
    }
}
