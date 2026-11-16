using System;

namespace ApexBrake.Simulation
{
    /// <summary>Ev şeridinden (0 en sol .. 4 en sağ) parçanın şerit sayısına göre etkin şerit ve yanal konum.</summary>
    public static class LaneMap
    {
        public const int HomeLanes = 5;

        static readonly int[] Map3 = { 0, 0, 1, 2, 2 };
        static readonly int[] Map2 = { 0, 0, 0, 1, 1 };

        public static bool IsValidLaneCount(int laneCount) => laneCount == 1 || laneCount == 2 || laneCount == 3 || laneCount == 5;

        public static int EffectiveLane(int homeLane, int laneCount)
        {
            if (homeLane < 0 || homeLane >= HomeLanes) throw new ArgumentOutOfRangeException(nameof(homeLane));
            switch (laneCount)
            {
                case 5: return homeLane;
                case 3: return Map3[homeLane];
                case 2: return Map2[homeLane];
                case 1: return 0;
                default: throw new ArgumentOutOfRangeException(nameof(laneCount));
            }
        }

        public static float LateralOffset(int effectiveLane, int laneCount, float laneWidth) =>
            (effectiveLane - (laneCount - 1) * 0.5f) * laneWidth;

        static float PieceLateral(TrackPieceDef def, int homeLane, float laneWidth) =>
            LateralOffset(EffectiveLane(homeLane, def.LaneCount), def.LaneCount, laneWidth);

        /// <summary>Mesafedeki yanal konum; sıradaki parçanın şerit sayısı farklıysa mevcut parçanın son
        /// LaneBlendDistance metresinde (parça daha kısaysa tüm parça boyunca) ona doğrusal geçiş.</summary>
        public static float LateralAt(TrackLayout layout, int homeLane, float distance, SimTuning tuning)
        {
            var span = layout.SpanAtDistance(distance);
            float here = PieceLateral(span.Def, homeLane, tuning.LaneWidth);
            if (span.Index + 1 >= layout.PieceCount) return here;

            var next = layout.SpanAt(span.Index + 1).Def;
            if (next.LaneCount == span.Def.LaneCount) return here;

            float length = span.End - span.Start;
            float blend = Math.Min(tuning.LaneBlendDistance, length);
            float blendStart = span.End - blend;
            if (distance <= blendStart || !(blend > 0f)) return here;

            float t = (distance - blendStart) / blend;
            if (t > 1f) t = 1f;
            float there = PieceLateral(next, homeLane, tuning.LaneWidth);
            return here + (there - here) * t;
        }
    }
}
