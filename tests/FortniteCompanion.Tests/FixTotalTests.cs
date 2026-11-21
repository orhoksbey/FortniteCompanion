using FortniteCompanion.Core.Data;
using FortniteCompanion.Core.Services;

namespace FortniteCompanion.Tests;

public class FixTotalTests
{
    private static CompanionService NewService(Database db, out string settingsPath)
    {
        settingsPath = Path.Combine(Path.GetTempPath(), $"fc-settings-{Guid.NewGuid():N}.json");
        return new CompanionService(db, new AppSettings { ReplayFolder = Path.GetTempPath() }, settingsPath);
    }

    [Fact]
    public void Setting_total_from_game_only_moves_baseline_chests()
    {
        using var db = Database.OpenInMemory();
        using var service = NewService(db, out var path);
        try
        {
            service.CompleteOnboarding(null, "Mert", "MertFN", chests: 312, matches: 20, kills: 41);
            db.UpsertReplayMatch(TestData.Match("NEW", DateTime.UtcNow.AddHours(1), TestData.Player("MertFN", 10, recorder: true, kills: 2)));
            service.Refresh();
            Assert.Equal(322, service.Snapshot.Self!.TotalChests);

            Assert.Null(service.SetTotalChestsFromGame(330, "oyun çöktü"));
            var self = service.Snapshot.Self!;
            Assert.Equal(330, self.TotalChests);
            Assert.Equal(320, self.BaselineChests);
            Assert.Equal(21, self.TotalMatches);     // maç sayısı değişmez
            Assert.Equal(10, self.TrackedChests);    // replay maçı hâlâ sayılıyor

            // replay'den sayılanın altına inemez
            Assert.NotNull(service.SetTotalChestsFromGame(5, null));
            Assert.Equal(330, service.Snapshot.Self!.TotalChests);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Admin_adjustment_shows_in_total_but_is_not_sent_back()
    {
        using var db = Database.OpenInMemory();
        using var service = NewService(db, out var path);
        try
        {
            service.CompleteOnboarding(null, "Mert", "MertFN", chests: 312, matches: 20, kills: 41);
            service.Settings.AdminChestAdjust = 7;
            service.Refresh();
            var self = service.Snapshot.Self!;
            Assert.Equal(319, self.TotalChests);

            TeamSync.EnsureDeviceIdentity(service.Settings);
            var payload = TeamSync.BuildPayload(service.Settings, self, "0.6.0");
            Assert.Equal(312, payload.TotalChests); // sunucu düzeltmeyi kendisi ekler

            // Eşitlemeden sonra da toplam düzeltmeyi korur
            Assert.Null(service.SetTotalChestsFromGame(330, null));
            Assert.Equal(330, service.Snapshot.Self!.TotalChests);
            Assert.Equal(323, service.Snapshot.Self!.BaselineChests);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
