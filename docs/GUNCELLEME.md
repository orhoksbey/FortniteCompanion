# Otomatik güncelleme: arkadaşlarına güncelleme yayınlama

Arkadaşların bir kez kurar, sonra hiçbir şey yapmaz. Sen GitHub'da yeni sürüm yayınlarsın; onların uygulaması
açılışta ve 6 saatte bir kontrol eder, yeni sürümü indirir, doğrular ve **Fortnite kapalıyken** kurup kendini
yeniden başlatır. Maç sırasında asla güncellenmez.

## Bir kerelik kurulum (yaklaşık 10 dakika)

1. <https://github.com> adresinde ücretsiz hesap aç.
2. **GitHub Desktop**'ı kur: <https://desktop.github.com>
3. GitHub Desktop → *File → Add local repository* → `FortniteCompanion` klasörünü seç → "create a repository" de →
   *Publish repository*. **"Keep this code private" işaretini KALDIR** (otomatik güncelleme herkese açık depodan
   indirir; depoda şifre veya kişisel veri yok, yalnızca kod var).
4. GitHub'da depo sayfası → **Actions** sekmesi → iş akışlarını etkinleştir (sorarsa).
5. İlk sürümü yayınla (aşağıdaki "Yeni sürüm yayınlama" adımları, ör. `v0.3.0`).
6. Arkadaşlarına **Releases** sayfasındaki `FortniteCompanion.exe`'yi gönder. **Bu sürümden itibaren** güncellemeler otomatik.

> Kendi bilgisayarında derlediğin EXE'de güncelleme kaynağı yoktur. Arkadaşlarına GitHub'ın derlediği EXE'yi ver
> ya da yerelde `.\build.ps1 -UpdateRepo kullanici/FortniteCompanion` ile derle.

## Yeni sürüm yayınlama (her güncellemede)

1. Kod değişikliklerini klasöre koy (ör. benim gönderdiğim yeni zip'in içeriğini üzerine kopyala).
2. GitHub Desktop → değişiklikleri **Commit** → **Push origin**.
3. GitHub'da depo → **Releases → Draft a new release** → *Choose a tag*: bir öncekinden büyük bir sürüm yaz
   (ör. `v0.3.1`) → *Create new tag* → **Publish release**.
4. Birkaç dakika içinde GitHub Actions EXE'yi derler, testleri çalıştırır ve sürüme `FortniteCompanion.exe` olarak ekler.
   (Actions sekmesinde yeşil tik görünür. Kırmızıysa testler veya derleme başarısız olmuştur; sürüm boş kalır ve
   kimseye dağıtılmaz.)
5. Arkadaşların: uygulama açılışta veya 6 saat içinde indirir, oyun kapalıyken kurar ve "güncellendi" bildirimi gösterir.
   Hemen istemeleri hâlinde: Ayarlar → **Şimdi denetle**.

## Güvenlik notları

- Uygulama yalnızca **senin deponun** son sürümünü indirir; dosya boyutu ve (GitHub sağlıyorsa) SHA-256 özeti doğrulanır.
- Kurulum başarısız olursa eski sürüm geri konur.
- EXE kullanıcının yazabildiği bir klasördeyse (Masaüstü, İndirilenler, Belgeler) çalışır. "Program Files" içindeyse
  yalnızca bildirim gösterilir, elle indirilmesi gerekir.
- İsteyen Ayarlar → "Otomatik güncelle" kutusunu kapatabilir.
- EXE imzalı olmadığı için bazı antivirüsler kendini güncelleyen programları şüpheli bulabilir; böyle bir durumda
  dosyayı istisnalara eklemek gerekebilir.
