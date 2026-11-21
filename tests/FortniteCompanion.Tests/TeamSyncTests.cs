using FortniteCompanion.Core.Data;
using FortniteCompanion.Core.Services;

namespace FortniteCompanion.Tests;

public class TeamSyncTests
{
    private static List<PlayerStats> Stats(Database db) =>
        StatsService.Compute(db.GetPlayers(), db.GetBaselines(), db.GetMatches(), db.GetMatchPlayers(),
            new StatsOptions { NowUtc = TestData.Cutoff.AddDays(2), Zone = TimeZoneInfo.Utc });

    [Fact]
    public void Device_identity_is_generated_once()
    {
        var s = new AppSettings();
        Assert.True(TeamSync.EnsureDeviceIdentity(s));
        var id = s.DeviceId;
        Assert.Equal(32, id!.Length);
        Assert.False(string.IsNullOrEmpty(s.DeviceSecret));
        Assert.False(TeamSync.EnsureDeviceIdentity(s));
        Assert.Equal(id, s.DeviceId);
    }

    [Fact]
    public void Payload_contains_only_own_counted_matches()
    {
        using var db = TestData.SeededDb();
        db.UpsertReplayMatch(TestData.Match("A", TestData.Cutoff.AddDays(1),
            TestData.Player("OrhanTR", 21, recorder: true, kills: 4),
            TestData.Player("RyderFN", 9)));
        db.UpsertReplayMatch(TestData.Match("OLD", TestData.Cutoff.AddDays(-1), TestData.Player("OrhanTR", 30, recorder: true)));

        var settings = new AppSettings();
        TeamSync.EnsureDeviceIdentity(settings);
        var orhan = Stats(db).Single(s => s.Player.DisplayName == "Orhan");
        var payload = TeamSync.BuildPayload(settings, orhan, "0.5.0");

        Assert.Equal("Orhan", payload.DisplayName);
        Assert.Equal(529 + 21, payload.TotalChests);
        Assert.Single(payload.Matches); // örtüşen (sayılmayan) maç gönderilmez
        Assert.Equal("A", payload.Matches[0].SessionId);
        Assert.Equal(settings.DeviceId, payload.DeviceId);
    }

    [Fact]
    public void Board_prefers_server_numbers_for_others_but_local_for_self()
    {
        using var db = TestData.SeededDb();
        var stats = Stats(db);
        var orhanId = db.GetPlayers().Single(p => p.DisplayName == "Orhan").Id;
        var remote = new List<RemotePlayer>
        {
            new() { DeviceId = "r1", DisplayName = "Ryder (PC)", EpicNames = "RyderFN", TotalChests = 1200, TotalMatches = 60 },
            new() { DeviceId = "r2", DisplayName = "Orhan", EpicNames = "OrhanTR", TotalChests = 5 }, // kendi eski cihazı
            new() { DeviceId = "r3", DisplayName = "YeniKanka", EpicNames = "Yeni123", TotalChests = 77 },
        };

        var board = TeamBoard.Build(stats, remote, orhanId, includeHidden: false);

        Assert.Equal(5, board.Count); // 4 yerel + 1 yalnızca sunucuda
        var ryder = board.Single(e => e.Name == "Ryder");
        Assert.True(ryder.FromServer);
        Assert.Equal(1200, ryder.TotalChests);
        var me = board.Single(e => e.IsSelf);
        Assert.False(me.FromServer);
        Assert.Equal(529, me.TotalChests);
        var newcomer = board.Single(e => e.Name == "YeniKanka");
        Assert.Null(newcomer.LocalPlayerId);
        Assert.Equal("Ryder", board[0].Name); // sıralama: en çok sandık önce
    }

    [Fact]
    public void Board_hides_hidden_local_players_unless_requested()
    {
        using var db = TestData.SeededDb();
        var ohxo = db.GetPlayers().Single(p => p.DisplayName == "Ohxo");
        db.SetHidden(ohxo.Id, true, "test");
        var stats = Stats(db);
        Assert.DoesNotContain(TeamBoard.Build(stats, Array.Empty<RemotePlayer>(), null, false), e => e.Name == "Ohxo");
        Assert.Contains(TeamBoard.Build(stats, Array.Empty<RemotePlayer>(), null, true), e => e.Name == "Ohxo");
    }
}
