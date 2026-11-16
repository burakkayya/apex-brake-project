using System;

namespace ApexBrake.Simulation
{
    /// <summary>Botun fren kararı: ileriye bakarak sıradaki değerlendirme noktasına hedef hızla girmeye çalışır.
    /// Trafiğe bakmaz. Fren noktası her engel için bir kez örneklenen sapmayla kayar (pozitif = geç fren).</summary>
    public sealed class BotBrain
    {
        readonly BotProfile _profile;
        readonly TrackLayout _layout;
        readonly SimRandom _random;
        readonly float _brakeDecel;

        int _currentPiece = -1;
        float _deviation;

        public BotBrain(BotProfile profile, TrackLayout layout, uint seed)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            _random = new SimRandom(seed);
            _brakeDecel = profile.ToStats().BrakeDecel;
        }

        /// <summary>Her Tick'ten önce bir kez çağrılır.</summary>
        public bool ShouldBrake(CarSim car)
        {
            if (car.Phase != CarPhase.Driving && car.Phase != CarPhase.Skidding) return false;
            if (!_layout.TryGetNextEvalPoint(car.Distance, out float evalDistance, out PieceSpan span)) return false;

            if (span.Index != _currentPiece)
            {
                _currentPiece = span.Index;
                float u = _random.NextFloat() + _random.NextFloat() + _random.NextFloat();
                _deviation = (u - 1.5f) * 2f * _profile.BrakeError;
            }

            var def = span.Def;
            float target;
            if (def.Kind == PieceKind.Ramp)
            {
                target = def.SafeSpeed * Math.Min(_profile.Risk, 0.98f);
                if (target < def.MinJumpSpeed + 1f) target = def.MinJumpSpeed + 1f;
                if (target > def.SafeSpeed) target = def.SafeSpeed;
            }
            else if (def.Kind == PieceKind.Corner)
            {
                target = car.CornerLimit(def) * _profile.Risk;
            }
            else
            {
                target = def.SafeSpeed * _profile.Risk;
            }

            float v = car.Speed;
            if (v <= target) return false;

            float required = (v * v - target * target) / (2f * _brakeDecel) + v * CarSim.Step;
            float distance = evalDistance - car.Distance;
            return distance <= required - _deviation;
        }
    }
}
