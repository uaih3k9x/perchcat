using System.Reflection;

namespace PerchCat.Core;

/// 内置橘猫. 三份 JSON 嵌在程序集里 (Assets/*.json, 由 scripts/extract_assets.py 生成); 不读任何外部文件.
public static class BuiltinAssets
{
    public static byte[] SkinJson() => Read("skin.json");
    public static byte[] ScruffJson() => Read("scruff.json");
    public static byte[] ShakeJson() => Read("shake.json");

    public static Skin Skin() => PerchCat.Core.Skin.Parse(SkinJson(), "内置皮肤");
    public static ScruffSheet Scruff() => ScruffSheet.Parse(ScruffJson());
    public static ShakeSheet Shake() => ShakeSheet.Parse(ShakeJson());

    static byte[] Read(string name)
    {
        using var s = typeof(BuiltinAssets).Assembly.GetManifestResourceStream($"PerchCat.Core.Assets.{name}")
            ?? throw new InvalidOperationException($"缺少内置资源 {name}");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
