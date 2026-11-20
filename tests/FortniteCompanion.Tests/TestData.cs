using FortniteCompanion.Core.Data;
using FortniteCompanion.Core.Replay;

namespace FortniteCompanion.Tests;

internal static class TestData
{
    public static readonly DateTime Cutoff = new(2026, 11, 21, 0, 0, 0, DateTimeKind.Utc);

    public static Database SeededDb()
    {
        var db = Database.OpenInMemory();
        db.SeedDefaults(Cutoff);
        var players = db.GetPlayers();
        db.SetEpicNames(players.Single(p => p.DisplayName == "Ryder").Id, "RyderFN", "test");
        db.SetEpicNames(players.Single(p => p.DisplayName == "Orhan").Id, "OrhanTR", "test");
        db.SetEpicNames(players.Single(p => p.DisplayName == "Ohxo").Id, "ohxo", "test");
        db.SetEpicNames(players.Single(p => p.DisplayName == "Doğukan").Id, "Dogukan07", "test");
        return db;
    }

    public static ReplayPlayerResult Player(string name, int chests, bool recorder = false, int? kills = null, int ammo = 0) => new()
    {
        Name = name,
        IsRecorder = recorder,
        ChestsTotal = chests,
        ChestsByKind = chests > 0 ? new Dictionary<string, int> { [ChestKinds.Normal] = chests } : new(),
        AmmoBoxes = ammo,
        Kills = kills,
    };

    /// <summary>Kaydeden oyuncu ilk sırada olacak şekilde bir maç sonucu oluşturur.</summary>
    public static ReplayMatchResult Match(string session, DateTime startUtc, params ReplayPlayerResult[] players) => new()
    {
        SessionId = session,
        FileName = session + ".replay",
        MatchStartUtc = startUtc,
        DurationSeconds = 1500,
        Playlist = "Playlist_NoBuildBR_Squad",
        AnalyzerVersion = ReplayAnalyzer.CurrentVersion,
        RecorderFound = true,
        RecorderName = players.First(p => p.IsRecorder).Name,
        Players = players.ToList(),
    };
}
