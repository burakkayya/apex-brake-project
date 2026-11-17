namespace ApexBrake.Simulation
{
    /// <summary>Parça geometrisi (tür, uzunluk, şerit sayısı, viraj yarıçapı/yönü, rampa boşluğu). Hız eşiği yoktur (spec §12.1).</summary>
    public sealed class TrackPieceDef
    {
        public PieceKind Kind;
        public float Length;        // m, > 0
        public float GapLength;     // yalnızca Ramp: parça içindeki gerçek boşluğun uzunluğu
        public int LaneCount = 5;   // 1, 2, 3 veya 5; yalnızca Straight 5'ten az olabilir
        public int TurnSign;        // Corner: -1 sola, +1 sağa; diğerleri 0
        public float Radius;        // Corner: > 0 zorunlu. Diğerleri 0.

        public float HalfWidth => LaneCount * 3.2f / 2f;   // yol yarı genişliği (m)
    }
}
