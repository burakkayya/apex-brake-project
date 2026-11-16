using System.Collections.Generic;
using NUnit.Framework;
using ApexBrake.Simulation;

namespace ApexBrake.Tests.EditMode
{
    public sealed class CarSimDrivingTests
    {
        static CarSim Make(float straight = 2000f)
        {
            var layout = new TrackLayout(new List<TrackPieceDef> { new TrackPieceDef { Kind = PieceKind.Straight, Length = straight } });
            return new CarSim(layout, new CarStats(), new SimTuning(), CrashPolicy.Eliminate);
        }

        static void Run(CarSim s, int n, bool brake) { for (int i = 0; i < n; i++) s.Tick(brake); }

        [Test]
        public void StartsAtRestInDrivingPhase()
        {
            var s = Make();
            Assert.AreEqual(0f, s.Distance); Assert.AreEqual(0f, s.Speed); Assert.AreEqual(CarPhase.Driving, s.Phase);
        }

        [Test] public void Accelerates_8PerSecond() { var s = Make(); Run(s, 50, false); Assert.AreEqual(8f, s.Speed, 0.001f); }
        [Test] public void ClampsAtTopSpeed() { var s = Make(); Run(s, 500, false); Assert.AreEqual(30f, s.Speed); }

        [Test]
        public void Braking_Decelerates25PerSecond()
        {
            var s = Make(); Run(s, 500, false); Run(s, 20, true);
            Assert.AreEqual(20f, s.Speed, 0.01f);
        }

        [Test]
        public void Braking_NeverGoesNegative()
        {
            var s = Make(); Run(s, 500, false); Run(s, 200, true);
            Assert.AreEqual(0f, s.Speed);
            float d = s.Distance; s.Tick(true);
            Assert.AreEqual(d, s.Distance);
        }

        [Test]
        public void Distance_IntegratesSpeed()
        {
            var s = Make(); Run(s, 500, false);
            float d = s.Distance; Run(s, 50, false);
            Assert.AreEqual(d + 30f, s.Distance, 0.01f);
        }

        [Test]
        public void Finishes_AtTrackEnd_AndEmitsOnce()
        {
            var s = Make(100f);
            int finished = 0;
            for (int i = 0; i < 2000 && s.Phase != CarPhase.Finished; i++)
            {
                s.Tick(false);
                foreach (var e in s.Events) if (e.Kind == SimEventKind.Finished) finished++;
            }
            Assert.AreEqual(CarPhase.Finished, s.Phase); Assert.AreEqual(100f, s.Distance);
            for (int i = 0; i < 10; i++) { s.Tick(false); foreach (var e in s.Events) if (e.Kind == SimEventKind.Finished) finished++; }
            Assert.AreEqual(1, finished);
        }

        [Test]
        public void Tick_AfterFinish_IsNoOp()
        {
            var s = Make(50f);
            for (int i = 0; i < 2000 && s.Phase != CarPhase.Finished; i++) s.Tick(false);
            float d = s.Distance, v = s.Speed; s.Tick(false); s.Tick(true);
            Assert.AreEqual(d, s.Distance); Assert.AreEqual(v, s.Speed); Assert.AreEqual(0, s.Events.Count);
        }

        [Test]
        public void SameInputs_ProduceIdenticalState()
        {
            var a = Make(); var b = Make();
            var rng = new System.Random(42);
            for (int i = 0; i < 1000; i++) { bool br = rng.Next(3) == 0; a.Tick(br); b.Tick(br); }
            Assert.AreEqual(a.Distance, b.Distance); Assert.AreEqual(a.Speed, b.Speed);
        }
    }
}
