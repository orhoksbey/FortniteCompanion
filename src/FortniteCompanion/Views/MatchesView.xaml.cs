using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using FortniteCompanion.Core.Services;
using FortniteCompanion.ViewModels;

namespace FortniteCompanion.Views;

public partial class MatchesView : UserControl
{
    public MatchesView()
    {
        InitializeComponent();
    }

    private static CompanionService Service => App.Current.Service;

    private MatchItem? Selected => MatchGrid.SelectedItem as MatchItem;

    private void OnToggleExclude(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } m)
        {
            MessageBox.Show("Önce tablodan bir maç seçin.", "Maç Geçmişi");
            return;
        }
        var exclude = !m.Excluded;
        var verb = exclude ? "toplamlardan HARİÇ tutulsun" : "yeniden toplama DAHİL edilsin";
        if (MessageBox.Show($"{m.Date} tarihli maç ({m.Player}, {m.Chests} sandık) {verb} mı?\n\nKayıt silinmez; bu işlem değişiklik geçmişine yazılır.",
                "Onay", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }
        Service.Db.SetExcluded(m.SessionId, exclude, exclude ? "Kullanıcı hariç tuttu" : "Kullanıcı yeniden dahil etti");
        Service.Refresh();
    }

    private void OnOverlapInclude(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } m || !m.BeforeCutoff)
        {
            MessageBox.Show("Başlangıç tarihinden önce oynanmış (örtüşen) bir maç seçin.", "Maç Geçmişi");
            return;
        }
        if (MessageBox.Show("Bu maç geçmiş başlangıç değerlerinin tutulduğu dönemde oynanmış. Geçmiş değerlere DAHİL OLMADIĞINDAN eminseniz toplama ekleyin; aksi hâlde aynı sandıklar iki kez sayılır.\n\nDahil edilsin mi?",
                "Örtüşme", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }
        Service.Db.SetOverlapOverride(m.SessionId, true, "Kullanıcı: geçmiş değerlere dahil değil");
        Service.Refresh();
    }

    private void OnAddManual(object sender, RoutedEventArgs e)
    {
        var players = Service.Db.GetPlayers();
        var self = players.FirstOrDefault(p => p.Id == Service.Settings.SelfPlayerId);
        var dlg = new FormDialog("Manuel maç ekle",
                "Replay'i olmayan bir maçı elle ekleyin. \"Manuel girildi\" olarak işaretlenir; aynı zamana ait replay gelirse otomatik olarak toplamdan çıkarılır.")
            .Choice("player", "Oyuncu", players.Select(p => p.DisplayName), self?.DisplayName)
            .Date("date", "Maç tarihi ve saati (SS:dd)", DateTime.Now)
            .Number("chests", "Sandık sayısı", 0)
            .Number("kills", "Kill (boş bırakılabilir)", null)
            .Text("note", "Not (isteğe bağlı)")
            .Validate(d =>
            {
                if (d.GetInt("chests") is not int c || c < 0 || c > 500) return "Sandık sayısı 0–500 arasında olmalı.";
                if (d.GetText("kills").Length > 0 && (d.GetInt("kills") is not int k || k < 0 || k > 200)) return "Kill 0–200 arasında olmalı.";
                if (d.GetLocalDateTime("date") is not DateTime dt || dt > DateTime.Now.AddHours(1)) return "Geçerli bir tarih seçin.";
                return null;
            });
        dlg.Owner = Window.GetWindow(this);
        if (dlg.ShowDialog() != true)
        {
            return;
        }
        var player = players.First(p => p.DisplayName == dlg.GetChoice("player"));
        var started = DateTime.SpecifyKind(dlg.GetLocalDateTime("date")!.Value, DateTimeKind.Local).ToUniversalTime();
        var note = dlg.GetText("note");
        Service.Db.AddManualMatch(player, started, null, dlg.GetInt("chests") ?? 0, dlg.GetInt("kills"),
            note.Length > 0 ? note : null, "Kullanıcı manuel maç ekledi");
        Service.Refresh();
    }

    private void OnExportCsv(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "CSV dosyası (*.csv)|*.csv",
            FileName = $"fortnite-companion-maclar-{DateTime.Now:yyyyMMdd}.csv",
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }
        try
        {
            ImportExportService.ExportCsv(Service.Snapshot.Players, dlg.FileName);
            MessageBox.Show("CSV kaydedildi:\n" + dlg.FileName, "Dışa aktarma");
        }
        catch (Exception ex)
        {
            AppLog.Error("CSV dışa aktarılamadı", ex);
            MessageBox.Show("CSV kaydedilemedi: " + ex.Message, "Dışa aktarma", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnEditLog(object sender, RoutedEventArgs e)
    {
        var w = new EditLogWindow(Service.Db) { Owner = Window.GetWindow(this) };
        w.ShowDialog();
    }
}
