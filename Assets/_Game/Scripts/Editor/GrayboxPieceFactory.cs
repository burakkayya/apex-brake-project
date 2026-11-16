using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using ApexBrake.Simulation;
using ApexBrake.Track;

namespace ApexBrake.Editor
{
    /// <summary>Gri kutu pist parçalarını (prefab + TrackPieceAsset) ve Recipe_Graybox'ı üretir.</summary>
    public static class GrayboxPieceFactory
    {
        const string Root = "Assets/_Game";
        const string PrefabDir = Root + "/Prefabs/Track";
        const string PieceDir = Root + "/Data/Pieces";
        const string LevelDir = Root + "/Data/Levels";
        const string MaterialDir = Root + "/Materials";
        const float RoadWidth = 16f; // 5 şerit x 3,2 m

        [MenuItem("Apex Brake/Graybox/Parçaları Üret")]
        public static void Generate()
        {
            EnsureDir(PrefabDir); EnsureDir(PieceDir); EnsureDir(LevelDir); EnsureDir(MaterialDir);

            var road = Mat("Road_Gray", new Color(0.35f, 0.35f, 0.37f));
            var cornerMat = Mat("Road_Corner", new Color(0.55f, 0.30f, 0.30f));
            var rampMat = Mat("Road_Ramp", new Color(0.75f, 0.65f, 0.20f));
            var bumpMat = Mat("Road_Bump", new Color(0.25f, 0.45f, 0.60f));

            var pieces = new Dictionary<string, TrackPieceAsset>();

            pieces["Straight50"] = MakePiece("Piece_Straight50", PieceKind.Straight, StraightKnots(50f), 0f, 50f, road, null, a => { });
            pieces["Straight100"] = MakePiece("Piece_Straight100", PieceKind.Straight, StraightKnots(100f), 0f, 100f, road, null, a => { });

            foreach (var (name, radius) in new[] { ("R40", 40f), ("R25", 25f) })
            {
                foreach (var right in new[] { false, true })
                {
                    string id = (right ? "CornerR90_" : "CornerL90_") + name;
                    float r = radius;
                    pieces[id] = MakePiece("Piece_" + id, PieceKind.Corner, ArcKnots(r, right), 0f, -1f, cornerMat, null, a => { a.radius = r; });
                }
            }

            pieces["Ramp"] = MakePiece("Piece_Ramp", PieceKind.Ramp, StraightKnots(40f), 0f, 28f, rampMat, go => AddRampWedge(go, rampMat),
                a => { a.safeSpeedOverride = 26f; a.minJumpSpeed = 15f; a.gapLength = 12f; });
            pieces["Bump"] = MakePiece("Piece_Bump", PieceKind.Bump, StraightKnots(12f), 0f, 12f, bumpMat, go => AddBump(go, bumpMat),
                a => { a.safeSpeedOverride = 20f; });

            var recipe = LoadOrCreate<LevelRecipe>(LevelDir + "/Recipe_Graybox.asset");
            recipe.pieces = new List<TrackPieceAsset>();
            foreach (var key in new[]
            {
                "Straight100", "CornerR90_R40", "Straight100", "Bump", "Straight50", "CornerL90_R25", "Straight100", "Ramp",
                "Straight100", "CornerL90_R40", "Straight50", "CornerR90_R25", "Straight100", "Ramp", "Straight50", "CornerR90_R40", "Straight100",
            })
                recipe.pieces.Add(pieces[key]);
            EditorUtility.SetDirty(recipe);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[GrayboxPieceFactory] Parçalar ve Recipe_Graybox üretildi.");
        }

        // ---- çizgi tanımları -------------------------------------------------

        static BezierKnot[] StraightKnots(float length) => new[]
        {
            new BezierKnot(new float3(0, 0, 0)),
            new BezierKnot(new float3(0, 0, length)),
        };

        static BezierKnot[] ArcKnots(float radius, bool right)
        {
            float k = 0.5523f * radius;
            float side = right ? 1f : -1f;
            var end = Quaternion.LookRotation(new Vector3(side, 0, 0));
            return new[]
            {
                new BezierKnot(new float3(0, 0, 0), new float3(0, 0, -k), new float3(0, 0, k), quaternion.identity),
                new BezierKnot(new float3(side * radius, 0, radius), new float3(0, 0, -k), new float3(0, 0, k), end),
            };
        }

        // ---- parça üretimi ---------------------------------------------------

        static TrackPieceAsset MakePiece(string assetName, PieceKind kind, BezierKnot[] knots, float roadFrom, float roadTo,
            Material mat, System.Action<GameObject> decorate, System.Action<TrackPieceAsset> configure)
        {
            var go = new GameObject(assetName);
            var container = go.AddComponent<SplineContainer>();
            var spline = container.Spline;
            spline.Clear();
            bool linear = knots.Length == 2 && math.lengthsq(knots[0].TangentOut) == 0f;
            foreach (var kn in knots) spline.Add(kn, linear ? TangentMode.Linear : TangentMode.Broken);

            float length = spline.GetLength();
            float to = roadTo < 0f ? length : roadTo;
            var mesh = BuildRoadMesh(spline, roadFrom, to);
            string meshPath = $"{PrefabDir}/{assetName}.mesh.asset";
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);

            var roadGo = new GameObject("Road");
            roadGo.transform.SetParent(go.transform, false);
            roadGo.AddComponent<MeshFilter>().sharedMesh = mesh;
            roadGo.AddComponent<MeshRenderer>().sharedMaterial = mat;
            decorate?.Invoke(go);

            string prefabPath = $"{PrefabDir}/{assetName}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);

            var asset = LoadOrCreate<TrackPieceAsset>($"{PieceDir}/{assetName}.asset");
            asset.kind = kind;
            asset.prefab = prefab;
            asset.radius = 0f; asset.safeSpeedOverride = 0f; asset.minJumpSpeed = 0f; asset.gapLength = 0f;
            configure(asset);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        static Mesh BuildRoadMesh(Spline spline, float from, float to)
        {
            float total = spline.GetLength();
            int segs = Mathf.Max(2, Mathf.CeilToInt((to - from) / 1f));
            var pts = new Vector3[segs + 1];
            for (int i = 0; i <= segs; i++)
            {
                float d = Mathf.Lerp(from, to, i / (float)segs);
                float t = spline.ConvertIndexUnit(d, PathIndexUnit.Distance, PathIndexUnit.Normalized);
                spline.Evaluate(Mathf.Clamp01(t), out float3 p, out _, out _);
                pts[i] = p;
            }

            var verts = new Vector3[(segs + 1) * 2];
            var tris = new List<int>();
            for (int i = 0; i <= segs; i++)
            {
                Vector3 fwd = (i < segs ? pts[i + 1] - pts[i] : pts[i] - pts[i - 1]);
                fwd.y = 0f; fwd.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                verts[i * 2] = pts[i] - right * (RoadWidth * 0.5f);
                verts[i * 2 + 1] = pts[i] + right * (RoadWidth * 0.5f);
                if (i < segs)
                {
                    int a = i * 2, b = a + 1, c = a + 2, d2 = a + 3;
                    tris.AddRange(new[] { a, c, b, b, c, d2 });
                }
            }
            var mesh = new Mesh { name = "Road" };
            mesh.vertices = verts;
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static void AddRampWedge(GameObject piece, Material mat)
        {
            // Kalkış rampası: boşluktan hemen önce, 4 m uzunlukta eğik levha (2 m yükselti).
            var wedge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(wedge.GetComponent<Collider>());
            wedge.name = "RampWedge";
            wedge.transform.SetParent(piece.transform, false);
            float angle = Mathf.Atan2(2f, 4f) * Mathf.Rad2Deg;
            wedge.transform.localScale = new Vector3(RoadWidth, 0.3f, 4.5f);
            wedge.transform.localPosition = new Vector3(0, 1f, 26f);
            wedge.transform.localRotation = Quaternion.Euler(-angle, 0, 0);
            wedge.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static void AddBump(GameObject piece, Material mat)
        {
            var bump = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(bump.GetComponent<Collider>());
            bump.name = "BumpMesh";
            bump.transform.SetParent(piece.transform, false);
            bump.transform.localScale = new Vector3(2f, RoadWidth * 0.5f, 2f);
            bump.transform.localRotation = Quaternion.Euler(0, 0, 90f);
            bump.transform.localPosition = new Vector3(0, 0f, 6f);
            bump.GetComponent<MeshRenderer>().sharedMaterial = mat;
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
