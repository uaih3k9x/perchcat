using PerchCat.Core;
using Xunit;

namespace PerchCat.Core.Tests;

public class RubSensorTests
{
    const double Cell = 4.0;

    /// 在 x0 起点, 以 step 每拍来回 (每 period 拍换一次方向), 数蹭到的拍数.
    static int RubCount(RubSensor s, int samples, double step, int period, bool inHead = true)
    {
        double x = 100.0, d = 1.0; var n = 0;
        for (var i = 0; i < samples; i++)
        {
            if (i % period == 0) d = -d;
            x += d * step;
            if (s.Sample(new Pt(x, 50), inHead, 0.1)) n++;
        }
        return n;
    }

    [Fact]
    public void HoverIsNotPetting()
    {
        var s = new RubSensor(Cell);
        for (var i = 0; i < 100; i++) Assert.False(s.Sample(new Pt(10, 10), true, 0.1));
    }

    [Fact] public void RubbingBackAndForthTriggersWithinASecond() => Assert.True(RubCount(new RubSensor(Cell), 10, 30, 3) > 0);
    [Fact] public void RubbingOutsideHeadDoesNothing() => Assert.Equal(0, RubCount(new RubSensor(Cell), 50, 30, 3, inHead: false));

    [Fact]
    public void PassingStraightAcrossHeadDoesNotCount()
    {
        // 头宽 14 格 = 56 点, 中速一次穿过去
        var s = new RubSensor(Cell);
        var triggered = false;
        for (var i = 0; i < 12; i++)
        {
            var x = i * 10.0;
            if (s.Sample(new Pt(x, 0), x <= 56, 0.1)) triggered = true;
        }
        Assert.False(triggered);
    }

    [Fact]
    public void OneHugeJumpDoesNotCount()
    {
        var s = new RubSensor(Cell);
        s.Sample(new Pt(0, 0), false, 0.1);
        Assert.False(s.Sample(new Pt(2000, 0), true, 0.1));
    }

    [Fact] public void SlowCreepDoesNotCount() => Assert.Equal(0, RubCount(new RubSensor(Cell), 200, 2, 5));   // 20 点/秒, 比漏掉的还慢

    [Fact]
    public void StoppingStopsCounting()
    {
        var s = new RubSensor(Cell);
        Assert.True(RubCount(s, 20, 30, 3) > 0);
        var p = new Pt(100, 50);
        s.Sample(p, true, 0.1);
        Assert.False(s.Sample(p, true, 0.1));
    }
}

public class HeadRectTests
{
    static readonly Rect Cat = new(100, 200, 96, 96);

    [Fact]
    public void StandFacingRight() =>
        // stand 头在第 10 列第 5 行, 14x13 格, 每格 4 点; y 向上
        Assert.Equal(new Rect(140, 224, 56, 52), Petting.HeadRect("stand@squint", Cat, 4, 1));

    [Fact] public void FacingLeftMirrors() => Assert.Equal(new Rect(100, 224, 56, 52), Petting.HeadRect("stand", Cat, 4, -1));

    [Fact]
    public void HeadMovesWithPose()
    {
        Assert.Equal(128, Petting.HeadRect("sit", Cat, 4, 1).MinX);
        Assert.Equal(256, Petting.HeadRect("sleep1@half", Cat, 4, 1).MaxY);
        Assert.Equal(264, Petting.HeadRect("knead1", Cat, 4, 1).MaxY);
    }
}

public class ThrowTests
{
    const double Cell = 4.0;

    [Fact]
    public void FlickGivesTheSpeedOfTheLastMoment()
    {
        var t = new ThrowTracker();
        t.Add(0, 0, 0);
        t.Add(0, 0, 1);            // 拎着停了很久, 不算
        t.Add(50, 0, 1.05);
        t.Add(100, 10, 1.1);
        var v = t.Velocity(1.1);
        Assert.True(Math.Abs(v.X - 1000) < 1e-6);
        Assert.True(Math.Abs(v.Y - 100) < 1e-6);
    }

    [Fact]
    public void PausingBeforeLettingGoDropsStraight()
    {
        var t = new ThrowTracker();
        t.Add(0, 0, 1);
        t.Add(100, 0, 1.05);
        var v = t.Velocity(1.5);
        Assert.True(v.X == 0 && v.Y == 0);
    }

    [Fact]
    public void SingleSampleHasNoSpeed()
    {
        var t = new ThrowTracker();
        t.Add(5, 5, 1);
        Assert.Equal(0, t.Velocity(1).X);
    }

    [Fact]
    public void SlowLetGoIsAPlainDrop()
    {
        var v = Lift.ThrowVelocity(100, -100, Cell);
        Assert.True(v.Vx == 0 && v.Vy == 0);
    }

    [Fact]
    public void HardFlickIsCapped()
    {
        var v = Lift.ThrowVelocity(90000, 90000, Cell);
        Assert.Equal(ThrowTuning.MaxSideCells * Cell, v.Vx);
        Assert.Equal(ThrowTuning.MaxUpCells * Cell, v.Vy);
        Assert.Equal(-ThrowTuning.MaxSideCells * Cell, Lift.ThrowVelocity(-90000, 0, Cell).Vx);
    }

    [Fact]
    public void DownwardFlickIsNotCappedByTheUpLimit() =>
        Assert.True(Lift.ThrowVelocity(0, -3000, Cell).Vy < -ThrowTuning.MaxUpCells * Cell);
}

public class PressGestureTests
{
    [Fact]
    public void ClickWithoutMovingPokes()
    {
        var g = new PressGesture();
        g.Down(new Pt(10, 10));
        Assert.True(g.IsPressed);
        Assert.Equal(PressAction.Click, g.Up());
        Assert.False(g.IsPressed);
    }

    [Fact]
    public void JitterBelowThresholdIsStillAClick()
    {
        var g = new PressGesture();
        g.Down(new Pt(10, 10));
        Assert.Equal(PressAction.None, g.Moved(new Pt(10.5, 10.2)));
        Assert.Equal(PressAction.Click, g.Up());
    }

    [Fact]
    public void PullingUpLiftsThenMovesThenDrops()
    {
        var g = new PressGesture();
        g.Down(new Pt(10, 10));
        Assert.Equal(PressAction.Rub, g.Moved(new Pt(10, 15)));      // 还没够高
        Assert.Equal(PressAction.Lift, g.Moved(new Pt(11, 30)));
        Assert.Equal(PressAction.Move, g.Moved(new Pt(30, 10)));     // 拎起后随便往哪拖
        Assert.Equal(PressAction.Move, g.Moved(new Pt(10, -90)));
        Assert.Equal(PressAction.Drop, g.Up());
        Assert.Equal(PressAction.None, g.Up());
    }

    [Fact]
    public void SidewaysPressIsARubNotALift()
    {
        var g = new PressGesture();
        g.Down(new Pt(10, 10));
        Assert.Equal(PressAction.Rub, g.Moved(new Pt(60, 10)));
        Assert.Equal(PressAction.Rub, g.Moved(new Pt(10, 11)));
        Assert.Equal(PressAction.Rub, g.Moved(new Pt(80, 13)));
        Assert.Equal(PressAction.Click, g.Up());
    }

    [Fact]
    public void ExactlyTheLiftDistanceLifts()
    {
        // 往上 16 点 (LiftUpPoints), 横向为 0: 刚好够
        var g = new PressGesture();
        g.Down(new Pt(0, 0));
        Assert.Equal(PressAction.Rub, g.Moved(new Pt(0, 15.9)));
        Assert.Equal(PressAction.Lift, g.Moved(new Pt(0, 16)));
    }

    [Fact]
    public void RatioMustBeAtLeastOnePointFive()
    {
        var g = new PressGesture();
        g.Down(new Pt(0, 0));
        Assert.Equal(PressAction.Rub, g.Moved(new Pt(20, 29)));      // 29 < 20 * 1.5
        var h = new PressGesture();
        h.Down(new Pt(0, 0));
        Assert.Equal(PressAction.Lift, h.Moved(new Pt(20, 30)));     // 30 = 20 * 1.5, 且 >= 16
    }

    [Fact]
    public void PushingDownNeverLifts()
    {
        var g = new PressGesture();
        g.Down(new Pt(10, 10));
        Assert.Equal(PressAction.Rub, g.Moved(new Pt(10, -100)));
        Assert.Equal(PressAction.Click, g.Up());
    }

    [Fact]
    public void ShallowDiagonalIsARub()
    {
        var g = new PressGesture();
        g.Down(new Pt(0, 0));
        Assert.Equal(PressAction.Rub, g.Moved(new Pt(100, 30)));
    }

    [Fact]
    public void CanPullUpAfterRubbing()
    {
        var g = new PressGesture();
        g.Down(new Pt(10, 10));
        Assert.Equal(PressAction.Rub, g.Moved(new Pt(90, 10)));
        Assert.Equal(PressAction.Rub, g.Moved(new Pt(90, -40)));
        Assert.Equal(PressAction.Lift, g.Moved(new Pt(92, -15)));    // 从最后的低点往上提
    }

    [Fact]
    public void MovingWithoutPressDoesNothing() => Assert.Equal(PressAction.None, new PressGesture().Moved(new Pt(100, 100)));

    [Fact]
    public void CancelOnlyDropsWhenLifted()
    {
        var a = new PressGesture();
        a.Down(new Pt(0, 0));
        Assert.Equal(PressAction.None, a.Cancel());                  // 丢了松手事件: 没拖动就不算点击
        Assert.False(a.IsPressed);
        var b = new PressGesture();
        b.Down(new Pt(0, 0));
        b.Moved(new Pt(0, 50));
        Assert.Equal(PressAction.Drop, b.Cancel());
        var c = new PressGesture();
        c.Down(new Pt(0, 0));
        c.Moved(new Pt(50, 0));
        Assert.Equal(PressAction.None, c.Cancel());                  // 只是在蹭
    }
}

public class LiftMotionTests
{
    const double Cell = 4.0, Dt = 0.125;

    [Fact]
    public void StaysOnFirstStageThenDroopsStepByStepToLast()
    {
        var m = new LiftMotion(0, Cell);
        var seen = new List<int>();
        for (var i = 0; i < 80; i++) { m.Update(0, Dt); seen.Add(m.Pose(5).Stage); }
        Assert.Equal(0, seen.First());
        Assert.Equal(4, seen.Last());
        Assert.Equal(seen.OrderBy(x => x).ToList(), seen);           // 只往下坠
        Assert.Equal(new HashSet<int> { 0, 1, 2, 3, 4 }, seen.ToHashSet());   // 不跳帧
        Assert.True(seen.Count(x => x == 0) * (int)(Dt * 1000) >= (int)(LiftTuning.DroopAfter * 1000) - 125);
    }

    [Fact]
    public void SingleStageSheetStaysOnIt()
    {
        var m = new LiftMotion(0, Cell);
        for (var i = 0; i < 80; i++) m.Update(0, Dt);
        Assert.Equal(0, m.Pose(1).Stage);
    }

    [Fact]
    public void HangingUsesSmallSwayCycle()
    {
        var m = new LiftMotion(0, Cell);
        var idx = new List<int>();
        for (var i = 0; i < 8; i++)
        {
            m.Update(0, Dt);
            var p = m.Pose(5);
            Assert.False(p.Big);
            idx.Add(p.Index);
        }
        Assert.Equal(8, idx.Distinct().Count());
    }

    /// 大摆第 1-3 帧身子偏右, 第 5-7 帧偏左.
    static LiftPose BigPose(double speedCells)
    {
        var m = new LiftMotion(0, Cell);
        m.Update(speedCells * Cell * Dt, Dt);
        return m.Pose(5);
    }

    [Fact]
    public void DraggingRightSwingsBodyLeft()
    {
        var p = BigPose(200);
        Assert.True(p.Big);
        Assert.InRange(p.Index, 5, 7);
    }

    [Fact]
    public void DraggingLeftSwingsBodyRight()
    {
        var p = BigPose(-200);
        Assert.True(p.Big);
        Assert.InRange(p.Index, 1, 3);
    }

    [Fact] public void SlowMoveKeepsSmallSway() => Assert.False(BigPose(2).Big);

    [Fact] public void FasterDragSwingsFarther() => Assert.True(BigPose(200).Index > BigPose(LiftTuning.DragCellsPerSec + 1).Index);

    [Fact]
    public void StoppingReturnsToSmallSway()
    {
        var m = new LiftMotion(0, Cell);
        m.Update(200, Dt);
        Assert.True(m.Pose(5).Big);
        for (var i = 0; i < 8; i++) m.Update(200, Dt);
        Assert.False(m.Pose(5).Big);
    }

    [Fact]
    public void ZeroDtDoesNotBlowUp()
    {
        var m = new LiftMotion(0, Cell);
        m.Update(100, 0);
        Assert.False(m.Pose(5).Big);
    }
}

public class LiftGeometryTests
{
    const double Cell = 4.0;

    [Fact]
    public void GripCellSitsUnderMouse()
    {
        var r = Lift.LiftedRect(new Pt(500, 800), (11, 3), 24, 34, Cell);
        Assert.Equal(96, r.Width);
        Assert.Equal(136, r.Height);
        var gx = r.MinX + 11 * Cell; var gy = r.MaxY - 4 * Cell;     // 第 11 列第 3 行的格子
        Assert.True(new Rect(gx, gy, Cell, Cell).Contains(500, 800));
    }

    [Fact]
    public void TallFrameGrowsDownwardOnly()
    {
        var a = Lift.LiftedRect(new Pt(500, 800), (11, 3), 24, 24, Cell);
        var d = Lift.LiftedRect(new Pt(500, 800), (11, 3), 24, 34, Cell);
        Assert.Equal(a.MaxY, d.MaxY);
        Assert.Equal(a.MinX, d.MinX);
        Assert.Equal(a.MinY - 40, d.MinY);
    }

    [Fact]
    public void MirroredFrameMirrorsGrip()
    {
        var r = Lift.LiftedRect(new Pt(500, 800), (17, 9), 24, 24, Cell, -1);
        // 镜像后那一格在第 6 列
        Assert.True(new Rect(r.MinX + 6 * Cell, r.MaxY - 10 * Cell, Cell, Cell).Contains(500, 800));
    }

    [Fact]
    public void StaysOnWholePoints()
    {
        var r = Lift.LiftedRect(new Pt(500.3, 800.7), (11, 3), 24, 24, Cell);
        Assert.Equal(Math.Round(r.MinX), r.MinX);
        Assert.Equal(Math.Round(r.MinY), r.MinY);
    }

    [Fact]
    public void DropKeepsHeadWhereItWas()
    {
        foreach (var dir in new[] { 1.0, -1.0 })
        {
            var p = Lift.DropPoint(500, 800, Cell, 24, 24, 1, dir);
            var rect = new Rect(p.X - 48, p.Y - Cell, 96, 96);
            var head = Petting.HeadRect("fall", rect, Cell, dir);
            Assert.Equal(500, head.MidX);
            Assert.Equal(800, head.MaxY);
        }
    }
}
