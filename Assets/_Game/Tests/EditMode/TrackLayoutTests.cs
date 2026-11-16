using System;
using System.Collections.Generic;
using NUnit.Framework;
using ApexBrake.Simulation;

namespace ApexBrake.Tests.EditMode
{
    public sealed class TrackLayoutTests
    {
        static TrackPieceDef S(float len) => new TrackPieceDef { Kind = PieceKind.Straight, Length = len };
        static TrackPieceDef C(float len, float radius = 40f, int turn = 1) => new TrackPieceDef { Kind = PieceKind.Corner, Length = len, Radius = radius, TurnSign = turn };

        static TrackLayout Basic() => new TrackLayout(new List<TrackPieceDef> { S(50), C(60), S(40) });

        [Test] public void TotalLength_IsSumOfPieces() => Assert.AreEqual(150f, Basic().TotalLength, 1e-4f);

        [Test]
        public void SpanAtDistance_ReturnsPieceAndBounds()
        {
            var s = Basic().SpanAtDistance(75f);
            Assert.AreEqual(1, s.Index); Assert.AreEqual(50f, s.Start, 1e-4f); Assert.AreEqual(110f, s.End, 1e-4f);
        }

        [Test] public void SpanAtDistance_BoundaryBelongsToNextPiece() => Assert.AreEqual(1, Basic().SpanAtDistance(50f).Index);

        [Test]
        public void SpanAtDistance_ClampsOutOfRange()
        {
            var l = Basic();
            Assert.AreEqual(0, l.SpanAtDistance(-5f).Index);
            Assert.AreEqual(2, l.SpanAtDistance(999f).Index);
        }

        [Test]
        public void TryGetNextObstacle_SkipsStraights()
        {
            var l = Basic();
            Assert.IsTrue(l.TryGetNextObstacle(10f, out var s));
            Assert.AreEqual(1, s.Index);
            Assert.IsFalse(l.TryGetNextObstacle(120f, out _));
        }

        [Test] public void SafeSpeedFromRadius_UsesGrip12() => Assert.AreEqual(21.9089f, TrackPieceDef.SafeSpeedFromRadius(40f), 0.001f);

        [Test] public void Ctor_RejectsEmpty() => Assert.Throws<ArgumentException>(() => new TrackLayout(new List<TrackPieceDef>()));
        [Test] public void Ctor_RejectsObstacleAsFirstPiece() => Assert.Throws<ArgumentException>(() => new TrackLayout(new List<TrackPieceDef> { C(60), S(40) }));
        [Test] public void Ctor_RejectsObstacleAsLastPiece() => Assert.Throws<ArgumentException>(() => new TrackLayout(new List<TrackPieceDef> { S(40), C(60) }));

        [Test]
        public void Ctor_RejectsRampWithMinJumpAboveSafe()
        {
            var ramp = new TrackPieceDef { Kind = PieceKind.Ramp, Length = 40, SafeSpeed = 20, MinJumpSpeed = 25, GapLength = 12 };
            Assert.Throws<ArgumentException>(() => new TrackLayout(new List<TrackPieceDef> { S(50), ramp, S(50) }));
        }

        [Test]
        public void Ctor_RejectsNullPiece_WithArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new TrackLayout(new List<TrackPieceDef> { null, S(10) }));
            Assert.Throws<ArgumentException>(() => new TrackLayout(new List<TrackPieceDef> { S(10), null, S(10) }));
            Assert.Throws<ArgumentException>(() => new TrackLayout(new List<TrackPieceDef> { S(10), null }));
        }

        [Test] public void Ctor_RejectsCornerWithoutRadius() => Assert.Throws<ArgumentException>(() => new TrackLayout(new List<TrackPieceDef> { S(10), C(60, 0f), S(10) }));
        [Test] public void Ctor_RejectsCornerWithoutTurnSign() => Assert.Throws<ArgumentException>(() => new TrackLayout(new List<TrackPieceDef> { S(10), C(60, 40f, 0), S(10) }));

        [Test]
        public void HalfWidth_IsLaneCountTimes1_6()
        {
            Assert.AreEqual(8f, new TrackPieceDef { LaneCount = 5 }.HalfWidth, 1e-5f);
            Assert.AreEqual(3.2f, new TrackPieceDef { LaneCount = 2 }.HalfWidth, 1e-5f);
        }

        [Test] public void Ctor_RejectsNonPositiveLength() => Assert.Throws<ArgumentException>(() => new TrackLayout(new List<TrackPieceDef> { S(0), S(10) }));
    }
}
