using System;
using System.Collections.Generic;
using NUnit.Framework;
using ApexBrake.Simulation;

namespace ApexBrake.Tests.EditMode
{
    public sealed class LaneMapTests
    {
        static TrackPieceDef S(float len, int lanes = 5) => new TrackPieceDef { Kind = PieceKind.Straight, Length = len, LaneCount = lanes };
        static TrackPieceDef C(float len, float safe, int lanes = 5) =>
            new TrackPieceDef { Kind = PieceKind.Corner, Length = len, SafeSpeed = safe, LaneCount = lanes, TurnSign = 1 };

        static void AssertMap(int laneCount, params int[] expected)
        {
            for (int home = 0; home < LaneMap.HomeLanes; home++)
                Assert.AreEqual(expected[home], LaneMap.EffectiveLane(home, laneCount), $"home {home}, lanes {laneCount}");
        }

        [Test] public void EffectiveLane_5Lanes_IsIdentity() => AssertMap(5, 0, 1, 2, 3, 4);
        [Test] public void EffectiveLane_3Lanes_Maps_0_0_1_2_2() => AssertMap(3, 0, 0, 1, 2, 2);
        [Test] public void EffectiveLane_2Lanes_Maps_0_0_0_1_1() => AssertMap(2, 0, 0, 0, 1, 1);
        [Test] public void EffectiveLane_1Lane_AllZero() => AssertMap(1, 0, 0, 0, 0, 0);

        [Test]
        public void EffectiveLane_RejectsLaneCount4_AndHomeLane5()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => LaneMap.EffectiveLane(0, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => LaneMap.EffectiveLane(5, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => LaneMap.EffectiveLane(-1, 5));
        }

        [Test]
        public void LateralOffset_5Lanes_CenterIsZero_EdgesAre6_4()
        {
            Assert.AreEqual(0f, LaneMap.LateralOffset(2, 5, 3.2f), 1e-4f);
            Assert.AreEqual(-6.4f, LaneMap.LateralOffset(0, 5, 3.2f), 1e-4f);
            Assert.AreEqual(6.4f, LaneMap.LateralOffset(4, 5, 3.2f), 1e-4f);
        }

        [Test]
        public void LateralOffset_2Lanes_AreMinusPlus1_6()
        {
            Assert.AreEqual(-1.6f, LaneMap.LateralOffset(0, 2, 3.2f), 1e-4f);
            Assert.AreEqual(1.6f, LaneMap.LateralOffset(1, 2, 3.2f), 1e-4f);
        }

        [Test]
        public void LateralAt_BlendsOverLast20m()
        {
            var l = new TrackLayout(new List<TrackPieceDef> { S(100), S(60, 2), S(100) });
            var t = new SimTuning();
            Assert.AreEqual(6.4f, LaneMap.LateralAt(l, 4, 50f, t), 0.01f);
            Assert.AreEqual(4.0f, LaneMap.LateralAt(l, 4, 90f, t), 0.01f);
            Assert.AreEqual(1.6f, LaneMap.LateralAt(l, 4, 100f, t), 0.01f);
            Assert.AreEqual(1.6f, LaneMap.LateralAt(l, 4, 130f, t), 0.01f);
            Assert.AreEqual(4.0f, LaneMap.LateralAt(l, 4, 150f, t), 0.01f);
            Assert.AreEqual(6.4f, LaneMap.LateralAt(l, 4, 160f, t), 0.01f);
        }

        [Test]
        public void Layout_RejectsNarrowCorner() =>
            Assert.Throws<ArgumentException>(() => new TrackLayout(new List<TrackPieceDef> { S(100), C(60, 20, 3), S(100) }));

        [Test]
        public void Layout_RejectsNarrowFirstPiece() =>
            Assert.Throws<ArgumentException>(() => new TrackLayout(new List<TrackPieceDef> { S(100, 3), S(100) }));

        [Test]
        public void Layout_RejectsLaneCount4() =>
            Assert.Throws<ArgumentException>(() => new TrackLayout(new List<TrackPieceDef> { S(100), S(60, 4), S(100) }));
    }
}
