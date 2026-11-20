using FortniteCompanion.Core.Data;
using FortniteCompanion.Core.Services;

namespace FortniteCompanion.Tests;

public class StatsTests
{
    private static StatsOptions Options(DateTime? now = null) => new()
    {
        NowUtc = now ?? new DateTime(2026, 11, 25, 20, 0, 0, DateTimeKind.Utc),
        Zone = TimeZoneInfo.Utc,
        SeasonStartUtc = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    private static List<PlayerStats> Compute(Database db, StatsOptions? o = null) =>
        StatsService.Compute(db.GetPlayers(), db.GetBaselines(), db.GetMatches(), db.GetMatchPlayers(), o ?? Options());

    [Fact]
    public void Seed_contains_the_four_players_with_exact_historical_values()
    {
        using var db = TestData.SeededDb();
        var stats = Compute(db);

        var expected = new Dictionary<string, (int M, int C, int K)>
        {
            ["Ryder"] = (47, 495, 222),
            ["Orhan"] = (46, 529, 176),
            ["Ohxo"] = (68, 644, 217),
            ["Doğukan"] = (68, 997, 163),
        };
        Assert.Equal(4, stats.Count);
        foreach (var s in stats)
        {
            var e = expected[s.Player.DisplayName];
            Assert.Equal(e.M, s.TotalMatches);
            Assert.Equal(e.C, s.TotalChests);
            Assert.Equal(e.K, s.TotalKills);
            Assert.Equal(0, s.TrackedMatches);
        }
    }

    [Fact]
    public void Seed_is_not_applied_twice()
    {
        using var db = TestData.SeededDb();
        Assert.False(db.SeedDefaults(DateTime.UtcNow));
        Assert.Equal(4, db.GetPlayers().Count);
    }

    [Fact]
    public void Total_is_baseline_plus_new_unique_matches()
    {
        using var db = TestData.SeededDb();
        var t = TestData.Cutoff.AddDays(2);
        db.UpsertReplayMatch(TestData.Match("S1", t, TestData.Player("RyderFN", 24, recorder: true, kills: 3)));
        db.UpsertReplayMatch(TestData.Match("S2", t.AddHours(1), TestData.Player("RyderFN", 10, recorder: true, kills: 1)));

        var ryder = Compute(db).Single(s => s.Player.DisplayName == "Ryder");
        Assert.Equal(495 + 34, ryder.TotalChests);
        Assert.Equal(47 + 2, ryder.TotalMatches);
        Assert.Equal(222 + 4, ryder.TotalKills);
        Assert.Equal(34, ryder.ReplayVerifiedChests);
        Assert.Equal(24, ryder.BestMatch!.Chests);
        Assert.Equal(10, ryder.LastMatch!.Chests);
    }

    [Fact]
    public void Same_replay_twice_is_not_counted_twice()
    {
        using var db = TestData.SeededDb();
        var m = TestData.Match("S1", TestData.Cutoff.AddDays(1), TestData.Player("RyderFN", 24, recorder: true));
        var first = db.UpsertReplayMatch(m);
        var second = db.UpsertReplayMatch(m);

        Assert.True(first.NewMatch);
        Assert.True(second.AlreadyCounted);
        Assert.Equal(495 + 24, Compute(db).Single(s => s.Player.DisplayName == "Ryder").TotalChests);
    }

    [Fact]
    public void Teammate_chests_are_partial_and_own_replay_wins_when_both_exist()
    {
        using var db = TestData.SeededDb();
        var t = TestData.Cutoff.AddDays(1);

        // Ryder'ın replay'i: Ryder 20 (tam), Orhan 7 (kısmi: Ryder'ın yakınında açtıkları)
        db.UpsertReplayMatch(TestData.Match("SQUAD1", t,
            TestData.Player("RyderFN", 20, recorder: true, kills: 2),
            TestData.Player("OrhanTR", 7, kills: 1)));

        var orhan = Compute(db).Single(s => s.Player.DisplayName == "Orhan");
        Assert.Equal(529 + 7, orhan.TotalChests);
        Assert.Equal(7, orhan.PartialChests);
        Assert.Equal(DataSource.ReplayPartial, orhan.Matches.Single().Source);

        // Orhan'ın kendi replay'i içe aktarılınca: Orhan 15 (tam). Aynı maç, iki kez sayılmaz.
        db.UpsertReplayMatch(TestData.Match("SQUAD1", t,
            TestData.Player("OrhanTR", 15, recorder: true, kills: 1),
            TestData.Player("RyderFN", 18)));

        var stats = Compute(db);
        orhan = stats.Single(s => s.Player.DisplayName == "Orhan");
        var ryder = stats.Single(s => s.Player.DisplayName == "Ryder");
        Assert.Equal(529 + 15, orhan.TotalChests);
        Assert.Equal(46 + 1, orhan.TotalMatches);
        Assert.Equal(DataSource.Replay, orhan.Matches.Single().Source);
        Assert.Equal(495 + 20, ryder.TotalChests); // Ryder'ın kendi replay'i (20), Orhan'ın gördüğü 18 değil
        Assert.Equal(2, ryder.Matches.Single().ObserverCount);
    }

    [Fact]
    public void Match_before_cutoff_is_flagged_and_excluded_until_user_overrides()
    {
        using var db = TestData.SeededDb();
        db.UpsertReplayMatch(TestData.Match("OLD", TestData.Cutoff.AddDays(-1), TestData.Player("RyderFN", 30, recorder: true)));

        var ryder = Compute(db).Single(s => s.Player.DisplayName == "Ryder");
        Assert.Equal(495, ryder.TotalChests);
        Assert.Equal(1, ryder.OverlapCount);
        Assert.False(ryder.Matches.Single().Counted);
        Assert.Contains("Örtüşme", ryder.Matches.Single().Flag);

        db.SetOverlapOverride("OLD", true, "test");
        ryder = Compute(db).Single(s => s.Player.DisplayName == "Ryder");
        Assert.Equal(495 + 30, ryder.TotalChests);
        Assert.Equal(0, ryder.OverlapCount);
    }

    [Fact]
    public void Match_right_after_cutoff_is_counted_but_suspicious()
    {
        using var db = TestData.SeededDb();
        db.UpsertReplayMatch(TestData.Match("NEAR", TestData.Cutoff.AddHours(2), TestData.Player("RyderFN", 12, recorder: true)));
        var ryder = Compute(db).Single(s => s.Player.DisplayName == "Ryder");
        Assert.Equal(495 + 12, ryder.TotalChests);
        Assert.Equal(1, ryder.SuspiciousCount);
    }

    [Fact]
    public void Manual_match_is_superseded_by_excluding_it()
    {
        using var db = TestData.SeededDb();
        var ryder = db.GetPlayers().Single(p => p.DisplayName == "Ryder");
        var manual = db.AddManualMatch(ryder, TestData.Cutoff.AddDays(1), 1200, 21, null, null, "test");

        Assert.Equal(495 + 21, Compute(db).Single(s => s.Player.DisplayName == "Ryder").TotalChests);
        Assert.Equal(DataSource.Manual, Compute(db).Single(s => s.Player.DisplayName == "Ryder").Matches.Single().Source);

        db.SetExcluded(manual, true, "Replay ile doğrulandı");
        db.UpsertReplayMatch(TestData.Match("REAL", TestData.Cutoff.AddDays(1).AddMinutes(1), TestData.Player("RyderFN", 23, recorder: true)));
        var s = Compute(db).Single(x => x.Player.DisplayName == "Ryder");
        Assert.Equal(495 + 23, s.TotalChests);
        Assert.Equal(47 + 1, s.TotalMatches);
    }

    [Fact]
    public void Matches_cannot_be_deleted_permanent_history()
    {
        using var db = TestData.SeededDb();
        db.UpsertReplayMatch(TestData.Match("S1", TestData.Cutoff.AddDays(1), TestData.Player("RyderFN", 5, recorder: true)));
        Assert.ThrowsAny<Exception>(() => db.ExecuteForTest("DELETE FROM matches;"));
        Assert.ThrowsAny<Exception>(() => db.ExecuteForTest("DELETE FROM edit_log;"));
        Assert.Single(db.GetMatches());
    }

    [Fact]
    public void Baseline_edit_is_logged_field_by_field()
    {
        using var db = TestData.SeededDb();
        var ryder = db.GetPlayers().Single(p => p.DisplayName == "Ryder");
        db.UpdateBaseline(ryder.Id, 48, 500, 222, TestData.Cutoff, "not", "Düzeltme");
        var log = db.GetEditLog().Where(e => e.Entity == "baseline" && e.EntityKey == "Ryder" && e.Reason == "Düzeltme").ToList();
        Assert.Contains(log, e => e.Field == "matches" && e.OldValue == "47" && e.NewValue == "48");
        Assert.Contains(log, e => e.Field == "chests" && e.OldValue == "495" && e.NewValue == "500");
        Assert.DoesNotContain(log, e => e.Field == "kills");
    }

    [Fact]
    public void Today_week_and_season_use_local_dates()
    {
        using var db = TestData.SeededDb();
        var now = new DateTime(2026, 11, 25, 20, 0, 0, DateTimeKind.Utc); // Çarşamba
        db.UpsertReplayMatch(TestData.Match("A", now.AddHours(-2), TestData.Player("RyderFN", 10, recorder: true)));
        db.UpsertReplayMatch(TestData.Match("B", now.AddDays(-1), TestData.Player("RyderFN", 5, recorder: true)));
        db.UpsertReplayMatch(TestData.Match("C", now.AddDays(-3.5), TestData.Player("RyderFN", 7, recorder: true))); // önceki hafta (Pazar)

        var ryder = StatsService.Compute(db.GetPlayers(), db.GetBaselines(), db.GetMatches(), db.GetMatchPlayers(), Options(now))
            .Single(s => s.Player.DisplayName == "Ryder");
        Assert.Equal(10, ryder.Today);
        Assert.Equal(15, ryder.ThisWeek);
        Assert.Equal(22, ryder.ThisSeason);
    }

    [Fact]
    public void Unmatched_names_are_reported_but_bots_are_not()
    {
        using var db = TestData.SeededDb();
        var m = TestData.Match("S", TestData.Cutoff.AddDays(1),
            TestData.Player("RyderFN", 3, recorder: true),
            TestData.Player("RandomGuy", 2));
        m.Players.Add(new FortniteCompanion.Core.Replay.ReplayPlayerResult { Name = "Bot", IsBot = true, ChestsTotal = 1 });
        db.UpsertReplayMatch(m);

        var unmatched = StatsService.Unmatched(db.GetPlayers(), db.GetMatchPlayers());
        Assert.Single(unmatched);
        Assert.Equal("RandomGuy", unmatched[0].Name);
    }

    [Fact]
    public void Legacy_v1_start_times_are_shifted_once_to_real_utc()
    {
        using var db = TestData.SeededDb();
        var stored = TestData.Cutoff.AddHours(-1); // 0.1.x bu maçı yerel saat farkı kadar erken yazmıştı
        var m = TestData.Match("LEGACY", stored, TestData.Player("RyderFN", 9, recorder: true));
        m.AnalyzerVersion = 1;
        db.UpsertReplayMatch(m);

        var expected = stored + TimeZoneInfo.Local.GetUtcOffset(stored);
        db.RepairLegacyStartTimes();
        db.RepairLegacyStartTimes(); // ikinci çağrı tekrar kaydırmamalı
        var row = db.GetMatch("LEGACY")!;
        Assert.Equal(expected, row.StartedUtc);
        Assert.Equal(2, row.AnalyzerVersion);
    }

    [Fact]
    public void Unspecified_kind_is_stored_as_utc_not_shifted()
    {
        using var db = TestData.SeededDb();
        var start = DateTime.SpecifyKind(TestData.Cutoff.AddDays(1), DateTimeKind.Unspecified);
        db.UpsertReplayMatch(TestData.Match("U", start, TestData.Player("RyderFN", 4, recorder: true)));
        Assert.Equal(DateTime.SpecifyKind(start, DateTimeKind.Utc), db.GetMatch("U")!.StartedUtc);
    }
}
