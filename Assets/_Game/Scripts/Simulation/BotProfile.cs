namespace ApexBrake.Simulation
{
    [System.Serializable]
    public sealed class BotProfile
    {
        public string Name;
        public float Acceleration = 8f;
        public float TopSpeed = 30f;
        public float Grip = 12f;               // ToStats() kopyalar
        public float BrakeError;               // m; fren noktasındaki sapmanın yaklaşık standart sapması
        public float Risk = 0.95f;             // hedef giriş hızı / aracın viraj sınırı (tümsek/rampa: güvenli hız)
        public float RiskJitter = 0.04f;       // engel başına risk oynaması (±)
        public float MistakeChance;            // engel başına büyük hata olasılığı (fren noktası 4–9 m geç)
        public float HeroChance;               // engel başına kusursuz geçiş olasılığı (sapma 0, risk 0.99)
        public float ReactionTime = 0.10f;     // sn; karar bu kadar eski durumla verilir
        public float Wander = 0.35f;           // m; çizgide gezinme genliği

        public static BotProfile Master() => new BotProfile { Name = "Usta", BrakeError = 0.3f, Risk = 0.92f,
            MistakeChance = 0.02f, HeroChance = 0.25f, ReactionTime = 0.06f };
        public static BotProfile Balanced() => new BotProfile { Name = "Dengeli", BrakeError = 0.8f, Risk = 0.87f,
            MistakeChance = 0.06f, HeroChance = 0.10f, ReactionTime = 0.10f };
        public static BotProfile Reckless() => new BotProfile { Name = "Atak", BrakeError = 1.8f, Risk = 0.95f,
            MistakeChance = 0.15f, HeroChance = 0.10f, ReactionTime = 0.12f };
        public static BotProfile Slow() => new BotProfile { Name = "Yavas", BrakeError = 0.6f, Risk = 0.82f, TopSpeed = 27f,
            MistakeChance = 0.05f, HeroChance = 0.05f, ReactionTime = 0.16f };

        public CarStats ToStats() => new CarStats { Acceleration = Acceleration, TopSpeed = TopSpeed, BrakeDecel = 25f, Grip = Grip };
    }
}
