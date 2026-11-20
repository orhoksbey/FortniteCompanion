using System.Globalization;
using System.Text;

namespace FortniteCompanion.Core.Services;

/// <summary>Basit, dosyaya yazan hata günlüğü (%APPDATA%\FortniteCompanion\logs\app-YYYY-MM-DD.log).</summary>
public static class AppLog
{
    private static readonly object Gate = new();
    public static string? Folder { get; set; }

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message) => Write("UYARI", message, null);
    public static void Error(string message, Exception? ex = null) => Write("HATA", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            var line = new StringBuilder()
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                .Append(" [").Append(level).Append("] ")
                .Append(message);
            if (ex != null)
            {
                line.AppendLine().Append(ex);
            }

            System.Diagnostics.Debug.WriteLine(line.ToString());
            if (Folder == null)
            {
                return;
            }

            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                var file = Path.Combine(Folder, $"app-{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(file, line.AppendLine().ToString());
            }
        }
        catch
        {
            // Günlük yazılamıyorsa uygulamayı düşürme.
        }
    }

    /// <summary>30 günden eski günlükleri temizler (yalnızca günlük dosyaları; veriler değil).</summary>
    public static void Prune(int keepDays = 30)
    {
        try
        {
            if (Folder == null || !Directory.Exists(Folder))
            {
                return;
            }
            foreach (var f in new DirectoryInfo(Folder).GetFiles("app-*.log"))
            {
                if (f.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-keepDays))
                {
                    f.Delete();
                }
            }
        }
        catch
        {
            // yoksay
        }
    }
}
