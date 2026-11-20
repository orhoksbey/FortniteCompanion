// Bu dosya LED-N/Fortnite-ChestCounter (MIT, Copyright (c) 2026 LED-N) içindeki ReplayAnalyzer.cs'ten
// uyarlanmıştır. Hazine (treasure) takibi, hata ayıklama çıktıları ve araç sürüş raporları çıkarıldı;
// sandık sayımı kaydeden oyuncu ve tüm takım arkadaşları için aynı kurallarla yapılacak şekilde genelleştirildi.
// Lisans metni: lib/LICENSE-ChestCounter-LED-N
using System.Text.RegularExpressions;
using FortniteReplayReader.Models;
using FortniteReplayReader.Models.NetFieldExports;
using FortniteReplayReader.Models.NetFieldExports.Items;
using FortniteReplayReader.Models.NetFieldExports.Vehicles;
using FortniteReplayReader.Models.NetFieldExports.Weapons;
using Unreal.Core;
using Unreal.Core.Contracts;
using Unreal.Core.Models;
using Unreal.Core.Models.Enums;

namespace FortniteCompanion.Core.Replay;

/// <summary>
/// Bir .replay dosyasını salt okunur biçimde çözümler ve kaydeden oyuncu ile takım arkadaşlarının açtığı
/// sandıkları bulur. Oyun belleğine, ağ trafiğine veya oyun dosyalarına hiçbir şekilde erişmez.
///
/// Nasıl çalışır: aranabilir her konteyner, kendisini açan piyonu (<c>SearchingPawn</c>) ve
/// <c>bAlreadySearched</c> bayrağını replikasyonla gönderir. Piyon → PlayerState → oyuncu eşlemesiyle
/// sandığı kimin açtığı bulunur.
///
/// Sınır: replay yalnızca kaydeden oyuncunun yakınında (~150 m) olan konteynerleri içerir. Bu yüzden takım
/// arkadaşlarının sayıları eksik olabilir ("kısmi"); kaydeden oyuncunun kendi sayısı ise tamdır.
/// </summary>
public sealed class ReplayAnalyzer : FortniteReplayReader.ReplayReader
{
    /// <summary>Analiz mantığı değiştiğinde artırılır; eski kayıtlar yeniden analiz edilebilir.</summary>
    /// 2: maç başlangıç saati UTC olarak düzeltildi (sürüm 1 yerel saat farkı kadar erken kaydediyordu).
    public const int CurrentVersion = 2;

    private sealed record SearchEvent(float Time, string ActorKey, string Label, uint? SearcherPawn, bool Destroyed = false, bool Stale = false);
    private sealed record TossEvent(float Time, string Item, Pt? Loc, uint? Owner);
    private sealed record OrphanDestroy(float Time, string ActorKey, string Label, uint Actor);

    private readonly record struct Pt(float T, float X, float Y, float Z);

    private sealed class SeatSpan(uint pawn, uint vehicle, float from)
    {
        public uint Pawn { get; } = pawn;
        public uint Vehicle { get; } = vehicle;
        public float From { get; } = from;
        public float To { get; set; } = float.MaxValue;
    }

    private static readonly Regex VehicleClass = new(@"_Vehicle(_Static)?_C$|MeatballVehicle", RegexOptions.Compiled);
    private static readonly Regex TreasureActor = new(
        @"FlameCeleb|RelicExtractor|RelicFinder|LuckyLocator|SmugglerExtractionKey|ExtractionAccelerat",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex UaidSuffix = new(@"_UAID_.*$", RegexOptions.Compiled);

    static ReplayAnalyzer()
    {
        // Henüz tanınmayan her sandık/konteyner blueprint'ini genel konteyner grubuna eşle: yeni sezon sandıkları
        // kod değişikliği olmadan okunur.
        NetFieldParser.AliasResolver = path =>
        {
            if (path.Contains(':') || path.EndsWith("_ClassNetCache", StringComparison.Ordinal))
            {
                return null;
            }

            var last = path[(path.LastIndexOf('/') + 1)..];

            // Altın hazine sandığı animasyonu ayrı bir aktördür; sandık olarak sayılmamalı.
            if (last.StartsWith("BGA_DelilahTreasureHuntAnimatedChest", StringComparison.Ordinal))
            {
                return "/DelilahTreasureHuntGameplay/Gameplay/Actors/BGA_DelilahTreasureHuntAnimatedChest.BGA_DelilahTreasureHuntAnimatedChest_C";
            }

            if (last.StartsWith("B_", StringComparison.Ordinal) && TreasureActor.IsMatch(last))
            {
                return GenericWeapon.GroupPath;
            }

            if (VehicleClass.IsMatch(last))
            {
                return GenericVehicle.GroupPath;
            }

            return path.Contains("/Containers/", StringComparison.OrdinalIgnoreCase)
                   || last.Contains("Chest", StringComparison.OrdinalIgnoreCase)
                ? GenericContainer.GroupPath
                : null;
        };
    }

    private readonly Dictionary<uint, string> _channelKey = new();
    private readonly Dictionary<uint, string> _channelLabel = new();
    private readonly Dictionary<uint, uint> _lastSearcher = new();
    private readonly HashSet<string> _searched = new();
    private readonly List<SearchEvent> _searches = new();
    private readonly Dictionary<uint, float> _channelOpenAt = new();
    private readonly Dictionary<uint, bool> _channelSearched = new();
    private readonly Dictionary<string, uint?> _lastOpener = new();

    private readonly Dictionary<uint, List<Pt>> _pawnTrack = new();
    private readonly Dictionary<uint, List<Pt>> _vehicleTrack = new();
    private readonly List<SeatSpan> _seatSpans = new();
    private readonly Dictionary<(uint Vehicle, int Seat), SeatSpan> _seated = new();
    private readonly List<OrphanDestroy> _orphans = new();
    private readonly HashSet<uint> _orphanChannels = new();
    private readonly List<TossEvent> _tosses = new();

    private readonly Dictionary<uint, uint> _pawnToPlayerState = new();
    private readonly Dictionary<uint, int> _playerStateToPlayerId = new();

    public ReplayAnalyzer() : base(null, ParseMode.Normal)
    {
    }

    internal static float Now => Unreal.Core.ReplayReader<FortniteReplay>.CurrentTime;

    private static void Track(Dictionary<uint, List<Pt>> tracks, uint id, FVector? location, float minInterval)
    {
        if (location == null)
        {
            return;
        }
        if (!tracks.TryGetValue(id, out var track))
        {
            tracks[id] = track = new List<Pt>();
        }
        if (track.Count == 0 || Now - track[^1].T >= minInterval)
        {
            track.Add(new Pt(Now, (float)location.X, (float)location.Y, (float)location.Z));
        }
    }

    private static Pt? At(List<Pt>? track, float t, float maxGap = 3f)
    {
        if (track == null || track.Count == 0)
        {
            return null;
        }
        int lo = 0, hi = track.Count - 1;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (track[mid].T < t) lo = mid + 1; else hi = mid;
        }
        var after = track[lo];
        if (lo == 0 || after.T <= t)
        {
            return Math.Abs(after.T - t) <= maxGap ? after : null;
        }
        var before = track[lo - 1];
        if (after.T - before.T <= maxGap)
        {
            var f = (t - before.T) / (after.T - before.T);
            return new Pt(t, before.X + (after.X - before.X) * f, before.Y + (after.Y - before.Y) * f, before.Z + (after.Z - before.Z) * f);
        }
        var nearest = t - before.T <= after.T - t ? before : after;
        return Math.Abs(nearest.T - t) <= maxGap ? nearest : null;
    }

    private Pt? PositionOf(uint pawn, float t)
    {
        foreach (var span in _seatSpans)
        {
            if (span.Pawn == pawn && t >= span.From - 0.5f && t <= span.To + 0.5f
                && At(_vehicleTrack.GetValueOrDefault(span.Vehicle), t) is { } inVehicle)
            {
                return inVehicle;
            }
        }
        return At(_pawnTrack.GetValueOrDefault(pawn), t);
    }

    private static float Distance(Pt a, Pt b) =>
        MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));

    /// <summary>Seviye yolu veya doğma konumu: aynı konteyner maç boyunca aynı anahtarı korur.</summary>
    private static bool StableKey(string key) =>
        !key.StartsWith("guid:", StringComparison.Ordinal) && !key.StartsWith("ch:", StringComparison.Ordinal);

    private static bool IsDynamicChest(string label) =>
        !label.StartsWith("Prop_", StringComparison.OrdinalIgnoreCase) && ChestKinds.IsChest(ChestKinds.Classify(label));

    private string PathOf(uint? guid)
    {
        if (guid is null || guid == 0)
        {
            return "";
        }
        return _netGuidCache.TryGetPathName(guid.Value, out var path) ? path : "";
    }

    private static string CleanName(string path)
    {
        var name = path[(path.LastIndexOf('/') + 1)..];
        var dot = name.LastIndexOf('.');
        if (dot >= 0)
        {
            name = name[(dot + 1)..];
        }
        return UaidSuffix.Replace(name, "");
    }

    protected override void OnChannelOpened(uint channelIndex, NetworkGUID? actor)
    {
        base.OnChannelOpened(channelIndex, actor);

        var channel = Channels[channelIndex];
        var archetype = PathOf(channel?.ArchetypeId);
        var actorPath = PathOf(channel?.ActorId);

        var label = CleanName(!string.IsNullOrEmpty(archetype) ? archetype : actorPath);
        if (!string.IsNullOrEmpty(actorPath))
        {
            // Haritaya yerleştirilmiş aktörlerin yolu her sandık için benzersizdir.
            _channelKey[channelIndex] = actorPath;
        }
        else if (channel?.Actor?.Location is { } loc && IsDynamicChest(label))
        {
            // Maç içinde doğan sandıklar (gömülü vb.) her görünürlükte yeni GUID alır; konumları sabittir.
            _channelKey[channelIndex] = $"{label}@{loc.X:F0},{loc.Y:F0}";
        }
        else
        {
            _channelKey[channelIndex] = $"guid:{channel?.ActorId}";
        }
        _channelLabel[channelIndex] = label;
        _channelOpenAt[channelIndex] = Now;
        _channelSearched.Remove(channelIndex);
        _lastSearcher.Remove(channelIndex);
        _orphanChannels.Remove(channelIndex);
    }

    protected override void OnExportRead(uint channelIndex, INetFieldExportGroup? exportGroup)
    {
        base.OnExportRead(channelIndex, exportGroup);

        switch (exportGroup)
        {
            case BaseContainer container:
                OnContainer(channelIndex, container);
                break;

            case GenericVehicle vehicle when vehicle.ReplicatedMovement is { } vehicleMove && Channels[channelIndex]?.ActorId is uint movingVehicle:
                Track(_vehicleTrack, movingVehicle, vehicleMove.Location, 0.25f);
                break;

            case SeatComponent seats when seats.PlayerSlots != null && Channels[channelIndex]?.ActorId is uint seatVehicle:
                for (var seat = 0; seat < seats.PlayerSlots.Length; seat++)
                {
                    if (seats.PlayerSlots[seat]?.Player is not uint occupant)
                    {
                        continue;
                    }
                    if (_seated.Remove((seatVehicle, seat), out var leaving))
                    {
                        leaving.To = Now;
                    }
                    if (occupant != 0)
                    {
                        var span = new SeatSpan(occupant, seatVehicle, Now);
                        _seated[(seatVehicle, seat)] = span;
                        _seatSpans.Add(span);
                    }
                }
                break;

            case FortPickup pickup when pickup.bTossedFromContainer == true && pickup.ItemDefinition != null:
                var item = pickup.ItemDefinition.Name;
                if (string.IsNullOrEmpty(item))
                {
                    item = CleanName(PathOf(pickup.ItemDefinition.Value));
                }
                if (!string.IsNullOrEmpty(item))
                {
                    var spawn = Channels[channelIndex]?.Actor?.Location;
                    _tosses.Add(new TossEvent(Now, item,
                        spawn == null ? null : new Pt(Now, (float)spawn.X, (float)spawn.Y, (float)spawn.Z), pickup.Owner));
                }
                break;

            case PlayerPawn pawn:
                var pawnId = Channels[channelIndex]?.ActorId;
                if (pawnId != null)
                {
                    if (pawn.PlayerState is uint ps && ps != 0)
                    {
                        _pawnToPlayerState[pawnId.Value] = ps;
                    }
                    if (pawn.ReplicatedMovement is { } pawnMove)
                    {
                        Track(_pawnTrack, pawnId.Value, pawnMove.Location, 0.5f);
                    }
                }
                break;

            case FortPlayerState state:
                var psId = Channels[channelIndex]?.ActorId;
                var playerId = state.PlayerId is null ? state.PlayerID : (int?)state.PlayerId;
                if (psId != null && playerId != null)
                {
                    _playerStateToPlayerId[psId.Value] = playerId.Value;
                }
                break;
        }
    }

    private void OnContainer(uint channelIndex, BaseContainer container)
    {
        if (container.SearchingPawn is uint searcher && searcher != 0)
        {
            _lastSearcher[channelIndex] = searcher;
        }

        var containerKey = _channelKey.GetValueOrDefault(channelIndex, $"ch:{channelIndex}");
        var containerLabel = _channelLabel.GetValueOrDefault(channelIndex, "?");
        // Kanal açılırken gelen durum "bayat"tır: konteyner, kaydeden oyuncuya görünür olmadan önce aranmıştır.
        var stale = Now - _channelOpenAt.GetValueOrDefault(channelIndex, float.MinValue) < 0.5f;

        if (container.bAlreadySearched == false)
        {
            // Maç içinde yeniden dolduruldu: tekrar açılabilir ve tekrar sayılabilir.
            _channelSearched[channelIndex] = false;
        }
        else if (container.bAlreadySearched == true && !_channelSearched.GetValueOrDefault(channelIndex))
        {
            _channelSearched[channelIndex] = true;
            var openedBy = _lastSearcher.TryGetValue(channelIndex, out var s) ? s : (uint?)null;
            // Bayat + aynı açan = aynı eski arama yeniden görülüyor; yeni bir açılış değil.
            var seenAgain = stale && (StableKey(containerKey)
                ? _lastOpener.TryGetValue(containerKey, out var previous) && previous == openedBy
                : _searched.Contains(containerKey));
            if (!seenAgain)
            {
                _searched.Add(containerKey);
                _lastOpener[containerKey] = openedBy;
                _searches.Add(new SearchEvent(Now, containerKey, containerLabel, openedBy, Stale: stale));
            }
        }
        else if (container.bDestroyed && container.SearchingPawn is uint destroyer && destroyer != 0
                 && (_searched.Add(containerKey) || (_channelSearched.TryGetValue(channelIndex, out var wasSearched) && !wasSearched)))
        {
            // Açılmak yerine kırıldı: ganimet yine düşer, sunucu SearchingPawn'ı kıran oyuncu yapar.
            _channelSearched[channelIndex] = true;
            _lastOpener[containerKey] = destroyer;
            _searches.Add(new SearchEvent(Now, containerKey, containerLabel, destroyer, Destroyed: true, Stale: stale));
        }
        else if (container.bDestroyed && container.SearchingPawn is null or 0 && !stale && StableKey(containerKey)
                 && (!_searched.Contains(containerKey) || (_channelSearched.TryGetValue(channelIndex, out var opened) && !opened))
                 && Channels[channelIndex]?.ActorId is uint orphanActor && _orphanChannels.Add(channelIndex))
        {
            // Kıranı belli olmayan kapalı sandık: sonda saçılan ganimetin yanında kim durduğuna bakılır.
            _orphans.Add(new OrphanDestroy(Now, containerKey, containerLabel, orphanActor));
        }
    }

    /// <summary>
    /// Kıranı bilinmeyen sandığı, ganimeti saçıldığında 4,5 m içinde en yakın duran oyuncuya verir.
    /// </summary>
    private void ResolveOrphans()
    {
        foreach (var orphan in _orphans)
        {
            var loot = _tosses
                .Where(x => x.Loc != null && x.Owner == orphan.Actor && Math.Abs(x.Time - orphan.Time) <= 2f)
                .Select(x => x.Loc!.Value)
                .ToList();
            if (loot.Count == 0)
            {
                continue;
            }

            uint nearest = 0;
            var nearestDistance = 450f;
            foreach (var pawn in _pawnToPlayerState.Keys)
            {
                if (PositionOf(pawn, orphan.Time) is not { } at)
                {
                    continue;
                }
                var d = loot.Min(l => Distance(at, l));
                if (d < nearestDistance)
                {
                    nearestDistance = d;
                    nearest = pawn;
                }
            }
            if (nearest == 0)
            {
                continue;
            }

            if (_searched.Add(orphan.ActorKey + "#orphan" + orphan.Time.ToString("F0")))
            {
                _searches.Add(new SearchEvent(orphan.Time, orphan.ActorKey, orphan.Label, nearest, Destroyed: true));
            }
        }
    }

    /// <summary>Replay akışını çözümler. Hatalı veride istisna fırlatabilir; çağıran taraf yakalar.</summary>
    public ReplayMatchResult Analyze(Stream stream, string fileName)
    {
        var replay = ReadReplay(stream);
        ResolveOrphans();

        var players = replay.PlayerData?.ToList() ?? new List<PlayerData>();

        // --- Kaydeden oyuncu ---
        uint? recorderPs = replay.GameData?.RecorderId;
        PlayerData? recorder = null;
        if (recorderPs != null && _playerStateToPlayerId.TryGetValue(recorderPs.Value, out var recorderPlayerId))
        {
            recorder = players.FirstOrDefault(p => p.Id == recorderPlayerId);
        }
        recorder ??= players.FirstOrDefault(p => p.IsReplayOwner);

        var result = new ReplayMatchResult
        {
            FileName = fileName,
            AnalyzerVersion = CurrentVersion,
            SessionId = !string.IsNullOrWhiteSpace(replay.GameData?.GameSessionId)
                ? replay.GameData!.GameSessionId
                : $"{replay.Info?.Timestamp:O}-{Path.GetFileName(fileName)}",
            // UtcTimeStartedMatch UTC'dir ama kütüphane onu Kind=Unspecified olarak verir; açıkça UTC işaretlenmeli
            // (yoksa yerel saat sanılıp 3 saat geri kayar). Info.Timestamp ise oyunun YEREL saatidir.
            MatchStartUtc = replay.GameData?.UtcTimeStartedMatch is DateTime utcStart && utcStart > DateTime.MinValue
                ? DateTime.SpecifyKind(utcStart, DateTimeKind.Utc)
                : (replay.Info != null && replay.Info.Timestamp > DateTime.MinValue
                    ? DateTime.SpecifyKind(replay.Info.Timestamp, DateTimeKind.Local).ToUniversalTime()
                    : null),
            DurationSeconds = replay.Info != null ? (int)(replay.Info.LengthInMs / 1000) : null,
            Playlist = replay.GameData?.CurrentPlaylist,
            Map = replay.Header?.LevelNamesAndTimes?.Select(l => l.Item1).FirstOrDefault(l => !string.IsNullOrEmpty(l)),
            IsCustomMatch = replay.GameData?.IsCustomMatch == true,
            GameVersion = replay.Header?.Branch,
            TeamSize = replay.GameData?.TeamSize,
            TotalPlayers = replay.TeamStats != null ? (int)replay.TeamStats.TotalPlayers : null,
            RecorderFound = recorder != null || recorderPs != null,
            RecorderName = recorder?.PlayerName,
        };

        // Kaydeden oyuncunun PlayerState aktör(ler)i
        var recorderStates = new HashSet<uint>();
        if (recorder?.Id != null)
        {
            foreach (var kv in _playerStateToPlayerId.Where(kv => kv.Value == recorder.Id))
            {
                recorderStates.Add(kv.Key);
            }
        }
        if (recorderStates.Count == 0 && recorderPs != null)
        {
            recorderStates.Add(recorderPs.Value);
        }

        if (recorderStates.Count == 0)
        {
            result.RecorderFound = false;
            result.Warnings.Add("Kaydeden oyuncu replay içinde bulunamadı; sandıklar kimseye yazılmadı.");
            return result;
        }

        var placement = recorder?.Placement ?? (replay.TeamStats != null ? (int?)replay.TeamStats.Position : null);
        result.Placement = placement is > 0 ? placement : null;

        // Kaydeden oyuncu
        var recorderPawns = _pawnToPlayerState.Where(kv => recorderStates.Contains(kv.Value)).Select(kv => kv.Key).ToHashSet();
        var recorderResult = BuildPlayer(recorderPawns);
        recorderResult.IsRecorder = true;
        recorderResult.Name = recorder?.PlayerName ?? "";
        recorderResult.EpicId = recorder?.EpicId;
        recorderResult.IsBot = recorder?.IsBot == true;
        recorderResult.Kills = recorder?.Kills is uint k ? (int)k : (replay.Stats != null ? (int?)replay.Stats.Eliminations : null);
        recorderResult.Placement = result.Placement;
        result.Players.Add(recorderResult);

        // Takım arkadaşları (aynı TeamIndex)
        if (recorder?.TeamIndex is int team)
        {
            foreach (var mate in players.Where(p => p.TeamIndex == team && p.Id != recorder.Id))
            {
                var mateStates = _playerStateToPlayerId.Where(kv => kv.Value == mate.Id).Select(kv => kv.Key).ToHashSet();
                var matePawns = _pawnToPlayerState.Where(kv => mateStates.Contains(kv.Value)).Select(kv => kv.Key).ToHashSet();
                var mateResult = BuildPlayer(matePawns);
                mateResult.Name = mate.PlayerName ?? (mate.IsBot ? "Bot" : "");
                mateResult.EpicId = mate.EpicId;
                mateResult.IsBot = mate.IsBot;
                mateResult.Kills = mate.Kills is uint mk ? (int)mk : null;
                mateResult.Placement = result.Placement;
                result.Players.Add(mateResult);
            }
        }

        return result;
    }

    private ReplayPlayerResult BuildPlayer(HashSet<uint> pawns)
    {
        var player = new ReplayPlayerResult();
        var interactions = _searches
            .Where(s => s.SearcherPawn is uint p && pawns.Contains(p) && (!s.Stale || StableKey(s.ActorKey)))
            .OrderBy(s => s.Time);

        foreach (var s in interactions)
        {
            var kind = ChestKinds.Classify(s.Label);
            if (ChestKinds.IsChest(kind))
            {
                player.ChestsTotal++;
                player.ChestsByKind[kind] = player.ChestsByKind.GetValueOrDefault(kind) + 1;
                if (s.Destroyed)
                {
                    player.DestroyedChests++;
                }
                player.ChestEvents.Add(new ChestEvent
                {
                    TimeSeconds = MathF.Round(s.Time, 1),
                    Kind = kind,
                    Blueprint = s.Label,
                    Destroyed = s.Destroyed,
                });
            }
            else if (kind == ChestKinds.AmmoBox)
            {
                player.AmmoBoxes++;
            }
            else
            {
                player.OtherContainers++;
            }
        }

        return player;
    }
}
