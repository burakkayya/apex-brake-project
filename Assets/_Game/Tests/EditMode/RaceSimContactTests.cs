using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ApexBrake.Simulation;

namespace ApexBrake.Tests.EditMode
{
    public sealed class RaceSimContactTests
    {
        static TrackPieceDef S(float len, int lanes = 5) => new TrackPieceDef { Kind = PieceKind.Straight, Length = len, LaneCount = lanes };
        static TrackPieceDef R(float len, float min, float safe, float gap) =>
            new TrackPieceDef { Kind = PieceKind.Ramp, Length = len, MinJumpSpeed = min, SafeSpeed = safe, GapLength = gap };

        static TrackLayout Open() => new TrackLayout(new List<TrackPieceDef> { S(2000) });
        // Şerit 0 ve 1, 2 şeritli parçada aynı etkin şeride (0, yanal −1.6) düşer.
        static TrackLayout Narrow() => new TrackLayout(new List<TrackPieceDef> { S(100), S(500, 2) });

        static RacerSetup P(string name, int lane, CarStats stats = null, CrashPolicy policy = CrashPolicy.Eliminate) =>
            new RacerSetup { Name = name, HomeLane = lane, Stats = stats ?? new CarStats(), Policy = policy };

        static RaceSim Race(TrackLayout l, params RacerSetup[] setups) => new RaceSim(l, new SimTuning(), setups);

        // Aracı o mesafedeki ev çizgisine oturtur.
        static void Set(RaceSim r, int i, float d, float v) => r.SetStateForTest(i, d, v);
        static void Set(RaceSim r, int i, float d, float v, float lateral)
        {
            r.SetStateForTest(i, d, v);
            r.Racers[i].Car.SetStateForTest(d, v, lateral, r.Racers[i].Car.TargetLateral);
        }

        static bool Has(Racer r, SimEventKind k) => r.Car.Events.Any(e => e.Kind == k);
        static int Count(Racer r, SimEventKind k) => r.Car.Events.Count(e => e.Kind == k);
        static bool AnyContact(Racer r) => Has(r, SimEventKind.RearHit) || Has(r, SimEventKind.Bumped) || Has(r, SimEventKind.SideHit);
        static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);

        static void Tick(RaceSim r, params bool[] braking) => r.Tick(braking.Length == 0 ? new bool[r.Racers.Count] : braking);

        [Test]
        public void Start_CarsSitOnTheirHomeLines()
        {
            var r = Race(Open(), P("A", 0), P("B", 2), P("C", 4));
            Assert.AreEqual(-6.4f, r.Racers[0].Car.Lateral, 1e-4f);
            Assert.AreEqual(0f, r.Racers[1].Car.Lateral, 1e-4f);
            Assert.AreEqual(6.4f, r.Racers[2].Car.Lateral, 1e-4f);
            Assert.AreEqual(r.Racers[2].Car.Lateral, r.Racers[2].Lateral);
            Assert.AreEqual(6.4f, r.Racers[2].Car.TargetLateral, 1e-4f);
        }

        [Test]
        public void FarApartLaterally_NoContact()
        {
            var r = Race(Open(), P("A", 1), P("B", 2));
            Set(r, 0, 200f, 20f); Set(r, 1, 200f, 20f); // yanal −3.2 ve 0: 3.2 m arayla yan yana
            for (int i = 0; i < 50; i++)
            {
                Tick(r);
                foreach (var x in r.Racers) Assert.IsFalse(AnyContact(x), "tick " + i);
            }
            Assert.AreEqual(r.Racers[0].Car.Distance, r.Racers[1].Car.Distance);
        }

        [Test]
        public void RearContact_LimitsFollower_AndBumpsLeader_Once()
        {
            // B'nin TopSpeed'i 20: Tick içinde 20'de kalır (Faz 2 Ruling'i).
            var r = Race(Narrow(), P("A", 0), P("B", 1, new CarStats { TopSpeed = 20f }));
            Set(r, 0, 200f, 30f); Set(r, 1, 203f, 20f);
            Tick(r);
            Assert.AreEqual(20f, r.Racers[1].Car.Speed, 1e-4f);
            Assert.LessOrEqual(r.Racers[0].Car.Speed, 17.01f);
            Assert.GreaterOrEqual(r.Racers[0].Car.Speed, 16.99f);
            int rear = Count(r.Racers[0], SimEventKind.RearHit), bumped = Count(r.Racers[1], SimEventKind.Bumped);
            for (int i = 0; i < 20; i++)
            {
                Tick(r);
                rear += Count(r.Racers[0], SimEventKind.RearHit);
                bumped += Count(r.Racers[1], SimEventKind.Bumped);
                Assert.IsFalse(Has(r.Racers[1], SimEventKind.RearHit)); Assert.IsFalse(Has(r.Racers[0], SimEventKind.Bumped));
                Assert.IsFalse(Has(r.Racers[0], SimEventKind.SideHit) || Has(r.Racers[1], SimEventKind.SideHit));
                Assert.LessOrEqual(r.Racers[0].Car.Speed, 17.01f, "tick " + i); // temas sürüyor: A her Tick sınırda
            }
            Assert.AreEqual(1, rear); Assert.AreEqual(1, bumped);
        }

        [Test]
        public void RearContact_BumpsLeaderBy1()
        {
            var r = Race(Narrow(), P("A", 0), P("B", 1));
            Set(r, 0, 200f, 30f); Set(r, 1, 203f, 20f);
            Tick(r);
            Assert.IsTrue(Has(r.Racers[1], SimEventKind.Bumped));
            Assert.AreEqual(20f + 8f * CarSim.Step + 1f, r.Racers[1].Car.Speed, 1e-4f);
        }

        [Test]
        public void RearContact_NeverMovesFollowerBackwards()
        {
            var r = Race(Narrow(), P("A", 0), P("B", 1));
            Set(r, 0, 200f, 30f); Set(r, 1, 203f, 5f);
            float prev = r.Racers[0].Car.Distance;
            bool contact = false;
            for (int i = 0; i < 200; i++)
            {
                Tick(r, false, i < 100); // B 100 Tick fren yapar, A arkadan sıkışır
                contact |= Has(r.Racers[0], SimEventKind.RearHit);
                Assert.GreaterOrEqual(r.Racers[0].Car.Distance, prev, "tick " + i);
                prev = r.Racers[0].Car.Distance;
            }
            Assert.IsTrue(contact);
        }

        [Test]
        public void SideContact_PushesCarsApart_AndEmitsSideHitOnce()
        {
            var r = Race(Open(), P("A", 2), P("B", 3));
            Set(r, 0, 200f, 20f, 0f); Set(r, 1, 200f, 20f, 1.5f);
            Tick(r);
            Assert.IsTrue(Has(r.Racers[0], SimEventKind.SideHit));
            Assert.IsTrue(Has(r.Racers[1], SimEventKind.SideHit));
            Assert.Less(r.Racers[0].Car.LateralVel, -1f);
            Assert.Greater(r.Racers[1].Car.LateralVel, 1f);
            Assert.IsFalse(Has(r.Racers[0], SimEventKind.RearHit) || Has(r.Racers[1], SimEventKind.RearHit));
            int a = 1, b = 1;
            for (int i = 0; i < 100; i++)
            {
                Tick(r);
                a += Count(r.Racers[0], SimEventKind.SideHit);
                b += Count(r.Racers[1], SimEventKind.SideHit);
            }
            Assert.AreEqual(1, a); Assert.AreEqual(1, b);
            Assert.AreEqual(0f, r.Racers[0].Car.Lateral, 0.1f);
            Assert.AreEqual(3.2f, r.Racers[1].Car.Lateral, 0.1f);
        }

        [Test]
        public void SideContact_BothLoseFivePercent()
        {
            var pair = Race(Open(), P("A", 2), P("B", 3));
            var aloneA = Race(Open(), P("A", 2));
            var aloneB = Race(Open(), P("B", 3));
            Set(pair, 0, 200f, 20f, 0f); Set(pair, 1, 200f, 20f, 1.5f);
            Set(aloneA, 0, 200f, 20f, 0f); Set(aloneB, 0, 200f, 20f, 1.5f);
            Tick(pair); Tick(aloneA); Tick(aloneB);
            Assert.IsTrue(Has(pair.Racers[0], SimEventKind.SideHit));
            Assert.AreEqual(aloneA.Racers[0].Car.Speed * 0.95f, pair.Racers[0].Car.Speed, 1e-4f);
            Assert.AreEqual(aloneB.Racers[0].Car.Speed * 0.95f, pair.Racers[1].Car.Speed, 1e-4f);
        }

        [Test]
        public void SideContact_GrantsEdgeProtection()
        {
            // A banketin dış kenarında (9.4); içindeki B dışa doğru hızla kayıyor ve A'ya çarpıyor.
            var r = Race(Open(), P("A", 4), P("B", 3));
            Set(r, 0, 200f, 20f, 9.4f); Set(r, 1, 200f, 20f, 7.6f);
            r.Racers[1].Car.AddLateralVel(6f);
            bool hit = false;
            for (int i = 0; i < 60; i++)
            {
                Tick(r);
                hit |= Has(r.Racers[0], SimEventKind.SideHit);
                Assert.IsFalse(Has(r.Racers[0], SimEventKind.Crash), "A crashed at tick " + i);
                Assert.IsFalse(Has(r.Racers[1], SimEventKind.Crash), "B crashed at tick " + i);
                Assert.LessOrEqual(Math.Abs(r.Racers[0].Car.Lateral), 9.5f + 1e-4f);
            }
            Assert.IsTrue(hit);
            Assert.IsFalse(r.Racers[0].Car.IsOut);

            // Kontrol: aynı yanal hız korumasız tek araçta yoldan çıkarır.
            var alone = Race(Open(), P("A", 4));
            Set(alone, 0, 200f, 20f, 9.4f);
            alone.Racers[0].Car.AddLateralVel(3f);
            bool crashed = false;
            for (int i = 0; i < 60 && !crashed; i++) { Tick(alone); crashed = Has(alone.Racers[0], SimEventKind.Crash); }
            Assert.IsTrue(crashed, "control: unprotected car must leave the road");
        }

        [Test]
        public void SlidingCar_HitsNeighbour()
        {
            // Pist V (sağa viraj, R 40). İç şeritteki A (şerit 3, +3.2) %115 ile girer ve dışa (sola) kayar;
            // dışındaki B (şerit 2, 0) limitte girer ve çizgisini tutar → A, B'ye yandan çarpar.
            var r = new RaceSim(LateralPhysicsTests.TrackV(), new SimTuning(), new[] { P("A", 3), P("B", 2) });
            Set(r, 0, 199f, 1.15f * 21.909f); Set(r, 1, 201f, 21.0f);
            bool sideB = false, rearAny = false;
            for (int i = 0; i < 200 && !sideB; i++)
            {
                Tick(r);
                sideB |= Has(r.Racers[1], SimEventKind.SideHit);
                rearAny |= Has(r.Racers[0], SimEventKind.RearHit) || Has(r.Racers[1], SimEventKind.RearHit);
            }
            Assert.IsTrue(sideB, "outer car never got a SideHit");
            Assert.IsTrue(Has(r.Racers[0], SimEventKind.SideHit));
            Assert.IsFalse(rearAny);
        }

        [Test]
        public void IdenticalPosition_SeparatesDeterministically_NoTeleport()
        {
            var r = Race(Open(), P("A", 2), P("B", 3));
            Set(r, 0, 200f, 20f, 1.6f); Set(r, 1, 200f, 20f, 1.6f);
            float prevGap = 0f;
            for (int i = 0; i < 100; i++)
            {
                Tick(r);
                var a = r.Racers[0].Car; var b = r.Racers[1].Car;
                if (i == 0)
                {
                    Assert.Less(a.LateralVel, 0f, "lower index goes left");
                    Assert.Greater(b.LateralVel, 0f);
                }
                float gap = b.Lateral - a.Lateral;
                Assert.LessOrEqual(Math.Abs(gap - prevGap), 0.2f, "tick " + i);
                Assert.IsTrue(Finite(a.Lateral) && Finite(b.Lateral));
                prevGap = gap;
            }
            Assert.GreaterOrEqual(prevGap, 2.0f);

            // Aynı girdi aynı sonucu verir.
            var r2 = Race(Open(), P("A", 2), P("B", 3));
            Set(r2, 0, 200f, 20f, 1.6f); Set(r2, 1, 200f, 20f, 1.6f);
            for (int i = 0; i < 100; i++) Tick(r2);
            Assert.AreEqual(r.Racers[0].Car.Lateral, r2.Racers[0].Car.Lateral);
            Assert.AreEqual(r.Racers[1].Car.Lateral, r2.Racers[1].Car.Lateral);
        }

        [Test]
        public void SideBySide_SameLine_DoNotInterpenetrate()
        {
            // 2 şeritli parçada şerit 0 ve 1 aynı çizgiyi (−1.6) ister; yan yana ve aynı hızda giderken
            // birbirinin içine geçmezler.
            var r = Race(Narrow(), P("A", 0), P("B", 1));
            Set(r, 0, 200f, 20f, -2.6f); Set(r, 1, 200f, 20f, -0.6f);
            float minGap = float.MaxValue;
            for (int i = 0; i < 300; i++)
            {
                Tick(r);
                minGap = Math.Min(minGap, r.Racers[1].Car.Lateral - r.Racers[0].Car.Lateral);
                Assert.IsFalse(r.Racers[0].Car.IsOut || r.Racers[1].Car.IsOut);
            }
            Assert.Greater(minGap, 1.5f);
        }

        [Test]
        public void CrashedOrAirborneCar_IsIgnored()
        {
            // Çakılmış araç
            var r = Race(Narrow(), P("A", 0), P("B", 1));
            Set(r, 0, 200f, 30f); Set(r, 1, 203f, 0f);
            r.Racers[1].Car.SetOutForTest();
            for (int i = 0; i < 50; i++)
            {
                Tick(r);
                Assert.IsFalse(AnyContact(r.Racers[0]));
                Assert.AreEqual(30f, r.Racers[0].Car.Speed, 1e-4f);
            }
            Assert.Greater(r.Racers[0].Car.Distance, r.Racers[1].Car.Distance);

            // Havadaki araç: rampa [100,140], kalkış 128. B kalkar; A aynı yanal konumda 3 m arkada, henüz yerde.
            var l = new TrackLayout(new List<TrackPieceDef> { S(100), R(40, 15, 26, 12), S(100) });
            var q = Race(l, P("A", 2), P("B", 1));
            Set(q, 0, 124.9f, 20f, 0f); Set(q, 1, 127.9f, 20f, 0f);
            bool checkedOne = false;
            for (int i = 0; i < 40 && !Has(q.Racers[1], SimEventKind.Landed); i++)
            {
                Tick(q);
                Assert.IsFalse(AnyContact(q.Racers[0]) || AnyContact(q.Racers[1]), "tick " + i);
                if (q.Racers[1].Car.Phase == CarPhase.Airborne && q.Racers[0].Car.Phase == CarPhase.Driving
                    && Math.Abs(q.Racers[1].Car.Distance - q.Racers[0].Car.Distance) < 4.5f
                    && Math.Abs(q.Racers[1].Car.Lateral - q.Racers[0].Car.Lateral) < 2f) checkedOne = true;
            }
            Assert.IsTrue(checkedOne, "precondition: airborne car overlapped a grounded one");
        }

        [Test]
        public void Contact_NeverProducesNaN()
        {
            var r = Race(Narrow(), P("A", 0, null, CrashPolicy.Respawn), P("B", 1, null, CrashPolicy.Respawn),
                P("C", 2, null, CrashPolicy.Respawn), P("D", 3, null, CrashPolicy.Respawn), P("E", 4, null, CrashPolicy.Respawn));
            for (int k = 0; k < 5; k++) Set(r, k, 120f, 30f - k, -1.6f);
            r.Racers[2].Car.AddLateralVel(1e6f);
            r.Racers[3].Car.AddLateralVel(-50f);
            var rng = new SimRandom(5);
            for (int i = 0; i < 1500; i++)
            {
                var br = new bool[5];
                for (int k = 0; k < 5; k++) br[k] = rng.NextFloat() < 0.2f;
                r.Tick(br);
                foreach (var x in r.Racers)
                {
                    var c = x.Car;
                    Assert.IsTrue(Finite(c.Lateral) && Finite(c.LateralVel) && Finite(c.Speed) && Finite(c.Distance), "tick " + i);
                    Assert.GreaterOrEqual(c.Speed, 0f);
                }
            }
        }

        [Test]
        public void PlayerAndBot_ContactIsSymmetric()
        {
            var bot = BotProfile.Master();
            RacerSetup Player() => P("A", 2);
            RacerSetup Bot() => new RacerSetup { Name = "B", HomeLane = 3, Stats = bot.ToStats(), Policy = CrashPolicy.Eliminate, Bot = bot, Seed = 7 };

            var r1 = Race(Open(), Player(), Bot());
            var r2 = Race(Open(), Bot(), Player());
            Set(r1, 0, 200f, 20f, 0.5f); Set(r1, 1, 201f, 22f, 1.8f);
            Set(r2, 1, 200f, 20f, 0.5f); Set(r2, 0, 201f, 22f, 1.8f);
            bool side = false;
            for (int i = 0; i < 150; i++)
            {
                r1.Tick(new[] { i % 9 == 0, false });
                r2.Tick(new[] { false, i % 9 == 0 });
                side |= Has(r1.Racers[0], SimEventKind.SideHit);
                for (int k = 0; k < 2; k++)
                {
                    var a = r1.Racers[k].Car; var b = r2.Racers[1 - k].Car;
                    Assert.AreEqual(a.Lateral, b.Lateral, "tick " + i);
                    Assert.AreEqual(a.LateralVel, b.LateralVel, "tick " + i);
                    Assert.AreEqual(a.Speed, b.Speed, "tick " + i);
                    Assert.AreEqual(a.Distance, b.Distance, "tick " + i);
                    CollectionAssert.AreEqual(a.Events.Select(e => e.Kind).ToArray(), b.Events.Select(e => e.Kind).ToArray(), "tick " + i);
                }
            }
            Assert.IsTrue(side, "precondition: contact happened");
        }

        [Test]
        public void Ctor_RejectsDuplicateHomeLanes()
        {
            Assert.Throws<ArgumentException>(() => Race(Open(), P("A", 2), P("B", 2)));
        }
    }
}
