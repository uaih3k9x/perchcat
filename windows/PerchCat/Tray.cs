using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using PerchCat.Core;

namespace PerchCat;

/// 托盘图标和菜单: 显示/藏起来、开机自启、退出. 图标从内置皮肤的站姿生成.
internal sealed class Tray : IDisposable
{
    readonly NotifyIcon icon;
    readonly Icon cat;
    readonly ToolStripMenuItem hideItem = new("藏起来");
    readonly ToolStripMenuItem autoItem = new("开机自启");
    readonly AutoStart autoStart;

    public Tray(Skin skin, AutoStart autoStart, Action toggleHidden, Action quit)
    {
        this.autoStart = autoStart;
        cat = MakeIcon(skin);
        var menu = new ContextMenuStrip();
        hideItem.Click += (_, _) => toggleHidden();
        autoItem.Click += (_, _) => ToggleAutoStart();
        menu.Items.Add(hideItem);
        menu.Items.Add(autoItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => quit()));
        // 每次打开菜单都按注册表实际状态刷新勾选
        menu.Opening += (_, _) => autoItem.Checked = SafeIsEnabled();
        icon = new NotifyIcon { Icon = cat, Text = "perchcat", ContextMenuStrip = menu, Visible = true };
        icon.DoubleClick += (_, _) => toggleHidden();
    }

    public bool Hidden { set => hideItem.Text = value ? "显示" : "藏起来"; }

    bool SafeIsEnabled()
    {
        try { return autoStart.IsEnabled; } catch { return false; }
    }

    void ToggleAutoStart()
    {
        try
        {
            if (autoStart.IsEnabled) autoStart.Disable(); else autoStart.Enable();
        }
        catch (Exception e)
        {
            MessageBox.Show("开机自启设置失败: " + e.Message, "perchcat");
        }
    }

    /// 把 stand 姿势裁到有颜色的范围, 整数倍最近邻放大到托盘图标大小.
    static Icon MakeIcon(Skin skin)
    {
        var rows = skin.Resolve("stand");
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (var y = 0; y < rows.Length; y++)
            for (var x = 0; x < rows[y].Length; x++)
                if (rows[y][x] != '.') { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
        var size = Math.Max(16, SystemInformation.SmallIconSize.Width);
        var src = new Bitmap(maxX - minX + 1, maxY - minY + 1, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var rgba = skin.Rgba(rows);
        for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
            {
                var i = (y * skin.Width + x) * 4;
                src.SetPixel(x - minX, y - minY, Color.FromArgb(rgba[i + 3], rgba[i], rgba[i + 1], rgba[i + 2]));
            }
        var factor = Math.Max(1, Math.Min(size / src.Width, size / src.Height));
        using var canvas = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(canvas))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            var w = src.Width * factor; var h = src.Height * factor;
            g.DrawImage(src, new Rectangle((size - w) / 2, size - h, w, h));
        }
        var hIcon = canvas.GetHicon();
        try { return (Icon)Icon.FromHandle(hIcon).Clone(); }
        finally { Native.DestroyIcon(hIcon); }
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
        cat.Dispose();
    }
}
