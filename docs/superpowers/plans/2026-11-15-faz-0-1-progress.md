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
- Task 3–10: bekliyor.

## Kullanıcı onayı gerektirenler

- `git push` (her seferinde), Unity Cloud bağlantısı, GitHub secret'ları (kullanıcı ekler), Android telefon bağlantısı.
