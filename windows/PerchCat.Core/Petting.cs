namespace PerchCat.Core;

public readonly record struct Pt(double X, double Y);

/// 把鼠标采样变成"这一下算不算在蹭": 头上累计移动距离, 随时间漏掉. 悬停不攒.
public sealed class RubSensor
{
    private readonly double cell;
    private Pt? last;
    private double charge;

    public RubSensor(double cell) { this.cell = cell; }

    public bool Sample(Pt p, bool inHead, double dt)
    {
        var moved = last is { } l ? Math.Sqrt((p.X - l.X) * (p.X - l.X) + (p.Y - l.Y) * (p.Y - l.Y)) : 0;
        last = p;
        charge = Math.Max(0, charge - PetTuning.RubLeakCellsPerSec * cell * dt);
        if (!inHead || moved < PetTuning.RubMinStepPoints) return false;
        charge = Math.Min(charge + Math.Min(moved, PetTuning.RubMaxStepCells * cell), PetTuning.RubCapCells * cell);
        return charge >= PetTuning.RubOnCells * cell;
    }
}

public static class Petting
{
    const int HeadCols = 14, HeadRows = 13;
    static readonly (int Col, int Row) StandHead = (10, 5);

    /// 各姿势头的左上角在第几列第几行 (对着皮肤数出来的). 没列的 (走路等) 和 stand 差不到一格.
    static readonly Dictionary<string, (int Col, int Row)> HeadAt = new()
    {
        ["sit"] = (7, 3), ["lick0"] = (7, 3), ["lick1"] = (7, 3), ["lick2"] = (7, 4),
        ["knead0"] = (10, 7), ["knead1"] = (10, 8), ["sleep0"] = (8, 9), ["sleep1"] = (8, 10),
        ["half"] = (10, 7), ["crouch"] = (10, 9), ["fall"] = (10, 7),
    };

    /// 猫头在屏幕上的矩形. frame 如 "knead1@happy"; catRect 是整张图的矩形; dir<0 时图是镜像的.
    public static Rect HeadRect(string frame, Rect catRect, double cell, double dir)
    {
        var at = frame.IndexOf('@');
        var pose = at >= 0 ? frame[..at] : frame;
        var h = HeadAt.TryGetValue(pose, out var v) ? v : StandHead;
        double w = HeadCols * cell, tall = HeadRows * cell;
        var left = dir >= 0 ? h.Col * cell : catRect.Width - h.Col * cell - w;
        return new Rect(catRect.MinX + left, catRect.MaxY - h.Row * cell - tall, w, tall);
    }
}
