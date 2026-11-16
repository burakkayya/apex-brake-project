using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ApexBrake.Simulation;
using ApexBrake.Track;

namespace ApexBrake.Editor
{
    /// <summary>Gri kutu pist parçalarını (prefab + TrackPieceAsset) ve tarifleri üretir. Geometri
    /// TrackPieceGeometry'dedir (gerçek çarpışma yüzeyi: yol + banket + yan etek, katman Road).</summary>
    public static class GrayboxPieceFactory
    {
        const string Root = "Assets/_Game";
        const string PrefabDir = Root + "/Prefabs/Track";
        const string PieceDir = Root + "/Data/Pieces";
        const string LevelDir = Root + "/Data/Levels";
        const string MaterialDir = Root + "/Materials";

        [MenuItem("Apex Brake/Graybox/Parçaları Üret")]
        public static void Generate()
        {
            ProjectPhysicsSetup.EnsureLayers(false);
            EnsureDir(PrefabDir); EnsureDir(PieceDir); EnsureDir(LevelDir); EnsureDir(MaterialDir);

            var shoulder = Mat("Road_Shoulder", new Color(0.62f, 0.56f, 0.42f));
            var skirt = Mat("Road_Skirt", new Color(0.30f, 0.28f, 0.26f));
            TrackPieceGeometry.Materials M(Material road) => new TrackPieceGeometry.Materials { Road = road, Shoulder = shoulder, Skirt = skirt };

            var road = M(Mat("Road_Gray", new Color(0.35f, 0.35f, 0.37f)));
            var cornerMat = M(Mat("Road_Corner", new Color(0.55f, 0.30f, 0.30f)));
            var rampMat = M(Mat("Road_Ramp", new Color(0.75f, 0.65f, 0.20f)));
            var bumpMat = M(Mat("Road_Bump", new Color(0.25f, 0.45f, 0.60f)));
            Mat("Ground_Grass", new Color(0.36f, 0.45f, 0.30f));

            var pieces = new Dictionary<string, TrackPieceAsset>();

            pieces["Straight50"] = Save(TrackPieceGeometry.CreateStraight("Piece_Straight50", 50f, 5, road), PieceKind.Straight, a => { });
            pieces["Straight100"] = Save(TrackPieceGeometry.CreateStraight("Piece_Straight100", 100f, 5, road), PieceKind.Straight, a => { });

            foreach (var (name, radius) in new[] { ("R60", 60f), ("R40", 40f), ("R25", 25f) })
            {
                foreach (var right in new[] { false, true })
                {
                    string id = (right ? "CornerR90_" : "CornerL90_") + name;
                    float r = radius;
                    int sign = right ? 1 : -1;
                    pieces[id] = Save(TrackPieceGeometry.CreateCorner("Piece_" + id, r, right, cornerMat), PieceKind.Corner,
                        a => { a.radius = r; a.turnSign = sign; });
                }
            }

            pieces["Ramp"] = Save(TrackPieceGeometry.CreateRamp("Piece_Ramp", rampMat), PieceKind.Ramp, a => { a.gapLength = TrackPieceGeometry.RampGap; });
            pieces["Bump"] = Save(TrackPieceGeometry.CreateBump("Piece_Bump", bumpMat), PieceKind.Bump, a => { });

            // Daralma parçaları: 60 m düz; iki ucunda 15 m'lik genişlik geçişi.
            foreach (var lanes in new[] { 3, 2 })
            {
                int n = lanes;
                var narrowMat = M(Mat("Road_Narrow" + n, n == 3 ? new Color(0.45f, 0.38f, 0.55f) : new Color(0.55f, 0.32f, 0.55f)));
                pieces["Narrow" + n + "_60"] = Save(TrackPieceGeometry.CreateStraight("Piece_Narrow" + n + "_60", 60f, n, narrowMat), PieceKind.Straight,
                    a => { a.laneCount = n; });
            }

            WriteRecipe(LevelDir + "/Recipe_Graybox.asset", pieces, new[]
            {
                "Straight100", "CornerR90_R40", "Straight100", "Bump", "Straight50", "CornerL90_R25", "Straight100", "Ramp",
                "Straight100", "CornerL90_R40", "Straight50", "CornerR90_R25", "Straight100", "Ramp", "Straight50", "CornerR90_R40", "Straight100",
            });
            WriteRecipe(LevelDir + "/Recipe_Graybox_Race.asset", pieces, new[]
            {
                "Straight100", "CornerR90_R40", "Straight100", "Narrow3_60", "Straight50", "Bump", "Straight50", "CornerL90_R25",
                "Straight100", "Ramp", "Straight100", "Narrow2_60", "Straight50", "CornerL90_R40", "Straight50", "CornerR90_R25",
                "Straight100", "Ramp", "Straight50", "CornerR90_R40", "Straight100",
            });
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[GrayboxPieceFactory] Parçalar, Recipe_Graybox ve Recipe_Graybox_Race üretildi.");
        }

        static void WriteRecipe(string path, Dictionary<string, TrackPieceAsset> pieces, string[] keys)
        {
            var recipe = LoadOrCreate<LevelRecipe>(path);
            recipe.pieces = new List<TrackPieceAsset>();
            foreach (var key in keys) recipe.pieces.Add(pieces[key]);
            EditorUtility.SetDirty(recipe);
        }

        /// <summary>Çalışma anında kurulan parça nesnesinin meshini varlık olarak kaydeder, prefab ve TrackPieceAsset yazar.</summary>
        static TrackPieceAsset Save(GameObject go, PieceKind kind, System.Action<TrackPieceAsset> configure)
        {
            string assetName = go.name;
            go.SetActive(true);
            var mf = go.GetComponentInChildren<MeshFilter>(true);
            var mesh = mf.sharedMesh;
            string meshPath = $"{PrefabDir}/{assetName}.mesh.asset";
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.DeleteAsset($"{PrefabDir}/{assetName}.shoulder.asset");   // eski ayrı banket meshi
            AssetDatabase.CreateAsset(mesh, meshPath);
            var mc = go.GetComponentInChildren<MeshCollider>(true);
            mc.sharedMesh = mesh;

            string prefabPath = $"{PrefabDir}/{assetName}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);

            var asset = LoadOrCreate<TrackPieceAsset>($"{PieceDir}/{assetName}.asset");
            asset.kind = kind;
            asset.prefab = prefab;
            asset.radius = 0f; asset.gapLength = 0f;
            asset.laneCount = 5; asset.turnSign = 0;
            configure(asset);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        // ---- yardımcılar -----------------------------------------------------

        static Material Mat(string name, Color color)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", color);
            m.color = color;
            EditorUtility.SetDirty(m);
            return m;
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a == null)
            {
                a = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(a, path);
            }
            return a;
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
