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

        int _phaseTicks;
        int _crashPiece;
        bool _crashWasRamp;

        internal void SetStateForTest(float distance, float speed)
        {
            Distance = distance; Speed = speed; Phase = CarPhase.Driving; _phaseTicks = 0; IsOut = false;
        }

        public void Tick(bool braking)
        {
            _events.Clear();
            if (Phase == CarPhase.Finished || IsOut) return;
            _phaseTicks++;

            if (Phase == CarPhase.Crashed)
            {
                TickCrashed();
                return;
            }

            if (Phase == CarPhase.Skidding && _phaseTicks >= SkidTicks)
                SetPhase(CarPhase.Driving);

            float startSpeed = Speed;       // sınırlar bu hızla değerlendirilir
            float oldDistance = Distance;

            if (Phase != CarPhase.Airborne)
            {
                if (braking) Speed -= _stats.BrakeDecel * Step;
                else Speed += _stats.Acceleration * Step;
                if (Speed < 0f) Speed = 0f;
                if (Speed > _stats.TopSpeed) Speed = _stats.TopSpeed;
            }

            float newDistance = oldDistance + Speed * Step;
            Distance = newDistance;

            if (EvaluateBoundaries(oldDistance, newDistance, startSpeed))
                return; // çakılma

            // Viraj içinde hız tavanı
            if (Phase != CarPhase.Airborne)
            {
                var span = _layout.SpanAtDistance(Distance);
                if (span.Def.Kind == PieceKind.Corner && Distance < span.End && Speed > span.Def.SafeSpeed)
                    Speed = span.Def.SafeSpeed;
            }

            if (Distance >= _layout.TotalLength)
            {
                Distance = _layout.TotalLength;
                SetPhase(CarPhase.Finished);
                _events.Add(new SimEvent(SimEventKind.Finished, _layout.PieceCount - 1, Distance));
            }
        }

        int SkidTicks => (int)(_tuning.SkidDuration / Step + 0.5f);

        // true dönerse araç çakıldı (değerlendirme durur).
        bool EvaluateBoundaries(float from, float to, float evalSpeed)
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
                            _events.Add(new SimEvent(SimEventKind.CleanPass, i, span.Start));
                        else if (ratio <= _tuning.SkidBand)
                        {
                            evalSpeed *= 1f - _tuning.SkidSpeedLoss;
                            Speed = evalSpeed;
                            SetPhase(CarPhase.Skidding);
                            _events.Add(new SimEvent(SimEventKind.Skid, i, span.Start));
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
                    }
                    if (Phase == CarPhase.Airborne && span.End > from && span.End <= to)
                    {
                        SetPhase(CarPhase.Driving);
                        _events.Add(new SimEvent(SimEventKind.Landed, i, span.End));
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
            SetPhase(CarPhase.Crashed);
            _events.Add(new SimEvent(kind, pieceIndex, Distance));
        }

        void TickCrashed() { }

        void SetPhase(CarPhase p) { Phase = p; _phaseTicks = 0; }
    }
}
