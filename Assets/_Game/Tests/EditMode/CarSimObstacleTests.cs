using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ApexBrake.Simulation;

namespace ApexBrake.Tests.EditMode
{
    public sealed class CarSimObstacleTests
    {
        static TrackPieceDef S(float len) => new TrackPieceDef { Kind = PieceKind.Straight, Length = len };
        static TrackPieceDef C(float len, float safe) => new TrackPieceDef { Kind = PieceKind.Corner, Length = len, SafeSpeed = safe };
        static TrackPieceDef B(float len, float safe) => new TrackPieceDef { Kind = PieceKind.Bump, Length = len, SafeSpeed = safe };
        static TrackPieceDef R(float len, float min, float safe, float gap) =>
            new TrackPieceDef { Kind = PieceKind.Ramp, Length = len, MinJumpSpeed = min, SafeSpeed = safe, GapLength = gap };

        static TrackLayout TrackA() => new TrackLayout(new List<TrackPieceDef> { S(100), C(60, 20), S(100) });
        static TrackLayout TrackB() => new TrackLayout(new List<TrackPieceDef> { S(100), R(40, 15, 26, 12), S(100) });

        static CarSim Sim(TrackLayout l, float distance, float speed)
        {
            var s = new CarSim(l, new CarStats(), new SimTuning(), CrashPolicy.Eliminate);
            s.SetStateForTest(distance, speed);
            return s;
        }

        static bool Has(CarSim s, SimEventKind k) => s.Events.Any(e => e.Kind == k);

        // Aracı Start'tan hemen önce, istenen hızla kurar ve o Tick'i atar.
        static CarSim EnterCorner(float speed)
        {
            var s = Sim(TrackA(), 99.9f, speed);
            s.Tick(false);
            return s;
        }

        [Test] public void Corner_AtSafeSpeed_CleanPass()
        {
            var s = EnterCorner(20f);
            Assert.IsTrue(Has(s, SimEventKind.CleanPass));
            Assert.AreEqual(20f, s.Speed, 0.001f);
            Assert.AreEqual(CarPhase.Driving, s.Phase);
        }

        [Test] public void Corner_At110Percent_SkidsAndLoses40()
        {
            var s = EnterCorner(22f);
            Assert.IsTrue(Has(s, SimEventKind.Skid));
            Assert.AreEqual(13.2f, s.Speed, 0.05f);
            Assert.AreEqual(CarPhase.Skidding, s.Phase);
        }

        [Test] public void Corner_JustBelow115Percent_StillSkids() => Assert.IsTrue(Has(EnterCorner(22.9f), SimEventKind.Skid));

        [Test] public void Corner_Above115Percent_Crashes()
        {
            var s = EnterCorner(23.5f);
            Assert.IsTrue(Has(s, SimEventKind.Crash));
            Assert.AreEqual(CarPhase.Crashed, s.Phase);
            Assert.AreEqual(0f, s.Speed);
        }

        [Test] public void Skid_ReturnsToDrivingAfter0_6s()
        {
            var s = EnterCorner(22f);
            for (int i = 0; i < 29; i++) s.Tick(true);
            Assert.AreEqual(CarPhase.Skidding, s.Phase);
            s.Tick(true);
            Assert.AreEqual(CarPhase.Driving, s.Phase);
        }

        [Test] public void Corner_CapsSpeedAtSafeWhileInside()
        {
            var s = EnterCorner(18f);
            for (int i = 0; i < 100; i++) { s.Tick(false); Assert.LessOrEqual(s.Speed, 20f + 1e-4f); }
            Assert.Less(s.Distance, 160f);
        }

        [Test] public void Corner_ExitReleasesCap()
        {
            var s = Sim(TrackA(), 159.9f, 20f);
            for (int i = 0; i < 30; i++) s.Tick(false);
            Assert.Greater(s.Speed, 20f);
        }

        [Test] public void Bump_UsesSameTwoTiers_NoCap()
        {
            var l = new TrackLayout(new List<TrackPieceDef> { S(100), B(50, 20), S(100) });
            var s = Sim(l, 99.9f, 22f);
            s.Tick(false);
            Assert.IsTrue(Has(s, SimEventKind.Skid));
            for (int i = 0; i < 60; i++) s.Tick(false);
            Assert.Greater(s.Speed, 20f);
        }

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
            var l = new TrackLayout(new List<TrackPieceDef> { S(100), B(0.2f, 10), C(0.2f, 10), S(100) });
            var s = Sim(l, 99.9f, 30f);
            s.Tick(false);
            Assert.IsTrue(Has(s, SimEventKind.Crash));
            Assert.IsFalse(Has(s, SimEventKind.CleanPass));
            Assert.AreEqual(1, s.Events.First(e => e.Kind == SimEventKind.Crash).PieceIndex);
        }

        [Test] public void LandingDirectlyIntoCorner_AppliesCornerRule()
        {
            var l = new TrackLayout(new List<TrackPieceDef> { S(100), R(40, 15, 26, 12), C(60, 20), S(100) });
            var s = Sim(l, 127.9f, 25f);
            s.Tick(false);
            Assert.IsTrue(Has(s, SimEventKind.Takeoff));
            bool landed = false, crashed = false;
            for (int i = 0; i < 100 && !crashed; i++)
            {
                s.Tick(false);
                landed |= Has(s, SimEventKind.Landed);
                crashed |= Has(s, SimEventKind.Crash);
            }
            Assert.IsTrue(landed); Assert.IsTrue(crashed);
        }
    }
}
