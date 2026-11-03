namespace PerchCat.Core;

/// 物理像素矩形, Windows 约定: 原点在主屏左上, y 向下, Right/Bottom 是开边.
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

public readonly record struct RawMonitor(PixelRect Frame, PixelRect Work);

/// 像素 (y 向下) <-> 逻辑点 (y 向上) 的唯一一份换算.
///
/// 整个桌面共用一个线性映射: 不看各屏自己的 DPI, 所以混合 DPI 下相邻屏的边在点坐标里依然相接, 猫走过去不会跳.
/// 代价是猫的像素大小在所有屏上一样 (按启动时主屏 DPI 定, 一格取整数个像素, 保证最近邻缩放不糊).
/// referenceHeightPx 启动时定死 (主屏高), 之后分辨率变了猫也不会被整体平移.
public sealed class PixelSpace
{
    private readonly int refH;
    public double PixelsPerPoint { get; }
    /// 猫图一格 (逻辑里 4 点) 占几个像素.
    public int CellPixels { get; }

    public PixelSpace(double pixelsPerPoint, int referenceHeightPx)
    {
        if (pixelsPerPoint <= 0) throw new ArgumentOutOfRangeException(nameof(pixelsPerPoint));
        PixelsPerPoint = pixelsPerPoint;
        CellPixels = Math.Max(1, (int)Math.Round(4 * pixelsPerPoint, MidpointRounding.AwayFromZero));
        refH = referenceHeightPx;
    }

    /// 按主屏 DPI 选格子大小: 96 DPI 一格 4 像素, 144 DPI 一格 6 像素, 以此类推.
    public static PixelSpace ForDpi(int dpi, int referenceHeightPx)
    {
        var cell = Math.Max(1, (int)Math.Round(4.0 * dpi / 96, MidpointRounding.AwayFromZero));
        return new PixelSpace(cell / 4.0, referenceHeightPx);
    }

    public double ToPointX(double px) => px / PixelsPerPoint;
    public double ToPointY(double py) => (refH - py) / PixelsPerPoint;

    public Rect ToPoints(PixelRect r) =>
        new(r.Left / PixelsPerPoint, (refH - r.Bottom) / PixelsPerPoint, r.Width / PixelsPerPoint, r.Height / PixelsPerPoint);

    public PixelRect ToPixels(Rect r) =>
        new(R(r.MinX * PixelsPerPoint), R(refH - r.MaxY * PixelsPerPoint), R(r.MaxX * PixelsPerPoint), R(refH - r.MinY * PixelsPerPoint));

    public (int X, int Y) ToPixels(double x, double y) => (R(x * PixelsPerPoint), R(refH - y * PixelsPerPoint));

    public ScreenInfo ToScreen(RawMonitor m) => new(ToPoints(m.Frame), ToPoints(m.Work));

    static int R(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);
}
