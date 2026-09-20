using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DieYing
{
    /** <summary>在独立 OpenGL 帧缓冲中绘制二维分层网格，再交给透明窗口合成。所有调用限定在创建线程；释放后不可使用。</summary> */
    internal sealed class GpuCanvas : IDisposable
    {
        private readonly NativeWindow window = new NativeWindow();
        private IntPtr dc, context;
        private uint framebuffer, color;
        private uint scaleTexture;
        private uint overlayTexture;
        private Size overlaySize;
        private Size scaleSourceSize;
        private readonly List<uint> textures = new List<uint>();
        private Bitmap output;
        private int width, height;
        private GenObjects genFramebuffers;
        private BindObject bindFramebuffer;
        private AttachTexture attachTexture;
        private CheckFramebuffer checkFramebuffer;
        private DeleteObjects deleteFramebuffers;
        private bool disposed;
        private readonly float[] vertices = new float[32768];
        private GCHandle vertexPin;
        private int vertexCount;
        internal readonly string device;

        [StructLayout(LayoutKind.Sequential)]
        private struct PixelFormat
        {
            internal ushort size, version;
            internal uint flags;
            internal byte pixelType, colorBits, redBits, redShift, greenBits, greenShift, blueBits, blueShift, alphaBits, alphaShift;
            internal byte accumBits, accumRedBits, accumGreenBits, accumBlueBits, accumAlphaBits, depthBits, stencilBits, auxBuffers, layerType, reserved;
            internal uint layerMask, visibleMask, damageMask;
        }
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GenObjects(int count, out uint id);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void BindObject(uint target, uint id);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void AttachTexture(uint target, uint attachment, uint textureTarget, uint texture, int level);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint CheckFramebuffer(uint target);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void DeleteObjects(int count, ref uint id);

        internal GpuCanvas(int width, int height)
        {
            this.width = width; this.height = height;
            output = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            try
            {
                window.CreateHandle(new CreateParams { Caption = "DieYing render context", Width = 1, Height = 1, Style = unchecked((int)0x80000000), ExStyle = 0x08000080 });
                dc = GetDC(window.Handle);
                var format = new PixelFormat { size = (ushort)Marshal.SizeOf(typeof(PixelFormat)), version = 1, flags = 0x24, colorBits = 32, alphaBits = 8 };
                int index = ChoosePixelFormat(dc, ref format);
                if (index == 0 || !SetPixelFormat(dc, index, ref format)) throw new InvalidOperationException("OpenGL 像素格式不可用。");
                context = wglCreateContext(dc);
                if (context == IntPtr.Zero || !wglMakeCurrent(dc, context)) throw new InvalidOperationException("OpenGL 上下文不可用。");
                device = Marshal.PtrToStringAnsi(glGetString(0x1f01)) + " / " + Marshal.PtrToStringAnsi(glGetString(0x1f02));
                genFramebuffers = Extension<GenObjects>("glGenFramebuffers");
                bindFramebuffer = Extension<BindObject>("glBindFramebuffer");
                attachTexture = Extension<AttachTexture>("glFramebufferTexture2D");
                checkFramebuffer = Extension<CheckFramebuffer>("glCheckFramebufferStatus");
                deleteFramebuffers = Extension<DeleteObjects>("glDeleteFramebuffers");
                glGenTextures(1, out color); glBindTexture(0x0de1, color);
                glTexImage2D(0x0de1, 0, 0x8058, width, height, 0, 0x80e1, 0x1401, IntPtr.Zero);
                glTexParameteri(0x0de1, 0x2801, 0x2601); glTexParameteri(0x0de1, 0x2800, 0x2601);
                genFramebuffers(1, out framebuffer); bindFramebuffer(0x8d40, framebuffer);
                attachTexture(0x8d40, 0x8ce0, 0x0de1, color, 0);
                if (checkFramebuffer(0x8d40) != 0x8cd5) throw new InvalidOperationException("OpenGL 离屏缓冲不可用。");
                glViewport(0, 0, width, height);
                // 内存第 0 行也是画布顶部，省去每帧图像翻转。
                glMatrixMode(0x1701); glLoadIdentity(); glOrtho(0, width, 0, height, -1, 1);
                glMatrixMode(0x1700); glLoadIdentity();
                glEnable(0x0de1); glEnable(0x0be2); glBlendFunc(1, 0x0303);
                glClearColor(0, 0, 0, 0);
                vertexPin = GCHandle.Alloc(vertices,GCHandleType.Pinned);
            }
            catch { Dispose(); throw; }
        }

        private static T Extension<T>(string name) where T : class
        {
            IntPtr address = wglGetProcAddress(name);
            if (address == IntPtr.Zero || address.ToInt64() == -1 || address.ToInt64() <= 3) address = wglGetProcAddress(name + "EXT");
            if (address == IntPtr.Zero || address.ToInt64() == -1 || address.ToInt64() <= 3) throw new NotSupportedException("缺少 " + name);
            return Marshal.GetDelegateForFunctionPointer(address, typeof(T)) as T;
        }

        internal uint Upload(Bitmap bitmap)
        {
            MakeCurrent();
            uint texture; glGenTextures(1, out texture); textures.Add(texture); glBindTexture(0x0de1, texture);
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            try { glTexImage2D(0x0de1, 0, 0x8058, bitmap.Width, bitmap.Height, 0, 0x80e1, 0x1401, data.Scan0); }
            finally { bitmap.UnlockBits(data); }
            glTexParameteri(0x0de1, 0x2801, 0x2601); glTexParameteri(0x0de1, 0x2800, 0x2601);
            glTexParameteri(0x0de1, 0x2802, 0x812f); glTexParameteri(0x0de1, 0x2803, 0x812f);
            return texture;
        }

        internal void Begin()
        {
            MakeCurrent(); bindFramebuffer(0x8d40, framebuffer); glClear(0x4000);
        }
        internal void StartMesh(uint texture, float opacity)
        {
            glBindTexture(0x0de1, texture); glColor4f(opacity, opacity, opacity, opacity); vertexCount=0;
        }
        internal void Vertex(float u, float v, PointF p)
        {
            int index=vertexCount*5;
            if(index+4>=vertices.Length)throw new InvalidOperationException("网格顶点超出预分配容量。");
            // GL_T2F_V3F 为两个纹理坐标和三个位置坐标，不能用四浮点步长。
            vertices[index]=u;vertices[index+1]=v;vertices[index+2]=p.X;vertices[index+3]=p.Y;vertices[index+4]=0;vertexCount++;
        }
        internal void EndMesh()
        {
            glInterleavedArrays(0x2a27,0,vertexPin.AddrOfPinnedObject());
            glDrawArrays(0x0004,0,vertexCount);
        }
        /** <summary>按实际显示像素调整离屏缓冲；纹理和逻辑坐标保留，避免先缩小再放大。仅绘制线程调用。</summary> */
        internal void ResizeTarget(int nextWidth,int nextHeight,float logicalWidth,float logicalHeight)
        {
            if(nextWidth==width && nextHeight==height)return;
            MakeCurrent();
            Bitmap next=new Bitmap(nextWidth,nextHeight,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            output.Dispose();output=next;width=nextWidth;height=nextHeight;
            glBindTexture(0x0de1,color);
            glTexImage2D(0x0de1,0,0x8058,width,height,0,0x80e1,0x1401,IntPtr.Zero);
            glViewport(0,0,width,height);
            glMatrixMode(0x1701);glLoadIdentity();glOrtho(0,logicalWidth,0,logicalHeight,-1,1);
            glMatrixMode(0x1700);glLoadIdentity();
        }
        /** <summary>更新并合成可复用的键鼠透明层；调用方必须已 Begin。只在尺寸变化时重新分配纹理。</summary> */
        internal void DrawOverlay(Bitmap bitmap,RectangleF destination)
        {
            if(overlayTexture==0){overlayTexture=Upload(bitmap);overlaySize=bitmap.Size;}
            else
            {
                glBindTexture(0x0de1,overlayTexture);
                BitmapData data=bitmap.LockBits(new Rectangle(Point.Empty,bitmap.Size),ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                try
                {
                    if(overlaySize!=bitmap.Size){glTexImage2D(0x0de1,0,0x8058,bitmap.Width,bitmap.Height,0,0x80e1,0x1401,data.Scan0);overlaySize=bitmap.Size;}
                    else glTexSubImage2D(0x0de1,0,0,0,bitmap.Width,bitmap.Height,0x80e1,0x1401,data.Scan0);
                }
                finally{bitmap.UnlockBits(data);}
            }
            StartMesh(overlayTexture,1);
            Vertex(0,0,new PointF(destination.Left,destination.Top));Vertex(1,0,new PointF(destination.Right,destination.Top));Vertex(0,1,new PointF(destination.Left,destination.Bottom));
            Vertex(1,0,new PointF(destination.Right,destination.Top));Vertex(1,1,new PointF(destination.Right,destination.Bottom));Vertex(0,1,new PointF(destination.Left,destination.Bottom));EndMesh();
        }
        internal void Present(Graphics graphics,float renderScale=1)
        {
            BitmapData data = output.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            try { glReadPixels(0, 0, width, height, 0x80e1, 0x1401, data.Scan0); }
            finally { output.UnlockBits(data); }
            if(renderScale==1)graphics.DrawImageUnscaled(output,0,0);
            else graphics.DrawImage(output,new RectangleF(0,0,width/renderScale,height/renderScale),new RectangleF(0,0,width,height),GraphicsUnit.Pixel);
        }
        /** <summary>在创建线程用双线性纹理采样缩放预乘 Alpha 画面，写入同尺寸目标；复用纹理，不逐帧分配位图。</summary> */
        internal void ScaleInto(Bitmap source, Bitmap destination)
        {
            if (destination.Width != width || destination.Height != height) throw new ArgumentException("缩放目标尺寸不匹配。");
            Begin();
            if (scaleTexture == 0) { scaleTexture = Upload(source); scaleSourceSize = source.Size; }
            else
            {
                if (source.Size != scaleSourceSize) throw new ArgumentException("缩放输入尺寸不能改变。");
                glBindTexture(0x0de1, scaleTexture);
                BitmapData input = source.LockBits(new Rectangle(Point.Empty, source.Size), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                try { glTexSubImage2D(0x0de1, 0, 0, 0, source.Width, source.Height, 0x80e1, 0x1401, input.Scan0); }
                finally { source.UnlockBits(input); }
            }
            StartMesh(scaleTexture, 1);
            Vertex(0,0,new PointF(0,0)); Vertex(1,0,new PointF(width,0)); Vertex(0,1,new PointF(0,height));
            Vertex(0,1,new PointF(0,height)); Vertex(1,0,new PointF(width,0)); Vertex(1,1,new PointF(width,height));
            EndMesh();
            BitmapData target = destination.LockBits(new Rectangle(Point.Empty, destination.Size), ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            try { glReadPixels(0,0,width,height,0x80e1,0x1401,target.Scan0); }
            finally { destination.UnlockBits(target); }
        }
        private void MakeCurrent() { if (!wglMakeCurrent(dc, context)) throw new InvalidOperationException("无法激活绘图上下文。"); }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            if (context != IntPtr.Zero)
            {
                wglMakeCurrent(dc, context);
                foreach (uint value in textures) { uint id = value; glDeleteTextures(1, ref id); }
                if (framebuffer != 0 && deleteFramebuffers != null) deleteFramebuffers(1, ref framebuffer);
                if (color != 0) glDeleteTextures(1, ref color);
                wglMakeCurrent(IntPtr.Zero, IntPtr.Zero); wglDeleteContext(context); context = IntPtr.Zero;
            }
            if (dc != IntPtr.Zero) { ReleaseDC(window.Handle, dc); dc = IntPtr.Zero; }
            if (window.Handle != IntPtr.Zero) window.DestroyHandle();
            if(vertexPin.IsAllocated)vertexPin.Free();
            output.Dispose();
        }
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern int ChoosePixelFormat(IntPtr dc, ref PixelFormat p);
        [DllImport("gdi32.dll")] private static extern bool SetPixelFormat(IntPtr dc, int n, ref PixelFormat p);
        [DllImport("opengl32.dll")] private static extern IntPtr wglCreateContext(IntPtr dc);
        [DllImport("opengl32.dll")] private static extern bool wglMakeCurrent(IntPtr dc, IntPtr context);
        [DllImport("opengl32.dll")] private static extern bool wglDeleteContext(IntPtr context);
        [DllImport("opengl32.dll")] private static extern IntPtr wglGetProcAddress(string name);
        [DllImport("opengl32.dll")] private static extern IntPtr glGetString(uint name);
        [DllImport("opengl32.dll")] private static extern void glGenTextures(int count, out uint texture);
        [DllImport("opengl32.dll")] private static extern void glDeleteTextures(int count, ref uint texture);
        [DllImport("opengl32.dll")] private static extern void glBindTexture(uint target, uint texture);
        [DllImport("opengl32.dll")] private static extern void glTexParameteri(uint target, uint name, int value);
        [DllImport("opengl32.dll")] private static extern void glTexImage2D(uint target, int level, int format, int width, int height, int border, uint sourceFormat, uint type, IntPtr pixels);
        [DllImport("opengl32.dll")] private static extern void glTexSubImage2D(uint target, int level, int x, int y, int width, int height, uint format, uint type, IntPtr pixels);
        [DllImport("opengl32.dll")] private static extern void glViewport(int x, int y, int width, int height);
        [DllImport("opengl32.dll")] private static extern void glMatrixMode(uint mode);
        [DllImport("opengl32.dll")] private static extern void glLoadIdentity();
        [DllImport("opengl32.dll")] private static extern void glOrtho(double l, double r, double b, double t, double n, double f);
        [DllImport("opengl32.dll")] private static extern void glEnable(uint value);
        [DllImport("opengl32.dll")] private static extern void glBlendFunc(uint source, uint destination);
        [DllImport("opengl32.dll")] private static extern void glClearColor(float r, float g, float b, float a);
        [DllImport("opengl32.dll")] private static extern void glClear(uint mask);
        [DllImport("opengl32.dll")] private static extern void glBegin(uint mode);
        [DllImport("opengl32.dll")] private static extern void glEnd();
        [DllImport("opengl32.dll")] private static extern void glTexCoord2f(float u, float v);
        [DllImport("opengl32.dll")] private static extern void glVertex2f(float x, float y);
        [DllImport("opengl32.dll")] private static extern void glColor4f(float r, float g, float b, float a);
        [DllImport("opengl32.dll")] private static extern void glInterleavedArrays(uint format,int stride,IntPtr pointer);
        [DllImport("opengl32.dll")] private static extern void glDrawArrays(uint mode,int first,int count);
        [DllImport("opengl32.dll")] private static extern void glReadPixels(int x, int y, int width, int height, uint format, uint type, IntPtr pixels);
    }
}
