# Ledger — plan: docs/superpowers/plans/2026-11-17-faz-2c-gercek-fizik.md

## Başlangıç

- Çalışma ağacında Faz 2b Görev 5'ten kalan commit'lenmemiş değişiklikler (`ChaseCameraRig.cs`, `RaceDebugProbe.cs`, `RacerView.cs`, `RaceHud.cs`, `RaceController.cs`, `GrayboxPieceFactory.cs`, `GrayboxSceneBuilder.cs`) derleniyordu (yalnızca `RaceDebugProbe` CS0618 uyarıları); olduğu gibi `3fd359d wip: yarım kalan görünüm ve kamera işleri` olarak commit'lendi. Banket fikri Görev 1'de yeni yüzey üretimine taşındı.

## Görev 1 — Gerçek pist geometrisi ve izdüşüm

- Ruling: Yol yüzeyi, banket ve yan etekler tek mesh + tek `MeshCollider` (alt meshler: yol / banket / etek), katman `Road`; geometri çalışma anı derlemesindeki `TrackPieceGeometry`'de, Editor fabrikası ve testler aynı kodu kullanır — testler Editor derlemesine bağlanmadan aynı parçaları kurabilsin — bedeli: yok denecek kadar az (fabrika meshi varlık olarak kaydeder).
- Ruling: Katmanlar sabit indekslidir (`Road`=8, `Ground`=9, `Car`=10, `TrackLayers`); adlar `ProjectPhysicsSetup` (Editor, `[InitializeOnLoad]`) ile TagManager'a yazılır ve commit'lenir, çarpışma matrisi `TrackLayers.ApplyCollisionMatrix` (`RuntimeInitializeOnLoadMethod`) ile oyun açılışında koddan kurulur — derlemede de geçerli — bedeli: 8–10 başka amaçla kullanılırsa çakışır (Editor betiği hata yazar).
- Ruling: Rampa parçası 46 m: 10 m düz, 12 m karesel yükseliş (tepe 1,6 m, kalkış açısı ≈ 15°), 12 m gerçek boşluk, iniş yüzeyi 0,6 m yüksek bir ön kenarla başlar ve 6 m'de düze iner, 6 m düz çıkış. Plandaki "iniş yüzeyi yol seviyesinde başlar ve aşağı eğimli karşılama rampasıyla düze bağlanır" ifadesi kendi içinde çelişkili (yol seviyesinden aşağı eğim düze inemez); iniş için aşağı eğim (aracın teğet inmesi) esas alındı — bedeli: yavaş araç boşluğa düşünce 1,1 m'lik iniş ön yüzüne çarpar (fiziksel, istenen).
- Ruling: Boşluk artık parçanın sonunda değil ortasında; `gapLength` (12) geometri verisi olarak kalır. Eski simülasyonun `TrackLayout.TryGetNextEvalPoint`'i boşluğu parça sonunda varsayar — eski sim Görev 5'te silinir — bedeli: eski `Race_Graybox` sahnesindeki eski görünümde rampa yerleşimi Görev 5'e kadar uyumsuz.
- Ruling: `TrackPieceAsset`'ten `safeSpeedOverride` ve `minJumpSpeed` silindi; eski `TrackLayout` doğrulaması (Bump `SafeSpeed > 0`, Ramp `0 < MinJumpSpeed < SafeSpeed`) Görev 5'e kadar durduğundan `ToDef` yalnızca eski sim için sabit eski değerleri (`Legacy*`) doldurur; yeni araç/RaceManager bunları okumaz — eski sahne Görev 5'e kadar çalışır kalsın — bedeli: Görev 5'te bu sabitler ve doğrulama birlikte silinmeli.
- Ruling: `TrackBuilder.Build` pistin altına tek bir `Ground` kutusu (üst yüzey yol tabanının 0,5 m altında, pist sınırlarından 150 m taşan) kurar ve onu çocuk olarak ekler; mevcut `TrackBuilderTests.Build_Twice_DoesNotDuplicateChildren` beklentisi `pieces + 1` olarak güncellendi, `Asset()` yardımcısı silinen alanı artık yazmıyor — bedeli: yok.
- Ruling: Fizik malzemeleri çalışma anında oluşturulur (`TrackLayers.RoadMaterial` μ = 1,0; `GroundMaterial` μ = 0,4) ve `Build` sırasında atanır; `WheelCollider` zemin malzemesini kendisi okumadığından araç her tekerin değdiği yüzeyin sürtünme oranını lastik sürtünmesine çarpan olarak uygular (Görev 2) — bedeli: yeni yüzey türü eklenirse malzemesi atanmalı.
- Ruling: `TrackPath.Project` ipucu negatif/NaN ise tüm pistte arar (yeniden doğma/ilk çağrı için); aksi halde plan gereği ±40 m — bedeli: yok.
- Not: Görev 1'in uygulaması testlerle aynı adımda yazıldı; testler ilk koşuda yeşildi (önce kırmızı görülmedi).

Task 1: complete — PlayMode 19/19 (TrackGeometryTests 11 + TrackBuilderTests 8), EditMode 149/149
