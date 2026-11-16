using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ApexBrake.Simulation;

namespace ApexBrake.Tests.EditMode
{
    public sealed class BoostTests
    {
        const float Step = CarSim.Step;

        static TrackPieceDef S(float len) => new TrackPieceDef { Kind = PieceKind.Straight, Length = len };
        static TrackPieceDef C(float len, float safe) => new TrackPieceDef { Kind = PieceKind.Corner, Length = len, SafeSpeed = safe, TurnSign = 1 };
        static TrackPieceDef B(float len, float safe) => new TrackPieceDef { Kind = PieceKind.Bump, Length = len, SafeSpeed = safe };
        static TrackPieceDef R(float len, float min, float safe, float gap) =>
            new TrackPieceDef { Kind = PieceKind.Ramp, Length = len, MinJumpSpeed = min, SafeSpeed = safe, GapLength = gap };

        // Pist A: Straight 200, Corner 60 (Safe 20), Straight 400 — viraj girişi 200, çıkışı 260.
        static TrackLayout TrackA() => new TrackLayout(new List<TrackPieceDef> { S(200), C(60, 20), S(400) });

        static bool Has(CarSim s, SimEventKind k) => s.Events.Any(e => e.Kind == k);

        /// <summary>
        /// Aracı, brakeTicks fren + freeTicks frensiz Tick'ten sonraki Tick'te (değerlendirme Tick'i) evalPoint'i
        /// evalSpeed başlangıç hızıyla geçecek şekilde kurar ve bu diziyi uygular. Değerlendirme Tick'i
        /// brakeOnEntry ise frenli atılır. Dönüşte araç değerlendirme Tick'ini atmış olur.
        /// </summary>
        static CarSim Approach(TrackLayout layout, float evalPoint, float evalSpeed, int brakeTicks, int freeTicks, bool brakeOnEntry = false)
        {
            var stats = new CarStats();
            float v0 = evalSpeed - freeTicks * stats.Acceleration * Step + brakeTicks * stats.BrakeDecel * Step;
            if (brakeOnEntry) v0 = evalSpeed + brakeTicks * stats.BrakeDecel * Step; // fren girişe kadar sürer

            // Kinematiği ayrıca yürüt: başlangıçtan değerlendirme Tick'inin başına kadar alınan yol.
            float d = 0f, v = v0;
            int nb = brakeOnEntry ? brakeTicks : brakeTicks;
            int nf = brakeOnEntry ? 0 : freeTicks;
            for (int i = 0; i < nb; i++) { v -= stats.BrakeDecel * Step; d += v * Step; }
            for (int i = 0; i < nf; i++) { v += stats.Acceleration * Step; d += v * Step; }
            float vLast = brakeOnEntry ? v - stats.BrakeDecel * Step : v + stats.Acceleration * Step;
            float start = evalPoint - d - 0.5f * vLast * Step;

            var s = new CarSim(layout, stats, new SimTuning(), CrashPolicy.Eliminate);
            s.SetStateForTest(start, v0);
            for (int i = 0; i < nb; i++) { s.Tick(true); Assert.IsFalse(s.Events.Any(), "event before evaluation tick"); }
            for (int i = 0; i < nf; i++) { s.Tick(false); Assert.IsFalse(s.Events.Any(), "event before evaluation tick"); }
            Assert.AreEqual(evalSpeed, s.Speed, 0.3f, "speed at evaluation");
            s.Tick(brakeOnEntry);
            return s;
        }

        static CarSim EarnPerfectOnA() => Approach(TrackA(), 200f, 19.4f, 10, 4);

        // Boost başlayana kadar frensiz sür; BoostStarted Tick'inde durur.
        static void RunUntilBoostStarted(CarSim s)
        {
            for (int i = 0; i < 500; i++)
            {
                if (Has(s, SimEventKind.BoostStarted)) return;
                s.Tick(false);
            }
            Assert.Fail("BoostStarted never came");
        }

        static bool NoBrakeBonus(CarSim s) => !Has(s, SimEventKind.PerfectBrake) && !Has(s, SimEventKind.GoodBrake);

        [Test]
        public void ReleaseJustBeforeEntry_At97Percent_EarnsPerfect()
        {
            var s = EarnPerfectOnA();
            Assert.IsTrue(Has(s, SimEventKind.CleanPass));
            Assert.IsTrue(Has(s, SimEventKind.PerfectBrake));
            Assert.IsFalse(Has(s, SimEventKind.GoodBrake));
            Assert.AreEqual(1, s.PerfectCount);
            Assert.AreEqual(0, s.GoodCount);
        }

        [Test]
        public void ReleaseJustBeforeEntry_At90Percent_EarnsGood()
        {
            var s = Approach(TrackA(), 200f, 18f, 10, 4);
            Assert.IsTrue(Has(s, SimEventKind.CleanPass));
            Assert.IsTrue(Has(s, SimEventKind.GoodBrake));
            Assert.IsFalse(Has(s, SimEventKind.PerfectBrake));
            Assert.AreEqual(1, s.GoodCount);
            Assert.AreEqual(0, s.PerfectCount);
        }

        [Test]
        public void EntryBelow85Percent_EarnsNothing()
        {
            var s = Approach(TrackA(), 200f, 16f, 10, 4);
            Assert.IsTrue(Has(s, SimEventKind.CleanPass));
            Assert.IsTrue(NoBrakeBonus(s));
        }

        [Test]
        public void NeverBraked_InBand_EarnsNothing()
        {
            var s = Approach(TrackA(), 200f, 19.5f, 0, 0);
            Assert.IsTrue(Has(s, SimEventKind.CleanPass));
            Assert.IsTrue(NoBrakeBonus(s));
        }

        [Test]
        public void StillBrakingAtEntry_EarnsNothing()
        {
            var s = Approach(TrackA(), 200f, 19.4f, 10, 0, brakeOnEntry: true);
            Assert.IsTrue(Has(s, SimEventKind.CleanPass));
            Assert.IsTrue(NoBrakeBonus(s));
        }

        [Test]
        public void ReleasedTooEarly_EarnsNothing()
        {
            var s = Approach(TrackA(), 200f, 19.4f, 10, 29); // giriş bırakıştan 30 Tick sonra
            Assert.IsTrue(Has(s, SimEventKind.CleanPass));
            Assert.IsTrue(NoBrakeBonus(s));
        }

        [Test]
        public void BrakeTapShorterThan0_1s_EarnsNothing()
        {
            var s = Approach(TrackA(), 200f, 19.4f, 3, 4);
            Assert.IsTrue(Has(s, SimEventKind.CleanPass));
            Assert.IsTrue(NoBrakeBonus(s));
        }

        [Test]
        public void Skid_EarnsNothing()
        {
            var s = Approach(TrackA(), 200f, 21f, 10, 4);
            Assert.IsTrue(Has(s, SimEventKind.Skid));
            Assert.IsTrue(NoBrakeBonus(s));
            for (int i = 0; i < 300; i++) { s.Tick(false); Assert.IsFalse(Has(s, SimEventKind.BoostStarted)); }
            Assert.AreEqual(0, s.PerfectCount + s.GoodCount);
        }

        [Test]
        public void Boost_StartsAtCornerExit_NotBefore()
        {
            var s = EarnPerfectOnA();
            Assert.IsFalse(s.IsBoosting);
            for (int i = 0; i < 500; i++)
            {
                float before = s.Distance;
                s.Tick(false);
                if (Has(s, SimEventKind.BoostStarted))
                {
                    Assert.Less(before, 260f);
                    Assert.GreaterOrEqual(s.Distance, 260f);
                    Assert.IsTrue(s.IsBoosting);
                    Assert.AreEqual(1.2f, s.BoostFactor, 1e-5f);
                    return;
                }
                Assert.IsFalse(s.IsBoosting, "boosting inside the corner");
                Assert.AreEqual(1f, s.BoostFactor);
                Assert.Less(s.Distance, 260f);
            }
            Assert.Fail("BoostStarted never came");
        }

        [Test]
        public void PerfectBoost_Reaches36_AndLasts1_5s()
        {
            var s = EarnPerfectOnA();
            RunUntilBoostStarted(s);
            float max = 0f;
            for (int i = 1; i <= 75; i++)
            {
                s.Tick(false);
                max = System.Math.Max(max, s.Speed);
                if (i < 75) { Assert.IsFalse(Has(s, SimEventKind.BoostEnded), "ended early at " + i); Assert.IsTrue(s.IsBoosting); }
            }
            Assert.IsTrue(Has(s, SimEventKind.BoostEnded));
            Assert.IsFalse(s.IsBoosting);
            Assert.AreEqual(1f, s.BoostFactor);
            Assert.AreEqual(36f, max, 0.001f);
        }

        [Test]
        public void GoodBoost_CapsAt33()
        {
            var s = Approach(TrackA(), 200f, 18f, 10, 4);
            RunUntilBoostStarted(s);
            Assert.AreEqual(1.1f, s.BoostFactor, 1e-5f);
            float max = 0f;
            for (int i = 0; i < 74; i++) { s.Tick(false); max = System.Math.Max(max, s.Speed); }
            Assert.AreEqual(33f, max, 0.001f);
        }

        [Test]
        public void Boost_AcceleratesAt16PerSecond()
        {
            var s = EarnPerfectOnA();
            RunUntilBoostStarted(s);
            float v = s.Speed;
            for (int i = 0; i < 25; i++) s.Tick(false);
            Assert.AreEqual(v + 8f, s.Speed, 0.01f);
        }

        [Test]
        public void AfterBoost_SpeedDecaysTo30_At4PerSecond()
        {
            var s = EarnPerfectOnA();
            RunUntilBoostStarted(s);
            for (int i = 0; i < 75; i++) s.Tick(false);
            Assert.IsTrue(Has(s, SimEventKind.BoostEnded));
            Assert.AreEqual(36f, s.Speed, 0.001f);
            for (int i = 0; i < 50; i++) s.Tick(false);
            Assert.AreEqual(32f, s.Speed, 0.01f);
            for (int i = 0; i < 100; i++) { s.Tick(false); Assert.GreaterOrEqual(s.Speed, 30f); }
            Assert.AreEqual(30f, s.Speed, 1e-4f);
        }

        [Test]
        public void Bump_BoostStartsImmediately()
        {
            var l = new TrackLayout(new List<TrackPieceDef> { S(200), B(12, 20), S(400) });
            var s = Approach(l, 200f, 19.4f, 10, 4);
            Assert.IsTrue(Has(s, SimEventKind.PerfectBrake));
            Assert.IsTrue(Has(s, SimEventKind.BoostStarted));
            Assert.IsTrue(s.IsBoosting);
        }

        [Test]
        public void Ramp_BoostStartsOnLanding()
        {
            // Rampa [200,240], kalkış 228, iniş 240.
            var l = new TrackLayout(new List<TrackPieceDef> { S(200), R(40, 15, 26, 12), S(400) });
            var s = Approach(l, 228f, 25f, 10, 4);
            Assert.IsTrue(Has(s, SimEventKind.Takeoff));
            Assert.IsTrue(Has(s, SimEventKind.PerfectBrake));
            Assert.IsFalse(Has(s, SimEventKind.BoostStarted));
            for (int i = 0; i < 200; i++)
            {
                s.Tick(false);
                bool landed = Has(s, SimEventKind.Landed), started = Has(s, SimEventKind.BoostStarted);
                Assert.AreEqual(landed, started, "BoostStarted must come on the Landed tick");
                if (landed) { Assert.IsTrue(s.IsBoosting); return; }
                Assert.IsFalse(s.IsBoosting);
            }
            Assert.Fail("never landed");
        }

        [Test]
        public void CrashBeforeExit_DropsEarnedBoost()
        {
            // Plan "Straight 1" ara parçası kullanıyordu; viraj içinde hız 20 iken 1 m üç Tick sürer ve boost kural gereği
            // 260'ta (çıkışta) başlardı. Çıkış ile çakılmanın aynı Tick'te olması için ikinci viraj doğrudan bitişik (Ruling).
            var l = new TrackLayout(new List<TrackPieceDef> { S(200), C(60, 20), C(60, 10), S(400) });
            var s = Approach(l, 200f, 19.4f, 10, 4);
            Assert.IsTrue(Has(s, SimEventKind.PerfectBrake));
            bool crashed = false;
            for (int i = 0; i < 500 && !crashed; i++)
            {
                s.Tick(false);
                crashed = Has(s, SimEventKind.Crash);
                Assert.IsFalse(Has(s, SimEventKind.BoostStarted));
            }
            Assert.IsTrue(crashed);
            Assert.IsFalse(s.IsBoosting);
            for (int i = 0; i < 100; i++) { s.Tick(false); Assert.IsFalse(Has(s, SimEventKind.BoostStarted)); }
        }
    }
}
