# Ledger — plan: docs/superpowers/plans/2026-11-15-faz-0-1-kurulum-ve-gri-kutu.md

Kural: bir görev ancak burada `Task N: complete` satırı varsa bitmiştir. Devam eden oturum ilk tamamlanmamış göreve, o görevin ilk tamamlanmamış adımından başlar. Her adımdan sonra bu dosya güncellenir.

## Kararlar (Ruling)

- Task 1: Ruling: `unity projects create` var olan klasörü reddediyor (boş olsa bile) — proje scratchpad'de oluşturulup içeriği `C:\Projects\ApexBrakeProject` köküne taşınacak — yanlışsa: Hub'da eski yol kayıtlı kalır, `unity open` ile yeni yol kaydedilir.
- Task 1: Ruling: proje `--no-cloud` ile oluşturuldu — Unity Cloud projesi oluşturmak spec §10'a göre Faz 5 öncesi kullanıcı onayı ister — yanlışsa: Faz 5'te `unity projects link cloud` ile bağlanır.

## Durum

- Task 1 Step 1: tamam — oturum açık (Unity Personal lisansı etkin).
- Task 1 Step 2: tamam — proje (URP blank, Pipeline 1.0.0 ve Cinemachine 3.1.7 dahil) oluşturulup köke taşındı.
- Task 1 Step 3: SORUNLU — `unity install-modules --editor-version 6000.6.9f1 --list` boş liste dönüyor; Editor `C:\D\Unity Projects\6000.6.9f1` altına elle eklenmiş, Hub modül bilgisini bilmiyor (PlaybackEngines'te yalnızca windowsstandalonesupport var). Denenecek: `unity editors module refresh 6000.6.9f1`, sonra `unity install-modules --editor-version 6000.6.9f1 -m android android-sdk-ndk-tools android-open-jdk --accept-eula`. Olmazsa kullanıcıdan Unity Hub'dan Android Build Support eklemesini iste. Bu adım Görev 3'e kadar engelleyici değil; Görev 2 ve 4–9 devam edebilir.
- Task 1 Step 4: tamam — git init, Unity .gitignore, origin remote eklendi; Library/Temp/Logs/UserSettings izlenmiyor.
- Task 1 Step 5: bekliyor (Pipeline paketi zaten kurulu; `unity open` ile aç).
- Task 1 Step 6: yerel commit yapıldı; push kullanıcı onayı bekliyor.
- Task 2–10: bekliyor.

## Kullanıcı onayı gerektirenler

- `git push` (her seferinde), Unity Cloud bağlantısı, GitHub secret'ları (kullanıcı ekler), Android telefon bağlantısı.
