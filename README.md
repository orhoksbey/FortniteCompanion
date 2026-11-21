# Fortnite Companion

Fortnite için Windows 10/11 masaüstü uygulaması: sağ üstte şeffaf sandık sayacı overlay'i, maç geçmişi,
dört kişilik arkadaş karşılaştırması ve grafikler. Arayüz Türkçedir.

> **Önemli:** Bu sürüm Linux üzerinde, .NET SDK'sı olmayan bir ortamda yazıldı. **Kod henüz derlenmedi ve
> çalıştırılmadı.** İlk derleme ve deneme sizin bilgisayarınızda yapılacak. Ayrıntı: [docs/TEST-DURUMU.md](docs/TEST-DURUMU.md).

## Hızlı başlangıç

1. [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (Windows x64) kurun.
2. Bu klasörde `build.cmd` dosyasına çift tıklayın (veya PowerShell'de `.\build.ps1`).
3. Oluşan `dist\FortniteCompanion.exe` dosyasını çalıştırın. Tek dosyadır, kurulum gerektirmez.
4. Fortnite'ta **Ayarlar → Oyun → Replays → Record Replays: Açık** ve **Pencere modu: Pencereli Tam Ekran** yapın.

Ayrıntılı kurulum: [docs/KURULUM.md](docs/KURULUM.md) · Ekibe otomatik güncelleme yayınlama: [docs/GUNCELLEME.md](docs/GUNCELLEME.md)

## Nasıl sayar?

- Fortnite her maç için `%LOCALAPPDATA%\FortniteGame\Saved\Demos` klasörüne bir `.replay` dosyası yazar.
- Fortnite replay'i maç süresince şifreli tutar ve anahtarı **maç bitince** yazar. Uygulama bu noktadan sonra
  dosyayı **salt okunur** açar, hangi sandığı hangi oyuncunun açtığını (`SearchingPawn`) bulur ve kaydeder.
- Bu yüzden sayı **maç bitince** güncellenir; maç içinde gerçek zamanlı değildir. İsterseniz **manuel canlı mod**
  ile maç içinde kısayolla sayabilirsiniz; maç bitince replay'deki doğrulanmış sayı esas alınır, manuel sayı
  toplama eklenmez.
- **Takım arkadaşları:** Aynı replay'de takım arkadaşlarının açtığı sandıklar da görünür ve ayrı kaydedilir.
  Replay yalnızca kaydedenin yakınını (~150 m) içerdiği için bu sayılar **"Tahmini (kısmi)"** olarak işaretlenir.
  Arkadaşınız kendi bilgisayarında uygulamayı çalıştırıp JSON paylaşırsa, onun kendi (tam) sayısı esas alınır;
  aynı maç iki kez sayılmaz.
- Mühimmat kutuları ve diğer konteynerler (soğutucu, varil, yemek kutusu) sandık toplamına dahil edilmez, ayrı gösterilir.

## Kurulum ve geçmiş veriler

Uygulama herkese açıktır; sabit bir oyuncu listesi yoktur. İlk açılışta **"Sen kimsin?"** ekranı çıkar:
görünen ad, Epic adı ve oyundaki görev sayacından (Görevler → Hazine Avı: hazine + nadir + ruh sandıkları)
şu anki sandık sayısı girilir. Eski maçlar geri getirilemez ve uygulama bunları kurtarılmış gibi göstermez; girilen
değer **"Önceki istatistikten aktarıldı"** olarak saklanır.

**Toplam = başlangıç değeri + başlangıç tarihinden sonraki yeni ve benzersiz maçlar.**

**Yönetici paneli:** Google Sheets + Apps Script ile ücretsiz bir sunucu kurulursa herkesin uygulaması kendi toplamlarını
oraya gönderir; yönetici herkesi bir web panelinden görür ve gerekirse bir oyuncuya sandık ekleyip çıkarabilir (oyuncunun
uygulamasına "Yönetici düzeltmesi" olarak geri gelir). Uygulamada ayrı bir ekip sekmesi yoktur.
Kurulum: [server/KURULUM.md](server/KURULUM.md). Oyuncular Ayarlar'dan gönderimi kapatabilir.

**Sayacı düzeltme:** Oyun çöktüğünde o maçın replay'i yarım kalır ve okunamaz. Genel Bakış → **Oyundaki sayıya eşitle**
ile oyundaki sandık görevinde yazan sayıyı girersin; fark geçmiş değere eklenir, maç sayısı değişmez.

## Güvenlik

Uygulama oyun belleğini okumaz, DLL enjekte etmez, oyun dosyalarını değiştirmez, ağ trafiğini dinlemez,
anti-cheat'e dokunmaz, tuş/fare enjeksiyonu yapmaz. Yalnızca Fortnite'ın kendi yazdığı replay dosyalarını okur
ve kendi şeffaf penceresini çizer. **Epic Games üçüncü taraf yazılımlar için ban riski olmadığını garanti etmez;
bu uygulama da etmez.** Ayrıntı: [docs/GUVENLIK.md](docs/GUVENLIK.md)

## Klasör yapısı

```
src/FortniteCompanion.Core   replay analizi, SQLite, istatistik, içe/dışa aktarma (Windows'a bağımlı değil)
src/FortniteCompanion        WPF uygulaması: overlay, kontrol paneli, tepsi, kısayollar
tests/                       xUnit testleri (istatistik, tekilleştirme, örtüşme, içe aktarma, yedek)
tools/ReplayScan             gerçek replay'lerle okuyucuyu doğrulamak için komut satırı aracı
lib/                         FortniteReplayDecompressor (MIT), LED-N yamalarıyla
docs/                        kurulum, güvenlik, mimari, test durumu, güncelleme
server/                      ekip sunucusu (Google Apps Script) ve yönetici paneli
```

Lisanslar ve kaynaklar: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)

Fortnite Companion, Epic Games ile bağlantılı değildir ve Epic Games tarafından onaylanmamıştır.
Fortnite, Epic Games, Inc.'in ticari markasıdır.
