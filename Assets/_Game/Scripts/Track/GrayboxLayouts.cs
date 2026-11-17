namespace ApexBrake.Track
{
    /// <summary>
    /// Gri kutu tariflerinin parça dizileri (parça kimlikleri: Straight50/100, CornerR90_R60/R40/R25, CornerL90_..., Ramp, Bump,
    /// Narrow3_60, Narrow2_60). Editor fabrikası tarif varlıklarını, testler aynı pisti kodla bu dizilerden kurar.
    /// </summary>
    public static class GrayboxLayouts
    {
        /// <summary>Yarış sahnesinin pisti (Recipe_Graybox_Race).</summary>
        public static readonly string[] Race =
        {
            "Straight100", "CornerR90_R40", "Straight100", "Narrow3_60", "Straight50", "Bump", "Straight50", "CornerL90_R25",
            "Straight100", "Ramp", "Straight100", "Narrow2_60", "Straight50", "CornerL90_R40", "Straight50", "CornerR90_R25",
            "Straight100", "Ramp", "Straight50", "CornerR90_R40", "Straight100",
        };

        /// <summary>Eski kısa gri kutu tarifi (Recipe_Graybox).</summary>
        public static readonly string[] Short =
        {
            "Straight100", "CornerR90_R40", "Straight100", "Bump", "Straight50", "CornerL90_R25", "Straight100", "Ramp",
            "Straight100", "CornerL90_R40", "Straight50", "CornerR90_R25", "Straight100", "Ramp", "Straight50", "CornerR90_R40", "Straight100",
        };
    }
}
