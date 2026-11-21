# Güvenlik ve Epic Games uyumluluğu

## Uygulamanın yaptıkları

| İşlem | Nasıl | Oyuna etkisi |
|---|---|---|
| Replay okuma | `%LOCALAPPDATA%\FortniteGame\Saved\Demos\*.replay` dosyaları `FileAccess.Read` ile, maç bittikten sonra açılır | Yok — Fortnite'ın kendi yazdığı dosya, salt okunur |
| Overlay | Kendi şeffaf, her zaman üstte, odak almayan WPF penceresi (`WS_EX_TRANSPARENT`, `WS_EX_NOACTIVATE`) | Oyunun içine çizilmez; DirectX/Vulkan hook yok |
| Fortnite açık mı | `Process.GetProcessesByName("FortniteClient-Win64-Shipping")` — yalnızca işlem listesi | Oyun işlemine tutamaç açılmaz, bellek okunmaz |
| Global kısayol | Win32 `RegisterHotKey` — Windows yalnızca kayıtlı kombinasyon basılınca haber verir | Tuş okuma/kaydetme yok, oyuna tuş gönderme yok |
| Bildirim | Sistem tepsisi balonu | Yok |
| Ağ | (1) Güncelleme: GitHub'daki kendi deponuzun sürüm bilgisi okunur, EXE indirilir (boyut/SHA-256 doğrulanır). (2) Ekip sunucusu (ayarlıysa): kendi Google Apps Script adresinize oyuncunun kendi özeti gönderilir (ad, Epic adı, sandık/maç/kill, kendi maç listesi). Rakip adları, bilgisayar veya Epic hesap bilgisi gönderilmez. İkisi de Ayarlar'dan kapatılabilir | Yok |

## Kesinlikle yapılmayanlar

- Fortnite işlemine DLL enjeksiyonu
- Oyun belleğini okuma/yazma, Cheat Engine benzeri tarama
- Easy Anti-Cheat / BattlEye'ı atlatma, devre dışı bırakma, gizlenme
- Oyun dosyalarını değiştirme (yalnızca Demos klasöründeki replay'ler **okunur**)
- Ağ trafiğini yakalama veya oyun protokolünü çözme
- Maç içinde gizli konum, düşman veya ganimet bilgisi üretme
- Otomatik oynama, makro, `SendInput` veya benzeri giriş otomasyonu
- Epic şifresi, oturum çerezi veya erişim anahtarı isteme/kullanma

## Neden maç içi canlı sayım yok?

Fortnite, kaydı sürerken replay'i şifreler ve şifre anahtarını yalnızca maç bittiğinde dosyaya yazar. Bu Epic'in
bilinçli tasarımıdır: maç içi verinin replay'den çekilmesini engeller. Uygulama bunu aşmaya çalışmaz; anahtarı
olmayan (canlı) replay'leri okumaz. Maç içi sayı için **manuel canlı mod** vardır.

Sandık konumu veya ganimet bilgisi **maç sırasında** hiçbir zaman gösterilmez; replay'den yalnızca maç bittikten
sonra, oyuncunun kendi açtığı sandıkların **sayısı** çıkarılır. Bu, oyun içi rekabet avantajı sağlamaz.

## Deneysel ekran analizi

İstenen "yalnızca ekranda görünen bilgiyi işleyen görüntü analizi" bu sürümde **uygulanmadı**. Teorik olarak
ekran görüntüsü okumak oyuna müdahale etmez, ancak:
- "Sandık açıldı" anını görüntüden güvenilir biçimde ayırt etmek için ciddi bir test verisi ve isabet ölçümü gerekir;
- ölçülmemiş bir sayıyı "doğru" diye göstermek istemediğiniz şeydi.
Ayarlar'da bu seçenek kapalı ve devre dışı olarak, nedeni yazılı biçimde durur.

## Garanti yok

Epic Games, üçüncü taraf yazılımlar için ban riski olmadığını garanti etmez. Bu uygulama da etmez. Yukarıdaki
tasarım, bilinen tespit yöntemlerinin hedeflediği davranışların (enjeksiyon, bellek erişimi, giriş otomasyonu)
hiçbirini içermez; Discord veya Steam overlay'lerinden farklı olarak oyunun render hattına da girmez.

Uygulamanın kullandığı replay okuma yaklaşımı, açık kaynak FortniteReplayDecompressor kütüphanesine ve bu kütüphaneyi
kullanan topluluk araçlarına (ör. LED-N/Fortnite-ChestCounter, rossalv/fortnite-chest-counter) dayanır.
