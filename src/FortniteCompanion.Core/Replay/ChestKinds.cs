namespace FortniteCompanion.Core.Replay;

/// <summary>
/// Konteyner blueprint adından tür çıkarımı. Sınıflandırma kuralları LED-N/Fortnite-ChestCounter (MIT)
/// projesindeki ChestKinds sınıfından uyarlanmıştır. Bilinmeyen ama adında "chest" geçen yeni sezon
/// sandıkları "Diğer sandık" olarak sayılır; mühimmat kutusu ve diğer konteynerler ayrı tutulur.
/// </summary>
public static class ChestKinds
{
    // Sandıklar (toplama dahil)
    public const string Normal = "Normal sandık";
    public const string Rare = "Nadir sandık";
    public const string Buried = "Gömülü sandık";
    public const string Special = "Özel etkinlik sandığı";
    public const string Other = "Diğer sandık";

    // Sandık sayılmayan konteynerler
    public const string AmmoBox = "Mühimmat kutusu";
    public const string FoodBox = "Yemek kutusu";
    public const string OtherContainer = "Diğer konteyner";

    public static readonly IReadOnlyList<string> ChestKindsOrdered = new[] { Normal, Rare, Buried, Special, Other };

    private static readonly HashSet<string> ChestSet = new(ChestKindsOrdered);

    public static bool IsChest(string kind) => ChestSet.Contains(kind);

    public static string Classify(string blueprint)
    {
        var b = (blueprint ?? "").ToLowerInvariant();
        if (b.Contains("lootcannon") || b.Contains("cannon")) return OtherContainer;
        if (b.Contains("ammo")) return AmmoBox;
        if (b.Contains("foodbox") || b.Contains("food_box")) return FoodBox;
        if (b.Contains("rarechest") || b.Contains("legendary") || b.Contains("rare_chest")) return Rare;
        if (b.Contains("buried")) return Buried;
        // Sezon/etkinliğe özel sandıklar (ruh/relic, korsan "booty", poster sandıkları vb.)
        if (b.Contains("spriteposter") || b.Contains("booty") || b.Contains("relic") || b.Contains("extraction")
            || b.Contains("event") || b.Contains("holiday") || b.Contains("winterfest")) return Special;
        if (b.Contains("tiered_chest") || b.Contains("normalchest") || b.Contains("standardchest")) return Normal;
        return b.Contains("chest") ? Other : OtherContainer;
    }
}
