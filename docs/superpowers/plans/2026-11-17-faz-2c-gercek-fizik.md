# Faz 2c: Gerçek Fizik — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Araç dinamiğini kural/formül tabanlı simülasyondan Unity fizik motoruna taşımak: gerçek gövde, süspansiyon, lastik sürtünmesi ve gerçek pist geometrisi; oyun kurallarında hiçbir hız eşiği kalmayacak.

**Architecture:** Her araç bir `Rigidbody` + 4 `WheelCollider`'dır. `VehicleMotor` gaz/fren kuvvetini, `LineDriver` direksiyonu (hedef çizgiye doğru) uygular; oyuncunun tek girdisi frendir. Pist parçaları gerçek `MeshCollider` yüzeyleridir. `RaceManager` araçların pistteki ilerlemesini dünya konumundan izdüşümle ölçer, sıralamayı tutar ve tek oyun kuralını uygular: yol dışına çıkan ya da ters dönen araç oyun dışı kalır.

**Tech Stack:** Unity 6000.6.9f1 (PhysX), URP, C#, Splines, Cinemachine 3, Input System, uGUI + TMP, Unity Test Framework, `unity` CLI.

**Spec:** `docs/superpowers/specs/2026-11-15-apex-brake-design.md` — **§12 bağlayıcıdır**; §11.6 (kamera) geçerlidir.

## Global Constraints

- Proje kökü `C:\Projects\ApexBrakeProject`; komutlarda `--project-path C:\Projects\ApexBrakeProject`. Editor açıktır; kapatılmaz. `unity test` kullanılmaz; testler `unity command test run ...` ile koşar (EditMode eşzamanlı; PlayMode `--mode playmode --async-tests` + `unity command test status`).
- Sahne/prefab/asset YAML'ı elle düzenlenmez; Editor betikleri ve `unity command script eval` kullanılır.
- **Yasak (spec §12.1):** oyun kurallarında hız eşiği, güvenli hız, sınır hızı, yüzde bandı; "hız X'ten büyükse savrul/çakıl/atla" türü hiçbir koşul. Aracın konumunu, hızını ya da dönüşünü doğrudan yazarak sonuç üretmek (yeniden doğma ve start dizilimi hariç). Sonuçlar yalnızca kuvvetlerden ve çarpışmalardan doğar.
- **Serbest:** fiziksel parametreler (kütle, motor/fren kuvveti, lastik sürtünme eğrileri, süspansiyon, yerçekimi, hava direnci, ağırlık merkezi, viraj denge çubuğu etkisi); direksiyon denetleyicisi; botların kendi kararları için tahmin yürütmesi (bu bir oyun kuralı değil, sürücü muhakemesidir); HUD ve kamera için ölçüm (kayma açısı, havada olma) — bunlar yalnızca sunumdur, sonucu etkilemez.
- Fizik: `Time.fixedDeltaTime = 0.02`. Araç `Rigidbody` interpolasyonu açık. Katmanlar: `Road` (yol + banket), `Ground` (yol dışı zemin), `Car`.
- Ledger: `docs/superpowers/plans/2026-11-17-faz-2c-progress.md`. Her sapma/karar `Ruling: <karar> — <neden> — <yanlışsa bedeli>`.
- Önce test, sonra kod (fizik için PlayMode testleri; saf mantık için EditMode). Fizik testleri **nitel sonuç** ve geniş bantlarla yazılır (ör. "yolda kalır", "yoldan çıkar", "3–6 sn içinde"), kesin sayı beklemez. Testler `Time.timeScale` ile hızlandırılabilir (fizik adımı 0.02 kalır); test sonunda eski değerine döndürülür.
- Görev başına commit; mesajlar `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` ile biter. Push yok.
- Arayüz metinleri ASCII; `OnGUI` yok.
- **His hedefleri** (kural değil; fiziksel parametreleri ayarlarken ulaşılacak davranış, ölçülüp raporlanır):
  - Düzlükte 0 → 100 km/s yaklaşık 3–4 sn; en yüksek hız yaklaşık 140–150 km/s (≈ 40 m/s).
  - Tam fren 140 → 0 km/s yaklaşık 1,5–2,2 sn; fren sırasında burun belirgin biçimde dalar.
  - Geniş virajda (R 60) tam gazla temiz dönüş; orta virajda (R 40) tam gazla girince araç belirgin biçimde dışa kayar/drift yapar ve çoğu zaman yoldan çıkar; frenleyip girince döner.
  - Kayan araç kontrolü tamamen kaybetmez: hız düşünce lastikler tutar ve araç çizgisine döner.
  - Rampada yeterince hızlı araç karşıya uçup iner; yavaş araç boşluğa düşer; aşırı hızlı araç uzun uçar ve sert iner (takla atabilir).
  - Gövde virajda yatar, frende dalar, gazda çöker (süspansiyon gerçek).
  - Hız göstergesi km/s'dir.

## Review Focus

1. **Düşük kare hızı / kare atlaması:** fizik `FixedUpdate`'te yürür; kare hızı düşünce araç davranışı değişmemeli. → Görev 2.
2. **Araç pistin altına düşmesi (tünelleme) ve iniş:** en yüksek hızda yol yüzeyinden geçip düşmemeli; rampa inişinde yere gömülmemeli. → Görev 1–2.
3. **İzdüşüm belirsizliği:** pist kendine yaklaştığı yerlerde (U dönüşü) araç yanlış parçaya izdüşmemeli; ilerleme geri sıçramamalı. → Görev 1.
4. **Start:** 5 araç geri sayımda kıpırdamamalı, start anında birbirine çarpmadan çıkmalı. → Görev 3.
5. **Oyun dışı kararının yanlış verilmesi:** rampadan uçarken (havada, altında yol yokken) araç elenmemeli; bankete çıkıp dönen araç elenmemeli. → Görev 3.

---

### Task 1: Gerçek pist geometrisi ve izdüşüm

**Files:** Modify `Editor/GrayboxPieceFactory.cs`, `Track/TrackBuilder.cs`, `Track/TrackPath.cs`, `Track/TrackPieceAsset.cs`; Test `Tests/PlayMode/TrackGeometryTests.cs`

**Interfaces — Produces:**
```csharp
// TrackPath
public struct TrackPose { public float Distance; public float Lateral; public float HeightAboveRoad; public Vector3 Forward; public Vector3 Right; public float HalfWidth; }
public TrackPose Project(Vector3 worldPos, float hintDistance);   // hintDistance çevresinde (±40 m) arar; ilk çağrıda hint 0
public Vector3 PointAt(float distance, float lateral);            // yol yüzeyi üzerindeki dünya noktası
public float CurvatureAt(float distance);                         // işaretli, 1/m; + sağa (botlar ve kamera için)
public float SlopeAt(float distance);                             // yol yüzeyinin eğimi (radyan; botlar için)
```
- **Yol yüzeyi:** her parça için çizgi boyunca üretilen mesh + `MeshCollider`, katman `Road`. Genişlik = `laneCount × 3.2` + iki yanda 1,5 m banket (banket ayrı düz renk, aynı çarpışma yüzeyi). Yüzey örnekleme aralığı en çok 1 m.
- **Rampa:** yol yüzeyi kalkıştan önceki ~12 m'de yumuşakça yükselir (tepe ≈ 1,6 m, kalkış açısı ≈ 12–15°), ardından **boşluk** (yol yüzeyi yok; mevcut `gapLength` 12 m), sonra iniş yüzeyi yol seviyesinde başlar ve hafif aşağı eğimli bir karşılama rampasıyla (≈ 6 m) düze bağlanır. Boşluğun altı `Ground`.
- **Tümsek:** yol yüzeyinde gerçek, yumuşak bir kambur (12 m boyunca, tepe ≈ 0,6 m).
- **Yol dışı:** tüm pistin altına yayılan, yol yüzeyinden 0,5 m aşağıda büyük bir `Ground` zemini (düz renk, çarpışmalı).
- Parça verisinden silinenler: `safeSpeedOverride`, `minJumpSpeed` ve viraj için `SafeSpeed` kullanımları (oyun kuralı olarak). `radius`, `turnSign`, `laneCount`, `gapLength` geometri verisi olarak kalır.
- Fizik malzemeleri: `Road` yüksek sürtünme, `Ground` düşük sürtünme (araç yol dışında tutunamaz).

- [ ] **Step 1: Başarısız testleri yaz** (kodla küçük pistler kur)
```csharp
[UnityTest] public IEnumerator Road_HasCollider_OnRoadLayer_FullWidthPlusShoulders()    // merkezden ±(HalfWidth+1.4) m'de aşağı ışın Road'a çarpar; ±(HalfWidth+2.0)'da çarpmaz (Ground'a çarpar)
[UnityTest] public IEnumerator Ramp_HasRealGap_NoRoadColliderInside()                   // boşluğun ortasında aşağı ışın Road'a çarpmaz
[UnityTest] public IEnumerator Ramp_SurfaceRisesBeforeGap()                             // kalkış kenarında yüzey ≥ 1.2 m yukarıda
[UnityTest] public IEnumerator Bump_SurfaceIsRaised()                                   // tümsek ortasında yüzey ≥ 0.4 m yukarıda
[UnityTest] public IEnumerator Project_OnCentreLine_ReturnsDistanceAndZeroLateral()     // PointAt(d, 0) → Distance d ± 0.3, Lateral ± 0.1
[UnityTest] public IEnumerator Project_Lateral_SignIsRightPositive()
[UnityTest] public IEnumerator Project_InsideCorner_IsContinuous()                      // viraj boyunca 1 m adımlarla: Distance tekdüze artar
[UnityTest] public IEnumerator Project_UTurn_DoesNotJumpToOtherLeg()                    // U dönüşlü pistte hint ile doğru bacak
[UnityTest] public IEnumerator Curvature_StraightIsZero_RightCornerPositive()           // R 40 sağa → +0.025 ± 0.003
[UnityTest] public IEnumerator Slope_RampApproachIsPositive()
[UnityTest] public IEnumerator Ground_ExistsBelowAndBeyondRoad()
```
- [ ] **Step 2–4:** çalıştır (başarısız), uygula, parçaları yeniden üret; yeni testler ve mevcut `TrackBuilderTests` yeşil
- [ ] **Step 5: Commit** — `feat(track): gerçek çarpışma yüzeyli pist ve izdüşüm`

---

### Task 2: Fizik araç

**Files:** Create `Racing/VehicleMotor.cs`, `Racing/LineDriver.cs`, `Racing/VehicleConfig.cs`, `Racing/VehicleTelemetry.cs`, `Editor/CarPrefabFactory.cs`; Prefab `Prefabs/Cars/Car_Physics.prefab`; Test `Tests/PlayMode/VehiclePhysicsTests.cs`

**Interfaces — Produces:**
```csharp
[CreateAssetMenu(menuName = "Apex Brake/Vehicle Config")]
public sealed class VehicleConfig : ScriptableObject {     // yalnızca fiziksel değerler; kütle, ağırlık merkezi, motor kuvveti, hız tavanı (motor gücünün bittiği hız),
}                                                          // fren kuvveti ve ön/arka dağılımı, hava direnci, aşağı bastırma, süspansiyon (yay, sönüm, mesafe), lastik sürtünme eğrileri (ileri/yanal), en büyük direksiyon açısı ve hızı, yerçekimi çarpanı
public struct VehicleUpgrades { public float Engine, TopSpeed, Tire; }   // 1 = taban; çarpan olarak motor kuvvetine, hız tavanına, lastik sürtünmesine uygulanır

public sealed class VehicleMotor : MonoBehaviour {
    public VehicleConfig config;
    public bool Brake { get; set; }              // tek sürüş girdisi; false = tam gaz
    public bool EngineOn { get; set; }           // false: araç frenle yerinde tutulur (geri sayım)
    public float SteerInput { get; set; }        // −1..+1; LineDriver yazar
    public void Apply(VehicleUpgrades u);
    public Rigidbody Body { get; }
}
public sealed class LineDriver : MonoBehaviour {
    public TrackPath Path { get; set; }
    public float TargetLateral { get; set; }     // izlenecek çizgi (pist merkezinden, + sağ)
    public float Distance { get; }               // son izdüşüm
    public float Lateral { get; }
}
public sealed class VehicleTelemetry : MonoBehaviour {      // yalnızca sunum/ölçüm; sonucu etkilemez
    public float SpeedKmh { get; }  public float ForwardSpeed { get; }
    public float SlipAngleDeg { get; }           // hız vektörü ile burun arasındaki açı
    public int GroundedWheels { get; }  public bool IsAirborne { get; }   // 0 teker yerde ≥ 0.15 sn
    public bool IsSliding { get; }               // |SlipAngleDeg| ≥ 8 ve hız ≥ 20 km/s (HUD/kamera için)
    public float UpDot { get; }                  // gövde yukarısı · dünya yukarısı
}
```
- **Araç:** `Rigidbody` (kütle ≈ 1000 kg, alçak ağırlık merkezi), gövde için kutu çarpışma, 4 `WheelCollider`; arkadan ya da dört çeker (his hedeflerine göre seç, ledger'a yaz). Görsel tekerlekler `WheelCollider` pozuna bağlanır ve döner; ön tekerler direksiyonla döner.
- **Gaz/fren:** `Brake` false iken motor torku (hız tavanına yaklaştıkça azalan); true iken dört tekere fren torku. ABS/çekiş kontrolü gibi yardımcılar eklenebilir ama yalnızca tork sınırlayarak — hız ya da konum yazılmaz.
- **Direksiyon (`LineDriver`):** ileriye bakan saf takip: araç hızına bağlı bir mesafe (≈ 0,35 sn × hız, en az 6 m) ilerideki `PointAt(d, TargetLateral)` noktasına yönelir; direksiyon açısı ve dönüş hızı `VehicleConfig` ile sınırlıdır. Kayma sırasında karşı direksiyon bu takibin doğal sonucudur.
- **Yerçekimi:** arcade his için `Physics.gravity` çarpanı (≈ 1,5–2,5×) proje ayarında ya da araç başına kuvvetle; seçilen değer ledger'a.
- `WheelCollider` kararsız çıkarsa (titreme, zıplama, tünelleme giderilemiyorsa) aynı arayüzlerle ışın tabanlı süspansiyon + lastik kuvveti modeline geçilebilir; bu bir `Ruling` gerektirir ve "sonuç kuvvetlerden doğar" ilkesi korunur.

- [ ] **Step 1: Başarısız testleri yaz** (kodla kurulan pistlerde tek araç; `timeScale` ile hızlandır)
```csharp
[UnityTest] public IEnumerator Stationary_WithEngineOff_DoesNotMove()                 // 3 sn: yer değiştirme < 0.2 m
[UnityTest] public IEnumerator Straight_AcceleratesTo100Kmh_In2To5s()
[UnityTest] public IEnumerator Straight_TopSpeed_Between130And160Kmh()
[UnityTest] public IEnumerator Straight_HoldsItsLine()                                // 600 m: |Lateral − hedef| < 0.6 m
[UnityTest] public IEnumerator Brake_FromTopSpeed_StopsIn1_2To2_8s_WithoutSpinning()  // durduğunda burun yol yönünden < 20° sapmış
[UnityTest] public IEnumerator LaneOffset_CarMovesToNewTargetLine()                   // hedef +3.2 → 3 sn içinde |Lateral − 3.2| < 0.6
[UnityTest] public IEnumerator WideCorner_R60_FlatOut_StaysOnRoad()
[UnityTest] public IEnumerator Corner_R40_FlatOut_SlidesWide()                        // en büyük |Lateral − hedef| ≥ 2 m YA DA yoldan çıkar
[UnityTest] public IEnumerator Corner_R40_BrakedBeforeEntry_StaysOnRoad()             // girişten 40 m önce 1 sn fren
[UnityTest] public IEnumerator TightCorner_R25_FlatOut_LeavesRoad()
[UnityTest] public IEnumerator Slide_ThenBrake_CarRecoversToLine()                    // R 40 tam gaz giriş, kayma başlayınca fren → yolda kalır ve çıkıştan 4 sn sonra |Lateral − hedef| < 1 m
[UnityTest] public IEnumerator Ramp_AtSpeed_LandsOnFarSide()                          // tam gazla 150 m düzlükten sonra rampa → iniş yüzeyine iner, yola devam eder
[UnityTest] public IEnumerator Ramp_Crawling_FallsIntoGap()                           // rampaya 25 km/s civarında bırakılan araç boşluğa düşer (Ground'a değer)
[UnityTest] public IEnumerator Bump_AtSpeed_GoesAirborneBriefly_AndContinues()
[UnityTest] public IEnumerator NeverTunnelsThroughRoad_AtTopSpeed()                   // 1000 m düz + rampa inişi: gövde merkezi hiç yol yüzeyinin altına inmez
[UnityTest] public IEnumerator Upgrades_EngineAndTopSpeedAndTire_ChangeBehaviour()    // Engine 1.3 → 100 km/s'e daha çabuk; TopSpeed 1.2 → daha yüksek tavan; Tire 1.3 → R 40 tam gaz girişte daha az dışa kayma
[UnityTest] public IEnumerator LowFrameRate_SameOutcome()                             // Application.targetFrameRate 15 ve 60: 5 sn sonundaki mesafe farkı < %5
[UnityTest] public IEnumerator Telemetry_ReportsAirborneAndSliding()
```
Bu testler birer hız eşiği kuralı **değildir**: fizik parametrelerinin his hedeflerini tutturduğunu doğrular. Tutmuyorsa yalnızca `VehicleConfig` ve pist geometrisi ayarlanır.
- [ ] **Step 2–4:** çalıştır (başarısız), uygula ve ayarla, yeşil
- [ ] **Step 5: Ölçümleri ledger'a yaz** (0→100, tavan hız, fren süresi/mesafesi, R 60/R 40/R 25 tam gaz sonuçları, rampa uçuş mesafesi) ve **Commit** — `feat: fizik motoruna dayalı araç`

---

### Task 3: Yarış kuralları (`RaceManager`)

**Files:** Create `Racing/RaceManager.cs`, `Racing/RacerEntry.cs`, `Racing/OutDetector.cs`; Delete `Racing/RaceController.cs`; Test `Tests/PlayMode/RaceRulesTests.cs`

**Interfaces — Produces:**
```csharp
public enum RaceState { Countdown, Racing, Ended }
public enum OutReason { None, OffRoad, Fell, Flipped }
public enum OutPolicy { Eliminate, Respawn, EndRun }

public sealed class RacerEntry {
    public string Name;  public bool IsPlayer;  public int HomeLane;
    public VehicleMotor Motor;  public LineDriver Driver;  public VehicleTelemetry Telemetry;
    public float Distance { get; }  public float Lateral { get; }
    public int Rank { get; }  public bool Finished { get; }  public float FinishTime { get; }
    public bool IsOut { get; }  public OutReason OutReason { get; }  public bool IsRespawning { get; }
}
public sealed class RaceManager : MonoBehaviour {
    public TrackBuilder track;  public TouchBrakeInput input;  public OutPolicy playerPolicy;  public OutPolicy botPolicy;
    public IReadOnlyList<RacerEntry> Racers { get; }   public const int PlayerIndex = 0;
    public RaceState State { get; }  public float CountdownRemaining { get; }  public float ElapsedTime { get; }
    public event Action<int, OutReason> RacerOut;  public event Action<int> RacerRespawned;  public event Action<int> RacerFinished;
    public event Action<int, int, float> RacerContact;   // (a, b, bağıl hız) — yalnızca HUD/kamera için
    public event Action Restarted;  public event Action Ended;
    public void Restart();
}
```
- **Tek kural — oyun dışı (spec §12.2).** Bir araç şu durumlardan biri **kesinleşince** oyun dışıdır:
  - `OffRoad`: tekerleklerinden en az ikisi `Ground` katmanına 0,4 sn boyunca kesintisiz değiyor ve altında `Road` yok.
  - `Fell`: gövde merkezi, izdüşümdeki yol yüzeyinin 3 m'den fazla altında.
  - `Flipped`: `UpDot < 0.3` durumu 2 sn sürüyor (yan ya da ters yatmış).
  - Havadaki araç (hiçbir teker yerde değil) oyun dışı sayılmaz. Bankette olan araç oyun dışı değildir.
- **Politika:** `Eliminate` → araç yarıştan çıkar (motoru kapanır, fizik gövdesi kalır ama diğer araçlarla çarpışması kapatılır 1,5 sn sonra). `Respawn` → kesinleşmeden 1,5 sn sonra, çıktığı andaki ilerlemesinin 30 m gerisinde, ev çizgisinde, yol yönüne bakar ve duran halde yeniden doğar; nokta bir rampa yükselişi/boşluğu/tümseği üstüne düşüyorsa ondan önceki düz yüzeye çekilir. `EndRun` → `Eliminate` gibi (sonsuz mod için).
- **Start:** 5 araç ev çizgilerinde, aynı hizada, `EngineOn = false`. Geri sayım 3 sn; bitince `EngineOn = true`.
- **İlerleme ve sıra:** `Distance` izdüşümden; sıra: bitirenler bitiş zamanına göre, sonra diğerleri `Distance`'a göre. Oyun dışı araç mesafesini korur.
- **Bitiş:** `Distance ≥ TotalLength`. Oyuncu bitirince ya da oyun dışı kalınca 1,5 sn sonra `Ended`.
- **Ev çizgisi:** `LaneMap.LateralAt` (daralmalar dahil) → `Driver.TargetLateral`. (Sollama Görev 4'te.)

- [ ] **Step 1: Başarısız testleri yaz**
```csharp
[UnityTest] public IEnumerator Countdown_CarsStayStill_ThenGo()
[UnityTest] public IEnumerator Start_FiveCars_NoContactInFirst2Seconds()
[UnityTest] public IEnumerator DrivingOffTheSide_IsOut_OffRoad()                    // araç yola dik itilir (dış kuvvetle) → RacerOut(OffRoad)
[UnityTest] public IEnumerator OnShoulder_IsNotOut()
[UnityTest] public IEnumerator AirborneOverGap_IsNotOut()
[UnityTest] public IEnumerator FallingIntoGap_IsOut()                               // Fell ya da OffRoad
[UnityTest] public IEnumerator Upturned_For2s_IsOut_Flipped()                       // araç ters çevrilip bırakılır
[UnityTest] public IEnumerator BrieflyTilted_IsNotOut()                             // 1 sn yan yatıp düzelen araç
[UnityTest] public IEnumerator Eliminate_StopsCar_KeepsDistance_RankDrops()
[UnityTest] public IEnumerator Respawn_After1_5s_30mBack_OnHomeLine_Upright_Stopped()
[UnityTest] public IEnumerator Respawn_NeverOnRampOrBump()
[UnityTest] public IEnumerator Respawn_CarCanContinueAndFinish()
[UnityTest] public IEnumerator Rank_OrdersByDistance_FinishedFirst()
[UnityTest] public IEnumerator PlayerFinish_EndsRaceAfterDelay()
[UnityTest] public IEnumerator Restart_ResetsEverything_NoStaleEvents()
[UnityTest] public IEnumerator CarsCollide_PhysicallyAndRaiseContact()              // iki araç aynı çizgide, arkadaki daha hızlı → RacerContact; öndeki ileri itilir
```
- [ ] **Step 2–4:** çalıştır (başarısız), uygula, yeşil
- [ ] **Step 5: Commit** — `feat: yarış yöneticisi ve oyun dışı kuralı`

---

### Task 4: Bot sürücüler ve sollama

**Files:** Create `Racing/BotDriver.cs`, `Racing/OvertakePlanner.cs`; Modify `Simulation/BotProfile.cs`; Test `Tests/PlayMode/BotDriverTests.cs`, `Tests/EditMode/OvertakePlannerTests.cs`

- **`BotDriver`** her fizik adımında `Motor.Brake`'i yazar. Karar bir **sürücü muhakemesidir**, oyun kuralı değil:
  - İleriye bakar (hıza bağlı, ≈ 2,5 sn'lik yol): `CurvatureAt` ve `SlopeAt` ile önündeki en keskin viraj ve eğim değişimini bulur.
  - Kendi lastiğine ve becerisine göre o noktadan "rahat geçebileceğini düşündüğü" bir hız tahmin eder (ör. viraj için sürtünme ve yarıçaptan; rampa/tümsek için profildeki cesaret payı). Bu tahmin **bottan bota ve yarıştan yarışa farklıdır** ve yanlış olabilir.
  - Şu anki hızı o tahminin üstündeyse ve kalan mesafe fren mesafesi tahminine yaklaştıysa fren yapar.
  - Kayma algılarsa (telemetri) beceriye göre tepki verir: iyi sürücü kısa fren, kötü sürücü geç ya da aşırı fren.
- **Profil** (`BotProfile`, mevcut dört arketip korunur): `Skill` (tahmin doğruluğu), `Courage` (hız payı), `MistakeChance` (engel başına: fren noktasını kaçırma), `HeroChance`, `ReactionTime`, `Wander`; yarış başına `Form`. Motor/hız/lastik gücü `VehicleUpgrades` ile verilir. Bot kararları diğer araçların konumuna göre hızlanıp yavaşlamaz (lastik bant yok).
- **`OvertakePlanner`** (saf mantık, EditMode test edilir): Faz 2b'deki sollama kuralı aynen — girdiler artık `RacerEntry.Distance/Lateral/hız`: önde yavaş araç ve yanında yer varsa hedef çizgi yana kayar; yer yoksa arkada kalınır; hedef en az 0,5 sn değişmez; hedef hiçbir zaman yol dışına düşmez. Oyuncu ve botlar için aynı. `RaceManager` her araç için `TargetLateral = ev çizgisi (+ bot gezinmesi) → OvertakePlanner`.

- [ ] **Step 1: Başarısız testleri yaz**
```csharp
// OvertakePlannerTests (EditMode) — Faz 2b OvertakeTests'in karşılıkları
[Test] public void NoBlocker_TargetsHomeLine()
[Test] public void SlowCarAhead_PicksFreeSide_NearerHome()
[Test] public void BothSidesOccupied_StaysBehind()
[Test] public void NarrowRoad_NoRoom_StaysBehind()
[Test] public void NeverTargetsOffRoad()
[Test] public void TargetIsStable_NoFlipFlop()
[Test] public void EqualSpeedCarAhead_NoOvertake()
// BotDriverTests (PlayMode, hızlandırılmış) — pist: Recipe_Graybox_Race eşdeğeri, kodla ya da varlıktan
[UnityTest] public IEnumerator Master_Alone_FinishesOnAtLeast4Of5Seeds()
[UnityTest] public IEnumerator Master_BrakesBeforeTightCorner()                     // R 25'e girmeden önce fren kullanmış ve yolda kalmış
[UnityTest] public IEnumerator Reckless_GoesOutOnSomeSeeds()                        // 8 tohumda 1..7 kez oyun dışı
[UnityTest] public IEnumerator FinishTimes_VaryAcrossSeeds()                        // Master, 5 tohum: en hızlı ile en yavaş arası ≥ 0.3 sn
[UnityTest] public IEnumerator Slow_IsSlowerThanMaster_OnAverage()
[UnityTest] public IEnumerator FiveCarRace_Completes_Within120s()                   // herkes bitirir ya da oyun dışı
[UnityTest] public IEnumerator FiveCarRace_WinnerVaries()                           // 6 yarışta en az 2 farklı kazanan
[UnityTest] public IEnumerator FasterCar_OvertakesSlowerCar_WithoutCrashingIt()     // düzlükte: arkadaki hızlı araç yana kayıp geçer, ikisi de yolda
```
İstatistik tutmuyorsa yalnızca profil değerleri ve `VehicleConfig` ayarlanır; değişiklikler ledger'a.
- [ ] **Step 2–4:** çalıştır (başarısız), uygula, yeşil
- [ ] **Step 5: Commit** — `feat: fizik araç süren botlar ve sollama`

---

### Task 5: Temizlik, görünüm, kamera, HUD ve doğrulama

**Files:** Delete eski simülasyon dinamiği ve testleri; Modify/Create `Racing/RacerView.cs` (→ `VehicleVisual.cs`), `Racing/ChaseCameraRig.cs`, `Racing/RaceHud.cs`, `Racing/MiniMapView.cs`, `Racing/RaceResultPanel.cs`, `Editor/GrayboxSceneBuilder.cs`

- **Silinir:** `Simulation/CarSim.cs`, `RaceSim.cs`, `BotBrain.cs`, `SimClock.cs`, `CrashPolicy.cs`, `CarPhase.cs`, `SimEvent.cs`, `SimTuning.cs` içindeki dinamik alanları ve bunların EditMode testleri (`CarSim*Tests`, `BoostTests`, `LateralPhysicsTests`, `RaceSim*Tests`, `OvertakeTests`, `BotBrainTests`, `SimClockTests`). **Kalır:** `TrackPieceDef`/`TrackLayout` (geometri ve şerit verisi olarak; `SafeSpeed`/`MinJumpSpeed` alanları ve doğrulamaları kaldırılır), `LaneMap`, `SimRandom`, `BotProfile`, `PieceKind`, ve testleri. Silinen her dosya ledger'da listelenir.
- **Perfect Brake Boost tamamen kaldırılır** (kod, olaylar, HUD metinleri, sonuç panelindeki Perfect/Good satırı).
- Çalışma ağacında Faz 2b Görev 5'ten kalan commit'lenmemiş değişiklikler var (`ChaseCameraRig.cs`, `RaceDebugProbe.cs`, `RacerView.cs`, `RaceHud.cs`, `RaceController.cs`, fabrikalar). Bunlar eski simülasyona göre yazıldı; kamera ve banket fikirleri için başlangıç noktası olarak kullanılabilir, uymayan kısımlar atılır.
- **Görsel:** araç gövdesi artık fizik gövdesinin kendisidir (ayrı bir "görünüm konumlandırma" yok). Tekerlekler döner ve süspansiyonla oynar. Kayarken lastiklerin altında basit duman/iz parçacığı (gri kutu düzeyinde), sert frende arka stop ışığı kırmızılaşır.
- **Kamera (`ChaseCameraRig`, spec §11.6):** fizik gövdesini izler; hız arttıkça uzaklaşır ve FOV genişler, yavaşlayınca yaklaşır ve alçalır; frende hafif öne gelir; kayarken (`SlipAngleDeg`) kamera viraj dışına hafifçe kayar ve en çok 4° yatar; havadayken hafif yukarı kalkar; temas ve sert inişte kısa sarsıntı; oyuncu oyun dışı kalınca takibi bırakıp durur, araca bakar ve yakınlaşır; bitişte çapraza döner. Hız hissi için kamera Faz 2'den daha alçak ve yakın başlar (ilk değer: hız 0'da ofset `(0, 3.2, −7)`, en yüksek hızda `(0, 4.2, −10)`, FOV 62 → 78); 5 aracın kadraja sığması şart değildir.
- **HUD:** hız `km/s` (`142 km/s`); sıra; geri sayım; geri bildirim metinleri: kayarken `KAYIYORSUN`, havada `HAVADA`, temas `TEMAS`, sollarken `SOLLAMA`; fren şeridi. Mini harita ve sonuç paneli `RaceManager`'a bağlanır; sonuç panelinde durum: `Bitti` / `Yol disi` / `Takla` / `Dustu` / `Yarista`.
- **Sahne:** `Race_Graybox` — `Recipe_Graybox_Race`, 5 fizik araç (oyuncu kırmızı; botlar mavi, sarı, yeşil, mor; Master, Balanced, Reckless, Slow), `playerPolicy = Eliminate`, `botPolicy = Eliminate`. Tarifteki parça dizisi fizik araçla baştan sona sürülebilir olmalı; değilse geometri/parça dizisi düzeltilir ve ledger'a yazılır.

- [ ] **Step 1: Temizle, yeniden bağla, sahneyi üret, derle; tüm EditMode ve PlayMode testleri yeşil**
- [ ] **Step 2: Editor'de oynat (1080×1920). Her ekran görüntüsüne BAK ve gösterdiğini doğrula:**
  - `docs/screens/faz2c-start.png`: geri sayım, 5 araç gridde.
  - `docs/screens/faz2c-speed.png`: düzlükte en yüksek hıza yakın; hız göstergesi km/s.
  - `docs/screens/faz2c-brake.png`: sert fren anı; burun dalmış.
  - `docs/screens/faz2c-drift.png`: virajda tam gaz; araç yan kaymış (burun viraj içine, gövde dışa).
  - `docs/screens/faz2c-recover.png`: frenle kurtarılmış viraj; araç yolda.
  - `docs/screens/faz2c-offroad.png`: araç yolun dışında, yarış bitmiş (`Yol disi`).
  - `docs/screens/faz2c-jump.png`: rampada havada.
  - `docs/screens/faz2c-contact.png` ya da `faz2c-overtake.png`: iki araç yan yana/temas.
  - `docs/screens/faz2c-result.png`: sonuç paneli.
- [ ] **Step 3: Telemetri raporu** — betikle bir tur boyunca kaydet ve ledger'a yaz: 0→100 süresi, en yüksek hız, tam fren süresi ve mesafesi; her virajda tam gazla girişin sonucu (yolda/dışarı, en büyük yanal sapma, en büyük kayma açısı) ve fren kullanılarak girişin sonucu; rampa uçuş mesafesi ve iniş sonrası durum; kamera değerleri (ofset, FOV, yatma) yavaşta, hızlıda ve kayarken; arka arkaya iki yarışın sıralaması ve süreleri.
- [ ] **Step 4: Kod taraması** — `Assets/_Game/Scripts` içinde oyun kuralı olarak hız karşılaştırması kalmadığını doğrula (`SafeSpeed`, `MinJumpSpeed`, `CornerLimit`, `Perfect`, `Boost` araması boş; kalan hız karşılaştırmaları yalnızca bot muhakemesi, sunum ve motor torku eğrisinde) ve sonucu ledger'a yaz.
- [ ] **Step 5: Android APK derle; ledger'ı tamamla; commit** — `feat: gerçek fizikle yarış, kamera ve HUD`

**Kapı:** araç gerçek fizik gövdesi; virajda kayma, rampada uçma ve takla kuvvetlerden doğuyor; hiçbir oyun kuralı hız karşılaştırmıyor; yol dışına çıkan ya da ters dönen araç oyun dışı kalıyor.
