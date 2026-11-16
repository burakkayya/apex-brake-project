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
    }
}
