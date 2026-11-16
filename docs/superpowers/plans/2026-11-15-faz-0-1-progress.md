# Ledger — plan: docs/superpowers/plans/2026-11-15-faz-0-1-kurulum-ve-gri-kutu.md

Kural: bir görev ancak burada `Task N: complete` satırı varsa bitmiştir. Devam eden oturum ilk tamamlanmamış göreve, o görevin ilk tamamlanmamış adımından başlar. Her adımdan sonra bu dosya güncellenir.

## Kararlar (Ruling)

- Task 1: Ruling: `unity projects create` var olan klasörü reddediyor (boş olsa bile) — proje scratchpad'de oluşturulup içeriği `C:\Projects\ApexBrakeProject` köküne taşınacak — yanlışsa: Hub'da eski yol kayıtlı kalır, `unity open` ile yeni yol kaydedilir.
- Task 1: Ruling: proje `--no-cloud` ile oluşturuldu — Unity Cloud projesi oluşturmak spec §10'a göre Faz 5 öncesi kullanıcı onayı ister — yanlışsa: Faz 5'te `unity projects link cloud` ile bağlanır.

## Durum

- Task 1 Step 1: tamam — oturum açık (Unity Personal lisansı etkin).
- Task 1 Step 2: tamam — proje (URP blank, Pipeline 1.0.0 ve Cinemachine 3.1.7 dahil) oluşturulup köke taşındı.
- Task 1 Step 3: tamam — Android Build Support, SDK/NDK ve OpenJDK kurulu (PlaybackEngines/AndroidPlayer mevcut, `--list` Installed gösteriyor).
- Task 1 Step 4: tamam — git init, Unity .gitignore, origin remote eklendi; Library/Temp/Logs/UserSettings izlenmiyor.
- Task 1 Step 5: tamam — Editor açık, `unity status` ready, derleme hatası yok.
- Task 1: complete (Step 6 push hariç — kullanıcı onayı bekliyor).
- Task 1 Step 6: yerel commit yapıldı; push kullanıcı onayı bekliyor.
- Task 2 Step 1: tamam — splines 2.9.2, probuilder 6.1.4, test-framework 1.8.0 `unity command package add` ile eklendi (cinemachine, inputsystem zaten vardı).
- Task 2 Step 2: tamam — klasörler + 6 asmdef, recompile hatasız.
- Task 2 Step 3: tamam — Portrait, IL2CPP, ARM64, API 26, com.apexbrake.game, Apex Brake, Input System, fixedDeltaTime 0.02.
- Task 2 Step 4: kısmen — Mobile kalite katmanı Android varsayılanı; `Assets/_Game/Data/GlobalVolumeProfile.asset` (ACES, bloom, vignette) üretildi. Sahneye Global Volume ve kamera post-process Task 10'da `Race_Graybox` sahnesinde eklenecek; ekran görüntüsü doğrulaması orada.
- Task 2: Ruling: Global Volume sahne nesnesi şablon sahnesine değil Task 10 sahnesine konur — Task 2'de kalıcı sahne yok, çift iş olur — yanlışsa: şablon sahnesine elle eklenir.
- Task 2: complete.
- Task 4: complete — 11/11 geçti (commit: feat(sim): pist modeli).
- Task 5: complete — 9/9 geçti.
- Task 6: complete — 14 yeni test, toplam 23 geçti.
- Task 7: complete — toplam 42/42 EditMode geçti.
- Ruling: Testler `unity test` yerine `unity command test run --filter <TamAd>` ile koşuluyor — `unity test` açık Editor varken reddediyor ve Editor'ü kapatmak yerine açık Editor'ü kullanmak güvenli — yanlışsa: Editor kapatılıp `unity test` kullanılır.
- Ruling: Task 7 `Respawn_NeverLandsInsideAnotherObstacle`: "o engelin Start'ından önceki ilk Straight" = ÇAKILAN engelden önceki ilk Straight (test 80'i bekliyor: 85'teki viraj için aradaki 5 m'lik düz) — yanlışsa: aradaki düz çok kısaysa yeniden doğma adaletsiz olur, tasarımda en az ~15 m düz bırakılmalı.
- Ruling: Skid hızı kaybı Tick başı hızından hesaplanır (o Tick'te ivme uygulanmaz); kalkışta hız Tick başı hızına sabitlenir — testlerdeki 13.2 / 20.0 beklentileri için — yanlışsa: ± 0.16 m/s sapma.
- Task 3 Step 1–2: tamam — `BuildScript.BuildAndroid()` yazıldı; Android hedefine geçildi; `build run` ile `Build/Android/ApexBrake.apk` (32 MB, IL2CPP ARM64 dev) başarıyla derlendi (21 dk).
- Task 3 Step 3: atlandı — Android telefon bağlı değil (`adb devices` boş). Kullanıcı bağlayınca `adb install -r Build/Android/ApexBrake.apk`.
- Task 3 Step 4: tamam — `.github/workflows/android.yml` (`unity ci init --target Android`; push main + workflow_dispatch; test + APK artifact). Gerekli secret'lar: UNITY_SERVICE_ACCOUNT_ID, UNITY_SERVICE_ACCOUNT_SECRET, UNITY_LICENSE_SERIAL — kullanıcı ekler.
- Task 3 Step 5: commit yapıldı; push ve CI doğrulaması kullanıcı onayı/secret bekliyor.
- Task 3: complete (cihaz, push ve CI yeşil doğrulaması hariç).
- Ruling: `BuildAndroid()` yalnızca batchmode'da `EditorApplication.Exit(1)` çağırır; Editor'den çağrılırsa istisna fırlatır — açık Editor'ü kapatmamak için — yanlışsa: CI `unity build` kullandığı için etkilenmez.
- Ruling: Derleme `unity command build run` ile açık Editor'den alındı (`script eval` içinden çağrı "scripts are compiling" ile reddedildi).
- Ruling: CI iş akışı CLI iskeletinden; `android.yml` olarak yeniden adlandırıldı, tetikleyiciler plana göre daraltıldı.
- Task 8: complete — SimClockTests 6/6 geçti; TouchBrakeInput cihazda Task 10 Step 4'te doğrulanacak.
- Task 9: complete — PlayMode 6/6 geçti; gri kutu parçalar, Recipe_Graybox üretildi (17 parça, 1109,04 m).
- Ruling: Recipe_Graybox toplam 1109 m (plan aralığı 950–1100) — plan tarifi sabit ve sıra bozulmasın diye kısaltılmadı, sapma %0,8 — yanlışsa: bir Straight100 → Straight50 yapılır.
- Ruling: Rampa/tümsek çizgisi düz; 2 m yükselti ve tümsek yalnızca görsel mesh (sim zaten yüksekliği bilmiyor); uçuş yayını CarView çizer.
- Ruling: SplineContainer.Evaluate tanjantı yerel uzayda döndü ve Linear knot'ta sıfır — TrackPath.EvaluateWorld yerelde değerlendirip dünyaya çeviriyor, sıfır tanjantta komşu örnekten yön türetiyor.
- Task 10 Step 1: tamam — CarController/CarView/RaceDebugHud, Car_Graybox prefab, Race_Graybox sahnesi (Editor menü: Apex Brake/Graybox/Sahneyi Üret), Build Settings'te tek sahne.
- Task 10 Step 2: tamam — Editor'de Play: araç pisti takip ediyor, dokunmadan ilk virajda çakılıp eleniyor (HUD "CAKILDIN"), sanal Space ile fren çalışıyor, konsolda oyun hatası yok (yalnızca URP'nin ilgisiz `urtshader` uyarı/hatası).
- Task 10 Step 3: tamam — 48 EditMode + 6 PlayMode geçti.
- Ruling: Araç gövdesi ProBuilder yerine Unity primitifleri (cube/cylinder) — görsel fark yok, bu fazda gri kutu — yanlışsa: ProBuilder'a çevrilir.
- Ruling: HUD metni ASCII (Turkce karakter yok) — TMP varsayılan fontu ç/ş/ğ/ı içermiyor — yanlışsa: ileride font asset genişletilir.
- Ruling: Kamera Cinemachine 3 (CinemachineFollow offset 0,5,-9 + HardLookAt offset 0,2.6,0); FOV 60→68 CarView'da hıza bağlı.
- Task 10 Step 4: Android APK derlendi ama telefon yok → cihaz testi (60 fps, çoklu parmak, arka plan, basılı başlangıç) yapılmadı; kullanıcı telefonda doğrulayacak.
- Task 10 Step 5: commit yapıldı; push kullanıcı onayı bekliyor.
- Task 10 Step 6: Faz 1 kapısı — kullanıcı oynanış testi bekleniyor. DURULDU.

## Kullanıcı onayı gerektirenler

- `git push` (her seferinde), Unity Cloud bağlantısı, GitHub secret'ları (kullanıcı ekler), Android telefon bağlantısı.
