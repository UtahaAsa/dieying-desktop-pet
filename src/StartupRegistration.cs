using System;
using System.IO;
using System.Security;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DieYing
{
    /** <summary>当前用户登录启动项。仅显式切换时写注册表；启动和刷新界面只读，不要求管理员权限。</summary> */
    internal static class StartupRegistration
    {
        private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "DieYing.DesktopPet";
        internal static string Command { get { return BuildCommand(Application.ExecutablePath); } }
        internal static string BuildCommand(string path) { return "\"" + Path.GetFullPath(path) + "\""; }
        internal static bool Enabled
        {
            get
            {
                try
                {
                    using(RegistryKey key=Registry.CurrentUser.OpenSubKey(KeyPath,false))
                        return key!=null && String.Equals(key.GetValue(ValueName) as string,Command,StringComparison.OrdinalIgnoreCase);
                }
                catch(SecurityException){return false;}
                catch(UnauthorizedAccessException){return false;}
                catch(IOException){return false;}
            }
        }
        /** <summary>按用户选择更新本应用的启动项；失败时提示原因，不伪造启用状态。不修改其它程序的项目。</summary> */
        internal static void SetEnabled(bool enabled)
        {
            try
            {
                using(RegistryKey key=enabled ? Registry.CurrentUser.CreateSubKey(KeyPath) : Registry.CurrentUser.OpenSubKey(KeyPath,true))
                {
                    if(key!=null)Configure(enabled,Command,delegate{return key.GetValue(ValueName) as string;},
                        delegate(string value){key.SetValue(ValueName,value,RegistryValueKind.String);},delegate{key.DeleteValue(ValueName,false);});
                }
            }
            catch(Exception error)
            {
                if(!(error is SecurityException) && !(error is UnauthorizedAccessException) && !(error is IOException))throw;
                MessageBox.Show("无法修改开机自启："+error.Message,"蝶应",MessageBoxButtons.OK,MessageBoxIcon.Information);
            }
        }
        /** <summary>将启动意图应用到指定存储；仅删除指向本程序的条目。传入内存存储可验证行为而不改变系统启动项。</summary> */
        internal static void Configure(bool enabled,string command,Func<string> read,Action<string> write,Action delete)
        {
            string existing=read();
            if(enabled){if(!String.Equals(existing,command,StringComparison.OrdinalIgnoreCase))write(command);}
            else if(String.Equals(existing,command,StringComparison.OrdinalIgnoreCase))delete();
        }
    }
}
