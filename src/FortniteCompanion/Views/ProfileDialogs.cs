using System;
using System.Linq;
using System.Windows;
using FortniteCompanion.Core.Services;

namespace FortniteCompanion.Views;

/// <summary>Kendi sayacını düzeltme pencereleri (Genel Bakış ve Ayarlar'dan açılır).</summary>
public static class ProfileDialogs
{
    private static CompanionService Service => App.Current.Service;

    /// <summary>Toplamı oyundaki sandık görevinde yazan sayıya eşitler.</summary>
    public static void FixTotal(Window? owner)
    {
        var self = Service.Snapshot.Self;
        if (self == null)
        {
            MessageBox.Show("Önce Ayarlar → \"Sen kimsin?\" ile kendini seç.", "Sayacı düzelt");
            return;
        }
        var dlg = new FormDialog("Oyundaki sayıya eşitle",
                $"Şu an uygulamada {self.TotalChests:N0} sandık görünüyor. Oyundaki sandık görevinde yazan sayıyı gir; aradaki fark " +
                "\"geçmişten eklenen\" değere eklenir. Maç sayın ve replay maçların değişmez. Oyun çöktüğünde okunamayan sandıklar için kullan. " +
                "Maç devam ediyorsa önce maçın bitmesini ve sayılmasını bekle.")
            .Number("target", "Oyundaki toplam sandık", self.TotalChests)
            .Text("reason", "Neden (isteğe bağlı)", "")
            .Validate(d => d.GetInt("target") is int t && t >= 0 ? null : "0 veya daha büyük bir tam sayı girin.");
        dlg.Owner = owner;
        if (dlg.ShowDialog() != true)
        {
            return;
        }
        var error = Service.SetTotalChestsFromGame(dlg.GetInt("target")!.Value, dlg.GetText("reason"));
        if (error != null)
        {
            MessageBox.Show(error, "Sayacı düzelt");
        }
    }

    /// <summary>Geçmişten eklenen değerlerin (maç, sandık, kill, başlangıç tarihi) tam düzenleyicisi.</summary>
    public static void EditBaseline(Window? owner)
    {
        var player = Service.SelfPlayer;
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
        dlg.Owner = owner;
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
}
