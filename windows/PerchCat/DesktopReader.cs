using System;
using System.Collections.Generic;
using System.Text;
using PerchCat.Core;

namespace PerchCat;

/// 读桌面布局: 窗口 (z 序从前到后) 和显示器. 全部是物理像素; 筛选和换算交给 Core.
internal static class DesktopReader
{
    /// 只读位置和状态位. 不取标题、不取内容. 类名只用来认桌面/任务栏.
    public static List<RawWindow> ReadWindows()
    {
        var list = new List<RawWindow>();
        var sb = new StringBuilder(256);
        Native.EnumWindows((h, _) =>
        {
            if (!Native.IsWindowVisible(h)) return true;        // 不可见的直接略过 (Core 的过滤器对此也不收)
            Native.GetWindowThreadProcessId(h, out var pid);
            sb.Clear();
            Native.GetClassName(h, sb, sb.Capacity);
            var bounds = FrameBounds(h);
            if (bounds is null) return true;
            var cloaked = Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out int c, sizeof(int)) == 0 && c != 0;
            var ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE).ToInt64();
            list.Add(new RawWindow((int)h.ToInt64(), (int)pid, sb.ToString(), bounds.Value,
                                   Visible: true, Minimized: Native.IsIconic(h), Cloaked: cloaked,
                                   ToolWindow: (ex & Native.WS_EX_TOOLWINDOW) != 0));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// 可见边框 (不含 Win10/11 窗口四周看不见的阴影区); 取不到就退回 GetWindowRect.
    static PixelRect? FrameBounds(IntPtr h)
    {
        if (Native.DwmGetWindowAttribute(h, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out Native.RECT r, 16) == 0)
            return new PixelRect(r.Left, r.Top, r.Right, r.Bottom);
        return Native.GetWindowRect(h, out r) ? new PixelRect(r.Left, r.Top, r.Right, r.Bottom) : null;
    }

    /// 每块显示器的整块区域和工作区 (扣掉任务栏; 任务栏在哪条边、是否自动隐藏都由系统算好).
    public static List<RawMonitor> ReadMonitors(out PixelRect primaryFrame, out uint primaryDpi)
    {
        var list = new List<RawMonitor>();
        PixelRect primary = default;
        uint dpi = 96;
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr m, IntPtr dc, ref Native.RECT rc, IntPtr l) =>
        {
            var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
            if (!Native.GetMonitorInfo(m, ref info)) return true;
            var frame = new PixelRect(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right, info.rcMonitor.Bottom);
            var work = new PixelRect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom);
            list.Add(new RawMonitor(frame, work));
            if ((info.dwFlags & Native.MONITORINFOF_PRIMARY) != 0)
            {
                primary = frame;
                if (Native.GetDpiForMonitor(m, 0, out var dx, out _) == 0 && dx > 0) dpi = dx;   // MDT_EFFECTIVE_DPI
            }
            return true;
        }, IntPtr.Zero);
        primaryFrame = primary;
        primaryDpi = dpi;
        return list;
    }

    public static bool TryCursor(out int x, out int y)
    {
        var ok = Native.GetCursorPos(out var p);
        x = p.X; y = p.Y;
        return ok;
    }

    /// 主按键 (考虑左右手互换) 是否按着.
    public static bool PrimaryDown()
    {
        var vk = Native.GetSystemMetrics(Native.SM_SWAPBUTTON) != 0 ? Native.VK_RBUTTON : Native.VK_LBUTTON;
        return (Native.GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    public static bool AnyButtonDown() =>
        Down(Native.VK_LBUTTON) || Down(Native.VK_RBUTTON) || Down(Native.VK_MBUTTON);

    static bool Down(int vk) => (Native.GetAsyncKeyState(vk) & 0x8000) != 0;
}
