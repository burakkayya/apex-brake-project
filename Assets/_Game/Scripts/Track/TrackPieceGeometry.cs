using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

namespace ApexBrake.Track
{
    /// <summary>
    /// Pist parçalarının gerçek geometrisi: çizgi (yükseklik profili dahil) ve çizgi boyunca üretilen yol + banket +
    /// yan etek yüzeyi (tek MeshCollider, katman Road). Editor fabrikası ve testler aynı kodu kullanır.
    /// Parça yerel uzayı: çizgi (0,0,0)'dan +Z yönünde başlar; yolun taban seviyesi y = 0.
    /// </summary>
    public static class TrackPieceGeometry
    {
        public const float LaneWidth = 3.2f;
        public const float ShoulderWidth = 1.5f;
        public const float GroundDrop = 0.5f;        // Ground üst yüzeyi yol tabanının bu kadar altında
        public const float NarrowTaper = 15f;        // daralan parçanın iki ucundaki geçiş
        public const float SampleStep = 0.5f;        // yüzey örnekleme aralığı (m)

        // Rampa profili (yerel mesafe, m)
        public const float RampApproach = 10f;       // düz yaklaşım
        public const float RampRise = 12f;           // yükseliş (kalkış kenarında biter)
        public const float RampHeight = 1.8f;        // kalkış kenarı yüksekliği
        public const float RampCurve = 8f;           // yükselişin eğrisel geçiş kısmı; kalan kısım sabit açılı düz
        public const float RampSlope = RampHeight / (RampRise - RampCurve * 0.5f);   // tan(kalkış açısı) ≈ 0,225 → 12,7°
        public const float RampGap = 12f;            // boşluk
        public const float RampLandingLip = 0.6f;    // iniş yüzeyinin ön kenarı
        public const float RampLanding = 6f;         // aşağı eğimli karşılama
        public const float RampRunOut = 6f;          // düz çıkış
        public const float RampTakeoff = RampApproach + RampRise;         // 22
        public const float RampGapEnd = RampTakeoff + RampGap;            // 34
        public const float RampLength = RampGapEnd + RampLanding + RampRunOut; // 46

        /// <summary>Rampa profili çizgisi boyunca (yay uzunluğu) kalkış kenarı, boşluk sonu ve parça boyu.
        /// Yol aralıkları yay uzunluğuyla verilir; yatay ölçü kullanılırsa parça sonunda kapak/boşluk oluşur.</summary>
        public static readonly float RampTakeoffArc = ProfileArc(RampTakeoff, RampProfile);
        public static readonly float RampGapEndArc = ProfileArc(RampGapEnd, RampProfile);

        /// <summary>Doğrusal profil knot'ları boyunca (0,5 m) 0'dan z'ye yay uzunluğu.</summary>
        public static float ProfileArc(float z, Func<float, float> height)
        {
            float len = 0f, prevZ = 0f, prevH = height(0f);
            for (float s = SampleStep; prevZ < z - 1e-5f; s += SampleStep)
            {
                float zz = Mathf.Min(s, z), h = height(zz);
                len += Mathf.Sqrt((zz - prevZ) * (zz - prevZ) + (h - prevH) * (h - prevH));
                prevZ = zz; prevH = h;
            }
            return len;
        }

        // Tümsek profili
        public const float BumpLength = 12f;
        public const float BumpHeight = 0.8f;

        public static float HalfWidth(int laneCount) => laneCount * LaneWidth * 0.5f;

        /// <summary>Şerit yarı genişliği (banket hariç). 5'ten az şeritli parça iki ucunda 15 m'de 5 şeride açılır.</summary>
        public static float HalfWidthAt(int laneCount, float local, float length)
        {
            float full = HalfWidth(5), narrow = HalfWidth(laneCount);
            if (laneCount >= 5) return narrow;
            float taper = Mathf.Min(NarrowTaper, length * 0.5f);
            if (local < taper) return Mathf.Lerp(full, narrow, local / taper);
            if (local > length - taper) return Mathf.Lerp(narrow, full, (local - (length - taper)) / taper);
            return narrow;
        }

        // ---- yükseklik profilleri ---------------------------------------------

        public static float RampProfile(float s)
        {
            if (s <= RampApproach) return 0f;
            if (s <= RampTakeoff)
            {
                // Eğrisel geçiş (eğim 0 → RampSlope), sonra sabit açılı düz kısım: süspansiyon kalkıştan önce yükünü atar.
                float x = s - RampApproach;
                if (x <= RampCurve) return RampSlope * x * x / (2f * RampCurve);
                return RampSlope * RampCurve * 0.5f + RampSlope * (x - RampCurve);
            }
            if (s <= RampGapEnd) return Mathf.Lerp(RampHeight, RampLandingLip, (s - RampTakeoff) / RampGap);  // sanal (yüzey yok)
            if (s <= RampGapEnd + RampLanding)
            {
                float x = (s - RampGapEnd) / RampLanding;            // 0..1; sonda eğim 0
                return RampLandingLip * (1f - x) * (1f - x);
            }
            return 0f;
        }

        public static float BumpProfile(float s)
        {
            if (s <= 0f || s >= BumpLength) return 0f;
            return BumpHeight * 0.5f * (1f - Mathf.Cos(2f * Mathf.PI * s / BumpLength));
        }

        // ---- çizgiler -------------------------------------------------------------

        public static BezierKnot[] StraightKnots(float length) => new[]
        {
            new BezierKnot(new float3(0, 0, 0)),
            new BezierKnot(new float3(0, 0, length)),
        };

        public static BezierKnot[] ArcKnots(float radius, bool right)
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

        /// <summary>Düz hat boyunca yükseklik profili (doğrusal knot'lar, 0,5 m aralık).</summary>
        public static BezierKnot[] ProfileKnots(float length, Func<float, float> height)
        {
            int n = Mathf.CeilToInt(length / SampleStep);
            var knots = new BezierKnot[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float s = Mathf.Min(length, i * SampleStep);
                knots[i] = new BezierKnot(new float3(0, height(s), s));
            }
            return knots;
        }

        // ---- parça üretimi --------------------------------------------------------

        public struct Materials
        {
            public Material Road, Shoulder, Skirt;
        }

        static Material _defRoad, _defShoulder, _defSkirt;

        public static Materials DefaultMaterials(Color roadColor)
        {
            var m = new Materials
            {
                Road = MakeMat(roadColor),
                Shoulder = _defShoulder != null ? _defShoulder : (_defShoulder = MakeMat(new Color(0.62f, 0.56f, 0.42f))),
                Skirt = _defSkirt != null ? _defSkirt : (_defSkirt = MakeMat(new Color(0.30f, 0.28f, 0.26f))),
            };
            return m;
        }

        static Material MakeMat(Color c)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var m = new Material(shader) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }

        /// <summary>
        /// Çizgi + yol yüzeyi içeren (pasif) bir parça nesnesi kurar. <paramref name="roadRanges"/> yüzeyin bulunduğu
        /// yerel mesafe aralıklarıdır (boşluklar dışarıda kalır). Dönen nesne pasiftir (prefab şablonu gibi kullanılır).
        /// </summary>
        public static GameObject CreatePiece(string name, BezierKnot[] knots, int laneCount, IList<Vector2> roadRanges, Materials mats)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            var container = go.AddComponent<SplineContainer>();
            var spline = container.Spline;
            spline.Clear();
            bool linear = math.lengthsq(knots[0].TangentOut) == 0f;
            foreach (var kn in knots) spline.Add(kn, linear ? TangentMode.Linear : TangentMode.Broken);

            float length = spline.GetLength();
            var ranges = roadRanges ?? new[] { new Vector2(0f, length) };
            var mesh = BuildSurfaceMesh(spline, length, laneCount, ranges);
            mesh.name = name + "_Road";

            var roadGo = new GameObject("Road");
            roadGo.layer = TrackLayers.Road;
            roadGo.transform.SetParent(go.transform, false);
            roadGo.AddComponent<MeshFilter>().sharedMesh = mesh;
            roadGo.AddComponent<MeshRenderer>().sharedMaterials = new[] { mats.Road, mats.Shoulder, mats.Skirt };
            var col = roadGo.AddComponent<MeshCollider>();
            col.sharedMesh = mesh;
            return go;
        }

        public static GameObject CreateStraight(string name, float length, int laneCount, Materials mats) =>
            CreatePiece(name, StraightKnots(length), laneCount, null, mats);

        public static GameObject CreateCorner(string name, float radius, bool right, Materials mats) =>
            CreatePiece(name, ArcKnots(radius, right), 5, null, mats);

        public static GameObject CreateRamp(string name, Materials mats) =>
            CreatePiece(name, ProfileKnots(RampLength, RampProfile), 5,
                new[] { new Vector2(0f, RampTakeoffArc), new Vector2(RampGapEndArc, float.MaxValue) }, mats);

        public static GameObject CreateBump(string name, Materials mats) =>
            CreatePiece(name, ProfileKnots(BumpLength, BumpProfile), 5, null, mats);

        // ---- yüzey meshi ------------------------------------------------------------

        sealed class MeshAccum
        {
            public readonly List<Vector3> Verts = new List<Vector3>();
            public readonly List<int>[] Tris = { new List<int>(), new List<int>(), new List<int>() };

            /// <summary>Dörtgen a-b-c-d (çevre sırası), ön yüz normali <paramref name="normal"/> yönünde.</summary>
            public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
            {
                int i = Verts.Count;
                Verts.Add(a); Verts.Add(b); Verts.Add(c); Verts.Add(d);
                Tri(sub, i, i + 1, i + 2, normal);
                Tri(sub, i, i + 2, i + 3, normal);
            }

            void Tri(int sub, int i0, int i1, int i2, Vector3 normal)
            {
                var n = Vector3.Cross(Verts[i1] - Verts[i0], Verts[i2] - Verts[i0]);
                if (Vector3.Dot(n, normal) >= 0f) { Tris[sub].Add(i0); Tris[sub].Add(i1); Tris[sub].Add(i2); }
                else { Tris[sub].Add(i0); Tris[sub].Add(i2); Tris[sub].Add(i1); }
            }
        }

        static Mesh BuildSurfaceMesh(Spline spline, float length, int laneCount, IList<Vector2> ranges)
        {
            var acc = new MeshAccum();
            float floor = -GroundDrop - 0.05f;
            foreach (var r in ranges)
            {
                float from = Mathf.Max(0f, r.x), to = Mathf.Min(length, r.y);
                int segs = Mathf.Max(2, Mathf.CeilToInt((to - from) / SampleStep));
                var c = new Vector3[segs + 1];
                var rt = new Vector3[segs + 1];
                var hw = new float[segs + 1];
                for (int i = 0; i <= segs; i++)
                {
                    float d = Mathf.Lerp(from, to, i / (float)segs);
                    c[i] = Sample(spline, d, length);
                    hw[i] = HalfWidthAt(laneCount, d, length);
                }
                for (int i = 0; i <= segs; i++)
                {
                    // Gerçek teğet (parça ekleri dik kesitle buluşsun); doğrusal knot'ta teğet sıfırsa kirişten.
                    Vector3 f = Tangent(spline, Mathf.Lerp(from, to, i / (float)segs), length);
                    f.y = 0f;
                    if (f.sqrMagnitude < 1e-8f) { f = i < segs ? c[i + 1] - c[i] : c[i] - c[i - 1]; f.y = 0f; }
                    f.Normalize();
                    rt[i] = Vector3.Cross(Vector3.up, f);
                }
                for (int i = 0; i < segs; i++)
                {
                    int j = i + 1;
                    Vector3 L0 = c[i] - rt[i] * hw[i], L1 = c[j] - rt[j] * hw[j];
                    Vector3 R0 = c[i] + rt[i] * hw[i], R1 = c[j] + rt[j] * hw[j];
                    Vector3 LO0 = c[i] - rt[i] * (hw[i] + ShoulderWidth), LO1 = c[j] - rt[j] * (hw[j] + ShoulderWidth);
                    Vector3 RO0 = c[i] + rt[i] * (hw[i] + ShoulderWidth), RO1 = c[j] + rt[j] * (hw[j] + ShoulderWidth);
                    acc.Quad(0, L0, L1, R1, R0, Vector3.up);
                    acc.Quad(1, LO0, LO1, L1, L0, Vector3.up);
                    acc.Quad(1, R0, R1, RO1, RO0, Vector3.up);
                    // yan etekler: yol kenarından Ground'un altına kadar
                    acc.Quad(2, RO0, RO1, Floor(RO1, floor), Floor(RO0, floor), rt[i]);
                    acc.Quad(2, LO0, LO1, Floor(LO1, floor), Floor(LO0, floor), -rt[i]);
                }
                // boşluğa bakan uçlarda kapak (kalkış kenarı / iniş yüzeyinin ön yüzü)
                if (from > 0.01f) Cap(acc, c[0], rt[0], hw[0], floor, -Flat(c[1] - c[0]));
                if (to < length - 0.01f) Cap(acc, c[segs], rt[segs], hw[segs], floor, Flat(c[segs] - c[segs - 1]));
            }

            var mesh = new Mesh();
            mesh.indexFormat = acc.Verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(acc.Verts);
            mesh.subMeshCount = 3;
            for (int s = 0; s < 3; s++) mesh.SetTriangles(acc.Tris[s], s);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static void Cap(MeshAccum acc, Vector3 c, Vector3 rt, float hw, float floor, Vector3 outward)
        {
            Vector3 a = c - rt * (hw + ShoulderWidth), b = c + rt * (hw + ShoulderWidth);
            acc.Quad(2, a, b, Floor(b, floor), Floor(a, floor), outward);
        }

        static Vector3 Floor(Vector3 p, float floor) => new Vector3(p.x, floor, p.z);
        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.normalized; }

        static Vector3 Tangent(Spline spline, float d, float length)
        {
            float t = spline.ConvertIndexUnit(Mathf.Clamp(d, 0f, length), PathIndexUnit.Distance, PathIndexUnit.Normalized);
            spline.Evaluate(Mathf.Clamp01(t), out _, out float3 tan, out _);
            if (math.lengthsq(tan) < 1e-10f)
            {
                spline.Evaluate(Mathf.Clamp01(t - 0.002f), out float3 a, out _, out _);
                spline.Evaluate(Mathf.Clamp01(t + 0.002f), out float3 b, out _, out _);
                tan = b - a;
            }
            return (Vector3)tan;
        }

        static Vector3 Sample(Spline spline, float d, float length)
        {
            float t = spline.ConvertIndexUnit(Mathf.Clamp(d, 0f, length), PathIndexUnit.Distance, PathIndexUnit.Normalized);
            spline.Evaluate(Mathf.Clamp01(t), out float3 p, out _, out _);
            return p;
        }
    }
}
