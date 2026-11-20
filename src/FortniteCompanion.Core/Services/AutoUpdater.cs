using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace FortniteCompanion.Core.Services;

/// <summary>Bir GitHub sürümü (release).</summary>
public sealed record ReleaseInfo(Version Version, string Tag, string AssetUrl, long Size, string? Sha256, string? Notes, string? HtmlUrl);

/// <summary>
/// GitHub Releases üzerinden otomatik güncelleme.
///
/// Akış: api.github.com/repos/{depo}/releases/latest okunur → sürüm daha yeniyse "FortniteCompanion.exe" eki
/// EXE'nin yanına ".new" uzantısıyla indirilir → boyut (ve GitHub verdiyse SHA-256) doğrulanır → çalışan EXE
/// ".old" olarak yeniden adlandırılır (Windows çalışan dosyanın adının değiştirilmesine izin verir), yeni dosya
/// asıl adı alır → yeni sürüm "--after-update" ile başlatılır, eskisi kapanır → yeni sürüm açılışta ".old"u siler.
///
/// Yalnızca kullanıcının kendi GitHub deposundan, kendi derlediği sürüm indirilir. Oyunla ilgisi yoktur.
/// </summary>
public static class AutoUpdater
{
    public const string AssetName = "FortniteCompanion.exe";
    public const string AfterUpdateArg = "--after-update";

    public static Version CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>Derleme sırasında gömülen depo (-p:UpdateRepo=kullanici/depo). Yoksa null.</summary>
    public static string? EmbeddedRepo =>
        Assembly.GetEntryAssembly()?
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "UpdateRepo")?.Value is { Length: > 0 } repo && repo.Contains('/')
            ? repo.Trim()
            : null;

    public static string? EffectiveRepo(AppSettings s) =>
        !string.IsNullOrWhiteSpace(s.UpdateRepo) && s.UpdateRepo!.Contains('/') ? s.UpdateRepo.Trim() : EmbeddedRepo;

    private static HttpClient CreateClient(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FortniteCompanion", CurrentVersion.ToString()));
        return http;
    }

    /// <summary>Son sürümü okur. Uygun EXE eki yoksa veya istek başarısızsa null.</summary>
    public static async Task<ReleaseInfo?> GetLatestAsync(string repo, CancellationToken ct = default)
    {
        using var http = CreateClient(TimeSpan.FromSeconds(15));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var resp = await http.GetAsync($"https://api.github.com/repos/{repo}/releases/latest", ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            AppLog.Warn($"Güncelleme denetimi: GitHub {(int)resp.StatusCode} döndürdü ({repo}).");
            return null;
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        var root = doc.RootElement;
        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        if (string.IsNullOrWhiteSpace(tag) || !Version.TryParse(tag.TrimStart('v', 'V'), out var version))
        {
            return null;
        }
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean())
        {
            return null;
        }

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        foreach (var a in assets.EnumerateArray())
        {
            var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (!string.Equals(name, AssetName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
            var size = a.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var l) ? l : 0;
            string? sha = null;
            if (a.TryGetProperty("digest", out var dg) && dg.GetString() is { } digest
                && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                sha = digest["sha256:".Length..];
            }
            if (string.IsNullOrEmpty(url) || size <= 0)
            {
                return null;
            }
            return new ReleaseInfo(version, tag, url, size, sha,
                root.TryGetProperty("body", out var b) ? b.GetString() : null,
                root.TryGetProperty("html_url", out var h) ? h.GetString() : null);
        }
        return null;
    }

    public static bool IsNewer(ReleaseInfo r) => r.Version > CurrentVersion;

    /// <summary>EXE'nin bulunduğu klasöre yazılabiliyor mu? (ör. Program Files'ta değilse)</summary>
    public static bool CanWriteNextTo(string exePath)
    {
        try
        {
            var probe = Path.Combine(Path.GetDirectoryName(exePath)!, $".fc-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Yeni EXE'yi "&lt;exe&gt;.new" olarak indirir ve doğrular. Doğrulanan dosyanın yolunu döndürür.</summary>
    public static async Task<string> DownloadAsync(ReleaseInfo r, string exePath, CancellationToken ct = default)
    {
        var target = exePath + ".new";
        var temp = target + ".part";
        try
        {
            using var http = CreateClient(TimeSpan.FromMinutes(10));
            using (var resp = await http.GetAsync(r.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var dst = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None);
                await src.CopyToAsync(dst, ct).ConfigureAwait(false);
            }

            var info = new FileInfo(temp);
            if (info.Length != r.Size)
            {
                throw new InvalidDataException($"İndirilen dosya boyutu tutmuyor ({info.Length} / {r.Size}).");
            }
            if (r.Sha256 != null)
            {
                await using var fs = File.OpenRead(temp);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false));
                if (!string.Equals(hash, r.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("İndirilen dosyanın SHA-256 özeti GitHub'daki ile eşleşmiyor.");
                }
            }
            // Basit biçim denetimi: Windows çalıştırılabilir dosyası "MZ" ile başlar.
            await using (var fs = File.OpenRead(temp))
            {
                if (fs.ReadByte() != 'M' || fs.ReadByte() != 'Z')
                {
                    throw new InvalidDataException("İndirilen dosya bir Windows programı değil.");
                }
            }

            File.Move(temp, target, overwrite: true);
            return target;
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* yoksay */ }
        }
    }

    /// <summary>
    /// İndirilen sürümü yerine koyar ve başlatır. Başarılıysa true döner; çağıran uygulamayı hemen kapatmalıdır.
    /// Herhangi bir adım başarısız olursa eski EXE geri yüklenir.
    /// </summary>
    public static bool InstallAndRestart(string exePath, string newFile, bool minimized)
    {
        var old = exePath + ".old";
        try
        {
            if (File.Exists(old))
            {
                File.Delete(old);
            }
            File.Move(exePath, old);
            try
            {
                File.Move(newFile, exePath);
            }
            catch
            {
                File.Move(old, exePath); // geri al
                throw;
            }

            var args = $"{AfterUpdateArg} {Environment.ProcessId}" + (minimized ? " --minimized" : "");
            Process.Start(new ProcessStartInfo(exePath, args) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exePath)! });
            AppLog.Info($"Güncelleme kuruldu, yeni sürüm başlatıldı: {exePath}");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("Güncelleme kurulamadı", ex);
            try
            {
                if (!File.Exists(exePath) && File.Exists(old))
                {
                    File.Move(old, exePath);
                }
            }
            catch
            {
                // yoksay
            }
            return false;
        }
    }

    /// <summary>Açılışta: önceki sürümden kalan ".old" ve yarım indirmeleri siler.</summary>
    public static void Cleanup(string exePath)
    {
        foreach (var leftover in new[] { exePath + ".old", exePath + ".new.part" })
        {
            try
            {
                if (File.Exists(leftover))
                {
                    File.Delete(leftover);
                }
            }
            catch
            {
                // eski süreç henüz kapanmadıysa bir sonraki açılışta silinir
            }
        }
    }

    /// <summary>Güncellemeden sonra açılıyorsa, eski sürecin kapanmasını bekler (kısayollar ve tek örnek kilidi için).</summary>
    public static void WaitForPreviousInstance(string[] args)
    {
        var i = Array.IndexOf(args, AfterUpdateArg);
        if (i < 0 || i + 1 >= args.Length || !int.TryParse(args[i + 1], out var pid))
        {
            return;
        }
        try
        {
            using var p = Process.GetProcessById(pid);
            p.WaitForExit(20000);
        }
        catch (ArgumentException)
        {
            // zaten kapanmış
        }
        catch (InvalidOperationException)
        {
            // zaten kapanmış
        }
    }
}
