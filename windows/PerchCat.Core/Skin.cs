using System.Text;
using System.Text.Json;

namespace PerchCat.Core;

public sealed class SkinError : Exception
{
    public SkinError(string message) : base(message) { }
}

public readonly record struct Rgb(byte R, byte G, byte B);

/// 像素皮肤: 字符调色板 + 命名姿势 (每个姿势是等宽字符行, "." 透明).
public sealed class Skin
{
    public IReadOnlyDictionary<char, Rgb> Palette { get; }
    public IReadOnlyDictionary<string, string[]> Poses { get; }
    public int Width { get; }
    public int Height { get; }
    /// 脚底到图片底边隔了几行 (按 stand 算), 用来把脚对齐平台线.
    public int FootInset { get; }

    private Skin(IReadOnlyDictionary<char, Rgb> palette, IReadOnlyDictionary<string, string[]> poses, int w, int h, int footInset)
    { Palette = palette; Poses = poses; Width = w; Height = h; FootInset = footInset; }

    public static Skin Parse(string json) => Parse(Encoding.UTF8.GetBytes(json));

    public static Skin Parse(byte[] data, string source = "皮肤")
    {
        JsonElement pal, poseEl;
        using var doc = Json.TryParse(data);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("palette", out pal) || !doc.RootElement.TryGetProperty("poses", out poseEl)
            || poseEl.ValueKind != JsonValueKind.Object)
            throw new SkinError($"{source} 不是合法皮肤: 需要 {{\"palette\":{{...}},\"poses\":{{...}}}}");
        var palette = Palettes.Parse(pal, source);
        var poses = new Dictionary<string, string[]>();
        foreach (var p in poseEl.EnumerateObject())
            poses[p.Name] = Json.Strings(p.Value) ?? throw new SkinError($"{source} 姿势 {p.Name} 不是字符串数组");
        if (!poses.TryGetValue("stand", out var stand) || stand.Length == 0 || stand[0].Length == 0)
            throw new SkinError($"{source} 缺少 stand 姿势");
        var w = stand[0].Length;
        foreach (var (name, rows) in poses)
            if (rows.Length != stand.Length || rows.Any(r => r.Length != w))
                throw new SkinError($"{source} 姿势 {name} 尺寸与 stand 不一致");
        var lastInk = Array.FindLastIndex(stand, r => r.Any(c => c != '.'));
        if (lastInk < 0) lastInk = stand.Length - 1;
        return new Skin(palette, poses, w, stand.Length, stand.Length - 1 - lastInk);
    }

    /// "walk3@half" -> 有就用, 没有退回 "walk3", 再退回 stand.
    public string[] Resolve(string frame)
    {
        if (Poses.TryGetValue(frame, out var p)) return p;
        if (ShakeSheet.IndexOf(frame) is not null)
            return Poses.TryGetValue("sit@happy", out var sh) ? sh : Poses.TryGetValue("sit", out var s) ? s : Poses["stand"];
        var at = frame.IndexOf('@');
        if (at > 0 && Poses.TryGetValue(frame[..at], out var b)) return b;
        return Poses["stand"];
    }

    /// RGBA8 (不透明像素 alpha=255).
    public byte[] Rgba(string[] rows) => Palettes.Pixels(rows, Width, Palette);
}

internal static class Json
{
    public static JsonDocument? TryParse(byte[] data)
    {
        try { return JsonDocument.Parse(data); } catch (JsonException) { return null; }
    }

    public static string[]? Strings(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Array) return null;
        var r = new List<string>();
        foreach (var x in e.EnumerateArray())
        {
            if (x.ValueKind != JsonValueKind.String) return null;
            r.Add(x.GetString()!);
        }
        return r.ToArray();
    }
}

internal static class Palettes
{
    public static Dictionary<char, Rgb> Parse(JsonElement pal, string source)
    {
        var palette = new Dictionary<char, Rgb>();
        if (pal.ValueKind != JsonValueKind.Object) throw new SkinError($"{source} 调色板不是对象");
        foreach (var kv in pal.EnumerateObject())
        {
            var v = kv.Value.ValueKind == JsonValueKind.String ? kv.Value.GetString()! : "";
            if (kv.Name.Length != 1 || !TryHex(v, out var rgb))
                throw new SkinError($"{source} 调色板项无效: {kv.Name} = {v}");
            palette[kv.Name[0]] = rgb;
        }
        return palette;
    }

    static bool TryHex(string s, out Rgb rgb)
    {
        rgb = default;
        var t = s.StartsWith('#') ? s[1..] : s;
        if (t.Length != 6 || !uint.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out var v)) return false;
        rgb = new Rgb((byte)(v >> 16 & 255), (byte)(v >> 8 & 255), (byte)(v & 255));
        return true;
    }

    public static byte[] Pixels(string[] rows, int width, IReadOnlyDictionary<char, Rgb> palette)
    {
        var buf = new byte[width * rows.Length * 4];
        for (var y = 0; y < rows.Length; y++)
            for (var x = 0; x < rows[y].Length; x++)
            {
                if (!palette.TryGetValue(rows[y][x], out var c)) continue;
                var i = (y * width + x) * 4;
                buf[i] = c.R; buf[i + 1] = c.G; buf[i + 2] = c.B; buf[i + 3] = 255;
            }
        return buf;
    }
}
