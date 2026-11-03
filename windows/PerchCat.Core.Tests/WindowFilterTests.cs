using PerchCat.Core;
using Xunit;

namespace PerchCat.Core.Tests;

public class WindowFilterTests
{
    static RawWindow Win(string cls = "Chrome_WidgetWin_1", int pid = 1, bool visible = true, bool min = false,
                         bool cloaked = false, bool tool = false, PixelRect? b = null) =>
        new(Id: 5, Pid: pid, ClassName: cls, Bounds: b ?? new PixelRect(100, 100, 600, 500),
            Visible: visible, Minimized: min, Cloaked: cloaked, ToolWindow: tool);

    [Fact] public void OrdinaryWindowPasses() => Assert.True(WindowFilter.Accepts(Win(), ownPid: 99));
    [Fact] public void InvisibleIsSkipped() => Assert.False(WindowFilter.Accepts(Win(visible: false), 99));
    [Fact] public void MinimizedIsSkipped() => Assert.False(WindowFilter.Accepts(Win(min: true), 99));
    [Fact] public void CloakedIsSkipped() => Assert.False(WindowFilter.Accepts(Win(cloaked: true), 99));       // 别的虚拟桌面
    [Fact] public void ToolWindowIsSkipped() => Assert.False(WindowFilter.Accepts(Win(tool: true), 99));
    [Fact] public void OwnProcessIsSkipped() => Assert.False(WindowFilter.Accepts(Win(pid: 99), 99));
    [Fact] public void EmptyBoundsAreSkipped() => Assert.False(WindowFilter.Accepts(Win(b: new PixelRect(5, 5, 5, 300)), 99));

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    public void ShellWindowsAreSkipped(string cls) => Assert.False(WindowFilter.Accepts(Win(cls: cls), 99));

    [Fact]
    public void ToWindowInfoConvertsBoundsAndKeepsIdentity()
    {
        var s = new PixelSpace(1.0, 1080);
        var w = WindowFilter.ToInfo(Win(), s);
        Assert.Equal(new Rect(100, 580, 500, 400), w.Rect);
        Assert.Equal(5, w.Id);
        Assert.Equal(1, w.Pid);
    }
}
