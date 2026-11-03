namespace PerchCat.Core;

/// HKCU\Software\Microsoft\Windows\CurrentVersion\Run 的一个值. 真实现在 WPF 外壳里, 这里只定接口方便测试.
public interface IRunKeyStore
{
    string? Get(string name);
    void Set(string name, string value);
    void Delete(string name);
}

/// 开机自启: 只有用户在托盘菜单勾选才写, 取消就删. 勾选状态以注册表里有没有这个值为准.
public sealed class AutoStart
{
    public const string ValueName = "PerchCat";
    private readonly IRunKeyStore store;
    private readonly string exePath;

    public AutoStart(IRunKeyStore store, string exePath) { this.store = store; this.exePath = exePath; }

    public bool IsEnabled => store.Get(ValueName) is not null;

    /// 路径带引号, 含空格也能启动.
    public void Enable() => store.Set(ValueName, "\"" + exePath + "\"");

    public void Disable() => store.Delete(ValueName);
}
