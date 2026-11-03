using PerchCat.Core;
using Xunit;

namespace PerchCat.Core.Tests;

// 三块屏的布局 (点, y 向上): A 是主屏, 底下有任务栏; B 在 A 左上方, 更大; C 在 A 右边, 竖放.
// 只在测试里出现, 实现不写死.
public class PlatformBuildTests
{
    internal static readonly ScreenInfo A = new(new Rect(0, 0, 1512, 982), new Rect(0, 56, 1512, 888));
    internal static readonly ScreenInfo B = new(new Rect(-1048, 982, 2560, 1440), new Rect(-1048, 982, 2560, 1415));
    internal static readonly ScreenInfo C = new(new Rect(1512, 769, 1080, 1920), new Rect(1512, 769, 1080, 1895));
    internal static readonly ScreenInfo[] Screens = { A, B, C };
    internal static readonly Rect[] Rects = Screens.Select(s => s.Frame).ToArray();
    internal const double CatH = 128;

    /// x, 顶边, 宽, 高.
    internal static WindowInfo Win(int id, double x, double top, double w, double h = 300, int pid = 1, string? cls = null) =>
        new(new Rect(x, top - h, w, h), pid, id, cls);

    internal static Platform[] Build(IEnumerable<WindowInfo> ws, ScreenInfo[]? s = null, double minWidth = 100) =>
        Geometry.BuildPlatforms(ws.ToArray(), 99, s ?? Screens, CatH, minWidth);

    internal static Platform[] WindowPlatforms(IEnumerable<Platform> ps) => ps.Where(p => p.WindowId != null).ToArray();

    [Fact]
    public void EveryScreenHasAFloorAtTheWorkAreaBottom()
    {
        var floors = Build(Array.Empty<WindowInfo>()).Where(p => p.WindowId == null).ToArray();
        Assert.Equal(new[]
        {
            new Platform(0, 1512, 56, null, 0),
            new Platform(-1048, 1512, 982, null, 982),
            new Platform(1512, 2592, 769, null, 769),
        }, floors);
    }

    [Fact]
    public void SideTaskbarNarrowsTheFloor()
    {
        // 任务栏在左边 48 点: 工作区从 x=48 起, 地板也从那里起; 任务栏在右边同理
        var left = new ScreenInfo(new Rect(0, 0, 1000, 600), new Rect(48, 0, 952, 600));
        var right = new ScreenInfo(new Rect(0, 0, 1000, 600), new Rect(0, 0, 952, 600));
        Assert.Equal(new Platform(48, 1000, 0, null, 0), Build(Array.Empty<WindowInfo>(), new[] { left }).Single());
        Assert.Equal(new Platform(0, 952, 0, null, 0), Build(Array.Empty<WindowInfo>(), new[] { right }).Single());
    }

    [Fact]
    public void TopTaskbarKeepsFloorAtScreenBottom()
    {
        var top = new ScreenInfo(new Rect(0, 0, 1000, 600), new Rect(0, 0, 1000, 560));
        Assert.Equal(0, Build(Array.Empty<WindowInfo>(), new[] { top }).Single().Top);
    }

    [Fact]
    public void FiltersAbnormalWindows()
    {
        var ps = WindowPlatforms(Build(new[]
        {
            Win(2, 100, 500, 400, pid: 99),                  // 自己
            Win(3, 100, 500, 80),                            // 太窄
            Win(4, 100, 500, 400, h: 20),                    // 太矮
            Win(6, 100, 500, 400, cls: "Shell_TrayWnd"),
            Win(7, 100, 40, 400, h: 40),                     // 顶边低于地板
        }));
        Assert.Empty(ps);
    }

    [Fact]
    public void NonParticipantsDoNotOcclude()
    {
        var back = Win(2, 100, 500, 400);
        foreach (var front in new[] { Win(1, 200, 600, 200, pid: 99), Win(1, 200, 600, 200, cls: "Progman") })
            Assert.Equal(new[] { new Platform(100, 500, 500, 2) }, WindowPlatforms(Build(new[] { front, back })));
    }

    [Fact]
    public void NoMenuBarOnWindowsSoSafeMarginIsZero()
    {
        // Windows 没有屏幕顶部的菜单栏 (最大化窗口自己贴着屏幕顶, 任务栏在顶部时已经被工作区排除),
        // 所以不需要像 macOS 那样留 40 点. "头顶出屏" 的限制由 CatHeight 那条规则负责.
        Assert.Equal(0, PlatformTuning.MenuBarSafe);
        var s = new ScreenInfo(new Rect(0, 0, 1000, 600), new Rect(0, 0, 1000, 600));
        // 用矮猫单测这条规则: 顶边正好贴着屏顶的窗口算站得住 (猫高 1 就放不下了, 所以拿 0 高度)
        var ps = Geometry.BuildPlatforms(new[] { Win(1, 100, 600, 400) }, 99, new[] { s }, 0, 100);
        Assert.Single(WindowPlatforms(ps));
    }

    [Fact]
    public void CatHeadMustFitBelowScreenTop()
    {
        Assert.Empty(WindowPlatforms(Build(new[] { Win(1, 100, 982 - CatH + 1, 400) })));
        Assert.Single(WindowPlatforms(Build(new[] { Win(1, 100, 982 - CatH, 400) })));
    }

    [Fact]
    public void FrontWindowCutsTheEdgeBehind()
    {
        var ps = WindowPlatforms(Build(new[]
        {
            Win(1, 300, 700, 200),                           // 前: 盖住 300..500
            Win(2, 100, 500, 600),                           // 后: 剩 100..300, 500..700
        }));
        Assert.Contains(new Platform(100, 300, 500, 2), ps);
        Assert.Contains(new Platform(500, 700, 500, 2), ps);
        Assert.Contains(new Platform(300, 500, 700, 1), ps);
        Assert.Equal(3, ps.Length);
    }

    [Fact]
    public void ShortFragmentsAreDropped()
    {
        var ps = WindowPlatforms(Build(new[] { Win(1, 150, 700, 400), Win(2, 100, 500, 600) }));
        Assert.DoesNotContain(ps, p => p.WindowId == 2 && p.Lo == 100);
        Assert.Contains(new Platform(550, 700, 500, 2), ps);
    }

    [Fact]
    public void FrontWindowBelowOrAboveTheEdgeDoesNotCut()
    {
        var above = Win(1, 200, 800, 200, h: 100);           // 底边 700, 高于 500
        var below = Win(3, 200, 400, 200, h: 100);           // 顶边 400, 低于 500
        var ps = WindowPlatforms(Build(new[] { above, below, Win(2, 100, 500, 600) }));
        Assert.Contains(new Platform(100, 700, 500, 2), ps);
    }

    [Fact]
    public void FrontWindowTooSmallToStandOnStillOccludes()
    {
        var ps = WindowPlatforms(Build(new[]
        {
            Win(1, 300, 600, 60, h: 200),                    // 太窄不能站, 但会挡
            Win(2, 100, 500, 600),
        }));
        Assert.Equal(new[] { new Platform(100, 300, 500, 2), new Platform(360, 700, 500, 2) }, ps);
    }

    [Fact]
    public void WindowSpanningTwoScreensIsAPlatformOnEachOfThem()
    {
        // Windows 上跨屏的窗口两边都看得见 (不像 macOS 开了"显示器具有单独的空间"会被切掉), 所以每块屏各一段.
        var ps = WindowPlatforms(Build(new[] { Win(1, 1333, 1900, 1512, h: 887) }));
        Assert.Equal(new[] { new Platform(1333, 1512, 1900, 1), new Platform(1512, 2592, 1900, 1) }, ps);
    }

    [Fact]
    public void SpanningWindowOccludesOnEveryScreenItCovers()
    {
        // 窗口 1 横跨 B/C, 在 C 上盖住 1512..1600: 窗口 2 (在 C 上) 的这一段不能站
        var ps = WindowPlatforms(Build(new[]
        {
            Win(1, 1000, 1600, 600, h: 800),
            Win(2, 1520, 1500, 500, h: 400),
        }));
        Assert.Contains(new Platform(1600, 2020, 1500, 2), ps);
        Assert.DoesNotContain(ps, p => p.WindowId == 2 && p.Lo < 1600);
    }

    [Fact]
    public void WindowOffEveryScreenIsIgnored()
    {
        Assert.Empty(WindowPlatforms(Build(new[] { Win(1, 5000, 500, 400) })));
    }

    [Fact]
    public void CarryFollowsMoveAndResizeRules()
    {
        var a = Win(1, 100, 500, 400);
        Assert.Null(Geometry.CarryDelta(1, new[] { a }, new[] { a }));
        var moved = Geometry.CarryDelta(1, new[] { a }, new[] { Win(1, 130, 520, 400) });
        Assert.True(moved?.Dx == 30 && moved?.Dy == 20);
        var resized = Geometry.CarryDelta(1, new[] { a }, new[] { Win(1, 130, 520, 300) });   // 改了宽度: 不算平移, 顶边照跟
        Assert.True(resized?.Dx == 0 && resized?.Dy == 20);
        Assert.Null(Geometry.CarryDelta(1, new[] { a }, Array.Empty<WindowInfo>()));
    }

    [Fact]
    public void ArcRoutesFollowRealAdjacency()
    {
        Arc arc(double x0, double y0, double tx, double ty) => new(x0, y0, tx, ty, 24, 1360);
        Assert.True(Geometry.ArcStaysOnScreens(arc(1450, 982, 1560, 769), Rects));     // B -> C
        Assert.True(Geometry.ArcStaysOnScreens(arc(1560, 769, 1450, 982), Rects));     // C -> B
        Assert.True(Geometry.ArcStaysOnScreens(arc(700, 800, 700, 982), Rects));       // A 窗口 -> B
        Assert.True(Geometry.ArcStaysOnScreens(arc(700, 982, 700, 56), Rects));        // B -> A
        Assert.True(Geometry.ArcStaysOnScreens(arc(1450, 760, 1560, 769), Rects));     // A 高窗口 -> C
        Assert.False(Geometry.ArcStaysOnScreens(arc(1450, 300, 1560, 769), Rects));    // 太低, 穿过屏幕外
        Assert.False(Geometry.ArcStaysOnScreens(arc(-100, 982, 100, 56), Rects));      // B 左半下面没屏
        Assert.True(Geometry.ArcStaysOnScreens(arc(0, 0, 9999, 9999), Array.Empty<Rect>()));   // 空 = 不限制
    }
}

// 全屏: 任务栏被盖住 (或自动隐藏), 工作区还留着任务栏的高度, 地板要落到屏幕底边.
public class FullscreenFloorTests
{
    static double? FloorTop(Platform[] ps, double lo) => ps.FirstOrDefault(p => p.WindowId == null && p.Lo == lo).Top;

    static Platform[] B(params WindowInfo[] ws) => PlatformBuildTests.Build(ws);

    [Fact]
    public void FullscreenWindowDropsFloorToScreenBottom()
    {
        var ps = B(PlatformBuildTests.Win(1, 0, 982, 1512, 982));
        Assert.Equal(0, FloorTop(ps, 0));
        Assert.Equal(982, FloorTop(ps, -1048));                  // 别的屏不受影响
    }

    [Fact]
    public void MaximizedWindowAboveTaskbarKeepsFloor()
    {
        Assert.Equal(56, FloorTop(B(PlatformBuildTests.Win(1, 0, 944, 1512, 888)), 0));
    }

    [Fact]
    public void MaximizedWithTaskbarOnTheTopKeepsFloorLogic()
    {
        // 任务栏在顶: 最大化窗口底边贴着屏幕底, 但顶边在任务栏下面, 不是全屏
        var s = new ScreenInfo(new Rect(0, 0, 1000, 600), new Rect(0, 0, 1000, 560));
        var ps = Geometry.BuildPlatforms(new[] { PlatformBuildTests.Win(1, 0, 560, 1000, 560) }, 99, new[] { s }, 10, 100);
        Assert.Equal(0, ps.First(p => p.WindowId == null).Top);
        Assert.Single(ps, p => p.WindowId == 1);                 // 窗口顶边仍可站
    }

    [Fact]
    public void FullscreenWithSubPixelRoundingStillCounts()
    {
        Assert.Equal(0, FloorTop(B(PlatformBuildTests.Win(1, 0.4, 981.6, 1511.3, 981.2)), 0));
    }

    [Fact]
    public void NarrowOrIgnoredWindowsKeepFloor()
    {
        Assert.Equal(56, FloorTop(B(PlatformBuildTests.Win(1, 0, 982, 800, 982)), 0));
        Assert.Equal(56, FloorTop(B(PlatformBuildTests.Win(1, 0, 982, 1512, 982, pid: 99)), 0));
        Assert.Equal(56, FloorTop(B(PlatformBuildTests.Win(1, 0, 982, 1512, 982, cls: "Progman")), 0));
    }
}
