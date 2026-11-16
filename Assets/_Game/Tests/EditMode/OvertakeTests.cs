using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ApexBrake.Simulation;

namespace ApexBrake.Tests.EditMode
{
    public sealed class OvertakeTests
    {
        static TrackPieceDef S(float len, int lanes = 5) => new TrackPieceDef { Kind = PieceKind.Straight, Length = len, LaneCount = lanes };

        static TrackLayout Open() => new TrackLayout(new List<TrackPieceDef> { S(3000) });
        // 3 şerit (yanal −3.2 / 0 / +3.2): ev şeridi 0 ve 1 aynı çizgide (−3.2), 3 ve 4 aynı çizgide (+3.2).
        // İlk parça 5 şeritli olmak zorunda; araçlar 100 m'den sonra kurulur.
        static TrackLayout ThreeLane() => new TrackLayout(new List<TrackPieceDef> { S(20), S(2980, 3) });
        // 2 şerit (yanal −1.6 / +1.6): ev şeridi 0, 1, 2 → −1.6; 3, 4 → +1.6.
        static TrackLayout TwoLane() => new TrackLayout(new List<TrackPieceDef> { S(20), S(2980, 2) });

        static RacerSetup P(string name, int lane, float topSpeed = 30f) =>
            new RacerSetup { Name = name, HomeLane = lane, Stats = new CarStats { TopSpeed = topSpeed }, Policy = CrashPolicy.Eliminate };

        static RacerSetup Bot(string name, int lane, uint seed = 3) =>
            new RacerSetup { Name = name, HomeLane = lane, Stats = BotProfile.Master().ToStats(), Policy = CrashPolicy.Eliminate, Bot = BotProfile.Master(), Seed = seed };

        static RaceSim Race(TrackLayout l, params RacerSetup[] s) => new RaceSim(l, new SimTuning(), s);
        static bool Has(Racer r, SimEventKind k) => r.Car.Events.Any(e => e.Kind == k);
        static float Home(TrackLayout l, Racer r) => LaneMap.LateralAt(l, r.HomeLane, r.Car.Distance, new SimTuning());

        static void Tick(RaceSim r) => r.Tick(new bool[r.Racers.Count]);

        [Test]
        public void NoBlocker_TargetsHomeLine()
        {
            var l = Open();
            var r = Race(l, P("A", 2), P("B", 2 + 1, 25f));
            r.SetStateForTest(0, 100f, 30f);
            r.SetStateForTest(1, 300f, 25f);   // başka şeritte ve uzakta
            for (int i = 0; i < 300; i++)
            {
                Tick(r);
                foreach (var x in r.Racers)
                {
                    Assert.IsFalse(x.IsOvertaking, "tick " + i);
                    Assert.AreEqual(Home(l, x), x.Car.TargetLateral, 1e-4f, "tick " + i);
                }
            }
        }

        [Test]
        public void SlowCarAhead_FasterCarMovesAside_AndPasses()
        {
            // B (indeks 0, TopSpeed 15) önde 40 m; A (indeks 1, bot) 30 m/s ile aynı çizgide (−3.2) arkadan gelir.
            var l = ThreeLane();
            var r = Race(l, P("B", 0, 15f), Bot("A", 1));
            r.SetStateForTest(0, 140f, 15f);
            r.SetStateForTest(1, 100f, 30f);
            AssertPasses(r, l, slow: 0, fast: 1);
        }

        [Test]
        public void PlayerUsesSameRule()
        {
            var l = ThreeLane();
            var r = Race(l, P("A", 1), P("B", 0, 15f));   // A: indeks 0, oyuncu (Bot == null)
            Assert.IsFalse(r.Racers[0].IsBot);
            r.SetStateForTest(1, 140f, 15f);
            r.SetStateForTest(0, 100f, 30f);
            AssertPasses(r, l, slow: 1, fast: 0);
        }

        static void AssertPasses(RaceSim r, TrackLayout l, int slow, int fast)
        {
            var a = r.Racers[fast]; var b = r.Racers[slow];
            bool overtook = false, passed = false;
            int ticksAfterPass = 0;
            for (int i = 0; i < 1000 && ticksAfterPass < 150; i++)
            {
                Tick(r);
                Assert.IsFalse(Has(a, SimEventKind.RearHit), "fast car rear-ended at tick " + i);
                Assert.IsFalse(Has(a, SimEventKind.SideHit), "fast car side-hit at tick " + i);
                Assert.IsFalse(a.Car.IsOut);
                overtook |= a.IsOvertaking;
                if (a.Car.Distance > b.Car.Distance + 10f) passed = true;
                if (passed) ticksAfterPass++;
            }
            Assert.IsTrue(overtook, "never overtook");
            Assert.IsTrue(passed, "never passed");
            Assert.IsFalse(a.IsOvertaking);
            Assert.Less(Math.Abs(a.Car.Lateral - Home(l, a)), 0.5f, "did not return to its home line");
        }

        [Test]
        public void PicksSideNearerHome()
        {
            foreach (float blockerLateral in new[] { 0.8f, -0.8f })
            {
                // A şerit 2 (çizgi 0). B (şerit 3, çizgisi +3.2) yavaş ve henüz A'nın çizgisine yakın (blockerLateral).
                var l = Open();
                var r = Race(l, P("A", 2), P("B", 3, 15f));
                r.SetStateForTest(0, 100f, 30f);
                r.SetStateForTest(1, 112f, 15f);
                r.Racers[1].Car.SetStateForTest(112f, 15f, blockerLateral, 3.2f);
                Tick(r);
                Assert.IsTrue(r.Racers[0].IsOvertaking, "lateral " + blockerLateral);
                float expected = blockerLateral > 0f ? blockerLateral - 2.8f : blockerLateral + 2.8f;
                Assert.AreEqual(expected, r.Racers[0].Car.TargetLateral, 1e-4f, "lateral " + blockerLateral);
            }
        }

        [Test]
        public void BothSidesOccupied_QueuesBehind()
        {
            // 3 şerit: A (şerit 1, −3.2) arkada; önde yan yana üç yavaş araç: −3.2, 0, +3.2.
            var l = ThreeLane();
            var r = Race(l, P("A", 1), P("B", 0, 15f), P("C", 2, 15f), P("D", 3, 15f));
            r.SetStateForTest(0, 100f, 30f);
            for (int k = 1; k <= 3; k++) r.SetStateForTest(k, 130f, 15f);
            AssertQueues(r, l);
        }

        [Test]
        public void NarrowRoad_NoRoom_QueuesBehind()
        {
            // 2 şerit: A (şerit 0, −1.6); önde yavaş B (şerit 1, −1.6) ve yanında yavaş C (şerit 3, +1.6).
            var l = TwoLane();
            var r = Race(l, P("A", 0), P("B", 1, 15f), P("C", 3, 15f));
            r.SetStateForTest(0, 100f, 30f);
            r.SetStateForTest(1, 130f, 15f);
            r.SetStateForTest(2, 130f, 15f);
            AssertQueues(r, l);
        }

        static void AssertQueues(RaceSim r, TrackLayout l)
        {
            var a = r.Racers[0];
            bool rear = false;
            for (int i = 0; i < 300; i++)
            {
                Tick(r);
                Assert.IsFalse(a.IsOvertaking, "tick " + i);
                Assert.AreEqual(Home(l, a), a.Car.TargetLateral, 1e-4f, "tick " + i);
                rear |= Has(a, SimEventKind.RearHit);
                Assert.IsFalse(a.Car.IsOut);
            }
            Assert.IsTrue(rear, "queued car never touched the slow car");
            foreach (var x in r.Racers.Skip(1)) Assert.Greater(x.Car.Distance, a.Car.Distance, "passed " + x.Name);
        }

        [Test]
        public void NeverTargetsOffRoad()
        {
            // En dış çizgide (3 şerit, +3.2) arka arkaya yavaş araçlar; A tekrar tekrar sollar.
            var l = ThreeLane();
            var r = Race(l, P("A", 4), P("B", 3, 12f), P("C", 2, 20f), P("D", 0, 16f));
            r.SetStateForTest(0, 100f, 30f);
            r.SetStateForTest(1, 130f, 12f);
            r.SetStateForTest(2, 180f, 20f);
            r.SetStateForTest(3, 150f, 16f);
            bool overtook = false;
            for (int i = 0; i < 1500; i++)
            {
                Tick(r);
                foreach (var x in r.Racers)
                {
                    float limit = l.SpanAtDistance(x.Car.Distance).Def.HalfWidth - 1.2f;
                    Assert.LessOrEqual(Math.Abs(x.Car.TargetLateral), limit + 1e-4f, x.Name + " tick " + i);
                    Assert.IsFalse(x.Car.IsOut, x.Name + " tick " + i);
                }
                overtook |= r.Racers[0].IsOvertaking;
            }
            Assert.IsTrue(overtook);
        }

        [Test]
        public void TargetIsStable_NoFlipFlop()
        {
            // B'nin hızı eşiğin (A − 1) çevresinde gidip gelir: engel koşulu sık sık değişir.
            var l = ThreeLane();
            var r = Race(l, P("A", 1), P("B", 0, 30f));
            r.SetStateForTest(0, 100f, 30f);
            r.SetStateForTest(1, 112f, 29f);
            var changes = new List<int>();
            float prev = r.Racers[0].Car.TargetLateral;
            bool overtook = false;
            for (int i = 0; i < 600; i++)
            {
                bool bBrake = r.Racers[1].Car.Speed > 29.1f;
                r.Tick(new[] { r.Racers[0].Car.Speed > 30f, bBrake });
                float t = r.Racers[0].Car.TargetLateral;
                if (Math.Abs(t - prev) > 0.1f) changes.Add(i);
                prev = t;
                overtook |= r.Racers[0].IsOvertaking;
            }
            Assert.IsTrue(overtook, "precondition: overtake started");
            for (int k = 1; k < changes.Count; k++)
                Assert.GreaterOrEqual(changes[k] - changes[k - 1], 25, $"target changed at ticks {changes[k - 1]} and {changes[k]}");
        }

        [Test]
        public void EqualSpeedCarAhead_NoOvertake()
        {
            var l = ThreeLane();
            var r = Race(l, P("A", 1), P("B", 0));
            r.SetStateForTest(0, 100f, 30f);
            r.SetStateForTest(1, 110f, 30f);
            for (int i = 0; i < 300; i++)
            {
                Tick(r);
                Assert.IsFalse(r.Racers[0].IsOvertaking, "tick " + i);
                Assert.AreEqual(Home(l, r.Racers[0]), r.Racers[0].Car.TargetLateral, 1e-4f);
            }
        }
    }
}
