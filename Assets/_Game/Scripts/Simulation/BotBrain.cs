using System;

namespace ApexBrake.Simulation
{
    /// <summary>Botun fren kararı ve çizgide gezinmesi. Oyuncuyla aynı fiziği kullanır; trafiğe bakmaz.
    /// Her yarışta tohumdan bir form; her engel için değişen risk ve fren sapması, ara sıra büyük hata ya da kusursuz
    /// geçiş; kararlar tepki süresi kadar eski durumla verilir; startta kısa gecikme; kayarken panik freni.</summary>
    public sealed class BotBrain
    {
        readonly BotProfile _profile;
        readonly TrackLayout _layout;
        readonly SimRandom _random;
        readonly float _brakeDecel;

        readonly float _risk;           // Risk + 0.03 × Form
        readonly float _brakeError;     // BrakeError × (1 − 0.3 × Form)
        readonly int _startBrakeTicks;  // 0..15
        readonly float _wanderPeriod;   // sn, 4..9
        readonly float _wanderPhase;    // radyan

        // Tepki süresi için halka tampon (mesafe, hız)
        readonly float[] _histDistance;
        readonly float[] _histSpeed;
        int _histCount, _histHead;

        int _ticks;
        int _currentPiece = -1;
        float _deviation;               // m; pozitif = geç fren
        float _pieceRisk;
        bool _hero;                     // bu engelde kusursuz geçiş

        public BotBrain(BotProfile profile, TrackLayout layout, uint seed)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            _random = new SimRandom(seed);
            _brakeDecel = profile.ToStats().BrakeDecel;

            // Yarış başına: form, start gecikmesi, gezinme periyodu ve fazı (bu sırayla örneklenir).
            float f = (_random.NextFloat() + _random.NextFloat()) - 1f;          // −1..+1, ortaya yığılı
            Form = f < -1f ? -1f : (f > 1f ? 1f : f);
            _risk = profile.Risk + 0.03f * Form;
            _brakeError = profile.BrakeError * (1f - 0.3f * Form);
            _startBrakeTicks = (int)(_random.NextFloat() * 16f);                  // 0..15 Tick = 0..0.3 sn
            _wanderPeriod = 4f + 5f * _random.NextFloat();
            _wanderPhase = 2f * (float)Math.PI * _random.NextFloat();

            int delay = (int)(Math.Max(0f, profile.ReactionTime) / CarSim.Step + 0.5f);
            _histDistance = new float[delay + 1];
            _histSpeed = new float[delay + 1];
        }

        /// <summary>−1..+1, yarış başına tohumdan. Pozitif = iyi gün (daha cesur, daha isabetli).</summary>
        public float Form { get; }

        /// <summary>Ev çizgisine eklenecek gezinme (m): genliği Wander (daralmada × 0.3), periyodu 4–9 sn olan sinüs.</summary>
        public float LineOffset(CarSim car)
        {
            float amp = _profile.Wander;
            int lanes = Math.Min(_layout.SpanAtDistance(car.Distance).Def.LaneCount,
                                 _layout.SpanAtDistance(car.Distance + _profileLook).Def.LaneCount);
            if (lanes < LaneMap.HomeLanes) amp *= 0.3f;
            float t = _ticks * CarSim.Step;
            return amp * (float)Math.Sin(2.0 * Math.PI * t / _wanderPeriod + _wanderPhase);
        }

        const float _profileLook = 20f; // m; daralmaya bu kadar kala genlik küçülür

        /// <summary>Her Tick'ten önce bir kez çağrılır.</summary>
        public bool ShouldBrake(CarSim car)
        {
            _ticks++;
            Record(car.Distance, car.Speed);

            if (car.Phase != CarPhase.Driving && car.Phase != CarPhase.Skidding) return false;

            // Start: geç çıkış
            if (_ticks <= _startBrakeTicks) return true;

            // Panik freni: kayarken ve çizgiden 1 m'den fazla uzaktayken
            if (car.IsSliding && Math.Abs(car.Lateral - car.TargetLateral) > 1f) return true;

            // Tepki süresi: karar eski durumla verilir (kusursuz geçişte gecikme yok).
            Delayed(out float distance0, out float v);
            if (!_layout.TryGetNextEvalPoint(distance0, out float evalDistance, out PieceSpan span)) return false;
            if (span.Index != _currentPiece)
            {
                _currentPiece = span.Index;
                RollForObstacle();
            }
            if (_hero)
            {
                distance0 = car.Distance; v = car.Speed;
                if (!_layout.TryGetNextEvalPoint(distance0, out evalDistance, out span)) return false;
            }

            var def = span.Def;
            float risk = _pieceRisk;
            float target;
            if (def.Kind == PieceKind.Ramp)
            {
                target = def.SafeSpeed * Math.Min(risk, 0.98f);
                if (target < def.MinJumpSpeed + 1f) target = def.MinJumpSpeed + 1f;
                if (target > def.SafeSpeed) target = def.SafeSpeed;
            }
            else if (def.Kind == PieceKind.Corner)
            {
                // Viraj dışındaki çizgide daha temkinli.
                if (-def.TurnSign * car.TargetLateral > 3f) risk -= 0.03f;
                target = car.CornerLimit(def) * risk;
            }
            else
            {
                target = def.SafeSpeed * risk;
            }

            if (v <= target) return false;

            float required = (v * v - target * target) / (2f * _brakeDecel) + v * CarSim.Step;
            return evalDistance - distance0 <= required - _deviation;
        }

        // Engel başına zar: büyük hata (4–9 m geç fren), kusursuz geçiş (sapma 0, risk 0.99) ya da normal sapma.
        // Örnekleme sırası sabit: zar, risk, sapma (u1..u3), hata mesafesi.
        void RollForObstacle()
        {
            float dice = _random.NextFloat();
            float jitter = (2f * _random.NextFloat() - 1f) * _profile.RiskJitter;
            float u = _random.NextFloat() + _random.NextFloat() + _random.NextFloat();
            float mistake = 4f + 5f * _random.NextFloat();

            _pieceRisk = _risk + jitter;
            _hero = false;
            _deviation = (u - 1.5f) * 2f * _brakeError;
            if (dice < _profile.MistakeChance)
            {
                _deviation = mistake;
            }
            else if (dice < _profile.MistakeChance + _profile.HeroChance)
            {
                _deviation = 0f;
                _pieceRisk = 0.99f;
                _hero = true;
            }
        }

        void Record(float distance, float speed)
        {
            // Yeniden doğma gibi geriye sıçramada eski geçmiş anlamsızdır.
            if (_histCount > 0)
            {
                int newest = (_histHead - 1 + _histDistance.Length) % _histDistance.Length;
                if (distance < _histDistance[newest] - 1f) _histCount = 0;
            }
            _histDistance[_histHead] = distance;
            _histSpeed[_histHead] = speed;
            _histHead = (_histHead + 1) % _histDistance.Length;
            if (_histCount < _histDistance.Length) _histCount++;
        }

        // ReactionTime kadar önceki durum (tampon dolmadıysa en eskisi).
        void Delayed(out float distance, out float speed)
        {
            int newest = (_histHead - 1 + _histDistance.Length) % _histDistance.Length;
            int back = Math.Min(_histDistance.Length - 1, _histCount - 1);
            int idx = (newest - back + _histDistance.Length) % _histDistance.Length;
            distance = _histDistance[idx];
            speed = _histSpeed[idx];
        }
    }
}
