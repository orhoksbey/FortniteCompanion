namespace FortniteCompanion.Core.Services;

public static class AppPaths
{
    /// <summary>%APPDATA%\FortniteCompanion (FC_DATA_DIR ortam değişkeniyle değiştirilebilir; testler için).</summary>
    public static string DataFolder { get; } =
        Environment.GetEnvironmentVariable("FC_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FortniteCompanion");

    public static string DatabaseFile => Path.Combine(DataFolder, "companion.db");
    public static string SettingsFile => Path.Combine(DataFolder, "settings.json");
    public static string LogFolder => Path.Combine(DataFolder, "logs");
    public static string DefaultBackupFolder => Path.Combine(DataFolder, "backups");

    /// <summary>Fortnite'ın replay klasörü: %LOCALAPPDATA%\FortniteGame\Saved\Demos</summary>
    public static string DefaultReplayFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FortniteGame", "Saved", "Demos");
}
