using FortniteCompanion.Core.Data;
using FortniteCompanion.Core.Replay;

namespace FortniteCompanion.Core.Services;

public sealed record WatcherStatus(
    bool FolderExists,
    string Folder,
    int ReplayFiles,
    int Pending,
    bool InMatch,
    string? Processing,
    string? LastMessage);

public sealed record MatchRecordedEventArgs(ReplayMatchResult Result, MatchUpsertOutcome Outcome, string FileName);

/// <summary>
/// Demos klasörünü izler (FileSystemWatcher + 5 sn'lik yoklama). Fortnite maç bitince replay'in şifre
/// anahtarını yazar ve "canlı" bayrağını kapatır; dosya bir süre değişmeyince salt okunur biçimde analiz edilir.
/// İşlenen her dosya (ad+boyut+tarih) kaydedilir; aynı maç oturum kimliğiyle ikinci kez sayılmaz.
/// </summary>
public sealed class ReplayWatcher : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan AbandonedLiveAge = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan OldReplayAge = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan FinalizedQuietPeriod = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ParseTimeout = TimeSpan.FromMinutes(3);

    private readonly Database _db;
    private readonly Dictionary<string, long> _lastSeenSize = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private FileSystemWatcher? _fsw;
    private Timer? _timer;
    private volatile bool _disposed;
    private bool _inMatch;
    private string? _lastMessage;

    public string Folder { get; private set; }

    public event Action<WatcherStatus>? StatusChanged;
    public event Action<MatchRecordedEventArgs>? MatchRecorded;
    public event Action<string, string>? FileSkipped;
    public event Action<bool>? InMatchChanged;

    public ReplayWatcher(Database db, string folder)
    {
        _db = db;
        Folder = folder;
    }

    public void Start()
    {
        SetupFileWatcher();
        _timer = new Timer(_ => _ = ScanAsync(), null, TimeSpan.FromSeconds(1), PollInterval);
    }

    public void ChangeFolder(string folder)
    {
        Folder = folder;
        SetupFileWatcher();
        _ = ScanAsync();
    }

    private void SetupFileWatcher()
    {
        _fsw?.Dispose();
        _fsw = null;
        try
        {
            if (!Directory.Exists(Folder))
            {
                return;
            }
            _fsw = new FileSystemWatcher(Folder, "*.replay")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
            };
            _fsw.Created += (_, _) => _ = ScanAsync();
            _fsw.Changed += (_, _) => { /* yoklama yeterli; her yazmada taramaya gerek yok */ };
            _fsw.Renamed += (_, _) => _ = ScanAsync();
            _fsw.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            AppLog.Error("Klasör izleyici başlatılamadı: " + Folder, ex);
        }
    }

    /// <summary>Tüm replay'leri tarar; hazır olanları sırayla işler. Aynı anda tek tarama çalışır.</summary>
    public async Task ScanAsync(bool ignoreQuietPeriod = false)
    {
        if (_disposed || !await _scanGate.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await Task.Run(() => ScanCore(ignoreQuietPeriod)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Error("Replay taraması başarısız", ex);
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private void ScanCore(bool ignoreQuietPeriod)
    {
        if (!Directory.Exists(Folder))
        {
            Raise(new WatcherStatus(false, Folder, 0, 0, false, null, "Replay klasörü bulunamadı."));
            if (_fsw == null)
            {
                SetupFileWatcher();
            }
            return;
        }
        if (_fsw == null)
        {
            SetupFileWatcher();
        }

        var files = new DirectoryInfo(Folder).GetFiles("*.replay")
            .OrderBy(f => f.LastWriteTimeUtc)
            .ToList();

        var inMatch = files.Any(f => DateTime.UtcNow - f.LastWriteTimeUtc < TimeSpan.FromMinutes(10) && ReplayHeaderProbe.IsLive(f.FullName) == true);
        if (inMatch != _inMatch)
        {
            _inMatch = inMatch;
            InMatchChanged?.Invoke(inMatch);
        }

        var pending = files.Where(f => !_db.IsProcessed(Database.FileKey(f))).ToList();
        Raise(new WatcherStatus(true, Folder, files.Count, pending.Count, _inMatch, null, _lastMessage));

        foreach (var file in pending)
        {
            if (_disposed)
            {
                return;
            }

            file.Refresh();
            if (!file.Exists || !IsReady(file, ignoreQuietPeriod))
            {
                continue;
            }

            Raise(new WatcherStatus(true, Folder, files.Count, pending.Count, _inMatch, file.Name, _lastMessage));
            Process(file);
        }

        Raise(new WatcherStatus(true, Folder, files.Count, files.Count(f => !_db.IsProcessed(Database.FileKey(f))), _inMatch, null, _lastMessage));
    }

    private bool IsReady(FileInfo file, bool ignoreQuietPeriod)
    {
        var live = ReplayHeaderProbe.IsLive(file.FullName);
        var quietFor = DateTime.UtcNow - file.LastWriteTimeUtc;
        if (live == true)
        {
            // Maç sürüyor; ancak kayıt çok önce yarıda kaldıysa (oyun çöktü) işlenip "eksik" işaretlenir.
            return quietFor > AbandonedLiveAge;
        }

        if (live == false && (ignoreQuietPeriod || quietFor > OldReplayAge))
        {
            return true;
        }

        var stable = _lastSeenSize.TryGetValue(file.FullName, out var size) && size == file.Length;
        _lastSeenSize[file.FullName] = file.Length;

        if (live == null)
        {
            return stable && quietFor > OldReplayAge;
        }

        if (!ReplayHeaderProbe.KeyMissing(file.FullName))
        {
            return stable && quietFor >= FinalizedQuietPeriod;
        }
        return stable && quietFor > QuietPeriod;
    }

    private void Process(FileInfo file)
    {
        var key = Database.FileKey(file);

        if (ReplayHeaderProbe.IsLive(file.FullName) == true && ReplayHeaderProbe.KeyMissing(file.FullName))
        {
            const string why = "Eksik replay (Fortnite maç sırasında kapandı): bu maç okunamaz.";
            _db.MarkProcessed(key, file.Name, "incomplete", null, why);
            Skip(file.Name, why);
            return;
        }

        ReplayMatchResult result;
        try
        {
            result = ReplayProcessor.Analyze(file.FullName, ParseTimeout);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Replay okunamadı: {file.FullName} ({file.Length} bayt)", ex);
            var msg = ex is TimeoutException
                ? "Analiz çok uzun sürdü."
                : "Replay okunamadı (yeni Fortnite sürümü biçimi değiştirmiş olabilir): " + ex.Message;
            _db.MarkProcessed(key, file.Name, "error", null, msg);
            Skip(file.Name, msg);
            return;
        }

        if (!result.RecorderFound || result.Players.Count == 0)
        {
            const string why = "Kaydeden oyuncu bulunamadı; sandıklar kimseye yazılamadı (sayı uydurulmadı).";
            _db.MarkProcessed(key, file.Name, "no_recorder", result.SessionId, why);
            Skip(file.Name, why);
            return;
        }

        try
        {
            var outcome = _db.UpsertReplayMatch(result);
            _db.MarkProcessed(key, file.Name, outcome.AlreadyCounted ? "duplicate" : "ok", result.SessionId,
                outcome.AlreadyCounted ? "Aynı maç zaten sayılmış (mükerrer kayıt oluşturulmadı)." : null);
            _lastMessage = outcome.AlreadyCounted
                ? $"{file.Name}: maç zaten sayılmış."
                : $"{file.Name}: {result.Players[0].ChestsTotal} sandık kaydedildi.";
            MatchRecorded?.Invoke(new MatchRecordedEventArgs(result, outcome, file.Name));
        }
        catch (Exception ex)
        {
            AppLog.Error("Maç veri tabanına yazılamadı: " + file.Name, ex);
            Skip(file.Name, "Veri tabanı hatası: " + ex.Message);
        }
    }

    private void Skip(string file, string reason)
    {
        _lastMessage = $"{file}: {reason}";
        FileSkipped?.Invoke(file, reason);
    }

    private void Raise(WatcherStatus status)
    {
        try
        {
            StatusChanged?.Invoke(status);
        }
        catch (Exception ex)
        {
            AppLog.Error("Durum olayı işlenemedi", ex);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer?.Dispose();
        _fsw?.Dispose();
    }
}
