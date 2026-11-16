namespace ApexBrake.Simulation
{
    public sealed class SimClock
    {
        public const int MaxTicksPerFrame = 5;

        float _accumulator;

        /// <summary>Bu karede atılacak Tick sayısı. Artan süre biriktirilir; uzun duraklamada fazlalık atılır.</summary>
        public int Advance(float frameDeltaSeconds)
        {
            if (float.IsNaN(frameDeltaSeconds) || frameDeltaSeconds < 0f) frameDeltaSeconds = 0f;
            _accumulator += frameDeltaSeconds;
            // Kayan nokta birikimi yüzünden 0.02'nin tam katlarında bir Tick kaçmasın.
            int ticks = (int)((_accumulator + 1e-6f) / CarSim.Step);
            if (ticks > MaxTicksPerFrame)
            {
                _accumulator = 0f;
                return MaxTicksPerFrame;
            }
            _accumulator -= ticks * CarSim.Step;
            if (_accumulator < 0f) _accumulator = 0f;
            return ticks;
        }

        public float Alpha => _accumulator / CarSim.Step;
    }
}
