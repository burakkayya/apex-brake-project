using System;

namespace ApexBrake.Simulation
{
    public sealed class TrackPieceDef
    {
        public PieceKind Kind;
        public float Length;        // m, > 0
        public float SafeSpeed;     // Corner, Bump: güvenli hız. Ramp: en yüksek güvenli atlama hızı
        public float MinJumpSpeed;  // yalnızca Ramp
        public float GapLength;     // yalnızca Ramp: parçanın SONUNDAKİ boşluk, Length'e dahil

        public static float SafeSpeedFromRadius(float radius) => (float)Math.Sqrt(12f * radius);
    }
}
