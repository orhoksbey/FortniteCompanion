using System;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;
using FortniteCompanion.Native;

namespace FortniteCompanion;

/// <summary>Sistem tepsisi simgesi ve menüsü (Windows Forms NotifyIcon).</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly Drawing.Icon _image;
    private readonly WinForms.ToolStripMenuItem _overlayItem;
    private readonly WinForms.ToolStripMenuItem _lockItem;
    private readonly WinForms.ToolStripMenuItem _liveMenu;
    private readonly WinForms.ToolStripMenuItem _headerItem;

    public event Action? OpenRequested;
    public event Action? ToggleOverlayRequested;
    public event Action? ToggleLockRequested;
    public event Action<int>? ManualRequested;
    public event Action? SaveLiveRequested;
    public event Action? ScanRequested;
    public event Action? ExitRequested;

    public TrayIcon()
    {
        _image = CreateChestIcon();
        var menu = new WinForms.ContextMenuStrip();

        _headerItem = new WinForms.ToolStripMenuItem("Fortnite Companion") { Enabled = false };
        var open = new WinForms.ToolStripMenuItem("Kontrol panelini aç", null, (_, _) => OpenRequested?.Invoke());
        open.Font = new Drawing.Font(open.Font, Drawing.FontStyle.Bold);
        _overlayItem = new WinForms.ToolStripMenuItem("Overlay'i göster", null, (_, _) => ToggleOverlayRequested?.Invoke());
        _lockItem = new WinForms.ToolStripMenuItem("Overlay kilitli (tıklamalar oyuna geçer)", null, (_, _) => ToggleLockRequested?.Invoke());

        _liveMenu = new WinForms.ToolStripMenuItem("Manuel canlı sayaç");
        _liveMenu.DropDownItems.Add(new WinForms.ToolStripMenuItem("+1 sandık", null, (_, _) => ManualRequested?.Invoke(+1)));
        _liveMenu.DropDownItems.Add(new WinForms.ToolStripMenuItem("−1 sandık", null, (_, _) => ManualRequested?.Invoke(-1)));
        _liveMenu.DropDownItems.Add(new WinForms.ToolStripSeparator());
        _liveMenu.DropDownItems.Add(new WinForms.ToolStripMenuItem("Replay gelmedi: manuel maç olarak kaydet", null, (_, _) => SaveLiveRequested?.Invoke()));

        menu.Items.Add(_headerItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(open);
        menu.Items.Add(_overlayItem);
        menu.Items.Add(_lockItem);
        menu.Items.Add(_liveMenu);
        menu.Items.Add(new WinForms.ToolStripMenuItem("Replay klasörünü şimdi tara", null, (_, _) => ScanRequested?.Invoke()));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(new WinForms.ToolStripMenuItem("Çıkış", null, (_, _) => ExitRequested?.Invoke()));

        _icon = new WinForms.NotifyIcon
        {
            Icon = _image,
            Text = "Fortnite Companion",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    public void Update(string header, bool overlayVisible, bool locked, bool liveMode)
    {
        _headerItem.Text = header;
        _overlayItem.Checked = overlayVisible;
        _lockItem.Checked = locked;
        _liveMenu.Visible = liveMode;
        var tip = header.Length > 63 ? header[..63] : header;
        _icon.Text = tip;
    }

    public void ShowNotification(string title, string text, bool warning = false)
    {
        _icon.ShowBalloonTip(6000, title, text, warning ? WinForms.ToolTipIcon.Warning : WinForms.ToolTipIcon.Info);
    }

    /// <summary>Sandık simgesini çalışma anında çizer (harici .ico dosyası gerekmez).</summary>
    private static Drawing.Icon CreateChestIcon()
    {
        using var bmp = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Drawing.Color.Transparent);
            using var lid = new Drawing.SolidBrush(Drawing.Color.FromArgb(0x9A, 0x5B, 0x2E));
            using var body = new Drawing.SolidBrush(Drawing.Color.FromArgb(0x7A, 0x43, 0x22));
            using var gold = new Drawing.SolidBrush(Drawing.Color.FromArgb(0xFF, 0xC8, 0x3D));
            using var latch = new Drawing.SolidBrush(Drawing.Color.FromArgb(0xFF, 0xE0, 0x7A));
            g.FillEllipse(lid, 3, 3, 26, 16);
            g.FillRectangle(lid, 3, 10, 26, 4);
            g.FillRectangle(body, 3, 14, 26, 14);
            g.FillRectangle(gold, 3, 12, 26, 3);
            g.FillRectangle(gold, 7, 4, 2, 24);
            g.FillRectangle(gold, 23, 4, 2, 24);
            g.FillRectangle(latch, 13, 10, 6, 8);
        }
        var handle = bmp.GetHicon();
        try
        {
            using var temp = Drawing.Icon.FromHandle(handle);
            return (Drawing.Icon)temp.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _image.Dispose();
    }
}
