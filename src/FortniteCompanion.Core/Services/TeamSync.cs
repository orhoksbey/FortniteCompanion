using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortniteCompanion.Core.Data;

namespace FortniteCompanion.Core.Services;

/// <summary>Sunucuya gönderilen maç (yalnızca oyuncunun kendi, toplama giren maçları).</summary>
public sealed class SyncMatch
{
    public string SessionId { get; set; } = "";
    public DateTime? StartedUtc { get; set; }
    public int Chests { get; set; }
    public int? Kills { get; set; }
    public string Source { get; set; } = "";
    public string? Playlist { get; set; }
    public int? Placement { get; set; }
    public int? DurationSeconds { get; set; }
}

/// <summary>Sunucuya gönderilen oyuncu özeti.</summary>
public sealed class SyncPayload
{
    public string DeviceId { get; set; } = "";
    public string DeviceSecret { get; set; } = "";
    public string AppVersion { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string EpicNames { get; set; } = "";
    public int TotalChests { get; set; }
    public int TotalMatches { get; set; }
    public int TotalKills { get; set; }
    public double ChestsPerMatch { get; set; }
    public double KillsPerMatch { get; set; }
    public int Today { get; set; }
    public int Week { get; set; }
    public int Season { get; set; }
    public int BestMatch { get; set; }
    public int BaselineChests { get; set; }
    public int BaselineMatches { get; set; }
    public int BaselineKills { get; set; }
    public int TrackedMatches { get; set; }
    public int TrackedChests { get; set; }
    public DateTime? LastMatchUtc { get; set; }
    public List<SyncMatch> Matches { get; set; } = new();
}

/// <summary>Sunucudan gelen ekip arkadaşı.</summary>
public sealed class RemotePlayer
{
    public string DeviceId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? EpicNames { get; set; }
    public int TotalChests { get; set; }
    public int TotalMatches { get; set; }
    public int TotalKills { get; set; }
    public double ChestsPerMatch { get; set; }
    public double KillsPerMatch { get; set; }
    public int Today { get; set; }
    public int Week { get; set; }
    public int Season { get; set; }
    public int BestMatch { get; set; }
    public int TrackedMatches { get; set; }
    public DateTime? LastMatchUtc { get; set; }
    public DateTime? UpdatedUtc { get; set; }
    public string? AppVersion { get; set; }

    public IEnumerable<string> EpicNameList =>
        (EpicNames ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public sealed record TeamSyncStatus(bool Configured, bool Enabled, DateTime? LastSuccessUtc, string? LastError, int RemoteCount);

/// <summary>
/// Ekip sunucusuyla (Google Apps Script web uygulaması) eşitleme. Varsayılan açık, Ayarlar'dan kapatılabilir.
/// Yalnızca oyuncunun kendi istatistikleri gönderilir; yanıt olarak ekip sıralaması gelir.
/// </summary>
public sealed class TeamSync
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AppSettings _settings;
    private List<RemotePlayer> _remote = new();

    public DateTime? LastSuccessUtc { get; private set; }
    public string? LastError { get; private set; }
    public string? LastSentSignature { get; private set; }

    public IReadOnlyList<RemotePlayer> Remote => _remote;

    public TeamSync(AppSettings settings)
    {
        _settings = settings;
    }

    // ------------------------------------------------------------------ yapılandırma

    private static string? Embedded(string key) =>
        Assembly.GetEntryAssembly()?
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == key)?.Value is { Length: > 0 } v ? v.Trim() : null;

    public string? Url => !string.IsNullOrWhiteSpace(_settings.TeamSyncUrl) ? _settings.TeamSyncUrl!.Trim() : Embedded("SyncUrl");
    public string? Key => !string.IsNullOrWhiteSpace(_settings.TeamSyncKey) ? _settings.TeamSyncKey!.Trim() : Embedded("SyncKey");

    public bool Configured =>
        Url is { } u && u.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(Key);

    public TeamSyncStatus Status => new(Configured, _settings.TeamSyncEnabled, LastSuccessUtc, LastError, _remote.Count);

    /// <summary>Cihaz kimliği ve gizli anahtarı yoksa üretir. Değiştiyse true döner (ayarlar kaydedilmeli).</summary>
    public static bool EnsureDeviceIdentity(AppSettings settings)
    {
        var changed = false;
        if (string.IsNullOrWhiteSpace(settings.DeviceId) || settings.DeviceId!.Length != 32)
        {
            settings.DeviceId = Guid.NewGuid().ToString("N");
            changed = true;
        }
        if (string.IsNullOrWhiteSpace(settings.DeviceSecret))
        {
            settings.DeviceSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            changed = true;
        }
        return changed;
    }

    // ------------------------------------------------------------------ yük

    /// <summary>Kendi istatistiklerinden gönderilecek özeti üretir (saf fonksiyon, test edilebilir).</summary>
    public static SyncPayload BuildPayload(AppSettings settings, PlayerStats self, string appVersion)
    {
        return new SyncPayload
        {
            DeviceId = settings.DeviceId ?? "",
            DeviceSecret = settings.DeviceSecret ?? "",
            AppVersion = appVersion,
            DisplayName = self.Player.DisplayName,
            EpicNames = self.Player.EpicNames,
            TotalChests = self.TotalChests,
            TotalMatches = self.TotalMatches,
            TotalKills = self.TotalKills,
            ChestsPerMatch = Math.Round(self.ChestsPerMatch, 2),
            KillsPerMatch = Math.Round(self.KillsPerMatch, 2),
            Today = self.Today,
            Week = self.ThisWeek,
            Season = self.ThisSeason,
            BestMatch = self.BestMatch?.Chests ?? 0,
            BaselineChests = self.BaselineChests,
            BaselineMatches = self.BaselineMatches,
            BaselineKills = self.BaselineKills,
            TrackedMatches = self.TrackedMatches,
            TrackedChests = self.TrackedChests,
            LastMatchUtc = self.LastMatch?.StartedUtc,
            Matches = self.Matches
                .Where(m => m.Counted)
                .OrderByDescending(m => m.StartedUtc)
                .Take(300)
                .Select(m => new SyncMatch
                {
                    SessionId = m.SessionId,
                    StartedUtc = m.StartedUtc,
                    Chests = m.Chests,
                    Kills = m.Kills,
                    Source = DataSourceText.Short(m.Source),
                    Playlist = m.Playlist,
                    Placement = m.Placement,
                    DurationSeconds = m.DurationSeconds,
                })
                .ToList(),
        };
    }

    /// <summary>Değişiklik olmadıysa gereksiz gönderim yapılmasın diye özet imzası.</summary>
    public static string Signature(SyncPayload p) =>
        string.Join("|", p.DisplayName, p.EpicNames, p.TotalChests, p.TotalMatches, p.TotalKills, p.Today, p.Week,
            p.Matches.Count, p.Matches.FirstOrDefault()?.SessionId, p.AppVersion);

    // ------------------------------------------------------------------ ağ

    /// <summary>
    /// Kendi özetini gönderir (self null ise yalnızca sıralamayı alır). Ağ hatasında sessizce başarısız olur,
    /// sonraki denemede tekrar dener. Aynı anda tek eşitleme çalışır.
    /// </summary>
    public async Task<bool> SyncAsync(PlayerStats? self, string appVersion, bool force, CancellationToken ct = default)
    {
        if (!_settings.TeamSyncEnabled || !Configured)
        {
            return false;
        }
        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false))
        {
            return false;
        }

        try
        {
            object body;
            string? signature = null;
            if (self != null)
            {
                EnsureDeviceIdentity(_settings); // çağıran ayarları kaydeder
                var payload = BuildPayload(_settings, self, appVersion);
                signature = Signature(payload);
                if (!force && signature == LastSentSignature && LastSuccessUtc is DateTime last && DateTime.UtcNow - last < TimeSpan.FromMinutes(9))
                {
                    return true; // değişiklik yok ve yakın zamanda eşitlendi
                }
                body = new { action = "push", key = Key, payload };
            }
            else
            {
                body = new { action = "leaderboard", key = Key };
            }

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "text/plain");
            // Apps Script POST'u işler ve sonucu 302 yönlendirmesiyle verir; HttpClient yönlendirmeyi GET ile izler.
            using var resp = await http.PostAsync(Url, content, ct).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                LastError = $"Sunucu {(int)resp.StatusCode} döndürdü.";
                return false;
            }

            ServerResponse? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<ServerResponse>(text, Json);
            }
            catch (JsonException)
            {
                LastError = "Sunucu yanıtı okunamadı (adres yanlış veya web uygulaması \"Herkes\" erişimine açık değil).";
                return false;
            }
            if (parsed is not { Ok: true })
            {
                LastError = "Sunucu reddetti: " + (parsed?.Error ?? "bilinmeyen hata");
                return false;
            }

            var own = _settings.DeviceId;
            _remote = (parsed.Players ?? new List<RemotePlayer>())
                .Where(p => !string.Equals(p.DeviceId, own, StringComparison.OrdinalIgnoreCase))
                .ToList();
            LastSuccessUtc = DateTime.UtcNow;
            LastError = null;
            if (signature != null)
            {
                LastSentSignature = signature;
            }
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            LastError = "Bağlanılamadı: " + ex.Message;
            AppLog.Warn("Ekip eşitlemesi: " + ex.Message);
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed class ServerResponse
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public List<RemotePlayer>? Players { get; set; }
    }

    public static string Ago(DateTime? utc)
    {
        if (utc is not DateTime d)
        {
            return "—";
        }
        var m = (DateTime.UtcNow - d).TotalMinutes;
        if (m < 2) return "şimdi";
        if (m < 60) return ((int)m).ToString(CultureInfo.InvariantCulture) + " dk önce";
        if (m < 48 * 60) return ((int)(m / 60)).ToString(CultureInfo.InvariantCulture) + " sa önce";
        return ((int)(m / 1440)).ToString(CultureInfo.InvariantCulture) + " gün önce";
    }
}
