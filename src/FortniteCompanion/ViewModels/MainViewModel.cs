using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using FortniteCompanion.Core.Data;
using FortniteCompanion.Core.Services;

namespace FortniteCompanion.ViewModels;

public enum UpdateUiState
{
    None,
    Downloading,
    Ready,
    Installing,
    ManualOnly,
}

/// <summary>Kontrol panelinin tüm sekmeleri için görünüm modeli. Anlık durumdan (snapshot) yeniden doldurulur.</summary>
public sealed class MainViewModel : ObservableObject
{
    public const string AllPlayers = "Tüm oyuncular";
    public const string AllSources = "Tüm kaynaklar";

    private readonly CompanionService _service;
    private CompanionSnapshot _snapshot = new();
    private List<MatchItem> _allMatches = new();
    private bool _firstApply = true;

    public MainViewModel(CompanionService service)
    {
        _service = service;
    }

    public CompanionService Service => _service;

    // ------------------------------------------------------------------ Güncelleme rozeti (Discord tarzı)

    private UpdateUiState _updateState;
    public UpdateUiState UpdateState { get => _updateState; private set => Set(ref _updateState, value); }

    private string _updateTag = "";
    public string UpdateTag { get => _updateTag; private set => Set(ref _updateTag, value); }

    private string _updateButtonText = "";
    public string UpdateButtonText { get => _updateButtonText; private set => Set(ref _updateButtonText, value); }

    private string _updateBannerText = "";
    public string UpdateBannerText { get => _updateBannerText; private set => Set(ref _updateBannerText, value); }

    private bool _updateVisible;
    public bool UpdateVisible { get => _updateVisible; private set => Set(ref _updateVisible, value); }

    private bool _bannerDismissed;
    private bool _updateBannerVisible;
    public bool UpdateBannerVisible { get => _updateBannerVisible; private set => Set(ref _updateBannerVisible, value); }

    public string CurrentVersionText => "v" + AutoUpdater.CurrentVersion.ToString(3);

    public void SetUpdateState(string? tag, UpdateUiState state)
    {
        if (tag != null && tag != UpdateTag)
        {
            _bannerDismissed = false; // yeni sürüm: şeridi yeniden göster
        }
        UpdateTag = tag ?? "";
        UpdateState = state;
        UpdateVisible = state != UpdateUiState.None;
        UpdateButtonText = state switch
        {
            UpdateUiState.Downloading => "İNDİRİLİYOR…",
            UpdateUiState.Installing => "KURULUYOR…",
            UpdateUiState.ManualOnly => "İNDİR",
            _ => "GÜNCELLE",
        };
        UpdateBannerText = state switch
        {
            UpdateUiState.Downloading => $"Yeni sürüm {tag} indiriliyor…",
            UpdateUiState.Installing => $"{tag} kuruluyor, uygulama birkaç saniye içinde yeniden açılacak…",
            UpdateUiState.ManualOnly => $"Yeni sürüm {tag} yayımlandı. Bu klasöre yazılamadığı için elle indirmeniz gerekiyor.",
            _ => $"{tag} hazır! Yeni özellikler ve düzeltmeler için şimdi güncelleyin (uygulama birkaç saniyede yeniden açılır).",
        };
        UpdateBannerVisible = UpdateVisible && !_bannerDismissed;
    }

    public void DismissUpdateBanner()
    {
        _bannerDismissed = true;
        UpdateBannerVisible = false;
    }

    // ------------------------------------------------------------------ Genel Bakış

    private string _selfName = "";
    public string SelfName { get => _selfName; private set => Set(ref _selfName, value); }

    private string _totalChests = "0";
    public string TotalChests { get => _totalChests; private set => Set(ref _totalChests, value); }

    private string _totalChestsSource = "";
    public string TotalChestsSource { get => _totalChestsSource; private set => Set(ref _totalChestsSource, value); }

    private string _totalMatches = "0";
    public string TotalMatches { get => _totalMatches; private set => Set(ref _totalMatches, value); }

    private string _avgChests = "0,0";
    public string AvgChests { get => _avgChests; private set => Set(ref _avgChests, value); }

    private string _avgKills = "0,0";
    public string AvgKills { get => _avgKills; private set => Set(ref _avgKills, value); }

    private string _totalKills = "0";
    public string TotalKills { get => _totalKills; private set => Set(ref _totalKills, value); }

    private string _today = "0";
    public string Today { get => _today; private set => Set(ref _today, value); }

    private string _week = "0";
    public string Week { get => _week; private set => Set(ref _week, value); }

    private string _season = "0";
    public string Season { get => _season; private set => Set(ref _season, value); }

    private string _best = "—";
    public string Best { get => _best; private set => Set(ref _best, value); }

    private string _lastMatch = "—";
    public string LastMatch { get => _lastMatch; private set => Set(ref _lastMatch, value); }

    private string _statusText = "";
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    private bool _inMatch;
    public bool InMatch { get => _inMatch; private set => Set(ref _inMatch, value); }

    private string _warningText = "";
    public string WarningText { get => _warningText; private set { if (Set(ref _warningText, value)) OnPropertyChanged(nameof(HasWarning)); } }
    public bool HasWarning => WarningText.Length > 0;

    private bool _showReplayHelp;
    public bool ShowReplayHelp { get => _showReplayHelp; private set => Set(ref _showReplayHelp, value); }

    public ObservableCollection<MatchItem> RecentMatches { get; } = new();

    // ------------------------------------------------------------------ Maç Geçmişi

    public ObservableCollection<MatchItem> FilteredMatches { get; } = new();
    public ObservableCollection<string> PlayerFilterOptions { get; } = new() { AllPlayers };

    public IReadOnlyList<string> SourceFilterOptions { get; } = new[]
    {
        AllSources,
        DataSourceText.Short(DataSource.Replay),
        DataSourceText.Short(DataSource.ReplayPartial),
        DataSourceText.Short(DataSource.Manual),
    };

    private string _filterPlayer = AllPlayers;
    public string FilterPlayer { get => _filterPlayer; set { if (Set(ref _filterPlayer, value ?? AllPlayers)) ApplyFilter(); } }

    private string _filterSource = AllSources;
    public string FilterSource { get => _filterSource; set { if (Set(ref _filterSource, value ?? AllSources)) ApplyFilter(); } }

    private string _filterText = "";
    public string FilterText { get => _filterText; set { if (Set(ref _filterText, value ?? "")) ApplyFilter(); } }

    private bool _onlyCounted;
    public bool OnlyCounted { get => _onlyCounted; set { if (Set(ref _onlyCounted, value)) ApplyFilter(); } }

    private bool _onlyFlagged;
    public bool OnlyFlagged { get => _onlyFlagged; set { if (Set(ref _onlyFlagged, value)) ApplyFilter(); } }

    private string _matchSummary = "";
    public string MatchSummary { get => _matchSummary; private set => Set(ref _matchSummary, value); }

    // ------------------------------------------------------------------ Arkadaşlar

    /// <summary>Tüm görünür oyuncular (sıralı).</summary>
    public ObservableCollection<PlayerCardItem> PlayerCards { get; } = new();

    /// <summary>İlk üç (kürsü).</summary>
    public ObservableCollection<PlayerCardItem> Podium { get; } = new();

    /// <summary>4. ve sonrası (sıralama listesi).</summary>
    public ObservableCollection<PlayerCardItem> Others { get; } = new();

    private bool _showHidden;
    public bool ShowHidden { get => _showHidden; set { if (Set(ref _showHidden, value)) BuildTeam(); } }

    private string _teamSummary = "";
    public string TeamSummary { get => _teamSummary; private set => Set(ref _teamSummary, value); }

    private string _hiddenInfo = "";
    public string HiddenInfo { get => _hiddenInfo; private set => Set(ref _hiddenInfo, value); }

    private bool _hasOthers;
    public bool HasOthers { get => _hasOthers; private set => Set(ref _hasOthers, value); }

    private bool _teamEmpty = true;
    public bool TeamEmpty { get => _teamEmpty; private set => Set(ref _teamEmpty, value); }

    /// <summary>Grafikler ve sıralama için görünür oyuncular.</summary>
    private IEnumerable<PlayerStats> VisiblePlayers(CompanionSnapshot s) =>
        s.Players.Where(p => !p.Player.Hidden || p.Player.Id == _service.Settings.SelfPlayerId);

    private void BuildTeam()
    {
        var s = _snapshot;
        var selfId = _service.Settings.SelfPlayerId;
        var entries = TeamBoard.Build(s.Players, s.Remote, selfId, ShowHidden);
        var maxChests = Math.Max(1, entries.Count == 0 ? 1 : entries.Max(e => e.TotalChests));

        PlayerCards.Clear();
        Podium.Clear();
        Others.Clear();
        var rank = 0;
        foreach (var e in entries)
        {
            rank++;
            var p = e.Local;
            var warn = new List<string>();
            if (!e.FromServer && string.IsNullOrWhiteSpace(e.EpicNames))
            {
                warn.Add("Epic adı girilmedi: replay'lerdeki sayılar bu oyuncuya bağlanamaz.");
            }

            string baselineText, trackedText, sourceText;
            if (e.FromServer)
            {
                baselineText = $"☁ Ekip sunucusundan · {TeamSync.Ago(e.ServerUpdatedUtc)} güncellendi";
                trackedText = $"Bu hafta {Fmt.N(e.Week)} · bugün {Fmt.N(e.Today)} sandık";
                sourceText = string.IsNullOrEmpty(e.AppVersion) ? "Kendi bilgisayarındaki replay'lerle sayıldı" : $"Kendi bilgisayarında sayıldı · v{e.AppVersion}";
            }
            else if (p != null)
            {
                baselineText = $"Geçmiş: {Fmt.N(p.BaselineMatches)} maç · {Fmt.N(p.BaselineChests)} sandık · {Fmt.N(p.BaselineKills)} kill";
                trackedText = $"Yeni: {Fmt.N(p.TrackedMatches)} maç · {Fmt.N(p.TrackedChests)} sandık · {Fmt.N(p.TrackedKills)} kill";
                sourceText = $"Replay: {Fmt.N(p.ReplayVerifiedChests)} · Tahmini: {Fmt.N(p.PartialChests)} · Manuel: {Fmt.N(p.ManualChests)}";
                if (p.OverlapCount > 0)
                {
                    // Hata değil: bu maçlar geçmiş toplamın tarihinden önce oynandı, yani o sayıya zaten dahil.
                    sourceText += $"\n✓ {p.OverlapCount} replay maçı geçmiş toplamın içinde (çift sayılmadı)";
                }
            }
            else
            {
                baselineText = trackedText = sourceText = "";
            }

            var item = new PlayerCardItem
            {
                PlayerId = e.LocalPlayerId ?? 0,
                Name = e.Name,
                EpicNames = e.EpicNames,
                IsSelf = e.IsSelf,
                Hidden = e.Hidden,
                FromServer = e.FromServer,
                TotalChestsValue = e.TotalChests,
                TotalMatches = Fmt.N(e.TotalMatches),
                AvgChests = Fmt.D1(e.ChestsPerMatch),
                TotalKills = Fmt.N(e.TotalKills),
                AvgKills = Fmt.D1(e.KillsPerMatch),
                Best = e.BestMatch is int b ? Fmt.N(b) : "—",
                Week = Fmt.N(e.Week),
                BaselineText = baselineText,
                TrackedText = trackedText,
                SourceText = sourceText,
                BarRatio = (double)e.TotalChests / maxChests,
                Warning = string.Join(" ", warn),
                Rank = rank,
            };
            PlayerCards.Add(item);
            if (rank <= 3)
            {
                Podium.Add(item);
            }
            else
            {
                Others.Add(item);
            }
        }

        var hiddenCount = s.Players.Count(p => p.Player.Hidden && p.Player.Id != selfId);
        HiddenInfo = hiddenCount == 0 ? "" : $"{hiddenCount} gizli oyuncu";
        HasOthers = Others.Count > 0;
        TeamEmpty = entries.Count <= 1;
        var st = s.SyncStatus;
        var syncText = st == null || !st.Configured ? "Ekip sunucusu: bağlı değil (dosya ile paylaşım)"
            : !st.Enabled ? "Ekip sunucusu: kapalı (Ayarlar)"
            : st.LastError != null ? "Ekip sunucusu: " + st.LastError
            : st.LastSuccessUtc != null ? $"☁ Ekip sunucusu: {TeamSync.Ago(st.LastSuccessUtc)} eşitlendi"
            : "☁ Ekip sunucusuna bağlanılıyor…";
        TeamSummary = $"{entries.Count} oyuncu · ekip toplamı {Fmt.N(entries.Sum(e => e.TotalChests))} sandık · {Fmt.N(entries.Sum(e => e.TotalMatches))} maç   |   {syncText}";
        _teamEntries = entries;
        var myRank = entries.FindIndex(e => e.IsSelf);
        SelfRankText = myRank >= 0 && entries.Count > 1 ? $"EKİPTE #{myRank + 1} / {entries.Count}" : "";
    }

    private List<TeamEntry> _teamEntries = new();

    private string _selfRankText = "";
    public string SelfRankText { get => _selfRankText; private set => Set(ref _selfRankText, value); }

    public ObservableCollection<UnmatchedItem> Unmatched { get; } = new();

    private bool _hasUnmatched;
    public bool HasUnmatched { get => _hasUnmatched; private set => Set(ref _hasUnmatched, value); }

    // ------------------------------------------------------------------ Grafikler

    public ObservableCollection<string> ChartPlayerOptions { get; } = new();

    private string? _chartPlayer;
    public string? ChartPlayer { get => _chartPlayer; set { if (Set(ref _chartPlayer, value)) BuildCharts(); } }

    private IReadOnlyList<ChartPoint> _dailyPoints = Array.Empty<ChartPoint>();
    public IReadOnlyList<ChartPoint> DailyPoints { get => _dailyPoints; private set => Set(ref _dailyPoints, value); }

    private IReadOnlyList<ChartPoint> _weeklyPoints = Array.Empty<ChartPoint>();
    public IReadOnlyList<ChartPoint> WeeklyPoints { get => _weeklyPoints; private set => Set(ref _weeklyPoints, value); }

    private IReadOnlyList<ChartPoint> _distributionPoints = Array.Empty<ChartPoint>();
    public IReadOnlyList<ChartPoint> DistributionPoints { get => _distributionPoints; private set => Set(ref _distributionPoints, value); }

    private IReadOnlyList<ChartPoint> _comparePoints = Array.Empty<ChartPoint>();
    public IReadOnlyList<ChartPoint> ComparePoints { get => _comparePoints; private set => Set(ref _comparePoints, value); }

    private IReadOnlyList<ChartPoint> _compareAvgPoints = Array.Empty<ChartPoint>();
    public IReadOnlyList<ChartPoint> CompareAvgPoints { get => _compareAvgPoints; private set => Set(ref _compareAvgPoints, value); }

    private IReadOnlyList<ChartPoint> _compareKillPoints = Array.Empty<ChartPoint>();
    public IReadOnlyList<ChartPoint> CompareKillPoints { get => _compareKillPoints; private set => Set(ref _compareKillPoints, value); }

    private IReadOnlyList<ChartSeries> _progressSeries = Array.Empty<ChartSeries>();
    public IReadOnlyList<ChartSeries> ProgressSeries { get => _progressSeries; private set => Set(ref _progressSeries, value); }

    private IReadOnlyList<string> _progressLabels = Array.Empty<string>();
    public IReadOnlyList<string> ProgressLabels { get => _progressLabels; private set => Set(ref _progressLabels, value); }

    public ObservableCollection<string> Records { get; } = new();

    // ------------------------------------------------------------------ güncelleme

    public void Apply(CompanionSnapshot s)
    {
        // İzleyici durumu her birkaç saniyede bir yayınlanır; listeler ve grafikler yalnızca veri değişince
        // yeniden oluşturulur (gereksiz yeniden çizim ve titreme olmasın).
        var prev = _snapshot;
        var dataChanged = _firstApply
                          || !ReferenceEquals(prev.Players, s.Players)
                          || !ReferenceEquals(prev.Remote, s.Remote)
                          || prev.SyncStatus?.LastSuccessUtc != s.SyncStatus?.LastSuccessUtc
                          || prev.SyncStatus?.LastError != s.SyncStatus?.LastError
                          || prev.SyncStatus?.Enabled != s.SyncStatus?.Enabled;
        _firstApply = false;
        _snapshot = s;
        var self = s.Self;

        SelfName = self?.Player.DisplayName ?? "—";
        TotalChests = Fmt.N(self?.TotalChests ?? 0);
        TotalChestsSource = self == null ? "" :
            $"Önceki istatistikten aktarıldı: {Fmt.N(self.BaselineChests)} · Replay ile doğrulandı: {Fmt.N(self.ReplayVerifiedChests)}" +
            (self.PartialChests > 0 ? $" · Tahmini (takım replay'i): {Fmt.N(self.PartialChests)}" : "") +
            (self.ManualChests > 0 ? $" · Manuel girildi: {Fmt.N(self.ManualChests)}" : "");
        TotalMatches = Fmt.N(self?.TotalMatches ?? 0);
        AvgChests = Fmt.D1(self?.ChestsPerMatch ?? 0);
        AvgKills = Fmt.D1(self?.KillsPerMatch ?? 0);
        TotalKills = Fmt.N(self?.TotalKills ?? 0);
        Today = $"{Fmt.N(self?.Today ?? 0)} ({self?.TodayMatches ?? 0} maç)";
        Week = $"{Fmt.N(self?.ThisWeek ?? 0)} ({self?.ThisWeekMatches ?? 0} maç)";
        Season = $"{Fmt.N(self?.ThisSeason ?? 0)} ({self?.ThisSeasonMatches ?? 0} maç)";
        Best = self?.BestMatch is { } b ? $"{Fmt.N(b.Chests)} sandık · {Fmt.Date(b.StartedUtc)}" : "Henüz replay ile doğrulanmış maç yok";
        LastMatch = self?.LastMatch is { } l
            ? $"+{Fmt.N(l.Chests)} sandık · {Fmt.Date(l.StartedUtc)} · {DataSourceText.Short(l.Source)}"
            : "—";
        InMatch = s.InMatch;

        // Durum ve uyarılar
        var w = s.Watcher;
        var warnings = new List<string>();
        if (w == null)
        {
            StatusText = "Replay klasörü taranıyor…";
        }
        else if (!w.FolderExists)
        {
            StatusText = $"Replay klasörü bulunamadı: {w.Folder}";
            warnings.Add("Replay klasörü yok. Fortnite'ta replay kaydını açın veya Ayarlar'dan doğru klasörü seçin.");
        }
        else
        {
            StatusText = (s.InMatch ? "● Maç devam ediyor — replay maç bitince okunacak. " : "")
                         + $"{w.ReplayFiles} replay dosyası, {w.Pending} bekleyen"
                         + (w.Processing != null ? $" · okunuyor: {w.Processing}" : "")
                         + (w.LastMessage != null ? $" · {w.LastMessage}" : "");
            if (w.ReplayFiles == 0)
            {
                warnings.Add("Demos klasörü boş. Yeni maçlar oynandıkça burada replay dosyaları oluşur; eski maçlar geri getirilemez.");
            }
        }
        ShowReplayHelp = w == null || !w.FolderExists || w.ReplayFiles == 0;

        if (self != null && self.OverlapCount > 0)
        {
            warnings.Add($"Bilgi: {self.OverlapCount} replay maçı geçmiş toplamını girdiğin andan önce oynanmış, yani o sayının içinde. Çift sayılmasın diye ayrıca eklenmedi (Maç Geçmişi → Dahil et ile değiştirilebilir).");
        }
        if (self != null && self.SuspiciousCount > 0)
        {
            warnings.Add($"{self.SuspiciousCount} maç başlangıç tarihine çok yakın: geçmiş değerlerde sayılmış olabilir, kontrol edin.");
        }
        if (s.Unmatched.Any(u => u.SeenAsRecorder))
        {
            warnings.Add("Replay sahibi olarak görülen ama hiçbir oyuncuya bağlanmamış Epic adı var. EKİP sekmesinden eşleştirin.");
        }
        if (self == null)
        {
            warnings.Add("Henüz kim olduğunu seçmedin: Ayarlar → \"Sen kimsin?\" kurulumunu aç.");
        }
        WarningText = string.Join(Environment.NewLine, warnings.Select(x => "• " + x));

        if (!dataChanged)
        {
            return;
        }

        RecentMatches.Clear();
        if (self != null)
        {
            foreach (var m in self.Matches.Take(6))
            {
                RecentMatches.Add(MatchItem.From(self.Player, m, s.Matches.FirstOrDefault(x => x.SessionId == m.SessionId)));
            }
        }

        // Maç geçmişi
        var matchById = s.Matches.ToDictionary(m => m.SessionId, StringComparer.Ordinal);
        _allMatches = s.Players
            .SelectMany(p => p.Matches.Select(m => MatchItem.From(p.Player, m, matchById.GetValueOrDefault(m.SessionId))))
            .OrderByDescending(m => m.StartedUtc)
            .ToList();
        SyncOptions(PlayerFilterOptions, new[] { AllPlayers }.Concat(s.Players.Select(p => p.Player.DisplayName)));
        if (!PlayerFilterOptions.Contains(FilterPlayer))
        {
            _filterPlayer = AllPlayers;
            OnPropertyChanged(nameof(FilterPlayer));
        }
        ApplyFilter();

        // Ekip
        BuildTeam();

        Unmatched.Clear();
        foreach (var u in s.Unmatched.Take(30))
        {
            Unmatched.Add(new UnmatchedItem
            {
                Name = u.Name,
                Info = $"{u.Matches} maç, {u.Chests} sandık" + (u.SeenAsRecorder ? " · replay sahibi" : " · takım arkadaşı olarak görüldü"),
            });
        }
        HasUnmatched = Unmatched.Count > 0;

        // Grafikler
        SyncOptions(ChartPlayerOptions, s.Players.Select(p => p.Player.DisplayName));
        if (_chartPlayer == null || !ChartPlayerOptions.Contains(_chartPlayer))
        {
            _chartPlayer = self?.Player.DisplayName ?? ChartPlayerOptions.FirstOrDefault();
            OnPropertyChanged(nameof(ChartPlayer));
        }
        BuildCharts();
    }

    private static void SyncOptions(ObservableCollection<string> target, IEnumerable<string> values)
    {
        var list = values.ToList();
        if (target.SequenceEqual(list))
        {
            return;
        }
        target.Clear();
        foreach (var v in list)
        {
            target.Add(v);
        }
    }

    private void ApplyFilter()
    {
        IEnumerable<MatchItem> q = _allMatches;
        if (FilterPlayer != AllPlayers)
        {
            q = q.Where(m => m.Player == FilterPlayer);
        }
        if (FilterSource != AllSources)
        {
            q = q.Where(m => m.Source == FilterSource);
        }
        if (OnlyCounted)
        {
            q = q.Where(m => m.Counted);
        }
        if (OnlyFlagged)
        {
            q = q.Where(m => m.HasFlag);
        }
        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            var t = FilterText.Trim();
            q = q.Where(m => m.Mode.Contains(t, StringComparison.OrdinalIgnoreCase)
                             || m.Date.Contains(t, StringComparison.OrdinalIgnoreCase)
                             || m.Flag.Contains(t, StringComparison.OrdinalIgnoreCase)
                             || m.Player.Contains(t, StringComparison.OrdinalIgnoreCase));
        }

        var list = q.ToList();
        FilteredMatches.Clear();
        foreach (var m in list)
        {
            FilteredMatches.Add(m);
        }
        MatchSummary = $"{list.Count} satır · sayılan sandık: {Fmt.N(list.Where(m => m.Counted).Sum(m => m.Chests))}";
    }

    private void BuildCharts()
    {
        var s = _snapshot;
        var p = s.Players.FirstOrDefault(x => x.Player.DisplayName == ChartPlayer);
        var today = DateTime.Now.Date;

        // Günlük (son 14 gün)
        var daily = new List<ChartPoint>();
        for (var i = 13; i >= 0; i--)
        {
            var day = today.AddDays(-i);
            var v = p?.DailyChests.GetValueOrDefault(day) ?? 0;
            daily.Add(new ChartPoint(day.ToString("dd.MM", Fmt.Tr), v, $"{day:dd.MM.yyyy}: {v} sandık"));
        }
        DailyPoints = daily;

        // Haftalık (son 8 hafta, Pazartesi başlangıçlı)
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var weekly = new List<ChartPoint>();
        for (var i = 7; i >= 0; i--)
        {
            var ws = weekStart.AddDays(-7 * i);
            var we = ws.AddDays(7);
            var v = p?.DailyChests.Where(kv => kv.Key >= ws && kv.Key < we).Sum(kv => kv.Value) ?? 0;
            weekly.Add(new ChartPoint(ws.ToString("dd.MM", Fmt.Tr), v, $"{ws:dd.MM} haftası: {v} sandık"));
        }
        WeeklyPoints = weekly;

        // Maç başına sandık dağılımı
        var counted = p?.Matches.Where(m => m.Counted).ToList() ?? new List<PlayerMatchStat>();
        var buckets = new[] { (0, 4), (5, 9), (10, 14), (15, 19), (20, 24), (25, 29), (30, 39), (40, 999) };
        DistributionPoints = buckets
            .Select(b => new ChartPoint(b.Item2 >= 999 ? $"{b.Item1}+" : $"{b.Item1}-{b.Item2}",
                counted.Count(m => m.Chests >= b.Item1 && m.Chests <= b.Item2)))
            .ToList();

        // Dört oyuncu karşılaştırması
        // Karşılaştırma: ekip sunucusundakiler dahil birleşik sıralama
        var board = _teamEntries.Where(e => !e.Hidden || e.IsSelf).ToList();
        ComparePoints = board.Select(x => new ChartPoint(x.Name, x.TotalChests)).ToList();
        CompareAvgPoints = board.Select(x => new ChartPoint(x.Name, Math.Round(x.ChestsPerMatch, 1))).ToList();
        CompareKillPoints = board.Select(x => new ChartPoint(x.Name, x.TotalKills)).ToList();
        var team = VisiblePlayers(s).OrderByDescending(x => x.TotalChests).ToList();

        // Zaman içindeki gelişim: geçmiş değerden başlayan kümülatif toplam (son 30 gün)
        var colors = new[] { "#FFFFC83D", "#FF3FA9FF", "#FF3DDC84", "#FFB57CFF", "#FFFF7A59", "#FF4DE1D2" };
        var days = Enumerable.Range(0, 30).Select(i => today.AddDays(-29 + i)).ToList();
        ProgressLabels = days.Select(d => d.ToString("dd.MM", Fmt.Tr)).ToList();
        // Okunabilirlik için en çok sandığı olan 6 oyuncu
        ProgressSeries = team.Take(6).Select((x, idx) =>
        {
            var before = x.DailyChests.Where(kv => kv.Key < days[0]).Sum(kv => kv.Value);
            double running = x.BaselineChests + before;
            var values = new List<double>();
            foreach (var d in days)
            {
                running += x.DailyChests.GetValueOrDefault(d);
                values.Add(running);
            }
            return new ChartSeries { Name = x.Player.DisplayName, Color = colors[idx % colors.Length], Values = values };
        }).ToList();

        // Kişisel rekorlar
        Records.Clear();
        foreach (var x in team)
        {
            var bestDay = x.DailyChests.OrderByDescending(kv => kv.Value).FirstOrDefault();
            Records.Add($"{x.Player.DisplayName}: en iyi maç {(x.BestMatch is { } bm ? $"{bm.Chests} sandık ({Fmt.Date(bm.StartedUtc)})" : "—")}" +
                        $" · en iyi gün {(bestDay.Value > 0 ? $"{bestDay.Value} sandık ({bestDay.Key:dd.MM.yyyy})" : "—")}" +
                        $" · ortalama {Fmt.D1(x.ChestsPerMatch)} sandık/maç");
        }
    }

    public string CurrentSelfHeader()
    {
        var self = _snapshot.Self;
        return self == null ? "Fortnite Companion" : $"{self.Player.DisplayName}: {Fmt.N(self.TotalChests)} sandık · {Fmt.N(self.TotalMatches)} maç";
    }

    public static string Invariant(int v) => v.ToString(CultureInfo.InvariantCulture);
}
