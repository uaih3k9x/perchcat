using System;
using System.Threading;
using PerchCat;

// 单实例: 再启动一次直接退出.
using var mutex = new Mutex(true, @"Local\PerchCat.SingleInstance", out var first);
if (!first) return;

var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
using var controller = new Controller(app);
app.Run();
