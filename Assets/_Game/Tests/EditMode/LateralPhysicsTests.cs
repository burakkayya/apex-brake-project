using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ApexBrake.Simulation;

namespace ApexBrake.Tests.EditMode
{
    public sealed class LateralPhysicsTests
    {
        const float Step = CarSim.Step;
        const float CornerStart = 200f;
        const float CornerEnd = 262.8f;
        const float RoadEdge = 9.5f; // 5 şerit: 8 m yarı genişlik + 1.5 m banket

        static TrackPieceDef S(float len) => new TrackPieceDef { Kind = PieceKind.Straight, Length = len };
        internal static TrackPieceDef Corner(float length, float radius, int turn) =>
            new TrackPieceDef { Kind = PieceKind.Corner, Length = length, Radius = radius, TurnSign = turn };
        static TrackPieceDef B(float len, float safe) => new TrackPieceDef { Kind = PieceKind.Bump, Length = len, SafeSpeed = safe };
        static TrackPieceDef R(float len, float min, float safe, float gap) =>
            new TrackPieceDef { Kind = PieceKind.Ramp, Length = len, MinJumpSpeed = min, SafeSpeed = safe, GapLength = gap };

        // Pist V: Straight 200, Corner 62.8 (R 40), Straight 300 — giriş 200, çıkış 262.8.
        internal static TrackLayout TrackV(int turn = 1) =>
            new TrackLayout(new List<TrackPieceDef> { S(200), Corner(62.8f, 40f, turn), S(300) });

        static TrackLayout Open() => new TrackLayout(new List<TrackPieceDef> { S(3000) });

        static CarSim Car(TrackLayout l, float distance, float speed, float lateral = 0f, float target = 0f,
            CarStats stats = null, CrashPolicy policy = CrashPolicy.Eliminate)
        {
            var s = new CarSim(l, stats ?? new CarStats(), new SimTuning(), policy);
            s.SetStateForTest(distance, speed, lateral, target);
            return s;
        }

        // Girişten 1 m önce, istenen hızla.
        static CarSim AtCorner(float speed, int turn = 1, float lateral = 0f, float target = 0f, CrashPolicy policy = CrashPolicy.Eliminate) =>
            Car(TrackV(turn), CornerStart - 1f, speed, lateral, target, null, policy);

        static bool Has(CarSim s, SimEventKind k) => s.Events.Any(e => e.Kind == k);
        static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);

        sealed class Trace
        {
            public float MinLateral = float.MaxValue, MaxLateral = float.MinValue;
            public int SkidTick = -1, RecoveredTick = -1, CrashTick = -1, ExitTick = -1;
            public float CrashLateral;
            public int Crashes;
            public int Ticks;
        }

        // Tick sayısı kadar sürer; çakılınca (Eliminate) durur.
        static Trace Run(CarSim s, int ticks, Func<int, bool> brake = null)
        {
            var t = new Trace();
            for (int i = 1; i <= ticks; i++)
            {
                s.Tick(brake != null && brake(i));
                t.Ticks = i;
                t.MinLateral = Math.Min(t.MinLateral, s.Lateral);
                t.MaxLateral = Math.Max(t.MaxLateral, s.Lateral);
                if (t.ExitTick < 0 && s.Distance >= CornerEnd) t.ExitTick = i;
                if (Has(s, SimEventKind.Skid) && t.SkidTick < 0) t.SkidTick = i;
                if (Has(s, SimEventKind.Recovered) && t.RecoveredTick < 0) t.RecoveredTick = i;
                if (Has(s, SimEventKind.Crash))
                {
                    t.Crashes++;
                    if (t.CrashTick < 0) { t.CrashTick = i; t.CrashLateral = s.Lateral; }
                }
                if (s.IsOut) break;
            }
            return t;
        }

        [Test]
        public void Straight_HoldsLine_NoLateralMotion()
        {
            var s = Car(Open(), 0f, 0f);
            for (int i = 0; i < 500; i++)
            {
                s.Tick(i % 7 == 0);
                Assert.Less(Math.Abs(s.Lateral), 0.001f, "tick " + i);
            }
        }

        [Test]
        public void Straight_OffsetCar_ReturnsToTarget_Within2s()
        {
            var s = Car(Open(), 100f, 20f, lateral: 3f, target: 0f);
            var t = Run(s, 100);
            Assert.Less(Math.Abs(s.Lateral), 0.3f);
            Assert.GreaterOrEqual(t.MinLateral, -1.0f, "overshoot");
        }

        [Test]
        public void Corner_AtLimit_StaysWithinHalfMetre()
        {
            var s = AtCorner(21.0f);
            Assert.AreEqual(21.909f, s.CornerLimit(TrackV().SpanAt(1).Def), 0.01f);
            var t = Run(s, 400);
            Assert.Greater(t.ExitTick, 0, "never left the corner");
            // Viraj boyunca (çıkışa kadar) ölçülür.
            var s2 = AtCorner(21.0f);
            float max = 0f; bool skid = false;
            for (int i = 0; i < 400 && s2.Distance < CornerEnd; i++)
            {
                s2.Tick(false);
                max = Math.Max(max, Math.Abs(s2.Lateral));
                skid |= Has(s2, SimEventKind.Skid);
            }
            Assert.Less(max, 0.5f);
            Assert.IsFalse(skid);
            Assert.IsFalse(t.SkidTick > 0);
        }

        [Test]
        public void Corner_At110Percent_SlidesOutward_ThenRecovers()
        {
            var s = AtCorner(24.1f);
            var t = Run(s, 600);
            float outward = -t.MinLateral; // sağa viraj: dışa = sol = negatif
            Assert.GreaterOrEqual(outward, 0.8f);
            Assert.LessOrEqual(outward, 6f);
            Assert.Greater(t.SkidTick, 0, "no Skid");
            Assert.AreEqual(-1, t.CrashTick, "left the road");
            Assert.Greater(t.ExitTick, 0);
            Assert.Greater(t.RecoveredTick, 0, "never recovered");
            Assert.LessOrEqual((t.RecoveredTick - t.ExitTick) * Step, 3f, "recovered too late");
        }

        [Test]
        public void Corner_At140Percent_NoBrake_LeavesRoad()
        {
            var s = AtCorner(30.7f);
            var t = Run(s, 600);
            Assert.Greater(t.CrashTick, 0, "no Crash");
            Assert.Greater(Math.Abs(t.CrashLateral), RoadEdge);
            Assert.IsTrue(s.IsOut);
        }

        [Test]
        public void Corner_At140Percent_BrakingHard_SlidesLessThanNoBrake()
        {
            var free = AtCorner(30.7f);
            var braked = AtCorner(30.7f);
            float freeSpeed = float.NaN, brakedSpeed = float.NaN;
            int freeCrash = -1, brakedCrash = -1;
            for (int i = 1; i <= 600; i++)
            {
                if (!free.IsOut) { free.Tick(false); if (Has(free, SimEventKind.Crash)) freeCrash = i; }
                if (!braked.IsOut) { braked.Tick(braked.Distance >= CornerStart); if (Has(braked, SimEventKind.Crash)) brakedCrash = i; }
                if (i == 75) { freeSpeed = free.Speed; brakedSpeed = braked.Speed; }
            }
            Assert.Greater(freeCrash, 0, "precondition: no-brake car leaves the road");
            Assert.Greater(freeCrash, 75, "precondition: no-brake car still on the road after 1.5 s");
            Assert.IsTrue(brakedCrash < 0 || brakedCrash > freeCrash, $"braked crash {brakedCrash} vs free {freeCrash}");
            Assert.LessOrEqual(brakedSpeed, freeSpeed - 10f);
        }

        [Test]
        public void Braking_ReducesGrip_SlidesMoreAtSameSpeed()
        {
            var free = Car(TrackV(), CornerStart + 0.5f, 22.5f);
            var braked = Car(TrackV(), CornerStart + 0.5f, 22.5f);
            for (int i = 0; i < 10; i++) { free.Tick(false); braked.Tick(true); }
            Assert.Greater(Math.Abs(braked.LateralVel), Math.Abs(free.LateralVel));
        }

        [Test]
        public void LeftCorner_SlidesRight()
        {
            var s = AtCorner(24.1f, turn: -1);
            var t = Run(s, 600);
            Assert.GreaterOrEqual(t.MaxLateral, 0.8f);
            Assert.Greater(t.MinLateral, -0.3f);
            Assert.AreEqual(-1, t.CrashTick);
        }

        [Test]
        public void LoadedTyre_AcceleratesSlower()
        {
            var corner = Car(TrackV(), CornerStart + 1f, 21.0f);
            var straight = Car(Open(), 100f, 21.0f);
            for (int i = 0; i < 20; i++) { corner.Tick(false); straight.Tick(false); }
            Assert.Less(corner.Speed - 21f, straight.Speed - 21f);
            Assert.Greater(corner.Speed, 21f);
        }

        [Test]
        public void NoSpeedCapInsideCorner()
        {
            var s = AtCorner(15f);
            float max = 0f;
            while (s.Distance < CornerEnd)
            {
                s.Tick(false);
                if (s.Distance < CornerEnd) max = Math.Max(max, s.Speed);
            }
            Assert.Greater(max, 15.5f);
        }

        [Test]
        public void Sliding_ScrubsSpeed()
        {
            float entry = 1.2f * 21.909f;
            var s = AtCorner(entry);
            for (int i = 0; i < 50; i++) s.Tick(false);
            Assert.IsFalse(s.IsOut);
            Assert.Less(s.Speed, entry);
        }

        [Test]
        public void SlipAngle_SignMatchesLateralVel()
        {
            var s = AtCorner(26f);
            bool seen = false;
            for (int i = 0; i < 300 && !s.IsOut; i++)
            {
                s.Tick(false);
                float expected = (float)Math.Atan2(s.LateralVel, Math.Max(s.Speed, 1f));
                Assert.AreEqual(expected, s.SlipAngle, 1e-4f, "tick " + i);
                if (Math.Abs(s.LateralVel) > 0.01f)
                {
                    Assert.AreEqual(Math.Sign(s.LateralVel), Math.Sign(s.SlipAngle), "tick " + i);
                    seen = true;
                }
            }
            Assert.IsTrue(seen);
        }

        [Test]
        public void OuterLaneTarget_AtLimit_StaysOnRoad()
        {
            var s = AtCorner(21.0f, lateral: -6.4f, target: -6.4f);
            var t = Run(s, 400);
            Assert.AreEqual(-1, t.CrashTick);
            Assert.Greater(t.ExitTick, 0);
            Assert.IsFalse(s.IsOut);
        }

        [Test]
        public void EdgeProtection_ClampsInsteadOfCrash()
        {
            var s = Car(Open(), 100f, 20f);
            s.ProtectFromEdge(0.5f);
            s.AddLateralVel(-40f);
            for (int i = 0; i < 25; i++)
            {
                s.Tick(false);
                Assert.IsFalse(Has(s, SimEventKind.Crash), "tick " + i);
                Assert.LessOrEqual(Math.Abs(s.Lateral), RoadEdge + 1e-4f, "tick " + i);
            }
            Assert.IsFalse(s.IsOut);
            s.AddLateralVel(-40f);
            var t = Run(s, 50);
            Assert.Greater(t.CrashTick, 0);
            Assert.IsTrue(s.IsOut);
        }

        [Test]
        public void ZeroSpeed_NoNaN()
        {
            var s = Car(TrackV(), 220f, 0f, lateral: 2f, target: 0f);
            for (int i = 0; i < 200; i++)
            {
                s.Tick(true);
                Assert.IsTrue(Finite(s.Lateral) && Finite(s.LateralVel) && Finite(s.SlipAngle) && Finite(s.LateralLoad), "tick " + i);
            }
            Assert.AreEqual(0f, s.Speed);
            Assert.Less(Math.Abs(s.Lateral), 2.5f);
        }

        [Test]
        public void HugeLateralKick_NoNaN_AndCrashesOnce()
        {
            var s = Car(Open(), 100f, 20f);
            s.AddLateralVel(1e6f);
            int crashes = 0;
            for (int i = 0; i < 100; i++)
            {
                s.Tick(false);
                crashes += s.Events.Count(e => e.Kind == SimEventKind.Crash);
                Assert.IsTrue(Finite(s.Lateral) && Finite(s.LateralVel) && Finite(s.SlipAngle) && Finite(s.Speed), "tick " + i);
            }
            Assert.AreEqual(1, crashes);
        }

        // Tümsek [200,212], güvenli 20.
        static TrackLayout BumpTrack() => new TrackLayout(new List<TrackPieceDef> { S(200), B(12, 20), S(300) });

        [Test]
        public void Bump_Between100And115_GoesLight_NotCrash()
        {
            var s = Car(BumpTrack(), 199.8f, 21f); // oran 1.05
            s.Tick(false);
            Assert.IsTrue(Has(s, SimEventKind.Light));
            Assert.IsFalse(Has(s, SimEventKind.Crash));
            Assert.IsFalse(Has(s, SimEventKind.CleanPass));
            Assert.AreEqual(1.5f, s.LateralVel, 1e-4f, "BumpKick towards + when Lateral is 0");

            // Karşılaştırma: aynı itmeyi tam tutunmayla alan araç.
            var reference = Car(Open(), 100f, 21f);
            reference.AddLateralVel(1.5f);
            for (int i = 0; i < 30; i++)
            {
                s.Tick(false); reference.Tick(false);
                Assert.IsFalse(Has(s, SimEventKind.Crash));
            }
            Assert.Greater(s.Lateral, reference.Lateral + 0.1f, "grip not reduced while light");
            Assert.IsFalse(s.IsOut);
        }

        [Test]
        public void Bump_Above115_Crashes()
        {
            var s = Car(BumpTrack(), 199.8f, 23.5f); // oran 1.175
            s.Tick(false);
            Assert.IsTrue(Has(s, SimEventKind.Crash));
            Assert.IsTrue(s.IsOut);
        }

        [Test]
        public void Airborne_FreezesLateral()
        {
            var l = new TrackLayout(new List<TrackPieceDef> { S(100), R(40, 15, 26, 12), S(100) });
            var s = Car(l, 127.9f, 20f, lateral: 2f, target: 0f);
            s.Tick(false);
            Assert.IsTrue(Has(s, SimEventKind.Takeoff));
            float lat = s.Lateral, vel = s.LateralVel;
            bool landed = false;
            for (int i = 0; i < 100 && !landed; i++)
            {
                s.Tick(false);
                landed = Has(s, SimEventKind.Landed);
                Assert.AreEqual(lat, s.Lateral, "tick " + i);
                Assert.AreEqual(vel, s.LateralVel, "tick " + i);
            }
            Assert.IsTrue(landed);
            s.Tick(false);
            Assert.AreNotEqual(lat, s.Lateral, "lateral must resume after landing");
        }

        [Test]
        public void Respawn_ResetsLateralToTarget()
        {
            var s = AtCorner(30.7f, policy: CrashPolicy.Respawn);
            s.TargetLateral = 1.6f;
            bool crashed = false, respawned = false;
            for (int i = 0; i < 600 && !respawned; i++)
            {
                s.Tick(false);
                crashed |= Has(s, SimEventKind.Crash);
                respawned = Has(s, SimEventKind.Respawned);
            }
            Assert.IsTrue(crashed); Assert.IsTrue(respawned);
            Assert.AreEqual(1.6f, s.Lateral);
            Assert.AreEqual(0f, s.LateralVel);
            Assert.AreEqual(CarPhase.Driving, s.Phase);
            Assert.AreEqual(CornerStart - 15f, s.Distance, 0.01f);
        }

        [Test]
        public void SameInputs_IdenticalLateralState()
        {
            var l = new TrackLayout(new List<TrackPieceDef>
                { S(200), Corner(62.8f, 40f, 1), S(100), Corner(40f, 25f, -1), S(150), B(12, 20), S(200), Corner(62.8f, 40f, -1), S(800) });
            var a = new CarSim(l, new CarStats(), new SimTuning(), CrashPolicy.Respawn);
            var b = new CarSim(l, new CarStats(), new SimTuning(), CrashPolicy.Respawn);
            var rng = new SimRandom(99);
            bool moved = false;
            for (int i = 0; i < 1500; i++)
            {
                bool br = rng.NextFloat() < 0.3f;
                if (i % 200 == 50) { a.AddLateralVel(2f); b.AddLateralVel(2f); }
                a.Tick(br); b.Tick(br);
                Assert.AreEqual(a.Lateral, b.Lateral, "tick " + i);
                Assert.AreEqual(a.LateralVel, b.LateralVel, "tick " + i);
                Assert.AreEqual(a.Distance, b.Distance, "tick " + i);
                moved |= Math.Abs(a.Lateral) > 0.1f;
            }
            Assert.IsTrue(moved);
        }

        [Test]
        public void CornerLimit_ScalesWithGrip()
        {
            var def = TrackV().SpanAt(1).Def;
            var a = new CarSim(TrackV(), new CarStats { Grip = 12f }, new SimTuning(), CrashPolicy.Eliminate);
            var b = new CarSim(TrackV(), new CarStats { Grip = 13.5f }, new SimTuning(), CrashPolicy.Eliminate);
            Assert.AreEqual(21.909f, a.CornerLimit(def), 0.01f);
            Assert.AreEqual(23.238f, b.CornerLimit(def), 0.01f);
        }

        /// <summary>10 Tick fren, 4 Tick frensiz düzde; değerlendirme Tick'inde (frensiz) 200'ü evalSpeed ile geçer.</summary>
        static CarSim ApproachWithBrake(CarStats stats, float evalSpeed)
        {
            const int nb = 10, nf = 4;
            float v0 = evalSpeed - nf * stats.Acceleration * Step + nb * stats.BrakeDecel * Step;
            float d = 0f, v = v0;
            for (int i = 0; i < nb; i++) { v -= stats.BrakeDecel * Step; d += v * Step; }
            for (int i = 0; i < nf; i++) { v += stats.Acceleration * Step; d += v * Step; }
            float vLast = v + stats.Acceleration * Step;
            float start = CornerStart - d - 0.5f * vLast * Step;
            var s = Car(TrackV(), start, v0, stats: stats);
            for (int i = 0; i < nb; i++) s.Tick(true);
            for (int i = 0; i < nf; i++) s.Tick(false);
            Assert.AreEqual(evalSpeed, s.Speed, 0.05f, "speed at evaluation");
            Assert.Less(s.Distance, CornerStart);
            s.Tick(false);
            Assert.GreaterOrEqual(s.Distance, CornerStart);
            return s;
        }

        [Test]
        public void Perfect_UsesCarsOwnCornerLimit()
        {
            var grippy = ApproachWithBrake(new CarStats { Grip = 13.5f }, 22.5f);
            Assert.IsTrue(Has(grippy, SimEventKind.PerfectBrake));
            var normal = ApproachWithBrake(new CarStats { Grip = 12f }, 22.5f);
            Assert.IsFalse(Has(normal, SimEventKind.PerfectBrake));
            Assert.IsFalse(Has(normal, SimEventKind.GoodBrake));
        }
    }
}
