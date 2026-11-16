namespace ApexBrake.Simulation
{
    public sealed class SimTuning
    {
        public float RespawnDelay = 1.5f;
        public float CornerRespawnRunUp = 15f;  // m
        public float RampRunUpMargin = 1.2f;
        public float Gravity = 20f;             // m/s², yalnızca görsel uçuş yayı için

        // Yanal fizik (işaret: + = sağ)
        public float LineStiffness = 16f;       // 1/s²; çizgiye dönüş yayı
        public float LineDamping = 5.6f;        // 1/s; sönüm
        public float BrakeGripFactor = 0.8f;    // fren basılıyken yanal tutunma çarpanı
        public float SlideScrub = 1.5f;         // kayarken hız kaybı = bu × |yanal hız| (m/s²)
        public float ShoulderWidth = 1.5f;      // m; banket
        public float CarWidth = 2.0f;           // m
        public float SkidStartSlip = 0.5f;      // m/s; kayma olayı eşiği
        public float BumpLightGrip = 0.3f;      // tümsekte hafifleme çarpanı
        public float BumpLightDuration = 0.6f;  // sn
        public float BumpKick = 1.5f;           // m/s yanal itme
        public float BumpCrashBand = 1.15f;     // tümsekte bu oranın üstü çakılma

        // Şeritler ve temas
        public float LaneWidth = 3.2f;          // m
        public float LaneBlendDistance = 20f;   // m; şerit kayması bu mesafede yumuşatılır
        public float CarLength = 4.5f;          // m; temas mesafesi
        public float RearHitSpeedFactor = 0.85f; // arkadan çarpanın hızı = öndekinin hızı × bu
        public float RearBump = 1.0f;           // m/s; öndekine itme
        public float SideHitKick = 1.5f;        // m/s; yan temasta karşılıklı itme
        public float SideHitSpeedLoss = 0.05f;  // yan temasta iki tarafın hız kaybı
        public float ContactProtect = 0.5f;     // sn; çarpılan araç bu süre yoldan çıkamaz

        // Sollama
        public float OvertakeLook = 18f;        // m; öndeki araca bakış
        public float OvertakeGap = 0.8f;        // m; geçerken yanal boşluk
        public float OvertakeMinSpeedDiff = 1.0f; // m/s
        public float OvertakeHold = 0.5f;       // sn; hedef değiştirmeden önce en kısa süre

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
