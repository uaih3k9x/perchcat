using PerchCat.Core;
using Xunit;

namespace PerchCat.Core.Tests;

internal static class B
{
    public const double Dt = 0.125;
    public static readonly Platform Floor = new(0, 1440, 70);

    public static CatBrain Cat(double x = 300, double y = 70, double sleepChance = 0, Func<double>? rand = null, double scale = 4) =>
        new(scale, x, y, sleepChance, rand ?? (() => 0.5));

    public static void Step(CatBrain b, int n, params Platform[] ps)
    {
        for (var i = 0; i < n; i++) b.Tick(Dt, ps);
    }

    /// 跑 seconds 秒. stroking 时每一拍都蹭一下. 返回沿途见过的帧.
    public static List<string> Run(CatBrain b, double seconds, bool stroking = false, Platform[]? on = null)
    {
        var ps = on ?? new[] { Floor };
        var seen = new List<string>();
        for (var i = 0; i < (int)Math.Round(seconds / Dt); i++)
        {
            if (stroking) b.Stroke();
            b.Tick(Dt, ps);
            seen.Add(b.Frame());
        }
        return seen;
    }

    public static CatBrain AwakeCat()
    {
        var b = Cat();                      // 不睡, 在地板上来回走
        Run(b, 0.25);
        return b;
    }

    public static CatBrain SleepingCat()
    {
        var b = Cat(sleepChance: 1);        // 站 0.5 坐 0.5 然后睡
        Run(b, 1.5);
        Assert.True(b.IsSleeping);
        return b;
    }

    public static CatBrain Thrown(double x = 300, double y = 600, double vx = 0, double vy = 0)
    {
        var b = Cat();
        b.BeginLift(x, y);
        b.Release(x, y, vx, vy);
        return b;
    }

    public static void FlyUntilLanded(CatBrain b, Platform[] ps, int maxSteps = 400)
    {
        for (var i = 0; i < maxSteps && b.IsFalling; i++) b.Tick(0.05, ps);
    }
}

/// 确定性伪随机, 让仿真可复现.
internal sealed class Lcg
{
    ulong s;
    public Lcg(ulong seed) { s = seed; }
    public double Next()
    {
        unchecked { s = s * 6364136223846793005UL + 1442695040888963407UL; }
        return (double)(s >> 11) / (double)(1UL << 53);
    }
}
