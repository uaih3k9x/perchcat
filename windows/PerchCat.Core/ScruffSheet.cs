using System.Text.Json;

namespace PerchCat.Core;

/// 该画哪一帧: 第几阶段, 小摆还是大摆, 第几帧.
public readonly record struct LiftPose(int Stage, bool Big, int Index);

/// 拎起帧: 高度可以比皮肤高, 所以不放进 Skin.
public sealed class ScruffSheet
{
    private readonly IReadOnlyDictionary<char, Rgb> palette;
    private readonly List<(string[][] Small, string[][] Big)> stages;
    public int Cols { get; }
    public (int Col, int Row) Grip { get; }
    /// 头水平中心在第几列; 头顶在第 0 行.
    public int HeadMidCol { get; }
    public int StageCount => stages.Count;

    private ScruffSheet(IReadOnlyDictionary<char, Rgb> pal, int cols, (int, int) grip, int headMid, List<(string[][], string[][])> st)
    { palette = pal; Cols = cols; Grip = grip; HeadMidCol = headMid; stages = st; }

    public static ScruffSheet Parse(byte[] data)
    {
        static SkinError Bad(string why) => new($"拎起帧无效: {why}");
        using var doc = Json.TryParse(data);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object) throw Bad("缺字段");
        var o = doc.RootElement;
        if (!o.TryGetProperty("palette", out var pal) || !o.TryGetProperty("grip", out var gripE)
            || gripE.ValueKind != JsonValueKind.Array || gripE.GetArrayLength() != 2
            || !o.TryGetProperty("headMidCol", out var hm) || !hm.TryGetInt32(out var headMid)
            || !o.TryGetProperty("stages", out var raw) || raw.ValueKind != JsonValueKind.Array || raw.GetArrayLength() == 0)
            throw Bad("缺字段");
        var g = gripE.EnumerateArray().Select(x => x.TryGetInt32(out var n) ? n : -1).ToArray();
        var stages = new List<(string[][], string[][])>();
        var cols = 0;
        foreach (var st in raw.EnumerateArray())
        {
            var small = Frames(st, "small"); var big = Frames(st, "big");
            if (small is null || big is null || small.Length != 8 || big.Length != 8 || small[0].Length == 0) throw Bad("每阶段要 8+8 帧");
            var tall = small[0].Length;
            if (cols == 0) cols = small[0][0].Length;
            foreach (var f in small.Concat(big))
                if (f.Length != tall || f.Any(r => r.Length != cols)) throw Bad("帧尺寸不一致");
            stages.Add((small, big));
        }
        if (cols <= 0 || g[0] < 0 || g[0] >= cols || g[1] < 0 || g[1] >= stages[0].Item1[0].Length) throw Bad("拎点越界");
        return new ScruffSheet(Palettes.Parse(pal, "拎起帧"), cols, (g[0], g[1]), headMid, stages);
    }

    static string[][]? Frames(JsonElement stage, string key)
    {
        if (!stage.TryGetProperty(key, out var e) || e.ValueKind != JsonValueKind.Array) return null;
        var r = new List<string[]>();
        foreach (var f in e.EnumerateArray())
        {
            if (Json.Strings(f) is not { } rows) return null;
            r.Add(rows);
        }
        return r.ToArray();
    }

    public string[] Rows(LiftPose p)
    {
        var st = stages[Math.Min(Math.Max(p.Stage, 0), stages.Count - 1)];
        var fs = p.Big ? st.Big : st.Small;
        return fs[Math.Min(Math.Max(p.Index, 0), fs.Length - 1)];
    }

    public byte[] Rgba(string[] rows) => Palettes.Pixels(rows, Cols, palette);
}
