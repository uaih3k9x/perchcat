using System;
using System.Threading;

namespace PerchCat;

static class Program
{
    [STAThread] // WPF 的 UI 线程必须是 STA, 否则一构造窗口就崩.
    static void Main()
    {
        // 单实例: 再启动一次直接退出.
        using var mutex = new Mutex(true, @"Local\PerchCat.SingleInstance", out var first);
        if (!first) return;

        var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        using var controller = new Controller(app);
        app.Run();
    }
}
