using UnityEngine;
using ApexBrake.Simulation;

namespace ApexBrake.Track
{
    [CreateAssetMenu(menuName = "Apex Brake/Track Piece")]
    public sealed class TrackPieceAsset : ScriptableObject
    {
        public PieceKind kind;
        public GameObject prefab;       // kökte SplineContainer; çizgi yerel (0,0,0)'dan +Z yönünde başlar
        public float radius;            // Corner: > 0 ise SafeSpeed buradan türetilir
        public float safeSpeedOverride; // > 0 ise türetilen değeri ezer; Bump ve Ramp için zorunlu
        public float minJumpSpeed;      // Ramp
        public float gapLength;         // Ramp
        public int laneCount = 5;       // 1, 2, 3 veya 5; yalnızca Straight 5'ten az olabilir
        public int turnSign;            // Corner: -1 sola, +1 sağa; diğerleri 0

        public TrackPieceDef ToDef(float measuredLength)
        {
            float safe = 0f;
            if (radius > 0f) safe = TrackPieceDef.SafeSpeedFromRadius(radius);
            if (safeSpeedOverride > 0f) safe = safeSpeedOverride;
            return new TrackPieceDef
            {
                Kind = kind,
                Length = measuredLength,
                SafeSpeed = kind == PieceKind.Straight ? 0f : safe,
                MinJumpSpeed = kind == PieceKind.Ramp ? minJumpSpeed : 0f,
                GapLength = kind == PieceKind.Ramp ? gapLength : 0f,
                LaneCount = laneCount,
                TurnSign = turnSign,
            };
        }
    }
}
