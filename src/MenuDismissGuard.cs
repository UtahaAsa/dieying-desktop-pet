using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DieYing
{
    /** <summary>为不激活桌宠补充菜单收起：外部点击、Esc、切换窗口均关闭；子菜单区域计入命中。仅在菜单打开时运行。</summary> */
    internal sealed class MenuDismissGuard : IDisposable
    {
        private readonly ContextMenuStrip menu;
        private readonly Form owner;
        private readonly Timer timer=new Timer{Interval=16};
        private int previousButtons;
        private IntPtr previousForeground, menuForeground;
        internal MenuDismissGuard(ContextMenuStrip menu,Form owner)
        {
            this.menu=menu;this.owner=owner;
            menu.Opening+=Opening;menu.Opened+=Opened;menu.Closed+=Closed;
            timer.Tick+=delegate{Check(Cursor.Position,Buttons(),(GetAsyncKeyState(27)&0x8000)!=0,Native.GetForegroundWindow());};
        }
        private void Opening(object sender,System.ComponentModel.CancelEventArgs args)
        {
            previousForeground=Native.GetForegroundWindow();
            if(owner.Visible)Native.SetForegroundWindow(owner.Handle);
        }
        private void Opened(object sender,EventArgs args)
        {
            previousButtons=Buttons();menuForeground=Native.GetForegroundWindow();timer.Start();
        }
        private void Closed(object sender,ToolStripDropDownClosedEventArgs args)
        {
            timer.Stop();
            IntPtr current=Native.GetForegroundWindow();
            if(previousForeground!=IntPtr.Zero && (current==owner.Handle || OwnsHandle(menu,current)))Native.SetForegroundWindow(previousForeground);
        }
        internal void Check(Point cursor,int buttons,bool escape,IntPtr foreground)
        {
            if(!menu.Visible)return;
            bool outsidePress=(buttons & ~previousButtons)!=0 && !Contains(menu,cursor);
            previousButtons=buttons;
            bool switched=foreground!=IntPtr.Zero && foreground!=menuForeground && foreground!=owner.Handle && !OwnsHandle(menu,foreground);
            if(escape || outsidePress || switched)menu.Close();
        }
        internal static bool Contains(ToolStripDropDown root,Point point)
        {
            if(root.Visible && root.Bounds.Contains(point))return true;
            foreach(ToolStripItem item in root.Items)
            {
                ToolStripDropDownItem child=item as ToolStripDropDownItem;
                if(child!=null && child.HasDropDownItems && child.DropDown.Visible && Contains(child.DropDown,point))return true;
            }
            return false;
        }
        private static bool OwnsHandle(ToolStripDropDown root,IntPtr handle)
        {
            if(root.IsHandleCreated && root.Handle==handle)return true;
            foreach(ToolStripItem item in root.Items)
            {
                ToolStripDropDownItem child=item as ToolStripDropDownItem;
                if(child!=null && child.HasDropDownItems && child.DropDown.Visible && OwnsHandle(child.DropDown,handle))return true;
            }
            return false;
        }
        private static int Buttons()
        {
            return ((GetAsyncKeyState(1)&0x8000)!=0?1:0)|((GetAsyncKeyState(2)&0x8000)!=0?2:0)|((GetAsyncKeyState(4)&0x8000)!=0?4:0);
        }
        public void Dispose(){timer.Stop();timer.Dispose();menu.Opening-=Opening;menu.Opened-=Opened;menu.Closed-=Closed;}
        [DllImport("user32.dll")]private static extern short GetAsyncKeyState(int key);
    }
}
