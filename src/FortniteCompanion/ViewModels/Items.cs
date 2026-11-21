using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using FortniteCompanion.Core.Data;
using FortniteCompanion.Core.Services;

namespace FortniteCompanion.ViewModels;

internal static class Fmt
{
    public static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static string N(int v) => v.ToString("N0", Tr);
    public static string D1(double v) => v.ToString("0.0", Tr);

    public static string Date(DateTime? utc) =>
        utc is DateTime d ? DateTime.SpecifyKind(d, DateTimeKind.Utc).ToLocalTime().ToString("dd.MM.yyyy HH:mm", Tr) : "—";

    public static string Duration(int? seconds) =>
        seconds is int s && s > 0 ? $"{s / 60} dk {s % 60:00} sn" : "—";

    /// <summary>"Playlist_NoBuildBR_Squad" → "Sıfır İnşa · Squad" gibi okunur ad (bilinmiyorsa ham ad).</summary>
    public static string Mode(string? playlist)
    {
        if (string.IsNullOrWhiteSpace(playlist))
        {
            return "—";
        }
        var p = playlist.Replace("Playlist_", "", StringComparison.OrdinalIgnoreCase);
        var build = p.Contains("NoBuild", StringComparison.OrdinalIgnoreCase) ? "Sıfır İnşa" : "Battle Royale";
        string size = p.Contains("Solo", StringComparison.OrdinalIgnoreCase) ? "Solo"
            : p.Contains("Duo", StringComparison.OrdinalIgnoreCase) ? "Duo"
            : p.Contains("Trio", StringComparison.OrdinalIgnoreCase) ? "Trio"
            : p.Contains("Squad", StringComparison.OrdinalIgnoreCase) ? "Squad" : "";
        if (p.StartsWith("Default", StringComparison.OrdinalIgnoreCase) || p.StartsWith("NoBuildBR", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("Trios", StringComparison.OrdinalIgnoreCase))
        {
            return size.Length > 0 ? $"{build} · {size}" : build;
        }
        return p;
    }
}

/// <summary>Fortnite nadirlik renkleri (sıradan → efsanevi). Fırçalar dondurulmuştur, iş parçacıkları arası güvenlidir.</summary>
public static class Rarity
{
    public static readonly Color[] Main =
    {
        Color.FromRgb(0x9A, 0xA4, 0xB4), // sıradan
        Color.FromRgb(0x5C, 0xC9, 0x3B), // nadir olmayan (yeşil)
        Color.FromRgb(0x3F, 0xA9, 0xFF), // nadir (mavi)
        Color.FromRgb(0xB1, 0x5B, 0xE6), // destansı (mor)
        Color.FromRgb(0xF5, 0xB3, 0x23), // efsanevi (altın)
    };

    private static readonly Color[] Dark =
    {
        Color.FromRgb(0x3B, 0x43, 0x52),
        Color.FromRgb(0x1C, 0x5E, 0x14),
        Color.FromRgb(0x10, 0x3A, 0x8C),
        Color.FromRgb(0x46, 0x1A, 0x7E),
        Color.FromRgb(0x8A, 0x3D, 0x0C),
    };

    public static Brush Solid(int tier)
    {
        var b = new SolidColorBrush(Main[Math.Clamp(tier, 0, 4)]);
        b.Freeze();
        return b;
    }

    /// <summary>Fortnite eşya kartı gibi üstten aydınlanan radyal degrade.</summary>
    public static Brush Tile(int tier)
    {
        tier = Math.Clamp(tier, 0, 4);
        var b = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.05),
            Center = new Point(0.5, 0.05),
            RadiusX = 1.1,
            RadiusY = 1.0,
        };
        b.GradientStops.Add(new GradientStop(Main[tier], 0));
        b.GradientStops.Add(new GradientStop(Dark[tier], 1));
        b.Freeze();
        return b;
    }

    /// <summary>Maçtaki sandık sayısına göre nadirlik.</summary>
    public static int ForChests(int chests) => chests switch
    {
        < 8 => 0,
        < 13 => 1,
        < 18 => 2,
        < 25 => 3,
        _ => 4,
    };

    public static string Name(int tier) => tier switch
    {
        0 => "SIRADAN",
        1 => "NADİR OLMAYAN",
        2 => "NADİR",
        3 => "DESTANSI",
        _ => "EFSANEVİ",
    };
}

/// <summary>Maç geçmişi tablosundaki bir satır (oyuncu × maç).</summary>
public sealed class MatchItem
{
    public string SessionId { get; init; } = "";
    public string Player { get; init; } = "";
    public DateTime? StartedUtc { get; init; }
    public string Date => Fmt.Date(StartedUtc);
    public int Chests { get; init; }
    public string ChestsText => Counted ? Fmt.N(Chests) : $"({Fmt.N(Chests)})";
    public int? Kills { get; init; }
    public string KillsText => Kills?.ToString(Fmt.Tr) ?? "—";
    public string Mode { get; init; } = "—";
    public string Duration { get; init; } = "—";
    public string Placement { get; init; } = "—";
    public DataSource SourceKind { get; init; }
    public string Source => DataSourceText.Short(SourceKind);
    public string SourceLong => DataSourceText.Label(SourceKind);
    public string Reliability { get; init; } = "";
    public bool Counted { get; init; }
    public string Flag { get; init; } = "";
    public bool HasFlag => Flag.Length > 0;
    public string Kinds { get; init; } = "";
    public int AmmoBoxes { get; init; }
    public int OtherContainers { get; init; }
    public bool BeforeCutoff { get; init; }
    public bool Excluded { get; init; }
    public bool IsManual => SourceKind == DataSource.Manual;
    public int Tier => Rarity.ForChests(Chests);
    public Brush RarityBrush => Rarity.Solid(Tier);
    public Brush RarityTile => Rarity.Tile(Tier);

    public static MatchItem From(Player player, PlayerMatchStat m, MatchRow? row) => new()
    {
        SessionId = m.SessionId,
        Player = player.DisplayName,
        StartedUtc = m.StartedUtc,
        Chests = m.Chests,
        Kills = m.Kills,
        Mode = Fmt.Mode(m.Playlist),
        Duration = Fmt.Duration(m.DurationSeconds),
        Placement = m.Placement is int p ? "#" + p.ToString(Fmt.Tr) : "—",
        SourceKind = m.Source,
        Reliability = m.Source switch
        {
            DataSource.Replay => "Yüksek",
            DataSource.ReplayPartial => $"Kısmi ({m.ObserverCount} replay)",
            DataSource.Manual => "Kullanıcı girişi",
            _ => "—",
        },
        Counted = m.Counted,
        Flag = m.Flag ?? "",
        Kinds = string.Join(", ", m.ChestsByKind.OrderByDescending(k => k.Value).Select(k => $"{k.Key}: {k.Value}")),
        AmmoBoxes = m.AmmoBoxes,
        OtherContainers = m.OtherContainers,
        BeforeCutoff = m.BeforeCutoff,
        Excluded = row?.Excluded == true,
    };
}

/// <summary>Arkadaş karşılaştırma kartı.</summary>
public sealed class PlayerCardItem
{
    public long PlayerId { get; init; }
    public string Name { get; init; } = "";
    public string EpicNames { get; init; } = "";
    public bool IsSelf { get; init; }
    public bool Hidden { get; init; }
    public bool FromServer { get; init; }
    public bool IsLocal => PlayerId != 0;
    public Visibility LocalVisibility => IsLocal ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ServerVisibility => FromServer ? Visibility.Visible : Visibility.Collapsed;
    public string ServerTag => FromServer ? "  ☁ sunucu" : "";
    public string HideButtonText => Hidden ? "Göster" : "Gizle";
    public double RowOpacity => Hidden ? 0.55 : 1.0;
    public string SelfBadge => IsSelf ? "BU BİLGİSAYAR" : "";
    public int TotalChestsValue { get; init; }
    public string TotalChests => Fmt.N(TotalChestsValue);
    public string TotalMatches { get; init; } = "";
    public string AvgChests { get; init; } = "";
    public string TotalKills { get; init; } = "";
    public string AvgKills { get; init; } = "";
    public string Best { get; init; } = "";
    public string Week { get; init; } = "";
    public string BaselineText { get; init; } = "";
    public string TrackedText { get; init; } = "";
    public string SourceText { get; init; } = "";
    public double BarRatio { get; init; }
    public double BarWidth => Math.Max(4, BarRatio * 260);
    public double BarWidthSmall => Math.Max(3, BarRatio * 120);
    public string Warning { get; init; } = "";
    public bool HasWarning => Warning.Length > 0;

    /// <summary>Sandık sıralamasındaki yer (1 = en çok).</summary>
    public int Rank { get; init; }
    public string RankText => "#" + Rank.ToString(Fmt.Tr);
    public int Tier => Rank switch { 1 => 4, 2 => 3, 3 => 2, 4 => 1, _ => 0 };
    public Brush TileBrush => Rarity.Tile(Tier);
    public Brush AccentBrush => Rarity.Solid(Tier);
    public string TierName => Rarity.Name(Tier);
    public Visibility SelfVisibility => IsSelf ? Visibility.Visible : Visibility.Collapsed;
}

public sealed class UnmatchedItem
{
    public string Name { get; init; } = "";
    public string Info { get; init; } = "";
}

public sealed class EditLogItem
{
    public string When { get; init; } = "";
    public string Entity { get; init; } = "";
    public string Key { get; init; } = "";
    public string Field { get; init; } = "";
    public string Old { get; init; } = "";
    public string New { get; init; } = "";
    public string Reason { get; init; } = "";
}

public sealed record ChartPoint(string Label, double Value, string? Tooltip = null);

public sealed class ChartSeries
{
    public string Name { get; init; } = "";
    public string Color { get; init; } = "#FFFFC83D";
    public List<double> Values { get; init; } = new();
}
