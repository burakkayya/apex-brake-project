namespace ApexBrake.Simulation
{
    /// <summary>Şerit geometrisi (ev çizgileri ve daralmalarda geçiş). Araç dinamiği burada değildir; fizik motorundadır.</summary>
    public sealed class SimTuning
    {
        public float LaneWidth = 3.2f;          // m
        public float LaneBlendDistance = 20f;   // m; şerit kayması bu mesafede yumuşatılır
    }
}
