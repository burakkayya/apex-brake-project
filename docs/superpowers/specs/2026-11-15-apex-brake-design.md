# Apex Brake — Tasarım Dokümanı (GDD + Teknik Yol Haritası)

Tarih: 2026-11-15 · Durum: **onaylandı**

İşaretler: **[K]** = kullanıcının verdiği karar. **[Ö]** = Claude'un önerisi/kararı; itiraz edilebilir. Sayısal değerlerin hepsi ilk değerdir, playtest ile ayarlanır.

---

## 1. Amaç

Tek parmakla oynanan, dikey ekranlı, low-poly stilize 3D arcade yarış oyunu (Android + iOS). Oyuncunun tek işi doğru anda, doğru miktarda fren yapmaktır. Oyun "yarış → para kazan → aracı geliştir → tekrar dene" döngüsüyle ilerler ve 10 dünya boyunca sürer.

### Hedefler
- **[K]** One-tap: ekrana basılı tut = fren. Başka kontrol yok.
- **[K]** Dikey ekran, low-poly/stilize görsel, gerçekçilik yok.
- **[K]** Türkçe + İngilizce, yerelleştirme altyapısı ilk günden.
- **[K]** Her dünyanın pisti, araçları, arayüz vurgusu ve sesi tek bir tema olarak uyumlu.
- **[K]** Çevrimdışı oynanabilen kariyer; çevrimiçi multiplayer ve sonsuz mod.
- **[K]** Sunucu maliyeti sıfır (ücretsiz katmanlar).
- **[K]** Önce Çöl dünyasında 10 bölümle çekirdek döngü kanıtlanır; mağaza çıkışı 10 dünya × 10 bölüm.

### Hedef dışı (v1)
- Direksiyon, şerit değiştirme, vites gibi ikinci bir kontrol.
- "Reklamları kaldır" veya başka gerçek para satın alımı **[K]**.
- Sunucu taraflı hile koruması (bkz. §8 riskler).
- Sıralı lig, arkadaş listesi, sohbet.
- Addressables / indirilebilir içerik **[Ö]** (low-poly 10 dünya tek pakete sığar; sığmazsa sonra eklenir).

### Başarısızlık sayılacak durumlar
- Bir dünyanın pisti, aracı veya arayüzü birbirine yabancı durursa.
- Oyuncu neden elendiğini anlamıyorsa (haksız eleme hissi).
- Bölümler ya ilk denemede geçiliyorsa ya da yükseltme almadan hiç geçilemiyorsa.

---

## 2. Değerlendirilen yaklaşımlar

### Çarpışma modeli
| Yol | Özet | Sonuç |
|---|---|---|
| **A — Şerit içi temas** | Araçlar çizgide kalır; pist daralır, şeritler birleşir, temas kurallı hesaplanır. Her cihaz yalnızca kendi aracının tepkisini hesaplar. | **[K] Seçildi.** Bot, hayalet ve canlı yarışta aynı kod çalışır. |
| B — Tam fizik | Rigidbody araçlar, gerçek çarpışma. | Elendi: tek tuş hissi bozulur, canlı yarışta ışınlanma, hayaletle adil çarpışma imkânsız. |
| C — Sadece kariyerde çarpışma | Multiplayer'da araçlar iç içe geçer. | Elendi: iki mod farklı oyun gibi hisseder. |

### Multiplayer altyapısı
- **Unity Gaming Services (Relay + Lobby + Authentication + Cloud Save)** — **[Ö] seçildi.** Hesap ve bulut kayıt zaten UGS'te; tek SDK ailesi.
- Photon Fusion — elendi: ikinci bir servis ve hesap sistemi getirir, ücretsiz eşzamanlı oyuncu sınırı daha düşük.

### Bölüm üretimi
- **Modüler parçalar + bölüm tarifi** — **[K] seçildi.** Sonsuz modu da aynı sistem besler.
- Elle çizilmiş 100 pist — elendi: üretim süresi.

---

## 3. Oynanış

### 3.1 Yarış formatı [K]
- 5 araç: oyuncu + 4 rakip. Aynı hizadan başlar.
- Bölüm süresi 30–45 sn (pist uzunluğu yaklaşık 900–1300 m).
- Arkadan takip kamerası, dikey ekran.

### 3.2 Araç hareketi [Ö]
Araç fizik motoruyla değil, pist çizgisi üzerinde hesapla ilerler. Durumu üç sayıdır: pist üzerindeki mesafe, hız, yanal konum (şerit).
- Ekrana dokunulmuyorsa: `ivme` ile `maksimum hız`a kadar hızlanır.
- Basılı tutuluyorsa: sabit fren ivmesiyle yavaşlar (ilk değer: 25 m/s²).
- İlk değerler: maksimum hız 30 m/s, ivme 8 m/s². Yükseltmeler bu ikisini artırır.
- Simülasyon sabit adımlıdır (50 Hz) ve saf C#'tır (MonoBehaviour yok); aynı girdi her cihazda aynı sonucu verir. Bu, hayalet kaydı ve otomatik test için şarttır.

### 3.3 Engeller ve iki kademeli hata [K]
Her viraj/rampa/tümsek parçasının bir **güvenli hız**ı vardır (viraj yarıçapından türetilir, parça verisinde ezilebilir).

| Giriş hızı | Sonuç |
|---|---|
| ≤ güvenli hız | Temiz geçiş |
| güvenli hızın %100–115'i | **Savrulma:** araç dışa kayar, hızının %40'ını kaybeder, komşu şeride taşabilir |
| > %115 | **Çakılma:** araç pistten uçar (sonucu moda göre, aşağıda) |

- **Rampa:** çok hızlı → kontrolsüz uçuş, çakılma. Çok yavaş → boşluğu geçemez, araç düşer. İkisi de çakılma sayılır.
- **Tümsek:** aşırı hızda araç havalanır; inişte savrulma veya çakılma kuralı uygulanır.
- **Çakılmanın sonucu moda göre değişir [K]:**
  - **Kariyer (botlarla):** eleme. Botlar da aynı kuralla elenir.
  - **Multiplayer:** eleme yok. Araç 1,5 sn sonra, engeli yeniden deneyebileceği kadar geride yeniden doğar. Rampada bu mesafe, tam gazla rampanın en düşük atlama hızına ulaşmaya yetecek kadardır (aracın ivmesinden hesaplanır, parça verisinde ezilebilir).
  - **Sonsuz mod:** tur biter.
- Eşik yüzdeleri dünya temasından ayarlanabilir (örn. Kar'da savrulma bandı genişler).

### 3.4 Perfect Brake Boost [K]
Fren, engelin girişine yakın ve hız güvenli hızın %95–100 bandındayken bırakılırsa:
- **Perfect:** 1,5 sn boyunca maksimum hızın %20 üstüne çıkış + görsel/ses/titreşim ödülü.
- **Good** (%85–95 bandı): yarı güçte boost.
- Ödül parası da küçük bir bonus alır. Ayrı NOS tuşu yoktur.

### 3.5 Şerit içi temas [K]
- Pist 5 şeritlidir; her aracın bir ev şeridi vardır.
- **Daralma parçaları** şeritleri birleştirir (5 → 3 → 2). Birleşmeye önde giren şeridi alır; arkadaki sıraya girer. Pist genişleyince herkes ev şeridine döner.
- **Arkadan temas:** arkadaki araç öndekinin hızına düşürülür ve kısa bir sarsıntı yer; öndeki küçük bir itme alır.
- **Yan temas:** savrulan araç komşu şeride taşar; orada araç varsa ikisi de hız kaybeder.
- **Kural:** her cihaz yalnızca kendi aracının temas tepkisini hesaplar. Botlar aynı cihazda olduğundan iki taraf da tepki verir; hayalet ve uzak oyuncu için yalnızca yerel oyuncu tepki verir.

### 3.6 Mini harita [K]
Ekranın sol kenarında ince dikey ilerleme çubuğu: 5 araç ikonu, bitiş çizgisi üstte. Oyuncu ikonu vurgulu.

### 3.7 Botlar [K]
Botlar oyuncuyla aynı simülasyonu kullanır; frene ne zaman basacaklarına ileriye bakarak karar verirler. Her botun profili:
- ivme ve maksimum hız (bölüm zorluğuna göre ölçeklenir),
- fren zamanlama hatası (küçük = usta, büyük = sık savrulur/çakılır),
- risk iştahı (güvenli hıza ne kadar yakın girer).

Her bölümde 4 bot farklı arketiplerden seçilir: usta, dengeli, atak ama hatalı, yavaş. Bazıları elenir, biri zorlayıcıdır.

---

## 4. Dünyalar ve görsel uyum

### 4.1 Dünya teması = tek veri dosyası
Her dünya bir `WorldTheme` varlığıdır ve şunların hepsini birlikte taşır: renk paleti (5 renk), gökyüzü ve sis, pist malzemeleri, çevre süsleri, oyuncu aracı, 4 rakip araç, arayüz vurgu rengi ve arka planı, müzik, ses seti, parçacık efekti (toz, kar, kıvılcım), fizik ayarları (§4.3).

Bir sahnede hiçbir görsel öğe doğrudan atanmaz; hepsi aktif temadan okunur. Böylece bir dünyaya yabancı bir varlığın sızması yapısal olarak engellenir.

### 4.2 Sanat kuralları [Ö]
- Düz renkli low-poly; doku yerine ortak palet atlası.
- Tüm dünyalarda aynı ışık düzeni, aynı gölge ve aynı post-process tabanı; yalnızca renk ve sis değişir.
- Arayüz yerleşimi, yazı tipi ve ikon seti tüm dünyalarda aynıdır; yalnızca vurgu rengi ve ana ekran arka planı temaya göre değişir.
- Modeller tek stil ailesinden alınır (Kenney); farklı kaynaktan model ancak palete yeniden boyanarak girer.
- Pist parçaları dışarıdan alınmaz, projede üretilir (şerit genişliği ve birleşmeler oynanışla birebir eşleşmeli); temaya göre malzeme ve kenar süsü değişir.

### 4.3 Dünya listesi
İlk 6'sı **[K]**, son 4'ü ve tüm "özellik" sütunu **[Ö]**.

| # | Dünya | Dünyaya özgü küçük fark |
|---|---|---|
| 1 | Çöl | Yok — temel mekanik öğretilir |
| 2 | Kar | Fren mesafesi uzar, savrulma bandı genişler |
| 3 | Şehir | Daha çok daralma, trafik teması yoğun |
| 4 | Volkan | Lav boşluklu uzun rampalar |
| 5 | Yer Altı Suları | Periyodik fışkıran gayzerler: zamanlamalı geçiş |
| 6 | Uzay | Düşük yerçekimi: uzun atlayış, geç iniş |
| 7 | Orman | Arka arkaya tümsekler |
| 8 | Sahil / Ada | Islak zemin bölümleri (kısa kaygan alanlar) |
| 9 | Şeker Diyarı | Zıplatan yüzeyler |
| 10 | Neon Gece | En yüksek hızlar, en dar güvenli hız bantları |

Dünya farkları yeni kod değil, tema dosyasındaki fizik ayarları ve 1–2 yeni parça türüdür.

### 4.4 Ücretsiz varlık paketleri
Hepsi Kenney.nl, CC0 lisanslı. Kesin liste her dünyanın fazında doğrulanır; kullanıcı indirip `Assets/ThirdParty/Kenney/` altına koyar.

- **Ortak:** Car Kit, Racing Kit (bariyer, tabela), UI Pack, Game Icons, Particle Pack, Impact Sounds, Interface Sounds, Music Jingles.
- **Çöl:** Nature Kit (kaktüs, kaya), Survival Kit.
- **Kar:** Holiday Kit, Nature Kit (kış ağaçları).
- **Şehir:** City Kit (Roads, Commercial, Suburban).
- **Uzay:** Space Kit, Space Station Kit.
- **Orman / Sahil:** Nature Kit, Pirate Kit.
- **Volkan, Yer Altı Suları, Şeker Diyarı, Neon Gece:** hazır paket yok; Nature Kit kayaları ve projede üretilen basit geometri, palet ve parçacık efektleriyle temalanır.
- Müzik döngüleri Kenney'de sınırlıdır; dünya başına müzik için ayrı bir ücretsiz kaynak Faz 4'te seçilir.

---

## 5. Ekonomi ve ilerleme

### 5.1 Yükseltmeler [K]
Ana ekranın altında 3 dikdörtgen kutu: **İvme**, **Maksimum Hız**, **Gelir Çarpanı**. Her satın alımda fiyat artar.
- Fiyat: `taban × 1,15^seviye` **[Ö]**.
- İvme ve Maksimum Hız her seviyede yaklaşık %3 artar.
- Gelir Çarpanı her seviyede +%10.

### 5.2 Ödül [K]
Her yarış sonunda, elenmiş olsa bile:
`ödül = bölüm tabanı × katedilen mesafe oranı × sıralama çarpanı × gelir çarpanı (+ perfect bonusu)`
Sıralama çarpanı **[Ö]**: 1. = 2,0 · 2. = 1,5 · 3. = 1,2 · 4. = 1,0 · 5./elenen = 0,8.

### 5.3 Zorluk hedefi
**[K]** Bölüm geçme şartı 1. olmaktır. **[Ö]** Bir bölüm ortalama 2–4 denemede geçilmelidir; şart sıkı olduğu için her bölümde yalnızca bir bot gerçek rakiptir, diğer üçü daha yavaş veya hataya yatkındır. Bot hızları, oyuncunun o bölümde beklenen yükseltme seviyesinin biraz üstüne ayarlanır. Tüm değerler tek bir denge tablosundan okunur; bir simülasyon aracı "bu bölüm kaç denemede geçilir" tahmini üretir.

### 5.4 Dünya geçişi [K]
10. bölüm bitince yeni dünya açılır: oyuncu ve rakip araçları yeni temaya geçer, İvme ve Maksimum Hız seviyeleri sıfırlanır, **Gelir Çarpanı ve para kalır**. Fiyat tabanı ve ödül tabanı dünya başına büyür.

### 5.5 Kayıt [K]
Anonim hesap + bulut kayıt. Yerel kayıt her zaman yazılır; internet varken buluta eşitlenir. Çakışmada en çok ilerlemiş kayıt kazanır **[Ö]**.

---

## 6. Oyun modları

### 6.1 Kariyer [K]
Çevrimdışı. §3 ve §5'teki döngü.

### 6.2 Multiplayer [K]
- İnternet zorunlu. 5 kişilik yarış.
- **İstatistikler eşitlenir**; fark yalnızca beceri ve görünümdür. Oyuncu, ulaştığı en ileri dünyanın aracını görünüm olarak kullanır.
- Yarışlar, kariyer bölümlerinden ayrı bir multiplayer pist havuzunda oynanır.
- **Eşleşme:** oyuncu "Hızlı Yarış"a basar → açık oda aranır, yoksa oda kurulur → 10 sn içinde dolmayan koltuklara hayalet oturur → yarış başlar. Hiç oyuncu bulunmazsa 4 hayaletle oynanır.
- **Canlı senkron [Ö]:** Netcode for GameObjects + Relay. Her cihaz kendi aracının otoritesidir ve saniyede 10 kez mesafe, hız, yanal konum ve durum bayraklarını yollar; uzak araçlar yumuşatılarak gösterilir. Geri sayım ve bitiş sıralaması odayı kuran cihazda belirlenir.
- **Hayalet:** bir yarışın kaydı = fren bas/bırak zamanları + seyrek mesafe örnekleri (1 KB altı). Simülasyon deterministik olduğundan kayıt birebir yeniden oynar.
- **Hayalet havuzu [K]:** mümkünse gerçek oyuncu kayıtları saklanır ve kullanılır; mümkün değilse boş koltuklara bot oturur. **[Ö]** Yöntem: oyuncular multiplayer pistlerindeki en iyi kayıtlarını bulut kaydın herkese açık bölümüne yazar; rakip adayları pist başına skor tablosundan seçilir. Bir pistte yeterli kayıt yoksa eksik koltuklar botla doldurulur. *Bu yolun ücretsiz katmanda çalıştığı Faz 5'in ilk işinde doğrulanır.*
- Hayalet kaydı yeniden doğmaları da içerir; hayalet, kaydedildiği gibi oynar.
- Ödül: sıralamaya göre para.

### 6.3 Sonsuz mod [K]
- İnternet zorunlu. Tek başına, rakipsiz.
- Pist, modüler parçalardan rastgele ve giderek zorlaşarak üretilir. Geçilen her engel para ekler; çakılınca tur biter.
- **Kasaya aktarım için ödüllü reklam şart.**
- **[Ö]** Reklam yüklenemezse para "bekleyen kasa"da kalır ve sonraki izlenen reklamda aktarılır. Oyuncu reklamı kendi kapatırsa o turun parası yanar.

### 6.4 Reklam [K]
- Unity LevelPlay (Ads Mediation paketi): ödüllü + geçiş reklamı.
- Geçiş reklamı seyrek **[Ö]**: en çok 3 yarışta bir, iki reklam arası en az 90 sn, ilk oturumun ilk 3 bölümünde hiç yok.
- Çıkış öncesi gizlilik onayı akışı (GDPR, iOS izleme izni) eklenir.

---

## 7. Teknik tasarım

### 7.1 Ortam
- Unity **6000.6.9f1** (kurulu), URP, 3D. Android modülü kurulu değil; Faz 0'da eklenir.
- Hedef: Android (ana test platformu) ve iOS. iOS derlemesi GitHub Actions macOS runner'ında, yalnızca elle tetiklenir **[K]**.
- Depo: `https://github.com/burakkayya/apex-brake-project.git`, Unity `.gitignore`.

### 7.2 Paketler
| Paket | Kullanım |
|---|---|
| Splines | Pist parçası çizgileri |
| ProBuilder | Prototip geometrisi |
| Cinemachine | Takip kamerası |
| Input System | Dokunma |
| Localization | TR/EN metin tabloları |
| uGUI + TextMeshPro **[Ö]** | Arayüz (Türkçe karakterli yazı tipi varlığı) |
| Test Framework | Otomatik testler |
| Authentication, Cloud Save, Leaderboards | Hesap, kayıt, hayalet havuzu |
| Multiplayer Services, Netcode for GameObjects | Oda, Relay, canlı yarış |
| Ads Mediation (LevelPlay) | Reklam |

Paketler ilgili fazda eklenir; baştan hepsi kurulmaz.

### 7.3 Klasör yapısı
```
Assets/
  _Game/
    Art/            Shared/  Worlds/Desert/ ...
    Audio/          Shared/  Worlds/Desert/ ...
    Data/           Worlds/ Levels/ Cars/ Bots/ Economy/
    Localization/
    Prefabs/        Cars/ Track/ UI/ FX/
    Scenes/         Boot  Main  Race
    Scripts/
      Core/         açılış, servis kaydı, olaylar
      Simulation/   saf C#: araç, engel, temas, boost
      Track/        parça, tarif, pist kurucu, şerit haritası
      Racing/       yarış akışı, kamera, araç görseli
      AI/           bot kararları ve profilleri
      Economy/      ödül, yükseltme, denge
      Progression/  dünya/bölüm ilerlemesi, tema uygulama
      Save/         yerel + bulut kayıt
      Net/          oda, senkron, hayalet
      Ads/          reklam sarmalayıcı
      UI/           ekranlar, HUD, mini harita
      Editor/       pist ve denge araçları
    Tests/          EditMode/ PlayMode/
  ThirdParty/       Kenney/ ...
docs/
.github/workflows/
```
Her `Scripts/` alt klasörü ayrı bir derleme birimidir (asmdef). `Simulation` hiçbir Unity sahne sınıfına bağlı değildir.

### 7.4 Ana veri türleri
- **TrackPiece:** çizgi, uzunluk, tür (düz/viraj/rampa/tümsek/daralma), güvenli hız, şerit haritası.
- **LevelRecipe:** parça dizisi, bot profilleri, ödül tabanı, zorluk.
- **WorldTheme:** §4.1.
- **BalanceTable:** fiyat, ödül ve ölçek katsayıları.
- **GhostRun:** pist kimliği, girdi olayları, mesafe örnekleri.

### 7.5 Sahne akışı
`Boot` (servisler, kayıt yükleme) → `Main` (temalı ana ekran: araç, 3 yükseltme kutusu, mod düğmeleri) → `Race` (tarife göre pist kurulur, tema uygulanır) → sonuç ekranı → `Main`.

### 7.6 Test [Ö]
- Simülasyon, ekonomi ve hayalet tekrar oynatımı için EditMode testleri (önce test, sonra kod).
- Her bölüm tarifi için otomatik kontrol: usta bot bitirebiliyor mu, süre 30–45 sn aralığında mı.
- Her faz sonunda Android cihazda elle oynanış testi.

---

## 8. Riskler

| Risk | Karşılık |
|---|---|
| Tek tuş oynanış sıkıcı çıkabilir | Faz 1 gri kutu prototipi tam bunu sınar; eğlenceli değilse devam edilmez |
| Hayalet havuzunun ücretsiz katmanda kurulamaması | Faz 5'in ilk işi doğrulama; yedek plan botlar |
| Relay ücretsiz kotasının aşılması | Uzaktan kapatılabilen anahtar: canlı eşleşme kapanır, yalnızca hayalet kalır |
| Cihaz otoriteli senkron hileye açık | v1'de kabul edilir; multiplayer ödülleri ölçülü tutulur |
| iOS: cihaz testi ve TestFlight için Apple Developer hesabı (yıllık 99$) gerekir | Hesap yokken CI yalnızca "derleniyor mu" kontrolü yapar |
| Private depoda macOS CI dakikaları hızlı tükenir | iOS derlemesi yalnızca elle tetiklenir |
| Farklı Kenney paketlerinin tonu tutmayabilir | Ortak palet atlasına yeniden boyama, tek ışık düzeni |
| "Reklam şart" kuralı kötü yorum getirebilir | Bekleyen kasa; oran sonradan uzaktan ayarlanabilir tutulur |

---

## 9. Fazlar

Her fazın sonunda çalışan bir Android derlemesi ve kullanıcı onayı vardır.

| Faz | İçerik | Bitti sayılır |
|---|---|---|
| **0 — Kurulum** | Unity projesi, depo bağlantısı, Android modülü, temel görsel ayar, Android CI derlemesi | Boş sahne telefonda açılıyor |
| **1 — Gri kutu çekirdek** | Parça + tarif sistemi, araç simülasyonu, fren, iki kademeli hata, rampa/tümsek, kamera | Tek araçla bir test pisti oynanıyor ve fren hissi onaylandı |
| **2 — Yarış** | 4 bot, şerit içi temas, daralmalar, Perfect Brake Boost, mini harita, sonuç ekranı | 5 araçlı yarış baştan sona oynanıyor |
| **3 — Döngü** | Ödül, 3 yükseltme, ana ekran, yerel kayıt, TR/EN, 10 Çöl bölümü, denge aracı | **Çekirdek döngü kanıtı:** 10 bölüm, bölüm başına 2–4 denemeyle bitiyor |
| **4 — Çöl kimliği** | Tema sistemi, Kenney varlıkları, efekt, ses, titreşim, arayüz cilası | Çöl dünyası yayınlanabilir görünüyor |
| **5 — Çevrimiçi temel** | Anonim hesap, bulut kayıt, hayalet kayıt/oynatma, hayaletli multiplayer | Hayaletlere karşı çevrimiçi yarış |
| **6 — Canlı multiplayer** | Oda + Relay, canlı senkron, boş koltuğa hayalet | İki telefon aynı yarışta |
| **7 — Sonsuz mod + reklam** | Sonsuz pist üretimi, LevelPlay, ödüllü ve geçiş reklamı, gizlilik onayı | Test reklamıyla kasaya aktarım çalışıyor |
| **8 — İçerik** | Dünya 2–10: tema, 10 bölüm, dünya farkı, geçiş sekansı | 100 bölüm oynanabilir |
| **9 — Yayın** | iOS CI derlemesi ve kontrolü, performans, mağaza görselleri, kapalı test | Mağazaya gönderime hazır |

Faz 3 sonundaki kanıt olumsuzsa Faz 4'e geçilmez; mekanik yeniden ele alınır.

---

## 10. Kullanıcıdan beklenenler (engelleyici değil)

1. §4.3'teki son 4 dünya ve dünya farkları için onay veya değişiklik.
2. Test için USB ile bağlanabilen bir Android telefon.
3. Faz 5 öncesi: Unity Cloud organizasyonu/projesi için onay.
4. Faz 7 öncesi: LevelPlay hesabı ve uygulama kimlikleri.
5. Faz 9 öncesi: Apple Developer ve Google Play geliştirici hesapları.

---

## 11. Revizyon (2026-11-16): Yanal fizik ve canlı yarış

Kullanıcı Faz 2'yi görünce savrulmayı ve şerit takibini yapay buldu. Bu bölüm §3.2'nin viraj kısmını, §3.3'ün viraj kuralını, §3.5'i, §3.7'yi ve §5.1'i **geçersiz kılar**. Rampa kuralları ve Perfect Brake Boost (§3.4) aynı kalır.

### 11.1 Savrulma merkezkaç kuvvetinden doğar [K]
- Her aracın yanal konumu ve yanal hızı vardır. Virajda araca `hız² / yarıçap` kadar dışa doğru ivme etki eder.
- Lastikler en çok `Tutunma` (ilk değer 12 m/s²) kadar yanal ivme sağlar. Yeterliyse araç çizgisinde kalır; yetmezse aradaki fark kadar dışa kayar.
- Araç her an kendi çizgisine dönmeye çalışır [K]. Hız düşüp tutunma geri gelince çizgisine süzülür.
- Sabit eşik (%100–115) ve sabit hız kaybı (%40) yoktur. Hız kaybı kaymanın kendisinden (lastik sürtünmesi) doğar.
- **Eleme [K]:** yalnızca araç kayarak yolun dışına çıkarsa. Kariyerde eleme, multiplayer'da yeniden doğma, sonsuz modda tur sonu (§3.3'teki mod ayrımı aynı).
- Viraj içi yapay hız tavanı kalkar. Yerine lastik yükü gelir **[Ö]**: lastik viraja ne kadar yüklenmişse araç o kadar az hızlanır.
- **Viraj içinde fren [K]:** hızı düşürür ve toparlar, ama fren basılıyken yanal tutunma azalır (ilk değer ×0,8).
- Yolun iki yanında 1,5 m banket vardır **[Ö]**; araç merkezi banketi de aşınca yoldan çıkmış sayılır.
- Tümsek **[Ö]**: güvenli hızın üstünde araç hafifler (0,6 sn tutunma ×0,3 ve küçük bir yanal itme); %115'in üstünde kontrolsüz havalanıp çakılır.

### 11.2 Temas ve sollama
- **Temas [K]:** oyuncu botlara, botlar oyuncuya çarpabilir. Temas, şerit eşitliğine değil araçların gerçek konumuna (uzunluk 4,5 m, genişlik 2 m) bakar. Arkadan temasta arkadaki yavaşlar, öndeki itilir; yandan temasta araçlar birbirini yana iter ve ikisi de biraz hız kaybeder.
- **Koruma [Ö]:** başka bir aracın çarpması kimseyi yolun dışına atamaz; çarpılan araç kısa süre yol kenarında tutulur. Yoldan yalnızca kendi hızın yüzünden çıkılır.
- **Sollama [K]:** önünde yavaş bir araç varken yanında yer varsa araç kendiliğinden yana kayıp geçer; oyuncu ve botlar için aynı. Daralmalarda yer yoksa sıraya girilir.
- "Her cihaz yalnızca kendi aracının tepkisini hesaplar" ilkesi (§3.5) çevrimiçi modlar için geçerliliğini korur.

### 11.3 Botlar gerçek oyuncu gibi [K]
- Oyuncuyla aynı fiziği kullanırlar; kayarlar, toparlarlar, yoldan çıkarlar.
- Her yarışta farklı form; viraj başına değişen risk; ara sıra büyük hata, ara sıra kusursuz geçiş; karar gecikmesi (tepki süresi); startta farklı çıkış; kayarken panik freni; çizgide hafif gezinme.
- Gizli lastik bant (geride kalana yardım) yoktur **[Ö]**.

### 11.4 Dördüncü yükseltme: Lastik [K]
Ana ekranın altındaki kartlar dört tanedir: **İvme, Maksimum Hız, Lastik, Gelir Çarpanı**. Lastik her seviyede `Tutunma`yı yaklaşık %3 artırır **[Ö]**. Dünya geçişinde İvme ve Maksimum Hız gibi Lastik de sıfırlanır **[Ö]**; Gelir Çarpanı kalır.
