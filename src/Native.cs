using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;

namespace DieYing
{
    /** <summary>逐像素透明窗口接口。调用者必须在所属 UI 线程使用；绘制失败抛出 Win32Exception。</summary> */
    internal static class Native
    {
        internal const int ExLayered = 0x80000, ExTransparent = 0x20, ExToolWindow = 0x80, ExNoActivate = 0x08000000;
        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { internal int x, y; internal NativePoint(int x, int y) { this.x = x; this.y = y; } }
        [StructLayout(LayoutKind.Sequential)] private struct NativeSize { internal int width, height; internal NativeSize(int width, int height) { this.width = width; this.height = height; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct Blend { internal byte operation, flags, alpha, format; }
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
        [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr value);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destinationDc, ref NativePoint location, ref NativeSize size, IntPtr sourceDc, ref NativePoint source, int color, ref Blend blend, int flags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern int SetWindowLong(IntPtr window, int index, int value);
        [DllImport("kernel32.dll")] private static extern void SetLastError(uint error);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll")] internal static extern uint GetGuiResources(IntPtr process, uint flags);

        /** <summary>改变鼠标穿透状态，保留分层、不激活和工具窗口标志；不会激活窗口。</summary> */
        internal static void SetClickThrough(IntPtr window, bool enabled)
        {
            int style = GetWindowLong(window, -20) | ExLayered | ExNoActivate | ExToolWindow;
            style = enabled ? style | ExTransparent : style & ~ExTransparent;
            SetLastError(0);
            int previous = SetWindowLong(window, -20, style);
            int error = Marshal.GetLastWin32Error();
            if (previous == 0 && error != 0) throw new Win32Exception(error, "无法更新桌宠鼠标穿透状态");
            if (!SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法应用桌宠窗口样式");
        }

        /** <summary>设置置顶顺序，不移动、缩放或激活窗口。</summary> */
        internal static void SetTopMost(IntPtr window, bool enabled)
        {
            if (!SetWindowPos(window, new IntPtr(enabled ? -1 : -2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法应用桌宠置顶状态");
        }

        /** <summary>以预乘 Alpha 位图更新窗口。location 为屏幕物理坐标；alpha 为整体不透明度 0—255。</summary> */
        internal static void Present(IntPtr window, Bitmap bitmap, Point location, byte alpha)
        {
            if (bitmap == null) throw new ArgumentNullException("bitmap");
            IntPtr screenDc = GetDC(IntPtr.Zero), memoryDc = IntPtr.Zero, image = IntPtr.Zero, previous = IntPtr.Zero;
            if (screenDc == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法获取屏幕绘制上下文");
            try
            {
                memoryDc = CreateCompatibleDC(screenDc);
                if (memoryDc == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建透明窗口绘制上下文");
                image = bitmap.GetHbitmap(Color.FromArgb(0));
                previous = SelectObject(memoryDc, image);
                if (previous == IntPtr.Zero || previous == new IntPtr(-1))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "无法选择透明窗口位图");
                NativePoint position = new NativePoint(location.X, location.Y), source = new NativePoint(0, 0);
                NativeSize size = new NativeSize(bitmap.Width, bitmap.Height);
                Blend blend = new Blend { operation = 0, flags = 0, alpha = alpha, format = 1 };
                if (!UpdateLayeredWindow(window, screenDc, ref position, ref size, memoryDc, ref source, 0, ref blend, 2))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "透明窗口绘制失败");
            }
            finally
            {
                if (previous != IntPtr.Zero && previous != new IntPtr(-1)) SelectObject(memoryDc, previous);
                if (image != IntPtr.Zero) DeleteObject(image);
                if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
    }
}
