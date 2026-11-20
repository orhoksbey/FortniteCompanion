// ReplayScan: replay okuyucusunu gerçek dosyalarla doğrulamak için komut satırı aracı.
// Kullanım:
//   ReplayScan                       -> varsayılan Demos klasöründeki tüm .replay dosyaları
//   ReplayScan dosya.replay [...]    -> belirtilen dosyalar
//   ReplayScan --json dosya.replay   -> ayrıntılı JSON çıktısı
// Hiçbir şey yazmaz; yalnızca okur ve sonucu ekrana basar.
using System.Diagnostics;
using System.Text.Json;
using FortniteCompanion.Core.Replay;
using FortniteCompanion.Core.Services;

var json = args.Contains("--json");
var files = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
if (files.Count == 0)
{
    var folder = AppPaths.DefaultReplayFolder;
    if (!Directory.Exists(folder))
    {
        Console.Error.WriteLine("Replay klasörü bulunamadı: " + folder);
        return 1;
    }
    files = Directory.GetFiles(folder, "*.replay").OrderBy(File.GetLastWriteTimeUtc).ToList();
    if (files.Count == 0)
    {
        Console.Error.WriteLine("Klasör boş: " + folder);
        return 1;
    }
}

var failures = 0;
foreach (var file in files)
{
    var sw = Stopwatch.StartNew();
    try
    {
        if (ReplayHeaderProbe.IsLive(file) == true && ReplayHeaderProbe.KeyMissing(file))
        {
            Console.WriteLine($"{Path.GetFileName(file)}: maç hâlâ kaydediliyor (anahtar yok), atlandı.");
            continue;
        }
        var r = ReplayProcessor.Analyze(file, TimeSpan.FromMinutes(3));
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true }));
            continue;
        }
        Console.WriteLine($"{Path.GetFileName(file)}  [{sw.Elapsed.TotalSeconds:0.0} sn]");
        Console.WriteLine($"  Oturum: {r.SessionId}  Başlangıç(UTC): {r.MatchStartUtc:yyyy-MM-dd HH:mm}  Mod: {r.Playlist}  Sıra: {r.Placement}");
        if (!r.RecorderFound)
        {
            Console.WriteLine("  Kaydeden oyuncu bulunamadı: " + string.Join("; ", r.Warnings));
            continue;
        }
        foreach (var p in r.Players)
        {
            var kinds = string.Join(", ", p.ChestsByKind.Select(k => $"{k.Key}={k.Value}"));
            Console.WriteLine($"  {(p.IsRecorder ? "*" : " ")} {p.Name,-20} sandık={p.ChestsTotal,3} (kırılan {p.DestroyedChests})  mühimmat={p.AmmoBoxes,2}  diğer={p.OtherContainers,2}  kill={p.Kills?.ToString() ?? "-"}  [{kinds}]{(p.IsRecorder ? "" : "  (kısmi)")}");
        }
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"{Path.GetFileName(file)}: OKUNAMADI — {ex.GetType().Name}: {ex.Message}");
    }
}
Console.WriteLine("* = replay sahibi (tam sayım). Diğerleri takım arkadaşı: yalnızca kaydedenin yakınında açılanlar.");
return failures == 0 ? 0 : 2;
