using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using ApexBrake.Simulation;

namespace ApexBrake.Track
{
    public sealed class TrackBuilder : MonoBehaviour
    {
        public LevelRecipe recipe;

        public TrackLayout Layout { get; private set; }
        public TrackPath Path { get; private set; }

        /// <summary>Çocukları temizler, parçaları uç uca dizer, Layout ve Path üretir. Geçersiz tarifte ArgumentException fırlatır (yakalamaz; yalnızca üretilen çocukları temizleyip yeniden fırlatır).</summary>
        public void Build()
        {
            Clear();
            Layout = null;
            Path = null;
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

                    float length = container.Splines[0].GetLength();
                    defs.Add(asset.ToDef(length));
                    path.Add(container, length);

                    // Çıkış noktası ve yatay yön: sıradaki parça buraya hizalanır.
                    TrackPath.EvaluateWorld(container, 1f, out Vector3 endPos, out Vector3 endTan, out Vector3 endUp);
                    pos = endPos;
                    var flat = new Vector3(endTan.x, 0f, endTan.z);
                    if (flat.sqrMagnitude > 1e-8f) rot = Quaternion.LookRotation(flat, Vector3.up);
                }

                Layout = new TrackLayout(defs);
                Path = path;
            }
            catch
            {
                Clear();
                throw;
            }
        }

        void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                DestroyImmediate(transform.GetChild(i).gameObject);
        }
    }
}
