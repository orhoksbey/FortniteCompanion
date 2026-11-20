using System.Text;
using FortniteCompanion.Core.Data;
using FortniteCompanion.Core.Replay;
using FortniteCompanion.Core.Services;

namespace FortniteCompanion.Tests;

public class ChestKindsTests
{
    [Theory]
    [InlineData("Tiered_Chest_Athena", ChestKinds.Normal)]
    [InlineData("AlwaysSpawn_RareChest", ChestKinds.Rare)]
    [InlineData("B_BuriedChest", ChestKinds.Buried)]
    [InlineData("B_TieredChest_Relics", ChestKinds.Special)]
    [InlineData("Tiered_Ammo_Athena", ChestKinds.AmmoBox)]
    [InlineData("B_FoodBox_Produce", ChestKinds.FoodBox)]
    [InlineData("Cooler_Container", ChestKinds.OtherContainer)]
    [InlineData("SomeNewSeasonChest_X", ChestKinds.Other)]
    public void Classifies_blueprints(string blueprint, string expected)
    {
        Assert.Equal(expected, ChestKinds.Classify(blueprint));
    }

    [Fact]
    public void Ammo_boxes_and_containers_are_not_chests()
    {
        Assert.False(ChestKinds.IsChest(ChestKinds.AmmoBox));
        Assert.False(ChestKinds.IsChest(ChestKinds.FoodBox));
        Assert.False(ChestKinds.IsChest(ChestKinds.OtherContainer));
        Assert.True(ChestKinds.IsChest(ChestKinds.Rare));
    }
}

public class HeaderProbeTests
{
    /// <summary>UE yerel replay başlığının (FLocalFileReplayInfo) en küçük örneğini üretir.</summary>
    private static byte[] Header(bool live, bool encrypted, bool keyWritten)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        w.Write(0x1CA2E27Fu); // magic
        w.Write(7u);          // file version (custom versions var)
        w.Write(0);           // custom version sayısı
        w.Write(1000u);       // LengthInMS
        w.Write(2u);          // NetworkVersion
        w.Write(3u);          // Changelist
        var name = Encoding.ASCII.GetBytes("Test\0");
        w.Write(name.Length); // FString (ASCII)
        w.Write(name);
        w.Write(live ? 1u : 0u);
        w.Write(0L);          // timestamp
        w.Write(1u);          // compressed
        w.Write(encrypted ? 1u : 0u);
        if (encrypted)
        {
            w.Write(32);
            w.Write(keyWritten ? Enumerable.Repeat((byte)7, 32).ToArray() : new byte[32]);
        }
        w.Flush();
        return ms.ToArray();
    }

    [Fact]
    public void Detects_live_recording_and_missing_key()
    {
        using var live = new MemoryStream(Header(live: true, encrypted: true, keyWritten: false));
        Assert.True(ReplayHeaderProbe.IsLive(live));
        live.Position = 0;
        Assert.True(ReplayHeaderProbe.KeyMissing(live));
    }

    [Fact]
    public void Detects_finished_replay_with_key()
    {
        using var done = new MemoryStream(Header(live: false, encrypted: true, keyWritten: true));
        Assert.False(ReplayHeaderProbe.IsLive(done));
        done.Position = 0;
        Assert.False(ReplayHeaderProbe.KeyMissing(done));
    }

    [Fact]
    public void Garbage_is_not_a_replay()
    {
        using var junk = new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        Assert.Null(ReplayHeaderProbe.IsLive(junk));
    }
}

public class ImportExportTests
{
    [Fact]
    public void Export_then_import_into_friend_db_merges_without_duplicates()
    {
        using var mine = TestData.SeededDb();
        mine.UpsertReplayMatch(TestData.Match("S1", TestData.Cutoff.AddDays(1),
            TestData.Player("RyderFN", 20, recorder: true),
            TestData.Player("OrhanTR", 6),
            TestData.Player("Stranger123", 4)));

        var path = Path.Combine(Path.GetTempPath(), $"fc-test-{Guid.NewGuid():N}.json");
        try
        {
            ImportExportService.ExportJson(mine, path, hideUnknownNames: true, exportedBy: "Ryder");
            var file = ImportExportService.ReadJson(path);
            Assert.DoesNotContain(file.Matches.SelectMany(m => m.Rows), r => r.PlayerName == "Stranger123");

            using var friend = TestData.SeededDb();
            var r1 = ImportExportService.Import(friend, file, "ryder.json");
            var r2 = ImportExportService.Import(friend, file, "ryder.json");
            Assert.Equal(1, r1.NewMatches);
            Assert.Equal(2, r1.NewRows);
            Assert.Equal(0, r2.NewMatches);
            Assert.Equal(0, r2.NewRows);

            var stats = StatsService.Compute(friend.GetPlayers(), friend.GetBaselines(), friend.GetMatches(), friend.GetMatchPlayers(),
                new StatsOptions { NowUtc = TestData.Cutoff.AddDays(2), Zone = TimeZoneInfo.Utc });
            Assert.Equal(495 + 20, stats.Single(s => s.Player.DisplayName == "Ryder").TotalChests);
            Assert.Equal(529 + 6, stats.Single(s => s.Player.DisplayName == "Orhan").TotalChests);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Newer_baseline_in_import_replaces_untouched_seed()
    {
        using var mine = TestData.SeededDb();
        var orhan = mine.GetPlayers().Single(p => p.DisplayName == "Orhan");
        mine.UpdateBaseline(orhan.Id, 46, 840, 176, TestData.Cutoff, null, "oyundaki sayaçtan");
        var export = ImportExportService.BuildExport(mine, true, "x");

        using var friend = TestData.SeededDb(); // yeni kurulum: dokunulmamış başlangıç değerleri
        var report = ImportExportService.Import(friend, export, "x.json");
        var friendOrhan = friend.GetPlayers().Single(p => p.DisplayName == "Orhan");
        Assert.Equal(1, report.BaselinesUpdated);
        Assert.Equal(840, friend.GetBaselines().Single(b => b.PlayerId == friendOrhan.Id).Chests);
        Assert.Contains(friend.GetEditLog(), e => e.Entity == "baseline" && e.EntityKey == "Orhan" && e.NewValue == "840");
    }

    [Fact]
    public void Older_baseline_in_import_only_warns()
    {
        using var old = TestData.SeededDb();
        var export = ImportExportService.BuildExport(old, true, "x"); // dokunulmamış (eski) değerler

        using var mine = TestData.SeededDb();
        var orhan = mine.GetPlayers().Single(p => p.DisplayName == "Orhan");
        mine.UpdateBaseline(orhan.Id, 46, 840, 176, TestData.Cutoff, null, "oyundaki sayaçtan");
        var report = ImportExportService.Import(mine, export, "old.json");
        Assert.Equal(0, report.BaselinesUpdated);
        Assert.Contains(report.Warnings, w => w.Contains("Orhan"));
        Assert.Equal(840, mine.GetBaselines().Single(b => b.PlayerId == orhan.Id).Chests);
    }

    [Fact]
    public void Csv_export_writes_header_and_baseline_rows()
    {
        using var db = TestData.SeededDb();
        var stats = StatsService.Compute(db.GetPlayers(), db.GetBaselines(), db.GetMatches(), db.GetMatchPlayers(), new StatsOptions());
        var path = Path.Combine(Path.GetTempPath(), $"fc-test-{Guid.NewGuid():N}.csv");
        try
        {
            ImportExportService.ExportCsv(stats, path);
            var lines = File.ReadAllLines(path);
            Assert.StartsWith("Oyuncu;", lines[0]);
            Assert.Contains(lines, l => l.StartsWith("Doğukan;") && l.Contains(";997;"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class BackupTests
{
    [Fact]
    public void Backup_creates_a_readable_copy()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fc-backup-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var db = TestData.SeededDb();
            var path = BackupService.RunNow(db, dir, keep: 3);
            Assert.True(File.Exists(path));
            using var copy = Database.OpenFile(path);
            Assert.Equal(4, copy.GetPlayers().Count);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}

public class SettingsTests
{
    [Fact]
    public void Settings_roundtrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fc-settings-{Guid.NewGuid():N}.json");
        try
        {
            var s = new AppSettings { OverlayCorner = OverlayCorner.BottomLeft, OverlayScale = 1.3, HotkeyToggleOverlay = "Ctrl+Shift+F9" };
            s.Save(path);
            var back = AppSettings.Load(path);
            Assert.Equal(OverlayCorner.BottomLeft, back.OverlayCorner);
            Assert.Equal(1.3, back.OverlayScale);
            Assert.Equal("Ctrl+Shift+F9", back.HotkeyToggleOverlay);
            Assert.False(back.ExperimentalScreenAnalysis);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
