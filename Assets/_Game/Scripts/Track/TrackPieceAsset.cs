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

        // Yalnızca eski saf C# simülasyonun (RaceSim/CarSim, Görev 5'te silinir) TrackLayout doğrulamasını
        // geçebilmesi için; yeni fizik araç ve RaceManager bunları OKUMAZ.
        const float LegacyBumpSpeed = 20f, LegacyRampSpeed = 26f, LegacyRampMinJump = 15f;

        public TrackPieceDef ToDef(float measuredLength)
        {
            float legacy = 0f;
            if (kind == PieceKind.Corner && radius > 0f) legacy = TrackPieceDef.SafeSpeedFromRadius(radius);
            if (kind == PieceKind.Bump) legacy = LegacyBumpSpeed;
            if (kind == PieceKind.Ramp) legacy = LegacyRampSpeed;
            return new TrackPieceDef
            {
                Kind = kind,
                Length = measuredLength,
                SafeSpeed = legacy,
                MinJumpSpeed = kind == PieceKind.Ramp ? LegacyRampMinJump : 0f,
                GapLength = kind == PieceKind.Ramp ? gapLength : 0f,
                LaneCount = laneCount,
                TurnSign = turnSign,
                Radius = kind == PieceKind.Corner ? radius : 0f,
            };
        }
    }
}
