using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.TestTools;
using Unity.Mathematics;
using ApexBrake.Simulation;
using ApexBrake.Track;

namespace ApexBrake.Tests.PlayMode
{
    public sealed class TrackBuilderTests
    {
        readonly List<UnityEngine.Object> _cleanup = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _cleanup) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            _cleanup.Clear();
        }

        GameObject MakeStraightPrefab(float length)
        {
            var go = new GameObject("Straight" + length);
            go.SetActive(false);
            var sc = go.AddComponent<SplineContainer>();
            var spline = sc.Spline;
            spline.Clear();
            spline.Add(new BezierKnot(new float3(0, 0, 0)), TangentMode.Linear);
            spline.Add(new BezierKnot(new float3(0, 0, length)), TangentMode.Linear);
            _cleanup.Add(go);
            return go;
        }

        GameObject MakeCornerPrefab(float radius, bool right)
        {
            var go = new GameObject("Corner" + radius);
            go.SetActive(false);
            var sc = go.AddComponent<SplineContainer>();
            var spline = sc.Spline;
            spline.Clear();
            float k = 0.5523f * radius;
            float side = right ? 1f : -1f;
            spline.Add(new BezierKnot(new float3(0, 0, 0), new float3(0, 0, -k), new float3(0, 0, k), quaternion.identity), TangentMode.Broken);
            var rot = Quaternion.LookRotation(new Vector3(side, 0, 0));
            spline.Add(new BezierKnot(new float3(side * radius, 0, radius), new float3(0, 0, -k), new float3(0, 0, k), rot), TangentMode.Broken);
            _cleanup.Add(go);
            return go;
        }

        TrackPieceAsset Asset(PieceKind kind, GameObject prefab, float radius = 0f, float safe = 0f)
        {
            var a = ScriptableObject.CreateInstance<TrackPieceAsset>();
            a.kind = kind; a.prefab = prefab; a.radius = radius;
            if (kind == PieceKind.Corner) a.turnSign = 1; // Faz 2b: virajda TurnSign zorunlu
            _cleanup.Add(a);
            return a;
        }

        TrackBuilder MakeBuilder(params TrackPieceAsset[] pieces)
        {
            var recipe = ScriptableObject.CreateInstance<LevelRecipe>();
            recipe.pieces = new List<TrackPieceAsset>(pieces);
            _cleanup.Add(recipe);
            var go = new GameObject("Builder");
            go.transform.position = new Vector3(3f, 0f, -7f);
            go.transform.rotation = Quaternion.Euler(0, 30f, 0);
            _cleanup.Add(go);
            var b = go.AddComponent<TrackBuilder>();
            b.recipe = recipe;
            return b;
        }

        TrackPieceAsset[] Standard()
        {
            var s = MakeStraightPrefab(50f);
            var c = MakeCornerPrefab(25f, true);
            return new[] { Asset(PieceKind.Straight, s), Asset(PieceKind.Corner, c, 25f), Asset(PieceKind.Straight, s) };
        }

        static float SplineLength(GameObject prefab) => prefab.GetComponent<SplineContainer>().Spline.GetLength();

        [UnityTest]
        public IEnumerator Build_TotalLength_MatchesSumOfSplineLengths()
        {
            var pieces = Standard();
            var b = MakeBuilder(pieces);
            b.Build();
            float sum = 0f;
            foreach (var p in pieces) sum += SplineLength(p.prefab);
            Assert.AreEqual(sum, b.Layout.TotalLength, 0.05f);
            Assert.AreEqual(sum, b.Path.TotalLength, 0.05f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Build_PiecesAreContinuous()
        {
            var b = MakeBuilder(Standard());
            b.Build();
            for (int i = 1; i < b.Layout.PieceCount; i++)
            {
                float d = b.Layout.SpanAt(i).Start;
                b.Path.Evaluate(d - 0.01f, out var p0, out _);
                b.Path.Evaluate(d + 0.01f, out var p1, out _);
                Assert.Less(Vector3.Distance(p0, p1), 0.05f, "boundary " + i);
                b.Path.Evaluate(d - 0.01f, out _, out var r0);
                b.Path.Evaluate(d + 0.01f, out _, out var r1);
                Assert.Less(Vector3.Angle(r0 * Vector3.forward, r1 * Vector3.forward), 1f, "direction at boundary " + i);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Build_CornerExit_RotatesNextPiece()
        {
            var c = MakeCornerPrefab(25f, true);
            var s = MakeStraightPrefab(50f);
            var b = MakeBuilder(Asset(PieceKind.Straight, s), Asset(PieceKind.Corner, c, 25f), Asset(PieceKind.Straight, s));
            b.Build();
            b.Path.Evaluate(0f, out _, out var startRot);
            var last = b.Layout.SpanAt(2);
            b.Path.Evaluate((last.Start + last.End) * 0.5f, out _, out var midRot);
            float angle = Vector3.Angle(startRot * Vector3.forward, midRot * Vector3.forward);
            Assert.AreEqual(90f, angle, 1f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Evaluate_AtZero_IsBuilderOrigin_FacingForward()
        {
            var b = MakeBuilder(Standard());
            b.Build();
            b.Path.Evaluate(0f, out var pos, out var rot);
            Assert.Less(Vector3.Distance(pos, b.transform.position), 0.01f);
            Assert.Less(Vector3.Angle(rot * Vector3.forward, b.transform.forward), 0.5f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Layout_KindsMatchRecipeOrder()
        {
            var pieces = Standard();
            var b = MakeBuilder(pieces);
            b.Build();
            Assert.AreEqual(pieces.Length, b.Layout.PieceCount);
            for (int i = 0; i < pieces.Length; i++) Assert.AreEqual(pieces[i].kind, b.Layout.SpanAt(i).Def.Kind);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Build_Twice_DoesNotDuplicateChildren()
        {
            var pieces = Standard();
            var b = MakeBuilder(pieces);
            b.Build();
            b.Build();
            Assert.AreEqual(pieces.Length + 1, b.transform.childCount); // parçalar + Ground
            yield return null;
        }

        [UnityTest]
        public IEnumerator Build_CopiesLaneCountAndTurnSign()
        {
            var s = MakeStraightPrefab(50f);
            var c = MakeCornerPrefab(25f, false);
            var wide = Asset(PieceKind.Straight, s);
            var narrow = Asset(PieceKind.Straight, s);
            narrow.laneCount = 3;
            var corner = Asset(PieceKind.Corner, c, 25f);
            corner.turnSign = -1;
            var b = MakeBuilder(wide, narrow, corner, Asset(PieceKind.Straight, s));
            b.Build();
            Assert.AreEqual(5, b.Layout.SpanAt(0).Def.LaneCount);
            Assert.AreEqual(3, b.Layout.SpanAt(1).Def.LaneCount);
            Assert.AreEqual(0, b.Layout.SpanAt(1).Def.TurnSign);
            Assert.AreEqual(5, b.Layout.SpanAt(2).Def.LaneCount);
            Assert.AreEqual(-1, b.Layout.SpanAt(2).Def.TurnSign);
            Assert.AreEqual(0, b.Layout.SpanAt(3).Def.TurnSign);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Build_InvalidRecipe_Throws()
        {
            var s = MakeStraightPrefab(50f);
            var c = MakeCornerPrefab(25f, true);
            var b = MakeBuilder(Asset(PieceKind.Corner, c, 25f), Asset(PieceKind.Straight, s));
            Assert.Throws<ArgumentException>(() => b.Build());
            yield return null;
        }
    }
}
