using System.Text;
using System.Text.Json;
using PerchCat.Core;

namespace PerchCat.Core.Tests;

internal static class T
{
    public static byte[] NotJson() => Encoding.UTF8.GetBytes("not json");

    public static byte[] SheetJson(object[] stages, int[]? grip = null) =>
        JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["palette"] = new Dictionary<string, string> { ["K"] = "#000000" },
            ["grip"] = grip ?? new[] { 11, 3 },
            ["headMidCol"] = 12,
            ["stages"] = stages,
        });

    public static byte[] ShakeJson(int pad, int width, int height, string[] frames) =>
        JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["pad"] = pad, ["width"] = width, ["height"] = height, ["frames"] = frames,
        });

    /// 每帧 24 列, tall 行, 共 n 帧.
    public static string[][] Frames(int tall, int n = 8) =>
        Enumerable.Range(0, n).Select(_ => Enumerable.Repeat(new string('K', 24), tall).ToArray()).ToArray();

    public static object Stage(string name, string[][] small, string[][] big) =>
        new Dictionary<string, object> { ["name"] = name, ["small"] = small, ["big"] = big };
}
