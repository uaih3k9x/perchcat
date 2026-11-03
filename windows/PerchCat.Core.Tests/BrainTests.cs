using PerchCat.Core;
using Xunit;
using static PerchCat.Core.Tests.B;

namespace PerchCat.Core.Tests;

public class BrainBasicTests
{
    [Fact]
    public void BrainFallsWhenWindowRemoved()
    {
        var win = new Platform(100, 600, 500);
        var b = Cat(300, 500, sleepChance: 1);
        Step(b, 10, Floor, win);
        Assert.False(b.IsFalling);
        Assert.Equal(500, b.Y);
        b.Tick(Dt, new[] { Floor });                       // 窗口被拖走
        Assert.True(b.IsFalling);
        Assert.Equal("fall", b.Frame());
        Step(b, 20, Floor);
        Assert.False(b.IsFalling);
        Assert.Equal(70, b.Y);
    }

    [Fact]
    public void BrainFallsOntoLowerWindow()
    {
        var hi = new Platform(100, 600, 500);
        var lo = new Platform(0, 700, 250);
        var b = Cat(300, 500, sleepChance: 1);
        b.Tick(Dt, new[] { Floor, hi, lo });
        b.Tick(Dt, new[] { Floor, lo });
        Step(b, 20, Floor, lo);
        Assert.Equal(250, b.Y);
    }

    [Fact]
    public void PokePuffsThenLicks()
    {
        var b = Cat(sleepChance: 1);
        Step(b, 5, Floor);
        b.Poke();
        b.Tick(Dt, new[] { Floor });
        Assert.Equal("puff", b.CurrentAnim);
        Assert.Equal("哈!", b.Bubble);
        Step(b, 20, Floor);
        Assert.Null(b.Bubble);
        Assert.Contains(b.CurrentAnim, new[] { "sit", "lick" });
    }

    [Fact]
    public void BlinkSequence()
    {
        var b = Cat(rand: () => 0.01);                     // 总选舔爪/不睡
        var seen = new List<string>();
        for (var i = 0; i < 400; i++) { b.Tick(Dt, new[] { Floor }); seen.Add(b.Frame()); }
        Assert.Contains(seen, f => f.EndsWith("@half"));
    }
}

public class ThrownCatTests
{
    [Fact]
    public void ThrowRightLandsToTheRight()
    {
        var b = Thrown(vx: 600);
        FlyUntilLanded(b, new[] { Floor });
        Assert.False(b.IsFalling);
        Assert.Equal(70, b.Y);
        Assert.True(b.X > 500);
    }

    [Fact]
    public void PlainReleaseStillFallsStraight()
    {
        var b = Thrown(vx: 0);
        FlyUntilLanded(b, new[] { Floor });
        Assert.Equal(300, b.X);
    }

    [Fact]
    public void UpwardThrowRisesFirst()
    {
        var b = Thrown(vx: 0, vy: 400);
        b.Tick(0.05, new[] { Floor });
        Assert.True(b.Y > 600);
        FlyUntilLanded(b, new[] { Floor });
        Assert.Equal(70, b.Y);
    }

    [Fact]
    public void ScreenEdgeBouncesInsteadOfTeleporting()
    {
        var b = Thrown(x: 1300, vx: 1600);
        var maxX = 0.0;
        for (var i = 0; i < 400 && b.IsFalling; i++)
        {
            b.Tick(0.05, new[] { Floor });
            maxX = Math.Max(maxX, b.X);
        }
        Assert.True(maxX + b.HalfWidth <= 1440 + 1e-6);
        Assert.True(b.X < 1440 - b.HalfWidth);             // 弹回来了
        Assert.Equal(70, b.Y);
    }

    [Fact]
    public void LandsOnAWindowItFliesOver()
    {
        var win = new Platform(700, 1000, 300, 7);
        var b = Thrown(300, 500, vx: 900);
        FlyUntilLanded(b, new[] { Floor, win });
        Assert.Equal(300, b.Y);
        Assert.True(b.X >= 700 && b.X <= 1000);
    }
}

public class LiftBrainTests
{
    [Fact]
    public void LiftStopsPlanningAndFollowsMouse()
    {
        var b = Cat();
        b.Tick(Dt, new[] { Floor });
        b.BeginLift(600, 700);
        Assert.True(b.IsLifted);
        Assert.True(b.IsAirborne);
        Step(b, 40, Floor);
        Assert.Equal(600, b.X);
        Assert.Equal(700, b.Y);                            // 不下落
        Assert.False(b.IsFalling);
        b.LiftTo(650, 720);
        Assert.Equal(650, b.X);
    }

    [Fact]
    public void ReleaseFallsToFloorAndResumes()
    {
        var b = Cat();
        b.BeginLift(600, 700);
        b.Tick(Dt, new[] { Floor });
        b.Release(590, 500);
        Assert.False(b.IsLifted);
        Assert.True(b.IsFalling);
        Assert.Equal("fall", b.Frame());
        var seen = new List<string>();
        for (var i = 0; i < 40; i++) { b.Tick(Dt, new[] { Floor }); seen.Add(b.CurrentAnim); }
        Assert.False(b.IsFalling);
        Assert.Equal(70, b.Y);
        Assert.Contains("landing", seen);
    }

    [Fact]
    public void ReleaseLandsOnWindowBelowNotFloor()
    {
        var win = new Platform(400, 900, 300, 1);
        var b = Cat();
        b.BeginLift(600, 700);
        b.Release(600, 600);
        Step(b, 40, Floor, win);
        Assert.Equal(300, b.Y);
    }

    [Fact]
    public void ReleaseOnOtherScreenLandsOnThatFloor()
    {
        var other = new Platform(1440, 3000, 900, null, 900);
        var b = Cat();
        b.BeginLift(2000, 1200);
        b.Release(2000, 1100);
        Step(b, 40, Floor, other);
        Assert.Equal(900, b.Y);
    }

    [Fact]
    public void ReleaseOffEveryScreenIsRescued()
    {
        var b = Cat();
        b.BeginLift(5000, 100);
        b.Release(5000, 40);
        Step(b, 5, Floor);
        Assert.True(b.X <= 1440);
        Assert.Equal(70, b.Y);
    }

    [Fact]
    public void PokeAndStrokeIgnoredWhileLifted()
    {
        var b = Cat();
        b.BeginLift(600, 700);
        b.Poke();
        b.Stroke();
        Assert.False(b.IsPetted);
        Assert.Null(b.Bubble);
    }

    [Fact]
    public void LiftInterruptsPettingAndClearsLook()
    {
        var b = Cat();
        b.Tick(Dt, new[] { Floor });
        b.Stroke();
        Assert.True(b.IsPetted);
        b.BeginLift(600, 700);
        Assert.False(b.IsPetted);
        Assert.Equal("fall", b.Frame());
    }

    [Fact]
    public void LiftInterruptsJumpAndFall()
    {
        var hi = new Platform(100, 600, 500, 1);
        var b = Cat(300, 500, sleepChance: 1);
        b.Tick(Dt, new[] { Floor, hi });
        b.Tick(Dt, new[] { Floor });
        Assert.True(b.IsFalling);
        b.BeginLift(300, 450);
        Assert.False(b.IsFalling);
        Step(b, 5, Floor);
        Assert.Equal(450, b.Y);
    }

    [Fact]
    public void LiftWakesSleepingCat()
    {
        var b = Cat(sleepChance: 1);
        Step(b, 12, Floor);
        Assert.True(b.IsSleeping);
        b.BeginLift(300, 300);
        Assert.False(b.IsSleeping);
    }

    [Fact]
    public void PetCooldownSurvivesBeingLifted()
    {
        var b = Cat();
        b.Tick(Dt, new[] { Floor });
        for (var i = 0; i < (int)(PetTuning.FedUpAfter / Dt + 2); i++) { b.Stroke(); b.Tick(Dt, new[] { Floor }); }
        b.BeginLift(300, 300);
        b.Release(300, 200);
        Step(b, 40, Floor);
        b.Stroke();
        Assert.False(b.IsPetted);
    }

    [Fact]
    public void CarryIgnoredWhileLifted()
    {
        var b = Cat();
        b.BeginLift(600, 700);
        b.Carry(50, 50);
        Assert.Equal(600, b.X);
    }

    [Fact]
    public void LiftPoseFollowsMotion()
    {
        var b = Cat();
        Assert.Null(b.LiftPose(5));
        b.BeginLift(0, 700);
        b.Tick(Dt, new[] { Floor });
        Assert.Equal(0, b.LiftPose(5)?.Stage);
        b.LiftTo(400, 700);
        b.Tick(Dt, new[] { Floor });
        var p = b.LiftPose(5);
        Assert.True(p?.Big);
        Assert.InRange(p!.Value.Index, 5, 7);
    }
}
