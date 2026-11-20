# Test durumu (dürüst özet)

Bu proje, .NET SDK'sı bulunmayan ve internete paket indirmesi kapalı bir Linux ortamında yazıldı.
Bu nedenle:

| Konu | Durum |
|---|---|
| Derleme | **Yapılmadı.** Kod ilk kez sizin bilgisayarınızda (`build.cmd`) veya GitHub Actions'ta derlenecek. Küçük derleme hataları çıkabilir. |
| Birim testleri (`tests/`) | **Yazıldı, çalıştırılmadı.** 22 test metodu (29 test durumu): geçmiş verilerin doğru eklenmesi, toplam formülü, aynı replay'in iki kez sayılmaması, takım arkadaşı (kısmi) ↔ kendi replay'i önceliği, örtüşme/şüpheli örtüşme, manuel kaydın replay ile değiştirilmesi, silinemez geçmiş, değişiklik günlüğü, tarih aralıkları, eşleşmemiş adlar, JSON içe/dışa aktarma tekilleştirmesi, CSV, yedek, ayarlar, replay başlığı çözümleme, sandık sınıflandırma. Windows gerektirmez; `dotnet test` ile Linux/macOS'ta da çalışır. |
| Replay okuyucu | **Gerçek replay ile denenmedi.** Okuyucu çekirdeği, Kasım 2026'da güncel Fortnite replay'leriyle kullanılan LED-N/Fortnite-ChestCounter (MIT) kodundan ve onun yamalı FortniteReplayDecompressor kütüphanesinden alındı; sandık sayma kuralları aynen korundu. Yine de ilk maçlardan sonra `ReplayScan.exe` ile oyundaki sayınızla karşılaştırın. |
| WPF arayüz / overlay | **Çalıştırılmadı.** Görünüm, konumlandırma ve DPI davranışı ilk açılışta kontrol edilmeli. |
| Takım arkadaşı kill sayıları | Oyuncu durumundaki (PlayerState) kill alanından okunur; doğruluğu gerçek replay'de doğrulanmadı. |

## Bu sürümde bilinçli olarak OLMAYANLAR

- **Bulut senkronizasyonu** — yalnızca JSON içe/dışa aktarma var (varsayılan istenen buydu).
- **Ekran görüntüsü analiziyle sayma** — isabet oranı ölçülmeden doğru sayı iddia edilmemesi için eklenmedi; Ayarlar'da kapalı ve devre dışı.
- **Fortnite açılınca uygulamanın kendiliğinden başlaması** — bunun yerine "Windows ile başlat" (tepside bekler) ve
  "Yalnızca Fortnite açıkken overlay göster" var. Ayrı bir arka plan izleme süreci eklenmedi.
- **Özel (exclusive) tam ekranda overlay** — Windows sınırı; oyuna müdahale etmeden mümkün değil.
- **Maç içi gerçek zamanlı otomatik sayım** — Fortnite replay'i maç bitene kadar şifreli tutar.

## İlk denemede kontrol listesi

1. `build.cmd` → derleme ve testler geçiyor mu? Geçmiyorsa ilk hata satırlarını paylaşın.
2. Uygulama açılınca Genel Bakış'ta toplam sandık Ryder/Orhan/Ohxo/Doğukan için geçmiş değerleri gösteriyor mu?
3. Arkadaşlar → Epic adlarını girin, "Bu bilgisayar bu oyuncunun" seçin.
4. Pencereli tam ekranda bir maç oynayın; maç bitince bildirim ve overlay artışı geliyor mu?
5. `dist\tools\ReplayScan.exe` çıktısını oyundaki sayınızla karşılaştırın.
