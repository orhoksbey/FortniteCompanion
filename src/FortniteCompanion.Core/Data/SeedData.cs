namespace FortniteCompanion.Core.Data;

/// <summary>
/// Uygulamadan önce elle tutulan gerçek istatistikler. Bunlar replay'den kurtarılmış veri DEĞİLDİR;
/// arayüzde "Geçmişten eklenen kayıtlar / Önceki istatistikten aktarıldı" olarak gösterilir.
/// </summary>
public static class SeedData
{
    public sealed record SeedPlayer(string Name, int Matches, int Chests, int Kills);

    /// <summary>
    /// Başlangıç değerlerinin sabit "düzenlenme" tarihi. Böylece kullanıcının elle yaptığı her düzeltme,
    /// başka bir bilgisayardaki dokunulmamış başlangıç değerinden her zaman daha yeni sayılır (JSON içe aktarma).
    /// </summary>
    public static readonly DateTime SeededAtUtc = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    public const string Note = "Geçmişten eklenen kayıtlar (uygulama öncesi elle tutulan istatistik)";

    public static readonly IReadOnlyList<SeedPlayer> Players = new[]
    {
        new SeedPlayer("Ryder", 47, 495, 222),
        new SeedPlayer("Orhan", 46, 529, 176),
        new SeedPlayer("Ohxo", 68, 644, 217),
        new SeedPlayer("Doğukan", 68, 997, 163),
    };
}
