using PerchCat.Core;
using Xunit;
using static PerchCat.Core.Tests.B;

namespace PerchCat.Core.Tests;

public class PettingBrainTests
{
    [Fact]
    public void SquintThenHappyThenKnead()
    {
        var b = AwakeCat();
        b.Stroke();
        b.Tick(Dt, new[] { Floor });
        Assert.True(b.IsPetted);
        Assert.Equal("stand@squint", b.Frame());
        Run(b, PetTuning.HappyAfter, stroking: true);
        Assert.Equal("stand@happy", b.Frame());
        Run(b, PetTuning.KneadAfter - PetTuning.HappyAfter, stroking: true);
        Assert.Equal("knead", b.CurrentAnim);
        Assert.EndsWith("@happy", b.Frame());
    }

    [Fact]
    public void NoWalkingOrJumpingWhilePetted()
    {
        var win = new Platform(200, 800, 200);
        var b = Cat(rand: () => 0.7);                      // 0.7 = 选起跳
        Run(b, 0.25, on: new[] { Floor, win });            // 起跳预备中
        Assert.Equal("antic", b.CurrentAnim);
        var x = b.X;
        for (var i = 0; i < 80; i++)
        {
            b.Stroke();
            b.Tick(Dt, new[] { Floor, win });
            Assert.False(b.IsAirborne);
            Assert.Equal(x, b.X);
            Assert.Equal(70, b.Y);
        }
    }

    [Fact]
    public void StrokeStopsWalking()
    {
        var b = AwakeCat();
        Assert.Equal("walk", b.CurrentAnim);
        b.Stroke();
        var x = b.X;
        var seen = Run(b, 2, stroking: true);
        Assert.Equal(x, b.X);
        Assert.DoesNotContain(seen, f => f.StartsWith("walk"));
    }

    [Fact]
    public void GoesBackToDailyLifeAfterRelease()
    {
        var b = AwakeCat();
        Run(b, PetTuning.KneadAfter + 1, stroking: true);
        Run(b, PetTuning.Release - 0.25);
        Assert.True(b.IsPetted);                           // 停一下还不算走
        Run(b, 0.5);
        Assert.False(b.IsPetted);
        Assert.DoesNotContain("@squint", b.Frame());
        Assert.DoesNotContain("@happy", b.Frame());
        Assert.NotEqual("knead", b.CurrentAnim);
        var x = b.X;
        Run(b, 3);
        Assert.NotEqual(x, b.X);                           // 又走起来了
    }

    [Fact]
    public void PetEyesBeatBlink()
    {
        var b = Cat(rand: () => 0.01);
        var seen = Run(b, 3, stroking: true);
        Assert.DoesNotContain(seen.Skip(1), f => f.EndsWith("@closed") || f.EndsWith("@half"));
    }

    [Fact]
    public void PetSitsIfItWasLicking()
    {
        var b = Cat(rand: () => 0.01);                     // 总舔爪
        Run(b, 1);
        Assert.Equal("lick", b.CurrentAnim);
        Run(b, 1, stroking: true);
        Assert.Equal("sit@squint", b.Frame());
    }

    // 吵醒

    [Fact]
    public void StrokingSleeperHalfOpensThenSquintsWithoutGettingUp()
    {
        var b = SleepingCat();
        var y = b.Y;
        b.Stroke();
        b.Tick(Dt, new[] { Floor });
        Assert.EndsWith("@half", b.Frame());
        Assert.Equal("sleep", b.CurrentAnim);
        var seen = Run(b, PetTuning.StirHalf + 1, stroking: true);
        Assert.EndsWith("@squint", b.Frame());
        Assert.All(seen, f => Assert.StartsWith("sleep", f));
        Assert.Equal(y, b.Y);
    }

    [Fact]
    public void SleeperKeepsSleepingAfterRelease()
    {
        var b = SleepingCat();
        Run(b, 2, stroking: true);
        Assert.True(b.IsPetted);
        Run(b, PetTuning.Release + 0.25);
        Assert.False(b.IsPetted);
        Assert.True(b.IsSleeping);
        Assert.DoesNotContain("@", b.Frame());
        var seen = Run(b, 5);
        Assert.All(seen, f => Assert.StartsWith("sleep", f));
    }

    // 摸烦了

    [Fact]
    public void NotFedUpBeforeThreshold()
    {
        var b = AwakeCat();
        var seen = Run(b, PetTuning.FedUpAfter - 0.5, stroking: true);
        Assert.DoesNotContain(seen, f => f.StartsWith("puff"));
        Assert.True(b.IsPetted);
    }

    [Fact]
    public void FedUpPuffsThenWalksAway()
    {
        var b = AwakeCat();
        Run(b, PetTuning.FedUpAfter + 0.25, stroking: true);
        Assert.False(b.IsPetted);
        Assert.Equal("puff", b.CurrentAnim);
        Assert.StartsWith("puff", b.Frame());
        Assert.Equal("哈!", b.Bubble);
        var x = b.X;
        var seen = Run(b, 7);
        Assert.Contains(seen, f => f.StartsWith("walk"));
        Assert.True(Math.Abs(b.X - x) >= PetTuning.WalkAwayCells * 4 - 1);
    }

    [Fact]
    public void CooldownIgnoresStrokesThenRecovers()
    {
        var b = AwakeCat();
        Run(b, PetTuning.FedUpAfter + 0.25, stroking: true);
        var seen = Run(b, PetTuning.Cooldown - 1, stroking: true);
        Assert.False(b.IsPetted);
        Assert.DoesNotContain(seen, f => f.Contains("@squint") || f.Contains("@happy"));
        Run(b, 2);
        b.Stroke();
        Assert.True(b.IsPetted);
    }

    [Fact]
    public void WalkAwayStaysOnNarrowPlatform()
    {
        var ledge = new Platform(280, 320, 70);            // 比猫还窄, 没地方走
        var b = Cat();
        Run(b, PetTuning.FedUpAfter + 8, stroking: true, on: new[] { ledge });
        Assert.False(b.IsFalling);
        Assert.Equal(300, b.X);
    }

    [Fact]
    public void ReleaseResetsTheFedUpClock()
    {
        var b = AwakeCat();
        for (var i = 0; i < 3; i++)
        {
            var seen = Run(b, PetTuning.FedUpAfter * 0.6, stroking: true);
            Assert.DoesNotContain(seen, f => f.StartsWith("puff"));
            Run(b, PetTuning.Release + 0.25);
        }
    }

    // 空中与打断

    [Fact]
    public void AirborneIgnoresStrokesAndRespondsAfterLanding()
    {
        var win = new Platform(100, 600, 500);
        var b = Cat(300, 500, sleepChance: 1);
        Run(b, 0.25, on: new[] { Floor, win });
        b.Tick(Dt, new[] { Floor });                       // 窗口被拖走
        Assert.True(b.IsFalling);
        b.Stroke();
        Assert.False(b.IsPetted);
        var seen = new List<string>();
        while (b.IsFalling)
        {
            b.Stroke();
            b.Tick(Dt, new[] { Floor });
            seen.Add(b.Frame());
        }
        Assert.DoesNotContain(seen, f => f.Contains('@'));
        b.Stroke();
        Assert.False(b.IsPetted);                          // 落地动作演完才理你
        Run(b, 0.625, on: new[] { Floor });
        b.Stroke();
        Assert.True(b.IsPetted);
    }

    [Fact]
    public void MidJumpIgnoresStrokes()
    {
        var win = new Platform(200, 800, 200);
        var b = Cat(rand: () => 0.7);
        var n = 0;
        while (!b.IsAirborne && n < 40) { b.Tick(Dt, new[] { Floor, win }); n++; }
        Assert.True(b.IsAirborne);
        b.Stroke();
        Assert.False(b.IsPetted);
    }

    [Fact]
    public void WindowRemovedWhilePettedFallsCleanly()
    {
        var win = new Platform(100, 600, 500);
        var b = Cat(300, 500);
        Run(b, 1, stroking: true, on: new[] { Floor, win });
        Assert.True(b.IsPetted);
        b.Tick(Dt, new[] { Floor });
        Assert.True(b.IsFalling);
        Assert.False(b.IsPetted);
        Assert.Equal("fall", b.Frame());
    }

    [Fact]
    public void PokeInterruptsPetting()
    {
        var b = AwakeCat();
        Run(b, 1, stroking: true);
        b.Poke();
        b.Tick(Dt, new[] { Floor });
        Assert.False(b.IsPetted);
        Assert.Equal("puff", b.CurrentAnim);
        b.Stroke();                                        // 正炸毛, 摸也没用
        Assert.False(b.IsPetted);
    }

    [Fact]
    public void PokeDoesNotStartCooldown()
    {
        var b = AwakeCat();
        b.Poke();
        Run(b, 10);
        b.Stroke();
        Assert.True(b.IsPetted);
    }
}
