using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace DieYing
{
    /** <summary>使用 Windows 高精度等待计时器投递 UI 绘制；最多保留一个待绘制通知，暂停时不积压帧。必须由 UI 线程启停和释放。</summary> */
    internal sealed class FrameClock : IDisposable
    {
        private readonly Control owner;
        private readonly Action tick;
        private readonly ManualResetEvent stopping = new ManualResetEvent(false);
        private Thread worker;
        private int pending, rate=60;
        private IntPtr timer;
        private bool disposed;
        internal int Rate { set { Interlocked.Exchange(ref rate,Math.Max(1,value)); } }

        internal FrameClock(Control owner, Action tick) { this.owner=owner;this.tick=tick; }
        internal void Start()
        {
            if(disposed || worker!=null)return;
            timer=CreateWaitableTimerEx(IntPtr.Zero,null,2,0x001f0003);
            if(timer==IntPtr.Zero)timer=CreateWaitableTimerEx(IntPtr.Zero,null,0,0x001f0003);
            if(timer==IntPtr.Zero)throw new InvalidOperationException("无法创建绘制计时器。");
            stopping.Reset();worker=new Thread(Run){IsBackground=true,Name="DieYing frame clock"};worker.Start();
        }
        private void Run()
        {
            Stopwatch clock=Stopwatch.StartNew();double deadline=0;
            IntPtr[] waits={stopping.SafeWaitHandle.DangerousGetHandle(),timer};
            while(!stopping.WaitOne(0))
            {
                double period=1.0/Interlocked.CompareExchange(ref rate,0,0);
                deadline+=period;
                double now=clock.Elapsed.TotalSeconds;
                if(deadline<now)deadline=now+period;
                long due=-(long)Math.Max(1,(deadline-now)*10000000);
                if(!SetWaitableTimer(timer,ref due,0,IntPtr.Zero,IntPtr.Zero,false))return;
                if(WaitForMultipleObjects(2,waits,false,0xffffffff)!=1)return;
                if(Interlocked.CompareExchange(ref pending,1,0)!=0)continue;
                try
                {
                    owner.BeginInvoke((Action)delegate
                    {
                        // 绘制中也保持占位：慢帧期间不能继续向 WinForms 回调队列补帧。
                        // 否则 BeginInvoke 队列始终不空，鼠标、菜单与 WM_TIMER 会饥饿。
                        try { if(!disposed && !stopping.WaitOne(0))tick(); }
                        finally { Interlocked.Exchange(ref pending,0); }
                    });
                }
                catch(InvalidOperationException){Interlocked.Exchange(ref pending,0);return;}
            }
        }
        internal void Stop()
        {
            stopping.Set();
            if(worker!=null){worker.Join();worker=null;}
            if(timer!=IntPtr.Zero){CloseHandle(timer);timer=IntPtr.Zero;}
        }
        public void Dispose(){if(disposed)return;disposed=true;Stop();stopping.Dispose();}
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern IntPtr CreateWaitableTimerEx(IntPtr attributes,string name,uint flags,uint access);
        [DllImport("kernel32.dll",SetLastError=true)]private static extern bool SetWaitableTimer(IntPtr timer,ref long due,int period,IntPtr callback,IntPtr argument,bool resume);
        [DllImport("kernel32.dll")]private static extern uint WaitForMultipleObjects(uint count,IntPtr[] handles,bool all,uint milliseconds);
        [DllImport("kernel32.dll")]private static extern bool CloseHandle(IntPtr handle);
    }
}
