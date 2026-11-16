using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using ApexBrake.Simulation;

namespace ApexBrake.Track
{
    public sealed class TrackBuilder : MonoBehaviour
    {
        public LevelRecipe recipe;
        [Tooltip("Yol dışı zemin görünümü (boşsa çalışma anında düz renk)")]
        public Material groundMaterial;
        [Tooltip("Zeminin pistin dış sınırlarından taşma payı (m)")]
        public float groundMargin = 150f;

        public TrackLayout Layout { get; private set; }
        public TrackPath Path { get; private set; }
        public GameObject Ground { get; private set; }

        /// <summary>Çocukları temizler, parçaları uç uca dizer, Layout ve Path üretir, altına Ground zeminini kurar.
        /// Geçersiz tarifte ArgumentException fırlatır (yalnızca üretilen çocukları temizleyip yeniden fırlatır).</summary>
        public void Build()
        {
            Clear();
            Layout = null;
            Path = null;
            Ground = null;
            if (recipe == null || recipe.pieces == null)
                throw new ArgumentException("Recipe rule: recipe and its piece list must be set.");

            var defs = new List<TrackPieceDef>();
            var path = new TrackPath();
            Vector3 pos = transform.position;
            Quaternion rot = transform.rotation;

            try
            {
                for (int i = 0; i < recipe.pieces.Count; i++)
                {
                    var asset = recipe.pieces[i];
                    if (asset == null || asset.prefab == null)
                        throw new ArgumentException($"Recipe rule: piece {i} and its prefab must be set.");

                    var go = Instantiate(asset.prefab, pos, rot, transform);
                    go.SetActive(true);
                    go.name = $"{i:00}_{asset.name}";
                    var container = go.GetComponent<SplineContainer>();
                    if (container == null)
                        throw new ArgumentException($"Recipe rule: prefab of piece {i} needs a SplineContainer.");

                    foreach (var col in go.GetComponentsInChildren<Collider>(true))
                    {
                        col.gameObject.layer = TrackLayers.Road;
                        col.sharedMaterial = TrackLayers.RoadMaterial;
                    }

                    float length = container.Splines[0].GetLength();
                    defs.Add(asset.ToDef(length));
                    path.Add(container, length, asset.laneCount);

                    // Çıkış noktası ve yatay yön: sıradaki parça buraya hizalanır.
                    TrackPath.EvaluateWorld(container, 1f, out Vector3 endPos, out Vector3 endTan, out Vector3 endUp);
                    pos = endPos;
                    var flat = new Vector3(endTan.x, 0f, endTan.z);
                    if (flat.sqrMagnitude > 1e-8f) rot = Quaternion.LookRotation(flat, Vector3.up);
                }

                Layout = new TrackLayout(defs);
                path.Finish();
                Path = path;
                BuildGround();
                Physics.SyncTransforms();
            }
            catch
            {
                Clear();
                throw;
            }
        }

        void BuildGround()
        {
            var min = new Vector3(float.MaxValue, 0f, float.MaxValue);
            var max = new Vector3(float.MinValue, 0f, float.MinValue);
            for (float d = 0f; d <= Path.TotalLength; d += 2f)
            {
                var p = Path.PointAt(d, 0f);
                min = Vector3.Min(min, p); max = Vector3.Max(max, p);
            }
            min -= new Vector3(groundMargin, 0f, groundMargin);
            max += new Vector3(groundMargin, 0f, groundMargin);

            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = "Ground";
            g.layer = TrackLayers.Ground;
            g.transform.SetParent(transform, false);
            const float thickness = 2f;
            float top = transform.position.y - TrackPieceGeometry.GroundDrop;
            g.transform.SetPositionAndRotation(new Vector3((min.x + max.x) * 0.5f, top - thickness * 0.5f, (min.z + max.z) * 0.5f), Quaternion.identity);
            g.transform.localScale = new Vector3(max.x - min.x, thickness, max.z - min.z);
            var col = g.GetComponent<Collider>();
            col.sharedMaterial = TrackLayers.GroundMaterial;
            var mr = g.GetComponent<MeshRenderer>();
            if (groundMaterial != null) mr.sharedMaterial = groundMaterial;
            else
            {
                if (_defaultGround == null)
                {
                    var c = new Color(0.36f, 0.45f, 0.30f);
                    _defaultGround = new Material(mr.sharedMaterial) { name = "Ground_Runtime", color = c };
                    if (_defaultGround.HasProperty("_BaseColor")) _defaultGround.SetColor("_BaseColor", c);
                }
                mr.sharedMaterial = _defaultGround;
            }
            Ground = g;
        }

        static Material _defaultGround;

        void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                DestroyImmediate(transform.GetChild(i).gameObject);
        }
    }
}
