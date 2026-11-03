using System.Text.Json;

namespace PerchCat.Core;

/// 抖毛帧: 带半透明拖影和括号线, 所以是整张 RGBA, 四周比猫多 pad 格.
public sealed class ShakeSheet
{
    public int Pad { get; }
    public int Width { get; }
    public int Height { get; }
    /// 预乘 alpha 的 RGBA.
    public IReadOnlyList<byte[]> Frames { get; }

    private ShakeSheet(int pad, int w, int h, IReadOnlyList<byte[]> frames) { Pad = pad; Width = w; Height = h; Frames = frames; }

    public static ShakeSheet Parse(byte[] data)
    {
        using var doc = Json.TryParse(data);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object) throw new SkinError("抖毛表不是合法 JSON");
        var o = doc.RootElement;
        if (!o.TryGetProperty("pad", out var padE) || !padE.TryGetInt32(out var pad)
            || !o.TryGetProperty("width", out var wE) || !wE.TryGetInt32(out var w)
            || !o.TryGetProperty("height", out var hE) || !hE.TryGetInt32(out var h)
            || !o.TryGetProperty("frames", out var fE) || Json.Strings(fE) is not { Length: > 0 } hexes)
            throw new SkinError("抖毛表不是合法 JSON");
        var frames = hexes.Select(hex =>
        {
            if (hex.Length != w * h * 8 || Bytes(hex) is not { } px) throw new SkinError("抖毛帧尺寸不对");
            for (var i = 0; i < px.Length; i += 4)
            {
                var a = px[i + 3];
                for (var c = 0; c < 3; c++) px[i + c] = (byte)(px[i + c] * a / 255);
            }
            return px;
        }).ToList();
        return new ShakeSheet(pad, w, h, frames);
    }

    /// "shake1" -> 1; 不是抖毛帧 -> null.
    public static int? IndexOf(string frame) =>
        frame.StartsWith("shake", StringComparison.Ordinal) && int.TryParse(frame.AsSpan(5), out var n) ? n : null;

    static byte[]? Bytes(string hex)
    {
        var out_ = new byte[hex.Length / 2];
        for (var i = 0; i < out_.Length; i++)
        {
            var hi = Uri.IsHexDigit(hex[2 * i]); var lo = Uri.IsHexDigit(hex[2 * i + 1]);
            if (!hi || !lo) return null;
            out_[i] = Convert.ToByte(hex.Substring(2 * i, 2), 16);
        }
        return out_;
    }
}
