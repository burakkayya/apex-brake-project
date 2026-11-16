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

        public void Tick(bool braking)
        {
            _events.Clear();
            if (Phase == CarPhase.Finished || IsOut) return;
            _phaseTicks++;

            if (braking) Speed -= _stats.BrakeDecel * Step;
            else Speed += _stats.Acceleration * Step;
            if (Speed < 0f) Speed = 0f;
            if (Speed > _stats.TopSpeed) Speed = _stats.TopSpeed;
            Distance += Speed * Step;

            if (Distance >= _layout.TotalLength)
            {
                Distance = _layout.TotalLength;
                SetPhase(CarPhase.Finished);
                _events.Add(new SimEvent(SimEventKind.Finished, _layout.PieceCount - 1, Distance));
            }
        }

        void SetPhase(CarPhase p) { Phase = p; _phaseTicks = 0; }
    }
}
