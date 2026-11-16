using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

namespace ApexBrake.Track
{
    public sealed class TrackPath
    {
        readonly struct Segment
        {
            public readonly SplineContainer Container;
            public readonly float Start;
            public readonly float Length;
            public Segment(SplineContainer c, float start, float length) { Container = c; Start = start; Length = length; }
        }

        readonly List<Segment> _segments = new List<Segment>();

        public float TotalLength { get; private set; }

        internal void Add(SplineContainer container, float length)
        {
            _segments.Add(new Segment(container, TotalLength, length));
            TotalLength += length;
        }

        public void Evaluate(float distance, out Vector3 position, out Quaternion rotation)
        {
            if (_segments.Count == 0) { position = Vector3.zero; rotation = Quaternion.identity; return; }
            distance = Mathf.Clamp(distance, 0f, TotalLength);
            int lo = 0, hi = _segments.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (_segments[mid].Start <= distance) lo = mid; else hi = mid - 1;
            }
            var seg = _segments[lo];
            float local = Mathf.Clamp(distance - seg.Start, 0f, seg.Length);
            var spline = seg.Container.Splines[0];
            float t = spline.ConvertIndexUnit(local, PathIndexUnit.Distance, PathIndexUnit.Normalized);
            EvaluateWorld(seg.Container, t, out position, out Vector3 fwd, out Vector3 up);
            rotation = fwd.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(fwd, up) : Quaternion.identity;
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
