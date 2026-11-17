# Ledger — plan: docs/superpowers/plans/2026-11-17-faz-2c-gercek-fizik.md

## Başlangıç

- Çalışma ağacında Faz 2b Görev 5'ten kalan commit'lenmemiş değişiklikler (`ChaseCameraRig.cs`, `RaceDebugProbe.cs`, `RacerView.cs`, `RaceHud.cs`, `RaceController.cs`, `GrayboxPieceFactory.cs`, `GrayboxSceneBuilder.cs`) derleniyordu (yalnızca `RaceDebugProbe` CS0618 uyarıları); olduğu gibi `3fd359d wip: yarım kalan görünüm ve kamera işleri` olarak commit'lendi. Banket fikri Görev 1'de yeni yüzey üretimine taşındı.

## Görev 1 — Gerçek pist geometrisi ve izdüşüm

- Ruling: Yol yüzeyi, banket ve yan etekler tek mesh + tek `MeshCollider` (alt meshler: yol / banket / etek), katman `Road`; geometri çalışma anı derlemesindeki `TrackPieceGeometry`'de, Editor fabrikası ve testler aynı kodu kullanır — testler Editor derlemesine bağlanmadan aynı parçaları kurabilsin — bedeli: yok denecek kadar az (fabrika meshi varlık olarak kaydeder).
- Ruling: Katmanlar sabit indekslidir (`Road`=8, `Ground`=9, `Car`=10, `TrackLayers`); adlar `ProjectPhysicsSetup` (Editor, `[InitializeOnLoad]`) ile TagManager'a yazılır ve commit'lenir, çarpışma matrisi `TrackLayers.ApplyCollisionMatrix` (`RuntimeInitializeOnLoadMethod`) ile oyun açılışında koddan kurulur — derlemede de geçerli — bedeli: 8–10 başka amaçla kullanılırsa çakışır (Editor betiği hata yazar).
- Ruling: Rampa parçası 46 m: 10 m düz, 12 m karesel yükseliş (tepe 1,6 m, kalkış açısı ≈ 15°), 12 m gerçek boşluk, iniş yüzeyi 0,6 m yüksek bir ön kenarla başlar ve 6 m'de düze iner, 6 m düz çıkış. Plandaki "iniş yüzeyi yol seviyesinde başlar ve aşağı eğimli karşılama rampasıyla düze bağlanır" ifadesi kendi içinde çelişkili (yol seviyesinden aşağı eğim düze inemez); iniş için aşağı eğim (aracın teğet inmesi) esas alındı — bedeli: yavaş araç boşluğa düşünce 1,1 m'lik iniş ön yüzüne çarpar (fiziksel, istenen). [Görev 2'de yükseliş değişti: 8 m geçiş + 4 m sabit 12,7°, tepe 1,8 m; parça yay uzunluğu ≈46,2 m — aşağıdaki rampa kalkışı Ruling'i.]
- Ruling: Boşluk artık parçanın sonunda değil ortasında; `gapLength` (12) geometri verisi olarak kalır. Eski simülasyonun `TrackLayout.TryGetNextEvalPoint`'i boşluğu parça sonunda varsayar — eski sim Görev 5'te silinir — bedeli: eski `Race_Graybox` sahnesindeki eski görünümde rampa yerleşimi Görev 5'e kadar uyumsuz.
- Ruling: `TrackPieceAsset`'ten `safeSpeedOverride` ve `minJumpSpeed` silindi; eski `TrackLayout` doğrulaması (Bump `SafeSpeed > 0`, Ramp `0 < MinJumpSpeed < SafeSpeed`) Görev 5'e kadar durduğundan `ToDef` yalnızca eski sim için sabit eski değerleri (`Legacy*`) doldurur; yeni araç/RaceManager bunları okumaz — eski sahne Görev 5'e kadar çalışır kalsın — bedeli: Görev 5'te bu sabitler ve doğrulama birlikte silinmeli.
- Ruling: `TrackBuilder.Build` pistin altına tek bir `Ground` kutusu (üst yüzey yol tabanının 0,5 m altında, pist sınırlarından 150 m taşan) kurar ve onu çocuk olarak ekler; mevcut `TrackBuilderTests.Build_Twice_DoesNotDuplicateChildren` beklentisi `pieces + 1` olarak güncellendi, `Asset()` yardımcısı silinen alanı artık yazmıyor — bedeli: yok.
- Ruling: Fizik malzemeleri çalışma anında oluşturulur (`TrackLayers.RoadMaterial` μ = 1,0; `GroundMaterial` μ = 0,4) ve `Build` sırasında atanır; `WheelCollider` zemin malzemesini kendisi okumadığından araç her tekerin değdiği yüzeyin sürtünme oranını lastik sürtünmesine çarpan olarak uygular (Görev 2) — bedeli: yeni yüzey türü eklenirse malzemesi atanmalı.
- Ruling: `TrackPath.Project` ipucu negatif/NaN ise tüm pistte arar (yeniden doğma/ilk çağrı için); aksi halde plan gereği ±40 m — bedeli: yok.
- Not: Görev 1'in uygulaması testlerle aynı adımda yazıldı; testler ilk koşuda yeşildi (önce kırmızı görülmedi).

Task 1: complete — PlayMode 19/19 (TrackGeometryTests 11 + TrackBuilderTests 8), EditMode 149/149

## Görev 2 — Fizik araç

Testler önce yazıldı; boş taslak sınıflarla derlenip koşuldu: 18 testten 15'i kırmızı (3'ü taslakta kendiliğinden geçiyordu: durma, düşük kare hızı, rampaya sürünme), sonra uygulama ve ayar.

- Ruling: Taban Rigidbody + 4 `WheelCollider` olarak kaldı (ışın tabanlı modele geçilmedi) — ölçümlerde titreme/zıplama/tünelleme yok (durağanda 3 sn'de 0,022 m; en yüksek hızda 1000 m + rampa inişinde gövde merkezi yolun en az 0,196 m üstünde) — bedeli: WheelCollider'ın birleşik kayma modeli kapalı kutu; denge (ön/arka yanal sertlik) dar bir aralıkta değişiyor, sonraki ayarlar küçük adımlarla yapılmalı.
- Ruling: Çekiş dört çeker, %70 arka (`drive = AllWheel`, `rearDriveShare = 0.7`) — arkadan itişte tam gazlı virajda arka kolay kopuyor, rampa uçuşları da daha kararsızdı (tarama A–D vs E–F); dört çekerle kayma görünür ama frenle yakalanabilir — bedeli: daha az "güç kayması" hissi.
- Ruling: Yerçekimi çarpanı 2,5, araç başına ek kuvvet olarak (`gravityMultiplier`, proje ayarı değişmedi) — tutunma/fren ivmesi ~30 m/s² hedefine μ ≈ 1,5 ile ulaşılıyor, devrilme payı korunuyor, uçuşlar kısa ve ağır hissediyor — bedeli: WheelCollider dinlenme konumu normal yerçekimine göre hesaplandığından ek statik sarkma (≈0,12 m) teker bağlantı yüksekliğinde telafi edilir (`VehicleBuilder`); yay sertliği değişirse bu telafi otomatik.
- Ruling: Aerodinamik aşağı bastırma `F = 5·v²` (140 km/s'te ≈ 7,6 kN) — yüksek hızda geniş virajı (R 60) temiz tutar, keskin virajı (R 40/R 25) değil; tamamen fiziksel bir kuvvet — bedeli: havada da uygulanır (fiziksel), uçuşu biraz kısaltır.
- Ruling: Lastik denge ön/arka yanal sertlik 1,50/1,45 (yanal tepe kayma 0,2) seçildi — taramada arka belirgin hafif olduğunda (1,65/1,55) daha iyi lastikle (×1,3) virajda ani savrulma görüldü (daha fazla tutunma → daha kötü sonuç); dengeli ayarda yükseltme düzenli iyileştiriyor (R 40 tam gaz: taban yoldan çıkar 8,5 m/17°, ×1,3 lastik 3,4 m'de kalır) — bedeli: R 60'ta tam gazla ~10° hafif kayma var (yolda kalıyor, sapma 2,3 m).
- Ruling: Gövde/sac sürtünmesi 0,35 (`bodyFriction`, birleştirme Minimum) — varsayılan 0,6/ortalama ile rampaya yavaş tırmanan araç kalkış kenarında karnının üstünde asılı kalıyordu; kayan sac için gerçekçi değer — bedeli: takla atan araç biraz daha uzun kayar.
- Ruling: Tümsek tepe yüksekliği 0,6 → 0,8 m (uzunluk 12 m aynı) — yumuşak, uzun stroklu süspansiyon (dalış/yatma görünsün diye) 0,6 m'lik tümseği tamamen yutuyordu (havada 0,08 sn); 0,8 m'de 116–142 km/s'te 0,16–0,24 sn havada, dik iniş — bedeli: plan değerinden sapma.
- Ruling: Rampa kalkışı karesel geçiş (8 m) + sabit açılı düz (4 m, 12,7°), tepe 1,8 m (plan ≈1,6) — tamamen eğrisel kalkışta virajla sıkışan süspansiyon kenarda arkayı fırlatıp aracı burun üstü döndürüyordu (≈80°/sn); düz kısım süspansiyonu kalkıştan önce boşaltıyor — bedeli: 0,2 m daha yüksek tepe.
- Ruling: Rampa yol aralıkları yay uzunluğuyla verilir (`RampTakeoffArc`, `RampGapEndArc`); yatay ölçüyle verildiğinde parça sonunda ~0,1 m boşluk ve bir kapak duvarı oluşuyor, inen aracın burnu buna takılıp takla atıyordu (24 denemenin 2'si). Ayrıca yüzey kesitleri gerçek teğetle kurulur (parça eklerinde kama boşlukları vardı). İkisi için `Surface_IsContinuous_NoWallOrHoleAtSeams` testi eklendi — bedeli: yok.
- Ruling: `NeverTunnelsThroughRoad_AtTopSpeed` testine kendi eklediğim "pist sonuna ulaşır" beklentisi "iniş bölgesini geçer" olarak değişti — en yüksek hızda (139 km/s kalkış) uzun uçuş, his hedefine uygun olarak bazen sert iniş/takla ile bitiyor (ara bir ayarda 24 kalkışın 1'i); testin konusu tünelleme — bedeli: yok (tünelleme beklentisi aynı).
- Ruling: `OffRoad` için "Ground'a değme" teker temasının yanında gövde temasını da kapsar (`VehicleTelemetry.BodyOnGround`, `TouchesGround`) — rampadan sürünerek düşen araç burnu boşlukta, hiçbir tekeri bir yere değmeden gövdesiyle Ground'a yaslanıp kalıyordu; spec §12.2 "yol yüzeyi dışındaki zemine değerse" der — bedeli: yok.
- Ruling: Telemetri testinde `SpeedKmh` toleransı 0,5 → 1,5 km/s — telemetri fizik adımı başında ölçer, test adım sonunda; fark bir adımlık ivmedir (≈0,6 km/s) — bedeli: yok.
- Ruling: Fren: %75 ön, ABS tork kısma (ileri kayma > 0,3 → fren torku yarıya) — daha az ön paylı frende virajda frenlenen araç arkadan dönüyordu — bedeli: yok.
- Ruling: Telemetri ölçümleri için iki Explicit geliştirme testi eklendi (`VehicleDevTelemetry` CSV, `VehicleTuningSweep` aday yapılandırma taraması; normal koşuda çalışmaz) ve `Dev_PhysicsCar` sahnesi (`DevDriveSession`: pist, araç, fren girdisi, takip kamerası, betikli fren bölgeleri ve ekran görüntüsü) — bedeli: Görev 5'te istenirse silinebilir.

### Son VehicleConfig (Data/Vehicles/VehicleConfig_Default.asset = koddaki ilk değerler)

kütle 1000 kg, ağırlık merkezi 0,30 m (temas düzleminden), gövde 1,8×0,5×4,1 m (+kabin), dingil 2,6 m, iz 1,7 m, teker r 0,34 m / 20 kg; motor 11 500 N, hız tavanı 43 m/s (F = 11 500·(1 − (v/43)²)), dört çeker %70 arka; fren 26 000 N, %75 ön, ABS 0,3; hava direnci 0,35·v², aşağı bastırma 5·v², yuvarlanma 0,012; süspansiyon strok 0,30 m, yay 30 000 N/m, sönüm 3 500, hedef 0,30, denge çubuğu 3 000; lastik ileri (0,4/1,0, 0,8/0,65, sertlik 1,3), yanal (0,2/1,0, 0,5/0,75, ön 1,50, arka 1,45); direksiyon en çok 30°, 150°/sn; yerçekimi ×2,5; gövde sürtünmesi 0,35.

### Ölçülen telemetri (PlayMode, sabit adım 0,02; `VehiclePhysicsTests` günlüğü + `VehicleTuningSweep`)

| Ölçüm | Değer |
|---|---|
| 0 → 100 km/s | 3,36 sn (yükseltme Motor ×1,3: 2,60 sn) |
| En yüksek hız | 144,8 km/s (Hız Tavanı ×1,2: 170,8) |
| Kalkışta çökme (arka) | +1,5° |
| Tam fren 144,8 → 0 | 1,58 sn, 32,2 m; burun dalışı −3,8°; durunca yön sapması 0,4° |
| R 60 tam gaz (144 km/s giriş) | yolda; dışa en çok 2,3 m; kayma 10,7°; yatma 4,6° |
| R 40 tam gaz (144 km/s) | yoldan çıkar (virajın 65. m'sinde), sapma 8,5 m, kayma 17,4° (sol virajda); dev pistinde R 60'tan sonra girişte 7 m genişleyip yolda kaldı — "çoğu zaman" çıkar |
| R 40, girişten 40 m önce 1 sn fren | 70–81 km/s giriş, yolda, sapma 1,1 m, kayma 3,9° |
| R 40 tam gaz, kayma başlayınca fren | yolda, en çok 6,1 m dışa; çıkıştan 4 sn sonra çizgiden 0,02 m |
| R 25 tam gaz | her denemede yoldan çıkar (virajın ~30. m'si), kayma 14,4° |
| Rampa, 150 m düzlükten (128–129 km/s kalkış) | 17,7–19,5 m uçuş, iniş burnu −19…−27°, dik iniş, yola devam |
| Rampa, en yüksek hızda (139 km/s) | ~22 m uçuş, iniş −18,5°, devam (önceki ayarda 24'te 1 takla) |
| Rampaya ≈25 km/s'te sürünme | boşluğa düşer, burnu Ground'a değer (d ≈ 84, kalkış 82,4) |
| Tümsek, 142 km/s | 0,18 sn havada, pitch −11…+11°, devam |
| Düşük kare hızı | 15 ve 60 fps: 5 sn'de 110,32 / 108,32 m (15 fps'te ölçüm 3 adım fazla sürdü: 5,06 sn); fark %1,8 |

Task 2: complete — VehiclePhysicsTests 18/18 (iki ardışık koşu, aynı sayılar), PlayMode toplam 38/38 (Explicit geliştirme testleri hariç), EditMode 149/149

## Görev 3 — Yarış kuralları (RaceManager)

Testler önce yazıldı; boş iskeletle 16 testten 15'i kırmızı (1'i — startta temas yok — iskelette kendiliğinden geçiyordu), sonra uygulama.

- Ruling: `RaceController.cs` silinmedi; eski HUD/görünüm/mini harita/sonuç paneli ve `RaceDebugProbe` ona bağlı ve Görev 5'te yeniden yazılacak. `RaceManager` yanına eklendi; ortak `RaceState` enum'u `RaceController.cs`'ten `RaceState.cs`'e taşındı (değerler aynı), eski kod dokunulmadan derleniyor — bedeli: Görev 5'te `RaceController` ve bağımlıları silinmeli.
- Ruling: `OffRoad` = (en az 2 teker Ground'a değiyor YA DA gövde Ground'a değiyor) ve gövde merkezinin altında Road yok, 0,4 sn kesintisiz. "Havada" = hiçbir teker yerde değil VE gövde hiçbir yüzeye (Road/Ground) değmiyor; havadayken ters dönme sayacı bekler, yol dışı sayacı sıfırlanır. İlk sürümde "havada"yı yalnızca Ground temasına bakarak tanımlamıştım; yol üstünde ters yatan araç (gövdesi Road'da, teker yok) "havada" sayılıp hiç `Flipped` olmuyordu — test yakaladı — bedeli: yok.
- Ruling: `Fell` = gövde merkezi izdüşümdeki (boşlukta sanal) yol yüzeyinin 3 m'den fazla altında, anında kesinleşir; mevcut pistlerde zemin yolun yalnızca 0,5 m altında olduğundan pratikte boşluğa düşen araç `OffRoad` (gövde/teker Ground'a değer) olarak çıkar — bedeli: derin uçurumlu pist gelirse `Fell` devreye girer.
- Ruling: `RacerEntry.TargetLateralOverride` (float?) eklendi: doluysa ev çizgisi yerine izlenir — Görev 4'teki `OvertakePlanner` ve testler bu kancayı kullanır — bedeli: yok.
- Ruling: `VehicleMotor.Placed` olayı eklendi: start dizilimi/yeniden doğmada `LineDriver` izdüşümü tüm pistte yeniden arar ve telemetri sıfırlanır (aksi halde ±40 m pencere ışınlanan aracı yanlış yere izdüşürüyordu) — bedeli: yok.
- Ruling: Yeniden doğma noktası `max(startDistance, çıkış mesafesi − 30)`; nokta bir Ramp/Bump parçasının [Start − 4, End] aralığına düşerse `Start − 6`'ya çekilir; ev çizgisinde, yol yönüne bakarak, hızı 0 — bedeli: yok.
- Ruling: Bitiren araç kendi frenine basar (sürücü davranışı; pist sonu yol dışı olduğundan) ve oyun dışı denetimi bitirince durur — bedeli: Görev 4 `BotDriver` bitiren/oyun dışı araçta `Brake` yazmamalı.
- Ruling: Test kurulumu: yana itme/bankete çıkma testlerinde oyuncu önce frenleyip grubun arkasına düşer (aksi halde yanındaki botlara fiziksel olarak çarpıp onları da yoldan çıkarıyordu ya da yanal hareketi engelleniyordu — ikisi de doğru fizik); bankete çıkış hedefi `HalfWidth + 0,5` (araç tümüyle 1,5 m'lik banketin üstünde) ve 2 m/sn ile kademeli verilir — `HalfWidth + 1,0`'da ya da ani 8,5 m hedef sıçramasında dış tekerler yol kenarından taşıp araç 0,5 m'lik basamaktan zemine kayıyor ve haklı olarak oyun dışı kalıyordu; yeniden doğma hızı `RacerRespawned` anında ölçülür (bir adım sonra motor zaten itiyor: 0,82 m/s) — bedeli: yok.
- Not: itme testi dış kuvveti 25 → 60 m/s² (yanal itiş) — 25'te araç lastik tutunmasıyla (~30 m/s²) itişe karşı koyup yolda kalıyordu (fiziksel).

Task 3: complete — RaceRulesTests 16/16 (iki ardışık koşu), PlayMode toplam 54/54 (Explicit geliştirme testleri hariç), EditMode 149/149

## Görev 4 — Bot sürücüler ve sollama

Testler önce yazıldı; boş taslaklarla (`OvertakePlanner` ev çizgisini döndürür, `BotDriver` hiç frenlemez) koşuldu: `OvertakePlannerTests` 9 testten 3'ü kırmızı (6'sı "ev çizgisinde kal" türü, taslakta kendiliğinden geçiyordu), `BotDriverTests` 8/8 kırmızı (tam gazla herkes ilk R 25'te — ~475 m — ya da ilk R 40'ta yoldan çıkıyordu).

- Ruling: `OvertakePlanner` `Racing/` altında saf C# (UnityEngine kullanmaz); EditMode test derlemesi `ApexBrake.Racing`'e bağlandı — plan dosya yerini `Racing/` olarak veriyor, `Simulation` derlemesi Unity'siz kalsın — bedeli: yok.
- Ruling: Sollama kuralı Faz 2b'den aynen (bakış 25 m, araç genişliği 2 m + 0,8 m boşluk, en az 0,5 sn tutma, yan aday geçiş çizgisinde 10 m içinde araç varsa geçersiz); "belirgin yavaş" eşiği 1,0 → 1,5 m/s (fizik araçta hız gürültülü, eşit hızlı araçlar arasında gidip gelmesin). Bu bir oyun sonucu kuralı değil, çizgi seçimi (sürücü kararı); hedef ayrıca yol kenarından 1,3 m içeride tutulur (yol daralırsa kırpılır, yer kalmazsa sollama bırakılır) — bedeli: yok.
- Ruling: Planlanan çizgi `LineDriver`'a sıçramadan verilir: `RaceManager` izlenen çizgiyi hıza bağlı en çok `max(2 m/s, 0,12 × ileri hız)` ile kaydırır (≈ 7°'lik şerit değişimi). Daralmalardaki ev çizgisi geçişi de bu yoldan geçer (eskiden doğrudan veriliyordu). `TargetLateralOverride` (testler) doğrudan verilir — bedeli: daralmaya en dış şeritten giren araç çizgisine ~25–30 m'de varır (daralma parçasının 15 m'lik geçişi + önceki 20 m'lik karışım yeterli).
- Ruling: `BotDriver` bir `MonoBehaviour` (yürütme sırası −30: yönetici −50'den sonra, `LineDriver` −20'den önce); `RaceManager` bot için yalnızca `Driving` (yarışta, oyun dışı değil, bitirmemiş) bayrağını yazar, `Brake`'i bot yazar; bitiren ve oyun dışı araçta bot frene dokunmaz. Botun gezinmesi `LineOffset` ile ev çizgisine eklenir — bedeli: yok.
- Ruling: Bot muhakemesi: ≈ 2,5 sn'lik yol boyunca 1,5 m adımlarla `CurvatureAt`'ten kendi çizgisindeki yarıçapı (`1/|k| − işaret·yanal`) bulur, "bu viraj için kanaat ettiği yanal tutunma" `a` ile `√(a·R)` hızını, kendi fren kanaatiyle (≈ 18–21 m/s²) fren mesafesini tahmin eder ve tepki süresi kadar eski durumla karar verir; tümsekte `SlopeAt` farkından tepe yarıçapını bulup cesarete göre (×1,6 + 6·(Courage − 0,85)) hız tahmin eder. Kanaat `a = ReferenceGrip × Courage × (1 + 0,04·Form) × (1 + (1 − Skill)·0,14·gürültü)`; her engel için zar: `MistakeChance` → ya tutunmayı ×1,3–1,7 abartır ya da fren noktasını 8–18 m kaçırır; `HeroChance` → `a = 1,05 × ReferenceGrip`, gecikmesiz karar. Kayınca (telemetri `IsSliding`) tepki süresi (+ acemide 0–0,25 sn) sonra 0,18 sn (+ acemide 0,25–1,15 sn) fren. Startta 0,05–0,4 sn × (1,3 − Skill) geç kalkış. Diğer araçlara bakmaz — bedeli: yok; bu sayıların hiçbiri yarışın sonucunu doğrudan vermez, yalnızca pedal kararıdır.
- Ruling: `ReferenceGrip = 27 m/s²`. Ölçüm (`BotTuningSweep`, Explicit; hata/kusursuz geçiş kapalı, tam beceri, orta şerit, tek tohum): tutunma kanaati çarpanı (×22 m/s²) 1,0 → 100 m'den bitişe 39,5 sn; 1,2 → 38,2; 1,4 → 37,1; 1,6 → 37,2; 1,8 → 37,6; 2,0 → 38,1; 2,3 → 38,6 (kayarak); 2,6 / 3,0 / 4,0 → ilk R 25'te yoldan çıkar. Önce 31 denendi (en hızlı bölge): Master tek başına 5 tohumda 3'ünü bitirdi, yarışta 6'da 4 kez çıktı (rampadan inip 50 m sonraki R 40'ta, R 25'lerde) — gerçek oyuncu için fazla hatalı. 27 (≈ ×1,23) Master'ı en iyi sürüşün ~1 sn gerisinde, nadiren hatalı tutuyor — bedeli: en iyi sürücü bot oyuncunun kusursuz turundan ~1–2 sn yavaş.
- Ruling: Bot rampaya girince (yükselişin başından iniş + 8 m'ye kadar) frene dokunmaz ve fren mesafesi hesabında havada geçen kısmı (kalkış → iniş + 10 m) saymaz — ilk sürümde inişten 50 m sonraki R 40 için havada fren kararı veriyor (işe yaramıyor), sonra geç kalıp yoldan çıkıyordu; rampada frenleyen bot burnunu düşürüp takla attı (622 m, Flipped). Sürücü muhakemesi; fizik sonucu değişmedi — bedeli: yok.
- Ruling: Profiller (eski alanlar eski simülasyon için Görev 5'e kadar kalır): Master Skill 0,9 / Courage 1,0 / Mistake 0,02 / Hero 0,25 / tepki 0,06 / gezinme 0,25; Balanced 0,7 / 0,95 / 0,06 / 0,10 / 0,10; Reckless 0,45 / 1,15 / 0,15 / 0,12 / 0,12 / gezinme 0,45; Slow 0,6 / 0,85 / 0,05 / 0,05 / 0,16, araç Motor ×0,92, Hız tavanı ×0,94 (`VehicleUpgrades`; `RaceManager.racerUpgrades` boşsa botlara profilden). `VehicleConfig` değişmedi — bedeli: yok.
- Ruling: `RaceManager`'a eklenenler: `raceSeed` (0 = her yeniden başlatmada yeni tohum; `CurrentSeed`), `botProfiles`, `racerCount` ve `homeLanes` (testler: 2 araçlı sollama), `PlaceOnGrid` (yalnızca geri sayımda start dizilimi), `OvertakeCount`, `playerAutopilot` (çekim/ölçüm kancası: oyuncuyu Master profilli bir bot sürer). `RacerEntry`: `IsOvertaking`, `Bot` — bedeli: yok.
- Ruling: Yarış pisti testlerde `GrayboxLayouts.Race` dizisinden kodla kurulur; Editor fabrikası `Recipe_Graybox_Race` tarifini aynı diziden yazar (tek kaynak) — bedeli: yok.
- Not: "tek başına" bot testleri aynı pistte 5 aracı farklı ev şeritlerinde, birbirleriyle çarpışmaları kapalı koşar (hız için). Şerit virajlarda yarıçapı ±6,4 m değiştirir; bot bunu kendi çizgisinden hesaplar.

Task 4: complete — EditMode 158/158 (OvertakePlannerTests 9), PlayMode 62/62 (BotDriverTests 8; Explicit geliştirme testleri hariç). BotDriverTests son koşu: Master tek başına 5 tohumda 4 bitirdi (1'i R 40L'de, 879 m); Reckless 8 tohumda 2 kez çıktı (ikisi de 2. rampadan sonraki R 40R'de, 1215–1226 m); Slow ort. 45,9 sn vs Master 42,4 sn; 6 yarışta kazananlar Balanced, Master ×4, Reckless.

## Görev 5 — Temizlik, görünüm, kamera, HUD ve doğrulama

### Silinen dosyalar (eski saf C# simülasyon, eski görünüm ve testleri)

- `Scripts/Simulation/`: `CarSim.cs`, `RaceSim.cs`, `BotBrain.cs`, `SimClock.cs`, `CrashPolicy.cs`, `CarPhase.cs`, `SimEvent.cs`, `CarStats.cs` (+ `.meta`).
- `Scripts/Racing/`: `RaceController.cs`, `RacerView.cs`, `RaceDebugProbe.cs` (+ `.meta`).
- `Prefabs/Cars/Car_Graybox.prefab` (eski görünüm aracı).
- EditMode test sınıfları: `BoostTests`, `BotBrainTests`, `CarSimCrashPolicyTests`, `CarSimDrivingTests`, `CarSimObstacleTests`, `LateralPhysicsTests`, `OvertakeTests`, `RaceSimContactTests`, `RaceSimRaceTests`, `SimClockTests` (+ `.meta`). EditMode test sayısı 158 → 34 (`LaneMapTests`, `TrackLayoutTests`, `OvertakePlannerTests` kaldı).
- Küçültülenler: `SimTuning` yalnızca şerit geometrisi (`LaneWidth`, `LaneBlendDistance`); `TrackPieceDef`'ten `SafeSpeed`, `MinJumpSpeed`, `SafeSpeedFromRadius`; `TrackLayout`'tan `TryGetNextEvalPoint` ve hız doğrulamaları (rampa için yalnızca `0 < GapLength < Length`); `TrackPieceAsset`'ten `Legacy*` sabitleri; `BotProfile`'dan eski alanlar (`Acceleration`, `TopSpeed`, `Grip`, `BrakeError`, `Risk`, `RiskJitter`, `ToStats`). Perfect Brake Boost'a ait kod, olay, HUD metni ve sonuç satırı kalmadı. `TrackLayoutTests.SafeSpeedFromRadius_UsesGrip12` silindi, `Ctor_RejectsRampWithMinJumpAboveSafe` → `Ctor_RejectsRampWithGapNotInsidePiece`; `LaneMapTests` viraj yardımcısı `SafeSpeed` yerine `Radius` kullanır.

### Yeni / yeniden yazılan

- `VehicleVisual` (plandaki `RacerView → VehicleVisual`): frende stop lambaları (örnek malzeme, ışıma) kırmızı yanar; yanal kayma ≥ 6° (tam 18°) ya da teker ileri kayması ≥ 0,35 iken teker altından gri duman (parçacık, dünya uzayı). Gövde fizik gövdesinin kendisi; tekerlek dönüşü/süspansiyon `VehicleMotor`'da.
- `ChaseCameraRig` Cinemachine'siz, doğrudan kamerayı süren `LateUpdate` bileşeni; `RaceHud`, `MiniMapView`, `RaceResultPanel` `RaceManager`'a bağlı; `RaceStaging` (geliştirme/çekim kancası: oyuncu fren planı `TouchBrakeInput.ForceBrake` ile, olay/mesafe ekran görüntüleri, CSV telemetri; boşta hiçbir şey yapmaz).
- `GrayboxSceneBuilder` `Race_Graybox`'ı yeniden üretir: ışık, Global Volume, `Track` (Recipe_Graybox_Race, zemin malzemesi Ground_Grass), `Race` (`TouchBrakeInput`, `RaceManager` — `carPrefab = Car_Physics`, `VehicleConfig_Default`, `Eliminate/Eliminate`, `RaceStaging`), `Main Camera` + `ChaseCameraRig`, EventSystem + Canvas (HUD, mini harita, sonuç paneli). Build Settings: yalnızca bu sahne (ilk).

### Rulings

- Ruling: Kamera Cinemachine yerine kendi `LateUpdate` bileşeni (`ChaseCameraRig`, yürütme sırası 100) — fizik gövdesinin interpolasyonlu dönüşüne göre tek yerde ofset/FOV/yatma/sarsıntı/eleme/bitiş; Cinemachine ardışık düzeninde eleme "takibi bırak" ve bitiş çaprazı iki ayrı yerde yazılıyordu. Yön aracın burnunu değil (yatay) hız yönünü yumuşatarak izler: kayan araç kadrajda yan döner (drift görünür). Kamera, altındaki yol/zemin yüzeyinin en az 1,4 m üstünde tutulur (rampa/tümsekte gömülmesin) — bedeli: Cinemachine paket bağımlılığı Racing derlemesinde kullanılmıyor (asmdef referansı duruyor).
- Ruling: Kamera ofsetleri plandaki ilk değerlerden (0, 3,2, −7)/(0, 4,2, −10) → (0, 4,2, −8)/(0, 5,6, −11); FOV 62 → 78 aynı. İlk değerlerle dikey 1080×1920 ekranda araç ekranın ortasını kaplıyor, geri sayım yazısının ve ilerideki virajın önünü kesiyordu; ufuk ekranın ortasındaydı (çok gökyüzü). Geri sayımda grid görünsün diye (0, 8,5, −17); start ile yarış ofsetine süzülür. Bakış noktası araç + 0,6 m + hızla 5 m'ye kadar ileri — bedeli: hız hissi biraz azalır; FOV genişlemesi ve yaklaşma korunur.
- Ruling: Kayma etkisi (kamera kayması ≤ 1,4 m, yatma ≤ 4°) `SlipAngleDeg / 20°` ile ölçeklenir ve yalnızca `IsSliding` iken; ölçülen yarış kaymaları çoğunlukla 6–12° olduğundan pratikte 0,2–0,7 m ve 0,5–3,6° — "hafif, mide bulandırmayan" — bedeli: küçük kaymalarda etki zor fark edilir.
- Ruling: `OutDetector` — iki tekeri yol dışı zemine (Ground) değen ama ağırlık merkezi hâlâ yol kenarının üstünde olan araç ("asılı") bu durum 1,5 sn kesintisiz sürerse `OffRoad` (eski koşul: Ground teması VE merkezin altında yol yok, 0,4 sn — aynen duruyor). Bulgu: Reckless (tohum 606) ikinci rampadan sonra dış tekerleri 0,5 m'lik yol kenarı basamağından aşağıda, motor açık, hızı 0, sonsuza dek takılı kaldı (oyun dışı da sayılmadı, bitirmedi de; yarış hiç bitmeyebilirdi). Spec §12.2 "yol yüzeyi dışındaki zemine değerse" der — bedeli: tekerleri banket dışına 1,5 sn'den uzun taşan araç elenir; bankette kalan (tekerleri banket üstünde) elenmez.
- Ruling: `RaceRulesTests.OnShoulder_IsNotOut` kurulumu: bankete çıkış hedefi `HalfWidth + 0,5` → `HalfWidth + 0,1` ve ön koşul "tekerler banket üstünde kaldı" (`maxLat < HalfWidth + 1,5 − 0,85`) eklendi. Eski hedefle takipçi 0,5 m taşıp merkezi `HalfWidth + 1,0`'a getiriyordu: dış tekerler (±0,85 m) banketin (1,5 m) dışında, Ground'a değiyordu — yeni "asılı" kuralıyla haklı olarak oyun dışı. Test konusu (bankette olan araç elenmez) değişmedi — bedeli: yok.
- Ruling: Bot muhakemesine "dış kenar payı" eklendi: virajın dış kenarına (yol + banket − 1 m) kalan yer 6 m'den azsa tutunma kanaati doğrusal olarak ×0,75'e kadar düşer — Master ev şeridi 0 (ilk sağ virajda en dış), 120 km/s ile 2 m dışa kayıp banket kenarına (−8,5 m) kadar gidiyor, yanındaki araca değip iki yarışta ilk virajda elendi. Sürücü muhakemesi; fizik değişmedi — bedeli: dış şeritteki botlar virajda biraz daha yavaş.
- Ruling: Geri sayımda sıra dizine göre (oyuncu 1/5) — izdüşüm gürültüsü gridde "3 / 5" gösteriyordu; yalnızca sunum — bedeli: yok.
- Ruling: Pist dizisi değişmedi (1341 m). Oyuncu aracını Master profilli bot sürünce (iyi sürüş vekili) yarış 41,96 / 42,18 / 42,74 / 43,12 sn; spec §3.1 30–45 sn içinde. Tüm parça türleri (R 40, R 25, R 60 yok — yarış tarifinde zaten yoktu, tümsek, iki rampa, iki daralma) botlarca sürülebiliyor — bedeli: iyi bir insan oyuncu ~40 sn'ye inebilir; bant içinde.
- Ruling: Sonuç panelinde başlık oyuncu bitirdiyse sıra (`1.`), değilse `ELENDIN`; satır durumu `Bitti` / `Yol disi` (OffRoad) / `Takla` (Flipped) / `Dustu` (Fell) / `Yarista`. Eski "Perfect/Good" satırı silindi, satırlar yukarı kaydı — bedeli: yok.
- Ruling: `faz2c-offroad.png` aracı çimde, takibi bırakmış kamerayla gösterir (oyun dışından 1,2 sn sonra, panel açılmadan); yarışın bittiği ve `Yol disi` durumu ayrı bir karede: `faz2c-offroad-result.png` (aynı tür bir koşuda panel açıkken; panel aracı örtüyor). `faz2c-contact.png` bir otopilot yarışından (oyuncu mavi araçla yan yana, `TEMAS` + `SOLLAMA`) — bedeli: yok.
- Ruling: `Library/PackageCache/com.unity.ide.visualstudio@…/SdkStyleProjectGeneration.cs` dosyası paket önbelleğinde kayıptı (yalnızca `.meta`), Editor güvenli modda derleme hatası verdi (proje kodu değil). Bozuk önbellek klasörü çalışma alanı dışına taşındı, `Client.Resolve()` paketi yeniden indirdi, dosya yeniden içe aktarıldı; derleme temiz — bedeli: yok.
- Not: `Explicit` `BotTuningSweep` kaldı (bot kanaat ölçümü); `Dev_PhysicsCar` sahnesi ve `DevDriveSession` duruyor (yeniden üretildi).

### Telemetri raporu (Race_Graybox, Editor, 1080×1920, `RaceStaging` CSV kaydı; mesafeler pist boyunca)

Pist: S100 0–100, R40 sağ 100–163, S100, Daralma3 263–323, S50, Tümsek 373–385, S50, R25 sol 435–474, S100, Rampa 574–620, S100, Daralma2 720–780, S50, R40 sol 830–893, S50, R25 sağ 943–982, S100, Rampa 1082–1129, S50, R40 sağ 1179–1241, S100 (bitiş 1341,7).

| Ölçüm | Değer |
|---|---|
| 0 → 100 km/s (yarışta, start) | 3,36–3,48 sn |
| En yüksek hız (yarışta) | 140,0–141,9 km/s (S100 ~700 m; tek araçlı ölçümde 144,8) |
| Tam fren | 133 → 0 km/s 1,48 sn, 27,4 m; en büyük burun dalışı −3,7° |
| R 40 sağ #1, tam gaz (118 km/s giriş) | yolda; dışa en çok 2,3 m, kayma 11,9°, kamera kayması 0,73 m, yatma 2,1° |
| R 40 sağ #1, frenle (85–118 km/s) | yolda; sapma 1,2–2,8 m, kayma 5–9° |
| R 25 sol #1, tam gaz (136 km/s giriş) | yoldan çıkar (475 m), sapma 30 m, kayma 13,7° — 4 koşunun 4'ünde |
| R 25 sol #1, frenle (96–111 km/s giriş) | yolda; sapma 1,7–5,7 m, kayma 5,6–9,4° |
| R 40 sol, tam gaz (142 km/s giriş) + kayınca fren | yolda; dışa 4,8 m, kayma 10,8°, kamera kayması 0,48 m, yatma 1,4° (`faz2c-drift.png`) |
| R 40 sol, frenle (116–124 km/s) | yolda; sapma 1,7–2,4 m, kayma 6–7° |
| R 25 sağ, frenle (92–107 km/s) | yolda; sapma 1,4–5,8 m (kayınca frenli koşuda), kayma 4,7–8,9° |
| R 25 sağ, yalnızca R 25 sol öncesi frenlenen koşu (tam gaz) | yoldan çıkar (995 m) |
| R 40 sağ #2 (rampa inişinden 50 m sonra), frenle (126–129 km/s) | yolda; sapma 1,6–3,9 m, kayma 6,5–8,8° |
| Rampa 1 / 2 | kalkış 123–127 km/s; `IsAirborne` (≥ 0,15 sn yerden kesik) 13–14,5 m / 0,38–0,42 sn (gerçek uçuş ≈ 18 m), iniş burnu −17…−27°, yola devam |
| Kamera yavaşta (< 60 km/s) | ofset ≈ (0, 4,8, −9,2), FOV ≈ 65, yatma 0 |
| Kamera hızlıda (> 130 km/s) | ofset (0, 5,55, −10,9), FOV 77, yatma 0 |
| Kamera kayarken | ofset ≈ (±0,2–0,7, 5,35, −10,4), FOV ≈ 74, yatma en çok 2,4–3,6° |

Arka arkaya iki yarış (oyuncu otopilotta, Master profili; tohumlar 1630219717, 123806559):
1. Sen 41,96 sn; Reckless 42,58; Master (1318 m'de, oyuncu bitirince yarış bitti); Slow (1236 m); Balanced Yol disi (773 m, Daralma2'de sıkışma).
2. Sen 42,74 sn; Master 43,56; Balanced 43,80; Reckless (1309 m); Slow (1248 m).

Diğer staging yarışları: otopilot 42,18 sn (1.) ve 43,12 sn (Sen/Master/Reckless aynı adımda çizgide, 1.); manuel fren planı + kayınca fren 45,92 sn (4.).

### Kod taraması (Step 4)

`Assets/_Game/Scripts` içinde `SafeSpeed`, `MinJumpSpeed`, `CornerLimit`, `Perfect`, `Boost` araması: **boş**. Kalan hız karşılaştırmaları / hız kullanımları: `BotDriver` (sürücü muhakemesi: virajda tahmini hız ve fren mesafesi, `vv <= allow`), `OvertakePlanner` (çizgi seçimi: öndeki araç belirgin yavaş mı), `RaceManager` (izlenen çizginin kayma hızı ileri hızla ölçeklenir; sollama planlayıcısına hız girdisi), `VehicleMotor` (motor itiş eğrisi `1 − (v/tavan)²`; `PlaceAt` hızı sıfırlar — start/yeniden doğma), `VehicleTelemetry` / `RaceHud` / `ChaseCameraRig` / `VehicleVisual` (sunum: km/s, kayma, kamera, duman), `LineDriver` (bakış mesafesi hızla ölçeklenir), `DevDriveSession` / `RaceStaging` (geliştirme). Hiçbir oyun kuralı (oyun dışı, sıra, bitiş) hız karşılaştırmaz; `OutDetector` yalnızca temas, yükseklik, yönelim ve süre kullanır.

Task 5: complete — lider doğrulaması (2026-11-17): EditMode 34/34, PlayMode 62/62; kod taraması boş; APK 43,7 MB derlendi (cihazda denenmedi). Ajan limit nedeniyle son raporu veremeden kesildi; commit edde947.
