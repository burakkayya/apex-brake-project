using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ApexBrake.Simulation;

namespace ApexBrake.Tests.EditMode
{
    public sealed class CarSimCrashPolicyTests
    {
        static TrackPieceDef S(float len) => new TrackPieceDef { Kind = PieceKind.Straight, Length = len };
        static TrackPieceDef C(float len, float safe) => new TrackPieceDef { Kind = PieceKind.Corner, Length = len, SafeSpeed = safe };
        static TrackPieceDef R(float len, float min, float safe, float gap) =>
            new TrackPieceDef { Kind = PieceKind.Ramp, Length = len, MinJumpSpeed = min, SafeSpeed = safe, GapLength = gap };

        static TrackLayout TrackA() => new TrackLayout(new List<TrackPieceDef> { S(100), C(60, 20), S(100) });
        static TrackLayout TrackB() => new TrackLayout(new List<TrackPieceDef> { S(100), R(40, 15, 26, 12), S(100) });

        static CarSim Sim(TrackLayout l, CrashPolicy p, float distance, float speed)
        {
            var s = new CarSim(l, new CarStats(), new SimTuning(), p);
            s.SetStateForTest(distance, speed);
            return s;
        }

        static bool Has(CarSim s, SimEventKind k) => s.Events.Any(e => e.Kind == k);

        [Test]
        public void Eliminate_Crash_SetsIsOut_AndFreezes()
        {
            var s = Sim(TrackA(), CrashPolicy.Eliminate, 99.9f, 30f);
            s.Tick(false);
            Assert.IsTrue(s.IsOut); Assert.AreEqual(CarPhase.Crashed, s.Phase);
            float d = s.Distance;
            for (int i = 0; i < 200; i++) s.Tick(false);
            Assert.AreEqual(d, s.Distance); Assert.AreEqual(CarPhase.Crashed, s.Phase); Assert.IsTrue(s.IsOut);
        }

        [Test]
        public void EndRun_Crash_SetsIsOut()
        {
            var s = Sim(TrackA(), CrashPolicy.EndRun, 99.9f, 30f);
            s.Tick(false);
            Assert.IsTrue(s.IsOut);
        }

        [Test]
        public void Respawn_Corner_After1_5s_15mBeforeStart()
        {
            var s = Sim(TrackA(), CrashPolicy.Respawn, 99.9f, 30f);
            s.Tick(false);
            Assert.AreEqual(CarPhase.Crashed, s.Phase);
            for (int i = 0; i < 74; i++) s.Tick(false);
            Assert.AreEqual(CarPhase.Crashed, s.Phase);
            s.Tick(false);
            Assert.IsTrue(Has(s, SimEventKind.Respawned));
            Assert.AreEqual(85f, s.Distance, 0.01f);
            Assert.AreEqual(0f, s.Speed);
            Assert.AreEqual(CarPhase.Driving, s.Phase);
        }

        [Test]
        public void Respawn_Ramp_RunUpAllowsMinJump()
        {
            var s = Sim(TrackB(), CrashPolicy.Respawn, 127.9f, 14f);
            s.Tick(false);
            Assert.IsTrue(Has(s, SimEventKind.FellShort));
            for (int i = 0; i < 75; i++) s.Tick(false);
            Assert.AreEqual(111.125f, s.Distance, 0.01f);
        }

        [Test]
        public void Respawn_Ramp_FullThrottleClearsGap()
        {
            var s = Sim(TrackB(), CrashPolicy.Respawn, 127.9f, 14f);
            s.Tick(false);
            for (int i = 0; i < 75; i++) s.Tick(false);
            bool takeoff = false, landed = false, bad = false;
            for (int i = 0; i < 400 && !landed; i++)
            {
                s.Tick(false);
                takeoff |= Has(s, SimEventKind.Takeoff); landed |= Has(s, SimEventKind.Landed);
                bad |= Has(s, SimEventKind.Crash) || Has(s, SimEventKind.FellShort);
            }
            Assert.IsTrue(takeoff); Assert.IsTrue(landed); Assert.IsFalse(bad);
        }

        [Test]
        public void Respawn_ClampsAtTrackStart()
        {
            var l = new TrackLayout(new List<TrackPieceDef> { S(5), C(60, 20), S(100) });
            var s = Sim(l, CrashPolicy.Respawn, 4.9f, 30f);
            s.Tick(false);
            for (int i = 0; i < 75; i++) s.Tick(false);
            Assert.AreEqual(0f, s.Distance, 0.01f);
        }

        [Test]
        public void Respawn_NeverLandsInsideAnotherObstacle()
        {
            var l = new TrackLayout(new List<TrackPieceDef> { S(50), C(30, 20), S(5), C(30, 20), S(50) });
            var s = Sim(l, CrashPolicy.Respawn, 84.9f, 30f);
            s.Tick(false);
            Assert.AreEqual(CarPhase.Crashed, s.Phase);
            for (int i = 0; i < 75; i++) s.Tick(false);
            Assert.AreEqual(80f, s.Distance, 0.01f);
        }

        [Test]
        public void Respawn_Ramp_WalksBackUntilRunUpFits()
        {
            // S100 [0,100], C30 [100,130], S5 [130,135], Ramp20 [135,155] (kalkış 143), S100
            var l = new TrackLayout(new List<TrackPieceDef> { S(100), C(30, 20), S(5), R(20, 18, 26, 12), S(100) });
            const float takeoff = 143f;
            float runUp = 18f * 18f / (2f * 8f) * 1.2f; // 24.3
            var s = Sim(l, CrashPolicy.Respawn, takeoff - 0.1f, 17f);
            s.Tick(false);
            Assert.IsTrue(Has(s, SimEventKind.FellShort));
            bool respawned = false;
            for (int i = 0; i < 75 && !respawned; i++) { s.Tick(false); respawned = Has(s, SimEventKind.Respawned); }
            Assert.IsTrue(respawned);
            Assert.AreEqual(PieceKind.Straight, l.SpanAtDistance(s.Distance).Def.Kind);
            Assert.GreaterOrEqual(takeoff - s.Distance, runUp - 0.01f);
            Assert.Less(s.Distance, 100f);

            bool takeoffSeen = false, landed = false; int fellShort = 0;
            for (int i = 0; i < 3000 && !landed; i++)
            {
                s.Tick(false);
                takeoffSeen |= Has(s, SimEventKind.Takeoff);
                landed |= Has(s, SimEventKind.Landed);
                if (Has(s, SimEventKind.FellShort)) fellShort++;
            }
            Assert.IsTrue(takeoffSeen); Assert.IsTrue(landed); Assert.AreEqual(0, fellShort);
        }

        [Test]
        public void Respawn_IsNotOut()
        {
            var s = Sim(TrackA(), CrashPolicy.Respawn, 99.9f, 30f);
            s.Tick(false);
            Assert.IsFalse(s.IsOut);
            for (int i = 0; i < 75; i++) s.Tick(false);
            Assert.IsFalse(s.IsOut);
        }
    }
}
