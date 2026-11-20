using System.Text.Json;
using System.Text.Json.Serialization;

namespace FortniteCompanion.Core.Services;

public enum OverlayCorner
{
    TopRight,
    TopLeft,
    BottomRight,
    BottomLeft,
}

/// <summary>Kullanıcı ayarları (%APPDATA%\FortniteCompanion\settings.json).</summary>
public sealed class AppSettings
{
    // --- Overlay
    public bool OverlayVisible { get; set; } = true;
    public OverlayCorner OverlayCorner { get; set; } = OverlayCorner.TopRight;

    /// <summary>Köşeden uzaklık, ekran genişliği/yüksekliğinin oranı olarak (çözünürlük değişince konum korunur).</summary>
    public double OverlayOffsetX { get; set; } = 0.012;
    public double OverlayOffsetY { get; set; } = 0.18;

    /// <summary>Overlay'in gösterileceği ekran (cihaz adı). Boşsa birincil ekran.</summary>
    public string? OverlayScreen { get; set; }

    public double OverlayScale { get; set; } = 1.0;
    public double OverlayOpacity { get; set; } = 0.92;
    public bool OverlayLocked { get; set; } = true;
    public bool OverlayShowTeamLine { get; set; } = true;
    public bool OverlayAnimations { get; set; } = true;

    /// <summary>"dark" veya "light".</summary>
    public string Theme { get; set; } = "dark";

    /// <summary>"green", "blue" veya "gold".</summary>
    public string Accent { get; set; } = "gold";

    // --- Kısayollar (örn. "Ctrl+Alt+O")
    public string HotkeyToggleOverlay { get; set; } = "Ctrl+Alt+O";
    public string HotkeyToggleLock { get; set; } = "Ctrl+Alt+L";
    public string HotkeyManualIncrement { get; set; } = "Ctrl+Alt+Add";
    public string HotkeyManualDecrement { get; set; } = "Ctrl+Alt+Subtract";

    // --- Sayım
    /// <summary>Manuel canlı mod: kısayolla maç içi sayaç (replay geldiğinde onunla doğrulanır).</summary>
    public bool ManualLiveMode { get; set; }

    /// <summary>Bu bilgisayarın sahibi (overlay'de gösterilen oyuncu).</summary>
    public long? SelfPlayerId { get; set; }

    public string? ReplayFolder { get; set; }
    public bool IncludeCustomMatches { get; set; }
    public DateTime SeasonStartUtc { get; set; } = new(2026, 11, 1, 4, 0, 0, DateTimeKind.Utc);

    // --- Uygulama
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; } = true;

    /// <summary>Fortnite işlemi çalışırken overlay'i otomatik göster, kapanınca gizle.</summary>
    public bool AutoOverlayWithFortnite { get; set; } = true;

    public bool NotifyMatchEnd { get; set; } = true;
    public bool NotifyRecord { get; set; } = true;

    public bool AutoBackup { get; set; } = true;
    public int BackupKeepCount { get; set; } = 14;
    public string? BackupFolder { get; set; }

    /// <summary>Dışa aktarmada takipte olmayan oyuncu adlarını (rakipler/rastgele takım arkadaşları) gizle.</summary>
    public bool ExportHideUnknownNames { get; set; } = true;

    /// <summary>
    /// Otomatik güncelleme: açılışta ve 6 saatte bir GitHub'daki son sürüme bakılır; daha yenisi varsa indirilir,
    /// doğrulanır ve Fortnite kapalıyken kurulup uygulama yeniden başlatılır. Yalnızca api.github.com ve
    /// github.com'a bağlanılır; hiçbir kullanıcı verisi gönderilmez.
    /// </summary>
    public bool AutoUpdate { get; set; } = true;

    /// <summary>GitHub deposu ("kullanıcı/depo"). Boşsa EXE'nin içine derlemede gömülen depo kullanılır.</summary>
    public string? UpdateRepo { get; set; }

    /// <summary>Deneysel ekran analizi: bu sürümde uygulanmadı, her zaman kapalı.</summary>
    [JsonIgnore]
    public bool ExperimentalScreenAnalysis => false;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            AppLog.Error("Ayarlar okunamadı, varsayılanlar kullanılıyor", ex);
            try { File.Copy(path, path + ".bozuk", overwrite: true); } catch { /* yoksay */ }
        }
        return new AppSettings();
    }

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Ayarlar kaydedilemedi", ex);
        }
    }

    public StatsOptions ToStatsOptions() => new()
    {
        SeasonStartUtc = DateTime.SpecifyKind(SeasonStartUtc, DateTimeKind.Utc),
        IncludeCustomMatches = IncludeCustomMatches,
        NowUtc = DateTime.UtcNow,
    };
}
