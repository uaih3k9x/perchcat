using Microsoft.Win32;
using PerchCat.Core;

namespace PerchCat;

/// HKCU\Software\Microsoft\Windows\CurrentVersion\Run. 只有 AutoStart.Enable 才写, Disable 删.
internal sealed class RegistryRunKey : IRunKeyStore
{
    const string Path = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? Get(string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(Path);
        return k?.GetValue(name) as string;
    }

    public void Set(string name, string value)
    {
        using var k = Registry.CurrentUser.CreateSubKey(Path);
        k.SetValue(name, value, RegistryValueKind.String);
    }

    public void Delete(string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(Path, writable: true);
        k?.DeleteValue(name, throwOnMissingValue: false);
    }
}
