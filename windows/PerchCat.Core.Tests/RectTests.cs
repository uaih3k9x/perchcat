using PerchCat.Core;
using Xunit;

namespace PerchCat.Core.Tests;

public class RectTests
{
    [Fact]
    public void ContainsIsHalfOpenOnMaxEdges()
    {
        var r = new Rect(0, 0, 10, 10);
        Assert.True(r.Contains(0, 0));
        Assert.False(r.Contains(10, 5));
        Assert.False(r.Contains(5, 10));
        Assert.True(r.Contains(9.99, 9.99));
    }

    [Fact]
    public void IntersectionAndInset()
    {
        var a = new Rect(0, 0, 10, 10);
        Assert.Equal(new Rect(5, 5, 5, 5), a.Intersection(new Rect(5, 5, 10, 10)));
        Assert.Null(a.Intersection(new Rect(20, 0, 5, 5)));
        Assert.Equal(new Rect(-1, -1, 12, 12), a.InsetBy(-1, -1));
        Assert.Equal(5, a.MidX);
    }
}
