using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using FortniteCompanion.Core.Services;

namespace FortniteCompanion.Views;

public partial class FriendsView : UserControl
{
    public FriendsView()
    {
        InitializeComponent();
    }

    private static CompanionService Service => App.Current.Service;

    private static long? IdOf(object sender) => (sender as FrameworkElement)?.Tag is long id ? id : null;

    private void OnEditEpicNames(object sender, RoutedEventArgs e)
    {
        var player = Service.Db.GetPlayers().FirstOrDefault(p => p.Id == IdOf(sender));
        if (player == null)
        {
            return;
        }
        var dlg = new FormDialog($"{player.DisplayName} — düzenle",
                "Epic adını oyunda göründüğü gibi birebir yazın. Birden fazla hesap varsa virgülle ayırın. Replay'lerdeki sayılar bu adlarla eşleştirilir.")
            .Text("name", "Görünen ad", player.DisplayName)
            .Text("names", "Epic adları", player.EpicNames)
            .Validate(d => d.GetText("name").Length == 0 ? "Görünen ad boş olamaz." : null);
        dlg.Owner = Window.GetWindow(this);
        if (dlg.ShowDialog() != true)
        {
            return;
        }
        try
        {
            Service.Db.RenamePlayer(player.Id, dlg.GetText("name"), "Kullanıcı oyuncuyu düzenledi");
            Service.Db.SetEpicNames(player.Id, dlg.GetText("names"), "Kullanıcı Epic adlarını düzenledi");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Kaydedilemedi (aynı ad başka oyuncuda olabilir): " + ex.Message, "Düzenle");
        }
        Service.Refresh();
    }

    private void OnToggleHidden(object sender, RoutedEventArgs e)
    {
        var player = Service.Db.GetPlayers().FirstOrDefault(p => p.Id == IdOf(sender));
        if (player == null)
        {
            return;
        }
        if (!player.Hidden && player.Id == Service.Settings.SelfPlayerId)
        {
            MessageBox.Show("Kendinizi gizleyemezsiniz.", "Ekip");
            return;
        }
        Service.Db.SetHidden(player.Id, !player.Hidden, player.Hidden ? "Kullanıcı yeniden gösterdi" : "Kullanıcı gizledi (ekipten ayrıldı)");
        Service.Refresh();
    }

    private void OnEditBaseline(object sender, RoutedEventArgs e)
    {
        var player = Service.Db.GetPlayers().FirstOrDefault(p => p.Id == IdOf(sender));
        if (player == null)
        {
            return;
        }
        var b = Service.Db.GetBaselines().FirstOrDefault(x => x.PlayerId == player.Id);
        var cutoffLocal = (b?.CutoffUtc ?? DateTime.UtcNow).ToLocalTime();
        var dlg = new FormDialog($"{player.DisplayName} — Geçmişten eklenen kayıtlar",
                "Uygulamadan önce tutulan toplamlar. Başlangıç tarihi: bu değerlerin geçerli olduğu son an; bundan önce oynanan replay maçları örtüşme sayılır ve toplama eklenmez. Her değişiklik silinemez değişiklik geçmişine yazılır.")
            .Number("matches", "Maç sayısı", b?.Matches ?? 0)
            .Number("chests", "Sandık", b?.Chests ?? 0)
            .Number("kills", "Kill", b?.Kills ?? 0)
            .Date("cutoff", "Başlangıç tarihi ve saati (SS:dd)", cutoffLocal)
            .Text("note", "Not", b?.Note ?? "")
            .Text("reason", "Değişiklik nedeni (zorunlu)")
            .Validate(d =>
            {
                if (d.GetInt("matches") is not int m || m < 0) return "Maç sayısı 0 veya daha büyük bir tam sayı olmalı.";
                if (d.GetInt("chests") is not int c || c < 0) return "Sandık sayısı 0 veya daha büyük bir tam sayı olmalı.";
                if (d.GetInt("kills") is not int k || k < 0) return "Kill 0 veya daha büyük bir tam sayı olmalı.";
                if (d.GetLocalDateTime("cutoff") == null) return "Başlangıç tarihi seçin.";
                if (d.GetText("reason").Length < 3) return "Değişiklik nedenini yazın (değişiklik geçmişi için).";
                return null;
            });
        dlg.Owner = Window.GetWindow(this);
        if (dlg.ShowDialog() != true)
        {
            return;
        }
        var cutoff = DateTime.SpecifyKind(dlg.GetLocalDateTime("cutoff")!.Value, DateTimeKind.Local).ToUniversalTime();
        var note = dlg.GetText("note");
        Service.Db.UpdateBaseline(player.Id, dlg.GetInt("matches") ?? 0, dlg.GetInt("chests") ?? 0, dlg.GetInt("kills") ?? 0,
            cutoff, note.Length > 0 ? note : null, dlg.GetText("reason"));
        Service.Refresh();
    }

    private void OnSetSelf(object sender, RoutedEventArgs e)
    {
        if (IdOf(sender) is not long id)
        {
            return;
        }
        Service.Settings.SelfPlayerId = id;
        Service.SaveSettings();
        Service.Refresh();
    }

    private void OnMatchName(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string name)
        {
            return;
        }
        var players = Service.Db.GetPlayers();
        var dlg = new FormDialog("Epic adını eşleştir", $"\"{name}\" hangi oyuncuya ait?")
            .Choice("player", "Oyuncu", players.Select(p => p.DisplayName), null);
        dlg.Owner = Window.GetWindow(this);
        if (dlg.ShowDialog() != true)
        {
            return;
        }
        var player = players.First(p => p.DisplayName == dlg.GetChoice("player"));
        var merged = string.Join(", ", player.EpicNameList.Append(name).Distinct(StringComparer.OrdinalIgnoreCase));
        Service.Db.SetEpicNames(player.Id, merged, $"Eşleşmemiş ad eşleştirildi: {name}");
        Service.Refresh();
    }

    private void OnExportJson(object sender, RoutedEventArgs e)
    {
        var self = Service.SelfPlayer?.DisplayName ?? "oyuncu";
        var dlg = new SaveFileDialog
        {
            Filter = "Fortnite Companion verisi (*.json)|*.json",
            FileName = $"fortnite-companion-ekip-{self}-{DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.json",
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }
        try
        {
            ImportExportService.ExportJson(Service.Db, dlg.FileName, Service.Settings.ExportHideUnknownNames, self);
            MessageBox.Show("Dışa aktarıldı:\n" + dlg.FileName + "\n\nBu dosyayı arkadaşlarınıza gönderin; onlar \"JSON içe aktar\" ile ekleyebilir.", "Dışa aktarma");
        }
        catch (Exception ex)
        {
            AppLog.Error("JSON dışa aktarılamadı", ex);
            MessageBox.Show("Dışa aktarılamadı: " + ex.Message, "Dışa aktarma", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnImportJson(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Fortnite Companion verisi (*.json)|*.json" };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }
        try
        {
            // İçe aktarmadan önce güvenlik yedeği
            BackupService.RunNow(Service.Db, string.IsNullOrWhiteSpace(Service.Settings.BackupFolder) ? AppPaths.DefaultBackupFolder : Service.Settings.BackupFolder!, Service.Settings.BackupKeepCount);
            var file = ImportExportService.ReadJson(dlg.FileName);
            var report = ImportExportService.Import(Service.Db, file, Path.GetFileName(dlg.FileName));
            Service.Refresh();
            MessageBox.Show(report.ToString(), "İçe aktarma tamamlandı");
        }
        catch (Exception ex)
        {
            AppLog.Error("JSON içe aktarılamadı", ex);
            MessageBox.Show("İçe aktarılamadı: " + ex.Message, "İçe aktarma", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnAddPlayer(object sender, RoutedEventArgs e)
    {
        var dlg = new FormDialog("Oyuncu ekle")
            .Text("name", "Görünen ad")
            .Text("epic", "Epic adı (virgülle birden fazla)")
            .Validate(d => d.GetText("name").Length == 0 ? "Bir ad girin." : null);
        dlg.Owner = Window.GetWindow(this);
        if (dlg.ShowDialog() != true)
        {
            return;
        }
        try
        {
            Service.Db.AddPlayer(dlg.GetText("name"), dlg.GetText("epic"));
            Service.Refresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Oyuncu eklenemedi (aynı ad zaten olabilir): " + ex.Message, "Oyuncu ekle");
        }
    }
}
