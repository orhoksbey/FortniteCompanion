using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FortniteCompanion.Core.Data;

namespace FortniteCompanion.Core.Services;

/// <summary>Arkadaşlar arası paylaşım ve yedek için JSON biçimi (sürüm 1).</summary>
public sealed class ExportFile
{
    public string Format { get; set; } = "FortniteCompanion";
    public int Version { get; set; } = 1;
    public DateTime ExportedUtc { get; set; }
    public string? ExportedBy { get; set; }
    public List<ExportPlayer> Players { get; set; } = new();
    public List<ExportMatch> Matches { get; set; } = new();
}

public sealed class ExportPlayer
{
    public string DisplayName { get; set; } = "";
    public string EpicNames { get; set; } = "";
    public int? BaselineMatches { get; set; }
    public int? BaselineChests { get; set; }
    public int? BaselineKills { get; set; }
    public DateTime? BaselineCutoffUtc { get; set; }

    /// <summary>Geçmiş değerin en son düzenlendiği an: içe aktarmada daha yeni olan kazanır.</summary>
    public DateTime? BaselineUpdatedUtc { get; set; }
}

public sealed class ExportMatch
{
    public MatchRow Match { get; set; } = new();
    public List<MatchPlayerRow> Rows { get; set; } = new();
}

public sealed class ImportReport
{
    public int NewMatches { get; set; }
    public int NewRows { get; set; }
    public int SkippedRows { get; set; }
    public int NewPlayers { get; set; }
    public int BaselinesUpdated { get; set; }
    public List<string> Warnings { get; } = new();

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Yeni maç: {NewMatches}, yeni oyuncu-maç satırı: {NewRows}, zaten var olan (atlandı): {SkippedRows}, yeni oyuncu: {NewPlayers}, güncellenen geçmiş değer: {BaselinesUpdated}.");
        foreach (var w in Warnings)
        {
            sb.AppendLine("• " + w);
        }
        return sb.ToString().TrimEnd();
    }
}

public static class ImportExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static ExportFile BuildExport(Database db, bool hideUnknownNames, string? exportedBy)
    {
        var players = db.GetPlayers();
        var baselines = db.GetBaselines();
        var rows = db.GetMatchPlayers();
        var file = new ExportFile { ExportedUtc = DateTime.UtcNow, ExportedBy = exportedBy };

        foreach (var p in players)
        {
            var b = baselines.FirstOrDefault(x => x.PlayerId == p.Id);
            file.Players.Add(new ExportPlayer
            {
                DisplayName = p.DisplayName,
                EpicNames = p.EpicNames,
                BaselineMatches = b?.Matches,
                BaselineChests = b?.Chests,
                BaselineKills = b?.Kills,
                BaselineCutoffUtc = b?.CutoffUtc,
                BaselineUpdatedUtc = b?.UpdatedUtc,
            });
        }

        foreach (var m in db.GetMatches())
        {
            var matchRows = rows.Where(r => r.SessionId == m.SessionId).ToList();
            if (hideUnknownNames)
            {
                // Gizlilik: takipte olmayan oyuncuların (rastgele takım arkadaşları) adları paylaşılmaz,
                // gözlemci adı ise tekilleştirme için gereklidir.
                matchRows = matchRows.Where(r => r.IsRecorder || players.Any(p => p.Owns(r.PlayerName))).ToList();
            }
            if (matchRows.Count > 0)
            {
                file.Matches.Add(new ExportMatch { Match = m, Rows = matchRows });
            }
        }
        return file;
    }

    public static void ExportJson(Database db, string path, bool hideUnknownNames, string? exportedBy)
    {
        var export = BuildExport(db, hideUnknownNames, exportedBy);
        File.WriteAllText(path, JsonSerializer.Serialize(export, JsonOptions), new UTF8Encoding(false));
        db.Log("export", Path.GetFileName(path), "json", null, $"{export.Matches.Count} maç", "JSON dışa aktarma");
    }

    public static ExportFile ReadJson(string path)
    {
        var file = JsonSerializer.Deserialize<ExportFile>(File.ReadAllText(path), JsonOptions)
                   ?? throw new InvalidDataException("Dosya boş.");
        if (!string.Equals(file.Format, "FortniteCompanion", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Bu dosya bir Fortnite Companion dışa aktarımı değil.");
        }
        if (file.Version > 1)
        {
            throw new InvalidDataException($"Dosya daha yeni bir sürümle oluşturulmuş (v{file.Version}). Uygulamayı güncelleyin.");
        }
        return file;
    }

    /// <summary>
    /// İçe aktarır. Maçlar oturum kimliğiyle, satırlar (maç, gözlemci, oyuncu) üçlüsüyle tekilleştirilir;
    /// var olan veriler asla üzerine yazılmaz. Geçmiş başlangıç değerleri farklıysa yalnızca uyarı verilir.
    /// </summary>
    public static ImportReport Import(Database db, ExportFile file, string sourceName)
    {
        var report = new ImportReport();
        var players = db.GetPlayers();
        var baselines = db.GetBaselines();

        foreach (var ep in file.Players)
        {
            // Önce görünen ad, sonra ortak Epic adı ile eşleştir (biri "Orhan", diğeri "OrhanTR" yazmış olabilir).
            var fileEpics = ep.EpicNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var local = players.FirstOrDefault(p => string.Equals(p.DisplayName, ep.DisplayName, StringComparison.OrdinalIgnoreCase))
                        ?? players.FirstOrDefault(p => fileEpics.Any(e => p.Owns(e)));
            if (local == null)
            {
                local = db.AddPlayer(ep.DisplayName, ep.EpicNames);
                players.Add(local);
                report.NewPlayers++;
                if (ep.BaselineCutoffUtc is DateTime cut)
                {
                    db.UpdateBaseline(local.Id, ep.BaselineMatches ?? 0, ep.BaselineChests ?? 0, ep.BaselineKills ?? 0, cut,
                        "İçe aktarıldı: " + sourceName, "JSON içe aktarma (yeni oyuncu)");
                }
                continue;
            }

            // Epic adlarını birleştir (bilgi kaybı olmaz).
            var merged = local.EpicNameList.Concat(ep.EpicNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (merged.Count != local.EpicNameList.Count())
            {
                db.SetEpicNames(local.Id, string.Join(", ", merged), "JSON içe aktarma: " + sourceName);
            }

            var b = baselines.FirstOrDefault(x => x.PlayerId == local.Id);
            if (b == null && ep.BaselineCutoffUtc is DateTime firstCut)
            {
                // Yerelde geçmiş değer yok: dosyadakini al.
                db.UpdateBaseline(local.Id, ep.BaselineMatches ?? 0, ep.BaselineChests ?? 0, ep.BaselineKills ?? 0, firstCut,
                    "İçe aktarıldı: " + sourceName, "JSON içe aktarma (yerelde geçmiş değer yoktu)");
                report.BaselinesUpdated++;
                continue;
            }
            if (b != null && ep.BaselineChests != null
                && (b.Chests != ep.BaselineChests || b.Matches != ep.BaselineMatches || b.Kills != ep.BaselineKills
                    || (ep.BaselineCutoffUtc is DateTime fc && fc != b.CutoffUtc)))
            {
                if (ep.BaselineUpdatedUtc is DateTime fileUpdated && fileUpdated > b.UpdatedUtc && ep.BaselineCutoffUtc is DateTime newCut)
                {
                    // Dosyadaki değer daha yeni düzenlenmiş: onu al (eski değer değişiklik geçmişinde kalır).
                    db.UpdateBaseline(local.Id, ep.BaselineMatches ?? b.Matches, ep.BaselineChests ?? b.Chests, ep.BaselineKills ?? b.Kills,
                        newCut, b.Note, $"İçe aktarma ({sourceName}): daha yeni geçmiş değer");
                    report.BaselinesUpdated++;
                    report.Warnings.Add($"{local.DisplayName}: geçmiş değerler dosyadaki daha yeni değerlerle güncellendi " +
                                        $"({b.Matches}/{b.Chests}/{b.Kills} → {ep.BaselineMatches}/{ep.BaselineChests}/{ep.BaselineKills}).");
                }
                else
                {
                    report.Warnings.Add($"{local.DisplayName}: dosyadaki geçmiş değerler farklı ama daha eski (yerel {b.Matches} maç/{b.Chests} sandık/{b.Kills} kill, " +
                                        $"dosyada {ep.BaselineMatches}/{ep.BaselineChests}/{ep.BaselineKills}). Yerel değerler korundu.");
                }
            }
        }

        var existingMatches = db.GetMatches().Select(m => m.SessionId).ToHashSet(StringComparer.Ordinal);
        foreach (var em in file.Matches)
        {
            if (string.IsNullOrWhiteSpace(em.Match.SessionId))
            {
                continue;
            }
            var isNew = !existingMatches.Contains(em.Match.SessionId);
            foreach (var r in em.Rows)
            {
                r.ImportedFrom = sourceName;
            }
            // İçe aktarılan maçın hariç tutma durumu yerelde korunur; yeni maçlar dosyadaki gibi eklenir.
            var added = db.ImportRows(em.Match, em.Rows);
            report.NewRows += added;
            report.SkippedRows += em.Rows.Count - added;
            if (isNew)
            {
                report.NewMatches++;
                existingMatches.Add(em.Match.SessionId);
            }
        }

        // Eski sürümden (0.1.x) gelen dosyalardaki başlangıç saatlerini düzelt.
        db.RepairLegacyStartTimes();
        db.Log("import", sourceName, "json", null, report.ToString(), "JSON içe aktarma");
        return report;
    }

    /// <summary>Oyuncu başına, tekilleştirilmiş maç satırlarını CSV olarak yazar (Excel uyumlu, UTF-8 BOM, ';').</summary>
    public static void ExportCsv(IReadOnlyList<PlayerStats> stats, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Oyuncu;Tarih (yerel);Oturum;Sandık;Kill;Kaynak;Mod;Süre (sn);Sıra;Mühimmat kutusu;Diğer konteyner;Sayıldı;Not");
        foreach (var p in stats)
        {
            sb.AppendLine(string.Join(";",
                Csv(p.Player.DisplayName), "", "GEÇMİŞ", p.BaselineChests.ToString(CultureInfo.InvariantCulture),
                p.BaselineKills.ToString(CultureInfo.InvariantCulture), Csv(DataSourceText.Label(DataSource.Baseline)),
                "", "", "", "", "", "evet", Csv($"{p.BaselineMatches} maç (başlangıç değeri)")));

            foreach (var m in p.Matches.OrderBy(m => m.StartedUtc))
            {
                sb.AppendLine(string.Join(";",
                    Csv(p.Player.DisplayName),
                    m.StartedUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "",
                    Csv(m.SessionId),
                    m.Chests.ToString(CultureInfo.InvariantCulture),
                    m.Kills?.ToString(CultureInfo.InvariantCulture) ?? "",
                    Csv(m.SourceLabel),
                    Csv(m.Playlist ?? ""),
                    m.DurationSeconds?.ToString(CultureInfo.InvariantCulture) ?? "",
                    m.Placement?.ToString(CultureInfo.InvariantCulture) ?? "",
                    m.AmmoBoxes.ToString(CultureInfo.InvariantCulture),
                    m.OtherContainers.ToString(CultureInfo.InvariantCulture),
                    m.Counted ? "evet" : "hayır",
                    Csv(m.Flag ?? "")));
            }
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    private static string Csv(string s) =>
        s.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}
