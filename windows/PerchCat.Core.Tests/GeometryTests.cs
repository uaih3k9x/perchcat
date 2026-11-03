using PerchCat.Core;
using Xunit;

namespace PerchCat.Core.Tests;

public class GeometryTests
{
    [Fact]
    public void Support()
    {
        var ps = new[] { new Platform(0, 1000, 70), new Platform(100, 600, 700) };
        Assert.Equal(700, Geometry.SupportAt(ps, 300, 700)?.Top);
        Assert.Equal(700, Geometry.SupportAt(ps, 300, 702)?.Top);   // 容差内
        Assert.Null(Geometry.SupportAt(ps, 700, 700));              // 站的窗口没了/挪开
        Assert.Equal(70, Geometry.SupportAt(ps, 700, 70)?.Top);
    }

    static Platform? FallFrom(double x, double y, Platform[] ps)
    {
        double vy = 0; Platform? landed = null;
        for (var i = 0; i < 200 && landed is null; i++)
        {
            var r = Geometry.FallStep(x, y, vy, 0.125, 1360, ps);
            y = r.Y; vy = r.Vy; landed = r.Landed;
        }
        Assert.NotNull(landed);
        Assert.Equal(landed!.Value.Top, y);
        return landed;
    }

    [Fact]
    public void FallLandsOnWindowBelowNotFloor()
    {
        var ps = new[] { new Platform(0, 1000, 70), new Platform(0, 400, 300) };
        Assert.Equal(300, FallFrom(200, 700, ps)!.Value.Top);
    }

    [Fact]
    public void FallToFloorWhenNothingBelow()
    {
        var ps = new[] { new Platform(0, 1000, 70) };
        Assert.Equal(70, FallFrom(900, 700, ps)!.Value.Top);
    }

    [Fact]
    public void ArcApexIsClearanceAboveHigherEnd()
    {
        var a = new Arc(0, 100, 300, 400, 24, 1360);
        var maxY = double.NegativeInfinity;
        for (var t = 0.0; t < a.T; t += 0.001) maxY = Math.Max(maxY, a.Pos(t).Y);
        Assert.Equal(424, maxY, 0.5);
        Assert.Equal(400, a.Pos(a.T).Y, 1e-6);
        Assert.Equal(300, a.Pos(a.T).X, 1e-6);
        var d = new Arc(0, 400, 300, 100, 24, 1360);          // 往下跳: 顶点比起点高 24
        Assert.Equal(100, d.Pos(d.T).Y, 1e-6);
        Assert.Equal(Math.Sqrt(2 * 1360 * 24), d.Vy, 1e-9);
    }

    [Fact]
    public void PickJump()
    {
        var floor = new Platform(0, 1440, 70);
        var win = new Platform(500, 900, 300);
        var far = new Platform(3000, 3400, 300);
        var ps = new[] { floor, win, far };
        var j = Geometry.PickJump(100, 70, ps, 40, 500, 400, 0, 0.5);
        Assert.Equal(win, j!.Value.Platform);                 // 脚下的地板不算, 太远的不算
        Assert.Equal(540, j.Value.Tx, 1e-9);                  // 夹进 [lo+半宽, hi-半宽]
        Assert.Null(Geometry.PickJump(100, 70, ps, 40, 200, 400, 0, 0));
        Assert.Null(Geometry.PickJump(100, 70, ps, 40, 500, 100, 0, 0));
    }

    [Fact]
    public void FastThrowCannotTunnelPastAWindowEdgeAtTheCrossing()
    {
        var win = new Platform(400, 500, 100);
        var r = Geometry.FallStep(300, 120, -400, 0.1, 0, new[] { new Platform(0, 1440, 70), win }, vx: 2200, halfWidth: 0);
        Assert.Equal(100, r.Landed?.Top);
        Assert.True(r.X > 400 && r.X < 500);
    }

    [Fact]
    public void CeilingStopsTheRise()
    {
        var screens = new[] { new Rect(0, 0, 1440, 900) };
        var r = Geometry.FallStep(300, 895, 500, 0.1, 0, new[] { new Platform(0, 1440, 70) }, 0, 0, screens);
        Assert.Equal(895, r.Y);
        Assert.Equal(0, r.Vy);
    }

    [Fact]
    public void GapBetweenScreensActsAsAWall()
    {
        var laptop = new Platform(0, 1512, 56);
        var tall = new Platform(1512, 2592, 769);
        var low = Geometry.FallStep(1450, 500, 0, 0.1, 0, new[] { laptop, tall }, vx: 1000, halfWidth: 48);
        Assert.Equal(1450, low.X);
        Assert.True(low.Vx < 0);
        var high = Geometry.FallStep(1450, 800, 0, 0.1, 0, new[] { laptop, tall }, vx: 1000, halfWidth: 48);
        Assert.Equal(1550, high.X);
        Assert.Equal(1000, high.Vx);
    }

    [Fact]
    public void ScreenIndexHasOnePointTolerance()
    {
        var screens = new[] { new Rect(0, 0, 100, 100), new Rect(100, 0, 100, 100) };
        Assert.Equal(0, Geometry.ScreenIndex(50, 50, screens));
        Assert.Equal(1, Geometry.ScreenIndex(100, 50, screens));
        Assert.Equal(1, Geometry.ScreenIndex(200.5, 50, screens));   // 脚正好踩在边界上也算
        Assert.Null(Geometry.ScreenIndex(300, 50, screens));
    }
}
