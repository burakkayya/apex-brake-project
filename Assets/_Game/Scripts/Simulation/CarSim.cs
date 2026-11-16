using System.Collections.Generic;

namespace ApexBrake.Simulation
{
    public sealed class CarSim
    {
        public const float Step = 0.02f;

        readonly TrackLayout _layout;
        readonly CarStats _stats;
        readonly SimTuning _tuning;
        readonly CrashPolicy _policy;
        readonly List<SimEvent> _events = new List<SimEvent>();

        public CarSim(TrackLayout layout, CarStats stats, SimTuning tuning, CrashPolicy policy)
        {
            _layout = layout; _stats = stats; _tuning = tuning; _policy = policy;
        }

        public float Distance { get; private set; }
        public float Speed { get; private set; }
        public CarPhase Phase { get; private set; }
        public float PhaseTime => _phaseTicks * Step;
        public bool IsOut { get; private set; }
        public IReadOnlyList<SimEvent> Events => _events;
        public bool IsBoosting => _boostTicksLeft > 0;
        public float BoostFactor => IsBoosting ? _boostFactor : 1f;
        public int PerfectCount { get; private set; }
        public int GoodCount { get; private set; }

        int _phaseTicks;
        int _crashPiece;
        bool _crashWasRamp;

        // Fren geçmişi (Perfect Brake Boost)
        int _holdTicks;                       // süren kesintisiz fren
        int _lastHoldTicks;                   // son bırakılan frenin süresi
        int _ticksSinceRelease = int.MaxValue; // son bırakıştan bu yana (bırakış Tick'i 1)

        // Kazanılmış, henüz başlamamış boost
        float _pendingFactor;                 // 0 = yok
        int _pendingPiece = -1;
        bool _pendingReady;

        // Etkin boost
        float _boostFactor = 1f;
        int _boostTicksLeft;

        internal void SetStateForTest(float distance, float speed)
        {
            Distance = distance; Speed = speed; Phase = CarPhase.Driving; _phaseTicks = 0; IsOut = false;
            _holdTicks = 0; _lastHoldTicks = 0; _ticksSinceRelease = int.MaxValue;
            ClearPending(); _boostTicksLeft = 0; _boostFactor = 1f;
        }

        public void Tick(bool braking)
        {
            _events.Clear();
            if (Phase == CarPhase.Finished || IsOut) return;
            _phaseTicks++;
            UpdateBrakeHistory(braking);

            if (Phase == CarPhase.Crashed)
            {
                TickCrashed();
                return;
            }

            if (Phase == CarPhase.Skidding && _phaseTicks >= SkidTicks)
                SetPhase(CarPhase.Driving);

            float startSpeed = Speed;       // sınırlar bu hızla değerlendirilir
            float oldDistance = Distance;
            bool boostedThisTick = IsBoosting;

            if (Phase != CarPhase.Airborne)
            {
                float cap = _stats.TopSpeed * BoostFactor;
                if (braking) Speed -= _stats.BrakeDecel * Step;
                else if (Speed < cap)
                {
                    float accel = _stats.Acceleration * (IsBoosting ? _tuning.BoostAccelFactor : 1f);
                    Speed += accel * Step;
                    if (Speed > cap) Speed = cap;
                }
                else if (Speed > cap)
                {
                    // Tavanın üstündeki fazla hız (boost bitince) yavaşça erir; tavanın altına inmez.
                    Speed -= _tuning.OverspeedDecay * Step;
                    if (Speed < cap) Speed = cap;
                }
                if (Speed < 0f) Speed = 0f;
            }

            float newDistance = oldDistance + Speed * Step;
            Distance = newDistance;

            if (EvaluateBoundaries(oldDistance, newDistance, startSpeed, braking))
                return; // çakılma

            // Viraj içinde hız tavanı
            if (Phase != CarPhase.Airborne)
            {
                var span = _layout.SpanAtDistance(Distance);
                if (span.Def.Kind == PieceKind.Corner && Distance < span.End && Speed > span.Def.SafeSpeed)
                    Speed = span.Def.SafeSpeed;
            }

            // Boost süresi: bu Tick'te boost'lu sürdüyse bir Tick düş.
            if (boostedThisTick && IsBoosting)
            {
                _boostTicksLeft--;
                if (_boostTicksLeft == 0) EndBoost();
            }

            // Kazanılmış boost engelden çıkınca başlar: viraj → End; tümsek → hemen; rampa → iniş.
            if (_pendingFactor > 0f)
            {
                if (!_pendingReady)
                {
                    var p = _layout.SpanAt(_pendingPiece);
                    if (p.Def.Kind == PieceKind.Corner && Distance >= p.End) _pendingReady = true;
                }
                if (_pendingReady)
                {
                    _boostFactor = _pendingFactor;
                    _boostTicksLeft = BoostTicks;
                    _events.Add(new SimEvent(SimEventKind.BoostStarted, _pendingPiece, Distance));
                    ClearPending();
                }
            }

            if (Distance >= _layout.TotalLength)
            {
                Distance = _layout.TotalLength;
                SetPhase(CarPhase.Finished);
                if (IsBoosting) EndBoost();
                ClearPending();
                _events.Add(new SimEvent(SimEventKind.Finished, _layout.PieceCount - 1, Distance));
            }
        }

        int SkidTicks => (int)(_tuning.SkidDuration / Step + 0.5f);
        int BoostTicks => (int)(_tuning.BoostDuration / Step + 0.5f);
        int ReleaseWindowTicks => (int)(_tuning.BoostReleaseWindow / Step + 0.5f);
        int MinHoldTicks => (int)(_tuning.MinBrakeHold / Step + 0.5f);

        void UpdateBrakeHistory(bool braking)
        {
            if (braking)
            {
                _holdTicks++;
                return;
            }
            if (_holdTicks > 0)
            {
                _lastHoldTicks = _holdTicks;
                _holdTicks = 0;
                _ticksSinceRelease = 0;
            }
            if (_ticksSinceRelease < int.MaxValue) _ticksSinceRelease++;
        }

        // Temiz geçilen değerlendirme noktasında boost kazanılıp kazanılmadığına bakar.
        void TryEarnBoost(int pieceIndex, float ratio, bool braking, float at, bool readyNow)
        {
            if (braking) return;
            if (_ticksSinceRelease > ReleaseWindowTicks || _lastHoldTicks < MinHoldTicks) return;
            float factor;
            if (ratio >= _tuning.PerfectBandMin && ratio <= 1f)
            {
                factor = _tuning.PerfectBoostFactor;
                PerfectCount++;
                _events.Add(new SimEvent(SimEventKind.PerfectBrake, pieceIndex, at));
            }
            else if (ratio >= _tuning.GoodBandMin && ratio < _tuning.PerfectBandMin)
            {
                factor = _tuning.GoodBoostFactor;
                GoodCount++;
                _events.Add(new SimEvent(SimEventKind.GoodBrake, pieceIndex, at));
            }
            else return;
            _pendingFactor = factor;
            _pendingPiece = pieceIndex;
            _pendingReady = readyNow;
        }

        void ClearPending() { _pendingFactor = 0f; _pendingPiece = -1; _pendingReady = false; }

        void EndBoost()
        {
            _boostTicksLeft = 0;
            _boostFactor = 1f;
            _events.Add(new SimEvent(SimEventKind.BoostEnded, _layout.SpanAtDistance(Distance).Index, Distance));
        }

        // true dönerse araç çakıldı (değerlendirme durur).
        bool EvaluateBoundaries(float from, float to, float evalSpeed, bool braking)
        {
            int first = _layout.SpanAtDistance(from).Index;
            for (int i = first; i < _layout.PieceCount; i++)
            {
                var span = _layout.SpanAt(i);
                if (span.Start > to) break;
                var def = span.Def;

                if (def.Kind == PieceKind.Corner || def.Kind == PieceKind.Bump)
                {
                    if (span.Start > from && span.Start <= to)
                    {
                        float ratio = evalSpeed / def.SafeSpeed;
                        if (ratio <= 1f)
                        {
                            _events.Add(new SimEvent(SimEventKind.CleanPass, i, span.Start));
                            TryEarnBoost(i, ratio, braking, span.Start, def.Kind == PieceKind.Bump);
                        }
                        else if (ratio <= _tuning.SkidBand)
                        {
                            evalSpeed *= 1f - _tuning.SkidSpeedLoss;
                            Speed = evalSpeed;
                            SetPhase(CarPhase.Skidding);
                            _events.Add(new SimEvent(SimEventKind.Skid, i, span.Start));
                            // Savrulma etkin boost'u bitirir, bekleyeni düşürür.
                            ClearPending();
                            if (IsBoosting) EndBoost();
                        }
                        else
                        {
                            Crash(i, SimEventKind.Crash, false);
                            return true;
                        }
                    }
                }
                else if (def.Kind == PieceKind.Ramp)
                {
                    float takeoff = span.End - def.GapLength;
                    if (Phase != CarPhase.Airborne && takeoff > from && takeoff <= to)
                    {
                        if (evalSpeed > def.SafeSpeed) { Crash(i, SimEventKind.Crash, true); return true; }
                        if (evalSpeed < def.MinJumpSpeed) { Crash(i, SimEventKind.FellShort, true); return true; }
                        Speed = evalSpeed;
                        SetPhase(CarPhase.Airborne);
                        _events.Add(new SimEvent(SimEventKind.Takeoff, i, takeoff));
                        TryEarnBoost(i, evalSpeed / def.SafeSpeed, braking, takeoff, false);
                    }
                    if (Phase == CarPhase.Airborne && span.End > from && span.End <= to)
                    {
                        SetPhase(CarPhase.Driving);
                        _events.Add(new SimEvent(SimEventKind.Landed, i, span.End));
                        if (_pendingPiece == i) _pendingReady = true;
                    }
                }
            }
            return false;
        }

        void Crash(int pieceIndex, SimEventKind kind, bool ramp)
        {
            _crashPiece = pieceIndex;
            _crashWasRamp = ramp;
            Speed = 0f;
            // Çakılma etkin boost'u bitirir; kazanılmış ama başlamamış boost düşer.
            ClearPending();
            if (IsBoosting) EndBoost();
            SetPhase(CarPhase.Crashed);
            if (_policy != CrashPolicy.Respawn) IsOut = true;
            _events.Add(new SimEvent(kind, pieceIndex, Distance));
        }

        int RespawnTicks => (int)(_tuning.RespawnDelay / Step + 0.5f);

        void TickCrashed()
        {
            if (_policy != CrashPolicy.Respawn || _phaseTicks < RespawnTicks) return;

            float point = RespawnPoint();
            Distance = point;
            Speed = 0f;
            IsOut = false;
            SetPhase(CarPhase.Driving);
            _events.Add(new SimEvent(SimEventKind.Respawned, _layout.SpanAtDistance(point).Index, point));
        }

        float RespawnPoint()
        {
            var span = _layout.SpanAt(_crashPiece);
            float point;
            float takeoff = 0f, runUp = 0f;
            if (_crashWasRamp)
            {
                takeoff = span.End - span.Def.GapLength;
                runUp = span.Def.MinJumpSpeed * span.Def.MinJumpSpeed / (2f * _stats.Acceleration) * _tuning.RampRunUpMargin;
                point = takeoff - runUp;
            }
            else
            {
                point = span.Start - _tuning.CornerRespawnRunUp;
            }
            if (point < 0f) point = 0f;

            // Nokta başka bir engelin içine düşüyorsa ya da (rampada) kalkışa kadar koşu mesafesi yetmiyorsa,
            // çakılan engelden geriye doğru bir önceki düz parçanın başına çek; koşul sağlanana dek yinele.
            int cursor = _crashPiece - 1;
            while (true)
            {
                var at = _layout.SpanAtDistance(point);
                bool insideOther = at.Def.Kind != PieceKind.Straight && at.Index != _crashPiece;
                bool shortRunUp = _crashWasRamp && takeoff - point < runUp - 1e-4f;
                if (!insideOther && !shortRunUp) return point;

                while (cursor >= 0 && _layout.SpanAt(cursor).Def.Kind != PieceKind.Straight) cursor--;
                if (cursor < 0) return 0f;
                point = _layout.SpanAt(cursor).Start;
                cursor--;
            }
        }

        void SetPhase(CarPhase p) { Phase = p; _phaseTicks = 0; }
    }
}
