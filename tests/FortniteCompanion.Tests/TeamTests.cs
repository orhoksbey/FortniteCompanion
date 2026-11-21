using FortniteCompanion.Core.Data;
using FortniteCompanion.Core.Services;

namespace FortniteCompanion.Tests;

public class TeamTests
{
    private static CompanionService NewService(Database db, out string settingsPath)
    {
        settingsPath = Path.Combine(Path.GetTempPath(), $"fc-settings-{Guid.NewGuid():N}.json");
        return new CompanionService(db, new AppSettings { ReplayFolder = Path.GetTempPath() }, settingsPath);
    }

    [Fact]
    public void Fresh_install_has_no_hardcoded_players()
    {
        using var db = Database.OpenInMemory();
        using var service = NewService(db, out var path);
        try
        {
            Assert.Empty(db.GetPlayers());
            Assert.Null(service.Settings.SelfPlayerId);
            Assert.False(service.Settings.OnboardingDone);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Onboarding_creates_self_with_baseline_from_in_game_counter()
    {
        using var db = Database.OpenInMemory();
        using var service = NewService(db, out var path);
        try
        {
            var me = service.CompleteOnboarding(null, "Mert", "MertFN", chests: 312, matches: 20, kills: 41);
            Assert.Equal(me.Id, service.Settings.SelfPlayerId);
            Assert.True(service.Settings.OnboardingDone);
            Assert.Contains("MertFN", me.EpicNameList);
            var stats = service.Snapshot.Self!;
            Assert.Equal(312, stats.TotalChests);
            Assert.Equal(20, stats.TotalMatches);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Onboarding_can_pick_an_imported_player_and_rename_it()
    {
        using var db = TestData.SeededDb();
        using var service = NewService(db, out var path);
        try
        {
            var orhan = db.GetPlayers().Single(p => p.DisplayName == "Orhan");
            var me = service.CompleteOnboarding(orhan.Id, "Orhan Bey", "", chests: null, matches: null, kills: null);
            Assert.Equal(orhan.Id, me.Id);
            Assert.Equal("Orhan Bey", me.DisplayName);
            Assert.Equal(529, service.Snapshot.Self!.TotalChests); // değer girilmedi: geçmiş korunur
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Hidden_player_is_kept_but_flagged()
    {
        using var db = TestData.SeededDb();
        var ohxo = db.GetPlayers().Single(p => p.DisplayName == "Ohxo");
        db.SetHidden(ohxo.Id, true, "ekipten ayrıldı");
        Assert.True(db.GetPlayers().Single(p => p.Id == ohxo.Id).Hidden);
        Assert.Equal(4, db.GetPlayers().Count);
        Assert.Contains(db.GetEditLog(), e => e.Field == "hidden" && e.EntityKey == "Ohxo");
    }

    [Fact]
    public void Import_matches_players_by_epic_name_when_display_names_differ()
    {
        using var source = TestData.SeededDb();
        var export = ImportExportService.BuildExport(source, true, "x");

        using var target = Database.OpenInMemory();
        target.AddPlayer("Orhan Bey", "OrhanTR"); // aynı kişi, farklı görünen ad
        var report = ImportExportService.Import(target, export, "x.json");

        var names = target.GetPlayers().Select(p => p.DisplayName).ToList();
        Assert.Equal(4, names.Count);
        Assert.Contains("Orhan Bey", names);
        Assert.DoesNotContain("Orhan", names);
        Assert.Equal(3, report.NewPlayers);
    }
}
