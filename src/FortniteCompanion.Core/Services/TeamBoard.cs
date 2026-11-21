using FortniteCompanion.Core.Data;

namespace FortniteCompanion.Core.Services;

/// <summary>Ekip sıralamasındaki bir satır: yerel hesaplama veya ekip sunucusundan gelen özet.</summary>
public sealed class TeamEntry
{
    public string Name { get; set; } = "";
    public string EpicNames { get; set; } = "";
    public long? LocalPlayerId { get; set; }
    public bool IsSelf { get; set; }
    public bool Hidden { get; set; }

    /// <summary>Sayılar ekip sunucusundan (oyuncunun kendi uygulamasından) geliyor.</summary>
    public bool FromServer { get; set; }

    public DateTime? ServerUpdatedUtc { get; set; }
    public string? AppVersion { get; set; }

    public int TotalChests { get; set; }
    public int TotalMatches { get; set; }
    public int TotalKills { get; set; }
    public double ChestsPerMatch { get; set; }
    public double KillsPerMatch { get; set; }
    public int Week { get; set; }
    public int Today { get; set; }
    public int? BestMatch { get; set; }

    /// <summary>Yerel istatistik (varsa): geçmiş/yeni/kaynak dağılımı için.</summary>
    public PlayerStats? Local { get; set; }
}

/// <summary>
/// Yerel istatistikleri ve ekip sunucusundan gelenleri tek sıralamada birleştirir.
///
/// Kurallar: "Ben" her zaman yerel hesaplamadır (en güncel). Başka bir oyuncu sunucudaysa onun sayıları
/// sunucudan alınır — çünkü onlar o oyuncunun kendi bilgisayarında, kendi replay'leriyle hesaplanmıştır (tam).
/// Eşleştirme Epic adı veya görünen adla yapılır. Sunucuda olup yerelde olmayan oyuncular da listeye eklenir.
/// </summary>
public static class TeamBoard
{
    public static List<TeamEntry> Build(IReadOnlyList<PlayerStats> local, IReadOnlyList<RemotePlayer> remote, long? selfId, bool includeHidden)
    {
        var entries = new List<TeamEntry>();
        var usedRemote = new HashSet<RemotePlayer>();

        RemotePlayer? MatchRemote(Player p) =>
            remote.FirstOrDefault(r => !usedRemote.Contains(r) && r.EpicNameList.Any(p.Owns))
            ?? remote.FirstOrDefault(r => !usedRemote.Contains(r) && string.Equals(r.DisplayName, p.DisplayName, StringComparison.OrdinalIgnoreCase));

        foreach (var s in local)
        {
            var isSelf = s.Player.Id == selfId;
            var r = MatchRemote(s.Player);
            if (r != null)
            {
                usedRemote.Add(r);
            }
            if (s.Player.Hidden && !isSelf && !includeHidden)
            {
                continue;
            }

            var e = new TeamEntry
            {
                Name = s.Player.DisplayName,
                EpicNames = s.Player.EpicNames,
                LocalPlayerId = s.Player.Id,
                IsSelf = isSelf,
                Hidden = s.Player.Hidden,
                Local = s,
            };
            if (r != null && !isSelf)
            {
                Fill(e, r);
                if (string.IsNullOrWhiteSpace(e.EpicNames))
                {
                    e.EpicNames = r.EpicNames ?? "";
                }
            }
            else
            {
                e.TotalChests = s.TotalChests;
                e.TotalMatches = s.TotalMatches;
                e.TotalKills = s.TotalKills;
                e.ChestsPerMatch = s.ChestsPerMatch;
                e.KillsPerMatch = s.KillsPerMatch;
                e.Week = s.ThisWeek;
                e.Today = s.Today;
                e.BestMatch = s.BestMatch?.Chests;
            }
            entries.Add(e);
        }

        foreach (var r in remote)
        {
            if (usedRemote.Contains(r))
            {
                continue;
            }
            var e = new TeamEntry { Name = r.DisplayName, EpicNames = r.EpicNames ?? "" };
            Fill(e, r);
            entries.Add(e);
        }

        return entries
            .OrderByDescending(e => e.TotalChests)
            .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static void Fill(TeamEntry e, RemotePlayer r)
    {
        e.FromServer = true;
        e.ServerUpdatedUtc = r.UpdatedUtc;
        e.AppVersion = r.AppVersion;
        e.TotalChests = r.TotalChests;
        e.TotalMatches = r.TotalMatches;
        e.TotalKills = r.TotalKills;
        e.ChestsPerMatch = r.ChestsPerMatch;
        e.KillsPerMatch = r.KillsPerMatch;
        e.Week = r.Week;
        e.Today = r.Today;
        e.BestMatch = r.BestMatch > 0 ? r.BestMatch : null;
    }
}
