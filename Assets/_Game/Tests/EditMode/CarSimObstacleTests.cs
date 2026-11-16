using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ApexBrake.Simulation;

namespace ApexBrake.Tests.EditMode
{
    public sealed class CarSimObstacleTests
    {
        static TrackPieceDef S(float len) => new TrackPieceDef { Kind = PieceKind.Straight, Length = len };
        static TrackPieceDef B(float len, float safe) => new TrackPieceDef { Kind = PieceKind.Bump, Length = len, SafeSpeed = safe };
        static TrackPieceDef R(float len, float min, float safe, float gap) =>
            new TrackPieceDef { Kind = PieceKind.Ramp, Length = len, MinJumpSpeed = min, SafeSpeed = safe, GapLength = gap };

        static TrackLayout TrackB() => new TrackLayout(new List<TrackPieceDef> { S(100), R(40, 15, 26, 12), S(100) });

        static CarSim Sim(TrackLayout l, float distance, float speed)
        {
            var s = new CarSim(l, new CarStats(), new SimTuning(), CrashPolicy.Eliminate);
            s.SetStateForTest(distance, speed);
            return s;
        }

        static bool Has(CarSim s, SimEventKind k) => s.Events.Any(e => e.Kind == k);

        // Rampa: kalkış noktası 128. Aracı 127.9'a kurup bir Tick atarız.
        static CarSim Takeoff(float speed)
        {
            var s = Sim(TrackB(), 127.9f, speed);
            s.Tick(false);
            return s;
        }

        [Test] public void Ramp_BetweenMinAndSafe_TakesOffAndLands()
        {
            var s = Takeoff(20f);
            Assert.IsTrue(Has(s, SimEventKind.Takeoff));
            Assert.AreEqual(CarPhase.Airborne, s.Phase);
            bool landed = false;
            for (int i = 0; i < 100 && !landed; i++) { s.Tick(false); landed = Has(s, SimEventKind.Landed); }
            Assert.IsTrue(landed);
            Assert.AreEqual(CarPhase.Driving, s.Phase);
            Assert.AreEqual(20f, s.Speed, 0.001f);
        }

        [Test] public void Ramp_Airborne_IgnoresBrake()
        {
            var s = Takeoff(20f);
            s.Tick(true);
            Assert.AreEqual(20f, s.Speed, 0.001f);
        }

        [Test] public void Ramp_BelowMin_FellShort()
        {
            var s = Takeoff(14f);
            Assert.IsTrue(Has(s, SimEventKind.FellShort));
            Assert.AreEqual(CarPhase.Crashed, s.Phase);
        }

        [Test] public void Ramp_AboveSafe_Crashes()
        {
            var s = Takeoff(27f);
            Assert.IsTrue(Has(s, SimEventKind.Crash));
            Assert.AreEqual(CarPhase.Crashed, s.Phase);
        }

        [Test] public void MultipleBoundariesInOneTick_AllEvaluated()
        {
            // Viraj girişi artık çakılma kararı vermediğinden ikinci sınır da bir tümsek (Faz 2b).
            var l = new TrackLayout(new List<TrackPieceDef> { S(100), B(0.2f, 30), B(0.2f, 20), S(100) });
            var s = Sim(l, 99.9f, 30f);
            s.Tick(false);
            Assert.IsTrue(s.Events.Any(e => e.Kind == SimEventKind.CleanPass && e.PieceIndex == 1));
            Assert.IsTrue(s.Events.Any(e => e.Kind == SimEventKind.Crash && e.PieceIndex == 2));
        }
    }
}
