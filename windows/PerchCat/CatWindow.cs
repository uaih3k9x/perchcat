using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PerchCat.Core;

namespace PerchCat;

internal enum MouseKind { Down, Drag, Up }

/// 一扇小的无边框置顶透明窗口, 大小贴着猫图 (加抖毛留白和气泡), 每帧挪位置和尺寸.
/// 位置和尺寸一律用 SetWindowPos 按物理像素设, 不走 WPF 的 Left/Top/Width/Height (那些是按当前屏 DPI 换算的, 混合 DPI 下会漂).
/// 内部图片的 DIP 尺寸 = 像素 / 当前 DPI 缩放, 保证落在整数像素上, 最近邻缩放不糊.
internal sealed class CatWindow : Window
{
    readonly Canvas root = new();
    readonly Image sprite = new() { Stretch = Stretch.Fill, RenderTransformOrigin = new Point(0.5, 0.5), SnapsToDevicePixels = true };
    readonly Border bubble;
    readonly TextBlock bubbleText;
    IntPtr hwnd;
    PixelRect last;
    DateTime lastTopmost = DateTime.MinValue;
    bool clickThrough;

    public event Action<MouseKind>? Mouse;

    public CatWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = null;                      // 没画东西的像素 alpha=0, 系统直接把点击放给下面的窗口
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        UseLayoutRounding = true;
        Left = -32000; Top = -32000; Width = 8; Height = 8;
        RenderOptions.SetBitmapScalingMode(sprite, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(root, EdgeMode.Aliased);

        bubbleText = new TextBlock
        {
            Foreground = Brushes.Black, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        bubble = new Border
        {
            Background = Brushes.White, BorderBrush = Brushes.Black, BorderThickness = new Thickness(1),
            Child = bubbleText, IsHitTestVisible = false, Visibility = Visibility.Collapsed,
        };
        root.Children.Add(sprite);
        root.Children.Add(bubble);
        Content = root;

        sprite.MouseLeftButtonDown += (_, e) => { sprite.CaptureMouse(); e.Handled = true; Mouse?.Invoke(MouseKind.Down); };
        sprite.MouseMove += (_, _) => { if (sprite.IsMouseCaptured) Mouse?.Invoke(MouseKind.Drag); };
        sprite.MouseLeftButtonUp += (_, e) => { sprite.ReleaseMouseCapture(); e.Handled = true; Mouse?.Invoke(MouseKind.Up); };
    }

    /// 创建 HWND 并设成不抢焦点、不进 Alt-Tab 的工具窗口.
    public void Prepare()
    {
        hwnd = new WindowInteropHelper(this).EnsureHandle();
        Native.SetExStyle(hwnd, Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW, 0);
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    IntPtr WndProc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == Native.WM_MOUSEACTIVATE) { handled = true; return new IntPtr(Native.MA_NOACTIVATE); }
        // 猫的位置完全由我们按像素管: 不让 WPF 在跨屏 DPI 变化时自作主张改窗口大小
        if (msg == Native.WM_DPICHANGED) { handled = true; return IntPtr.Zero; }
        return IntPtr.Zero;
    }

    /// 光标不在猫身上 (含气泡、留白) 时让点击整个穿透; 在猫身上或正按着时收鼠标.
    public void SetClickThrough(bool on)
    {
        if (on == clickThrough || hwnd == IntPtr.Zero) return;
        clickThrough = on;
        Native.SetExStyle(hwnd, on ? Native.WS_EX_TRANSPARENT : 0, on ? 0 : Native.WS_EX_TRANSPARENT);
    }

    public void Present(PixelRect win, PixelRect spriteRect, BitmapSource image, bool flip,
                        string? bubbleLabel, PixelRect bubbleRect, double bubbleFontPx)
    {
        if (hwnd == IntPtr.Zero) Prepare();
        var dpi = VisualTreeHelper.GetDpi(this);
        double sx = dpi.DpiScaleX, sy = dpi.DpiScaleY;

        sprite.Source = image;
        sprite.Width = spriteRect.Width / sx; sprite.Height = spriteRect.Height / sy;
        Canvas.SetLeft(sprite, (spriteRect.Left - win.Left) / sx);
        Canvas.SetTop(sprite, (spriteRect.Top - win.Top) / sy);
        sprite.RenderTransform = flip ? new ScaleTransform(-1, 1) : Transform.Identity;

        if (bubbleLabel is null) bubble.Visibility = Visibility.Collapsed;
        else
        {
            bubbleText.Text = bubbleLabel;
            bubbleText.FontSize = bubbleFontPx / sx;
            bubble.Width = bubbleRect.Width / sx; bubble.Height = bubbleRect.Height / sy;
            bubble.CornerRadius = new CornerRadius(8 * bubbleRect.Height / 24.0 / sx);
            Canvas.SetLeft(bubble, (bubbleRect.Left - win.Left) / sx);
            Canvas.SetTop(bubble, (bubbleRect.Top - win.Top) / sy);
            bubble.Visibility = Visibility.Visible;
        }

        // 先 Show 再摆位: 首次显示时 WPF 会套用构造里的 Left/Top, 摆在前面会被覆盖
        var wasVisible = IsVisible;
        if (!wasVisible) Show();
        var now = DateTime.UtcNow;
        var raise = (now - lastTopmost).TotalSeconds > 2;       // 偶尔重新顶到最上面, 平时不动 z 序
        if (win != last || raise || !wasVisible)
        {
            var flags = Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER | (raise || !wasVisible ? 0u : Native.SWP_NOZORDER);
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, win.Left, win.Top, win.Width, win.Height, flags);
            last = win;
            if (raise || !wasVisible) lastTopmost = now;
        }
    }

    public void HideCat()
    {
        if (IsVisible) Hide();
    }
}
