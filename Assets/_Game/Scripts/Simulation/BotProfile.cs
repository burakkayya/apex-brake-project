namespace ApexBrake.Simulation
{
    /// <summary>
    /// Bot sürücünün kişiliği (Racing.BotDriver kullanır). Bunlar oyun kuralı değil, sürücü muhakemesinin değişkenleridir;
    /// sonucu (kayma, yoldan çıkma) fizik verir. Aracın gücü EngineFactor/TopSpeedFactor/TireFactor ile (VehicleUpgrades).
    /// </summary>
    [System.Serializable]
    public sealed class BotProfile
    {
        public string Name;
        public float Skill = 0.7f;             // 0..1; tahmin doğruluğu (viraj tutunması / fren mesafesi kanaatindeki saçılma)
        public float Courage = 1f;             // aracın tutunmasını ne kadar zorladığı (kanaat çarpanı)
        public float MistakeChance;            // engel başına büyük hata olasılığı (tutunmayı abartır ya da fren noktasını kaçırır)
        public float HeroChance;               // engel başına kusursuz geçiş olasılığı
        public float ReactionTime = 0.10f;     // sn; karar bu kadar eski durumla verilir
        public float Wander = 0.35f;           // m; çizgide gezinme genliği
        public float EngineFactor = 1f;        // VehicleUpgrades.Engine
        public float TopSpeedFactor = 1f;      // VehicleUpgrades.TopSpeed
        public float TireFactor = 1f;          // VehicleUpgrades.Tire

        public static BotProfile Master() => new BotProfile { Name = "Usta", Skill = 0.9f, Courage = 1.0f,
            MistakeChance = 0.02f, HeroChance = 0.25f, ReactionTime = 0.06f, Wander = 0.25f };
        public static BotProfile Balanced() => new BotProfile { Name = "Dengeli", Skill = 0.7f, Courage = 0.95f,
            MistakeChance = 0.06f, HeroChance = 0.10f, ReactionTime = 0.10f };
        public static BotProfile Reckless() => new BotProfile { Name = "Atak", Skill = 0.45f, Courage = 1.15f,
            MistakeChance = 0.15f, HeroChance = 0.12f, ReactionTime = 0.12f, Wander = 0.45f };
        public static BotProfile Slow() => new BotProfile { Name = "Yavas", Skill = 0.6f, Courage = 0.85f,
            MistakeChance = 0.05f, HeroChance = 0.05f, ReactionTime = 0.16f, EngineFactor = 0.92f, TopSpeedFactor = 0.94f };
    }
}
