using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using FortniteCompanion.Core.Services;
using FortniteCompanion.Native;
using WinForms = System.Windows.Forms;

namespace FortniteCompanion.Overlay;

/// <summary>Overlay'de gösterilecek değerler.</summary>
public sealed class OverlayModel
{
    public int Total { get; init; }
    public string Source { get; init; } = "";
    public string Match { get; init; } = "—";
    public string Today { get; init; } = "0";
    public string Average { get; init; } = "0,0";
    public string Last { get; init; } = "—";
    public string Team { get; init; } = "—";
    public bool InMatch { get; init; }
    public string? Status { get; init; }
}

/// <summary>
/// Her zaman üstte, tıklamaları oyuna geçiren, odak almayan şeffaf panel. Yalnızca kendi penceresini çizer;
/// oyunun içine çizim yapmaz (DirectX hook yok). Bu yüzden Fortnite "Pencereli Tam Ekran" veya "Pencereli"
/// modda olmalıdır: özel (exclusive) tam ekran modunda Windows hiçbir pencereyi oyunun üstünde gösteremez.
/// </summary>
public partial class OverlayWindow : Window
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static readonly DependencyProperty AnimatedTotalProperty = DependencyProperty.Register(
        nameof(AnimatedTotal), typeof(double), typeof(OverlayWindow),
        new PropertyMetadata(0.0, (d, e) => ((OverlayWindow)d).TotalText.Text = ((int)Math.Round((double)e.NewValue)).ToString("N0", Tr)));

    private readonly AppSettings _settings;
    private readonly DispatcherTimer _topmostTimer;
    private IntPtr _hwnd;
    private int? _shownTotal;
    private string? _status;

    /// <summary>Kullanıcı overlay'i taşıdı veya boyutlandırdı (ayarlar kaydedilmeli).</summary>
    public event Action? LayoutChangedByUser;

    public double AnimatedTotal
    {
        get => (double)GetValue(AnimatedTotalProperty);
        set => SetValue(AnimatedTotalProperty, value);
    }

    public OverlayWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        ApplyAppearance();

        // Bazı pencereli tam ekran oyunlar kendini en üste iter; periyodik olarak yeniden en üste al (ucuz bir çağrı).
        _topmostTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _topmostTimer.Tick += (_, _) => KeepOnTop();

        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) => ApplyPosition();
        MouseLeftButtonDown += OnMouseDown;
        MouseWheel += OnMouseWheel;
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        Closed += (_, _) =>
        {
            _topmostTimer.Stop();
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        };
    }

    private void OnDisplayChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(new Action(ApplyPosition));

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetExStyle(_hwnd, NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_LAYERED, 0);
        SetLocked(_settings.OverlayLocked);
        _topmostTimer.Start();
        ApplyPosition();
    }

    private void KeepOnTop()
    {
        if (_hwnd != IntPtr.Zero && IsVisible)
        {
            NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
        }
    }

    public void ApplyAppearance()
    {
        var scale = Math.Clamp(_settings.OverlayScale, 0.6, 2.5);
        CardScale.ScaleX = scale;
        CardScale.ScaleY = scale;
        Opacity = Math.Clamp(_settings.OverlayOpacity, 0.25, 1.0);
        var team = _settings.OverlayShowTeamLine ? Visibility.Visible : Visibility.Collapsed;
        TeamLabel.Visibility = team;
        TeamText.Visibility = team;
        ApplyPosition();
    }

    /// <summary>Kilitliyken fare tıklamaları pencereden geçer (oyuna gider).</summary>
    public void SetLocked(bool locked)
    {
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.SetExStyle(_hwnd,
                add: locked ? NativeMethods.WS_EX_TRANSPARENT : 0,
                remove: locked ? 0 : NativeMethods.WS_EX_TRANSPARENT);
        }
        Card.BorderThickness = new Thickness(locked ? 1 : 2);
        Card.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, locked ? "OverlayBorder" : "AccentBrush");
        Cursor = locked ? null : Cursors.SizeAll;
        RefreshStatus();
    }

    // ------------------------------------------------------------------ konum

    private WinForms.Screen TargetScreen()
    {
        var screens = WinForms.Screen.AllScreens;
        return screens.FirstOrDefault(s => s.DeviceName == _settings.OverlayScreen) ?? WinForms.Screen.PrimaryScreen ?? screens[0];
    }

    /// <summary>Hedef ekranın sınırları (DIP). Oyun tam ekranı kapladığı için görev çubuğu alanı hariç tutulmaz.</summary>
    private Rect ScreenRectDip(WinForms.Screen screen)
    {
        var b = screen.Bounds;
        var source = PresentationSource.FromVisual(this);
        var m = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var tl = m.Transform(new Point(b.Left, b.Top));
        var br = m.Transform(new Point(b.Right, b.Bottom));
        return new Rect(tl, br);
    }

    /// <summary>Köşe + oransal ofsetten konum hesaplar; çözünürlük değişse de aynı yerde kalır.</summary>
    public void ApplyPosition()
    {
        if (!IsLoaded && _hwnd == IntPtr.Zero)
        {
            return;
        }
        var area = ScreenRectDip(TargetScreen());
        var w = ActualWidth > 0 ? ActualWidth : 240;
        var h = ActualHeight > 0 ? ActualHeight : 200;
        var ox = Math.Clamp(_settings.OverlayOffsetX, 0, 0.95) * area.Width;
        var oy = Math.Clamp(_settings.OverlayOffsetY, 0, 0.95) * area.Height;
        var leftSide = _settings.OverlayCorner is OverlayCorner.TopLeft or OverlayCorner.BottomLeft;
        var topSide = _settings.OverlayCorner is OverlayCorner.TopLeft or OverlayCorner.TopRight;
        var left = leftSide ? area.Left + ox : area.Right - w - ox;
        var top = topSide ? area.Top + oy : area.Bottom - h - oy;
        Left = Math.Clamp(left, area.Left, Math.Max(area.Left, area.Right - w));
        Top = Math.Clamp(top, area.Top, Math.Max(area.Top, area.Bottom - h));
    }

    private void StoreOffsetsFromPosition()
    {
        // Pencerenin bulunduğu ekranı ve köşeye göre oransal ofseti kaydet.
        var center = new System.Drawing.Point((int)(Left + ActualWidth / 2), (int)(Top + ActualHeight / 2));
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
        {
            var dev = source.CompositionTarget.TransformToDevice.Transform(new Point(Left + ActualWidth / 2, Top + ActualHeight / 2));
            center = new System.Drawing.Point((int)dev.X, (int)dev.Y);
        }
        var screen = WinForms.Screen.FromPoint(center);
        _settings.OverlayScreen = screen.DeviceName;
        var area = ScreenRectDip(screen);

        // En yakın köşeyi seç
        var leftSide = Left + ActualWidth / 2 < area.Left + area.Width / 2;
        var topSide = Top + ActualHeight / 2 < area.Top + area.Height / 2;
        _settings.OverlayCorner = (leftSide, topSide) switch
        {
            (true, true) => OverlayCorner.TopLeft,
            (false, true) => OverlayCorner.TopRight,
            (true, false) => OverlayCorner.BottomLeft,
            _ => OverlayCorner.BottomRight,
        };
        var ox = leftSide ? Left - area.Left : area.Right - (Left + ActualWidth);
        var oy = topSide ? Top - area.Top : area.Bottom - (Top + ActualHeight);
        _settings.OverlayOffsetX = Math.Max(0, ox / area.Width);
        _settings.OverlayOffsetY = Math.Max(0, oy / area.Height);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_settings.OverlayLocked || e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }
        DragMove();
        StoreOffsetsFromPosition();
        LayoutChangedByUser?.Invoke();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_settings.OverlayLocked || (Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }
        _settings.OverlayScale = Math.Clamp(_settings.OverlayScale + (e.Delta > 0 ? 0.05 : -0.05), 0.6, 2.5);
        ApplyAppearance();
        LayoutChangedByUser?.Invoke();
    }

    // ------------------------------------------------------------------ içerik

    public void Display(OverlayModel m)
    {
        var previous = _shownTotal ?? m.Total;
        var increase = _shownTotal.HasValue && m.Total > previous ? m.Total - previous : 0;
        if (increase > 0 && _settings.OverlayAnimations)
        {
            var anim = new DoubleAnimation(previous, m.Total, TimeSpan.FromMilliseconds(900))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            BeginAnimation(AnimatedTotalProperty, anim);
            Celebrate(increase);
        }
        else
        {
            BeginAnimation(AnimatedTotalProperty, null);
            AnimatedTotal = m.Total;
        }
        _shownTotal = m.Total;

        SourceText.Text = m.Source;
        MatchText.Text = m.Match;
        TodayText.Text = m.Today;
        AvgText.Text = m.Average;
        LastText.Text = m.Last;
        TeamText.Text = m.Team;
        LiveDot.Visibility = m.InMatch ? Visibility.Visible : Visibility.Collapsed;
        LiveText.Visibility = LiveDot.Visibility;
        _status = m.Status;
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        var text = _settings.OverlayLocked
            ? _status
            : "Kilit açık: sürükleyerek taşıyın · Ctrl+tekerlek ile boyut · kilitlemek için " + _settings.HotkeyToggleLock;
        StatusText.Text = text ?? "";
        StatusText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Sayıda küçük "pop" ve kaybolan "+N" (yalnızca değer değişince çalışır).</summary>
    private void Celebrate(int increase)
    {
        var pop = new DoubleAnimation(1.25, 1.0, TimeSpan.FromMilliseconds(450))
        {
            EasingFunction = new ElasticEase { Oscillations = 1, Springiness = 4, EasingMode = EasingMode.EaseOut },
        };
        TotalScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        TotalScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);

        PlusText.Text = "+" + increase.ToString(Tr);
        var fade = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(2.4) };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1700))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2400))));
        PlusText.BeginAnimation(OpacityProperty, fade);
        PlusShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, -6, TimeSpan.FromSeconds(2.4)));
    }
}
