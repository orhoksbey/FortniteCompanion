using System.Globalization;
using FortniteCompanion.Core.Data;

namespace FortniteCompanion.Core.Services;

public static class BackupService
{
    private const string Prefix = "companion-";

    /// <summary>Bugün henüz yedek alınmadıysa alır; en yeni <paramref name="keep"/> yedeği tutar.</summary>
    public static string? RunIfDue(Database db, string folder, int keep)
    {
        Directory.CreateDirectory(folder);
        var today = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        if (Directory.GetFiles(folder, $"{Prefix}{today}-*.db").Length > 0)
        {
            return null;
        }
        return RunNow(db, folder, keep);
    }

    public static string RunNow(Database db, string folder, int keep)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{Prefix}{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.db");
        db.BackupTo(path);
        Prune(folder, keep);
        AppLog.Info("Yedek alındı: " + path);
        return path;
    }

    private static void Prune(string folder, int keep)
    {
        if (keep < 1)
        {
            return;
        }
        var old = new DirectoryInfo(folder).GetFiles($"{Prefix}*.db")
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Skip(keep);
        foreach (var f in old)
        {
            try { f.Delete(); } catch (IOException) { /* sonraki sefere */ }
        }
    }
}
