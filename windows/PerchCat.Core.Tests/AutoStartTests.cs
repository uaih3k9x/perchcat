using PerchCat.Core;
using Xunit;

namespace PerchCat.Core.Tests;

public class AutoStartTests
{
    sealed class FakeRunKey : IRunKeyStore
    {
        public Dictionary<string, string> Values { get; } = new();
        public string? Get(string name) => Values.TryGetValue(name, out var v) ? v : null;
        public void Set(string name, string value) => Values[name] = value;
        public void Delete(string name) => Values.Remove(name);
    }

    [Fact]
    public void NothingIsWrittenUntilEnabled()
    {
        var k = new FakeRunKey();
        var a = new AutoStart(k, @"C:\Apps\PerchCat.exe");
        Assert.False(a.IsEnabled);
        Assert.Empty(k.Values);
    }

    [Fact]
    public void EnableWritesQuotedPathAndDisableRemovesIt()
    {
        var k = new FakeRunKey();
        var a = new AutoStart(k, @"C:\Program Files\PerchCat\PerchCat.exe");
        a.Enable();
        Assert.True(a.IsEnabled);
        Assert.Equal("\"C:\\Program Files\\PerchCat\\PerchCat.exe\"", k.Values["PerchCat"]);
        a.Disable();
        Assert.False(a.IsEnabled);
        Assert.Empty(k.Values);
    }

    [Fact]
    public void DisableWhenNotEnabledIsHarmless()
    {
        var k = new FakeRunKey();
        new AutoStart(k, "x.exe").Disable();
        Assert.Empty(k.Values);
    }

    [Fact]
    public void EntryPointingElsewhereCountsAsEnabledButEnableRepoints()
    {
        // 程序挪过位置: 勾选状态以注册表里有没有为准, 重新勾选会改成当前路径
        var k = new FakeRunKey();
        k.Values["PerchCat"] = "\"D:\\old\\PerchCat.exe\"";
        var a = new AutoStart(k, @"E:\new\PerchCat.exe");
        Assert.True(a.IsEnabled);
        a.Enable();
        Assert.Equal("\"E:\\new\\PerchCat.exe\"", k.Values["PerchCat"]);
    }
}
