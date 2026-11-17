using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using ApexBrake.Racing;
using ApexBrake.Track;

namespace ApexBrake.Editor
{
    /// <summary>Race_Graybox sahnesini (fizik araçlar, RaceManager, takip kamerası, HUD) üretir.</summary>
    public static class GrayboxSceneBuilder
    {
        const string CarPrefabPath = "Assets/_Game/Prefabs/Cars/Car_Physics.prefab";
        const string ConfigPath = "Assets/_Game/Data/Vehicles/VehicleConfig_Default.asset";
        const string ScenePath = "Assets/_Game/Scenes/Race_Graybox.unity";

        [MenuItem("Apex Brake/Graybox/Sahneyi Üret")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(CarPrefabPath) == null) CarPrefabFactory.Generate();
            var carPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CarPrefabPath);
            var config = AssetDatabase.LoadAssetAtPath<VehicleConfig>(ConfigPath);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Işık
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            lightGo.AddComponent<UniversalAdditionalLightData>();
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // Global Volume
            var volGo = new GameObject("Global Volume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/_Game/Data/GlobalVolumeProfile.asset");

            // Pist (yol dışı zemini TrackBuilder kurar)
            var trackGo = new GameObject("Track");
            var builder = trackGo.AddComponent<TrackBuilder>();
            builder.recipe = AssetDatabase.LoadAssetAtPath<LevelRecipe>("Assets/_Game/Data/Levels/Recipe_Graybox_Race.asset");
            builder.groundMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Materials/Ground_Grass.mat");

            // Yarış: 5 fizik araç çalışma anında prefab'tan kurulur (oyuncu kırmızı; botlar mavi, sarı, yeşil, mor).
            var raceGo = new GameObject("Race");
            var input = raceGo.AddComponent<TouchBrakeInput>();
            var race = raceGo.AddComponent<RaceManager>();
            race.track = builder;
            race.input = input;
            race.carPrefab = carPrefab;
            race.vehicleConfig = config;
            race.playerPolicy = OutPolicy.Eliminate;
            race.botPolicy = OutPolicy.Eliminate;

            // Kamera (spec §11.6): ChaseCameraRig ofset/FOV/yatmayı hıza, frene, kaymaya göre sürer.
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 62f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 1500f;
            camGo.AddComponent<AudioListener>();
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            var rig = camGo.AddComponent<ChaseCameraRig>();
            rig.race = race;

            var staging = raceGo.AddComponent<RaceStaging>();
            staging.race = race; staging.input = input; staging.cameraRig = rig;

            // Arayüz: HUD, mini harita, sonuç paneli
            BuildUi(race, input, RaceManagerColors);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("[GrayboxSceneBuilder] Race_Graybox üretildi.");
        }

        static readonly Color[] RaceManagerColors =
        {
            new Color(0.85f, 0.12f, 0.10f), new Color(0.15f, 0.35f, 0.85f), new Color(0.95f, 0.80f, 0.15f),
            new Color(0.20f, 0.70f, 0.30f), new Color(0.55f, 0.25f, 0.75f),
        };

        // ---- arayüz -------------------------------------------------------------

        static void BuildUi(RaceManager race, TouchBrakeInput input, Color[] dotColors)
        {
            // EventSystem (Input System modülü): TEKRAR düğmesi için
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esGo.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();

            var canvasGo = new GameObject("RaceCanvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            var safe = Rect("SafeArea", canvasGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            // --- HUD ---
            var hud = canvasGo.AddComponent<RaceHud>();
            hud.race = race;
            hud.input = input;
            hud.safeArea = safe;

            var brake = Rect("BrakeBar", safe, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0, 36));
            hud.brakeBar = Img(brake, new Color(0.95f, 0.12f, 0.12f, 0.85f));
            hud.brakeBar.enabled = false;

            hud.countdownText = Text("Countdown", safe, new Vector2(0.5f, 0.72f), new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(900, 400), 280, TMPro.TextAlignmentOptions.Center);
            hud.rankText = Text("Rank", safe, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-40, -40), new Vector2(420, 150), 110, TMPro.TextAlignmentOptions.TopRight);
            hud.speedText = Text("Speed", safe, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 70), new Vector2(600, 130), 90, TMPro.TextAlignmentOptions.Bottom);
            hud.feedbackText = Text("Feedback", safe, new Vector2(0.5f, 0.78f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000, 200), 120, TMPro.TextAlignmentOptions.Center);
            hud.feedbackText.color = new Color(1f, 0.9f, 0.3f);
            hud.feedbackText.text = "";
            // Sollama göstergesi: hızın üstünde küçük yazı (yalnızca sollarken görünür)
            hud.overtakeText = Text("Overtake", safe, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 215), new Vector2(500, 80), 54, TMPro.TextAlignmentOptions.Bottom);
            hud.overtakeText.color = new Color(0.55f, 0.9f, 1f);
            hud.overtakeText.text = "SOLLAMA";
            hud.overtakeText.enabled = false;

            // --- Mini harita: sol kenara 24 px boşlukla yaslı, yarı saydam, dikeyde ortalı, yükseklik ekranın %45'i ---
            var mapGo = new GameObject("MiniMap", typeof(RectTransform));
            mapGo.transform.SetParent(safe, false);
            var mapRt = (RectTransform)mapGo.transform;
            mapRt.anchorMin = new Vector2(0, 0.275f); mapRt.anchorMax = new Vector2(0, 0.725f);
            mapRt.pivot = new Vector2(0, 0.5f);
            mapRt.anchoredPosition = new Vector2(24, 0);
            mapRt.sizeDelta = new Vector2(16, 0);
            Img(mapRt, new Color(0f, 0f, 0f, 0.45f));
            var mapGroup = mapGo.AddComponent<CanvasGroup>();
            mapGroup.alpha = 0.6f;
            mapGroup.blocksRaycasts = false;
            mapGroup.interactable = false;
            var map = mapGo.AddComponent<MiniMapView>();
            map.race = race;
            map.bar = mapRt;
            map.laneSpacing = 10f;   // en sol nokta (−2 × 10) ekranın içinde kalsın

            // Bitiş işareti: üstte küçük siyah-beyaz bayrak şeridi
            var finish = Rect("Finish", mapRt, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 0f), new Vector2(0, 6), new Vector2(44, 14));
            Img(finish, Color.white);
            for (int k = 0; k < 4; k++)
            {
                var sq = Rect("Check" + k, finish, new Vector2(k / 4f, k % 2 == 0 ? 0.5f : 0f), new Vector2((k + 1) / 4f, k % 2 == 0 ? 1f : 0.5f),
                    new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                Img(sq, Color.black);
            }

            var knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            var dots = new UnityEngine.UI.Image[dotColors.Length];
            for (int i = dotColors.Length - 1; i >= 0; i--)
            {
                if (i == RaceManager.PlayerIndex) continue;
                var d = Rect("Dot" + i, mapRt, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22, 22));
                dots[i] = Img(d, dotColors[i], knob);
            }
            // Oyuncu: beyaz çerçeve (34) + araç rengi (26), en son (en üstte) çizilir
            var ring = Rect("PlayerDot", mapRt, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34, 34));
            Img(ring, Color.white, knob);
            var inner = Rect("Dot0", ring, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24, 24));
            dots[RaceManager.PlayerIndex] = Img(inner, dotColors[RaceManager.PlayerIndex], knob);
            map.dots = dots;

            // --- Sonuç paneli ---
            var resultGo = new GameObject("ResultPanel", typeof(RectTransform));
            resultGo.transform.SetParent(safe, false);
            var resultRt = (RectTransform)resultGo.transform;
            resultRt.anchorMin = Vector2.zero; resultRt.anchorMax = Vector2.one;
            resultRt.offsetMin = Vector2.zero; resultRt.offsetMax = Vector2.zero;
            var result = resultGo.AddComponent<RaceResultPanel>();
            result.race = race;

            var panel = Rect("Panel", resultRt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var dim = Img(panel, new Color(0f, 0f, 0f, 0.72f));
            dim.raycastTarget = true; // panel açıkken dokunuşlar alttakilere geçmez
            result.panel = panel.gameObject;

            var card = Rect("Card", panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900, 1340));
            Img(card, new Color(0.12f, 0.13f, 0.16f, 0.95f));
            result.titleText = Text("Title", card, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -40), new Vector2(820, 240), 190, TMPro.TextAlignmentOptions.Center);
            result.titleText.enableAutoSizing = true;   // "ELENDIN" kartın genişliğine sığsın
            result.titleText.fontSizeMin = 60;
            result.titleText.fontSizeMax = 190;
            result.timeText = Text("Time", card, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -290), new Vector2(820, 100), 72, TMPro.TextAlignmentOptions.Center);

            int rows = dotColors.Length;
            result.rowRank = new TMPro.TMP_Text[rows];
            result.rowName = new TMPro.TMP_Text[rows];
            result.rowStatus = new TMPro.TMP_Text[rows];
            for (int r = 0; r < rows; r++)
            {
                float y = -430 - r * 110;
                var row = Rect("Row" + (r + 1), card, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(780, 95));
                Img(row, new Color(1f, 1f, 1f, r % 2 == 0 ? 0.06f : 0.02f));
                result.rowRank[r] = Text("Rank", row, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(30, 0), new Vector2(120, 90), 56, TMPro.TextAlignmentOptions.Left);
                result.rowName[r] = Text("Name", row, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(160, 0), new Vector2(340, 90), 56, TMPro.TextAlignmentOptions.Left);
                result.rowStatus[r] = Text("Status", row, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-30, 0), new Vector2(280, 90), 56, TMPro.TextAlignmentOptions.Right);
            }

            var btn = Rect("RetryButton", card, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 50), new Vector2(560, 150));
            var btnImg = Img(btn, new Color(0.90f, 0.20f, 0.20f));
            btnImg.raycastTarget = true;
            var button = btn.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = btnImg;
            var label = Text("Label", btn, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(540, 140), 84, TMPro.TextAlignmentOptions.Center);
            label.text = "TEKRAR";
            result.retryButton = button;
            panel.gameObject.SetActive(false);
        }

        static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            return rt;
        }

        static UnityEngine.UI.Image Img(RectTransform rt, Color color, Sprite sprite = null)
        {
            var img = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
            img.color = color;
            img.sprite = sprite;
            img.raycastTarget = false;
            return img;
        }

        static TMPro.TMP_Text Text(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, float fontSize, TMPro.TextAlignmentOptions align)
        {
            var rt = Rect(name, parent, anchor, anchor, pivot, pos, size);
            var t = rt.gameObject.AddComponent<TMPro.TextMeshProUGUI>();
            t.fontSize = fontSize;
            t.alignment = align;
            t.color = Color.white;
            t.raycastTarget = false;
            t.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            t.fontStyle = TMPro.FontStyles.Bold;
            t.text = "";
            return t;
        }

        static Material Mat(string name, Color color)
        {
            string path = $"Assets/_Game/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", color);
            m.color = color;
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
