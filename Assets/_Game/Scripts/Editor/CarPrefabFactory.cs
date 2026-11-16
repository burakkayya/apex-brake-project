using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ApexBrake.Racing;
using ApexBrake.Simulation;
using ApexBrake.Track;

namespace ApexBrake.Editor
{
    /// <summary>Fizik aracın varsayılan VehicleConfig varlığını ve Car_Physics prefabını (VehicleBuilder ile) üretir;
    /// ayrıca aracı elle denemek için Dev_PhysicsCar sahnesini kurar.</summary>
    public static class CarPrefabFactory
    {
        const string ConfigPath = "Assets/_Game/Data/Vehicles/VehicleConfig_Default.asset";
        const string PrefabDir = "Assets/_Game/Prefabs/Cars";
        const string PrefabPath = PrefabDir + "/Car_Physics.prefab";
        const string MatDir = "Assets/_Game/Materials/Cars";
        const string DevRecipePath = "Assets/_Game/Data/Levels/Recipe_Dev_Physics.asset";
        const string DevScenePath = "Assets/_Game/Scenes/Dev_PhysicsCar.unity";

        [MenuItem("Apex Brake/Fizik/Arac Prefabini Uret")]
        public static void Generate()
        {
            ProjectPhysicsSetup.EnsureLayers(false);
            EnsureDir("Assets/_Game/Data/Vehicles"); EnsureDir(PrefabDir); EnsureDir(MatDir);

            var config = AssetDatabase.LoadAssetAtPath<VehicleConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<VehicleConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            else
            {
                // Varlık koddaki ayarlanmış varsayılanlarla eşitlenir (tek doğruluk kaynağı: VehicleConfig alan ilk değerleri).
                var fresh = ScriptableObject.CreateInstance<VehicleConfig>();
                EditorUtility.CopySerialized(fresh, config);
                config.name = "VehicleConfig_Default";
                Object.DestroyImmediate(fresh);
                EditorUtility.SetDirty(config);
            }

            var motor = VehicleBuilder.Create(config, new Color(0.85f, 0.12f, 0.10f), "Car_Physics");
            var go = motor.gameObject;
            // Çalışma anında oluşturulan malzemeleri varlık olarak kaydet (prefab referansları kalıcı olsun).
            var saved = new Dictionary<Material, Material>();
            int n = 0;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || AssetDatabase.Contains(mats[i])) continue;
                    if (!saved.TryGetValue(mats[i], out var asset))
                    {
                        string path = $"{MatDir}/Car_{n++}.mat";
                        AssetDatabase.DeleteAsset(path);
                        AssetDatabase.CreateAsset(mats[i], path);
                        asset = mats[i];
                        saved[mats[i]] = asset;
                    }
                    mats[i] = asset;
                }
                r.sharedMaterials = mats;
            }
            var pm = go.GetComponentInChildren<BoxCollider>().sharedMaterial;
            if (pm != null && !AssetDatabase.Contains(pm))
            {
                string pmPath = $"{MatDir}/CarBody.physicMaterial";
                AssetDatabase.DeleteAsset(pmPath);
                AssetDatabase.CreateAsset(pm, pmPath);
            }
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            Debug.Log("[CarPrefabFactory] " + ConfigPath + " ve " + PrefabPath + " üretildi.");
        }

        [MenuItem("Apex Brake/Fizik/Dev Sahnesini Kur")]
        public static void BuildDevScene()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null) Generate();
            var recipe = AssetDatabase.LoadAssetAtPath<LevelRecipe>(DevRecipePath);
            if (recipe == null) { recipe = ScriptableObject.CreateInstance<LevelRecipe>(); AssetDatabase.CreateAsset(recipe, DevRecipePath); }
            string P(string id) => "Assets/_Game/Data/Pieces/Piece_" + id + ".asset";
            recipe.pieces = new List<TrackPieceAsset>();
            foreach (var id in new[] { "Straight100", "Straight100", "Straight100", "CornerR90_R60", "Straight100", "Straight100", "CornerL90_R40",
                                       "Straight100", "Ramp", "Straight100", "Straight100", "Bump", "Straight100", "CornerR90_R25", "Straight100",
                                       "Straight100", "CornerR90_R40", "Straight100", "Straight100" })
                recipe.pieces.Add(AssetDatabase.LoadAssetAtPath<TrackPieceAsset>(P(id)));
            EditorUtility.SetDirty(recipe);
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var trackGo = new GameObject("Track");
            var builder = trackGo.AddComponent<TrackBuilder>();
            builder.recipe = recipe;
            builder.groundMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Materials/Ground_Grass.mat");

            var car = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            var input = new GameObject("Input").AddComponent<TouchBrakeInput>();
            var session = new GameObject("DevDriveSession").AddComponent<DevDriveSession>();
            session.track = builder;
            session.car = car.GetComponent<VehicleMotor>();
            session.input = input;
            session.cam = Camera.main;
            EditorSceneManager.SaveScene(scene, DevScenePath);
            Debug.Log("[CarPrefabFactory] " + DevScenePath + " kuruldu.");
        }

        static void EnsureDir(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureDir(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
