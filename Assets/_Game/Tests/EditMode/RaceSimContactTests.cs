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
        static TrackPieceDef C(float len, float safe, int turn) => new TrackPieceDef { Kind = PieceKind.Corner, Length = len, SafeSpeed = safe, TurnSign = turn };
        static TrackPieceDef R(float len, float min, float safe, float gap) =>
            new TrackPieceDef { Kind = PieceKind.Ramp, Length = len, MinJumpSpeed = min, SafeSpeed = safe, GapLength = gap };

        static TrackLayout Open() => new TrackLayout(new List<TrackPieceDef> { S(2000) });
        // Şerit 0 ve 1, 2 şeritli parçada aynı etkin şeride (0) düşer.
        static TrackLayout Narrow() => new TrackLayout(new List<TrackPieceDef> { S(100), S(500, 2) });

        static RacerSetup P(string name, int lane, CarStats stats = null) =>
            new RacerSetup { Name = name, HomeLane = lane, Stats = stats ?? new CarStats(), Policy = CrashPolicy.Eliminate };

        static RaceSim Race(TrackLayout l, params RacerSetup[] setups) => new RaceSim(l, new SimTuning(), setups);

        static void Set(RaceSim r, int i, float d, float v) => r.Racers[i].Car.SetStateForTest(d, v);

        static bool Has(Racer r, SimEventKind k) => r.Car.Events.Any(e => e.Kind == k);

        static void Tick(RaceSim r, params bool[] braking) => r.Tick(braking.Length == 0 ? new bool[r.Racers.Count] : braking);

        [Test]
        public void DifferentLanes_NoContact()
        {
            var r = Race(Open(), P("A", 1), P("B", 3));
            Set(r, 0, 200f, 20f); Set(r, 1, 200f, 20f);
            for (int i = 0; i < 50; i++)
            {
                Tick(r);
                foreach (var x in r.Racers)
                {
                    Assert.IsFalse(Has(x, SimEventKind.RearHit) || Has(x, SimEventKind.Bumped) || Has(x, SimEventKind.SideHit));
                }
            }
            Assert.AreEqual(r.Racers[0].Car.Distance, r.Racers[1].Car.Distance);
        }

        [Test]
        public void RearContact_LimitsFollowerTo85PercentOfLeader()
        {
            // B'nin TopSpeed'i 20: Tick içinde 20'de kalır (Ruling: aksi halde B 20.16'ya hızlanır ve sınır 17.14 olur).
            var r = Race(Narrow(), P("A", 0), P("B", 1, new CarStats { TopSpeed = 20f }));
            Set(r, 0, 200f, 30f); Set(r, 1, 203f, 20f);
            Tick(r);
            Assert.AreEqual(20f, r.Racers[1].Car.Speed, 1e-4f);
            Assert.LessOrEqual(r.Racers[0].Car.Speed, 17.01f);
            Assert.GreaterOrEqual(r.Racers[0].Car.Speed, 16.99f);
        }

        [Test]
        public void RearContact_EmitsRearHitAndBumped_Once()
        {
            var r = Race(Narrow(), P("A", 0), P("B", 1, new CarStats { TopSpeed = 20f }));
            Set(r, 0, 200f, 30f); Set(r, 1, 203f, 20f);
            int rear = 0, bumped = 0;
            for (int i = 0; i < 20; i++)
            {
                Tick(r);
                rear += r.Racers[0].Car.Events.Count(e => e.Kind == SimEventKind.RearHit);
                bumped += r.Racers[1].Car.Events.Count(e => e.Kind == SimEventKind.Bumped);
                Assert.IsFalse(Has(r.Racers[1], SimEventKind.RearHit)); Assert.IsFalse(Has(r.Racers[0], SimEventKind.Bumped));
                // Temas sürüyor: A her Tick sınırda.
                Assert.LessOrEqual(r.Racers[0].Car.Speed, 17.01f, "tick " + i);
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
            Set(r, 0, 200f, 30f); Set(r, 1, 201f, 5f);
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
        public void EqualDistance_LowerIndexIsAhead()
        {
            var r = Race(Narrow(), P("A", 0), P("B", 1));
            Set(r, 0, 200f, 20f); Set(r, 1, 200f, 20f);
            bool oneHit = false;
            for (int i = 0; i < 30; i++)
            {
                Tick(r);
                oneHit |= Has(r.Racers[1], SimEventKind.RearHit);
                Assert.IsFalse(Has(r.Racers[0], SimEventKind.RearHit), "index 0 must not be the follower");
                Assert.IsFalse(Has(r.Racers[1], SimEventKind.Bumped));
            }
            Assert.IsTrue(oneHit);
        }

        [Test]
        public void CrashedCar_DoesNotBlock()
        {
            var r = Race(Narrow(), P("A", 0), P("B", 1));
            Set(r, 0, 200f, 30f); Set(r, 1, 203f, 0f);
            r.Racers[1].Car.SetOutForTest();
            for (int i = 0; i < 50; i++)
            {
                Tick(r);
                Assert.IsFalse(Has(r.Racers[0], SimEventKind.RearHit));
                Assert.AreEqual(30f, r.Racers[0].Car.Speed, 1e-4f);
            }
            Assert.Greater(r.Racers[0].Car.Distance, r.Racers[1].Car.Distance);
        }

        [Test]
        public void AirborneCar_DoesNotContact()
        {
            // Rampa 5 şeritli olmak zorunda olduğundan havadaki araçla aynı şeritte arkadan temas kurulamaz (Ruling);
            // havadaki araç yan temasla sınanır: rampa [100,140] (kalkış 128) hemen ardından sağ viraj [140,200].
            // B (şerit 1) havadayken A (şerit 2) viraja %110 girip savrulur; taşma şeridi 1 = B'nin şeridi.
            var l = new TrackLayout(new List<TrackPieceDef> { S(100), R(40, 15, 26, 12), C(60, 20, +1), S(100) });
            var r = Race(l, P("A", 2), P("B", 1));
            Set(r, 0, 131.21f, 18.64f); Set(r, 1, 127.9f, 20f);
            for (int i = 0; i < 40; i++)
            {
                Tick(r);
                Assert.IsFalse(Has(r.Racers[0], SimEventKind.SideHit) || Has(r.Racers[1], SimEventKind.SideHit), "tick " + i);
                if (Has(r.Racers[0], SimEventKind.Skid))
                {
                    Assert.AreEqual(CarPhase.Airborne, r.Racers[1].Car.Phase, "precondition: B airborne");
                    Assert.Less(Math.Abs(r.Racers[0].Car.Distance - r.Racers[1].Car.Distance), 4.5f, "precondition: B alongside");
                    return;
                }
            }
            Assert.Fail("A never skidded");
        }

        [Test]
        public void Skid_SpillsOutward_HitsNeighbour()
        {
            var l = new TrackLayout(new List<TrackPieceDef> { S(100), C(60, 20, +1), S(100) });
            var r = Race(l, P("A", 2), P("B", 1));
            Set(r, 0, 99.9f, 22f); Set(r, 1, 99.9f, 18f);
            Tick(r);
            Assert.IsTrue(Has(r.Racers[0], SimEventKind.Skid));
            Assert.IsTrue(Has(r.Racers[1], SimEventKind.CleanPass));
            Assert.IsTrue(Has(r.Racers[0], SimEventKind.SideHit));
            Assert.IsTrue(Has(r.Racers[1], SimEventKind.SideHit));
            Assert.AreEqual((18f + 8f * CarSim.Step) * 0.85f, r.Racers[1].Car.Speed, 1e-3f);
            Assert.AreEqual(22f * 0.6f * 0.85f, r.Racers[0].Car.Speed, 1e-3f);
        }

        [Test]
        public void Skid_AtEdgeLane_SpillsNowhere()
        {
            var l = new TrackLayout(new List<TrackPieceDef> { S(100), C(60, 20, +1), S(100) });
            var r = Race(l, P("A", 0), P("B", 1));
            Set(r, 0, 99.9f, 22f); Set(r, 1, 99.9f, 18f);
            Tick(r);
            Assert.IsTrue(Has(r.Racers[0], SimEventKind.Skid));
            Assert.IsFalse(Has(r.Racers[0], SimEventKind.SideHit));
            Assert.IsFalse(Has(r.Racers[1], SimEventKind.SideHit));
        }

        [Test]
        public void Ctor_RejectsDuplicateHomeLanes()
        {
            Assert.Throws<ArgumentException>(() => Race(Open(), P("A", 2), P("B", 2)));
        }
    }
}
