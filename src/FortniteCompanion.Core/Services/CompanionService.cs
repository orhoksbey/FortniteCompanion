using System.Globalization;
using FortniteCompanion.Core.Data;

namespace FortniteCompanion.Core.Services;

/// <summary>Arayüzün ve overlay'in gösterdiği anlık durum.</summary>
public sealed class CompanionSnapshot
{
    public List<PlayerStats> Players { get; init; } = new();
    public PlayerStats? Self { get; init; }
    public LiveSession? Live { get; init; }
    public bool InMatch { get; init; }
    public WatcherStatus? Watcher { get; init; }
    public List<UnmatchedName> Unmatched { get; init; } = new();
    public List<MatchRow> Matches { get; init; } = new();
    public List<MatchPlayerRow> Rows { get; init; } = new();

    /// <summary>Ekip sunucusundan gelen oyuncular (kendi cihazım hariç).</summary>
    public IReadOnlyList<RemotePlayer> Remote { get; init; } = Array.Empty<RemotePlayer>();

    public TeamSyncStatus? SyncStatus { get; init; }

    /// <summary>Son maçta kaydeden oyuncunun takımının toplam sandığı (takım arkadaşları dahil).</summary>
    public int? LastMatchTeamChests { get; init; }

    public DateTime ComputedUtc { get; init; } = DateTime.UtcNow;
}

public enum NotificationKind
{
    MatchEnd,
    Record,
    Warning,
    Info,
}

/// <summary>
/// Veri tabanı, replay izleyici ve istatistik hesaplamasını bir araya getirir. WPF'e bağımlı değildir;
/// olaylar arka plan iş parçacığından gelebilir, arayüz kendi iş parçacığına aktarır.
/// </summary>
public sealed class CompanionService : IDisposable
{
    private readonly object _sync = new();
    private CompanionSnapshot _snapshot = new();
    private WatcherStatus? _watcherStatus;
    private bool _inMatch;

    public Database Db { get; }
    public AppSettings Settings { get; }
    public string SettingsPath { get; }
    public ReplayWatcher Watcher { get; }
    public TeamSync Sync { get; }
    private Timer? _syncTimer;

    public event Action<CompanionSnapshot>? SnapshotChanged;
    public event Action<NotificationKind, string, string>? Notify;

    public CompanionService(Database db, AppSettings settings, string settingsPath)
    {
        Db = db;
        Settings = settings;
        SettingsPath = settingsPath;

        // Sabit oyuncu listesi yok: herkes ilk açılışta kendini tanıtır ("Sen kimsin?"), ekip arkadaşları
        // JSON ekip dosyasıyla gelir. Geçersiz kalmış "ben" seçimi temizlenir.
        if (Settings.SelfPlayerId != null && Db.GetPlayers().All(p => p.Id != Settings.SelfPlayerId))
        {
            Settings.SelfPlayerId = null;
            SaveSettings();
        }

        Sync = new TeamSync(Settings);
        if (TeamSync.EnsureDeviceIdentity(Settings))
        {
            SaveSettings();
        }

        Watcher = new ReplayWatcher(Db, string.IsNullOrWhiteSpace(Settings.ReplayFolder) ? AppPaths.DefaultReplayFolder : Settings.ReplayFolder!);
        Watcher.StatusChanged += s =>
        {
            _watcherStatus = s;
            PublishLight();
        };
        Watcher.InMatchChanged += m =>
        {
            _inMatch = m;
            PublishLight();
        };
        Watcher.MatchRecorded += OnMatchRecorded;
        Watcher.FileSkipped += (file, reason) => Notify?.Invoke(NotificationKind.Warning, "Replay atlandı", $"{file}: {reason}");
    }

    public CompanionSnapshot Snapshot
    {
        get { lock (_sync) { return _snapshot; } }
    }

    public void Start()
    {
        Refresh();
        Watcher.Start();
        // Ekip eşitlemesi: açılıştan 6 sn sonra, sonra 10 dakikada bir.
        _syncTimer = new Timer(_ => _ = SyncTeamAsync(force: false), null, TimeSpan.FromSeconds(6), TimeSpan.FromMinutes(10));
    }

    /// <summary>Kendi özetini ekip sunucusuna gönderir ve sıralamayı alır (kapalı/ayarsızsa hiçbir şey yapmaz).</summary>
    public async Task SyncTeamAsync(bool force)
    {
        try
        {
            await Sync.SyncAsync(Snapshot.Self, AutoUpdater.CurrentVersion.ToString(3), force).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Error("Ekip eşitlemesi başarısız", ex);
        }
        PublishLight();
    }

    public void SaveSettings() => Settings.Save(SettingsPath);

    public Player? SelfPlayer => Db.GetPlayers().FirstOrDefault(p => p.Id == Settings.SelfPlayerId);

    /// <summary>Tüm istatistikleri yeniden hesaplar ve yayınlar.</summary>
    public CompanionSnapshot Refresh()
    {
        var players = Db.GetPlayers();
        var baselines = Db.GetBaselines();
        var matches = Db.GetMatches();
        var rows = Db.GetMatchPlayers();
        var stats = StatsService.Compute(players, baselines, matches, rows, Settings.ToStatsOptions());
        var self = stats.FirstOrDefault(s => s.Player.Id == Settings.SelfPlayerId);
        var live = Settings.SelfPlayerId is long sid ? Db.GetOpenLiveSession(sid) : null;

        int? teamChests = null;
        if (self?.LastMatch is { } last)
        {
            var sessionRows = rows.Where(r => r.SessionId == last.SessionId).ToList();
            var observer = last.Observer;
            teamChests = sessionRows.Where(r => string.Equals(r.Observer, observer, StringComparison.OrdinalIgnoreCase)).Sum(r => r.Chests);
        }

        var snapshot = new CompanionSnapshot
        {
            Players = stats,
            Self = self,
            Live = live,
            InMatch = _inMatch,
            Watcher = _watcherStatus,
            Unmatched = StatsService.Unmatched(players, rows),
            Matches = matches,
            Rows = rows,
            LastMatchTeamChests = teamChests,
            Remote = Sync.Remote,
            SyncStatus = Sync.Status,
        };

        lock (_sync)
        {
            _snapshot = snapshot;
        }
        SnapshotChanged?.Invoke(snapshot);
        return snapshot;
    }

    /// <summary>İzleyici durumunu, istatistikleri yeniden hesaplamadan yayınlar.</summary>
    private void PublishLight()
    {
        CompanionSnapshot s;
        lock (_sync)
        {
            var old = _snapshot;
            s = new CompanionSnapshot
            {
                Players = old.Players,
                Self = old.Self,
                Live = old.Live,
                InMatch = _inMatch,
                Watcher = _watcherStatus,
                Unmatched = old.Unmatched,
                Matches = old.Matches,
                Rows = old.Rows,
                LastMatchTeamChests = old.LastMatchTeamChests,
                Remote = Sync.Remote,
                SyncStatus = Sync.Status,
            };
            _snapshot = s;
        }
        SnapshotChanged?.Invoke(s);
    }

    // ------------------------------------------------------------------ ilk açılış

    /// <summary>
    /// "Sen kimsin?" kurulumunu kaydeder: var olan bir oyuncuyu seçer ya da yenisini oluşturur, Epic adını ve
    /// (girildiyse) oyundaki sayaçtan alınan başlangıç değerlerini yazar. Başlangıç tarihi şu andır.
    /// </summary>
    public Player CompleteOnboarding(long? existingPlayerId, string displayName, string epicNames, int? chests, int? matches, int? kills)
    {
        displayName = displayName.Trim();
        var players = Db.GetPlayers();
        Player player;
        if (existingPlayerId is long id && players.FirstOrDefault(p => p.Id == id) is { } existing)
        {
            player = existing;
            if (!string.Equals(existing.DisplayName, displayName, StringComparison.Ordinal) && displayName.Length > 0)
            {
                Db.RenamePlayer(existing.Id, displayName, "İlk açılış kurulumu");
            }
        }
        else
        {
            player = players.FirstOrDefault(p => string.Equals(p.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
                     ?? Db.AddPlayer(displayName, epicNames);
        }

        if (!string.IsNullOrWhiteSpace(epicNames))
        {
            var merged = string.Join(", ", player.EpicNameList.Concat(epicNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Distinct(StringComparer.OrdinalIgnoreCase));
            Db.SetEpicNames(player.Id, merged, "İlk açılış kurulumu");
        }

        if (chests != null || matches != null || kills != null)
        {
            var b = Db.GetBaselines().FirstOrDefault(x => x.PlayerId == player.Id);
            var newChests = chests ?? b?.Chests ?? 0;
            var newMatches = matches ?? b?.Matches ?? 0;
            var newKills = kills ?? b?.Kills ?? 0;
            if (b == null || b.Chests != newChests || b.Matches != newMatches || b.Kills != newKills)
            {
                Db.UpdateBaseline(player.Id, newMatches, newChests, newKills, DateTime.UtcNow,
                    "Oyundaki sayaçtan (ilk açılış)", "İlk açılış kurulumu");
            }
        }

        Db.SetHidden(player.Id, false, "İlk açılış kurulumu");
        Settings.SelfPlayerId = player.Id;
        Settings.OnboardingDone = true;
        SaveSettings();
        Refresh();
        _ = SyncTeamAsync(force: true);
        return Db.GetPlayers().First(p => p.Id == player.Id);
    }

    // ------------------------------------------------------------------ maç sonu

    private void OnMatchRecorded(MatchRecordedEventArgs e)
    {
        try
        {
            var previousBest = Snapshot.Self?.BestMatch?.Chests ?? 0;
            var players = Db.GetPlayers();
            var recorder = e.Result.Players.FirstOrDefault(p => p.IsRecorder);

            // Bu bilgisayardaki replay'lerin sahibi henüz eşleşmemişse, kendi oyuncuna otomatik bağla.
            if (recorder != null && !string.IsNullOrWhiteSpace(recorder.Name) && !players.Any(p => p.Owns(recorder.Name))
                && SelfPlayer is { } self && !self.EpicNameList.Any())
            {
                Db.SetEpicNames(self.Id, recorder.Name, "Bu bilgisayardaki replay sahibi otomatik eşleştirildi");
                players = Db.GetPlayers();
            }

            if (!e.Outcome.AlreadyCounted)
            {
                SupersedeManualMatches(e, players);
                VerifyLiveSession(e, players);
            }

            var snap = Refresh();
            if (!e.Outcome.AlreadyCounted)
            {
                _ = SyncTeamAsync(force: true);
            }

            if (e.Outcome.AlreadyCounted || e.Result.MatchStartUtc is DateTime st && DateTime.UtcNow - st > TimeSpan.FromHours(3))
            {
                return; // eski replay'ler (ilk tarama) için bildirim yok
            }

            var selfRow = e.Result.Players.FirstOrDefault(p => SelfPlayer?.Owns(p.Name) == true);
            if (selfRow != null && Settings.NotifyMatchEnd)
            {
                var mates = e.Result.Players.Where(p => !ReferenceEquals(p, selfRow) && p.ChestsTotal > 0)
                    .Select(p => $"{p.Name}: {p.ChestsTotal}").ToList();
                var text = $"Bu maç {selfRow.ChestsTotal} sandık açtın."
                           + (selfRow.Kills is int k ? $" Kill: {k}." : "")
                           + (mates.Count > 0 ? " Takım: " + string.Join(", ", mates) + " (kısmi)." : "");
                Notify?.Invoke(NotificationKind.MatchEnd, "Maç bitti", text);
            }

            if (selfRow != null && Settings.NotifyRecord && selfRow.ChestsTotal > previousBest && previousBest > 0
                && snap.Self?.BestMatch?.SessionId == e.Result.SessionId)
            {
                Notify?.Invoke(NotificationKind.Record, "Yeni kişisel rekor!",
                    $"Tek maçta {selfRow.ChestsTotal} sandık (önceki rekor {previousBest}).");
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Maç sonu işlemi başarısız", ex);
        }
    }

    /// <summary>Replay'i gelen maçla zaman olarak örtüşen manuel maçları hariç tutar (mükerrer sayım olmaz).</summary>
    private void SupersedeManualMatches(MatchRecordedEventArgs e, List<Player> players)
    {
        if (e.Result.MatchStartUtc is not DateTime start)
        {
            return;
        }
        var end = start.AddSeconds(e.Result.DurationSeconds ?? 1800).AddMinutes(10);
        var from = start.AddMinutes(-10);
        var involved = players.Where(p => e.Result.Players.Any(r => p.Owns(r.Name))).ToList();
        if (involved.Count == 0)
        {
            return;
        }

        var rows = Db.GetMatchPlayers();
        foreach (var m in Db.GetMatches().Where(m => m.Source == DataSource.Manual && !m.Excluded && m.StartedUtc is DateTime s && s >= from && s <= end))
        {
            var owners = rows.Where(r => r.SessionId == m.SessionId).ToList();
            if (owners.Any(r => involved.Any(p => p.Owns(r.PlayerName))))
            {
                Db.SetExcluded(m.SessionId, true, $"Replay ile doğrulandı ({e.Result.SessionId}); manuel kayıt toplamdan çıkarıldı");
            }
        }
    }

    private void VerifyLiveSession(MatchRecordedEventArgs e, List<Player> players)
    {
        var self = players.FirstOrDefault(p => p.Id == Settings.SelfPlayerId);
        if (self == null)
        {
            return;
        }
        var row = e.Result.Players.FirstOrDefault(p => self.Owns(p.Name));
        var live = Db.GetOpenLiveSession(self.Id);
        if (row == null || live == null)
        {
            return;
        }

        // Canlı oturum bu maç sırasında başladıysa replay ile doğrula; manuel sayı toplama eklenmez.
        var start = e.Result.MatchStartUtc ?? DateTime.UtcNow.AddHours(-1);
        if (live.StartedUtc >= start.AddMinutes(-15))
        {
            Db.CloseLiveSession(live.Id, "verified", e.Result.SessionId);
            Db.Log("live", live.Id.ToString(CultureInfo.InvariantCulture), "verified",
                live.ManualCount.ToString(CultureInfo.InvariantCulture), row.ChestsTotal.ToString(CultureInfo.InvariantCulture),
                "Manuel canlı sayaç replay ile doğrulandı (manuel sayı toplama eklenmedi)");
        }
    }

    // ------------------------------------------------------------------ manuel canlı sayaç

    public int ManualIncrement(int delta)
    {
        if (!Settings.ManualLiveMode || SelfPlayer is not { } self)
        {
            return 0;
        }
        var live = Db.GetOpenLiveSession(self.Id);
        if (live == null)
        {
            if (delta <= 0)
            {
                return 0;
            }
            live = Db.StartLiveSession(self.Id);
        }
        var count = Math.Max(0, live.ManualCount + delta);
        Db.SetLiveCount(live.Id, count);
        Refresh();
        return count;
    }

    /// <summary>Replay gelmeyen (ör. replay kaydı kapalı) canlı oturumu manuel maç olarak kaydeder.</summary>
    public string? SaveLiveAsManual(string reason)
    {
        if (SelfPlayer is not { } self || Db.GetOpenLiveSession(self.Id) is not { } live)
        {
            return null;
        }
        if (live.ManualCount <= 0)
        {
            Db.CloseLiveSession(live.Id, "discarded", null);
            Refresh();
            return null;
        }
        var duration = (int)Math.Max(0, (DateTime.UtcNow - live.StartedUtc).TotalSeconds);
        var session = Db.AddManualMatch(self, live.StartedUtc, duration, live.ManualCount, null, "Canlı manuel sayaç", reason);
        Db.CloseLiveSession(live.Id, "saved_manual", session);
        Refresh();
        return session;
    }

    public void DiscardLive()
    {
        if (SelfPlayer is { } self && Db.GetOpenLiveSession(self.Id) is { } live)
        {
            Db.CloseLiveSession(live.Id, "discarded", null);
            Refresh();
        }
    }

    public void Dispose()
    {
        _syncTimer?.Dispose();
        Watcher.Dispose();
    }
}
