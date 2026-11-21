using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using FortniteCompanion.Core.Services;

namespace FortniteCompanion.Views;

/// <summary>
/// İlk açılış: "Sen kimsin?" Herkes kendi adını, Epic adını ve oyundaki sayaçtan başlangıç değerlerini girer.
/// Ekipten gelen bir dosya varsa önce içe aktarılıp listeden seçilebilir. Sabit oyuncu listesi yoktur.
/// </summary>
public sealed class OnboardingWindow : Window
{
    private const string NewPlayerOption = "➕ Yeni oyuncu (ben)";

    private readonly CompanionService _service;
    private readonly ComboBox _who = new() { Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _name = new();
    private readonly TextBox _epic = new();
    private readonly TextBox _chests = new() { Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _matches = new() { Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _kills = new() { Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _error = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _importInfo = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };

    public OnboardingWindow(CompanionService service)
    {
        _service = service;
        Title = "Fortnite Companion — Hoş geldin";
        Width = 600;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "WindowBackground");
        FontFamily = (FontFamily)Application.Current.Resources["UiFont"];
        _importInfo.SetResourceReference(StyleProperty, "HintText");

        var root = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };

        // Başlık
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        header.Children.Add(new Image { Source = (ImageSource)Application.Current.Resources["ChestIcon"], Width = 54, Height = 54, Margin = new Thickness(0, 0, 14, 0) });
        var title = new TextBlock { Text = "HOŞ GELDİN!", VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(StyleProperty, "TitleText");
        header.Children.Add(title);
        root.Children.Add(header);
        root.Children.Add(Hint("Kurulum 1 dakika sürer. Bilgilerin yalnızca bu bilgisayarda saklanır; ekip dosyasını paylaşmak sana kalmış."));

        // 1. adım: ekip dosyası
        var step1 = Card();
        var s1 = new StackPanel();
        s1.Children.Add(Section("1 · EKİP DOSYAN VAR MI? (İSTEĞE BAĞLI)"));
        s1.Children.Add(Hint("Ekibinden biri sana Fortnite Companion ekip dosyası (.json) gönderdiyse önce onu ekle; sonra listeden kendini seçebilirsin."));
        var importButton = new Button { Content = "Ekip dosyasını içe aktar…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        importButton.Click += (_, _) => ImportTeamFile();
        s1.Children.Add(importButton);
        s1.Children.Add(_importInfo);
        step1.Child = s1;
        root.Children.Add(step1);

        // 2. adım: sen kimsin
        var step2 = Card();
        var s2 = new StackPanel();
        s2.Children.Add(Section("2 · SEN KİMSİN?"));
        s2.Children.Add(Label("Listede varsan seç, yoksa yeni oyuncu"));
        s2.Children.Add(_who);
        s2.Children.Add(Label("Görünen ad (ekipte görünecek)"));
        s2.Children.Add(_name);
        s2.Children.Add(Label("Epic adın (oyunda göründüğü gibi, birebir)"));
        s2.Children.Add(_epic);
        s2.Children.Add(Hint("Replay'lerdeki sandıklar bu adla sana yazılır. Boş bırakırsan ilk maçından sonra otomatik doldurulur."));
        step2.Child = s2;
        root.Children.Add(step2);

        // 3. adım: başlangıç değerleri
        var step3 = Card();
        var s3 = new StackPanel();
        s3.Children.Add(Section("3 · ŞU ANKİ SANDIK SAYIN"));
        s3.Children.Add(Hint("Eski maçların replay'i olmadığı için başlangıç değerini oyundan al: Görevler → Hazine Avı. Hazine, Nadir ve Ruh sandığı sayaçlarının TOPLAMINI yaz. Bilmiyorsan boş bırak (0'dan başlar)."));
        var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        AddField(grid, 0, "SANDIK", _chests);
        AddField(grid, 1, "MAÇ (isteğe bağlı)", _matches);
        AddField(grid, 2, "KILL (isteğe bağlı)", _kills);
        s3.Children.Add(grid);
        step3.Child = s3;
        root.Children.Add(step3);

        root.Children.Add(_error);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var skip = new Button { Content = "Şimdilik geç", MinWidth = 110 };
        skip.Click += (_, _) =>
        {
            _service.Settings.OnboardingDone = true;
            _service.SaveSettings();
            DialogResult = false;
        };
        var start = new Button { Content = "BAŞLA", MinWidth = 140, IsDefault = true };
        start.SetResourceReference(StyleProperty, "AccentButton");
        start.Click += (_, _) => Save();
        buttons.Children.Add(skip);
        buttons.Children.Add(start);
        root.Children.Add(buttons);

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 860 };

        _who.SelectionChanged += (_, _) => FillFromSelection();
        LoadPlayers(preferSelf: true);
    }

    private static Border Card()
    {
        var b = new Border();
        b.SetResourceReference(StyleProperty, "Card");
        b.Margin = new Thickness(0, 10, 0, 0);
        return b;
    }

    private static TextBlock Section(string text)
    {
        var t = new TextBlock { Text = text };
        t.SetResourceReference(StyleProperty, "SectionText");
        return t;
    }

    private static TextBlock Label(string text)
    {
        var t = new TextBlock { Text = text, Margin = new Thickness(0, 6, 0, 3) };
        t.SetResourceReference(StyleProperty, "LabelText");
        return t;
    }

    private static TextBlock Hint(string text)
    {
        var t = new TextBlock { Text = text };
        t.SetResourceReference(StyleProperty, "HintText");
        return t;
    }

    private static void AddField(Grid grid, int column, string label, TextBox box)
    {
        var panel = new StackPanel();
        panel.Children.Add(Label(label));
        panel.Children.Add(box);
        Grid.SetColumn(panel, column);
        grid.Children.Add(panel);
    }

    private void LoadPlayers(bool preferSelf)
    {
        var players = _service.Db.GetPlayers().Where(p => !p.Hidden).ToList();
        var options = new[] { NewPlayerOption }.Concat(players.Select(p => p.DisplayName)).ToList();
        _who.ItemsSource = options;
        var self = players.FirstOrDefault(p => p.Id == _service.Settings.SelfPlayerId);
        _who.SelectedItem = preferSelf && self != null ? self.DisplayName : NewPlayerOption;
        FillFromSelection();
    }

    private void FillFromSelection()
    {
        var name = _who.SelectedItem as string;
        var player = _service.Db.GetPlayers().FirstOrDefault(p => p.DisplayName == name);
        if (player == null)
        {
            _name.Text = "";
            _epic.Text = "";
            _chests.Text = "";
            _matches.Text = "";
            _kills.Text = "";
            return;
        }
        var b = _service.Db.GetBaselines().FirstOrDefault(x => x.PlayerId == player.Id);
        _name.Text = player.DisplayName;
        _epic.Text = player.EpicNames;
        _chests.Text = b?.Chests.ToString(CultureInfo.InvariantCulture) ?? "";
        _matches.Text = b?.Matches.ToString(CultureInfo.InvariantCulture) ?? "";
        _kills.Text = b?.Kills.ToString(CultureInfo.InvariantCulture) ?? "";
    }

    private void ImportTeamFile()
    {
        var dlg = new OpenFileDialog { Filter = "Fortnite Companion ekip dosyası (*.json)|*.json" };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }
        try
        {
            var folder = string.IsNullOrWhiteSpace(_service.Settings.BackupFolder) ? AppPaths.DefaultBackupFolder : _service.Settings.BackupFolder!;
            BackupService.RunNow(_service.Db, folder, _service.Settings.BackupKeepCount);
            var file = ImportExportService.ReadJson(dlg.FileName);
            var report = ImportExportService.Import(_service.Db, file, Path.GetFileName(dlg.FileName));
            _importInfo.Text = $"✓ {Path.GetFileName(dlg.FileName)} eklendi: {report.NewPlayers} oyuncu, {report.NewMatches} maç. Şimdi aşağıdan kendini seç.";
            LoadPlayers(preferSelf: false);
        }
        catch (Exception ex)
        {
            AppLog.Error("İlk açılışta ekip dosyası içe aktarılamadı", ex);
            _importInfo.Text = "İçe aktarılamadı: " + ex.Message;
        }
    }

    private static bool TryOptionalInt(string text, out int? value)
    {
        value = null;
        text = text.Trim().Replace(".", "").Replace(",", "");
        if (text.Length == 0)
        {
            return true;
        }
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v >= 0 && v <= 1_000_000)
        {
            value = v;
            return true;
        }
        return false;
    }

    private void Save()
    {
        var name = _name.Text.Trim();
        if (name.Length == 0)
        {
            _error.Text = "Görünen adını yaz.";
            return;
        }
        if (!TryOptionalInt(_chests.Text, out var chests) || !TryOptionalInt(_matches.Text, out var matches) || !TryOptionalInt(_kills.Text, out var kills))
        {
            _error.Text = "Sandık, maç ve kill alanlarına yalnızca sayı yaz (ya da boş bırak).";
            return;
        }

        var selected = _who.SelectedItem as string;
        var existing = _service.Db.GetPlayers().FirstOrDefault(p => p.DisplayName == selected);
        if (existing == null && _service.Db.GetPlayers().Any(p => string.Equals(p.DisplayName, name, StringComparison.OrdinalIgnoreCase)))
        {
            if (MessageBox.Show($"Ekipte zaten \"{name}\" adında biri var. O kişi sen misin?", "Fortnite Companion",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                _error.Text = "Farklı bir görünen ad seç.";
                return;
            }
        }

        try
        {
            _service.CompleteOnboarding(existing?.Id, name, _epic.Text.Trim(), chests, matches, kills);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            AppLog.Error("İlk açılış kaydedilemedi", ex);
            _error.Text = "Kaydedilemedi: " + ex.Message;
        }
    }
}
