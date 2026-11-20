# Kurulum ve kullanım

## 1. Derleme (bir kez)

1. **.NET 10 SDK** kurun: <https://dotnet.microsoft.com/download/dotnet/10.0> → ".NET SDK" → Windows x64.
2. Proje klasöründe **`build.cmd`** dosyasına çift tıklayın.
   - Paketleri indirir, testleri çalıştırır, `dist\FortniteCompanion.exe` dosyasını üretir.
   - Testleri atlamak için: PowerShell'de `.\build.ps1 -SkipTests`
   - Daha küçük EXE için (karşı bilgisayarda .NET 10 Desktop Runtime gerekir): `.\build.ps1 -FrameworkDependent`
3. Visual Studio 2022 (17.14+) veya Rider ile `FortniteCompanion.sln` açılarak da derlenebilir.

> **GitHub ile derleme:** Kodu bir GitHub deposuna yüklerseniz `.github/workflows/build.yml` her push'ta testleri
> çalıştırır ve EXE'yi "Artifacts" olarak verir. `v0.1.0` gibi bir etiket gönderirseniz EXE sürüme eklenir.

İlk derlemede derleme hatası çıkarsa hata metnini (ilk 20–30 satır) bana gönderin; düzeltmesi genellikle kısa sürer.

## 2. Fortnite ayarları

| Ayar | Değer | Neden |
|---|---|---|
| Ayarlar → Oyun → Replays → **Record Replays** | **Açık** | Uygulamanın okuyacağı tek veri kaynağı |
| Ayarlar → Video → **Pencere Modu** | **Pencereli Tam Ekran** | Özel tam ekranda Windows hiçbir pencereyi oyunun üstünde gösteremez |

## 3. İlk açılış

1. `FortniteCompanion.exe`'yi çalıştırın. Windows "Bilgisayarınız korundu" derse: **Ek bilgi → Yine de çalıştır**
   (EXE imzalı değildir).
2. Kontrol paneli açılır, sistem tepsisinde sandık simgesi belirir. Pencereyi kapatmak uygulamayı kapatmaz; tepsiye küçültür.
   Tamamen kapatmak için tepsi simgesi → **Çıkış**.
3. **Arkadaşlar** sekmesi:
   - Her oyuncunun **Epic adlarını** girin (oyunda görünen ad, birebir). Replay'lerdeki sayılar bu adlarla eşleşir.
   - Bu bilgisayar kimin ise **"Bu bilgisayar bu oyuncunun"** düğmesine basın (overlay bu oyuncuyu gösterir).
     İlk replay işlendiğinde, bu bilgisayarın oyuncusunun Epic adı boşsa replay sahibinin adı otomatik atanır.
4. **Ayarlar** sekmesi: overlay köşesi, boyut, opaklık, tema, kısayollar, otomatik başlatma, yedekleme.
   Değişiklikler **Kaydet ve uygula** ile kaydedilir.

## 4. Varsayılan kısayollar

| Kısayol | İşlev |
|---|---|
| Ctrl+Alt+O | Overlay göster/gizle |
| Ctrl+Alt+L | Overlay kilidini aç/kapat (kilit açıkken sürükleyerek taşı, Ctrl+tekerlek ile boyutlandır) |
| Ctrl+Alt+Numpad + | Manuel canlı sayaç +1 (yalnızca manuel mod açıkken) |
| Ctrl+Alt+Numpad − | Manuel canlı sayaç −1 |

Kısayollar Ayarlar'dan değiştirilebilir. Kayıtlı bir kombinasyon oyuna iletilmez; oyunda kullanmadığınız bir kombinasyon seçin.

## 5. Maç akışı

1. Fortnite açılınca overlay görünür (ayar: "Yalnızca Fortnite açıkken göster").
2. Maç sırasında overlay'de **● MAÇTA** görünür. "BU MAÇ" satırı:
   - Otomatik modda "maç sonunda" yazar (replay maç bitmeden okunamaz).
   - Manuel canlı modda kısayolla saydığınız sayıyı gösterir.
3. Maç bitince birkaç saniye içinde replay okunur: toplam animasyonla artar, "Bu maç 24 sandık açtın" bildirimi gelir.
   Kişisel rekor kırılırsa ayrıca bildirim gelir.
4. Fortnite kapandıktan sonra hiç yeni replay oluşmadıysa, replay kaydının kapalı olabileceğine dair uyarı gelir.
   Manuel canlı modda saydığınız sayı bu durumda **"Manuel girildi"** olarak kaydedilir; aynı maçın replay'i
   sonradan gelirse manuel kayıt otomatik olarak toplamdan çıkarılır.

## 6. Arkadaşlarla paylaşım

- Her arkadaş uygulamayı kendi bilgisayarında çalıştırır (kendi sandıkları tam sayılır).
- **Arkadaşlar → JSON dışa aktar** ile dosya oluşturun, arkadaşlarınıza gönderin; onlar **JSON içe aktar** ile ekler.
- İçe aktarma öncesi otomatik yedek alınır. Aynı dosyayı tekrar içe aktarmak veri çoğaltmaz.
- Bulut senkronizasyonu bu sürümde yoktur (bilinçli olarak: açık rıza ve sunucu gerektirir).

## 7. Veri konumları

| Ne | Nerede |
|---|---|
| Veri tabanı | `%APPDATA%\FortniteCompanion\companion.db` |
| Ayarlar | `%APPDATA%\FortniteCompanion\settings.json` |
| Günlük yedekler | `%APPDATA%\FortniteCompanion\backups\` (varsayılan 14 adet) |
| Hata günlüğü | `%APPDATA%\FortniteCompanion\logs\` |

Bir yedeği geri yüklemek için: uygulamadan çıkın, `companion.db` dosyasını (ve varsa `companion.db-wal`, `companion.db-shm`)
başka yere taşıyın, yedek dosyasını `companion.db` adıyla kopyalayın, uygulamayı açın.

## 8. Replay okuyucusunu doğrulama (önerilir)

`dist\tools\ReplayScan.exe` komut satırı aracı Demos klasöründeki replay'leri okur ve her oyuncu için sandık sayısını
yazdırır (hiçbir şey kaydetmez). İlk birkaç maçtan sonra çalıştırıp oyundaki sayımınızla karşılaştırın:

```
dist\tools\ReplayScan.exe
dist\tools\ReplayScan.exe "C:\...\Demos\UnsavedReplay-2026.11.21-20.15.00.replay" --json
```

## 9. Sorun giderme

| Belirti | Çözüm |
|---|---|
| Overlay oyunun üstünde görünmüyor | Pencereli Tam Ekran'a geçin. Overlay görünür mü: Ctrl+Alt+O |
| "Replay klasörü bulunamadı" | Replay kaydını açıp bir maç oynayın veya Ayarlar'dan klasörü seçin |
| Replay "okunamadı" | Fortnite güncellemesi biçimi değiştirmiş olabilir. Sayı uydurulmaz; hata günlüğünü paylaşın |
| Sayılar bir arkadaşa yazılmıyor | Arkadaşlar sekmesinde Epic adını kontrol edin; "Eşleşmemiş oyuncu adları" listesine bakın |
| Kısayol çalışmıyor | Başka bir program kullanıyor olabilir; Ayarlar'dan değiştirin |
