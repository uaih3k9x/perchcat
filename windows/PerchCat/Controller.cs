using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PerchCat.Core;
using Geometry = PerchCat.Core.Geometry;

namespace PerchCat;

/// 把 Core 的猫接到 Windows 上: 定时器、桌面读取、鼠标、渲染. 对应 Swift 版 main.swift 的 Controller.
internal sealed class Controller : IDisposable
{
    const double Scale = 4;                         // 一格 = 4 点 (和 Swift 版一样); 点和像素的换算在 PixelSpace

    readonly Skin skin = BuiltinAssets.Skin();
    readonly ShakeSheet shake = BuiltinAssets.Shake();
    readonly ScruffSheet scruff = BuiltinAssets.Scruff();
    readonly int ownPid = Environment.ProcessId;
    readonly PixelSpace space;
    readonly CatBrain brain;
    readonly CatWindow window = new();
    readonly AutoStart autoStart;
    readonly Tray tray;
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Dictionary<string, BitmapSource> images = new();

    readonly DispatcherTimer timer = new();
    readonly DispatcherTimer mouseTimer = new() { Interval = TimeSpan.FromSeconds(0.1) };

    Platform[] platforms = Array.Empty<Platform>();
    List<WindowInfo> lastWindows = new();
    double lastRefresh = double.NegativeInfinity;
    double followUntil = double.NegativeInfinity;   // 脚下窗口刚动过: 这之前高频刷新, 拖窗口时跟手
    double lastTick, lastMouseCheck;
    bool slow, hidden;
    RubSensor rubSensor = new(Scale);
    PressGesture gesture = new();
    ThrowTracker tracker = new();
    int upMisses;                                   // 连续几次看到键已松开但还没收到松手事件

    double CatW => skin.Width * Scale;
    double CatH => skin.Height * Scale;
    double Now => clock.Elapsed.TotalSeconds;

    public Controller(System.Windows.Application app)
    {
        var monitors = DesktopReader.ReadMonitors(out var primary, out var dpi);
        if (monitors.Count == 0) throw new InvalidOperationException("读不到显示器");
        // 参考高度和格子大小在启动时按主屏定死, 之后换屏/换分辨率猫不会整体平移
        space = PixelSpace.ForDpi((int)dpi, primary.Height);
        var s0 = space.ToScreen(monitors.First(m => m.Frame == primary));
        brain = new CatBrain(Scale, s0.Frame.MidX, s0.Visible.MinY);
        autoStart = new AutoStart(new RegistryRunKey(), Environment.ProcessPath ?? "PerchCat.exe");
        tray = new Tray(skin, autoStart, ToggleHidden, app.Shutdown);

        window.Prepare();
        window.Mouse += OnMouse;
        RefreshPlatforms();
        lastTick = lastMouseCheck = Now;
        timer.Tick += (_, _) => Step();
        mouseTimer.Tick += (_, _) => CheckMouse();
        Schedule(false);
        mouseTimer.Start();
        Render();
    }

    public void Dispose()
    {
        timer.Stop(); mouseTimer.Stop();
        tray.Dispose();
        window.Close();
    }

    // 菜单

    void ToggleHidden()
    {
        hidden = !hidden;
        tray.Hidden = hidden;
        if (hidden)
        {
            timer.Stop(); mouseTimer.Stop();
            window.HideCat();
        }
        else
        {
            RefreshPlatforms();
            Schedule(false);
            lastMouseCheck = Now;
            mouseTimer.Start();
            Render();
        }
    }

    // 屏幕和平台

    void RefreshPlatforms()
    {
        lastRefresh = Now;
        var monitors = DesktopReader.ReadMonitors(out _, out _);     // 屏幕增减/挪动/改分辨率/任务栏换边: 每次都重读
        if (monitors.Count == 0) return;
        var screens = monitors.Select(space.ToScreen).ToArray();
        brain.Screens = screens.Select(s => s.Frame).ToArray();

        var ws = DesktopReader.ReadWindows().Where(w => WindowFilter.Accepts(w, ownPid)).Select(w => WindowFilter.ToInfo(w, space)).ToList();
        if (!brain.IsAirborne && !brain.IsSleeping && Geometry.SupportAt(platforms, brain.X, brain.Y)?.WindowId is { } id
            && Geometry.CarryDelta(id, lastWindows, ws) is { } d)
        {
            brain.Carry(d.Dx, d.Dy);
            followUntil = lastRefresh + 1;
        }
        lastWindows = ws;
        platforms = Geometry.BuildPlatforms(ws, ownPid, screens, CatH, Math.Max(100, CatW * 1.2));
    }

    // 时钟

    void Schedule(bool slowMode)
    {
        timer.Stop();
        slow = slowMode;
        timer.Interval = TimeSpan.FromSeconds(slowMode ? 0.5 : 0.125);     // 睡觉 2 fps, 其余 8 fps
        timer.Start();
        lastTick = Now;
    }

    void Step()
    {
        var now = Now;
        if (now - lastRefresh >= (now < followUntil ? 0.1 : 0.5)) RefreshPlatforms();
        var dt = now - lastTick;
        lastTick = now;
        brain.Tick(dt, platforms);
        Render();
        var wantSlow = brain.IsSleeping && !brain.IsPetted && now >= followUntil;
        if (wantSlow != slow) Schedule(wantSlow);
    }

    /// 点一下或按着蹭都算摸: 停手后 PetTuning.Release 秒自己结束.
    void Pat()
    {
        brain.Stroke();
        Step();
        if (slow) Schedule(false);
    }

    // 渲染

    (string Key, string[] Rows)? LiftLook
    {
        get
        {
            if (brain.LiftPose(scruff.StageCount) is not { } p) return null;
            return ($"scruff/{p.Stage}/{(p.Big ? "big" : "small")}/{p.Index}", scruff.Rows(p));
        }
    }

    BitmapSource Image(string name, string[]? liftRows)
    {
        if (images.TryGetValue(name, out var cached)) return cached;
        byte[] rgba; int w, h;
        if (liftRows is null && ShakeSheet.IndexOf(name) is { } n && n < shake.Frames.Count)
        {
            rgba = shake.Frames[n]; w = shake.Width; h = shake.Height;
        }
        else if (liftRows is not null)
        {
            rgba = scruff.Rgba(liftRows); w = scruff.Cols; h = liftRows.Length;
        }
        else
        {
            var rows = skin.Resolve(name);
            rgba = skin.Rgba(rows); w = skin.Width; h = rows.Length;
        }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, Bgra.FromRgba(rgba), w * 4);
        bmp.Freeze();
        images[name] = bmp;
        return bmp;
    }

    /// 猫在逻辑坐标里的矩形 (用于点击判定和绘制). 拎着时拎点对准鼠标, 帧高可能超过皮肤.
    Rect CatRect
    {
        get
        {
            if (brain.IsLifted)
            {
                var m = new Pt(brain.X, brain.Y);
                if (LiftLook is { } look) return Lift.LiftedRect(m, scruff.Grip, scruff.Cols, look.Rows.Length, Scale);
                return Lift.LiftedRect(m, LiftTuning.FallbackGrip, skin.Width, skin.Height, Scale, brain.Dir);
            }
            var x = Math.Round(brain.X / Scale, MidpointRounding.AwayFromZero) * Scale;
            return new Rect(x - CatW / 2, Math.Round(brain.Y, MidpointRounding.AwayFromZero) - skin.FootInset * Scale, CatW, CatH);
        }
    }

    void Render()
    {
        if (hidden) return;
        var look = LiftLook;
        var f = look?.Key ?? brain.Frame();
        var cat = CatRect;
        if (look is null && ShakeSheet.IndexOf(f) is not null)       // 拖影和括号线画在猫的格子外面
            cat = cat.InsetBy(-shake.Pad * Scale, -shake.Pad * Scale);
        var flip = look is null && brain.Dir < 0;                    // 拎起帧是正面的, 摆动方向也要和拖动对得上, 不镜像

        var spritePx = space.ToPixelsKeepingSize(cat);
        var win = spritePx;
        var bubbleRect = default(PixelRect);
        if (brain.Bubble is not null)
        {
            const double bw = 44, bh = 24;
            bubbleRect = space.ToPixelsKeepingSize(new Rect(cat.MidX - bw / 2, cat.MaxY + 2, bw, bh));
            win = new PixelRect(Math.Min(win.Left, bubbleRect.Left), Math.Min(win.Top, bubbleRect.Top),
                                Math.Max(win.Right, bubbleRect.Right), Math.Max(win.Bottom, bubbleRect.Bottom));
        }
        window.Present(win, spritePx, Image(f, look?.Rows), flip, brain.Bubble, bubbleRect, 14 * space.PixelsPerPoint);
    }

    // 鼠标: 按下在猫身上才收事件 (点一下/按着蹭是摸, 往上提是拎); 无按键蹭头靠轮询位置

    Pt Cursor()
    {
        DesktopReader.TryCursor(out var x, out var y);
        return space.CursorToPoint(x, y);
    }

    void OnMouse(MouseKind e)
    {
        var p = Cursor();
        switch (e)
        {
            case MouseKind.Down: gesture.Down(p); upMisses = 0; break;
            case MouseKind.Drag: Handle(gesture.Moved(p), p); break;
            case MouseKind.Up: Handle(gesture.Up(), p); break;
        }
    }

    void Handle(PressAction action, Pt p)
    {
        switch (action)
        {
            case PressAction.None: return;
            case PressAction.Click: Pat(); break;
            case PressAction.Rub:
                if (CatRect.Contains(p.X, p.Y)) Pat();
                break;
            case PressAction.Lift:
                tracker = new ThrowTracker();
                tracker.Add(p.X, p.Y, Now);
                brain.BeginLift(p.X, p.Y);
                if (slow) Schedule(false);
                Render();
                break;
            case PressAction.Move:
                tracker.Add(p.X, p.Y, Now);
                brain.LiftTo(p.X, p.Y);
                Render();
                break;
            case PressAction.Drop:
                brain.LiftTo(p.X, p.Y);
                Drop();
                Render();
                break;
        }
    }

    /// 松手: 头留在原地, 换成 fall 姿势往下掉.
    void Drop()
    {
        var r = CatRect;
        var at = Lift.DropPoint(r.MinX + scruff.HeadMidCol * Scale, r.MaxY, Scale, skin.Width, skin.Height, skin.FootInset, brain.Dir);
        var m = tracker.Velocity(Now);
        var v = Lift.ThrowVelocity(m.X, m.Y, Scale);
        brain.Release(at.X, at.Y, v.Vx, v.Vy);
    }

    void CheckMouse()
    {
        var p = Cursor();
        // 窗口重建/失去捕获时会丢拖动和松手: 这里按实际按键状态兜底, 猫不会粘在鼠标上
        if (gesture.IsPressed)
        {
            if (DesktopReader.PrimaryDown())
            {
                upMisses = 0;
                Handle(gesture.Moved(p), p);
            }
            else
            {
                upMisses++;                         // 第一次可能只是松手事件还在队列里
                if (upMisses >= 2) Handle(gesture.Cancel(), p);
            }
        }
        var cat = CatRect;
        var hit = gesture.IsPressed || cat.Contains(p.X, p.Y);
        window.SetClickThrough(!hit);

        var now = Now;
        var dt = now - lastMouseCheck;
        lastMouseCheck = now;
        // 摸头只算无按键的蹭动: 按着键划过 (拎猫、从别的程序拖东西) 都不算
        var free = !DesktopReader.AnyButtonDown() && !brain.IsLifted;
        var head = Petting.HeadRect(brain.Frame(), cat, Scale, brain.Dir);
        if (rubSensor.Sample(p, free && head.Contains(p.X, p.Y), dt)) brain.Stroke();
    }
}
