namespace PerchCat.Core;

/// 记下拖动轨迹, 松手时算甩出去的速度.
public sealed class ThrowTracker
{
    private readonly List<(double X, double Y, double T)> samples = new();

    public void Add(double x, double y, double at)
    {
        samples.Add((x, y, at));
        samples.RemoveAll(s => s.T < at - ThrowTuning.Window - 1e-9);
    }

    public (double X, double Y) Velocity(double now)
    {
        if (samples.Count == 0) return (0, 0);
        var last = samples[^1]; var first = samples[0];
        if (!(last.T > first.T) || now - last.T > ThrowTuning.Idle) return (0, 0);
        var dt = last.T - first.T;
        return ((last.X - first.X) / dt, (last.Y - first.Y) / dt);
    }
}

public enum PressAction { None, Lift, Move, Rub, Click, Drop }

/// 把猫身上的一次按下分成: 点一下、按着蹭、往上提. 只有在猫窗口收到按下之后才算数.
public sealed class PressGesture
{
    private Pt? start;
    private Pt anchor;           // 这一段上提从哪开始; 往下或往旁边挪就重新算
    private Pt last;
    private bool lifted;

    public bool IsPressed => start is not null;

    public void Down(Pt p) { start = p; anchor = p; last = p; lifted = false; }

    public PressAction Moved(Pt p)
    {
        if (start is null) return PressAction.None;
        if (lifted) return PressAction.Move;
        var step = Math.Sqrt((p.X - last.X) * (p.X - last.X) + (p.Y - last.Y) * (p.Y - last.Y));
        last = p;
        double up = p.Y - anchor.Y, side = Math.Abs(p.X - anchor.X);
        if (up < side * LiftTuning.LiftUpRatio || up <= 0)
        {
            anchor = p;
            return step >= PetTuning.RubMinStepPoints ? PressAction.Rub : PressAction.None;
        }
        if (up >= LiftTuning.LiftUpPoints) { lifted = true; return PressAction.Lift; }
        return step >= PetTuning.RubMinStepPoints ? PressAction.Rub : PressAction.None;
    }

    public PressAction Up()
    {
        if (start is null) return PressAction.None;
        var a = lifted ? PressAction.Drop : PressAction.Click;
        start = null; lifted = false;
        return a;
    }

    /// 没等到松手事件但键已经松了: 拎着就放下, 否则当没发生过.
    public PressAction Cancel()
    {
        var a = lifted ? PressAction.Drop : PressAction.None;
        start = null; lifted = false;
        return a;
    }
}

/// 拎着时的时间和速度. 大摆帧约定: 第 1-3 帧身子偏右, 第 5-7 帧偏左, 帧号越大越开.
public sealed class LiftMotion
{
    private readonly double cell;
    private double elapsed;
    private double lastX;
    private double lagLeft;
    private int lagBase;

    public LiftMotion(double x, double cell) { lastX = x; this.cell = cell; }

    public void Update(double x, double dt)
    {
        if (!(dt > 0)) return;
        elapsed += dt;
        var v = (x - lastX) / dt / cell;
        lastX = x;
        if (Math.Abs(v) >= LiftTuning.DragCellsPerSec)
        {
            lagBase = (v > 0 ? 5 : 1) + (Math.Abs(v) >= LiftTuning.FastCellsPerSec ? 1 : 0);   // 身子落在拖动方向的反面
            lagLeft = LiftTuning.LagHold;
        }
        else
        {
            lagLeft = Math.Max(0, lagLeft - dt);
        }
    }

    public LiftPose Pose(int stages)
    {
        var beat = (int)((elapsed + 1e-6) / LiftTuning.SwayBeat);
        var drooped = elapsed < LiftTuning.DroopAfter ? 0 : 1 + (int)((elapsed - LiftTuning.DroopAfter + 1e-6) / LiftTuning.DroopStep);
        var stage = Math.Min(drooped, Math.Max(stages - 1, 0));
        return lagLeft > 0 ? new LiftPose(stage, true, lagBase + beat % 2) : new LiftPose(stage, false, beat % 8);
    }
}

public static class Lift
{
    /// 鼠标速度折算成猫飞出去的初速度: 太慢算放下, 横向和向上封顶, 向下不封.
    public static (double Vx, double Vy) ThrowVelocity(double mouseVX, double mouseVY, double cell)
    {
        if (Math.Sqrt(mouseVX * mouseVX + mouseVY * mouseVY) < ThrowTuning.MinMouseSpeed) return (0, 0);
        var side = ThrowTuning.MaxSideCells * cell;
        return (Math.Min(Math.Max(mouseVX * ThrowTuning.Gain, -side), side),
                Math.Min(mouseVY * ThrowTuning.Gain, ThrowTuning.MaxUpCells * cell));
    }

    static double Round(double v) => Math.Round(v, MidpointRounding.AwayFromZero);

    /// 拎起时猫图的矩形: 拎点这一格的中心对准鼠标, 帧再高也只向下长. dir<0 时图是镜像的.
    public static Rect LiftedRect(Pt mouse, (int Col, int Row) grip, int cols, int rows, double cell, double dir = 1)
    {
        var col = dir >= 0 ? grip.Col : cols - 1 - grip.Col;
        var left = Round(mouse.X - (col + 0.5) * cell);
        var top = Round(mouse.Y + (grip.Row + 0.5) * cell);
        return new Rect(left, top - rows * cell, cols * cell, rows * cell);
    }

    /// 松手后 fall 姿势的脚底位置: 头留在原地不跳, 身子缩回去.
    public static (double X, double Y) DropPoint(double headMidX, double headTop, double cell, int cols, int rows, int footInset, double dir)
    {
        var w = cols * cell;
        var head = Petting.HeadRect("fall", new Rect(0, 0, w, rows * cell), cell, dir);
        return (headMidX - head.MidX + w / 2, headTop - head.MaxY + footInset * cell);
    }
}
