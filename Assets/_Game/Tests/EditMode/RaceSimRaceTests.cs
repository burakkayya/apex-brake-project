using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ApexBrake.Simulation;

namespace ApexBrake.Tests.EditMode
{
    public sealed class RaceSimRaceTests
    {
        static TrackPieceDef S(float len, int lanes = 5) => new TrackPieceDef { Kind = PieceKind.Straight, Length = len, LaneCount = lanes };

        // Pist R (Task 3) + 2. parçadan sonra Straight 60 (3 şerit), rampadan sonraki düzden sonra Straight 60 (2 şerit).
        // S100 [0,100], C60 [100,160], S100 [160,260], S60/3 [260,320], B12 [320,332], S50 [332,382], C40 [382,422],
        // S100 [422,522], R40 [522,562], S100 [562,662], S60/2 [662,722], C60 [722,782], S100 [782,882]
        static TrackLayout Track()
        {
            var p = BotBrainTests.TrackRPieces();
            p.Insert(9, S(60, 2));
            p.Insert(3, S(60, 3));
            return new TrackLayout(p);
        }

        static RacerSetup P(string name, int lane) =>
            new RacerSetup { Name = name, HomeLane = lane, Stats = new CarStats(), Policy = CrashPolicy.Eliminate };

        static RacerSetup Bot(BotProfile b, int lane, uint seed) =>
            new RacerSetup { Name = b.Name, HomeLane = lane, Stats = b.ToStats(), Policy = CrashPolicy.Eliminate, Bot = b, Seed = seed };

        static readonly bool[] NoBrake = new bool[5];

        static RaceSim FiveBots(uint seed)
        {
            var profiles = new[] { BotProfile.Master(), BotProfile.Balanced(), BotProfile.Reckless(), BotProfile.Slow(), BotProfile.Balanced() };
            var setups = new List<RacerSetup>();
            for (int i = 0; i < 5; i++) setups.Add(Bot(profiles[i], i, seed * 100u + (uint)i));
            return new RaceSim(Track(), new SimTuning(), setups);
        }

        static void RunToEnd(RaceSim r)
        {
            for (int i = 0; i < 10000 && !r.AllDone; i++) r.Tick(NoBrake);
            Assert.IsTrue(r.AllDone, "race not done within 10000 Ticks");
        }

        [Test]
        public void Rank_OrdersByDistance()
        {
            var r = new RaceSim(Track(), new SimTuning(), new[] { P("A", 0), P("B", 2), P("C", 4) });
            r.Racers[0].Car.SetStateForTest(10f, 0f);
            r.Racers[1].Car.SetStateForTest(50f, 0f);
            r.Racers[2].Car.SetStateForTest(30f, 0f);
            r.Tick(new[] { true, true, true });
            Assert.AreEqual(3, r.Racers[0].Rank);
            Assert.AreEqual(1, r.Racers[1].Rank);
            Assert.AreEqual(2, r.Racers[2].Rank);
        }

        [Test]
        public void Rank_FinishedBeatUnfinished_ByFinishTick()
        {
            var l = Track();
            var r = new RaceSim(l, new SimTuning(), new[] { P("A", 0), P("B", 2), P("C", 4) });
            r.Racers[0].Car.SetStateForTest(l.TotalLength - 5f, 30f);
            r.Racers[1].Car.SetStateForTest(l.TotalLength - 100f, 30f);
            r.Racers[2].Car.SetStateForTest(l.TotalLength - 0.1f, 30f);
            r.Tick(NoBrake);
            Assert.AreEqual(1, r.Racers[2].FinishTick);
            Assert.AreEqual(-1, r.Racers[0].FinishTick);
            Assert.AreEqual(1, r.Racers[2].Rank); Assert.AreEqual(2, r.Racers[0].Rank); Assert.AreEqual(3, r.Racers[1].Rank);
            for (int i = 0; i < 20; i++) r.Tick(NoBrake);
            Assert.Greater(r.Racers[0].FinishTick, 1);
            Assert.AreEqual(-1, r.Racers[1].FinishTick);
            // İkisi de TotalLength'te; önce bitiren önde.
            Assert.AreEqual(1, r.Racers[2].Rank); Assert.AreEqual(2, r.Racers[0].Rank); Assert.AreEqual(3, r.Racers[1].Rank);
            for (int i = 0; i < 200; i++) r.Tick(NoBrake);
            Assert.IsTrue(r.AllDone);
            Assert.AreEqual(3, r.Racers[1].Rank);
        }

        [Test]
        public void EliminatedCar_KeepsDistance_AndDropsWhenPassed()
        {
            var r = new RaceSim(Track(), new SimTuning(), new[] { P("A", 0), Bot(BotProfile.Master(), 2, 5) });
            r.Racers[0].Car.SetStateForTest(99.9f, 30f); // 21.9'luk viraja 30 ile → çakılma
            r.Tick(new[] { false, false });
            Assert.IsTrue(r.Racers[0].Car.IsOut);
            float d = r.Racers[0].Car.Distance;
            Assert.AreEqual(1, r.Racers[0].Rank);
            bool dropped = false;
            for (int i = 0; i < 2000 && !r.AllDone; i++)
            {
                r.Tick(new[] { false, false });
                Assert.AreEqual(d, r.Racers[0].Car.Distance);
                if (r.Racers[1].Car.Distance > d) { Assert.AreEqual(2, r.Racers[0].Rank); Assert.AreEqual(1, r.Racers[1].Rank); dropped = true; }
                else Assert.AreEqual(1, r.Racers[0].Rank);
            }
            Assert.IsTrue(dropped);
        }

        [Test]
        public void FiveBots_RaceCompletes_AllDoneWithin10000Ticks()
        {
            for (uint seed = 1; seed <= 10; seed++)
            {
                var r = FiveBots(seed);
                RunToEnd(r);
                Assert.LessOrEqual(r.TickCount, 10000);
                var ranks = r.Racers.Select(x => x.Rank).OrderBy(x => x).ToArray();
                CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, ranks, "seed " + seed);
            }
        }

        [Test]
        public void FiveBots_MasterIsUsuallyOnPodium()
        {
            int podium = 0;
            for (uint seed = 1; seed <= 10; seed++)
            {
                var r = FiveBots(seed);
                RunToEnd(r);
                if (r.Racers[0].Rank <= 3) podium++;
            }
            Assert.GreaterOrEqual(podium, 7);
        }

        [Test]
        public void SameSeeds_IdenticalStandingsAndTicks()
        {
            for (uint seed = 1; seed <= 3; seed++)
            {
                var a = FiveBots(seed); var b = FiveBots(seed);
                RunToEnd(a); RunToEnd(b);
                Assert.AreEqual(a.TickCount, b.TickCount);
                for (int i = 0; i < 5; i++)
                {
                    Assert.AreEqual(a.Racers[i].Rank, b.Racers[i].Rank);
                    Assert.AreEqual(a.Racers[i].FinishTick, b.Racers[i].FinishTick);
                    Assert.AreEqual(a.Racers[i].Car.Distance, b.Racers[i].Car.Distance);
                }
            }
        }

        [Test]
        public void Lateral_FollowsNarrowing()
        {
            var l = Track();
            Assert.AreEqual(882f, l.TotalLength, 1e-3f);
            Assert.AreEqual(2, l.SpanAtDistance(692f).Def.LaneCount);
            var r = new RaceSim(l, new SimTuning(), new[] { P("A", 4) });
            r.Racers[0].Car.SetStateForTest(692f, 0f); // 2 şeritli parça [662,722] ortası
            r.Tick(new[] { true });
            Assert.AreEqual(1.6f, r.Racers[0].Lateral, 0.01f);
        }
    }
}
