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
            builder.recipe = AssetDatabase.LoadAssetAtPath<LevelRecipe>("Assets/_Game/Data/Levels/Recipe_Graybox.asset");

            // Yarış yöneticisi
            var raceGo = new GameObject("Race");
            var input = raceGo.AddComponent<TouchBrakeInput>();
            var controller = raceGo.AddComponent<CarController>();
            controller.track = builder;
            controller.input = input;
            controller.policy = CrashPolicy.Eliminate;
            var hud = raceGo.AddComponent<RaceDebugHud>();
            hud.controller = controller;

            // Araç
            var car = (GameObject)PrefabUtility.InstantiatePrefab(carPrefab);
            var view = car.GetComponent<CarView>();
            view.controller = controller;

            // Takip kamerası: aracın 9 m arkası, 5 m yukarısı, ~15° aşağı bakış, FOV 60
            var vcamGo = new GameObject("CM Chase Camera");
            var vcam = vcamGo.AddComponent<CinemachineCamera>();
            vcam.Target.TrackingTarget = car.transform;
            vcam.Lens.FieldOfView = 60f;
            vcam.Lens.NearClipPlane = 0.3f;
            vcam.Lens.FarClipPlane = 1500f;
            var follow = vcamGo.AddComponent<CinemachineFollow>();
            follow.FollowOffset = new Vector3(0f, 5f, -9f);
            follow.TrackerSettings.BindingMode = Unity.Cinemachine.TargetTracking.BindingMode.LazyFollow;
            follow.TrackerSettings.PositionDamping = new Vector3(0.3f, 0.3f, 0.1f);
            var composer = vcamGo.AddComponent<CinemachineHardLookAt>();
            composer.LookAtOffset = new Vector3(0f, 2.6f, 0f); // 9 m'de 15° aşağı bakış
            vcam.Target.LookAtTarget = car.transform;
            view.vcam = vcam;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("[GrayboxSceneBuilder] Race_Graybox üretildi.");
        }

        static GameObject BuildCarPrefab()
        {
            var root = new GameObject("Car_Graybox");
            root.AddComponent<CarView>();

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

            root.GetComponent<CarView>().body = body.transform;
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
