using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using FortniteCompanion.Core.Data;
using FortniteCompanion.Core.Services;
using FortniteCompanion.Native;
using FortniteCompanion.Overlay;
using FortniteCompanion.ViewModels;

namespace FortniteCompanion;

public partial class App : Application
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "FortniteCompanion";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private Database? _db;
    private CompanionService? _service;
    private MainViewModel? _vm;
    private MainWindow? _main;
    private OverlayWindow? _overlay;
    private TrayIcon? _tray;
    private HotkeyManager? _hotkeys;
    private FortniteProcessMonitor? _fortnite;
    private DispatcherTimer? _afterGameTimer;
    private bool _fortniteRunning;
    private bool _forceOverlay;
    private int _replaysAtGameStart = -1;
    private bool _exiting;
    private DispatcherTimer? _updateTimer;
    private string? _pendingUpdateFile;
    private ReleaseInfo? _pendingRelease;
    private bool _updating;

    public static new App Current => (App)Application.Current;

    public CompanionService Service => _service!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Güncellemeden sonra açılıyorsak eski sürümün tamamen kapanmasını bekle (tek örnek kilidi, kısayollar).
        AutoUpdater.WaitForPreviousInstance(e.Args);
        var afterUpdate = e.Args.Contains(AutoUpdater.AfterUpdateArg);

        // Tek örnek: ikinci kez açılırsa mevcut pencere öne getirilir.
        _mutex = new Mutex(true, @"Local\FortniteCompanion.Single", out var first);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\FortniteCompanion.Show");
        if (!first)
        {
            _showEvent.Set();
            Shutdown();
            return;
        }
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => Dispatcher.BeginInvoke(new Action(ShowMain)), null, Timeout.Infinite, false);

        AppLog.Folder = AppPaths.LogFolder;
        AppLog.Prune();
        AppLog.Info("Fortnite Companion başlatılıyor, sürüm " + AutoUpdater.CurrentVersion);
        if (Environment.ProcessPath is { } exePath)
        {
            AutoUpdater.Cleanup(exePath);
        }

        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Error("Beklenmeyen hata (arayüz)", args.Exception);
            MessageBox.Show("Beklenmeyen bir hata oluştu ve hata günlüğüne yazıldı:\n\n" + args.Exception.Message,
                "Fortnite Companion", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => AppLog.Error("Ölümcül hata", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Error("Gözlemlenmemiş görev hatası", args.Exception);
            args.SetObserved();
        };

        var firstRun = !File.Exists(AppPaths.SettingsFile);
        var settings = AppSettings.Load(AppPaths.SettingsFile);

        try
        {
            _db = Database.OpenFile(AppPaths.DatabaseFile);
            _service = new CompanionService(_db, settings, AppPaths.SettingsFile);
        }
        catch (Exception ex)
        {
            AppLog.Error("Veri tabanı açılamadı", ex);
            MessageBox.Show("Veri tabanı açılamadı:\n" + ex.Message + "\n\nKonum: " + AppPaths.DatabaseFile,
                "Fortnite Companion", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        ApplyTheme(settings.Theme, settings.Accent);
        _vm = new MainViewModel(_service);

        _overlay = new OverlayWindow(settings);
        _overlay.LayoutChangedByUser += () => _service.SaveSettings();

        _tray = new TrayIcon();
        _tray.OpenRequested += () => Dispatcher.BeginInvoke(new Action(ShowMain));
        _tray.ToggleOverlayRequested += () => Dispatcher.BeginInvoke(new Action(ToggleOverlay));
        _tray.ToggleLockRequested += () => Dispatcher.BeginInvoke(new Action(ToggleLock));
        _tray.ManualRequested += d => Dispatcher.BeginInvoke(new Action(() => Manual(d)));
        _tray.SaveLiveRequested += () => Dispatcher.BeginInvoke(new Action(() => _service.SaveLiveAsManual("Kullanıcı: replay gelmedi, manuel kaydedildi")));
        _tray.ScanRequested += () => _ = _service.Watcher.ScanAsync(ignoreQuietPeriod: true);
        _tray.ExitRequested += () => Dispatcher.BeginInvoke(new Action(ExitApp));

        _hotkeys = new HotkeyManager();
        RegisterHotkeys(showErrors: false);

        _service.SnapshotChanged += s => Dispatcher.BeginInvoke(new Action(() => OnSnapshot(s)));
        _service.Notify += (kind, title, text) => Dispatcher.BeginInvoke(new Action(() => OnNotify(kind, title, text)));

        _fortnite = new FortniteProcessMonitor();
        _fortnite.RunningChanged += running => Dispatcher.BeginInvoke(new Action(() => OnFortniteChanged(running)));

        _service.Start();
        UpdateOverlayVisibility();
        SyncStartupRegistry(settings.StartWithWindows);

        if (settings.AutoBackup)
        {
            var folder = string.IsNullOrWhiteSpace(settings.BackupFolder) ? AppPaths.DefaultBackupFolder : settings.BackupFolder!;
            Task.Run(() =>
            {
                try { BackupService.RunIfDue(_db, folder, settings.BackupKeepCount); }
                catch (Exception ex) { AppLog.Error("Otomatik yedek alınamadı", ex); }
            });
        }

        // Otomatik güncelleme: açılıştan 30 sn sonra, sonra 6 saatte bir.
        // Güncelleme denetimi: açılıştan 8 sn sonra, sonra 30 dakikada bir.
        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _updateTimer.Tick += (_, _) =>
        {
            _updateTimer!.Interval = TimeSpan.FromMinutes(30);
            if (_service.Settings.AutoUpdate)
            {
                _ = CheckForUpdateAsync(manual: false);
            }
        };
        _updateTimer.Start();
        if (afterUpdate)
        {
            _tray.ShowNotification("Fortnite Companion güncellendi", $"Sürüm {AutoUpdater.CurrentVersion.ToString(3)} kuruldu.");
        }

        var minimized = e.Args.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase))
                        || (settings.StartMinimized && !firstRun);
        if (!settings.OnboardingDone)
        {
            // İlk açılış (veya bu özellikten önceki sürümden güncelleme): "Sen kimsin?"
            ShowMain();
            Dispatcher.BeginInvoke(new Action(ShowOnboarding), DispatcherPriority.Loaded);
        }
        else if (!minimized)
        {
            ShowMain();
        }
    }

    /// <summary>"Sen kimsin?" kurulum penceresi.</summary>
    public void ShowOnboarding()
    {
        if (_service == null || _exiting)
        {
            return;
        }
        ShowMain();
        var w = new Views.OnboardingWindow(_service) { Owner = _main };
        w.ShowDialog();
        _service.Refresh();
    }

    // ------------------------------------------------------------------ pencereler

    public void ShowMain()
    {
        if (_vm == null || _exiting)
        {
            return;
        }
        if (_main == null)
        {
            _main = new MainWindow(_vm);
            _main.Closing += (_, args) =>
            {
                if (!_exiting)
                {
                    // Kapatınca tepsiye küçült: sayım arka planda devam eder.
                    args.Cancel = true;
                    _main.Hide();
                }
            };
        }
        _main.Show();
        if (_main.WindowState == WindowState.Minimized)
        {
            _main.WindowState = WindowState.Normal;
        }
        _main.Activate();
    }

    private void OnSnapshot(CompanionSnapshot s)
    {
        if (_exiting)
        {
            return;
        }
        _vm?.Apply(s);
        _overlay?.Display(BuildOverlay(s));
        var settings = _service!.Settings;
        _tray?.Update(_vm?.CurrentSelfHeader() ?? "Fortnite Companion", _overlay?.IsVisible == true, settings.OverlayLocked, settings.ManualLiveMode);
    }

    private OverlayModel BuildOverlay(CompanionSnapshot s)
    {
        var settings = _service!.Settings;
        var self = s.Self;
        string match;
        if (settings.ManualLiveMode && s.Live != null)
        {
            match = s.Live.ManualCount.ToString(Fmt.Tr) + " (manuel)";
        }
        else if (s.InMatch)
        {
            match = settings.ManualLiveMode ? "0 (manuel)" : "maç sonunda";
        }
        else
        {
            match = "—";
        }

        string? status = null;
        if (s.Watcher is { FolderExists: false })
        {
            status = "Replay klasörü bulunamadı — Ayarlar'ı kontrol edin";
        }
        else if (s.InMatch && !settings.ManualLiveMode)
        {
            status = "Sandıklar maç bitince replay'den eklenecek";
        }
        else if (settings.ManualLiveMode)
        {
            status = $"Manuel mod: {settings.HotkeyManualIncrement} / {settings.HotkeyManualDecrement}";
        }

        var source = self == null ? "" :
            self.PartialChests > 0 || self.ManualChests > 0
                ? "geçmiş + replay + tahmini/manuel"
                : self.TrackedChests > 0 ? "geçmiş + replay ile doğrulanmış" : "önceki istatistikten aktarıldı";

        return new OverlayModel
        {
            Total = self?.TotalChests ?? 0,
            Source = source,
            Match = match,
            Today = Fmt.N(self?.Today ?? 0),
            Average = Fmt.D1(self?.ChestsPerMatch ?? 0),
            Last = self?.LastMatch is { } l ? "+" + Fmt.N(l.Chests) : "—",
            Team = s.LastMatchTeamChests is int t ? Fmt.N(t) + " (kısmi)" : "—",
            InMatch = s.InMatch,
            Status = status,
        };
    }

    private void OnNotify(NotificationKind kind, string title, string text)
    {
        var settings = _service!.Settings;
        switch (kind)
        {
            case NotificationKind.MatchEnd when !settings.NotifyMatchEnd:
            case NotificationKind.Record when !settings.NotifyRecord:
                return;
            case NotificationKind.Warning:
                AppLog.Warn($"{title}: {text}");
                // Eski replay'lerin toplu taramasında bildirim yağmuru olmasın: yalnızca maç sırasında/sonrasında göster.
                if (!_fortniteRunning && _replaysAtGameStart < 0)
                {
                    return;
                }
                break;
        }
        _tray?.ShowNotification(title, text, kind == NotificationKind.Warning);
    }

    // ------------------------------------------------------------------ overlay

    public void ToggleOverlay()
    {
        var settings = _service!.Settings;
        var visibleNow = _overlay?.IsVisible == true;
        settings.OverlayVisible = !visibleNow;
        _forceOverlay = settings.OverlayVisible;
        _service.SaveSettings();
        UpdateOverlayVisibility();
    }

    public void ToggleLock()
    {
        var settings = _service!.Settings;
        settings.OverlayLocked = !settings.OverlayLocked;
        _service.SaveSettings();
        _overlay?.SetLocked(settings.OverlayLocked);
        if (!settings.OverlayLocked && _overlay?.IsVisible != true)
        {
            settings.OverlayVisible = true;
            _forceOverlay = true;
            UpdateOverlayVisibility();
        }
        OnSnapshot(_service.Snapshot);
    }

    private void UpdateOverlayVisibility()
    {
        if (_overlay == null || _service == null)
        {
            return;
        }
        var s = _service.Settings;
        var show = s.OverlayVisible && (!s.AutoOverlayWithFortnite || _fortniteRunning || _forceOverlay);
        if (show && !_overlay.IsVisible)
        {
            _overlay.Show();
            _overlay.ApplyPosition();
        }
        else if (!show && _overlay.IsVisible)
        {
            _overlay.Hide();
        }
        OnSnapshot(_service.Snapshot);
    }

    private void Manual(int delta)
    {
        if (_service == null)
        {
            return;
        }
        if (!_service.Settings.ManualLiveMode)
        {
            _tray?.ShowNotification("Manuel canlı mod kapalı", "Ayarlar → Sayım bölümünden \"Manuel canlı mod\"u açın.");
            return;
        }
        _service.ManualIncrement(delta);
    }

    private void OnFortniteChanged(bool running)
    {
        _fortniteRunning = running;
        if (running)
        {
            _forceOverlay = false;
            _replaysAtGameStart = _service?.Snapshot.Watcher?.ReplayFiles ?? 0;
            _afterGameTimer?.Stop();
        }
        else if (_replaysAtGameStart >= 0)
        {
            // Oyun kapandı: birkaç dakika içinde replay gelmezse manuel oturumu kaydet ve kullanıcıyı uyar.
            _afterGameTimer?.Stop();
            _afterGameTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(4) };
            _afterGameTimer.Tick += (_, _) =>
            {
                _afterGameTimer!.Stop();
                AfterGameCheck();
            };
            _afterGameTimer.Start();
        }
        UpdateOverlayVisibility();
    }

    private void AfterGameCheck()
    {
        if (_service == null)
        {
            return;
        }
        var nowFiles = _service.Snapshot.Watcher?.ReplayFiles ?? 0;
        if (_replaysAtGameStart >= 0 && nowFiles <= _replaysAtGameStart)
        {
            _tray?.ShowNotification("Yeni replay oluşmadı",
                "Fortnite'ta replay kaydı kapalı olabilir: Ayarlar → Oyun → Replays → \"Record Replays\" (Replay Kaydet) seçeneğini Açık yapın.", warning: true);
        }
        if (_service.SaveLiveAsManual("Fortnite kapandı ve replay gelmedi: manuel canlı sayaç kaydedildi") is { } session)
        {
            _tray?.ShowNotification("Manuel sayı kaydedildi", "Replay gelmediği için canlı manuel sayaç \"Manuel girildi\" olarak kaydedildi. Replay sonradan gelirse manuel kayıt otomatik olarak toplamdan çıkarılır.");
            AppLog.Info("Canlı oturum manuel maç olarak kaydedildi: " + session);
        }
        _replaysAtGameStart = -1;

        // Oyun kapandı ve maç sonu işleri bitti: bekleyen güncelleme varsa şimdi kur.
        TryInstallPendingUpdate(userInitiated: false);
    }

    // ------------------------------------------------------------------ ayarlar

    public bool RegisterHotkeys(bool showErrors)
    {
        if (_hotkeys == null || _service == null)
        {
            return false;
        }
        _hotkeys.UnregisterAll();
        var s = _service.Settings;
        var failed = new System.Collections.Generic.List<string>();
        if (!_hotkeys.Register(s.HotkeyToggleOverlay, ToggleOverlay)) failed.Add(s.HotkeyToggleOverlay);
        if (!_hotkeys.Register(s.HotkeyToggleLock, ToggleLock)) failed.Add(s.HotkeyToggleLock);
        if (!_hotkeys.Register(s.HotkeyManualIncrement, () => Manual(+1))) failed.Add(s.HotkeyManualIncrement);
        if (!_hotkeys.Register(s.HotkeyManualDecrement, () => Manual(-1))) failed.Add(s.HotkeyManualDecrement);
        if (failed.Count > 0)
        {
            var msg = "Şu kısayollar kaydedilemedi (geçersiz veya başka bir program kullanıyor): " + string.Join(", ", failed);
            AppLog.Warn(msg);
            if (showErrors)
            {
                MessageBox.Show(msg, "Kısayollar", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        return failed.Count == 0;
    }

    /// <summary>Ayarlar değiştiğinde çağrılır: tema, overlay, kısayollar, klasör, başlangıç kaydı.</summary>
    public void ApplySettings(bool hotkeysChanged, bool folderChanged)
    {
        if (_service == null)
        {
            return;
        }
        var s = _service.Settings;
        _service.SaveSettings();
        ApplyTheme(s.Theme, s.Accent);
        _overlay?.ApplyAppearance();
        _overlay?.SetLocked(s.OverlayLocked);
        if (hotkeysChanged)
        {
            RegisterHotkeys(showErrors: true);
        }
        if (folderChanged)
        {
            _service.Watcher.ChangeFolder(string.IsNullOrWhiteSpace(s.ReplayFolder) ? AppPaths.DefaultReplayFolder : s.ReplayFolder!);
        }
        SyncStartupRegistry(s.StartWithWindows);
        UpdateOverlayVisibility();
        _service.Refresh();
    }

    /// <summary>Kaydırıcılar sürüklenirken hafif önizleme (kaydetmez, veri tabanına dokunmaz).</summary>
    public void PreviewOverlayAppearance() => _overlay?.ApplyAppearance();

    public static void ApplyTheme(string theme, string accent)
    {
        var app = Current;
        var uri = new Uri(string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase)
            ? "pack://application:,,,/Themes/Light.xaml"
            : "pack://application:,,,/Themes/Dark.xaml", UriKind.Absolute);
        app.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = uri };

        var color = accent switch
        {
            "green" => Color.FromRgb(0x5C, 0xE0, 0x5A),
            "blue" => Color.FromRgb(0x4F, 0xC3, 0xFF),
            _ => Color.FromRgb(0xFF, 0xE1, 0x4D), // Fortnite sarısı
        };
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        app.Resources["AccentBrush"] = brush;
    }

    private static void SyncStartupRegistry(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled && Environment.ProcessPath is { } exe)
            {
                key.SetValue(RunValue, $"\"{exe}\" --minimized");
            }
            else if (key.GetValue(RunValue) != null)
            {
                key.DeleteValue(RunValue, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Otomatik başlatma ayarı uygulanamadı", ex);
        }
    }

    // ------------------------------------------------------------------ otomatik güncelleme

    private string? _notifiedTag;

    /// <summary>
    /// GitHub'daki son sürümü denetler. Daha yeniyse arka planda indirir ve üst menüde yeşil "GÜNCELLEME VAR"
    /// düğmesini gösterir. Kurulum kullanıcı tıklayınca (veya ayar açıksa oyun kapalıyken otomatik) yapılır.
    /// </summary>
    public async Task CheckForUpdateAsync(bool manual)
    {
        if (_service == null || _updating || _exiting)
        {
            return;
        }
        var repo = AutoUpdater.EffectiveRepo(_service.Settings);
        if (repo == null)
        {
            if (manual)
            {
                MessageBox.Show("Güncelleme deposu ayarlı değil. Ayarlar'da \"GitHub deposu\" alanına kullanıcı/depo yazın " +
                                "(GitHub'dan derlenen sürümlerde bu otomatik gelir).", "Güncelleme");
            }
            return;
        }

        _updating = true;
        try
        {
            var release = await AutoUpdater.GetLatestAsync(repo);
            if (release == null)
            {
                if (manual) MessageBox.Show($"Sürüm bilgisi alınamadı ({repo}). Depoda \"{AutoUpdater.AssetName}\" ekli bir sürüm olduğundan emin olun.", "Güncelleme");
                return;
            }
            if (!AutoUpdater.IsNewer(release))
            {
                _vm?.SetUpdateState(null, UpdateUiState.None);
                if (manual) MessageBox.Show($"Güncelsiniz. Kurulu: {AutoUpdater.CurrentVersion.ToString(3)} · GitHub'daki son sürüm: {release.Tag}", "Güncelleme");
                return;
            }

            var exe = Environment.ProcessPath;
            if (exe == null || !AutoUpdater.CanWriteNextTo(exe))
            {
                _pendingRelease = release; // düğme tıklanınca indirme sayfası açılır
                _vm?.SetUpdateState(release.Tag, UpdateUiState.ManualOnly);
                if (_notifiedTag != release.Tag)
                {
                    _notifiedTag = release.Tag;
                    _tray?.ShowNotification("Yeni sürüm var: " + release.Tag,
                        "Uygulamanın bulunduğu klasöre yazılamıyor; yeni sürümü elle indirin: " + (release.HtmlUrl ?? repo));
                }
                return;
            }

            if (_pendingRelease?.Version != release.Version || _pendingUpdateFile == null || !File.Exists(_pendingUpdateFile))
            {
                _vm?.SetUpdateState(release.Tag, UpdateUiState.Downloading);
                _pendingUpdateFile = await Task.Run(() => AutoUpdater.DownloadAsync(release, exe));
                _pendingRelease = release;
                AppLog.Info($"Güncelleme indirildi: {release.Tag}");
            }
            _vm?.SetUpdateState(release.Tag, UpdateUiState.Ready);

            if (_notifiedTag != release.Tag)
            {
                _notifiedTag = release.Tag;
                _tray?.ShowNotification("GÜNCELLEME VAR! " + release.Tag,
                    "Kurmak için uygulamanın üst menüsündeki yeşil GÜNCELLE düğmesine basın.");
            }

            if (manual)
            {
                InstallUpdateNow();
            }
            else if (_service.Settings.AutoInstallUpdates)
            {
                TryInstallPendingUpdate(userInitiated: false);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Güncelleme başarısız", ex);
            if (_pendingUpdateFile == null)
            {
                _vm?.SetUpdateState(null, UpdateUiState.None);
            }
            if (manual) MessageBox.Show("Güncelleme başarısız: " + ex.Message, "Güncelleme", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>Üst menüdeki yeşil "GÜNCELLE" düğmesi.</summary>
    public async void InstallUpdateNow()
    {
        if (_pendingUpdateFile == null || !File.Exists(_pendingUpdateFile))
        {
            if (!_updating)
            {
                await CheckForUpdateAsync(manual: false);
            }
            if (_pendingUpdateFile == null)
            {
                if (_pendingRelease?.HtmlUrl is { } url && _vm?.UpdateState == UpdateUiState.ManualOnly)
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                return;
            }
        }
        TryInstallPendingUpdate(userInitiated: true);
    }

    /// <summary>
    /// İndirilmiş güncellemeyi kurar. Otomatik kurulum oyun sırasında asla yapılmaz; kullanıcı tıkladıysa ve
    /// Fortnite açıksa önce onay istenir.
    /// </summary>
    private void TryInstallPendingUpdate(bool userInitiated)
    {
        if (_pendingUpdateFile == null || _pendingRelease == null || _service == null || _exiting || Environment.ProcessPath is not { } exe)
        {
            return;
        }
        if (!userInitiated && (!_service.Settings.AutoInstallUpdates || _fortniteRunning || _replaysAtGameStart >= 0))
        {
            return;
        }
        if (userInitiated && _fortniteRunning
            && MessageBox.Show($"{_pendingRelease.Tag} kurulacak. Fortnite açık: overlay birkaç saniyeliğine kapanıp yeniden açılacak. " +
                               "Maçın ortasındaysanız maç bitince güncellemeniz önerilir.\n\nŞimdi güncellensin mi?",
                    "Güncelleme", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _vm?.SetUpdateState(_pendingRelease.Tag, UpdateUiState.Installing);
        var minimized = _main == null || !_main.IsVisible;
        _service.SaveSettings();
        if (AutoUpdater.InstallAndRestart(exe, _pendingUpdateFile, minimized))
        {
            _pendingUpdateFile = null;
            ExitApp();
        }
        else
        {
            _vm?.SetUpdateState(_pendingRelease.Tag, UpdateUiState.Ready);
            _tray?.ShowNotification("Güncelleme kurulamadı", "Ayrıntılar hata günlüğünde. Mevcut sürüm çalışmaya devam ediyor.", warning: true);
        }
    }

    // ------------------------------------------------------------------ çıkış

    public void ExitApp()
    {
        if (_exiting)
        {
            return;
        }
        _exiting = true;
        try
        {
            _service?.SaveSettings();
            _afterGameTimer?.Stop();
            _updateTimer?.Stop();
            _fortnite?.Dispose();
            _hotkeys?.Dispose();
            _service?.Dispose();
            _overlay?.Close();
            _main?.Close();
            _tray?.Dispose();
            _db?.Dispose();
            _showWait?.Unregister(null);
        }
        catch (Exception ex)
        {
            AppLog.Error("Kapanış sırasında hata", ex);
        }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (!_exiting && _db != null)
        {
            _exiting = true;
            try
            {
                _service?.SaveSettings();
                _fortnite?.Dispose();
                _hotkeys?.Dispose();
                _service?.Dispose();
                _tray?.Dispose();
                _db.Dispose();
            }
            catch
            {
                // kapanışta yoksay
            }
        }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
