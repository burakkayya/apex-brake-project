# Faz 2b: Yanal Fizik ve Canlı Yarış — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Savrulmayı sabit eşiklerden merkezkaç kuvvetine dayalı yanal fiziğe çevirmek; teması gerçek konuma dayandırmak; otomatik sollama eklemek; botları insan gibi davranır hale getirmek.

**Architecture:** `CarSim` bir boyutlu (mesafe, hız) olmaktan çıkıp yanal konum ve yanal hız taşır. Lastik, çizgiye dönmek için gereken yanal ivmeyi `Tutunma` sınırına kadar sağlar; sınır aşılınca araç dışa kayar. `RaceSim` her aracın hedef çizgisini (ev şeridi + sollama) belirler ve teması gerçek konumdan çözer. Her şey saf C#, sabit adımlı ve deterministik kalır.

**Tech Stack:** Unity 6000.6.9f1, C#, Unity Test Framework, `unity` CLI.

**Spec:** `docs/superpowers/specs/2026-11-15-apex-brake-design.md` — **§11 (Revizyon)** bağlayıcıdır ve §3.2/3.3/3.5/3.7'nin ilgili kısımlarını geçersiz kılar.

## Global Constraints

- `docs/superpowers/plans/2026-11-16-faz-2-yaris.md` içindeki Global Constraints aynen geçerli (proje kökü, test komutları, Editor açık, YAML elle düzenlenmez, ASCII arayüz metni, önce test sonra kod, görev başına commit, push yok).
- Ledger: `docs/superpowers/plans/2026-11-16-faz-2b-progress.md`.
- İşaret kuralı: yanal konum `Lateral` pist merkezinden metre, **+ = sağ**. `TurnSign +1` = sağa viraj; merkezkaç ivme `−TurnSign × v²/r` (sağa virajda sola iter).
- Bu planla **silinen** kurallar: viraj girişinde `CleanPass/Skid/Crash` kademesi, `SkidBand`, `SkidSpeedLoss`, `SkidDuration`, viraj içi hız tavanı, şerit eşitliğine dayalı temas, `RearHitSpeedFactor` dışındaki yan temas kuralı. İlgili eski testler silinir ya da yeni kurala göre yeniden yazılır; her silinen test ledger'da adıyla listelenir.
- Yeni/ değişen ayarlar. `CarStats`: `Grip = 12f`. `SimTuning` (ilk değerler; **Ayar aralığı** olan satırlar testleri tutturmak için yalnızca o aralıkta değiştirilebilir, seçilen değer ledger'a yazılır):

| Alan | İlk değer | Ayar aralığı | Anlam |
|---|---|---|---|
| `LineStiffness` | 16 | 9–25 | çizgiye dönüş yayı (1/s²) |
| `LineDamping` | 5.6 | 4–9 | sönüm (1/s) |
| `BrakeGripFactor` | 0.8 | — | fren basılıyken yanal tutunma çarpanı |
| `SlideScrub` | 1.5 | 0.8–3 | kayarken hız kaybı = bu × \|yanal hız\| (m/s²) |
| `ShoulderWidth` | 1.5 | — | banket (m) |
| `CarWidth` | 2.0 | — | m |
| `SkidStartSlip` | 0.5 | — | m/s; kayma olayı eşiği |
| `BumpLightGrip` | 0.3 | — | tümsekte hafifleme çarpanı |
| `BumpLightDuration` | 0.6 | — | sn |
| `BumpKick` | 1.5 | — | m/s yanal itme |
| `BumpCrashBand` | 1.15 | — | bu oranın üstü çakılma |
| `SideHitKick` | 1.5 | — | m/s; yan temasta karşılıklı itme |
| `SideHitSpeedLoss` | 0.05 | — | yan temasta hız kaybı |
| `ContactProtect` | 0.5 | — | sn; çarpılan araç bu süre yoldan çıkamaz |
| `OvertakeLook` | 18 | — | m; öndeki araca bakış |
| `OvertakeGap` | 0.8 | — | m; geçerken yanal boşluk |
| `OvertakeMinSpeedDiff` | 1.0 | — | m/s |
| `OvertakeHold` | 0.5 | — | sn; hedef değiştirmeden önce en kısa süre |

## Review Focus

1. **Yüksek hızda tek Tick'te yol kenarını atlama / NaN:** yanal durum hiçbir girdide NaN ya da sonsuz olmamalı; `Speed` 0 iken yanal fizik sakin kalmalı. → Görev 1.
2. **Dış şeritteki araç:** banket payı az olan şeritte de güvenli hızda giren araç yolda kalmalı. → Görev 1.
3. **İki aracın aynı noktada başlaması / üst üste binmesi:** temas araçları sonsuz hızla itmemeli, kimseyi geri ışınlamamalı. → Görev 2.
4. **Sollama kararsızlığı:** araç iki hedef arasında her Tick gidip gelmemeli; yer yokken yolun dışına hedef seçmemeli. → Görev 3.
5. **Botlar farklı tohumlarda:** usta bot neredeyse her zaman bitirmeli, sonuçlar tohumdan tohuma değişmeli, aynı tohum aynı yarışı vermeli. → Görev 4.

---

### Task 1: Yanal fizik (`CarSim`)

**Files:** Modify `Simulation/CarSim.cs`, `CarStats.cs`, `SimTuning.cs`, `TrackPieceDef.cs`, `TrackLayout.cs`, `SimEvent.cs`; Tests: yeni `Tests/EditMode/LateralPhysicsTests.cs`; `CarSimObstacleTests.cs`, `CarSimCrashPolicyTests.cs`, `BoostTests.cs` güncellenir.

**Interfaces — Produces:**
```csharp
// TrackPieceDef
public float Radius;                       // Corner: > 0 zorunlu. Diğerleri 0.
public float HalfWidth => LaneCount * 3.2f / 2f;          // yol yarı genişliği
// Corner için SafeSpeed artık girilmez; nominal değer: SafeSpeedFromRadius(Radius) (HUD için). TrackLayout: Corner'da Radius > 0 ve TurnSign ≠ 0 zorunlu.

// CarSim
public float Lateral { get; }              // m, + sağ
public float LateralVel { get; }           // m/s
public float TargetLateral { get; set; }   // izlenecek çizgi (varsayılan 0); RaceSim her Tick yazar
public float SlipAngle { get; }            // radyan = atan2(LateralVel, max(Speed, 1)); görsel için
public float LateralLoad { get; }          // 0..1 = |lastik yanal ivmesi| / kullanılabilir tutunma
public bool IsSliding { get; }             // lastik doymuş ve |LateralVel| ≥ SkidStartSlip
public float CornerLimit(TrackPieceDef corner);   // sqrt(Stats.Grip × Radius) — bu aracın güvenli viraj hızı
public void AddLateralVel(float delta);    // temas itmesi
public void ProtectFromEdge(float seconds);// bu süre boyunca yol kenarında tutulur (yoldan çıkamaz)
// SimEventKind'e eklenir: Light (tümsekte hafifleme), Recovered (kayma bitti). CleanPass yalnızca tümsek/rampa için kalır.
```
**Tick fiziği** (`Driving`/`Skidding` fazlarında; `Airborne`, `Crashed`, `Finished` iken yanal durum değişmez):
1. Parça virajsa `merkezkaç = −TurnSign × Speed² / Radius`, değilse 0.
2. `tutunma = Grip × (fren ? BrakeGripFactor : 1) × (hafifleme sürüyorsa BumpLightGrip : 1)`.
3. `istenen = −merkezkaç + LineStiffness × (TargetLateral − Lateral) − LineDamping × LateralVel`.
4. `lastik = clamp(istenen, −tutunma, +tutunma)`; `LateralLoad = |lastik| / tutunma`; doymuş = `|istenen| > tutunma`.
5. `LateralVel += (merkezkaç + lastik) × Step`; `Lateral += LateralVel × Step`.
6. Boyuna: fren basılıysa eskisi gibi yavaşlar. Değilse ivme = `Acceleration × (boost çarpanı) × sqrt(max(0, 1 − LateralLoad²))`.
7. Doymuşsa `Speed −= SlideScrub × |LateralVel| × Step` (0'ın altına inmez).
8. Kayma olayı: `IsSliding` yeni başladıysa `Skid` olayı ve faz `Skidding`; `|Lateral − TargetLateral| < 0.3` ve `|LateralVel| < SkidStartSlip` ve doymamışsa `Recovered` olayı ve faz `Driving`. `Skid` kazanılmış/etkin boost'u düşürür (Faz 2 kuralı).
9. Yoldan çıkma: `|Lateral| > HalfWidth + ShoulderWidth` ise — koruma sürüyorsa `Lateral` sınıra kenetlenir ve dışa doğru `LateralVel` sıfırlanır; sürmüyorsa `Crash` (politikaya göre eleme/yeniden doğma). Parça sınırında yol daralıyorsa sınır, aracın bulunduğu parçanınkidir.
10. `Speed < 1` iken adım 1'deki merkezkaç 0 kabul edilir; hiçbir değer NaN/sonsuz olamaz.

**Viraj girişi:** artık çakılma/savrulma kararı verilmez. Perfect/Good değerlendirmesi aynı noktada sürer; oran = `giriş hızı / CornerLimit(parça)`; oran > 1 ise boost kazanılmaz.
**Tümsek:** oran = `hız / SafeSpeed`. `≤ 1` → `CleanPass`. `1 < oran ≤ BumpCrashBand` → `Light` olayı, `BumpLightDuration` boyunca hafifleme ve `BumpKick` yanal itme (yönü `Lateral`'ın işareti; 0 ise +). `> BumpCrashBand` → `Crash`.
**Rampa:** değişmez. Havada yanal durum donar.
**Yeniden doğma:** `Lateral = TargetLateral`, `LateralVel = 0`. Yoldan çıkarak çakılan aracın yeniden doğma noktası viraj kuralıyla hesaplanır (`Start − CornerRespawnRunUp`).

- [ ] **Step 1: Başarısız testleri yaz** (yardımcı `Corner(length, radius, turn)`; pist V: `Straight 200, Corner 62.8 (R 40, +1), Straight 300`; araç `SetStateForTest` ile girişten 1 m önce istenen hıza konur; `SetStateForTest`'e isteğe bağlı `lateral` ve `target` eklenir)
```csharp
[Test] public void Straight_HoldsLine_NoLateralMotion()                   // 500 Tick: |Lateral| < 0.001
[Test] public void Straight_OffsetCar_ReturnsToTarget_Within2s()          // Lateral 3, target 0, hız 20 → 100 Tick içinde |Lateral| < 0.3, taşma ≤ 1.0 m
[Test] public void Corner_AtLimit_StaysWithinHalfMetre()                  // giriş 21.0 (limit 21.9), viraj boyunca fren yok: max |Lateral| < 0.5, Skid olayı yok
[Test] public void Corner_At110Percent_SlidesOutward_ThenRecovers()       // giriş 24.1: max dışa kayma 0.8–6 m (sağa virajda Lateral negatif), Skid olayı var, yolda kalır, viraj çıkışından sonra 3 sn içinde Recovered
[Test] public void Corner_At140Percent_NoBrake_LeavesRoad()               // giriş 30.7: Crash, o anda |Lateral| > 9.5
[Test] public void Corner_At140Percent_BrakingHard_SlidesLessThanNoBrake()// aynı giriş, girişten itibaren fren: yoldan çıkma anı daha geç YA DA yolda kalır; her durumda 1 sn sonundaki |Lateral| frensizden küçük DEĞİL şartı aranmaz — bunun yerine 1.5 sn sonundaki Speed frensizden en az 10 m/s düşük
[Test] public void Braking_ReducesGrip_SlidesMoreAtSameSpeed()            // sabit 22.5 ile giriş: 10 Tick fren vs frensiz → frenli koşuda 10. Tick'teki |LateralVel| daha büyük
[Test] public void LeftCorner_SlidesRight()                               // TurnSign −1, %110 → Lateral pozitif
[Test] public void LoadedTyre_AcceleratesSlower()                         // viraj içinde limite yakın giden araç, aynı hızda düzlükteki araçtan 20 Tick'te daha az hız kazanır
[Test] public void NoSpeedCapInsideCorner()                               // giriş 15: viraj içinde Speed 15'in üstüne çıkar
[Test] public void Sliding_ScrubsSpeed()                                  // %120 giriş frensiz: 50 Tick sonra Speed < giriş hızı
[Test] public void SlipAngle_SignMatchesLateralVel()
[Test] public void OuterLaneTarget_AtLimit_StaysOnRoad()                  // sağa viraj, TargetLateral −6.4 (dış şerit), giriş 21.0 → Crash yok
[Test] public void EdgeProtection_ClampsInsteadOfCrash()                  // ProtectFromEdge(0.5) sonrası AddLateralVel(−40) → Crash yok, |Lateral| ≤ 9.5; koruma bitince aynı itme → Crash
[Test] public void ZeroSpeed_NoNaN()                                      // viraj içinde Speed 0, 200 Tick fren: Lateral ve LateralVel sonlu
[Test] public void HugeLateralKick_NoNaN_AndCrashesOnce()                 // AddLateralVel(1e6) → tek Crash olayı, değerler sonlu
[Test] public void Bump_Between100And115_GoesLight_NotCrash()             // Light olayı, 30 Tick boyunca tutunma azalmış (yanal itme sonrası dönüş yavaş)
[Test] public void Bump_Above115_Crashes()
[Test] public void Airborne_FreezesLateral()
[Test] public void Respawn_ResetsLateralToTarget()
[Test] public void SameInputs_IdenticalLateralState()                     // iki sim, aynı 1500 girdilik dizi → Lateral ve LateralVel bit düzeyinde eşit
[Test] public void CornerLimit_ScalesWithGrip()                           // Grip 12 → 21.909; Grip 13.5 → 23.238 (± 0.01)
[Test] public void Perfect_UsesCarsOwnCornerLimit()                       // Grip 13.5 ile giriş 22.5 (oran 0.968) → PerfectBrake; Grip 12 ile aynı giriş (oran 1.027) → yok
```
- [ ] **Step 2: Çalıştır, başarısız olduklarını gör**
- [ ] **Step 3: Uygula; eski viraj-kademe testlerini sil ya da yeniden yaz** (`Corner_AtSafeSpeed_CleanPass`, `Corner_At110Percent_SkidsAndLoses40`, `Corner_JustBelow115Percent_StillSkids`, `Corner_Above115Percent_Crashes`, `Skid_ReturnsToDrivingAfter0_6s`, `Corner_CapsSpeedAtSafeWhileInside`, `Corner_ExitReleasesCap`, `Bump_UsesSameTwoTiers_NoCap`, `LandingDirectlyIntoCorner_AppliesCornerRule` ve bunlara dayanan boost/çakılma testleri). Viraj kullanan her mevcut test `Radius` ve `TurnSign` verecek şekilde güncellenir.
- [ ] **Step 4: Tüm EditMode yeşil** (RaceSim/Bot testleri bu görevde derlenmeli; davranışı değişenler Görev 2–4'e kadar `[Ignore("Faz 2b Görev N")]` ile işaretlenebilir, her biri ledger'a yazılır ve ilgili görevde açılır)
- [ ] **Step 5: Commit** — `feat(sim): merkezkaç kuvvetine dayalı yanal fizik`

---

### Task 2: Gerçek konuma dayalı temas

**Files:** Modify `Simulation/RaceSim.cs`; Test: `Tests/EditMode/RaceSimContactTests.cs` (yeniden yazılır)

**Kurallar** (temasa giren araçlar Faz 2'deki gibi: `IsOut` değil, fazı `Driving`/`Skidding`):
- Çift (A, B) **üst üste**: `|ΔDistance| < CarLength` ve `|ΔLateral| < CarWidth`.
- **Arkadan:** üst üste ve `|ΔDistance| ≥ 0.6 × CarLength` → arkadaki için her Tick `LimitSpeed(öndeki.Speed × RearHitSpeedFactor)`; temas yeni başladıysa arkadakine `RearHit`, öndekine `Bumped` + `AddSpeed(RearBump)`.
- **Yandan:** üst üste ve `|ΔDistance| < 0.6 × CarLength` → temas yeni başladıysa: ikisi birbirinden uzağa `AddLateralVel(±SideHitKick)`, ikisine `ScaleSpeed(1 − SideHitSpeedLoss)`, `SideHit` olayı ve `ProtectFromEdge(ContactProtect)`. Temas sürdükçe her Tick: birbirine yaklaşan bağıl yanal hız sıfırlanır (ikisinin yanal hızı ortalamaya çekilir) — araçlar iç içe geçmez ama konum asla ışınlanmaz.
- `ΔLateral` tam 0 ise düşük indeksli araç sola (−), diğeri sağa itilir.
- `Racer.Lateral` artık `Car.Lateral`'dır. `RaceSim` her Tick'ten önce her aracın `TargetLateral`'ını yazar (bu görevde: `LaneMap.LateralAt(ev şeridi)`; sollama Görev 3'te eklenir). Başlangıçta `Car.Lateral = TargetLateral`.

- [ ] **Step 1: Başarısız testleri yaz**
```csharp
[Test] public void FarApartLaterally_NoContact()
[Test] public void RearContact_LimitsFollower_AndBumpsLeader_Once()
[Test] public void SideContact_PushesCarsApart_AndEmitsSideHitOnce()      // aynı mesafe, Lateral 0 ve 1.5 → biri −, diğeri + yanal hız alır
[Test] public void SideContact_BothLoseFivePercent()
[Test] public void SideContact_GrantsEdgeProtection()                     // kenardaki araç itilir → Crash yok
[Test] public void SlidingCar_HitsNeighbour()                             // Pist V, iç şeritteki araç %115 girer, dışındaki araç limitte → dıştakine SideHit
[Test] public void IdenticalPosition_SeparatesDeterministically_NoTeleport() // aynı mesafe ve Lateral: 100 Tick boyunca hiçbir Tick'te |ΔLateral değişimi| > 0.2 m, sonunda |ΔLateral| ≥ CarWidth
[Test] public void CrashedOrAirborneCar_IsIgnored()
[Test] public void Contact_NeverProducesNaN()
[Test] public void PlayerAndBot_ContactIsSymmetric()                      // indeksleri yer değiştirince sonuç ayna
```
- [ ] **Step 2–4:** çalıştır (başarısız), uygula, tüm EditMode yeşil
- [ ] **Step 5: Commit** — `feat(sim): gerçek konuma dayalı temas`

---

### Task 3: Otomatik sollama

**Files:** Modify `Simulation/RaceSim.cs`; Test: yeni `Tests/EditMode/OvertakeTests.cs`

**Kural** (her Tick, her araç için; oyuncu ve bot aynı):
- `ev = LaneMap.LateralAt(ev şeridi, Distance)`.
- **Engel:** önde `0 < ΔDistance ≤ OvertakeLook` olan, `|engel.Lateral − ev| < CarWidth + 0.4` ve `engel.Speed < Speed − OvertakeMinSpeedDiff` olan en yakın araç.
- Engel yoksa hedef `ev`. Varsa iki aday: `engel.Lateral ± (CarWidth + OvertakeGap)`. Aday geçerli: `|aday| ≤ HalfWidth − 1.2` (aracın bulunduğu ve 20 m ilerideki parçanın küçük olanı) ve `|ΔDistance| < 10` içindeki başka hiçbir araç `|Lateral − aday| < CarWidth` değil. Geçerli adaylardan `ev`'e yakın olan seçilir; ikisi de geçersizse hedef `ev` (sıraya girilir).
- **Kararlılık:** seçilen hedef en az `OvertakeHold` süre değişmez (engel kaybolsa bile). Araç engeli `CarLength + 2` m geçince hedef `ev`'e döner.
- `Racer`'a `public bool IsOvertaking { get; }` eklenir.

- [ ] **Step 1: Başarısız testleri yaz** (pist: `Straight 3000`)
```csharp
[Test] public void NoBlocker_TargetsHomeLine()
[Test] public void SlowCarAhead_FasterCarMovesAside_AndPasses()           // B d=40 v=15 (TopSpeed 15), A d=0 v=30, aynı şerit hattı → A hiç RearHit almadan geçer, sonra ev çizgisine döner (|Lateral − ev| < 0.5)
[Test] public void PicksSideNearerHome()
[Test] public void BothSidesOccupied_QueuesBehind()                       // üç yavaş araç yan yana → A geçemez, RearHit alır
[Test] public void NarrowRoad_NoRoom_QueuesBehind()                       // 2 şeritli parça, iki yavaş araç
[Test] public void NeverTargetsOffRoad()                                  // en dış şeritte: hiçbir Tick'te |TargetLateral| > HalfWidth − 1.2
[Test] public void TargetIsStable_NoFlipFlop()                            // 0.5 sn içinde hedef en çok 1 kez değişir
[Test] public void EqualSpeedCarAhead_NoOvertake()
[Test] public void PlayerUsesSameRule()                                   // indeks 0 (Bot == null) de geçer
```
- [ ] **Step 2–4:** çalıştır (başarısız), uygula, tüm EditMode yeşil
- [ ] **Step 5: Commit** — `feat(sim): otomatik sollama`

---

### Task 4: İnsan gibi botlar

**Files:** Modify `Simulation/BotProfile.cs`, `BotBrain.cs`; Tests: `BotBrainTests.cs` ve `RaceSimRaceTests.cs` güncellenir

**Interfaces — Produces:**
```csharp
// BotProfile'a eklenir
public float Grip = 12f;             // ToStats() kopyalar
public float RiskJitter = 0.04f;     // viraj başına risk oynaması (±)
public float MistakeChance;          // engel başına büyük hata olasılığı
public float HeroChance;             // engel başına kusursuz geçiş olasılığı
public float ReactionTime = 0.10f;   // sn; karar bu kadar eski durumla verilir
public float Wander = 0.35f;         // m; çizgide gezinme genliği
// Master: Mistake 0.02, Hero 0.25, Reaction 0.06 · Balanced: 0.06, 0.10, 0.10 · Reckless: 0.15, 0.10, 0.12 · Slow: 0.05, 0.05, 0.16

// BotBrain
public bool ShouldBrake(CarSim car);         // imza aynı
public float LineOffset(CarSim car);         // gezinme; RaceSim ev çizgisine ekler (yalnızca botlar)
public float Form { get; }                   // −1..+1, yarış başına tohumdan
```
**Davranış:**
- **Form:** yarış başına bir kez örneklenir. `Risk += 0.03 × Form`; `BrakeError ×= 1 − 0.3 × Form`.
- **Hedef hız:** viraj için `CornerLimit × risk` (botun kendi tutunması); tümsek/rampa Faz 2'deki gibi. `risk` her engel için `Risk ± RiskJitter` aralığında örneklenir. Hedef çizgisi virajın dış tarafındaysa (`−TurnSign × TargetLateral > 3`) risk 0.03 düşer.
- **Engel başına zar:** `MistakeChance` ile büyük hata → fren noktası 4–9 m geç; `HeroChance` ile kusursuz → sapma 0, risk 0.99; yoksa normal sapma.
- **Tepki süresi:** karar `ReactionTime` kadar önceki mesafe ve hızla verilir (küçük bir halka tampon).
- **Kayma tepkisi:** `IsSliding` ve `|Lateral − TargetLateral| > 1` iken fren (panik freni); kayma bitince bırakır.
- **Start:** ilk `0–0.3 sn` (tohumdan) fren basılı tutar — geç çıkış.
- **Gezinme:** `LineOffset` = genliği `Wander` olan, periyodu 4–9 sn (tohumdan) yavaş sinüs; daralmada genlik `Wander × 0.3`.
- Lastik bant yok: bot davranışı oyuncunun konumuna bakmaz.

- [ ] **Step 1: Başarısız testleri yaz / güncelle** (Pist R, virajlara `Radius` ve `TurnSign` verilmiş; `RunBot` Faz 2'deki gibi, `RaceSim` içinde tek araç)
```csharp
[Test] public void Master_FinishesOnAtLeast18Of20Seeds()
[Test] public void Master_FinishTimesVary()                               // 20 tohum: (en yavaş − en hızlı) ≥ 0.5 sn
[Test] public void Reckless_EliminatedOnSomeSeedsButNotAll()              // 40 tohumda 6..34
[Test] public void Balanced_FinishesOnAtLeast14Of20Seeds()
[Test] public void Slow_IsSlowerThanMaster_OnAverage()                    // 10 tohum ortalaması
[Test] public void SameSeed_SameResult()
[Test] public void DifferentSeeds_DifferentForm()                         // 20 tohumda Form en az 10 farklı değer, hepsi [−1, 1]
[Test] public void ReactionTime_DelaysBraking()                           // Reaction 0.3 vs 0: aynı tohumda ilk fren Tick'i daha geç
[Test] public void SlidingBot_Brakes()                                    // bot viraja %120 ile konur → kayarken ShouldBrake true
[Test] public void LateStart_HoldsBrakeBriefly()                          // bazı tohumlarda ilk 15 Tick içinde fren var; hiçbir tohumda 16. Tick'ten sonra start freni yok
[Test] public void Wander_StaysWithinAmplitude()
[Test] public void BotDecision_IgnoresOtherCars()                         // aynı tohum, yanında başka araç olsa da olmasa da ShouldBrake dizisi aynı (araç temas etmeyecek kadar uzakta)
// RaceSimRaceTests
[Test] public void FiveBots_RaceCompletes_AllDoneWithin10000Ticks()       // tohum 1..10
[Test] public void FiveBots_WinnerVariesAcrossSeeds()                     // 20 tohumda en az 2 farklı kazanan
[Test] public void FiveBots_SomeOvertakesHappen()                         // 10 tohumda toplam IsOvertaking süresi > 0
[Test] public void SameSeeds_IdenticalStandingsAndTicks()
```
İstatistik testleri tutmuyorsa yalnızca profil değerleri (`BrakeError`, `Risk`, `MistakeChance`, `HeroChance`) ayarlanır; her değişiklik ledger'a yazılır. Görev 1'de `[Ignore]` ile kapatılan testler burada açılmış olmalı.
- [ ] **Step 2–4:** çalıştır (başarısız), uygula, tüm EditMode yeşil; hiçbir `[Ignore]` kalmaz
- [ ] **Step 5: Commit** — `feat(sim): insan gibi davranan botlar`

---

### Task 5: Görünüm, sahne ve doğrulama (Unity)

**Files:** Modify `Track/TrackPieceAsset.cs`, `Editor/GrayboxPieceFactory.cs`, `Editor/GrayboxSceneBuilder.cs`, `Racing/RacerView.cs`, `Racing/RaceHud.cs`, `Racing/RaceController.cs`

- `TrackPieceAsset.ToDef` `Radius`'ü kopyalar (viraj varlıklarında zaten `radius` var).
- Gri kutu yol mesh'lerine iki yanda 1,5 m banket eklenir (yoldan farklı düz renk, kırmızı-beyaz şerit gerekmez).
- `RacerView`:
  - Yanal konum `Car.Lateral`'dan gelir (önceki/şimdiki Tick arası yumuşatma).
  - Gövde yönü = yol yönü + `SlipAngle × 1.6` (derece cinsinden, ±35° ile sınırlı) — gerçek drift açısı. Faz 2'deki sahte savrulma yalpası **silinir**.
  - Gövde, yanal ivmeye göre viraj dışına yatar (en çok 6°) ve fren/gazda öne-arkaya hafif eğilir (en çok 3°).
  - Yoldan çıkan araç: çıkış anındaki yanal hız yönünde savrulup takla atar (Faz 2'deki fırlama, yön artık `LateralVel` işaretinden).
  - `Light` olayında araç 0,6 sn hafifçe zıplar.
- `RaceHud`: `Skid` → `KAYIYORSUN`, `Recovered` → metin silinir, `Light` → `HAVALANDIN`; sollarken küçük `SOLLAMA` göstergesi.
- `RaceController`: oyuncuya her yarışta varsayılan `CarStats` (Grip 12). Botlar Görev 4 profilleri.
- Mini harita en sol aracın üstüne binmesin: çubuk ekranın sol kenarına 24 px boşlukla yaslanır ve yarı saydam olur.

- [ ] **Step 1: Uygula, parçaları ve sahneyi yeniden üret, derle**
- [ ] **Step 2: Tüm EditMode ve PlayMode testleri yeşil**
- [ ] **Step 3: Editor'de oynat (1080×1920) ve ekran görüntüsü al — her birine BAK ve planın dediğini gösterdiğini doğrula:**
  - `docs/screens/faz2b-slide.png`: oyuncu ilk viraja yaklaşık %110 hızla sokulmuş (betikle fren zamanlaması ya da `SetStateForTest` benzeri bir test kancası); araç dışa kaymış ve gövdesi drift açısında, yolda.
  - `docs/screens/faz2b-recover.png`: aynı koşuda 1–2 sn sonra araç çizgisine dönmüş.
  - `docs/screens/faz2b-offroad.png`: frensiz giriş; araç banketin dışında takla atıyor, `ELENDIN` ya da çakılma anı.
  - `docs/screens/faz2b-overtake.png`: oyuncu ya da bir bot, yavaş bir aracın yanından geçerken.
  - Aynı tohumsuz iki yarış arka arkaya: sıralama ya da bitiş süreleri farklı (değerleri raporla).
  - Konsolda proje kodundan hata yok.
- [ ] **Step 4: Android APK derle** (Faz 2'deki yöntem; 40 dakikayı aşarsa raporla)
- [ ] **Step 5: Ledger'ı tamamla; commit** — `feat: yanal fizik görünümü, banket ve doğrulama`

**Kapı:** araç virajda merkezkaçla kayıyor, toparlıyor ya da yoldan çıkıp eleniyor; araçlar birbirini sollayıp çarpışıyor; iki yarış aynı geçmiyor.
