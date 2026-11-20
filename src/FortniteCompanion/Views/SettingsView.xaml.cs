using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using FortniteCompanion.Core.Services;

namespace FortniteCompanion.Views;

public partial class SettingsView : UserControl
{
    private static readonly (OverlayCorner Value, string Text)[] Corners =
    {
        (OverlayCorner.TopRight, "Sağ üst"),
        (OverlayCorner.TopLeft, "Sol üst"),
        (OverlayCorner.BottomRight, "Sağ alt"),
        (OverlayCorner.BottomLeft, "Sol alt"),
    };

    private static readonly (string Value, string Text)[] Themes = { ("dark", "Koyu"), ("light", "Açık") };
    private static readonly (string Value, string Text)[] Accents = { ("gold", "Fortnite sarısı"), ("green", "Yeşil"), ("blue", "Mavi") };

    // LoadFromSettings çağrılana kadar kontrol olayları ayarlara yazmaz (başlangıçtaki varsayılan
    // kaydırıcı değerleri kullanıcının ayarlarını ezmesin).
    private bool _loading = true;

    public SettingsView()
    {
        InitializeComponent();
        Corner.ItemsSource = Corners.Select(c => c.Text).ToList();
        Theme.ItemsSource = Themes.Select(t => t.Text).ToList();
        Accent.ItemsSource = Accents.Select(a => a.Text).ToList();
    }

    private static CompanionService Service => App.Current.Service;

    public void LoadFromSettings()
    {
        _loading = true;
        var s = Service.Settings;
        OverlayVisible.IsChecked = s.OverlayVisible;
        AutoOverlay.IsChecked = s.AutoOverlayWithFortnite;
        OverlayLocked.IsChecked = s.OverlayLocked;
        TeamLine.IsChecked = s.OverlayShowTeamLine;
        Animations.IsChecked = s.OverlayAnimations;
        Corner.SelectedIndex = Math.Max(0, Array.FindIndex(Corners, c => c.Value == s.OverlayCorner));
        Scale.Value = s.OverlayScale;
        OverlayOpacity.Value = s.OverlayOpacity;
        Theme.SelectedIndex = Math.Max(0, Array.FindIndex(Themes, t => t.Value == s.Theme));
        Accent.SelectedIndex = Math.Max(0, Array.FindIndex(Accents, a => a.Value == s.Accent));

        HkToggle.Text = s.HotkeyToggleOverlay;
        HkLock.Text = s.HotkeyToggleLock;
        HkPlus.Text = s.HotkeyManualIncrement;
        HkMinus.Text = s.HotkeyManualDecrement;

        var players = Service.Db.GetPlayers();
        SelfPlayer.ItemsSource = players.Select(p => p.DisplayName).ToList();
        SelfPlayer.SelectedItem = players.FirstOrDefault(p => p.Id == s.SelfPlayerId)?.DisplayName;
        ReplayFolder.Text = string.IsNullOrWhiteSpace(s.ReplayFolder) ? AppPaths.DefaultReplayFolder : s.ReplayFolder;
        ManualLive.IsChecked = s.ManualLiveMode;
        IncludeCustom.IsChecked = s.IncludeCustomMatches;
        SeasonStart.SelectedDate = DateTime.SpecifyKind(s.SeasonStartUtc, DateTimeKind.Utc).ToLocalTime().Date;

        StartWithWindows.IsChecked = s.StartWithWindows;
        StartMinimized.IsChecked = s.StartMinimized;
        NotifyMatch.IsChecked = s.NotifyMatchEnd;
        NotifyRecord.IsChecked = s.NotifyRecord;

        AutoBackup.IsChecked = s.AutoBackup;
        BackupKeep.Text = s.BackupKeepCount.ToString(CultureInfo.InvariantCulture);
        BackupFolder.Text = s.BackupFolder ?? "";
        HideUnknown.IsChecked = s.ExportHideUnknownNames;
        AutoUpdateBox.IsChecked = s.AutoUpdate;
        UpdateRepoBox.Text = s.UpdateRepo ?? "";
        var repo = AutoUpdater.EffectiveRepo(s);
        UpdateInfo.Text = $"Kurulu sürüm: {AutoUpdater.CurrentVersion.ToString(3)} · Güncelleme kaynağı: " +
                          (repo == null ? "ayarlı değil" : "github.com/" + repo);
        UpdateSliderLabels();
        _loading = false;
    }

    private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ScaleLabel == null || OpacityLabel == null || Scale == null || OverlayOpacity == null)
        {
            return;
        }
        UpdateSliderLabels();
        if (_loading)
        {
            return;
        }
        // Canlı önizleme
        Service.Settings.OverlayScale = Scale.Value;
        Service.Settings.OverlayOpacity = OverlayOpacity.Value;
        App.Current.PreviewOverlayAppearance();
    }

    private void UpdateSliderLabels()
    {
        ScaleLabel.Text = $"Boyut: %{Math.Round(Scale.Value * 100)}";
        OpacityLabel.Text = $"Opaklık: %{Math.Round(OverlayOpacity.Value * 100)}";
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var s = Service.Settings;
        var oldHotkeys = string.Join("|", s.HotkeyToggleOverlay, s.HotkeyToggleLock, s.HotkeyManualIncrement, s.HotkeyManualDecrement);
        var oldFolder = s.ReplayFolder ?? "";

        s.OverlayVisible = OverlayVisible.IsChecked == true;
        s.AutoOverlayWithFortnite = AutoOverlay.IsChecked == true;
        s.OverlayLocked = OverlayLocked.IsChecked == true;
        s.OverlayShowTeamLine = TeamLine.IsChecked == true;
        s.OverlayAnimations = Animations.IsChecked == true;
        if (Corner.SelectedIndex >= 0)
        {
            s.OverlayCorner = Corners[Corner.SelectedIndex].Value;
        }
        s.OverlayScale = Scale.Value;
        s.OverlayOpacity = OverlayOpacity.Value;
        if (Theme.SelectedIndex >= 0) s.Theme = Themes[Theme.SelectedIndex].Value;
        if (Accent.SelectedIndex >= 0) s.Accent = Accents[Accent.SelectedIndex].Value;

        s.HotkeyToggleOverlay = HkToggle.Text.Trim();
        s.HotkeyToggleLock = HkLock.Text.Trim();
        s.HotkeyManualIncrement = HkPlus.Text.Trim();
        s.HotkeyManualDecrement = HkMinus.Text.Trim();

        var players = Service.Db.GetPlayers();
        if (SelfPlayer.SelectedItem is string selfName && players.FirstOrDefault(p => p.DisplayName == selfName) is { } self)
        {
            s.SelfPlayerId = self.Id;
        }
        var folder = ReplayFolder.Text.Trim();
        s.ReplayFolder = string.Equals(folder, AppPaths.DefaultReplayFolder, StringComparison.OrdinalIgnoreCase) ? null : folder;
        s.ManualLiveMode = ManualLive.IsChecked == true;
        s.IncludeCustomMatches = IncludeCustom.IsChecked == true;
        if (SeasonStart.SelectedDate is DateTime season)
        {
            s.SeasonStartUtc = DateTime.SpecifyKind(season.Date, DateTimeKind.Local).ToUniversalTime();
        }

        s.StartWithWindows = StartWithWindows.IsChecked == true;
        s.StartMinimized = StartMinimized.IsChecked == true;
        s.NotifyMatchEnd = NotifyMatch.IsChecked == true;
        s.NotifyRecord = NotifyRecord.IsChecked == true;

        s.AutoBackup = AutoBackup.IsChecked == true;
        if (int.TryParse(BackupKeep.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var keep) && keep is >= 1 and <= 365)
        {
            s.BackupKeepCount = keep;
        }
        s.BackupFolder = string.IsNullOrWhiteSpace(BackupFolder.Text) ? null : BackupFolder.Text.Trim();
        s.ExportHideUnknownNames = HideUnknown.IsChecked == true;
        s.AutoUpdate = AutoUpdateBox.IsChecked == true;
        s.UpdateRepo = string.IsNullOrWhiteSpace(UpdateRepoBox.Text) ? null : UpdateRepoBox.Text.Trim().Trim('/');

        var newHotkeys = string.Join("|", s.HotkeyToggleOverlay, s.HotkeyToggleLock, s.HotkeyManualIncrement, s.HotkeyManualDecrement);
        App.Current.ApplySettings(hotkeysChanged: newHotkeys != oldHotkeys, folderChanged: !string.Equals(oldFolder, s.ReplayFolder ?? "", StringComparison.OrdinalIgnoreCase));
        SavedText.Text = "Kaydedildi: " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private void OnResetPosition(object sender, RoutedEventArgs e)
    {
        var s = Service.Settings;
        s.OverlayCorner = OverlayCorner.TopRight;
        s.OverlayOffsetX = 0.012;
        s.OverlayOffsetY = 0.18;
        s.OverlayScreen = null;
        Corner.SelectedIndex = 0;
        App.Current.ApplySettings(hotkeysChanged: false, folderChanged: false);
    }

    private void OnBrowseReplay(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Replay klasörünü seçin (…\\FortniteGame\\Saved\\Demos)" };
        if (Directory.Exists(ReplayFolder.Text))
        {
            dlg.InitialDirectory = ReplayFolder.Text;
        }
        if (dlg.ShowDialog(Window.GetWindow(this)) == true)
        {
            ReplayFolder.Text = dlg.FolderName;
        }
    }

    private void OnDefaultReplay(object sender, RoutedEventArgs e) => ReplayFolder.Text = AppPaths.DefaultReplayFolder;

    private void OnOpenReplay(object sender, RoutedEventArgs e) => OpenFolder(ReplayFolder.Text);

    private void OnScanNow(object sender, RoutedEventArgs e)
    {
        _ = Service.Watcher.ScanAsync(ignoreQuietPeriod: true);
        SavedText.Text = "Tarama başlatıldı…";
    }

    private void OnBackupNow(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = BackupService.RunNow(Service.Db, BackupDir(), Service.Settings.BackupKeepCount);
            MessageBox.Show("Yedek alındı:\n" + path, "Yedekleme");
        }
        catch (Exception ex)
        {
            AppLog.Error("Yedek alınamadı", ex);
            MessageBox.Show("Yedek alınamadı: " + ex.Message, "Yedekleme", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string BackupDir() =>
        string.IsNullOrWhiteSpace(Service.Settings.BackupFolder) ? AppPaths.DefaultBackupFolder : Service.Settings.BackupFolder!;

    private void OnOpenBackups(object sender, RoutedEventArgs e) => OpenFolder(BackupDir());
    private void OnOpenData(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.DataFolder);
    private void OnOpenLogs(object sender, RoutedEventArgs e) => OpenFolder(AppPaths.LogFolder);

    private void OnEditLog(object sender, RoutedEventArgs e)
    {
        new EditLogWindow(Service.Db) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        await App.Current.CheckForUpdateAsync(manual: true);
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Klasör açılamadı: " + ex.Message, "Fortnite Companion");
        }
    }
}
