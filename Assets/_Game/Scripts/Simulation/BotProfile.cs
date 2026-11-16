namespace ApexBrake.Simulation
{
    [System.Serializable]
    public sealed class BotProfile
    {
        public string Name;
        public float Acceleration = 8f;
        public float TopSpeed = 30f;
        public float BrakeError;               // m; fren noktasındaki sapmanın yaklaşık standart sapması
        public float Risk = 0.95f;             // hedef giriş hızı / güvenli hız

        public static BotProfile Master() => new BotProfile { Name = "Usta", BrakeError = 0.3f, Risk = 0.97f };
        public static BotProfile Balanced() => new BotProfile { Name = "Dengeli", BrakeError = 0.8f, Risk = 0.93f };
        public static BotProfile Reckless() => new BotProfile { Name = "Atak", BrakeError = 1.8f, Risk = 1.00f };
        public static BotProfile Slow() => new BotProfile { Name = "Yavas", BrakeError = 0.6f, Risk = 0.82f, TopSpeed = 27f };

        public CarStats ToStats() => new CarStats { Acceleration = Acceleration, TopSpeed = TopSpeed, BrakeDecel = 25f };
    }
}
