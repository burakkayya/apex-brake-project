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
