using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ApexBrake.Track;

namespace ApexBrake.Tests.PlayMode
{
    public sealed class TrackGeometryTests
    {
        TestTrackKit _kit;

        [SetUp] public void SetUp() => _kit = new TestTrackKit();
        [TearDown] public void TearDown() => _kit.Dispose();

        [UnityTest]
        public IEnumerator Road_HasCollider_OnRoadLayer_FullWidthPlusShoulders()
        {
            var b = _kit.Build(new Vector3(5f, 0f, 3f), 25f, _kit.Straight(50f), _kit.Corner(40f, true), _kit.Straight(50f));
            yield return new WaitForFixedUpdate();
            var path = b.Path;
            foreach (float d in new[] { 25f, 50f + 31f, 120f })
            {
                float hw = path.HalfWidthAt(d);
                Assert.AreEqual(8f, hw, 0.01f);
                foreach (float side in new[] { -1f, 1f })
                {
                    Assert.IsTrue(TestTrackKit.RayDown(path.PointAt(d, side * (hw + 1.4f)), out var hit), $"d={d} side={side}: shoulder");
                    Assert.AreEqual(TrackLayers.Road, hit.collider.gameObject.layer, $"d={d} side={side}: shoulder layer");
                    Assert.IsTrue(TestTrackKit.RayDown(path.PointAt(d, side * (hw + 2.0f)), out hit), $"d={d} side={side}: beyond");
                    Assert.AreEqual(TrackLayers.Ground, hit.collider.gameObject.layer, $"d={d} side={side}: beyond layer");
                }
                Assert.IsTrue(TestTrackKit.RayDown(path.PointAt(d, 0f), out var c));
                Assert.AreEqual(TrackLayers.Road, c.collider.gameObject.layer);
            }
        }

        [UnityTest]
        public IEnumerator Ramp_HasRealGap_NoRoadColliderInside()
        {
            var b = _kit.Build(_kit.Straight(50f), _kit.Ramp(), _kit.Straight(50f));
            yield return new WaitForFixedUpdate();
            float mid = 50f + (TrackPieceGeometry.RampTakeoffArc + TrackPieceGeometry.RampGapEndArc) * 0.5f;
            foreach (float lat in new[] { -8f, 0f, 8f })
            {
                Assert.IsFalse(TestTrackKit.RayDown(b.Path.PointAt(mid, lat), out _, TrackLayers.RoadMask), "road inside gap at lateral " + lat);
                Assert.IsTrue(TestTrackKit.RayDown(b.Path.PointAt(mid, lat), out var g));
                Assert.AreEqual(TrackLayers.Ground, g.collider.gameObject.layer);
            }
            // boşluktan önce ve sonra yol var
            Assert.IsTrue(TestTrackKit.RayDown(b.Path.PointAt(50f + TrackPieceGeometry.RampTakeoffArc - 0.5f, 0f), out _, TrackLayers.RoadMask));
            Assert.IsTrue(TestTrackKit.RayDown(b.Path.PointAt(50f + TrackPieceGeometry.RampGapEndArc + 0.5f, 0f), out _, TrackLayers.RoadMask));
        }

        [UnityTest]
        public IEnumerator Surface_IsContinuous_NoWallOrHoleAtSeams()
        {
            var b = _kit.Build(_kit.Straight(30f), _kit.Ramp(), _kit.Straight(30f), _kit.Bump(), _kit.Corner(25f, true), _kit.Straight(30f, 3), _kit.Straight(30f));
            yield return new WaitForFixedUpdate();
            var path = b.Path;
            float gap0 = 30f + TrackPieceGeometry.RampTakeoffArc, gap1 = 30f + TrackPieceGeometry.RampGapEndArc;
            for (float d = 0.1f; d < path.TotalLength - 0.1f; d += 0.1f)
            {
                if (d > gap0 - 0.05f && d < gap1 + 0.05f) continue;
                foreach (float lat in new[] { -6f, 0f, 6f })
                {
                    var p = path.PointAt(d, lat);
                    Assert.IsTrue(TestTrackKit.RayDown(p, out var hit, TrackLayers.RoadMask), $"hole at d={d:F1} lat={lat}");
                    Assert.AreEqual(p.y, hit.point.y, 0.03f, $"step at d={d:F1} lat={lat}");
                    // 10 cm yukarıdan ileri doğru 1 m: yüzeyde duvar/kapak yok
                    var fwd = path.ForwardAt(d);
                    Assert.IsFalse(Physics.Raycast(p + Vector3.up * 0.1f, fwd, 0.6f, TrackLayers.RoadMask), $"wall at d={d:F1} lat={lat}");
                }
            }
        }

        [UnityTest]
        public IEnumerator Ramp_SurfaceRisesBeforeGap()
        {
            var b = _kit.Build(_kit.Straight(50f), _kit.Ramp(), _kit.Straight(50f));
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(TestTrackKit.RayDown(b.Path.PointAt(50f + TrackPieceGeometry.RampTakeoffArc - 0.3f, 0f), out var hit, TrackLayers.RoadMask));
            Assert.GreaterOrEqual(hit.point.y, 1.2f);
            Assert.IsTrue(TestTrackKit.RayDown(b.Path.PointAt(30f, 0f), out var flat, TrackLayers.RoadMask));
            Assert.AreEqual(0f, flat.point.y, 0.02f);
            // kalkış açısı 12–15° civarı
            float slopeDeg = b.Path.SlopeAt(50f + TrackPieceGeometry.RampTakeoffArc - 0.6f) * Mathf.Rad2Deg;
            Assert.That(slopeDeg, Is.InRange(11f, 17f));
        }

        [UnityTest]
        public IEnumerator Bump_SurfaceIsRaised()
        {
            var b = _kit.Build(_kit.Straight(50f), _kit.Bump(), _kit.Straight(50f));
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(TestTrackKit.RayDown(b.Path.PointAt(50f + TrackPieceGeometry.BumpLength * 0.5f, 3f), out var hit, TrackLayers.RoadMask));
            Assert.GreaterOrEqual(hit.point.y, 0.4f);
        }

        [UnityTest]
        public IEnumerator Project_OnCentreLine_ReturnsDistanceAndZeroLateral()
        {
            var b = _kit.Build(new Vector3(-4f, 0f, 7f), -40f, _kit.Straight(50f), _kit.Corner(40f, true), _kit.Straight(30f), _kit.Corner(25f, false), _kit.Straight(50f));
            yield return null;
            var path = b.Path;
            for (float d = 1f; d < path.TotalLength - 1f; d += 3.7f)
            {
                var pose = path.Project(path.PointAt(d, 0f), d);
                Assert.AreEqual(d, pose.Distance, 0.3f, "distance at " + d);
                Assert.AreEqual(0f, pose.Lateral, 0.1f, "lateral at " + d);
                var global = path.Project(path.PointAt(d, 0f), -1f);
                Assert.AreEqual(d, global.Distance, 0.3f, "global distance at " + d);
            }
        }

        [UnityTest]
        public IEnumerator Project_Lateral_SignIsRightPositive()
        {
            var b = _kit.Build(_kit.Straight(100f));
            yield return null;
            // pist +Z yönünde; sağ = +X
            var pose = b.Path.Project(new Vector3(2.5f, 0.3f, 40f), 0f);
            Assert.AreEqual(2.5f, pose.Lateral, 0.05f);
            Assert.AreEqual(40f, pose.Distance, 0.1f);
            Assert.AreEqual(0.3f, pose.HeightAboveRoad, 0.05f);
            Assert.Greater(Vector3.Dot(pose.Right, Vector3.right), 0.99f);
            Assert.Greater(b.Path.PointAt(40f, 3f).x, 2.9f);
            Assert.AreEqual(-3f, b.Path.Project(new Vector3(-3f, 0f, 60f), 50f).Lateral, 0.05f);
        }

        [UnityTest]
        public IEnumerator Project_InsideCorner_IsContinuous()
        {
            var b = _kit.Build(_kit.Straight(30f), _kit.Corner(25f, true), _kit.Straight(30f));
            yield return null;
            var path = b.Path;
            foreach (float lat in new[] { 7f, -9f })
            {
                float prev = -1f, hint = 0f;
                for (float d = 25f; d < 75f; d += 1f)
                {
                    var pose = path.Project(path.PointAt(d, lat), hint);
                    Assert.Greater(pose.Distance, prev, $"monotonic at d={d} lat={lat}");
                    Assert.AreEqual(d, pose.Distance, 0.5f, $"distance at d={d} lat={lat}");
                    Assert.AreEqual(lat, pose.Lateral, 0.15f, $"lateral at d={d} lat={lat}");
                    prev = pose.Distance; hint = pose.Distance;
                }
            }
        }

        [UnityTest]
        public IEnumerator Project_UTurn_DoesNotJumpToOtherLeg()
        {
            var b = _kit.Build(_kit.Straight(60f), _kit.Corner(25f, true), _kit.Corner(25f, true), _kit.Straight(60f));
            yield return null;
            var path = b.Path;
            // Dönüş bacağında, öbür bacağa daha yakın bir nokta (sağ = ilk bacağa doğru)
            float d2 = path.TotalLength - 40f;
            var p = path.PointAt(d2, 28f);
            var first = path.Project(p, -1f);
            Assert.Less(first.Distance, 60f, "precondition: global nearest is the first leg");
            var pose = path.Project(p, d2);
            Assert.AreEqual(d2, pose.Distance, 0.5f);
            Assert.AreEqual(28f, pose.Lateral, 0.2f);
            // Bacak boyunca adım adım ilerlerken geri sıçramaz
            float prev = d2 - 1f;
            for (float d = d2; d < path.TotalLength - 1f; d += 1f)
            {
                var q = path.Project(path.PointAt(d, 28f), prev);
                Assert.Greater(q.Distance, prev - 0.01f);
                prev = q.Distance;
            }
        }

        [UnityTest]
        public IEnumerator Curvature_StraightIsZero_RightCornerPositive()
        {
            var b = _kit.Build(_kit.Straight(50f), _kit.Corner(40f, true), _kit.Straight(50f), _kit.Corner(40f, false), _kit.Straight(50f));
            yield return null;
            var path = b.Path;
            float arc = b.Layout.SpanAt(1).End - b.Layout.SpanAt(1).Start;
            Assert.AreEqual(0f, path.CurvatureAt(25f), 0.002f);
            Assert.AreEqual(0.025f, path.CurvatureAt(50f + arc * 0.5f), 0.003f);
            Assert.AreEqual(-0.025f, path.CurvatureAt(b.Layout.SpanAt(3).Start + arc * 0.5f), 0.003f);
            Assert.AreEqual(0f, path.CurvatureAt(b.Layout.SpanAt(2).Start + 25f), 0.002f);
        }

        [UnityTest]
        public IEnumerator Slope_RampApproachIsPositive()
        {
            var b = _kit.Build(_kit.Straight(50f), _kit.Ramp(), _kit.Straight(50f));
            yield return null;
            Assert.AreEqual(0f, b.Path.SlopeAt(25f), 0.01f);
            Assert.Greater(b.Path.SlopeAt(50f + TrackPieceGeometry.RampApproach + TrackPieceGeometry.RampRise * 0.6f), 0.08f);
            Assert.Less(b.Path.SlopeAt(50f + TrackPieceGeometry.RampGapEndArc + 2f), 0f, "landing slopes down");
        }

        [UnityTest]
        public IEnumerator Ground_ExistsBelowAndBeyondRoad()
        {
            var b = _kit.Build(_kit.Straight(50f), _kit.Corner(40f, true), _kit.Straight(50f));
            yield return new WaitForFixedUpdate();
            Assert.IsNotNull(b.Ground);
            Assert.AreEqual(TrackLayers.Ground, b.Ground.layer);
            foreach (var p in new[] { b.Path.PointAt(30f, 30f), b.Path.PointAt(30f, -60f), b.Path.PointAt(0f, 0f) - b.Path.ForwardAt(0f) * 60f,
                                       b.Path.PointAt(b.Path.TotalLength, 0f) + b.Path.ForwardAt(b.Path.TotalLength) * 60f })
            {
                Assert.IsTrue(TestTrackKit.RayDown(p, out var hit), "ground at " + p);
                Assert.AreEqual(TrackLayers.Ground, hit.collider.gameObject.layer);
                Assert.AreEqual(-TrackPieceGeometry.GroundDrop, hit.point.y, 0.02f);
            }
            // yolun altında da zemin var (yoldan aşağı ışın yolu geçince zemine çarpar)
            Assert.IsTrue(Physics.Raycast(b.Path.PointAt(20f, 0f) + Vector3.down * 0.1f, Vector3.down, out var below, 5f, TrackLayers.GroundMask));
            Assert.AreEqual(-TrackPieceGeometry.GroundDrop, below.point.y, 0.02f);
            Assert.AreEqual(TrackLayers.GroundFriction, b.Ground.GetComponent<Collider>().sharedMaterial.dynamicFriction, 1e-4f);
        }
    }
}
