namespace PerchCat.Core;

// 所有逻辑坐标: 点, 原点在左下, y 向上 (和 Swift 版一致). 猫的 (x, y) = 脚底中心.
// Windows 的像素坐标进出都走 PixelSpace.

/// 一段能站的水平线: 窗口顶边或地板.
public readonly record struct Platform(double Lo, double Hi, double Top, int? WindowId = null, double? Base = null)
{
    /// WindowId 是窗口编号, 地板是 null; Base 只有地板有: 所在屏底边, [Base, Top) 里的猫会被托上来.
    public double Width => Hi - Lo;
}

public sealed record WindowInfo(Rect Rect, int Pid, int Id = 0, string? ClassName = null);

public readonly record struct ScreenInfo(Rect Frame, Rect Visible);

/// 一次起跳的抛物线. 顶点只比起点/终点中较高的那个再高 clearance.
public readonly struct Arc
{
    public double X0 { get; }
    public double Y0 { get; }
    public double Tx { get; }
    public double Ty { get; }
    public double Vy { get; }
    public double G { get; }
    public double T { get; }

    public Arc(double x0, double y0, double tx, double ty, double clearance, double g)
    {
        X0 = x0; Y0 = y0; Tx = tx; Ty = ty; G = g;
        var apex = Math.Max(y0, ty) + clearance;
        Vy = Math.Sqrt(2 * g * (apex - y0));
        T = Vy / g + Math.Sqrt(2 * (apex - ty) / g);
    }

    public (double X, double Y) Pos(double t)
    {
        var u = Math.Min(Math.Max(t / T, 0), 1);
        return (X0 + (Tx - X0) * u, Y0 + Vy * t - G * t * t / 2);
    }

    public string Pose(double t)
    {
        var u = t / T;
        return u < 0.4 ? "rise" : u <= 0.6 ? "apex" : "fall";
    }
}

public static class Geometry
{
    /// 每块屏一条地板 (工作区底边, 任务栏上方), 加上窗口顶边没被挡住的部分.
    /// windows 从前到后排 (EnumWindows 的 z 序). 跨屏的窗口在每块屏上各算一段 (Windows 上两边都看得见).
    public static Platform[] BuildPlatforms(IReadOnlyList<WindowInfo> windows, int ownPid, IReadOnlyList<ScreenInfo> screens,
                                            double catHeight, double minWidth)
    {
        var outp = new List<Platform>();
        foreach (var s in screens)
        {
            var full = windows.Any(w => IsFullscreen(w, s, ownPid));
            outp.Add(new Platform(full ? s.Frame.MinX : s.Visible.MinX, full ? s.Frame.MaxX : s.Visible.MaxX,
                                  full ? s.Frame.MinY : s.Visible.MinY, null, s.Frame.MinY));
        }
        var front = new List<Rect>();
        foreach (var w in windows)
        {
            if (w.Pid == ownPid || PlatformTuning.ShellClasses.Contains(w.ClassName ?? "")) continue;
            var clipped = new List<Rect>();
            foreach (var s in screens)
            {
                if (w.Rect.Intersection(s.Frame) is not { } r || r.Width <= 0 || r.Height <= 0) continue;
                clipped.Add(r);
                var top = r.MaxY;
                var screenTop = s.Frame.MaxY;
                if (r.Height >= PlatformTuning.MinHeight && top > s.Visible.MinY + 4
                    && top + catHeight <= screenTop && top <= screenTop - PlatformTuning.MenuBarSafe)
                {
                    var hidden = front.Where(f => f.MinY <= top && top <= f.MaxY).Select(f => (f.MinX, f.MaxX)).ToList();
                    foreach (var seg in Subtract(r.MinX, r.MaxX, hidden))
                        if (seg.Hi - seg.Lo >= minWidth) outp.Add(new Platform(seg.Lo, seg.Hi, top, w.Id));
                }
            }
            front.AddRange(clipped);        // 同一扇窗口的各段互不挡, 处理完才一起加入
        }
        return outp.ToArray();
    }

    /// 窗口铺满整块屏: 这时任务栏盖住了或藏着, 工作区却可能还留着它的高度.
    /// 最大化的窗口停在任务栏旁边 (底/顶/左/右总有一边够不到屏幕边), 不算.
    public static bool IsFullscreen(WindowInfo w, ScreenInfo s, int ownPid)
    {
        if (w.Pid == ownPid || PlatformTuning.ShellClasses.Contains(w.ClassName ?? "")) return false;
        var r = w.Rect; var f = s.Frame;
        return r.MinX <= f.MinX + 1 && r.MaxX >= f.MaxX - 1
            && r.MinY <= f.MinY + 1 && r.MinY >= f.MinY - 1
            && r.MaxY >= f.MaxY - PlatformTuning.MenuBarSafe - PlatformTuning.FullscreenSlack;
    }

    /// [lo, hi] 减去若干区间.
    public static List<(double Lo, double Hi)> Subtract(double lo, double hi, IEnumerable<(double, double)> holes)
    {
        var res = new List<(double Lo, double Hi)>();
        var cursor = lo;
        foreach (var h in holes.OrderBy(h => h.Item1))
        {
            if (h.Item1 > cursor) res.Add((cursor, Math.Min(h.Item1, hi)));
            cursor = Math.Max(cursor, h.Item2);
            if (cursor >= hi) return res;
        }
        res.Add((cursor, hi));
        return res;
    }

    /// 窗口这一帧相对上一帧要把站在上面的猫带多远. 窗口没了或没动返回 null.
    /// 宽度变了说明是在拖边改大小, 水平不跟.
    public static (double Dx, double Dy)? CarryDelta(int windowId, IReadOnlyList<WindowInfo> old, IReadOnlyList<WindowInfo> @new)
    {
        var a = old.FirstOrDefault(w => w.Id == windowId);
        var b = @new.FirstOrDefault(w => w.Id == windowId);
        if (a is null || b is null) return null;
        var dx = a.Rect.Width == b.Rect.Width ? b.Rect.MinX - a.Rect.MinX : 0;
        var dy = b.Rect.MaxY - a.Rect.MaxY;
        return dx == 0 && dy == 0 ? null : (dx, dy);
    }

    /// 脚下 (x, y) 是不是还有平台 (顶边在 y 上下 tol 以内).
    public static Platform? SupportAt(IEnumerable<Platform> ps, double x, double y, double tol = 3)
    {
        Platform? best = null;
        foreach (var p in ps)
            if (p.Lo <= x && x <= p.Hi && Math.Abs(p.Top - y) <= tol && (best is null || best.Value.Top < p.Top)) best = p;
        return best;
    }

    public readonly record struct FallResult(double X, double Y, double Vy, double Vx, Platform? Landed);

    /// 自由落体一步. vy 向上为正. 只会落在"起点不低于顶边、终点不高于顶边"的平台上, 取最高的, 所以不会穿过窗口.
    public static FallResult FallStep(double x, double y, double vy, double dt, double g, IReadOnlyList<Platform> platforms,
                                      double vx = 0, double halfWidth = 0, IReadOnlyList<Rect>? screens = null)
    {
        var nvy = vy - g * dt;
        var ny = y + nvy * dt;
        var nvx = vx;
        var nx = x + vx * dt;
        if (vx != 0)
        {
            // 前方脚下没有东西会接住 (屏幕外面, 或相邻屏幕的底比猫高): 当成墙弹回来
            var edge = nx + (vx > 0 ? halfWidth : -halfWidth);
            if (!platforms.Any(p => p.Lo <= edge && edge <= p.Hi && p.Top <= y + 1e-6))
            {
                nx = x;
                nvx = -vx * ThrowTuning.Bounce;
            }
        }
        if (nvy > 0 && screens is { Count: > 0 } && ScreenIndex(nx, ny, screens) is null)
        {
            ny = y;
            nvy = 0;
        }
        (Platform P, double X)? hit = null;
        foreach (var p in platforms)
        {
            if (!(y + 1e-6 >= p.Top && ny <= p.Top)) continue;
            var cx = y > ny ? x + (nx - x) * Math.Min(Math.Max((y - p.Top) / (y - ny), 0), 1) : nx;   // 穿过这个高度时的横坐标
            if (p.Lo <= cx && cx <= p.Hi && (hit is null || hit.Value.P.Top < p.Top)) hit = (p, cx);
        }
        if (hit is { } h) return new FallResult(h.X, h.P.Top, 0, 0, h.P);
        return new FallResult(nx, ny, nvy, nvx, null);
    }

    /// 抛物线上的点是否都在某块屏里. 只有相邻且有重叠的边才过得去. screens 为空表示不限制.
    public static bool ArcStaysOnScreens(Arc a, IReadOnlyList<Rect> screens)
    {
        if (screens.Count == 0) return true;
        const int n = 32;
        for (var i = 0; i <= n; i++)
        {
            var p = a.Pos(a.T * i / n);
            if (ScreenIndex(p.X, p.Y, screens) is null) return false;
        }
        return true;
    }

    /// (x, y) 在第几块屏. 留 1 点容差, 脚正好踩在屏的边界上也算.
    public static int? ScreenIndex(double x, double y, IReadOnlyList<Rect> screens)
    {
        for (var i = 0; i < screens.Count; i++) if (screens[i].Contains(x, y)) return i;
        for (var i = 0; i < screens.Count; i++) if (screens[i].InsetBy(-1, -1).Contains(x, y)) return i;
        return null;
    }

    /// 选下一个跳点: 不是脚下这块、水平够得着、不太高、allowed 放行. r1/r2 ∈ [0,1) 是随机数 (便于测试).
    public static (Platform Platform, double Tx)? PickJump(double x, double y, IReadOnlyList<Platform> platforms, double halfWidth,
                                                          double maxDX, double maxRise, double r1, double r2,
                                                          Func<Platform, double, bool>? allowed = null)
    {
        var cur = SupportAt(platforms, x, y);
        var cands = new List<(Platform Platform, double Tx)>();
        foreach (var p in platforms)
        {
            if (cur is { } c && p == c) continue;
            if (!(p.Top - y <= maxRise && Gap(x, p) <= maxDX)) continue;
            double lo = p.Lo + halfWidth, hi = p.Hi - halfWidth;
            var tx = hi <= lo ? (p.Lo + p.Hi) / 2 : Math.Min(Math.Max(x + (r2 - 0.5) * 240, lo), hi);
            if (allowed is null || allowed(p, tx)) cands.Add((p, tx));
        }
        if (cands.Count == 0) return null;
        return cands[Math.Min((int)(r1 * cands.Count), cands.Count - 1)];
    }

    public static double Gap(double x, Platform p) => x < p.Lo ? p.Lo - x : x > p.Hi ? x - p.Hi : 0;
}
