namespace ApexBrake.Simulation
{
    public enum SimEventKind { CleanPass, Skid, Crash, FellShort, Takeoff, Landed, Respawned, Finished,
        PerfectBrake, GoodBrake, BoostStarted, BoostEnded,
        RearHit, Bumped, SideHit, Light, Recovered }

    public readonly struct SimEvent
    {
        public readonly SimEventKind Kind;
        public readonly int PieceIndex;
        public readonly float Distance;

        public SimEvent(SimEventKind kind, int pieceIndex, float distance)
        {
            Kind = kind; PieceIndex = pieceIndex; Distance = distance;
        }
    }
}
