namespace PerchCat.Core;

/// 矩形. 约定同 Swift 版 (AppKit/CoreGraphics): 原点 (X, Y) 在左下, Y 向上, Contains 对最大边是开区间.
public readonly record struct Rect(double X, double Y, double Width, double Height)
{
    public double MinX => X;
    public double MinY => Y;
    public double MaxX => X + Width;
    public double MaxY => Y + Height;
    public double MidX => X + Width / 2;
    public double MidY => Y + Height / 2;

    public bool Contains(double px, double py) => px >= MinX && px < MaxX && py >= MinY && py < MaxY;

    public Rect InsetBy(double dx, double dy) => new(X + dx, Y + dy, Width - 2 * dx, Height - 2 * dy);

    public Rect Offset(double dx, double dy) => new(X + dx, Y + dy, Width, Height);

    /// 不相交返回 null; 只贴边返回零宽/零高的矩形.
    public Rect? Intersection(Rect o)
    {
        var x0 = Math.Max(MinX, o.MinX); var x1 = Math.Min(MaxX, o.MaxX);
        var y0 = Math.Max(MinY, o.MinY); var y1 = Math.Min(MaxY, o.MaxY);
        return x1 < x0 || y1 < y0 ? null : new Rect(x0, y0, x1 - x0, y1 - y0);
    }

    public Rect Union(Rect o)
    {
        var x0 = Math.Min(MinX, o.MinX); var y0 = Math.Min(MinY, o.MinY);
        return new Rect(x0, y0, Math.Max(MaxX, o.MaxX) - x0, Math.Max(MaxY, o.MaxY) - y0);
    }
}
