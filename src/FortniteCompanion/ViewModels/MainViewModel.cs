using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using FortniteCompanion.Core.Data;
using FortniteCompanion.Core.Services;

namespace FortniteCompanion.ViewModels;

/// <summary>Kontrol panelinin tüm sekmeleri için görünüm modeli. Anlık durumdan (snapshot) yeniden doldurulur.</summary>
public sealed class MainViewModel : ObservableObject
{
    public const string AllPlayers = "Tüm oyuncular";
    public const string AllSources = "Tüm kaynaklar";

    private readonly CompanionService _service;
    private CompanionSnapshot _snapshot = new();
    private List<MatchItem> _allMatches = new();

    public MainViewModel(CompanionService service)
    {
        _service = service;
    }

    public CompanionService Service => _service;

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

    public ObservableCollection<PlayerCardItem> PlayerCards { get; } = new();
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
            warnings.Add($"{self.OverlapCount} maç geçmiş başlangıç tarihinden önce oynanmış; geçmiş değerlerle örtüşebileceği için toplama eklenmedi (Maç Geçmişi'nden inceleyin).");
        }
        if (self != null && self.SuspiciousCount > 0)
        {
            warnings.Add($"{self.SuspiciousCount} maç başlangıç tarihine çok yakın: geçmiş değerlerde sayılmış olabilir, kontrol edin.");
        }
        if (s.Unmatched.Any(u => u.SeenAsRecorder))
        {
            warnings.Add("Replay sahibi olarak görülen ama hiçbir oyuncuya bağlanmamış Epic adı var. Arkadaşlar sekmesinden eşleştirin.");
        }
        WarningText = string.Join(Environment.NewLine, warnings.Select(x => "• " + x));

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

        // Arkadaşlar
        var maxChests = Math.Max(1, s.Players.Count == 0 ? 1 : s.Players.Max(p => p.TotalChests));
        PlayerCards.Clear();
        var rank = 0;
        foreach (var p in s.Players.OrderByDescending(x => x.TotalChests).ThenBy(x => x.Player.SortOrder))
        {
            rank++;
            var warn = new List<string>();
            if (!p.Player.EpicNameList.Any())
            {
                warn.Add("Epic adı girilmedi: replay'lerdeki sayılar bu oyuncuya bağlanamaz.");
            }
            if (p.OverlapCount > 0)
            {
                warn.Add($"{p.OverlapCount} örtüşen maç toplama eklenmedi.");
            }
            PlayerCards.Add(new PlayerCardItem
            {
                PlayerId = p.Player.Id,
                Name = p.Player.DisplayName,
                EpicNames = p.Player.EpicNames,
                IsSelf = p.Player.Id == _service.Settings.SelfPlayerId,
                TotalChestsValue = p.TotalChests,
                TotalMatches = Fmt.N(p.TotalMatches),
                AvgChests = Fmt.D1(p.ChestsPerMatch),
                TotalKills = Fmt.N(p.TotalKills),
                AvgKills = Fmt.D1(p.KillsPerMatch),
                Best = p.BestMatch is { } pb ? Fmt.N(pb.Chests) : "—",
                Week = Fmt.N(p.ThisWeek),
                BaselineText = $"Geçmiş: {Fmt.N(p.BaselineMatches)} maç · {Fmt.N(p.BaselineChests)} sandık · {Fmt.N(p.BaselineKills)} kill",
                TrackedText = $"Yeni: {Fmt.N(p.TrackedMatches)} maç · {Fmt.N(p.TrackedChests)} sandık · {Fmt.N(p.TrackedKills)} kill",
                SourceText = $"Replay: {Fmt.N(p.ReplayVerifiedChests)} · Tahmini: {Fmt.N(p.PartialChests)} · Manuel: {Fmt.N(p.ManualChests)}",
                BarRatio = (double)p.TotalChests / maxChests,
                Warning = string.Join(" ", warn),
                Rank = rank,
            });
        }

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
        ComparePoints = s.Players.Select(x => new ChartPoint(x.Player.DisplayName, x.TotalChests,
            $"{x.Player.DisplayName}: {x.TotalChests} sandık (geçmiş {x.BaselineChests} + yeni {x.TrackedChests})")).ToList();
        CompareAvgPoints = s.Players.Select(x => new ChartPoint(x.Player.DisplayName, Math.Round(x.ChestsPerMatch, 1))).ToList();
        CompareKillPoints = s.Players.Select(x => new ChartPoint(x.Player.DisplayName, x.TotalKills)).ToList();

        // Zaman içindeki gelişim: geçmiş değerden başlayan kümülatif toplam (son 30 gün)
        var colors = new[] { "#FFFFC83D", "#FF3FA9FF", "#FF3DDC84", "#FFB57CFF", "#FFFF7A59", "#FF4DE1D2" };
        var days = Enumerable.Range(0, 30).Select(i => today.AddDays(-29 + i)).ToList();
        ProgressLabels = days.Select(d => d.ToString("dd.MM", Fmt.Tr)).ToList();
        ProgressSeries = s.Players.Select((x, idx) =>
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
        foreach (var x in s.Players)
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
