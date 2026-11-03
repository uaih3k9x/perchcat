using PerchCat.Core;
using Xunit;

namespace PerchCat.Core.Tests;

// Windows 给的是物理像素、y 向下; 逻辑坐标是"点"、y 向上 (和 Swift 版一样). 这里测的是中间那层转换.
public class PixelSpaceTests
{
    [Theory]
    [InlineData(96, 4, 1.0)]
    [InlineData(120, 5, 1.25)]
    [InlineData(144, 6, 1.5)]
    [InlineData(168, 7, 1.75)]
    [InlineData(192, 8, 2.0)]
    public void OneCatCellIsAWholeNumberOfPixels(int dpi, int cellPx, double k)
    {
        var s = PixelSpace.ForDpi(dpi, referenceHeightPx: 1080);
        Assert.Equal(cellPx, s.CellPixels);
        Assert.Equal(k, s.PixelsPerPoint, 12);
        Assert.Equal(cellPx, 4 * s.PixelsPerPoint, 12);
    }

    [Fact]
    public void FlipsYAndKeepsSize()
    {
        var s = new PixelSpace(1.0, 1080);
        // 像素 (100,200)-(500,600), y 向下 -> 点: 底边 1080-600=480, 顶边 880
        var r = s.ToPoints(new PixelRect(100, 200, 500, 600));
        Assert.Equal(new Rect(100, 480, 400, 400), r);
        Assert.Equal(880, r.MaxY);
    }

    [Fact]
    public void ScalesByPixelsPerPoint()
    {
        var s = new PixelSpace(1.5, 1080);
        var r = s.ToPoints(new PixelRect(150, 0, 450, 1080));
        Assert.Equal(100, r.MinX, 9);
        Assert.Equal(200, r.Width, 9);
        Assert.Equal(0, r.MinY, 9);
        Assert.Equal(720, r.Height, 9);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(2.0)]
    public void RoundTripIsLossless(double k)
    {
        var s = new PixelSpace(k, 1440);
        foreach (var px in new[] { new PixelRect(-1920, -300, 0, 780), new PixelRect(0, 0, 2560, 1440), new PixelRect(2560, 123, 3001, 987), new PixelRect(7, 9, 8, 10) })
            Assert.Equal(px, s.ToPixels(s.ToPoints(px)));
    }

    [Fact]
    public void MixedDpiMonitorsStayAdjacentInPoints()
    {
        // 主屏 1920x1080 (150%), 右边 3840x2160 (200%) 顶对顶: 像素坐标是连续的, 点坐标也必须连续,
        // 否则猫走过边界会跳. 转换是全局唯一的线性映射, 不看屏幕自己的 DPI.
        var s = PixelSpace.ForDpi(144, 1080);
        var a = s.ToPoints(new PixelRect(0, 0, 1920, 1080));
        var b = s.ToPoints(new PixelRect(1920, 0, 5760, 2160));
        Assert.Equal(a.MaxX, b.MinX, 9);
        Assert.Equal(720, a.MaxY, 9);
        Assert.Equal(720, b.MaxY, 9);
    }

    [Fact]
    public void MonitorLeftAndAbovePrimaryHasNegativePixelsAndPositivePointsAbove()
    {
        var s = new PixelSpace(1.0, 1080);
        var above = s.ToPoints(new PixelRect(0, -1080, 1920, 0));       // 主屏上方
        Assert.Equal(1080, above.MinY);
        Assert.Equal(2160, above.MaxY);
        var left = s.ToPoints(new PixelRect(-1280, 0, 0, 1024));
        Assert.Equal(-1280, left.MinX);
    }

    [Fact]
    public void CatDoesNotMoveWhenDpiOfAnotherMonitorChanges()
    {
        // 参考高度和 k 是启动时固定的: 同一块像素区域任何时候都映射到同一块点区域.
        var s = PixelSpace.ForDpi(144, 1080);
        var before = s.ToPoints(new PixelRect(500, 500, 700, 600));
        var after = s.ToPoints(new PixelRect(500, 500, 700, 600));
        Assert.Equal(before, after);
    }

    [Fact]
    public void PointToPixelsRoundsToNearest()
    {
        var s = new PixelSpace(1.25, 1000);
        Assert.Equal((125, 1000 - 250), s.ToPixels(100, 200));
        Assert.Equal((1, 999), s.ToPixels(0.9, 0.9));
    }

    [Fact]
    public void MonitorsConvertBothFrameAndWorkArea()
    {
        var s = new PixelSpace(1.0, 1080);
        // 任务栏在底下 40 像素: 工作区底边比屏幕底边高 40 点, 地板就是这条线
        var info = s.ToScreen(new RawMonitor(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040)));
        Assert.Equal(new Rect(0, 0, 1920, 1080), info.Frame);
        Assert.Equal(new Rect(0, 40, 1920, 1040), info.Visible);
    }
}
