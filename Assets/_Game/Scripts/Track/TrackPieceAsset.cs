using UnityEngine;
using ApexBrake.Simulation;

namespace ApexBrake.Track
{
    /// <summary>Parça verisi: yalnızca geometri (tür, yarıçap, dönüş yönü, şerit sayısı, boşluk). Hız eşiği yoktur.</summary>
    [CreateAssetMenu(menuName = "Apex Brake/Track Piece")]
    public sealed class TrackPieceAsset : ScriptableObject
    {
        public PieceKind kind;
        public GameObject prefab;       // kökte SplineContainer; çizgi yerel (0,0,0)'dan +Z yönünde başlar; yüzey çocukta (Road katmanı)
        public float radius;            // Corner: > 0 zorunlu (geometri)
        public float gapLength;         // Ramp: boşluk uzunluğu (geometri)
        public int laneCount = 5;       // 1, 2, 3 veya 5; yalnızca Straight 5'ten az olabilir
        public int turnSign;            // Corner: -1 sola, +1 sağa; diğerleri 0

        public TrackPieceDef ToDef(float measuredLength)
        {
            return new TrackPieceDef
            {
                Kind = kind,
                Length = measuredLength,
                GapLength = kind == PieceKind.Ramp ? gapLength : 0f,
                LaneCount = laneCount,
                TurnSign = turnSign,
                Radius = kind == PieceKind.Corner ? radius : 0f,
            };
        }
    }
}
