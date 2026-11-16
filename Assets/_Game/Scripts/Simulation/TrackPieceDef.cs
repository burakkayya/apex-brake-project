using System;

namespace ApexBrake.Simulation
{
    public sealed class TrackPieceDef
    {
        public PieceKind Kind;
        public float Length;        // m, > 0
        public float SafeSpeed;     // Bump: güvenli hız. Ramp: en yüksek güvenli atlama hızı. Corner: kullanılmaz
                                    // (nominal değer SafeSpeedFromRadius(Radius), yalnızca HUD için; aracın sınırı CarSim.CornerLimit)
        public float MinJumpSpeed;  // yalnızca Ramp
        public float GapLength;     // yalnızca Ramp: parçanın SONUNDAKİ boşluk, Length'e dahil
        public int LaneCount = 5;   // 1, 2, 3 veya 5; yalnızca Straight 5'ten az olabilir
        public int TurnSign;        // Corner: -1 sola, +1 sağa; diğerleri 0
        public float Radius;        // Corner: > 0 zorunlu. Diğerleri 0.

        public float HalfWidth => LaneCount * 3.2f / 2f;   // yol yarı genişliği (m)

        /// <summary>Varsayılan tutunmayla (12 m/s²) nominal viraj hızı.</summary>
        public static float SafeSpeedFromRadius(float radius) => (float)Math.Sqrt(12f * radius);
    }
}
