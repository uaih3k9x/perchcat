using System.Text;
using PerchCat.Core;
using Xunit;

namespace PerchCat.Core.Tests;

public class SkinTests
{
    [Fact]
    public void SkinParseAndResolve()
    {
        var json = "{\"palette\":{\"K\":\"#000000\",\"W\":\"#ffffff\"},\"poses\":{\"stand\":[\"KW\",\"..\",\"..\"],\"stand@half\":[\"WK\",\"..\",\"..\"]}}";
        var s = Skin.Parse(json);
        Assert.Equal(2, s.Width);
        Assert.Equal(2, s.FootInset);
        Assert.Equal("WK", s.Resolve("stand@half")[0]);
        Assert.Equal("KW", s.Resolve("walk3@closed")[0]);   // 缺失退回 stand
        Assert.Equal(new byte[] { 0, 0, 0, 255, 255, 255, 255, 255 }, s.Rgba(s.Resolve("stand")).Take(8).ToArray());
        Assert.Throws<SkinError>(() => Skin.Parse("{}"));
    }

    [Fact]
    public void SkinRejectsMismatchedPose()
    {
        Assert.Throws<SkinError>(() => Skin.Parse("{\"palette\":{\"K\":\"#000000\"},\"poses\":{\"stand\":[\"K.\"],\"sit\":[\"K\"]}}"));
        Assert.Throws<SkinError>(() => Skin.Parse("{\"palette\":{\"K\":\"#zzz\"},\"poses\":{\"stand\":[\"K.\"]}}"));
    }

    [Fact]
    public void SkinFallsBackToHappySit()
    {
        var skin = Skin.Parse("{\"palette\":{\"K\":\"#000000\"},\"poses\":{\"stand\":[\"K.\"],\"sit\":[\"K.\"],\"sit@happy\":[\".K\"]}}");
        Assert.Equal(new[] { ".K" }, skin.Resolve("shake1"));
        Assert.Equal(new[] { "K." }, skin.Resolve("nope"));
    }

    [Fact]
    public void BuiltinSkinIsTheOrangeCat()
    {
        var s = BuiltinAssets.Skin();
        Assert.Equal(24, s.Width);
        Assert.Equal(24, s.Height);
        foreach (var name in new[] { "stand", "sit", "fall", "walk0", "sleep0", "knead0", "puff0", "lick0", "sit@happy" })
            Assert.True(s.Poses.ContainsKey(name), name);
        Assert.True(s.FootInset >= 0);
    }
}

public class ShakeSheetTests
{
    [Fact]
    public void ParsesAndPremultipliesAlpha()
    {
        var s = ShakeSheet.Parse(T.ShakeJson(1, 2, 1, new[] { "ff00007f" + "000000ff", "00ff00ff" + "00000000" }));
        Assert.True(s.Pad == 1 && s.Width == 2 && s.Height == 1 && s.Frames.Count == 2);
        Assert.True(s.Frames[0][0] == 127 && s.Frames[0][3] == 127);    // 红 * alpha
        Assert.Equal(255, s.Frames[1][1]);
    }

    [Fact]
    public void RejectsWrongSize()
    {
        Assert.Throws<SkinError>(() => ShakeSheet.Parse(T.ShakeJson(1, 2, 1, new[] { "ff0000ff" })));
        Assert.Throws<SkinError>(() => ShakeSheet.Parse(T.NotJson()));
    }

    [Fact]
    public void FrameIndexFromName()
    {
        Assert.Equal(1, ShakeSheet.IndexOf("shake1"));
        Assert.Equal(0, ShakeSheet.IndexOf("shake0"));
        Assert.False(ShakeSheet.IndexOf("sit").HasValue);
    }

    [Fact]
    public void BuiltinShakeHasTwoFramesWithPadding()
    {
        var s = BuiltinAssets.Shake();
        Assert.Equal(2, s.Frames.Count);
        Assert.Equal(8, s.Pad);
        Assert.Equal(40, s.Width);
    }
}

public class ScruffSheetTests
{
    [Fact]
    public void ParsesVariableHeightStages()
    {
        var s = ScruffSheet.Parse(T.SheetJson(new[]
        {
            T.Stage("A", T.Frames(24), T.Frames(24)),
            T.Stage("D", T.Frames(34), T.Frames(34)),
        }));
        Assert.Equal(24, s.Cols);
        Assert.Equal(11, s.Grip.Col);
        Assert.Equal(3, s.Grip.Row);
        Assert.Equal(12, s.HeadMidCol);
        Assert.Equal(2, s.StageCount);
        var rows = s.Rows(new LiftPose(1, true, 7));
        Assert.Equal(34, rows.Length);
        Assert.Equal(24 * 34 * 4, s.Rgba(rows).Length);
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, s.Rgba(rows).Take(4).ToArray());
    }

    [Fact]
    public void RejectsBrokenSheets()
    {
        var bad = new[]
        {
            T.NotJson(),
            T.SheetJson(Array.Empty<object>()),
            T.SheetJson(new[] { T.Stage("A", T.Frames(24, 7), T.Frames(24)) }),     // 帧数不对
            T.SheetJson(new[] { T.Stage("A", T.Frames(24), T.Frames(25)) }),        // 同阶段高度不一
            T.SheetJson(new[] { T.Stage("A", new[] { new[] { new string('K', 23) } }.Concat(T.Frames(24, 7)).ToArray(), T.Frames(24)) }),
            T.SheetJson(new[] { T.Stage("A", T.Frames(24), T.Frames(24)) }, new[] { 99, 3 }),
        };
        foreach (var b in bad) Assert.Throws<SkinError>(() => ScruffSheet.Parse(b));
    }

    [Fact]
    public void BuiltinSheetGrowsTaller()
    {
        var s = BuiltinAssets.Scruff();
        var hs = Enumerable.Range(0, s.StageCount).Select(i => s.Rows(new LiftPose(i, false, 0)).Length).ToArray();
        Assert.Equal(24, hs.First());
        Assert.Equal(hs.OrderBy(x => x).ToArray(), hs);
        Assert.True(hs.Length >= 2);
        Assert.Equal(11, s.Grip.Col);
    }
}
