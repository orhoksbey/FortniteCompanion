using FortniteCompanion.Core.Data;

namespace FortniteCompanion.Core.Services;

public sealed class StatsOptions
{
    public DateTime SeasonStartUtc { get; set; } = new(2026, 11, 1, 4, 0, 0, DateTimeKind.Utc);
    public bool IncludeCustomMatches { get; set; }
    public TimeZoneInfo Zone { get; set; } = TimeZoneInfo.Local;
    public DateTime NowUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Başlangıç tarihinden sonraki bu süre içindeki maçlar "şüpheli örtüşme" olarak işaretlenir.</summary>
    public TimeSpan SuspiciousWindow { get; set; } = TimeSpan.FromHours(12);
}

/// <summary>Bir oyuncunun tek bir maçtaki (tekilleştirilmiş) değeri.</summary>
public sealed class PlayerMatchStat
{
    public string SessionId { get; set; } = "";
    public DateTime? StartedUtc { get; set; }
    public int Chests { get; set; }
    public int? Kills { get; set; }
    public DataSource Source { get; set; }
    public string? Playlist { get; set; }
    public int? DurationSeconds { get; set; }
    public int? Placement { get; set; }
    public Dictionary<string, int> ChestsByKind { get; set; } = new();
    public int AmmoBoxes { get; set; }
    public int OtherContainers { get; set; }

    /// <summary>Toplamlara dahil mi?</summary>
    public bool Counted { get; set; }

    /// <summary>Uyarı/açıklama (örtüşme, hariç tutma nedeni vb.).</summary>
    public string? Flag { get; set; }

    public bool Suspicious { get; set; }
    public bool BeforeCutoff { get; set; }
    public string Observer { get; set; } = "";

    /// <summary>Kaç farklı replay bu maçı gördü (tekilleştirme bilgisi).</summary>
    public int ObserverCount { get; set; }

    public string SourceLabel => DataSourceText.Label(Source);
}

public sealed class PlayerStats
{
    public Player Player { get; set; } = new();
    public Baseline? Baseline { get; set; }

    public int BaselineMatches => Baseline?.Matches ?? 0;
    public int BaselineChests => Baseline?.Chests ?? 0;
    public int BaselineKills => Baseline?.Kills ?? 0;

    /// <summary>Başlangıç tarihinden sonraki benzersiz, sayılan maçlar.</summary>
    public int TrackedMatches { get; set; }
    public int TrackedChests { get; set; }
    public int TrackedKills { get; set; }
    public int TrackedMatchesWithKills { get; set; }

    public int TotalMatches => BaselineMatches + TrackedMatches;
    /// <summary>Yönetici panelinden eklenen/çıkarılan sandıklar (yalnızca kendi oyuncumda; ekip sunucusundan gelir).</summary>
    public int AdminChests { get; set; }

    public int TotalChests => BaselineChests + TrackedChests + AdminChests;
    public int TotalKills => BaselineKills + TrackedKills;

    public double ChestsPerMatch => TotalMatches == 0 ? 0 : (double)TotalChests / TotalMatches;

    /// <summary>Kill ortalaması: yalnızca kill bilgisi olan maçlar paydaya girer.</summary>
    public double KillsPerMatch
    {
        get
        {
            var matches = BaselineMatches + TrackedMatchesWithKills;
            return matches == 0 ? 0 : (double)TotalKills / matches;
        }
    }

    public int Today { get; set; }
    public int TodayMatches { get; set; }
    public int ThisWeek { get; set; }
    public int ThisWeekMatches { get; set; }
    public int ThisSeason { get; set; }
    public int ThisSeasonMatches { get; set; }

    public int ReplayVerifiedChests { get; set; }
    public int PartialChests { get; set; }
    public int ManualChests { get; set; }

    public PlayerMatchStat? BestMatch { get; set; }
    public PlayerMatchStat? LastMatch { get; set; }

    /// <summary>Tüm maçlar (sayılmayanlar dahil), en yeni önce.</summary>
    public List<PlayerMatchStat> Matches { get; set; } = new();

    public int OverlapCount { get; set; }
    public int SuspiciousCount { get; set; }

    /// <summary>Yerel tarih → sandık (sayılan maçlar).</summary>
    public SortedDictionary<DateTime, int> DailyChests { get; set; } = new();
}

public sealed class UnmatchedName
{
    public string Name { get; set; } = "";
    public int Matches { get; set; }
    public int Chests { get; set; }
    public bool SeenAsRecorder { get; set; }
}

/// <summary>
/// Tüm istatistikleri saf fonksiyon olarak hesaplar (veri tabanına yazmaz).
///
/// Tekilleştirme: aynı maç birden fazla bilgisayardan (arkadaşların replay'leri, içe aktarılan JSON) gelebilir.
/// Her (maç, oyuncu) için tek değer seçilir: oyuncunun kendi replay'i varsa o (tam), yoksa takım
/// arkadaşlarının gördüğü en yüksek değer (kısmi), o da yoksa manuel kayıt.
///
/// Toplam = geçmiş başlangıç değeri + başlangıç tarihinden sonraki benzersiz, hariç tutulmamış maçlar.
/// </summary>
public static class StatsService
{
    public static List<PlayerStats> Compute(
        IReadOnlyList<Player> players,
        IReadOnlyList<Baseline> baselines,
        IReadOnlyList<MatchRow> matches,
        IReadOnlyList<MatchPlayerRow> rows,
        StatsOptions options)
    {
        var matchById = matches.ToDictionary(m => m.SessionId, StringComparer.Ordinal);
        var rowsBySession = rows.GroupBy(r => r.SessionId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(options.NowUtc, options.Zone);
        var today = nowLocal.Date;
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7)); // Pazartesi

        var result = new List<PlayerStats>();
        foreach (var player in players)
        {
            var stats = new PlayerStats { Player = player, Baseline = baselines.FirstOrDefault(b => b.PlayerId == player.Id) };
            var cutoff = stats.Baseline?.CutoffUtc ?? DateTime.MinValue;

            foreach (var (sessionId, sessionRows) in rowsBySession)
            {
                if (!matchById.TryGetValue(sessionId, out var match))
                {
                    continue;
                }

                var mine = sessionRows.Where(r => player.Owns(r.PlayerName)).ToList();
                if (mine.Count == 0)
                {
                    continue;
                }

                var stat = Resolve(match, mine);
                stat.ObserverCount = sessionRows.Select(r => r.Observer).Distinct(StringComparer.OrdinalIgnoreCase).Count();

                // Sayılır mı?
                var counted = true;
                var flags = new List<string>();
                if (match.Excluded)
                {
                    counted = false;
                    flags.Add("Hariç: " + (match.ExcludeReason ?? "kullanıcı tarafından"));
                }
                if (match.IsCustom && !options.IncludeCustomMatches)
                {
                    counted = false;
                    flags.Add("Özel maç (ayarlardan dahil edilebilir)");
                }
                if (match.StartedUtc is DateTime started && stats.Baseline != null)
                {
                    if (started < cutoff)
                    {
                        stat.BeforeCutoff = true;
                        if (match.OverlapOverride)
                        {
                            flags.Add("Başlangıç tarihinden önce, kullanıcı dahil etti");
                        }
                        else
                        {
                            counted = false;
                            flags.Add("Örtüşme: geçmiş başlangıç tarihinden önce oynanmış; geçmiş değerlere zaten dahil olabilir");
                        }
                    }
                    else if (started - cutoff < options.SuspiciousWindow)
                    {
                        stat.Suspicious = true;
                        flags.Add("Şüpheli örtüşme: başlangıç tarihine çok yakın, geçmiş değerlerde sayılmış olabilir");
                    }
                }
                if (match.StartedUtc == null)
                {
                    flags.Add("Maç tarihi bilinmiyor");
                }

                stat.Counted = counted;
                stat.Flag = flags.Count == 0 ? null : string.Join(" · ", flags);
                stats.Matches.Add(stat);

                if (stat.BeforeCutoff && !match.OverlapOverride && !match.Excluded)
                {
                    stats.OverlapCount++;
                }
                if (stat.Suspicious && counted)
                {
                    stats.SuspiciousCount++;
                }

                if (!counted)
                {
                    continue;
                }

                stats.TrackedMatches++;
                stats.TrackedChests += stat.Chests;
                if (stat.Kills is int k)
                {
                    stats.TrackedKills += k;
                    stats.TrackedMatchesWithKills++;
                }

                switch (stat.Source)
                {
                    case DataSource.Replay: stats.ReplayVerifiedChests += stat.Chests; break;
                    case DataSource.ReplayPartial: stats.PartialChests += stat.Chests; break;
                    case DataSource.Manual: stats.ManualChests += stat.Chests; break;
                }

                if (stat.StartedUtc is DateTime s)
                {
                    var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(s, DateTimeKind.Utc), options.Zone);
                    var day = local.Date;
                    stats.DailyChests[day] = stats.DailyChests.GetValueOrDefault(day) + stat.Chests;
                    if (day == today)
                    {
                        stats.Today += stat.Chests;
                        stats.TodayMatches++;
                    }
                    if (day >= weekStart)
                    {
                        stats.ThisWeek += stat.Chests;
                        stats.ThisWeekMatches++;
                    }
                    if (s >= options.SeasonStartUtc)
                    {
                        stats.ThisSeason += stat.Chests;
                        stats.ThisSeasonMatches++;
                    }
                }

                if (stats.BestMatch == null || stat.Chests > stats.BestMatch.Chests)
                {
                    stats.BestMatch = stat;
                }
            }

            stats.Matches.Sort((a, b) => Nullable.Compare(b.StartedUtc, a.StartedUtc));
            stats.LastMatch = stats.Matches.FirstOrDefault(m => m.Counted);
            result.Add(stats);
        }

        return result;
    }

    /// <summary>Bir oyuncunun bir maçtaki satırlarından tek değer seçer.</summary>
    internal static PlayerMatchStat Resolve(MatchRow match, List<MatchPlayerRow> mine)
    {
        MatchPlayerRow chosen;
        DataSource source;
        int? kills;

        var own = mine.Where(r => r.Source == DataSource.Replay && r.IsRecorder).ToList();
        var partial = mine.Where(r => r.Source == DataSource.ReplayPartial).ToList();
        var manual = mine.Where(r => r.Source == DataSource.Manual).ToList();

        if (own.Count > 0)
        {
            chosen = own.OrderByDescending(r => r.Chests).First();
            source = DataSource.Replay;
            kills = chosen.Kills;
        }
        else if (partial.Count > 0)
        {
            // Her gözlemci yalnızca kendi çevresini gördüğü için en yüksek değer en az eksik olandır.
            chosen = partial.OrderByDescending(r => r.Chests).First();
            source = DataSource.ReplayPartial;
            kills = partial.Where(r => r.Kills != null).Select(r => r.Kills).DefaultIfEmpty(null).Max();
        }
        else
        {
            chosen = manual.Count > 0 ? manual.OrderByDescending(r => r.CreatedUtc).First() : mine[0];
            source = chosen.Source;
            kills = chosen.Kills;
        }

        return new PlayerMatchStat
        {
            SessionId = match.SessionId,
            StartedUtc = match.StartedUtc,
            Chests = chosen.Chests,
            Kills = kills,
            Source = source,
            Playlist = match.Playlist,
            DurationSeconds = match.DurationSeconds,
            Placement = match.Placement,
            ChestsByKind = chosen.ChestsByKind,
            AmmoBoxes = chosen.AmmoBoxes,
            OtherContainers = chosen.OtherContainers,
            Observer = chosen.Observer,
        };
    }

    /// <summary>Hiçbir takipteki oyuncuya bağlanmamış (bot olmayan) oyun adları: eşleştirme için gösterilir.</summary>
    public static List<UnmatchedName> Unmatched(IReadOnlyList<Player> players, IReadOnlyList<MatchPlayerRow> rows)
    {
        return rows
            .Where(r => !r.IsBot && !r.PlayerName.StartsWith('#') && r.PlayerName != "?" && !players.Any(p => p.Owns(r.PlayerName)))
            .GroupBy(r => r.PlayerName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new UnmatchedName
            {
                Name = g.First().PlayerName,
                Matches = g.Select(r => r.SessionId).Distinct().Count(),
                Chests = g.GroupBy(r => r.SessionId).Sum(s => s.Max(r => r.Chests)),
                SeenAsRecorder = g.Any(r => r.IsRecorder),
            })
            .OrderByDescending(u => u.SeenAsRecorder)
            .ThenByDescending(u => u.Matches)
            .ToList();
    }
}
