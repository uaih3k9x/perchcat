namespace PerchCat.Core;

public static class Bgra
{
    /// RGBA 字节序换成 BGRA (WPF 的 Pbgra32). 皮肤像素 alpha 只有 0/255, 抖毛帧已预乘, 所以只需换通道.
    public static byte[] FromRgba(byte[] rgba)
    {
        var o = new byte[rgba.Length];
        for (var i = 0; i + 3 < rgba.Length; i += 4)
        {
            o[i] = rgba[i + 2]; o[i + 1] = rgba[i + 1]; o[i + 2] = rgba[i]; o[i + 3] = rgba[i + 3];
        }
        return o;
    }
}
