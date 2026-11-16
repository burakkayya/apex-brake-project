using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

namespace ApexBrake.Track
{
    /// <summary>Bir dünya noktasının pist üzerindeki izdüşümü.</summary>
    public struct TrackPose
    {
        public float Distance;          // pist başından çizgi boyunca (m)
        public float Lateral;           // çizgiden yatay sapma (m), + sağ
        public float HeightAboveRoad;   // çizginin (yol yüzeyinin; boşlukta sanal yüzeyin) üstündeki yükseklik
        public Vector3 Forward;         // çizginin ileri yönü (eğim dahil, birim)
        public Vector3 Right;           // yatay sağ (birim)
        public float HalfWidth;         // şerit yarı genişliği (banket hariç)
    }

    public sealed class TrackPath
    {
        readonly struct Segment
        {
            public readonly SplineContainer Container;
            public readonly float Start;
            public readonly float Length;
            public readonly int LaneCount;
            public Segment(SplineContainer c, float start, float length, int laneCount) { Container = c; Start = start; Length = length; LaneCount = laneCount; }
        }

        public const float TableStep = 0.25f;     // izdüşüm tablosu aralığı (m)
        public const float SearchWindow = 40f;    // ipucu çevresinde arama (±m)

        readonly List<Segment> _segments = new List<Segment>();
        Vector3[] _pos = new Vector3[0];
        Vector3[] _fwd = new Vector3[0];
        Vector3[] _right = new Vector3[0];
        float[] _half = new float[0];
        float[] _heading = new float[0];   // yatay yön açısı (radyan, sarılmamış); sağa dönüşte artar

        public float TotalLength { get; private set; }

        internal void Add(SplineContainer container, float length, int laneCount = 5)
        {
            _segments.Add(new Segment(container, TotalLength, length, laneCount));
            TotalLength += length;
        }

        /// <summary>Tüm parçalar eklendikten sonra izdüşüm tablosunu kurar.</summary>
        internal void Finish()
        {
            int n = Mathf.Max(2, Mathf.CeilToInt(TotalLength / TableStep) + 1);
            _pos = new Vector3[n]; _fwd = new Vector3[n]; _right = new Vector3[n]; _half = new float[n]; _heading = new float[n];
            for (int i = 0; i < n; i++)
            {
                float d = Mathf.Min(TotalLength, i * TableStep);
                Evaluate(d, out _pos[i], out Quaternion rot);
                _fwd[i] = rot * Vector3.forward;
                var flat = new Vector3(_fwd[i].x, 0f, _fwd[i].z);
                if (flat.sqrMagnitude < 1e-8f) flat = i > 0 ? new Vector3(_fwd[i - 1].x, 0f, _fwd[i - 1].z) : Vector3.forward;
                flat.Normalize();
                _right[i] = Vector3.Cross(Vector3.up, flat);
                var seg = _segments[SegmentIndex(d)];
                _half[i] = TrackPieceGeometry.HalfWidthAt(seg.LaneCount, d - seg.Start, seg.Length);
                float h = Mathf.Atan2(flat.x, flat.z);
                if (i > 0) h = _heading[i - 1] + Mathf.DeltaAngle(_heading[i - 1] * Mathf.Rad2Deg, h * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                _heading[i] = h;
            }
        }

        int SegmentIndex(float distance)
        {
            int lo = 0, hi = _segments.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (_segments[mid].Start <= distance) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        public void Evaluate(float distance, out Vector3 position, out Quaternion rotation)
        {
            if (_segments.Count == 0) { position = Vector3.zero; rotation = Quaternion.identity; return; }
            distance = Mathf.Clamp(distance, 0f, TotalLength);
            var seg = _segments[SegmentIndex(distance)];
            float local = Mathf.Clamp(distance - seg.Start, 0f, seg.Length);
            var spline = seg.Container.Splines[0];
            float t = spline.ConvertIndexUnit(local, PathIndexUnit.Distance, PathIndexUnit.Normalized);
            EvaluateWorld(seg.Container, t, out position, out Vector3 fwd, out Vector3 up);
            rotation = fwd.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(fwd, up) : Quaternion.identity;
        }

        // ---- tablo tabanlı sorgular ---------------------------------------------

        void Locate(float distance, out int i, out float t)
        {
            float x = Mathf.Clamp(distance, 0f, TotalLength) / TableStep;
            i = Mathf.Clamp(Mathf.FloorToInt(x), 0, _pos.Length - 2);
            t = Mathf.Clamp01(x - i);
        }

        /// <summary>Yol yüzeyi üzerindeki dünya noktası (çizgiden yatay <paramref name="lateral"/> m sağda).</summary>
        public Vector3 PointAt(float distance, float lateral)
        {
            Locate(distance, out int i, out float t);
            Vector3 c = Vector3.Lerp(_pos[i], _pos[i + 1], t);
            Vector3 r = Vector3.Lerp(_right[i], _right[i + 1], t).normalized;
            return c + r * lateral;
        }

        public Vector3 ForwardAt(float distance)
        {
            Locate(distance, out int i, out float t);
            return Vector3.Lerp(_fwd[i], _fwd[i + 1], t).normalized;
        }

        public Vector3 RightAt(float distance)
        {
            Locate(distance, out int i, out float t);
            return Vector3.Lerp(_right[i], _right[i + 1], t).normalized;
        }

        public float HalfWidthAt(float distance)
        {
            Locate(distance, out int i, out float t);
            return Mathf.Lerp(_half[i], _half[i + 1], t);
        }

        float HeadingAt(float distance)
        {
            Locate(distance, out int i, out float t);
            return Mathf.Lerp(_heading[i], _heading[i + 1], t);
        }

        /// <summary>İşaretli yatay eğrilik (1/m); + sağa dönüş.</summary>
        public float CurvatureAt(float distance)
        {
            const float h = 1f;
            float a = Mathf.Max(0f, distance - h), b = Mathf.Min(TotalLength, distance + h);
            if (b - a < 1e-3f) return 0f;
            return (HeadingAt(b) - HeadingAt(a)) / (b - a);
        }

        /// <summary>Yol yüzeyinin boyuna eğimi (radyan; + yokuş yukarı).</summary>
        public float SlopeAt(float distance)
        {
            const float h = 0.5f;
            float a = Mathf.Max(0f, distance - h), b = Mathf.Min(TotalLength, distance + h);
            if (b - a < 1e-3f) return 0f;
            Vector3 pa = PointAt(a, 0f), pb = PointAt(b, 0f);
            float horiz = new Vector2(pb.x - pa.x, pb.z - pa.z).magnitude;
            return Mathf.Atan2(pb.y - pa.y, Mathf.Max(horiz, 1e-4f));
        }

        /// <summary>
        /// Dünya noktasını pist çizgisine izdüşürür. <paramref name="hintDistance"/> çevresinde (±40 m) arar;
        /// ipucu negatif ya da NaN ise tüm pistte arar. Pistin kendine yaklaştığı yerlerde ipucu doğru bacağı seçtirir.
        /// </summary>
        public TrackPose Project(Vector3 worldPos, float hintDistance)
        {
            int n = _pos.Length;
            if (n < 2) return default;
            int lo = 0, hi = n - 1;
            if (!float.IsNaN(hintDistance) && hintDistance >= 0f)
            {
                lo = Mathf.Clamp(Mathf.FloorToInt((hintDistance - SearchWindow) / TableStep), 0, n - 1);
                hi = Mathf.Clamp(Mathf.CeilToInt((hintDistance + SearchWindow) / TableStep), 0, n - 1);
            }
            int best = lo;
            float bestSq = float.MaxValue;
            for (int i = lo; i <= hi; i++)
            {
                float sq = (worldPos - _pos[i]).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = i; }
            }

            // komşu iki parça üzerinde sürekli izdüşüm
            float bestDist = best * TableStep;
            float bestSeg = float.MaxValue;
            for (int k = -1; k <= 0; k++)
            {
                int a = best + k, b = a + 1;
                if (a < 0 || b >= n) continue;
                Vector3 ab = _pos[b] - _pos[a];
                float t = Mathf.Clamp01(Vector3.Dot(worldPos - _pos[a], ab) / Mathf.Max(ab.sqrMagnitude, 1e-8f));
                float sq = (worldPos - (_pos[a] + ab * t)).sqrMagnitude;
                if (sq < bestSeg) { bestSeg = sq; bestDist = (a + t) * TableStep; }
            }
            bestDist = Mathf.Clamp(bestDist, 0f, TotalLength);

            Vector3 centre = PointAt(bestDist, 0f);
            Vector3 right = RightAt(bestDist);
            Vector3 delta = worldPos - centre;
            return new TrackPose
            {
                Distance = bestDist,
                Lateral = Vector3.Dot(delta, right),
                HeightAboveRoad = delta.y,
                Forward = ForwardAt(bestDist),
                Right = right,
                HalfWidth = HalfWidthAt(bestDist),
            };
        }

        /// <summary>Çizgiyi yerel uzayda değerlendirip dünya uzayına çevirir (konum, ileri yön, yukarı yön).</summary>
        internal static void EvaluateWorld(SplineContainer container, float t, out Vector3 position, out Vector3 forward, out Vector3 up)
        {
            var spline = container.Splines[0];
            spline.Evaluate(t, out float3 p, out float3 tan, out float3 u);
            var tr = container.transform;
            position = tr.TransformPoint((Vector3)p);
            up = tr.TransformDirection((Vector3)u);
            if (math.lengthsq(tan) < 1e-8f)
            {
                // Doğrusal knot'larda tanjant sıfır olabilir; komşu örneklerden yön türet.
                float t0 = Mathf.Clamp01(t - 0.002f), t1 = Mathf.Clamp01(t + 0.002f);
                spline.Evaluate(t0, out float3 a, out _, out _);
                spline.Evaluate(t1, out float3 b, out _, out _);
                tan = b - a;
            }
            forward = tr.TransformDirection(((Vector3)tan).normalized);
        }
    }
}
