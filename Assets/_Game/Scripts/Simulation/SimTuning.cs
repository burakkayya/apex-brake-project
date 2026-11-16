namespace ApexBrake.Simulation
{
    public sealed class SimTuning
    {
        public float SkidBand = 1.15f;          // güvenli hızın bu katına kadar savrulma, üstü çakılma
        public float SkidSpeedLoss = 0.40f;
        public float SkidDuration = 0.6f;       // sn, yalnızca görsel durum
        public float RespawnDelay = 1.5f;
        public float CornerRespawnRunUp = 15f;  // m
        public float RampRunUpMargin = 1.2f;
        public float Gravity = 20f;             // m/s², yalnızca görsel uçuş yayı için

        // Şeritler ve temas
        public float LaneWidth = 3.2f;          // m
        public float LaneBlendDistance = 20f;   // m; şerit kayması bu mesafede yumuşatılır
        public float CarLength = 4.5f;          // m; temas mesafesi
        public float RearHitSpeedFactor = 0.85f; // arkadan çarpanın hızı = öndekinin hızı × bu
        public float RearBump = 1.0f;           // m/s; öndekine itme
        public float SideHitSpeedLoss = 0.15f;  // yan temasta iki tarafın hız kaybı

        // Perfect Brake Boost
        public float BoostReleaseWindow = 0.5f; // sn; fren girişten en çok bu kadar önce bırakılmış olmalı
        public float MinBrakeHold = 0.1f;       // sn; boost sayılması için en kısa fren süresi
        public float PerfectBandMin = 0.95f;    // giriş hızı / güvenli hız alt sınırı (üst sınır 1.0)
        public float GoodBandMin = 0.85f;
        public float BoostDuration = 1.5f;      // sn
        public float PerfectBoostFactor = 1.20f; // maksimum hız çarpanı
        public float GoodBoostFactor = 1.10f;
        public float BoostAccelFactor = 2.0f;   // boost sırasında ivme çarpanı
        public float OverspeedDecay = 4.0f;     // m/s²; boost bitince fazla hızın erime hızı
    }
}
