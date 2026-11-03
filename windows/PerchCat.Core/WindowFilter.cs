namespace PerchCat.Core;

/// Win32 层读到的一扇窗口 (只有位置和状态位, 不含标题和内容). 按 z 序从前到后排.
public sealed record RawWindow(int Id, int Pid, string ClassName, PixelRect Bounds,
                               bool Visible, bool Minimized, bool Cloaked, bool ToolWindow);

public static class WindowFilter
{
    /// 能不能当平台/挡人: 可见、没最小化、没被 DWM 隐藏 (别的虚拟桌面)、不是工具窗口、不是自己、不是桌面/任务栏.
    public static bool Accepts(RawWindow w, int ownPid) =>
        w.Visible && !w.Minimized && !w.Cloaked && !w.ToolWindow && w.Pid != ownPid
        && !PlatformTuning.ShellClasses.Contains(w.ClassName)
        && w.Bounds.Width > 0 && w.Bounds.Height > 0;

    public static WindowInfo ToInfo(RawWindow w, PixelSpace space) =>
        new(space.ToPoints(w.Bounds), w.Pid, w.Id, w.ClassName);
}
