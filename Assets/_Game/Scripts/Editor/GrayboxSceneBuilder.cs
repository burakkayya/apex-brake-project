using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using ApexBrake.Racing;
using ApexBrake.Simulation;
using ApexBrake.Track;

namespace ApexBrake.Editor
{
    /// <summary>Car_Graybox prefab'ını ve Race_Graybox sahnesini üretir.</summary>
    public static class GrayboxSceneBuilder
    {
        const string CarPrefabPath = "Assets/_Game/Prefabs/Cars/Car_Graybox.prefab";
        const string ScenePath = "Assets/_Game/Scenes/Race_Graybox.unity";

        [MenuItem("Apex Brake/Graybox/Sahneyi Üret")]
        public static void Build()
        {
            var carPrefab = BuildCarPrefab();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Işık
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightGo.AddComponent<UniversalAdditionalLightData>();
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // Zemin
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            Object.DestroyImmediate(ground.GetComponent<Collider>());
            ground.transform.position = new Vector3(0, -0.25f, 0);
            ground.transform.localScale = new Vector3(3000f, 0.2f, 3000f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = Mat("Ground", new Color(0.18f, 0.28f, 0.18f));

            // Global Volume
            var volGo = new GameObject("Global Volume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/_Game/Data/GlobalVolumeProfile.asset");

            // Kamera + Cinemachine
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 1500f;
            camGo.AddComponent<AudioListener>();
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camGo.AddComponent<CinemachineBrain>();

            // Pist
            var trackGo = new GameObject("Track");
            var builder = trackGo.AddComponent<TrackBuilder>();
            builder.recipe = AssetDatabase.LoadAssetAtPath<LevelRecipe>("Assets/_Game/Data/Levels/Recipe_Graybox_Race.asset");

            // Yarış yöneticisi
            var raceGo = new GameObject("Race");
            var input = raceGo.AddComponent<TouchBrakeInput>();
            var controller = raceGo.AddComponent<RaceController>();
            controller.track = builder;
            controller.input = input;

            // Araçlar: 0 oyuncu (kırmızı), 1-4 botlar (mavi, sarı, yeşil, mor)
            var colors = new[]
            {
                ("Car", new Color(0.85f, 0.15f, 0.15f)),
                ("Car_Blue", new Color(0.15f, 0.35f, 0.90f)),
                ("Car_Yellow", new Color(0.95f, 0.80f, 0.10f)),
                ("Car_Green", new Color(0.15f, 0.70f, 0.25f)),
                ("Car_Purple", new Color(0.60f, 0.20f, 0.80f)),
            };
            var names = new[] { "Car_Player", "Car_Bot1_Usta", "Car_Bot2_Dengeli", "Car_Bot3_Atak", "Car_Bot4_Yavas" };
            RacerView playerView = null;
            for (int i = 0; i < colors.Length; i++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(carPrefab);
                go.name = names[i];
                var v = go.GetComponent<RacerView>();
                v.controller = controller;
                v.racerIndex = i;
                var mat = Mat(colors[i].Item1, colors[i].Item2);
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                    if (r.gameObject.name != "Wheel") r.sharedMaterial = mat;
                if (i == RaceController.PlayerIndex) playerView = v;
            }
            var car = playerView.gameObject;
            var view = playerView;

            // Takip kamerası: aracın 15 m arkası, 8 m yukarısı, ~20° aşağı bakış, FOV 60 (dikey ekranda 5 şerit görünsün)
            var vcamGo = new GameObject("CM Chase Camera");
            var vcam = vcamGo.AddComponent<CinemachineCamera>();
            vcam.Target.TrackingTarget = car.transform;
            vcam.Lens.FieldOfView = 60f;
            vcam.Lens.NearClipPlane = 0.3f;
            vcam.Lens.FarClipPlane = 1500f;
            var follow = vcamGo.AddComponent<CinemachineFollow>();
            follow.FollowOffset = new Vector3(0f, 8f, -15f);
            follow.TrackerSettings.BindingMode = Unity.Cinemachine.TargetTracking.BindingMode.LazyFollow;
            follow.TrackerSettings.PositionDamping = new Vector3(0.3f, 0.3f, 0.1f);
            var composer = vcamGo.AddComponent<CinemachineHardLookAt>();
            composer.LookAtOffset = new Vector3(0f, 2.6f, 0f); // 15 m'de ~20° aşağı bakış
            vcam.Target.LookAtTarget = car.transform;
            view.vcam = vcam;

            // Arayüz: HUD, mini harita, sonuç paneli
            var dotColors = new Color[colors.Length];
            for (int i = 0; i < colors.Length; i++) dotColors[i] = colors[i].Item2;
            BuildUi(controller, dotColors);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("[GrayboxSceneBuilder] Race_Graybox üretildi.");
        }

        // ---- arayüz -------------------------------------------------------------

        static void BuildUi(RaceController controller, Color[] dotColors)
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
            hud.controller = controller;
            hud.safeArea = safe;

            var brake = Rect("BrakeBar", safe, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0, 36));
            hud.brakeBar = Img(brake, new Color(0.95f, 0.12f, 0.12f, 0.85f));
            hud.brakeBar.enabled = false;

            hud.countdownText = Text("Countdown", safe, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(900, 400), 280, TMPro.TextAlignmentOptions.Center);
            hud.rankText = Text("Rank", safe, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-40, -40), new Vector2(420, 150), 110, TMPro.TextAlignmentOptions.TopRight);
            hud.speedText = Text("Speed", safe, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 70), new Vector2(600, 130), 90, TMPro.TextAlignmentOptions.Bottom);
            hud.feedbackText = Text("Feedback", safe, new Vector2(0.5f, 0.78f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000, 200), 120, TMPro.TextAlignmentOptions.Center);
            hud.feedbackText.color = new Color(1f, 0.9f, 0.3f);
            hud.feedbackText.text = "";

            // --- Mini harita: sol kenar, dikeyde ortalı, genişlik 16, yükseklik ekranın %45'i ---
            var mapGo = new GameObject("MiniMap", typeof(RectTransform));
            mapGo.transform.SetParent(safe, false);
            var mapRt = (RectTransform)mapGo.transform;
            mapRt.anchorMin = new Vector2(0, 0.275f); mapRt.anchorMax = new Vector2(0, 0.725f);
            mapRt.pivot = new Vector2(0, 0.5f);
            mapRt.anchoredPosition = new Vector2(48, 0);
            mapRt.sizeDelta = new Vector2(16, 0);
            Img(mapRt, new Color(0f, 0f, 0f, 0.45f));
            var map = mapGo.AddComponent<MiniMapView>();
            map.controller = controller;
            map.bar = mapRt;

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
                if (i == RaceController.PlayerIndex) continue;
                var d = Rect("Dot" + i, mapRt, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22, 22));
                dots[i] = Img(d, dotColors[i], knob);
            }
            // Oyuncu: beyaz çerçeve (34) + araç rengi (26), en son (en üstte) çizilir
            var ring = Rect("PlayerDot", mapRt, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34, 34));
            Img(ring, Color.white, knob);
            var inner = Rect("Dot0", ring, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24, 24));
            dots[RaceController.PlayerIndex] = Img(inner, dotColors[RaceController.PlayerIndex], knob);
            map.dots = dots;

            // --- Sonuç paneli ---
            var resultGo = new GameObject("ResultPanel", typeof(RectTransform));
            resultGo.transform.SetParent(safe, false);
            var resultRt = (RectTransform)resultGo.transform;
            resultRt.anchorMin = Vector2.zero; resultRt.anchorMax = Vector2.one;
            resultRt.offsetMin = Vector2.zero; resultRt.offsetMax = Vector2.zero;
            var result = resultGo.AddComponent<RaceResultPanel>();
            result.controller = controller;

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
            result.countsText = Text("Counts", card, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -390), new Vector2(820, 90), 56, TMPro.TextAlignmentOptions.Center);
            result.countsText.color = new Color(1f, 0.9f, 0.3f);

            int rows = dotColors.Length;
            result.rowRank = new TMPro.TMP_Text[rows];
            result.rowName = new TMPro.TMP_Text[rows];
            result.rowStatus = new TMPro.TMP_Text[rows];
            for (int r = 0; r < rows; r++)
            {
                float y = -520 - r * 105;
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

        static GameObject BuildCarPrefab()
        {
            var root = new GameObject("Car_Graybox");
            root.AddComponent<RacerView>();

            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            var carMat = Mat("Car", new Color(0.85f, 0.15f, 0.15f));
            var wheelMat = Mat("Wheel", new Color(0.08f, 0.08f, 0.08f));

            var hull = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hull.name = "Hull";
            Object.DestroyImmediate(hull.GetComponent<Collider>());
            hull.transform.SetParent(body.transform, false);
            hull.transform.localScale = new Vector3(1.8f, 0.6f, 4f);
            hull.transform.localPosition = new Vector3(0, 0.1f, 0);
            hull.GetComponent<MeshRenderer>().sharedMaterial = carMat;

            var cabin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cabin.name = "Cabin";
            Object.DestroyImmediate(cabin.GetComponent<Collider>());
            cabin.transform.SetParent(body.transform, false);
            cabin.transform.localScale = new Vector3(1.4f, 0.5f, 1.6f);
            cabin.transform.localPosition = new Vector3(0, 0.65f, -0.2f);
            cabin.GetComponent<MeshRenderer>().sharedMaterial = carMat;

            foreach (var x in new[] { -1f, 1f })
                foreach (var z in new[] { -1.3f, 1.3f })
                {
                    var w = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    w.name = "Wheel";
                    Object.DestroyImmediate(w.GetComponent<Collider>());
                    w.transform.SetParent(body.transform, false);
                    w.transform.localScale = new Vector3(0.7f, 0.15f, 0.7f);
                    w.transform.localRotation = Quaternion.Euler(0, 0, 90f);
                    w.transform.localPosition = new Vector3(x * 0.95f, -0.1f, z);
                    w.GetComponent<MeshRenderer>().sharedMaterial = wheelMat;
                }

            root.GetComponent<RacerView>().body = body.transform;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, CarPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
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
