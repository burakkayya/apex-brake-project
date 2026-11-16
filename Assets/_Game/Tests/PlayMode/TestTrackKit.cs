using System;
using System.Collections.Generic;
using UnityEngine;
using ApexBrake.Simulation;
using ApexBrake.Track;

namespace ApexBrake.Tests.PlayMode
{
    /// <summary>Testler için kodla küçük pistler kurar (gerçek çarpışma yüzeyli parçalar; TrackPieceGeometry ile).</summary>
    internal sealed class TestTrackKit : IDisposable
    {
        readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();
        readonly TrackPieceGeometry.Materials _mats = TrackPieceGeometry.DefaultMaterials(new Color(0.35f, 0.35f, 0.37f));

        public T Track<T>(T o) where T : UnityEngine.Object { _objects.Add(o); return o; }

        TrackPieceAsset Asset(PieceKind kind, GameObject prefab)
        {
            Track(prefab);
            var a = Track(ScriptableObject.CreateInstance<TrackPieceAsset>());
            a.name = prefab.name;
            a.kind = kind;
            a.prefab = prefab;
            return a;
        }

        public TrackPieceAsset Straight(float length, int lanes = 5)
        {
            var a = Asset(PieceKind.Straight, TrackPieceGeometry.CreateStraight("Straight" + length, length, lanes, _mats));
            a.laneCount = lanes;
            return a;
        }

        public TrackPieceAsset Corner(float radius, bool right)
        {
            var a = Asset(PieceKind.Corner, TrackPieceGeometry.CreateCorner((right ? "CornerR" : "CornerL") + radius, radius, right, _mats));
            a.radius = radius;
            a.turnSign = right ? 1 : -1;
            return a;
        }

        public TrackPieceAsset Ramp()
        {
            var a = Asset(PieceKind.Ramp, TrackPieceGeometry.CreateRamp("Ramp", _mats));
            a.gapLength = TrackPieceGeometry.RampGap;
            return a;
        }

        public TrackPieceAsset Bump() => Asset(PieceKind.Bump, TrackPieceGeometry.CreateBump("Bump", _mats));

        public TrackBuilder Build(Vector3 origin, float yaw, params TrackPieceAsset[] pieces)
        {
            var recipe = Track(ScriptableObject.CreateInstance<LevelRecipe>());
            recipe.pieces = new List<TrackPieceAsset>(pieces);
            var go = Track(new GameObject("TestTrack"));
            go.transform.SetPositionAndRotation(origin, Quaternion.Euler(0f, yaw, 0f));
            var b = go.AddComponent<TrackBuilder>();
            b.recipe = recipe;
            b.Build();
            return b;
        }

        public TrackBuilder Build(params TrackPieceAsset[] pieces) => Build(Vector3.zero, 0f, pieces);

        public void Dispose()
        {
            for (int i = _objects.Count - 1; i >= 0; i--)
                if (_objects[i] != null) UnityEngine.Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
        }

        /// <summary>Yukarıdan aşağı ışın: ilk çarpılan yüzey (Road ya da Ground).</summary>
        public static bool RayDown(Vector3 at, out RaycastHit hit, int mask = TrackLayers.SurfaceMask) =>
            Physics.Raycast(at + Vector3.up * 20f, Vector3.down, out hit, 60f, mask, QueryTriggerInteraction.Ignore);
    }
}
