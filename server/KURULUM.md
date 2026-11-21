# Ekip sunucusu kurulumu (Google Sheets + Apps Script, ücretsiz)

Bir kez yapılır (~10 dakika). Sonrasında herkesin uygulaması kendi toplamlarını bu tabloya gönderir, ekip sıralamasını
buradan alır; siz de yönetici panelinden herkesi görürsünüz.

## 1. Tablo ve kod

1. <https://sheets.new> ile yeni bir Google E-Tablosu açın, adını **Fortnite Companion Ekip** yapın.
2. **Uzantılar → Apps Script**.
3. Açılan editörde `Code.gs` dosyasının içeriğini silin, bu klasördeki **`Code.gs`** içeriğini yapıştırın.
4. Sol taraftaki **＋ → HTML** ile yeni dosya ekleyin, adını **`Admin`** yapın (uzantısız) ve **`Admin.html`** içeriğini yapıştırın.
5. **Kaydet** (💾).

## 2. Anahtarları üretin

1. Üstteki fonksiyon listesinden **`setup`** seçin → **Çalıştır**.
2. İzin isteyecek: hesabınızı seçin → *Gelişmiş* → *(proje adı)'na git (güvenli değil)* → **İzin ver**.
   (Kendi yazdığınız betik olduğu için Google bu uyarıyı gösterir.)
3. Alttaki **Yürütme günlüğü**'nde iki satır çıkar:
   - `EKİP ANAHTARI (uygulamaya): fc-…` → uygulamaya gömülecek
   - `YÖNETİCİ ANAHTARI (yalnızca sizde kalsın): adm-…` → yönetici paneli için, **kimseyle paylaşmayın**

   Anahtarlar sonradan *Proje ayarları → Komut dosyası özellikleri*'nde de görünür.

## 3. Web uygulaması olarak yayınlayın

1. Sağ üst **Dağıt → Yeni dağıtım** → tür: **Web uygulaması**.
2. **Şu kullanıcı olarak yürüt:** *Ben*  ·  **Erişimi olanlar:** *Herkes*
3. **Dağıt** → çıkan **Web uygulaması URL'sini** kopyalayın (`https://script.google.com/macros/s/…/exec`).

> Kodu sonradan değiştirirseniz: *Dağıt → Dağıtımları yönet → ✏️ → Sürüm: Yeni sürüm → Dağıt* (URL aynı kalır).

## 4. Uygulamaya bağlayın (GitHub)

GitHub'daki depoda **Settings → Secrets and variables → Actions**:

- **Variables** sekmesi → *New repository variable* → Ad: `SYNC_URL`, Değer: web uygulaması URL'si
- **Secrets** sekmesi → *New repository secret* → Ad: `SYNC_KEY`, Değer: EKİP anahtarı (`fc-…`)

Sonra yeni bir sürüm yayınlayın (ör. `v0.5.0`). GitHub'ın derlediği EXE bu adresi ve anahtarı içerir; ekipteki herkes
güncellemeyi kurunca otomatik bağlanır. Kimsenin bir şey ayarlamasına gerek yok.

(Elle denemek için: uygulamada Ayarlar → Ekip sunucusu alanlarına URL ve anahtarı yazabilirsiniz.)

## 5. Yönetici paneli

Tarayıcıda (telefonda da olur):

```
https://script.google.com/macros/s/…/exec?admin=YÖNETİCİ_ANAHTARI
```

- Herkesin toplam sandığı, maçı, kill'i, bugün/hafta değerleri, son görülme zamanı ve uygulama sürümü
- Satıra tıklayınca o oyuncunun son 30 maçı
- Birini sıralamadan çıkarmak: tabloda **Oyuncular** sayfası → **Gizli** sütununa `TRUE`

Bu bağlantıyı yer imlerine ekleyin; **anahtarı kimseyle paylaşmayın**.

## Gönderilen veriler

Uygulama yalnızca şunları gönderir: görünen ad, Epic adı, toplam/ortalama sandık-maç-kill, bugün/hafta/sezon
sandığı, en iyi maç, başlangıç değerleri, uygulama sürümü ve kendi maçlarının listesi (oturum kimliği, tarih,
sandık, kill, mod, sıra). Rakip veya rastgele takım arkadaşı adları, bilgisayar bilgisi, IP kaydı veya Epic hesap
bilgisi gönderilmez. Her oyuncu Ayarlar → "Ekip sıralamasına katıl" seçeneğini kapatabilir.

## Sınırlar

Google'ın ücretsiz kotaları onlarca, hatta birkaç yüz oyunculuk bir ekip için yeterlidir (her uygulama 10 dakikada bir
küçük bir istek gönderir). Tablo büyürse "Maclar" sayfasındaki eski satırları silebilirsiniz; toplamlar etkilenmez.
