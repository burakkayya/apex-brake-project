using System;
using System.Collections.Generic;

namespace ApexBrake.Simulation
{
    /// <summary>Tek aracın sabit adımlı simülasyonu: pist boyunca mesafe ve hız, yanal konum ve yanal hız.
    /// Virajda merkezkaç ivmesi dışa iter; lastik, çizgiye dönmek için gereken yanal ivmeyi tutunma sınırına kadar
    /// sağlar. Sınır aşılınca araç dışa kayar; yolun (banket dahil) dışına çıkarsa çakılır.</summary>
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

        public CarStats Stats => _stats;
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

        // Yanal durum (m, + = sağ)
        public float Lateral { get; private set; }
        public float LateralVel { get; private set; }
        /// <summary>İzlenecek çizgi; RaceSim her Tick'ten önce yazar.</summary>
        public float TargetLateral { get; set; }
        /// <summary>Radyan = atan2(LateralVel, max(Speed, 1)); görsel için.</summary>
        public float SlipAngle { get; private set; }
        /// <summary>0..1 = |lastik yanal ivmesi| / kullanılabilir tutunma.</summary>
        public float LateralLoad { get; private set; }
        /// <summary>Lastik doymuş, |LateralVel| ≥ SkidStartSlip ve araç çizgisinden uzaklaşıyor (çizgiye dönerken ya da
        /// şerit değiştirirken doymuş lastik kayma sayılmaz).</summary>
        public bool IsSliding { get; private set; }
        public bool IsLight => _lightTicks > 0;
        public bool IsEdgeProtected => _protectTicks > 0;

        int _phaseTicks;
        int _crashPiece;
        bool _crashWasRamp;
        int _lightTicks;                      // tümsekte hafifleme kalan Tick
        int _protectTicks;                    // yol kenarı koruması kalan Tick

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

        /// <summary>Yanal durumu TargetLateral'a oturtur.</summary>
        internal void SetStateForTest(float distance, float speed) => SetStateForTest(distance, speed, TargetLateral, TargetLateral);

        internal void SetStateForTest(float distance, float speed, float lateral, float target)
        {
            Distance = distance; Speed = speed; Phase = CarPhase.Driving; _phaseTicks = 0; IsOut = false;
            _holdTicks = 0; _lastHoldTicks = 0; _ticksSinceRelease = int.MaxValue;
            ClearPending(); _boostTicksLeft = 0; _boostFactor = 1f;
            Lateral = lateral; TargetLateral = target; LateralVel = 0f;
            SlipAngle = 0f; LateralLoad = 0f; IsSliding = false; _lightTicks = 0; _protectTicks = 0;
        }

        /// <summary>Aracı yanal olarak verilen çizgiye oturtur (yarış başı): Lateral = TargetLateral = lateral.</summary>
        internal void PlaceOnLine(float lateral)
        {
            Lateral = lateral; TargetLateral = lateral; LateralVel = 0f; SlipAngle = 0f;
        }

        internal void SetOutForTest()
        {
            Speed = 0f; SetPhase(CarPhase.Crashed); IsOut = true;
            ClearPending(); _boostTicksLeft = 0; _boostFactor = 1f;
        }

        /// <summary>Bu aracın güvenli viraj hızı: sqrt(Grip × Radius).</summary>
        public float CornerLimit(TrackPieceDef corner)
        {
            float g = _stats.Grip * corner.Radius;
            return g > 0f ? (float)Math.Sqrt(g) : 0f;
        }

        /// <summary>Speed = min(Speed, max); 0'ın altına inmez.</summary>
        public void LimitSpeed(float max)
        {
            if (Speed > max) Speed = max;
            if (Speed < 0f) Speed = 0f;
        }

        /// <summary>TopSpeed × BoostFactor tavanına kadar ekler (tavanın üstündeki hızı düşürmez).</summary>
        public void AddSpeed(float delta)
        {
            float cap = _stats.TopSpeed * BoostFactor;
            float v = Speed + delta;
            if (v > cap) v = cap;
            if (v > Speed) Speed = v;
            else if (delta < 0f) Speed = v < 0f ? 0f : v;
        }

        public void ScaleSpeed(float factor)
        {
            Speed *= factor;
            if (Speed < 0f) Speed = 0f;
        }

        /// <summary>Temas itmesi. Sonlu olmayan değer yok sayılır.</summary>
        public void AddLateralVel(float delta)
        {
            if (float.IsNaN(delta) || float.IsInfinity(delta)) return;
            LateralVel += delta;
            SlipAngle = (float)Math.Atan2(LateralVel, Math.Max(Speed, 1f));
        }

        /// <summary>Sonraki Tick'lerden bu süre boyunca araç yol kenarında tutulur (yoldan çıkamaz).</summary>
        public void ProtectFromEdge(float seconds)
        {
            if (!(seconds > 0f)) return;
            int ticks = (int)(seconds / Step + 0.5f);
            if (ticks > _protectTicks) _protectTicks = ticks;
        }

        /// <summary>Son Tick'in olaylarına ekler; PieceIndex = bulunduğu parça.</summary>
        public void RaiseEvent(SimEventKind kind) =>
            _events.Add(new SimEvent(kind, _layout.SpanAtDistance(Distance).Index, Distance));

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

            // Bu Tick için hafifleme ve kenar koruması (Tick içinde verilen yenisi sonraki Tick'ten geçerli).
            bool light = _lightTicks > 0;
            if (light) _lightTicks--;
            bool protect = _protectTicks > 0;
            if (protect) _protectTicks--;

            float startSpeed = Speed;       // sınırlar bu hızla değerlendirilir
            float oldDistance = Distance;
            bool boostedThisTick = IsBoosting;
            bool grounded = Phase != CarPhase.Airborne;
            bool saturated = false, sliding = false, scrubbing = false;

            if (grounded)
            {
                saturated = StepLateral(braking, light);
                // Doymuş lastik ancak araç çizgisinden uzaklaşırken kayar (sürtünür); çizgiye dönerken ya da şerit
                // değiştirirken doymuş lastik yalnızca hızlanmayı keser.
                scrubbing = saturated && LateralVel * (Lateral - TargetLateral) >= 0f;
                sliding = scrubbing && Math.Abs(LateralVel) >= _tuning.SkidStartSlip;

                float cap = _stats.TopSpeed * BoostFactor;
                if (braking) Speed -= _stats.BrakeDecel * Step;
                else if (Speed < cap)
                {
                    // Lastik viraja ne kadar yüklenmişse o kadar az hızlanır.
                    float load = LateralLoad;
                    float grip = 1f - load * load;
                    float accel = _stats.Acceleration * (IsBoosting ? _tuning.BoostAccelFactor : 1f)
                                  * (grip > 0f ? (float)Math.Sqrt(grip) : 0f);
                    Speed += accel * Step;
                    if (Speed > cap) Speed = cap;
                }
                else if (Speed > cap)
                {
                    // Tavanın üstündeki fazla hız (boost bitince) yavaşça erir; tavanın altına inmez.
                    Speed -= _tuning.OverspeedDecay * Step;
                    if (Speed < cap) Speed = cap;
                }
                // Kayan lastik hız kaybettirir.
                if (scrubbing) Speed -= _tuning.SlideScrub * Math.Abs(LateralVel) * Step;
                if (Speed < 0f) Speed = 0f;
            }
            else
            {
                LateralLoad = 0f;
            }

            float newDistance = oldDistance + Speed * Step;
            Distance = newDistance;

            if (EvaluateBoundaries(oldDistance, newDistance, startSpeed, braking))
                return; // çakılma

            if (grounded && Phase != CarPhase.Airborne)
            {
                if (CheckRoadEdge(protect)) return; // yoldan çıktı
                UpdateSlideState(saturated, sliding);
            }
            SlipAngle = (float)Math.Atan2(LateralVel, Math.Max(Speed, 1f));

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
                IsSliding = false;
                if (IsBoosting) EndBoost();
                ClearPending();
                _events.Add(new SimEvent(SimEventKind.Finished, _layout.PieceCount - 1, Distance));
            }
        }

        // Yanal adım; dönüş: lastik doydu mu.
        bool StepLateral(bool braking, bool light)
        {
            var def = _layout.SpanAtDistance(Distance).Def;
            float centrifugal = 0f;
            if (def.Kind == PieceKind.Corner && Speed >= 1f && def.Radius > 0f)
                centrifugal = -def.TurnSign * Speed * Speed / def.Radius;

            float grip = _stats.Grip * (braking ? _tuning.BrakeGripFactor : 1f) * (light ? _tuning.BumpLightGrip : 1f);
            if (!(grip > 0f)) grip = 0f;

            float wanted = -centrifugal
                           + _tuning.LineStiffness * (TargetLateral - Lateral)
                           - _tuning.LineDamping * LateralVel;
            float tyre = wanted > grip ? grip : (wanted < -grip ? -grip : wanted);
            bool saturated = Math.Abs(wanted) > grip;
            LateralLoad = grip > 0f ? Math.Abs(tyre) / grip : (saturated ? 1f : 0f);

            LateralVel += (centrifugal + tyre) * Step;
            Lateral += LateralVel * Step;
            return saturated;
        }

        // true dönerse araç yoldan çıkıp çakıldı.
        bool CheckRoadEdge(bool protect)
        {
            var span = _layout.SpanAtDistance(Distance);
            float edge = span.Def.HalfWidth + _tuning.ShoulderWidth;
            if (!(Math.Abs(Lateral) > edge)) return false;
            if (protect)
            {
                float side = Lateral < 0f ? -1f : 1f;
                Lateral = side * edge;
                if (LateralVel * side > 0f) LateralVel = 0f;
                return false;
            }
            IsSliding = false;
            SlipAngle = (float)Math.Atan2(LateralVel, Math.Max(Speed, 1f));
            // Viraj çıkışından sonraki düzde yoldan çıkan araç o virajda çıkmış sayılır (yeniden doğma viraj kuralıyla).
            int piece = span.Index;
            for (int k = span.Index; k >= 0; k--)
            {
                var kind = _layout.SpanAt(k).Def.Kind;
                if (kind == PieceKind.Corner) { piece = k; break; }
                if (kind != PieceKind.Straight) break;
            }
            Crash(piece, SimEventKind.Crash, false);
            return true;
        }

        void UpdateSlideState(bool saturated, bool sliding)
        {
            IsSliding = sliding;
            if (Phase == CarPhase.Driving && IsSliding)
            {
                SetPhase(CarPhase.Skidding);
                RaiseEvent(SimEventKind.Skid);
                // Kayma etkin boost'u bitirir, bekleyeni düşürür.
                ClearPending();
                if (IsBoosting) EndBoost();
            }
            else if (Phase == CarPhase.Skidding && !saturated
                     && Math.Abs(Lateral - TargetLateral) < 0.3f
                     && Math.Abs(LateralVel) < _tuning.SkidStartSlip)
            {
                SetPhase(CarPhase.Driving);
                RaiseEvent(SimEventKind.Recovered);
            }
        }

        int BoostTicks => (int)(_tuning.BoostDuration / Step + 0.5f);
        int ReleaseWindowTicks => (int)(_tuning.BoostReleaseWindow / Step + 0.5f);
        int MinHoldTicks => (int)(_tuning.MinBrakeHold / Step + 0.5f);
        int LightTicks => (int)(_tuning.BumpLightDuration / Step + 0.5f);

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

        // Değerlendirme noktasında boost kazanılıp kazanılmadığına bakar.
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

                if (def.Kind == PieceKind.Corner)
                {
                    // Viraj girişinde karar yok; yalnızca Perfect/Good değerlendirmesi (aracın kendi sınırına göre).
                    if (span.Start > from && span.Start <= to)
                    {
                        float ratio = evalSpeed / CornerLimit(def);
                        if (ratio <= 1f) TryEarnBoost(i, ratio, braking, span.Start, false);
                    }
                }
                else if (def.Kind == PieceKind.Bump)
                {
                    if (span.Start > from && span.Start <= to)
                    {
                        float ratio = evalSpeed / def.SafeSpeed;
                        if (ratio <= 1f)
                        {
                            _events.Add(new SimEvent(SimEventKind.CleanPass, i, span.Start));
                            TryEarnBoost(i, ratio, braking, span.Start, true);
                        }
                        else if (ratio <= _tuning.BumpCrashBand)
                        {
                            // Hafifleme: tutunma kısa süre azalır, küçük bir yanal itme.
                            _lightTicks = LightTicks;
                            LateralVel += _tuning.BumpKick * (Lateral < 0f ? -1f : 1f);
                            _events.Add(new SimEvent(SimEventKind.Light, i, span.Start));
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
                        IsSliding = false;
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
            IsSliding = false;
            LateralLoad = 0f;
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
            Lateral = TargetLateral;
            LateralVel = 0f;
            SlipAngle = 0f;
            LateralLoad = 0f;
            IsSliding = false;
            _lightTicks = 0;
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
