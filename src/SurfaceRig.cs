using System;
using System.Drawing;

namespace DieYing
{
    /** <summary>头部、发梢、衣饰的共享顶点变形；实际纹理绘制由 GPU 执行。纯函数，不保存帧状态。</summary> */
    internal static class SurfaceRig
    {
        /** <summary>将原画上的锚点映射到本帧位置；袖口与手臂根部使用同一函数，不能各自漂移。</summary> */
        internal static PointF Transform(PointF point, MotionState pose)
        {
            float x = point.X, y = point.Y;
            // 桌沿及桌面保持固定；后发在桌面外侧仍可摆动。
            float deskLeft = 180 - V.Clamp((y - 532) / 65, 0, 1) * 78;
            bool onDesk = y >= 532 && x >= deskLeft && x <= 800 - deskLeft;
            if (onDesk || y > 626) return point;
            if (y >= 475 && ((x >= 180 && x <= 335) || (x >= 465 && x <= 620))) return point;
            // 身体底图的头部权重在肩部归零；活动肩袖由 ArmRig 单独绑定。
            float head = 1 - V.Smooth((y - 414) / 61);
            float shoulder = 0;
            float radians = pose.headTilt * (float)Math.PI / 180;
            float dx = (-(y - 435) * radians + pose.leanX) * head + pose.leanX * .23f * shoulder;
            float dy = ((x - 400) * radians + pose.leanY) * head + pose.leanY * .3f * shoulder;
            float outer = V.Smooth((Math.Abs(x - 400) - 140) / 180);
            float ends = V.Smooth((y - 200) / 290) * (1 - V.Smooth((y - 580) / 46));
            dx += outer * ends * pose.hairSwing;
            dy += outer * ends * pose.hairLift;
            // 刘海轻微滞后，根部与额头保持连续。
            float fringe = (1 - V.Clamp(Math.Abs(x - 410) / 95, 0, 1)) * V.Smooth((y - 200) / 140) * (1 - V.Smooth((y - 350) / 75));
            dx += fringe * pose.hairSwing * .22f;
            // 头花和眼球交给独立层；领口与胸前装饰保持袖口外的轻微呼吸形变。
            float cloth = Math.Max(0,1-Math.Abs(x-400)/82) * V.Smooth((y-445)/20) * (1-V.Smooth((y-497)/32));
            dy += cloth * pose.hairLift * .8f;
            return new PointF(x + dx, y + dy);
        }

    }
}
