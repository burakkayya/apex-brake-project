# Faz 0–1: Kurulum ve Gri Kutu Çekirdek — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Depoya bağlı, Android'e derlenen bir Unity projesi kurmak ve tek araçla oynanan gri kutu bir test pistinde fren mekaniğini (iki kademeli hata, rampa, tümsek) telefonda oynanır hale getirmek.

**Architecture:** Oyun kuralları `ApexBrake.Simulation` içinde saf C# olarak yaşar (Unity sahne sınıfı yok, 50 Hz sabit adım) ve EditMode testleriyle doğrulanır. `ApexBrake.Track` modüler parça prefab'larını tarife göre uç uca dizer ve simülasyona bir `TrackLayout`, görsele bir `TrackPath` verir. `ApexBrake.Racing` dokunmayı okur, simülasyonu adımlar ve aracı çizgi üzerinde konumlar.

**Tech Stack:** Unity 6000.6.9f1, URP 3D, C#, Splines, ProBuilder, Cinemachine, Input System, Unity Test Framework, `unity` CLI 1.0.0, Unity MCP, git + GitHub.

**Spec:** `docs/superpowers/specs/2026-11-15-apex-brake-design.md`

## Global Constraints

- Depo kökü = Unity proje kökü = `C:\Projects\ApexBrakeProject` (`Assets/`, `docs/`, `.github/` yan yana).
- Editor: `6000.6.9f1`. Şablon: URP 3D (`--template 3d`). Built-in şablon kullanılmaz.
- Yalnızca dikey ekran (Portrait). Android: IL2CPP, ARM64, minimum API 26. Paket adı `com.apexbrake.game`.
- Tüm oyun kodu `Assets/_Game/` altında; her `Scripts/` alt klasörü ayrı asmdef; kök ad alanı `ApexBrake.<Modül>`.
- `ApexBrake.Simulation` asmdef'inde `noEngineReferences: true`. `UnityEngine` kullanamaz.
- Simülasyon adımı sabit `0.02` sn. Sayılar `float`.
- Spec'teki ilk değerler: maksimum hız `30` m/s, ivme `8` m/s², fren `25` m/s², savrulma bandı `%100–115`, savrulmada hız kaybı `%40`, yeniden doğma `1,5` sn.
- Paketler yalnızca `unity-package-management` skill'indeki komutlarla eklenir; `manifest.json` elle düzenlenmez.
- `OnGUI` kullanılmaz; arayüz uGUI + TextMeshPro.
- Bu fazda tüm görseller gri kutudur (ProBuilder / basit geometri). Kenney varlığı eklenmez.
- Önce test, sonra kod. Her görev kendi commit'iyle biter; commit mesajları `Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>` ile kapanır.
- `git push`, Unity oturum açma ve lisans etkinleştirme kullanıcının evet demesinden sonra yapılır.

## Review Focus

1. **Kare hızı dalgalanması / uygulama arka plana alınıp dönme:** yarış sonucu kare hızından bağımsız olmalı, dönüşte araç ileri sıçramamalı. → Görev 8 (`SimClock` testi).
2. **Tek adımda birden çok parça sınırı geçmek (kısa parça + yüksek hız):** hiçbir engel değerlendirmesi atlanmamalı. → Görev 6.
3. **Geçersiz tarif (boş, engelle başlayan, rampa ile biten):** pist kurulmamalı, açık bir hata mesajı vermeli. → Görev 4.
4. **Yarış başlamadan basılı tutulan parmak, çoklu dokunma, arayüz üstüne dokunma:** herhangi bir parmak ekrandaysa fren; parmak geri sayımdan önce basılıysa yarış frenli başlar, takılı kalmaz. → Görev 8.
5. **Rampa inişinin hemen ardından viraj:** iniş hızıyla viraj kuralı normal şekilde uygulanmalı. → Görev 6.

---

## Dosya haritası

```
Assets/_Game/
  Scripts/
    Simulation/   ApexBrake.Simulation.asmdef
      PieceKind.cs  TrackPieceDef.cs  TrackLayout.cs
      CarStats.cs  CarPhase.cs  CrashPolicy.cs  SimEvent.cs  SimTuning.cs
      CarSim.cs  SimClock.cs
    Track/        ApexBrake.Track.asmdef
      TrackPieceAsset.cs  LevelRecipe.cs  TrackBuilder.cs  TrackPath.cs
    Racing/       ApexBrake.Racing.asmdef
      TouchBrakeInput.cs  CarController.cs  CarView.cs  RaceDebugHud.cs
    Editor/       ApexBrake.Editor.asmdef
      BuildScript.cs  GrayboxPieceFactory.cs
  Tests/
    EditMode/     ApexBrake.Tests.EditMode.asmdef
      TrackLayoutTests.cs  CarSimDrivingTests.cs  CarSimObstacleTests.cs
      CarSimCrashPolicyTests.cs  SimClockTests.cs
    PlayMode/     ApexBrake.Tests.PlayMode.asmdef
      TrackBuilderTests.cs
  Data/Levels/    Recipe_Graybox.asset
  Data/Pieces/    Piece_*.asset
  Prefabs/Track/  Piece_*.prefab
  Prefabs/Cars/   Car_Graybox.prefab
  Scenes/         Race_Graybox.unity
.github/workflows/android.yml
```

---

# FAZ 0 — KURULUM

### Task 1: Unity projesi ve depo

**Files:**
- Create: Unity proje iskeleti (`Assets/`, `Packages/`, `ProjectSettings/`), `.gitignore`

- [ ] **Step 1: Oturum ve lisans durumunu oku**

Run: `unity auth status --format json` ve `unity license status --format json`
Beklenen: ikisi de etkin. Değilse kullanıcıya tek soru sor (varsayılan: ücretsiz Unity Personal, EULA kabulüyle); evet gelirse `unity auth login` ve `unity license activate --personal --accept-eula`.

- [ ] **Step 2: Projeyi oluştur**

Run: `unity projects create ApexBrakeProject --output-dir C:\Projects --editor-version 6000.6.9f1 --template 3d --no-initial-commit --format json`
Klasör dolu olduğu için reddederse: `docs/` klasörünü scratchpad'e taşı, komutu yinele, `docs/` klasörünü geri taşı.
Beklenen: `success: true`, `data.path` = `C:\Projects\ApexBrakeProject`. Sonuçtaki bulut bağlantısı durumunu not et (Faz 5'te gerekecek); bağlantı başarısızsa proje yine geçerlidir.

- [ ] **Step 3: Android modülünü kur**

Run: `unity install-modules -e 6000.6.9f1 -m android --accept-eula`
Doğrula: `unity editors --format json` çıktısında 6000.6.9f1 için `moduleNames` içinde android (SDK/NDK ve OpenJDK dahil) görünür.

- [ ] **Step 4: Git ve uzak depo**

`git init -b main`, Unity `.gitignore` (proje oluşturma eklemediyse `unity-cli` skill'indeki yolla), `git remote add origin https://github.com/burakkayya/apex-brake-project.git`.
Doğrula: `git status` çıktısında `Library/`, `Temp/`, `Logs/`, `obj/`, `Build/`, `UserSettings/` izlenmiyor.

- [ ] **Step 5: Pipeline paketini kur ve projeyi aç**

Run: `unity pipeline install --project-path C:\Projects\ApexBrakeProject`, sonra `unity open C:\Projects\ApexBrakeProject`.
Doğrula: `unity status` Editor'ü hazır gösterir; konsolda derleme hatası yok.

- [ ] **Step 6: Commit ve push**

`git add -A`, `git commit -m "chore: Unity 6 URP projesi ve tasarım dokümanları"`. Kullanıcı onayıyla `git push -u origin main`.

---

### Task 2: Paketler, klasörler, mobil ayarlar

**Files:**
- Create: `Assets/_Game/` altındaki tüm klasörler ve dosya haritasındaki 6 asmdef
- Modify: `ProjectSettings/` (oyuncu ve kalite ayarları)

**Interfaces:**
- Produces: asmdef adları ve bağımlılıkları:
  - `ApexBrake.Simulation` → bağımlılık yok, `noEngineReferences: true`
  - `ApexBrake.Track` → Simulation, `Unity.Splines`, `Unity.Mathematics`
  - `ApexBrake.Racing` → Simulation, Track, `Unity.InputSystem`, `Unity.Cinemachine`, `Unity.TextMeshPro`
  - `ApexBrake.Editor` (yalnızca Editor) → Simulation, Track, Racing, `Unity.ProBuilder`, `Unity.Splines`
  - `ApexBrake.Tests.EditMode` (Editor, test) → Simulation
  - `ApexBrake.Tests.PlayMode` (test) → Simulation, Track

- [ ] **Step 1: Paketleri ekle**

`unity-package-management` skill'i ile: `com.unity.splines`, `com.unity.probuilder`, `com.unity.cinemachine`, `com.unity.inputsystem`, `com.unity.test-framework`. Şablonda zaten olanlar atlanır.
Doğrula: `Packages/manifest.json` beşini de içerir; konsol temiz.

- [ ] **Step 2: Klasörleri ve asmdef'leri oluştur**

Spec §7.3'teki ağaç ve yukarıdaki asmdef'ler. Boş klasörlere `.gitkeep`.
Doğrula: `unity recompile` hatasız.

- [ ] **Step 3: Oyuncu ayarları**

Yalnızca Portrait; Android: IL2CPP, ARM64, min API 26, paket adı `com.apexbrake.game`, ürün adı `Apex Brake`; aktif giriş: Input System; hedef kare hızı 60 (`Application.targetFrameRate`, Task 8'de `CarController` kurar); `Time.fixedDeltaTime = 0.02`.
Doğrula: `ProjectSettings/ProjectSettings.asset` içinde bu değerler.

- [ ] **Step 4: Görsel taban**

`unity:new-unity-project` Step 7'yi uygula (`unity:urp-postprocessing`, `unity:craft-game-floors` skill'leriyle): mobil kalite katmanı aktif, Global Volume (ACES, hafif bloom, vignette), ana kamerada post-process açık.
Doğrula: `unity command capture screenshot --output baseline.png` düz gri değil, ışıklı ve tonlanmış görünür; Editor konsolunda shader/pipeline hatası yok.

- [ ] **Step 5: Commit**

`git commit -m "chore: paketler, klasör yapısı, mobil ayarlar ve görsel taban"`

---

### Task 3: Android derlemesi (yerel + CI)

**Files:**
- Create: `Assets/_Game/Scripts/Editor/BuildScript.cs`, `.github/workflows/android.yml`

**Interfaces:**
- Produces: `ApexBrake.Editor.BuildScript.BuildAndroid()` — static, parametresiz; Build Settings'teki sahneleri `Build/Android/ApexBrake.apk` olarak derler, hata varsa `EditorApplication.Exit(1)`.

- [ ] **Step 1: `BuildScript.BuildAndroid()` yaz**

`BuildPipeline.BuildPlayer` ile; geliştirme derlemesi (`BuildOptions.Development`).

- [ ] **Step 2: Yerel derleme**

Run: `unity build C:\Projects\ApexBrakeProject --help` ile bayrakları oku, sonra Android hedefiyle derle (gerekirse `-executeMethod ApexBrake.Editor.BuildScript.BuildAndroid`).
Beklenen: `Build/Android/ApexBrake.apk` oluşur, çıkış kodu 0.

- [ ] **Step 3: Telefonda aç**

Kullanıcıdan USB hata ayıklaması açık bir Android telefon bağlamasını iste. `adb devices` cihazı listeler; `adb install -r Build/Android/ApexBrake.apk`.
Beklenen: uygulama dikey açılır, şablon sahnesi görünür. Telefon yoksa bu adım açık kalır ve kullanıcıya bildirilir; plan devam eder.

- [ ] **Step 4: CI iş akışı**

Run: `unity ci --help`; CLI'ın ürettiği GitHub Actions iskeletini kullan. İş akışı `push` (main) ve `workflow_dispatch` ile tetiklenir, EditMode testlerini çalıştırır, Android APK'yı artifact olarak yükler. iOS işi bu fazda eklenmez.
İş akışının istediği lisans/secret adlarını kullanıcıya listele; secret'ları kullanıcı GitHub'da ekler.

- [ ] **Step 5: Commit, push ve CI doğrulaması**

`git commit -m "ci: Android derleme betiği ve iş akışı"`, kullanıcı onayıyla push.
Doğrula: `gh run watch` yeşil ve APK artifact'i var. Secret'lar eklenmediyse CI kırmızı kalır; bu durum kullanıcıya açıkça raporlanır.

**Faz 0 kapısı:** boş sahne telefonda açılıyor (ya da telefon yoksa APK üretiliyor) ve kullanıcı onayladı.

---

# FAZ 1 — GRİ KUTU ÇEKİRDEK

Test komutu (tüm görevlerde): `unity test C:\Projects\ApexBrakeProject --mode EditMode --filter "<SınıfAdı>"`. Çıkış kodu 0 = geçti, 8 = test başarısız.

### Task 4: Pist modeli (`TrackLayout`)

**Files:**
- Create: `Simulation/PieceKind.cs`, `Simulation/TrackPieceDef.cs`, `Simulation/TrackLayout.cs`
- Test: `Tests/EditMode/TrackLayoutTests.cs`

**Interfaces:**
- Produces:
```csharp
namespace ApexBrake.Simulation
public enum PieceKind { Straight, Corner, Ramp, Bump }

public sealed class TrackPieceDef {
    public PieceKind Kind;
    public float Length;        // m, > 0
    public float SafeSpeed;     // Corner, Bump: güvenli hız. Ramp: en yüksek güvenli atlama hızı
    public float MinJumpSpeed;  // yalnızca Ramp
    public float GapLength;     // yalnızca Ramp: parçanın SONUNDAKİ boşluk, Length'e dahil
    public static float SafeSpeedFromRadius(float radius); // sqrt(12f * radius)
}

public readonly struct PieceSpan { public readonly int Index; public readonly TrackPieceDef Def;
                                   public readonly float Start; public readonly float End; }

public sealed class TrackLayout {
    public TrackLayout(IReadOnlyList<TrackPieceDef> pieces); // geçersizse ArgumentException
    public float TotalLength { get; }
    public int PieceCount { get; }
    public PieceSpan SpanAt(int index);
    public PieceSpan SpanAtDistance(float distance);  // [0, TotalLength] dışı kenetlenir
    public bool TryGetNextObstacle(float distance, out PieceSpan span); // Start >= distance olan ilk Straight olmayan parça
}
```
- Rampa geometrisi: kalkış noktası = `End - GapLength`; iniş noktası = `End`.
- Geçerlilik kuralları (mesaj İngilizce, kuralı adıyla söyler): liste boş olamaz; ilk parça `Straight` olmalı; son parça `Straight` olmalı; her `Length > 0`; `Corner`/`Bump` için `SafeSpeed > 0`; `Ramp` için `0 < MinJumpSpeed < SafeSpeed` ve `0 < GapLength < Length`.

- [ ] **Step 1: Başarısız testleri yaz**

```csharp
[Test] public void TotalLength_IsSumOfPieces()            // Straight 50 + Corner 60 + Straight 40 → 150
[Test] public void SpanAtDistance_ReturnsPieceAndBounds()  // 75 → Index 1, Start 50, End 110
[Test] public void SpanAtDistance_BoundaryBelongsToNextPiece() // 50 → Index 1
[Test] public void SpanAtDistance_ClampsOutOfRange()       // -5 → Index 0; 999 → son parça
[Test] public void TryGetNextObstacle_SkipsStraights()     // 10 → Corner (Index 1); 120 → false
[Test] public void SafeSpeedFromRadius_UsesGrip12()        // radius 40 → 21.9089f ± 0.001
[Test] public void Ctor_RejectsEmpty()
[Test] public void Ctor_RejectsObstacleAsFirstPiece()
[Test] public void Ctor_RejectsObstacleAsLastPiece()
[Test] public void Ctor_RejectsRampWithMinJumpAboveSafe()
[Test] public void Ctor_RejectsNonPositiveLength()
```
Her `Rejects` testi `Assert.Throws<ArgumentException>` kullanır.

- [ ] **Step 2: Testleri çalıştır, başarısız olduklarını gör** (derleme hatası: tipler yok)
- [ ] **Step 3: Üç dosyayı yukarıdaki imzalarla uygula**
- [ ] **Step 4: Testleri çalıştır** — `--filter TrackLayoutTests`, beklenen: 11 geçti
- [ ] **Step 5: Commit** — `feat(sim): pist modeli ve tarif doğrulaması`

---

### Task 5: Araç simülasyonu — hızlanma ve fren

**Files:**
- Create: `Simulation/CarStats.cs`, `Simulation/CarPhase.cs`, `Simulation/SimTuning.cs`, `Simulation/SimEvent.cs`, `Simulation/CrashPolicy.cs`, `Simulation/CarSim.cs`
- Test: `Tests/EditMode/CarSimDrivingTests.cs`

**Interfaces:**
- Consumes: `TrackLayout` (Task 4)
- Produces:
```csharp
public sealed class CarStats { public float Acceleration = 8f; public float TopSpeed = 30f; public float BrakeDecel = 25f; }

public sealed class SimTuning {
    public float SkidBand = 1.15f;          // güvenli hızın bu katına kadar savrulma, üstü çakılma
    public float SkidSpeedLoss = 0.40f;
    public float SkidDuration = 0.6f;       // sn, yalnızca görsel durum
    public float RespawnDelay = 1.5f;
    public float CornerRespawnRunUp = 15f;  // m
    public float RampRunUpMargin = 1.2f;
    public float Gravity = 20f;             // m/s², yalnızca görsel uçuş yayı için
}

public enum CarPhase { Driving, Skidding, Airborne, Crashed, Respawning, Finished }
public enum CrashPolicy { Eliminate, Respawn, EndRun }
public enum SimEventKind { CleanPass, Skid, Crash, FellShort, Takeoff, Landed, Respawned, Finished }
public readonly struct SimEvent { public readonly SimEventKind Kind; public readonly int PieceIndex; public readonly float Distance; }

public sealed class CarSim {
    public const float Step = 0.02f;
    public CarSim(TrackLayout layout, CarStats stats, SimTuning tuning, CrashPolicy policy);
    public float Distance { get; }
    public float Speed { get; }
    public CarPhase Phase { get; }
    public float PhaseTime { get; }           // mevcut fazda geçen sn
    public bool IsOut { get; }                // Eliminate/EndRun ile çakıldıysa true
    public IReadOnlyList<SimEvent> Events { get; }  // yalnızca SON Tick'in olayları
    public void Tick(bool braking);           // tam olarak Step kadar ilerletir
}
```
- `Tick` kuralları: `Driving`/`Skidding`'de frensizken `Speed += Acceleration*Step`, `TopSpeed`'de kenetlenir; frenliyken `Speed -= BrakeDecel*Step`, 0'da kenetlenir. `Distance += Speed*Step`. `Distance >= TotalLength` olunca `Finished`, `Distance = TotalLength`, `Finished` olayı bir kez. `Finished` veya `IsOut` iken `Tick` hiçbir şeyi değiştirmez.

- [ ] **Step 1: Başarısız testleri yaz** (pist: tek `Straight` 2000 m, aksi belirtilmedikçe)

```csharp
[Test] public void StartsAtRestInDrivingPhase()               // Distance 0, Speed 0, Driving
[Test] public void Accelerates_8PerSecond()                    // 50 Tick(false) → Speed 8 ± 0.001
[Test] public void ClampsAtTopSpeed()                          // 500 Tick(false) → Speed == 30
[Test] public void Braking_Decelerates25PerSecond()            // 30'a çık, 20 Tick(true) → Speed 20 ± 0.01
[Test] public void Braking_NeverGoesNegative()                 // 30'dan 200 Tick(true) → Speed == 0, Distance artmıyor
[Test] public void Distance_IntegratesSpeed()                  // 30'da sabitken 50 Tick → Distance +30 ± 0.01
[Test] public void Finishes_AtTrackEnd_AndEmitsOnce()          // Straight 100: bitene dek Tick → Phase Finished, Distance == 100, Finished olayı tam 1 kez
[Test] public void Tick_AfterFinish_IsNoOp()
[Test] public void SameInputs_ProduceIdenticalState()          // iki CarSim, aynı 1000 girdilik dizi → Distance ve Speed bit düzeyinde eşit
```

- [ ] **Step 2: Çalıştır, başarısız olduklarını gör**
- [ ] **Step 3: Dosyaları uygula** (bu görevde yalnızca düz sürüş; engel mantığı Task 6)
- [ ] **Step 4: Çalıştır** — `--filter CarSimDrivingTests`, beklenen: 9 geçti
- [ ] **Step 5: Commit** — `feat(sim): araç hızlanma, fren ve bitiş`

---

### Task 6: Engeller ve iki kademeli hata

**Files:**
- Modify: `Simulation/CarSim.cs`
- Test: `Tests/EditMode/CarSimObstacleTests.cs`

**Interfaces:**
- Consumes: Task 4–5 tipleri
- Produces (davranış; imza değişmez):
  - **Viraj ve tümsek — giriş değerlendirmesi:** araç parçanın `Start` noktasını geçtiği Tick'te `oran = Speed / SafeSpeed`.
    - `oran <= 1` → `CleanPass`.
    - `1 < oran <= SkidBand` → `Skid`: `Speed *= (1 - SkidSpeedLoss)`, faz `Skidding`, `SkidDuration` sonra `Driving`.
    - `oran > SkidBand` → `Crash`.
  - **Viraj içinde hız tavanı:** araç `Corner` parçasının içindeyken `Speed`, `SafeSpeed` üstüne çıkamaz (temiz ya da savrulmuş girişten sonra). Tümsekte tavan yok.
  - **Rampa — kalkış değerlendirmesi:** araç kalkış noktasını (`End - GapLength`) geçtiği Tick'te:
    - `Speed > SafeSpeed` → `Crash`.
    - `Speed < MinJumpSpeed` → `FellShort` (çakılma sayılır).
    - aksi → `Takeoff`, faz `Airborne`: hız sabit, fren ve gaz etkisiz. `Distance >= End` olunca `Landed`, faz `Driving`.
  - **Değerlendirme hızı:** bir sınır, Tick'in başındaki hızla (o Tick'in gaz/freni uygulanmadan önceki `Speed`) değerlendirilir; aynı Tick'te önceki bir sınır hızı değiştirdiyse (savrulma) değişmiş hızla.
  - **Tek Tick'te birden çok sınır:** bir Tick'te geçilen her sınır mesafe sırasıyla değerlendirilir; hiçbiri atlanmaz. İlk çakılmada değerlendirme durur.
  - Bu görevde çakılma sonucu: `Phase = Crashed`, `Speed = 0`. Politikaya göre devamı Task 7.

- [ ] **Step 1: Başarısız testleri yaz**

Yardımcı: `SimAt(layout, distance, speed)` — testte aracı istenen mesafe ve hıza getiren, `internal` bir `CarSim.SetStateForTest(float distance, float speed)` üzerinden kurulum (`InternalsVisibleTo("ApexBrake.Tests.EditMode")`).
Pist A: `Straight 100, Corner 60 (Safe 20), Straight 100`.
Pist B: `Straight 100, Ramp 40 (Min 15, Safe 26, Gap 12), Straight 100`.

```csharp
[Test] public void Corner_AtSafeSpeed_CleanPass()             // giriş 20.0 → CleanPass, Speed değişmez
[Test] public void Corner_At110Percent_SkidsAndLoses40()      // giriş 22 → Skid, Speed 13.2 ± 0.05, Phase Skidding
[Test] public void Corner_JustBelow115Percent_StillSkids()    // giriş 22.9 → Skid
[Test] public void Corner_Above115Percent_Crashes()           // giriş 23.5 → Crash, Phase Crashed, Speed 0
[Test] public void Skid_ReturnsToDrivingAfter0_6s()           // 30 Tick sonra Driving
[Test] public void Corner_CapsSpeedAtSafeWhileInside()        // giriş 18, frensiz 100 Tick boyunca viraj içinde Speed <= 20
[Test] public void Corner_ExitReleasesCap()                   // çıkıştan sonra frensiz → Speed > 20
[Test] public void Bump_UsesSameTwoTiers_NoCap()              // Bump Safe 20: giriş 22 → Skid; içinde hızlanabilir
[Test] public void Ramp_BetweenMinAndSafe_TakesOffAndLands()  // kalkış 20 → Takeoff, Airborne; sonra Landed, Driving, Speed 20
[Test] public void Ramp_Airborne_IgnoresBrake()               // havada Tick(true) → Speed değişmez
[Test] public void Ramp_BelowMin_FellShort()                  // kalkış 14 → FellShort, Phase Crashed
[Test] public void Ramp_AboveSafe_Crashes()                   // kalkış 27 → Crash
[Test] public void MultipleBoundariesInOneTick_AllEvaluated() // Straight 100, Bump 0.2 (Safe 10), Corner 0.2 (Safe 10), Straight 100; 99.9'da hız 30 → o Tick'te Crash (Bump'ta), CleanPass yok
[Test] public void LandingDirectlyIntoCorner_AppliesCornerRule() // Straight 100, Ramp 40 (Min 15, Safe 26, Gap 12), Corner 60 (Safe 20), Straight 100; kalkış 25 → Landed ve aynı ya da sonraki Tick'te Crash (25 > 23)
```

- [ ] **Step 2: Çalıştır, başarısız olduklarını gör**
- [ ] **Step 3: `CarSim.Tick` içine sınır geçişi değerlendirmesini ekle**

Yaklaşım: Tick başında `eskiMesafe`, sonunda `yeniMesafe`; aradaki her değerlendirme noktası (parça `Start`'ları ve rampa kalkış/iniş noktaları) sırayla işlenir.

- [ ] **Step 4: Çalıştır** — `--filter CarSim`, beklenen: Task 5'in 9 testi + 14 yeni test geçti
- [ ] **Step 5: Commit** — `feat(sim): viraj, tümsek, rampa ve iki kademeli hata`

---

### Task 7: Çakılma politikaları (eleme / yeniden doğma / tur sonu)

**Files:**
- Modify: `Simulation/CarSim.cs`
- Test: `Tests/EditMode/CarSimCrashPolicyTests.cs`

**Interfaces:**
- Produces (davranış):
  - `Eliminate` ve `EndRun`: çakılınca `IsOut = true`, faz `Crashed` kalır, sonraki `Tick`'ler etkisiz.
  - `Respawn`: çakılınca faz `Crashed`; `RespawnDelay` (1,5 sn = 75 Tick) sonra `Respawned` olayı, faz `Driving`, `Speed = 0`, `IsOut = false`, `Distance = yenidenDoğmaNoktası`.
  - Yeniden doğma noktası:
    - Viraj/tümsek: `max(0, parça.Start - CornerRespawnRunUp)`.
    - Rampa (çakılma ya da kısa düşme): `max(0, kalkışNoktası - MinJumpSpeed² / (2 * Acceleration) * RampRunUpMargin)`.
  - Yeniden doğma noktası başka bir engelin içine düşerse, o engelin `Start`'ından önceki ilk `Straight` parçanın başına çekilir.
  - Yeniden doğan araç aynı engeli yeniden değerlendirir (sınırı tekrar geçer).

- [ ] **Step 1: Başarısız testleri yaz** (Pist A ve B, Task 6'daki gibi)

```csharp
[Test] public void Eliminate_Crash_SetsIsOut_AndFreezes()
[Test] public void EndRun_Crash_SetsIsOut()
[Test] public void Respawn_Corner_After1_5s_15mBeforeStart()   // Pist A: çakıl → 74 Tick sonra hâlâ Crashed; 75. Tick'te Distance 85, Speed 0, Driving, Respawned olayı
[Test] public void Respawn_Ramp_RunUpAllowsMinJump()           // Pist B: kalkış noktası 128; koşu = 15²/(2*8)*1.2 = 16.875 → Distance 111.125 ± 0.01
[Test] public void Respawn_Ramp_FullThrottleClearsGap()        // yeniden doğduktan sonra hep Tick(false) → Takeoff ve Landed olayları gelir, Crash/FellShort gelmez
[Test] public void Respawn_ClampsAtTrackStart()                // Straight 5, Corner 60 (Safe 20), Straight 100 → Distance 0
[Test] public void Respawn_NeverLandsInsideAnotherObstacle()   // Straight 50, Corner 30 (Safe 20), Straight 5, Corner 30 (Safe 20), Straight 50; ikinci virajda çakıl → Distance 80 (aradaki Straight'in başı)
[Test] public void Respawn_IsNotOut()
```

- [ ] **Step 2: Çalıştır, başarısız olduklarını gör**
- [ ] **Step 3: Politika mantığını uygula**
- [ ] **Step 4: Çalıştır** — `--mode EditMode` tümü, beklenen: 42 geçti (11 + 9 + 14 + 8)
- [ ] **Step 5: Commit** — `feat(sim): çakılma politikaları ve yeniden doğma`

---

### Task 8: Sabit adım saati ve dokunma girdisi

**Files:**
- Create: `Simulation/SimClock.cs`, `Racing/TouchBrakeInput.cs`
- Test: `Tests/EditMode/SimClockTests.cs`

**Interfaces:**
- Produces:
```csharp
namespace ApexBrake.Simulation
public sealed class SimClock {
    public const int MaxTicksPerFrame = 5;
    public int Advance(float frameDeltaSeconds); // bu karede atılacak Tick sayısı; artan süre biriktirilir
    public float Alpha { get; }                  // 0..1, görsel yumuşatma için adımlar arası oran
}

namespace ApexBrake.Racing
public sealed class TouchBrakeInput : MonoBehaviour {
    public bool IsBraking { get; }  // herhangi bir parmak ekrandaysa ya da (Editor/masaüstü) sol fare tuşu ya da Space basılıysa true
}
```
- `Advance`: negatif ya da `NaN` süre 0 sayılır. Tick sayısı `MaxTicksPerFrame`'i aşarsa 5 döner ve birikmiş fazla süre atılır (arka plandan dönüşte sıçrama olmaz).
- `TouchBrakeInput` durumu olay yerine her karede yoklar (`Touchscreen.current`, `Mouse.current`, `Keyboard.current`); böylece yarış başlamadan basılı tutulan parmak doğru okunur ve bırakma olayı kaçsa bile takılı kalmaz. Arayüz üstündeki dokunma da frendir.

- [ ] **Step 1: Başarısız testleri yaz**

```csharp
[Test] public void Advance_0_02_ReturnsOneTick()
[Test] public void Advance_AccumulatesRemainder()           // 0.015 → 0; 0.015 → 1
[Test] public void Advance_SameTotalTime_SameTickCount()    // 100×(1/30 sn) ile 200×(1/60 sn) toplam Tick sayısı eşit (±1)
[Test] public void Advance_LongPause_CapsAt5_AndDropsBacklog() // 3.0 → 5; hemen ardından 0.02 → 1
[Test] public void Advance_NegativeOrNaN_ReturnsZero()
[Test] public void Alpha_IsFractionOfStep()                 // Advance(0.03) sonrası Alpha 0.5 ± 0.01
```

- [ ] **Step 2: Çalıştır, başarısız olduklarını gör**
- [ ] **Step 3: `SimClock` ve `TouchBrakeInput` uygula**
- [ ] **Step 4: Çalıştır** — `--filter SimClockTests`, beklenen: 6 geçti. `TouchBrakeInput` Task 10'da cihazda doğrulanır.
- [ ] **Step 5: Commit** — `feat: sabit adım saati ve tek dokunuş fren girdisi`

---

### Task 9: Modüler pist kurucu ve gri kutu parçalar

**Files:**
- Create: `Track/TrackPieceAsset.cs`, `Track/LevelRecipe.cs`, `Track/TrackPath.cs`, `Track/TrackBuilder.cs`, `Editor/GrayboxPieceFactory.cs`
- Create (varlık): `Prefabs/Track/Piece_*.prefab`, `Data/Pieces/Piece_*.asset`, `Data/Levels/Recipe_Graybox.asset`
- Test: `Tests/PlayMode/TrackBuilderTests.cs`

**Interfaces:**
- Consumes: `TrackPieceDef`, `TrackLayout`, `PieceKind`
- Produces:
```csharp
namespace ApexBrake.Track
[CreateAssetMenu(menuName = "Apex Brake/Track Piece")]
public sealed class TrackPieceAsset : ScriptableObject {
    public PieceKind kind;
    public GameObject prefab;       // kökte SplineContainer; çizgi yerel (0,0,0)'dan +Z yönünde başlar
    public float radius;            // Corner: > 0 ise SafeSpeed buradan türetilir
    public float safeSpeedOverride; // > 0 ise türetilen değeri ezer; Bump ve Ramp için zorunlu
    public float minJumpSpeed;      // Ramp
    public float gapLength;         // Ramp
    public TrackPieceDef ToDef(float measuredLength);
}

[CreateAssetMenu(menuName = "Apex Brake/Level Recipe")]
public sealed class LevelRecipe : ScriptableObject { public List<TrackPieceAsset> pieces; }

public sealed class TrackPath {   // TrackBuilder üretir
    public float TotalLength { get; }
    public void Evaluate(float distance, out Vector3 position, out Quaternion rotation);
}

public sealed class TrackBuilder : MonoBehaviour {
    public LevelRecipe recipe;
    public TrackLayout Layout { get; }
    public TrackPath Path { get; }
    public void Build();  // çocukları temizler, parçaları uç uca dizer, Layout ve Path üretir. Geçersiz tarifte ArgumentException'ı yakalamaz.
}
```
- Parça uzunluğu elle girilmez; `SplineContainer`'dan ölçülür.
- Bir parçanın çıkış noktası ve yönü = çizgisinin son noktası ve teğeti; sıradaki parça oraya hizalanır.
- Gri kutu parçalar (`GrayboxPieceFactory`, menü: `Apex Brake/Graybox/Parçaları Üret`): yol genişliği 16 m (5 şerit × 3,2 m), düz gri yüzey, viraj/rampa/tümsek ayırt edilsin diye farklı düz renk.

| Parça | Geometri | Değerler |
|---|---|---|
| `Piece_Straight50` | 50 m düz | — |
| `Piece_Straight100` | 100 m düz | — |
| `Piece_CornerL90_R40`, `Piece_CornerR90_R40` | 90°, yarıçap 40 | Safe türetilir (≈21,9) |
| `Piece_CornerL90_R25`, `Piece_CornerR90_R25` | 90°, yarıçap 25 | Safe türetilir (≈17,3) |
| `Piece_Ramp` | 40 m, son 12 m boşluk, kalkışta 2 m yükselti | Min 15, Safe 26, Gap 12 |
| `Piece_Bump` | 12 m, 1 m tümsek | Safe 20 |

- `Recipe_Graybox` (≈ 1000 m, tam gazda ≈ 35–40 sn):
  `Straight100, CornerR90_R40, Straight100, Bump, Straight50, CornerL90_R25, Straight100, Ramp, Straight100, CornerL90_R40, Straight50, CornerR90_R25, Straight100, Ramp, Straight50, CornerR90_R40, Straight100`

- [ ] **Step 1: Başarısız PlayMode testlerini yaz**

```csharp
[UnityTest] public IEnumerator Build_TotalLength_MatchesSumOfSplineLengths()   // ±0.05 m
[UnityTest] public IEnumerator Build_PiecesAreContinuous()                      // her sınırda Evaluate(d-0.01) ile Evaluate(d+0.01) arası mesafe < 0.05 m
[UnityTest] public IEnumerator Evaluate_AtZero_IsBuilderOrigin_FacingForward()
[UnityTest] public IEnumerator Layout_KindsMatchRecipeOrder()
[UnityTest] public IEnumerator Build_Twice_DoesNotDuplicateChildren()
[UnityTest] public IEnumerator Build_InvalidRecipe_Throws()                     // Corner ile başlayan tarif → ArgumentException
```
Testler kendi küçük parçalarını kodla üretir (prefab varlıklarına bağlı değildir).

- [ ] **Step 2: Çalıştır** — `unity test . --mode PlayMode --filter TrackBuilderTests`, başarısız
- [ ] **Step 3: Dört çalışma zamanı dosyasını uygula**
- [ ] **Step 4: Çalıştır** — beklenen: 6 geçti
- [ ] **Step 5: `GrayboxPieceFactory` yaz ve çalıştır**

Parça prefab'larını, `TrackPieceAsset`'leri ve `Recipe_Graybox`'ı üretir.
Doğrula: boş bir sahnede `TrackBuilder.Build()` sonrası `Layout.TotalLength` 950–1100 m arasında; sahne görüntüsünde pist kopuksuz.

- [ ] **Step 6: Commit** — `feat(track): modüler pist kurucu ve gri kutu parçalar`

---

### Task 10: Oynanabilir gri kutu yarışı

**Files:**
- Create: `Racing/CarController.cs`, `Racing/CarView.cs`, `Racing/RaceDebugHud.cs`, `Prefabs/Cars/Car_Graybox.prefab`, `Scenes/Race_Graybox.unity`

**Interfaces:**
- Consumes: `TrackBuilder.Layout/Path`, `CarSim`, `SimClock`, `TouchBrakeInput.IsBraking`, `SimEvent`
- Produces:
```csharp
namespace ApexBrake.Racing
public sealed class CarController : MonoBehaviour {
    public TrackBuilder track; public TouchBrakeInput input; public CrashPolicy policy;
    public CarSim Sim { get; }
    public event Action<SimEvent> SimEventRaised;
    public void Restart();   // yeni CarSim, başlangıç çizgisi
}
public sealed class CarView : MonoBehaviour { }      // CarController'ı dinler, aracı konumlar
public sealed class RaceDebugHud : MonoBehaviour { } // uGUI + TMP
```
- `CarController.Update`: `SimClock.Advance(Time.unscaledDeltaTime)` kadar `Sim.Tick(input.IsBraking)`; her Tick'in olaylarını `SimEventRaised` ile yayar. `Start`'ta `Application.targetFrameRate = 60`, `track.Build()`.
- `CarView`: konum ve yön `TrackPath.Evaluate(Sim.Distance)`; önceki ve şimdiki Tick arasında `SimClock.Alpha` ile yumuşatma.
  - `Airborne`: yüksekliğe parabol eklenir (`SimTuning.Gravity`, uçuş süresi = boşluk / hız).
  - `Skidding`: araç gövdesi 0,6 sn boyunca ±25° sağa-sola yalpalar.
  - `Crashed`: gövde pistten dışa doğru takla atarak fırlar (yalnızca görsel).
- Kamera: Cinemachine takip kamerası, dikey ekran; aracın 9 m arkası, 5 m yukarısı, aşağı 15° bakış, FOV 60; hız arttıkça FOV 68'e açılır.
- `RaceDebugHud` (gri kutu yardımcısı, sonraki fazlarda kaldırılır): hız (m/s), sıradaki engelin türü, güvenli hızı ve uzaklığı, fren göstergesi, son olay (`Skid` / `Crash` / `Takeoff` ...), geçen süre; `Finished` ya da `IsOut` olunca "Tekrar için dokun" ve dokunuşta `Restart()`.
- `Race_Graybox` sahnesi: `TrackBuilder` (`Recipe_Graybox`), `Car_Graybox` (ProBuilder kutu gövde + 4 silindir teker), `policy = Eliminate`, yönlü ışık, Task 2'deki Global Volume. Build Settings'te tek sahne.

- [ ] **Step 1: Üç betiği, araç prefab'ını ve sahneyi oluştur**
- [ ] **Step 2: Editor'de oynat ve doğrula**

Unity MCP ile Play moduna gir, `unity command capture screenshot` ile görüntü al.
Beklenen: araç pisti takip ediyor; dokunmadan bırakılınca ilk virajda (R40, güvenli ≈21,9; araç 30 m/s) çakılıp eleniyor; Space basılı tutulunca yavaşlıyor; HUD değerleri simülasyonla tutarlı; konsolda hata yok.

- [ ] **Step 3: Tüm testleri çalıştır**

Run: `unity test . --mode EditMode` ve `unity test . --mode PlayMode`
Beklenen: 48 EditMode + 6 PlayMode geçti.

- [ ] **Step 4: Android'e derle ve telefonda dene**

`BuildScript.BuildAndroid()`, `adb install -r`.
Beklenen: 60 fps'e yakın akıcı; parmak basılıyken fren; iki parmakla da fren; uygulamayı arka plana alıp dönünce araç sıçramıyor; yarış başlamadan basılı parmakla frenli başlıyor ve bırakınca hızlanıyor.

- [ ] **Step 5: Commit ve push** — `feat: oynanabilir gri kutu yarışı` (push kullanıcı onayıyla)

- [ ] **Step 6: Kullanıcı oynanış testi (Faz 1 kapısı)**

Kullanıcı APK'yı oynar. Sorulacaklar: fren hissi tatmin edici mi, güvenli hızı sezmek mümkün mü, savrulma/çakılma ayrımı anlaşılıyor mu, rampa adil mi, 30–45 sn doğru uzunluk mu. Geri bildirime göre `SimTuning` / `CarStats` / parça değerleri ayarlanır; değişen ilk değerler spec'e işlenir. Onay gelmeden Faz 2 planı yazılmaz.

---

## Bu planın kapsamadığı

Spec'in Faz 2 ve sonrası: botlar, şerit içi temas ve daralma parçaları, Perfect Brake Boost, mini harita, ekonomi, ana ekran, yerelleştirme, tema sistemi, çevrimiçi modlar, reklam, iOS. Her biri kendi planını alır.
