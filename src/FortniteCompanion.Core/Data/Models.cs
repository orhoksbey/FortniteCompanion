namespace FortniteCompanion.Core.Data;

/// <summary>Bir sayının nereden geldiği. Arayüz her sayının yanında bunu gösterir.</summary>
public enum DataSource
{
    /// <summary>Kaydeden oyuncunun kendi replay'i: tam ve doğrulanmış.</summary>
    Replay,

    /// <summary>Takım arkadaşının replay'inden: oyuncu kaydedenden uzaktayken açtıkları görünmez, eksik olabilir.</summary>
    ReplayPartial,

    /// <summary>Kullanıcı tarafından elle girildi (canlı manuel sayaç veya düzenleme).</summary>
    Manual,

    /// <summary>Uygulamadan önce tutulan istatistiklerden aktarıldı.</summary>
    Baseline,
}

public static class DataSourceText
{
    public static string Label(DataSource s) => s switch
    {
        DataSource.Replay => "Replay ile doğrulandı",
        DataSource.ReplayPartial => "Tahmini (takım arkadaşının replay'inden, eksik olabilir)",
        DataSource.Manual => "Manuel girildi",
        DataSource.Baseline => "Önceki istatistikten aktarıldı",
        _ => "Bilinmiyor",
    };

    public static string Short(DataSource s) => s switch
    {
        DataSource.Replay => "Replay",
        DataSource.ReplayPartial => "Tahmini",
        DataSource.Manual => "Manuel",
        DataSource.Baseline => "Geçmiş",
        _ => "?",
    };

    public static string ToDb(DataSource s) => s switch
    {
        DataSource.Replay => "replay",
        DataSource.ReplayPartial => "replay_partial",
        DataSource.Manual => "manual",
        DataSource.Baseline => "baseline",
        _ => "replay",
    };

    public static DataSource FromDb(string? s) => s switch
    {
        "replay" => DataSource.Replay,
        "replay_partial" => DataSource.ReplayPartial,
        "manual" => DataSource.Manual,
        "baseline" => DataSource.Baseline,
        _ => DataSource.Replay,
    };
}

/// <summary>Takipteki oyuncu (Ryder, Orhan, Ohxo, Doğukan ...).</summary>
public sealed class Player
{
    public long Id { get; set; }
    public string DisplayName { get; set; } = "";

    /// <summary>Oyundaki Epic adları (virgülle ayrılmış). Replay'deki adlar bunlarla eşleştirilir.</summary>
    public string EpicNames { get; set; } = "";

    public int SortOrder { get; set; }

    /// <summary>Ekipten ayrılan oyuncu: silinmez (kalıcı geçmiş), listelerde gizlenir.</summary>
    public bool Hidden { get; set; }

    public IEnumerable<string> EpicNameList =>
        EpicNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Manuel kayıtlarda kullanılan iç ad: "#Ryder".</summary>
    public string ManualKey => "#" + DisplayName;

    public bool Owns(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            return false;
        }
        if (string.Equals(playerName, ManualKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        foreach (var n in EpicNameList)
        {
            if (string.Equals(n, playerName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}

/// <summary>"Geçmişten eklenen kayıtlar": uygulama öncesi tutulan toplamlar.</summary>
public sealed class Baseline
{
    public long PlayerId { get; set; }
    public int Matches { get; set; }
    public int Chests { get; set; }
    public int Kills { get; set; }

    /// <summary>Bu değerlerin geçerli olduğu son an. Bundan önceki replay maçları toplama eklenmez (örtüşme).</summary>
    public DateTime CutoffUtc { get; set; }

    public string? Note { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

public sealed class MatchRow
{
    public string SessionId { get; set; } = "";
    public DateTime? StartedUtc { get; set; }
    public int? DurationSeconds { get; set; }
    public string? Playlist { get; set; }
    public string? Map { get; set; }
    public bool IsCustom { get; set; }
    public int? TeamSize { get; set; }
    public int? TotalPlayers { get; set; }
    public int? Placement { get; set; }
    public string? GameVersion { get; set; }
    public DataSource Source { get; set; }
    public int AnalyzerVersion { get; set; }
    public DateTime FirstSeenUtc { get; set; }

    /// <summary>Toplamlardan hariç (ör. manuel kayıt replay ile değiştirildi, kullanıcı hariç tuttu).</summary>
    public bool Excluded { get; set; }

    public string? ExcludeReason { get; set; }

    /// <summary>Başlangıç tarihinden önceki maçı kullanıcı yine de toplama dahil etti.</summary>
    public bool OverlapOverride { get; set; }

    public string? Note { get; set; }
}

/// <summary>Bir maçta bir oyuncu için bir gözlemcinin (replay sahibinin) gördüğü değerler.</summary>
public sealed class MatchPlayerRow
{
    public string SessionId { get; set; } = "";

    /// <summary>Replay'i kaydeden oyuncunun adı; manuel kayıtlarda "manual".</summary>
    public string Observer { get; set; } = "";

    /// <summary>Oyundaki ad veya manuel kayıtlarda "#GörünenAd".</summary>
    public string PlayerName { get; set; } = "";

    public bool IsRecorder { get; set; }
    public bool IsBot { get; set; }
    public int Chests { get; set; }
    public Dictionary<string, int> ChestsByKind { get; set; } = new();
    public int AmmoBoxes { get; set; }
    public int OtherContainers { get; set; }
    public int DestroyedChests { get; set; }
    public int? Kills { get; set; }
    public DataSource Source { get; set; }
    public string? FileName { get; set; }
    public string? ImportedFrom { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public sealed class EditLogEntry
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string Entity { get; set; } = "";
    public string EntityKey { get; set; } = "";
    public string Field { get; set; } = "";
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Reason { get; set; }
}

public sealed class ProcessedFile
{
    public string FileKey { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Status { get; set; } = "";
    public string? SessionId { get; set; }
    public string? Message { get; set; }
    public DateTime ProcessedUtc { get; set; }
}

public sealed class LiveSession
{
    public long Id { get; set; }
    public long PlayerId { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime? EndedUtc { get; set; }
    public int ManualCount { get; set; }

    /// <summary>open | verified | saved_manual | discarded</summary>
    public string Status { get; set; } = "open";

    public string? LinkedSessionId { get; set; }
}
