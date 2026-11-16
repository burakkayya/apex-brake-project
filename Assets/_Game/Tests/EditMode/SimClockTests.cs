using NUnit.Framework;
using ApexBrake.Simulation;

namespace ApexBrake.Tests.EditMode
{
    public sealed class SimClockTests
    {
        [Test] public void Advance_0_02_ReturnsOneTick() => Assert.AreEqual(1, new SimClock().Advance(0.02f));

        [Test]
        public void Advance_AccumulatesRemainder()
        {
            var c = new SimClock();
            Assert.AreEqual(0, c.Advance(0.015f));
            Assert.AreEqual(1, c.Advance(0.015f));
        }

        [Test]
        public void Advance_SameTotalTime_SameTickCount()
        {
            var a = new SimClock(); var b = new SimClock();
            int ta = 0, tb = 0;
            for (int i = 0; i < 100; i++) ta += a.Advance(1f / 30f);
            for (int i = 0; i < 200; i++) tb += b.Advance(1f / 60f);
            Assert.LessOrEqual(System.Math.Abs(ta - tb), 1);
        }

        [Test]
        public void Advance_LongPause_CapsAt5_AndDropsBacklog()
        {
            var c = new SimClock();
            Assert.AreEqual(5, c.Advance(3.0f));
            Assert.AreEqual(1, c.Advance(0.02f));
        }

        [Test]
        public void Advance_NegativeOrNaN_ReturnsZero()
        {
            var c = new SimClock();
            Assert.AreEqual(0, c.Advance(-1f));
            Assert.AreEqual(0, c.Advance(float.NaN));
        }

        [Test]
        public void Advance_PositiveInfinity_ReturnsZero_AndClockStillWorks()
        {
            var c = new SimClock();
            Assert.AreEqual(0, c.Advance(float.PositiveInfinity));
            Assert.AreEqual(1, c.Advance(0.02f));
        }

        [Test]
        public void Alpha_IsFractionOfStep()
        {
            var c = new SimClock();
            c.Advance(0.03f);
            Assert.AreEqual(0.5f, c.Alpha, 0.01f);
        }
    }
}
