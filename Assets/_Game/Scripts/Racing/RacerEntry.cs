using UnityEngine;

namespace ApexBrake.Racing
{
    public enum OutReason { None, OffRoad, Fell, Flipped }
    public enum OutPolicy { Eliminate, Respawn, EndRun }

    /// <summary>Yarıştaki bir araç ve durumu. Durum alanlarını yalnızca RaceManager yazar.</summary>
    public sealed class RacerEntry
    {
        public string Name;
        public bool IsPlayer;
        public int HomeLane;
        public VehicleMotor Motor;
        public LineDriver Driver;
        public VehicleTelemetry Telemetry;

        /// <summary>Doluysa ev çizgisi yerine bu çizgi izlenir (sollama planlayıcısı / testler).</summary>
        public float? TargetLateralOverride;

        public float Distance { get; internal set; }
        public float Lateral { get; internal set; }
        public int Rank { get; internal set; }
        public bool Finished { get; internal set; }
        public float FinishTime { get; internal set; }
        public bool IsOut { get; internal set; }
        public OutReason OutReason { get; internal set; }
        public bool IsRespawning { get; internal set; }
        public OutPolicy Policy { get; internal set; }

        internal int Index;
        internal readonly OutDetector Detector = new OutDetector();
        internal float OutElapsed;          // oyun dışı kalalı geçen süre
        internal bool CollisionsDisabled;
        internal Collider[] Colliders;
    }
}
