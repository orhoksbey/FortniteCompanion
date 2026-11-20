# Mimari

## Katmanlar

```
FortniteCompanion (WPF, net10.0-windows)
 ├─ App            tek örnek, tepsi, kısayollar, Fortnite işlem izleme, bildirimler
 ├─ Overlay        şeffaf, tıklama geçiren pencere; köşe + oransal ofset ile konum
 ├─ MainWindow     Genel Bakış · Maç Geçmişi · Arkadaşlar · Grafikler · Ayarlar
 ├─ ViewModels     MainViewModel (MVVM, INotifyPropertyChanged)
 └─ Controls       BarChart / LineChart (OnRender ile çizim, kütüphane yok)
        │
FortniteCompanion.Core (net10.0)
 ├─ Replay         ReplayAnalyzer · ReplayHeaderProbe · ReplayProcessor · ChestKinds
 ├─ Data           Database (SQLite) · Models · SeedData
 └─ Services       CompanionService · StatsService · ReplayWatcher · ImportExport · Backup ·
                   AppSettings · FortniteProcessMonitor · UpdateChecker · AppLog
        │
lib/ FortniteReplayReader · Unreal.Core · Unreal.Encryption · OozSharp  (MIT, yamalı)
```

## Replay işleme

1. `ReplayWatcher` Demos klasörünü `FileSystemWatcher` + 5 sn yoklama ile izler.
2. Her dosya için `ReplayHeaderProbe` yalnızca başlığı okur: `bIsLive` ve şifre anahtarı var mı?
3. Maç bitmiş ve dosya birkaç saniyedir değişmemişse `ReplayProcessor` dosyayı belleğe okur ve
   en düşük öncelikli iş parçacığında, 3 dk zaman aşımıyla `ReplayAnalyzer`'a verir.
4. `ReplayAnalyzer` (LED-N'den uyarlandı):
   - Bilinmeyen sandık blueprint'lerini genel konteyner grubuna eşler (yeni sezonlar kod değişmeden okunur).
   - `bAlreadySearched` false→true geçişi = açılış; `SearchingPawn` = açan piyon. Yeniden doldurulan sandık tekrar sayılır.
   - Kanal açılırken gelen "bayat" durum, aynı açan için ikinci kez sayılmaz.
   - Kırılan sandık (`bDestroyed` + piyon) da sayılır; kıranı bilinmeyenler ganimetin saçıldığı noktaya en yakın oyuncuya (≤4,5 m) verilir.
   - Piyon → PlayerState → PlayerId → oyuncu adı/TeamIndex. Kaydeden + aynı takımdakiler raporlanır.
5. Sonuç `Database.UpsertReplayMatch` ile yazılır, dosya `processed_files` tablosuna işlenir.

## Veri modeli (SQLite)

| Tablo | Anahtar | İçerik |
|---|---|---|
| `players` | id | Görünen ad, Epic adları |
| `baselines` | player_id | Geçmiş maç/sandık/kill + başlangıç tarihi (cutoff) |
| `matches` | session_id | Sunucu oturum kimliği, tarih, mod, süre, sıra, kaynak, `excluded`, `overlap_override` |
| `match_players` | (session_id, observer, player_name) | Bir gözlemcinin (replay sahibi) bir oyuncu için gördüğü değerler |
| `processed_files` | ad\|boyut\|tarih | İşlenen dosyalar ve sonucu (ok, duplicate, error, incomplete, no_recorder) |
| `edit_log` | id | Silinemez değişiklik geçmişi |
| `live_sessions` | id | Manuel canlı sayaç oturumları ve doğrulama sonucu |

`matches`, `match_players`, `baselines`, `edit_log` tablolarında `DELETE` tetikleyicilerle engellenir.

## Tekilleştirme kuralları

- **Aynı replay iki kez:** aynı (oturum, gözlemci, oyuncu) satırı zaten varsa eklenmez.
- **Aynı maç farklı bilgisayarlardan:** oturum kimliği aynıdır. Her (maç, oyuncu) için tek değer seçilir:
  1. Oyuncunun kendi replay'i (`is_recorder`) → **Replay ile doğrulandı**
  2. Yoksa takım arkadaşlarının gördüğü en yüksek değer → **Tahmini (kısmi)**
  3. Yoksa manuel kayıt → **Manuel girildi**
- **Manuel ↔ replay:** Bir replay geldiğinde, aynı oyuncuya ait ve zaman aralığı örtüşen manuel maçlar
  `excluded` yapılır (silinmez); canlı oturum "verified" olarak kapanır.
- **Geçmiş ↔ replay:** `started_utc < cutoff` olan maçlar toplama eklenmez ve "Örtüşme" uyarısı taşır; kullanıcı tek tek
  dahil edebilir. Cutoff'tan sonraki ilk 12 saat "şüpheli" olarak işaretlenir ama sayılır.

## Performans

- Overlay yalnızca değer değişince yeniden çizilir; tek zamanlayıcı 2 sn'de bir `SetWindowPos(TOPMOST)` çağırır.
- Replay analizi maç sonunda bir kez, `ThreadPriority.Lowest` ile yapılır; analiz sonrası bellek serbest bırakılır.
- İş istasyonu GC, eşzamanlı GC kapalı (düşük boşta bellek).
- Grafikler kütüphanesiz `OnRender` ile çizilir; animasyon yalnızca sayı artışında (~1 sn).
