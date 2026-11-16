namespace ApexBrake.Simulation
{
    /// <summary>xorshift32; platformdan bağımsız, tohumla deterministik.</summary>
    public sealed class SimRandom
    {
        uint _state;

        public SimRandom(uint seed) { _state = seed == 0 ? 1u : seed; }

        /// <summary>[0, 1)</summary>
        public float NextFloat()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return (x >> 8) * (1f / 16777216f); // üst 24 bit → [0, 1)
        }
    }
}
