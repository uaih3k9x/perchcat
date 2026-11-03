using PerchCat.Core;
using Xunit;
using static PerchCat.Core.Tests.B;
using static PerchCat.Core.Tests.PlatformBuildTests;

namespace PerchCat.Core.Tests;

public class CatFollowTests
{
    static readonly Platform FloorA = new(0, 1512, 56, null, 0);

    [Fact]
    public void CarryMovesCatAndWalkTarget()
    {
        var w = new Platform(100, 900, 500, 1);
        var b = Cat(300, 500, rand: () => 0.3);            // r=0.3: 走
        Step(b, 1, FloorA, w);
        var x0 = b.X;
        b.Carry(200, 100);
        Assert.True(b.X == x0 + 200 && b.Y == 600);
        var moved = new Platform(300, 1100, 600, 1);
        Step(b, 200, FloorA, moved);
        Assert.True(!b.IsFalling && b.Y == 600);           // 全程没走出挪动后的窗口
    }

    [Fact]
    public void CarryIgnoredInAir()
    {
        var b = Cat(300, 500, sleepChance: 1);
        Step(b, 1, FloorA);
        Assert.True(b.IsFalling);
        var x = b.X;
        b.Carry(200, 100);
        Assert.Equal(x, b.X);
    }

    [Fact]
    public void CoveredEdgeDropsCatToNextPlatform()
    {
        var lower = new Platform(0, 900, 200, 2);
        var high = new Platform(100, 900, 500, 1);
        var b = Cat(300, 500, sleepChance: 1);
        Step(b, 2, FloorA, high, lower);
        var rest = new Platform(500, 900, 500, 1);         // 前面来了个窗口, 平台碎成两段, 猫脚下这段没了
        Step(b, 20, FloorA, rest, lower);
        Assert.Equal(200, b.Y);
    }

    [Fact]
    public void RisingFloorLiftsCat()
    {
        var b = Cat(300, 56, sleepChance: 1);
        Step(b, 2, FloorA);
        var higher = new Platform(0, 1512, 120, null, 0);
        Step(b, 12, higher);
        Assert.True(b.Y == 120 && !b.IsFalling);
    }

    [Fact]
    public void DroppingFloorLetsCatFall()
    {
        var b = Cat(300, 120, sleepChance: 1);
        Step(b, 2, new Platform(0, 1512, 120, null, 0));
        var lower = new Platform(0, 1512, 0, null, 0);
        Step(b, 1, lower);
        Assert.True(b.IsFalling);
        Step(b, 20, lower);
        Assert.True(b.Y == 0 && !b.IsFalling);
    }

    [Fact]
    public void RisingFloorDoesNotGrabCatOnAnotherScreen()
    {
        // 猫站在 A 的地板; B 的地板 (任务栏换到 B) x 范围也盖着它, 但 y 不在 B 里
        var floorB = new Platform(-1048, 1512, 1035, null, 982);
        var b = Cat(300, 56, sleepChance: 1);
        Step(b, 3, FloorA, floorB);
        Assert.Equal(56, b.Y);
    }

    [Fact]
    public void LostCatComesBackWhenItsScreenDisappears()
    {
        var floorB = new Platform(-1048, 1512, 982, null, 982);
        var b = Cat(2000, 769, sleepChance: 1);            // C 被拔
        Step(b, 2, FloorA, floorB);
        Assert.True(b.X <= 1512 && !b.IsFalling);
        Assert.True(b.Y == 56 || b.Y == 982);
    }
}

public class RouteSimulationTests
{
    // 三块屏 + A 上两级台阶 (地板够不到靠上的窗口) + C/B 各一个窗口.
    static readonly Platform[] Ps = Geometry.BuildPlatforms(new[]
    {
        Win(1, 500, 700, 900, 400),      // A 靠上
        Win(4, 100, 380, 350, 200),      // A 台阶
        Win(2, 1600, 1000, 500, 200),    // C
        Win(3, 300, 1500, 800, 400),     // B
    }, 99, Screens, 128, 100);

    static void Sim(double sleepChance, double crossChance, ulong seed, double hours,
                    Action<int>? visit = null, Action<int, int>? crossed = null, Action? jumped = null)
    {
        var r = new Lcg(seed);
        var b = new CatBrain(4, 756, 56, sleepChance, r.Next) { Screens = Rects, CrossChance = crossChance };
        int last = 0; var wasArc = false;
        for (var k = 0; k < (int)(hours * 3600 / Dt); k++)
        {
            b.Tick(Dt, Ps);
            var i = Geometry.ScreenIndex(b.X, b.Y, Rects);
            Assert.True(i != null, $"脚底跑到屏幕外: {b.X}, {b.Y}");     // 不变量: 任何时刻脚底都在某块屏里
            visit?.Invoke(i!.Value);
            if (!b.IsAirborne && i != last) { crossed?.Invoke(last, i!.Value); last = i!.Value; }
            if (b.IsJumping && !wasArc) jumped?.Invoke();
            wasArc = b.IsJumping;
        }
    }

    [Fact]
    public void CatVisitsAllScreensAndNeverLeavesThem()
    {
        var seen = new HashSet<int>(); var pairs = new HashSet<(int, int)>();
        Sim(0, 1, 7, 2, i => seen.Add(i), (a, c) => pairs.Add((a, c)));
        Assert.Equal(new HashSet<int> { 0, 1, 2 }, seen);
        Assert.Contains((1, 2), pairs);      // B -> C
        Assert.Contains((2, 1), pairs);      // C -> B
        Assert.Contains((0, 1), pairs);      // A -> B
        Assert.Contains((1, 0), pairs);      // B -> A
    }

    [Fact]
    public void DefaultsKeepJumpsAndCrossingsRare()
    {
        int jumps = 0, crossings = 0;
        Sim(0.55, PlatformTuning.CrossChance, 11, 8, crossed: (_, _) => crossings++, jumped: () => jumps++);
        double jph = jumps / 8.0, cph = crossings / 8.0;
        Assert.True(jph < 60, $"每小时跳 {jph}");
        Assert.True(cph < 15, $"每小时跨屏 {cph}");
        Assert.True(crossings > 0);
    }

    [Fact]
    public void SameScreenOnlyWhenCrossingDisabled()
    {
        var pairs = 0;
        Sim(0, 0, 3, 2, crossed: (_, _) => pairs++);       // 不会主动换屏
        Assert.Equal(0, pairs);
    }
}

// 地板变化和睡着的猫.
public class FloorJumpAndSleepDropTests
{
    static readonly Platform Low = new(0, 1512, 0, null, 0);
    static readonly Platform High = new(0, 1512, 56, null, 0);

    static CatBrain Sleeper(Platform[] ps, double x = 300, double y = 0)
    {
        var b = Cat(x, y, sleepChance: 1);
        Step(b, 12, ps);
        return b;
    }

    [Fact]
    public void RisingFloorMakesCatJumpInsteadOfTeleport()
    {
        var b = Cat(300, 0);
        b.Tick(Dt, new[] { Low });
        b.Tick(Dt, new[] { High });
        Assert.True(b.IsJumping && b.Y < 56);
        var n = 0;
        while (b.IsJumping && n < 40) { b.Tick(Dt, new[] { High }); n++; }
        Assert.True(!b.IsJumping && b.Y == 56 && !b.IsFalling);
    }

    [Fact]
    public void TinyFloorChangeJustSnaps()
    {
        var b = Cat(300, 54);
        b.Tick(Dt, new[] { High });
        Assert.True(!b.IsJumping && b.Y == 56);
    }

    [Fact]
    public void SleepingCatIsNotCarriedByItsWindow()
    {
        var w = new Platform(100, 900, 500, 1);
        var b = Sleeper(new[] { High, w }, y: 500);
        Assert.True(b.IsSleeping);
        b.Carry(200, 100);
        Assert.True(b.X == 300 && b.Y == 500);
    }

    [Fact]
    public void AwakeCatStillCarried()
    {
        var w = new Platform(100, 900, 500, 1);
        var b = Cat(300, 500, rand: () => 0.3);
        b.Tick(Dt, new[] { High, w });
        b.Carry(200, 0);
        Assert.True(b.X > 300);
    }

    [Fact]
    public void DroppedSleeperShakesThenSleepsAgain()
    {
        var w = new Platform(100, 900, 500, 1);
        var b = Sleeper(new[] { High, w }, y: 500);
        Assert.True(b.IsSleeping);
        var seen = new List<string>();
        for (var i = 0; i < 80; i++)
        {
            b.Tick(Dt, new[] { High });                    // 窗口被拖走
            if (seen.Count == 0 || seen[^1] != b.CurrentAnim) seen.Add(b.CurrentAnim);
        }
        Assert.Equal("fall", seen[0]);
        Assert.True(seen.Contains("landing") && seen.Contains("shake"));
        Assert.True(b.Y == 56 && b.IsSleeping);
    }

    [Fact]
    public void ThrownCatDoesNotGoBackToSleep()
    {
        var b = Cat(300, 56);
        b.Tick(Dt, new[] { High });
        b.BeginLift(300, 400);
        b.Release(300, 400);
        var seen = new HashSet<string>();
        for (var i = 0; i < 60; i++) { b.Tick(Dt, new[] { High }); seen.Add(b.CurrentAnim); }
        Assert.True(!seen.Contains("shake") && !b.IsSleeping);
    }

    [Fact]
    public void ShakeFramesExistInEveryBeat()
    {
        var b = Sleeper(new[] { High, new Platform(100, 900, 500, 1) }, y: 500);
        var frames = new HashSet<string>();
        for (var i = 0; i < 60; i++)
        {
            b.Tick(Dt, new[] { High });
            if (b.CurrentAnim == "shake") frames.Add(b.Frame());
        }
        Assert.True(frames.Count >= 2);                    // 真的在抖, 不是一张静图
    }

    [Fact]
    public void CatAlternatesTwoShakeFramesAndCannotBePetted()
    {
        var w = new Platform(100, 900, 500, 1);
        var floorP = new Platform(0, 1512, 56, null, 0);
        var b = Cat(300, 500, sleepChance: 1);
        Step(b, 12, floorP, w);
        var n = 0;
        while (b.CurrentAnim != "shake" && n < 80) { b.Tick(Dt, new[] { floorP }); n++; }
        Assert.Equal("shake", b.CurrentAnim);
        b.Stroke();
        Assert.False(b.IsPetted);
        var seen = new HashSet<string>();
        while (b.CurrentAnim == "shake" && n < 200) { seen.Add(b.Frame()); b.Tick(Dt, new[] { floorP }); n++; }
        Assert.Equal(new HashSet<string> { "shake0", "shake1" }, seen);
    }
}
