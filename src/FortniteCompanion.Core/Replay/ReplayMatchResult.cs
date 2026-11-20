namespace FortniteCompanion.Core.Replay;

/// <summary>Bir sandık/konteyner etkileşimi (replay içindeki zaman damgasıyla).</summary>
public sealed class ChestEvent
{
    /// <summary>Kaydın başlangıcından itibaren saniye.</summary>
    public float TimeSeconds { get; set; }

    /// <summary>Türkçe tür adı (bkz. <see cref="ChestKinds"/>).</summary>
    public string Kind { get; set; } = "";

    /// <summary>Oyundaki blueprint adı, örn. "Tiered_Chest_Athena".</summary>
    public string Blueprint { get; set; } = "";

    /// <summary>Açılmak yerine kırıldı (ganimeti yine düştü).</summary>
    public bool Destroyed { get; set; }
}

/// <summary>Bir replay içinde kayıt yapan oyuncu veya onun takım arkadaşı için çıkarılan sonuç.</summary>
public sealed class ReplayPlayerResult
{
    /// <summary>Oyundaki görünen ad (Epic adı). Bot ise "Bot" olabilir.</summary>
    public string Name { get; set; } = "";

    public string? EpicId { get; set; }

    /// <summary>Bu replay'i kaydeden oyuncu mu? (Sayımı eksiksiz olan tek oyuncu.)</summary>
    public bool IsRecorder { get; set; }

    public bool IsBot { get; set; }

    /// <summary>Oyuncu durumundan okunan eleme sayısı; okunamazsa null.</summary>
    public int? Kills { get; set; }

    public int? Placement { get; set; }

    /// <summary>Açılan + kırılan sandıklar (mühimmat kutusu vb. hariç).</summary>
    public int ChestsTotal { get; set; }

    /// <summary>Tür → adet (yalnızca sandıklar).</summary>
    public Dictionary<string, int> ChestsByKind { get; set; } = new();

    public int AmmoBoxes { get; set; }

    /// <summary>Soğutucu, varil, yemek kutusu gibi diğer konteynerler.</summary>
    public int OtherContainers { get; set; }

    public int DestroyedChests { get; set; }

    public List<ChestEvent> ChestEvents { get; set; } = new();
}

/// <summary>Bir .replay dosyasının çözümlenmesi sonucu.</summary>
public sealed class ReplayMatchResult
{
    /// <summary>Sunucu oturum kimliği: aynı maçı farklı bilgisayarlardan gelen replay'lerde de aynıdır.</summary>
    public string SessionId { get; set; } = "";

    public string FileName { get; set; } = "";
    public DateTime? MatchStartUtc { get; set; }
    public int? DurationSeconds { get; set; }
    public string? Playlist { get; set; }
    public string? Map { get; set; }
    public bool IsCustomMatch { get; set; }
    public string? GameVersion { get; set; }
    public int? TeamSize { get; set; }
    public int? TotalPlayers { get; set; }

    /// <summary>Kaydeden oyuncunun takımının sırası (1 = Victory Royale).</summary>
    public int? Placement { get; set; }

    public int AnalyzerVersion { get; set; }

    /// <summary>Kaydeden oyuncu tespit edilebildi mi? Edilemezse sandıklar kimseye yazılmaz.</summary>
    public bool RecorderFound { get; set; }

    public string? RecorderName { get; set; }

    /// <summary>Kaydeden oyuncu + aynı takımdaki oyuncular. Kaydeden her zaman ilk sıradadır.</summary>
    public List<ReplayPlayerResult> Players { get; set; } = new();

    /// <summary>Okuma sırasında atlanan bozuk veri blokları vb. için uyarılar.</summary>
    public List<string> Warnings { get; set; } = new();
}
