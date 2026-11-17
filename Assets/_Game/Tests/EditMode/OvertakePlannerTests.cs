using System;
using NUnit.Framework;
using ApexBrake.Racing;

namespace ApexBrake.Tests.EditMode
{
    /// <summary>Sollama planlayıcısı (saf mantık): Faz 2b OvertakeTests'in karşılıkları. Girdi: araçların mesafe/yanal konum/hızı.</summary>
    public sealed class OvertakePlannerTests
    {
        const float Dt = 0.02f;
        const float Wide = 8f;          // 5 şerit yarı genişliği
        const float ThreeLane = 4.8f;
        const float TwoLane = 3.2f;

        static TrafficCar Car(float d, float lat, float v) => new TrafficCar { Distance = d, Lateral = lat, Speed = v, Active = true };

        [Test]
        public void NoBlocker_TargetsHomeLine()
        {
            var p = new OvertakePlanner(3);
            var cars = new[] { Car(100f, 0f, 35f), Car(300f, 0f, 20f), Car(112f, 3.2f, 20f) };   // biri uzakta, biri başka şeritte
            for (int k = 0; k < 200; k++)
            {
                float t = p.Plan(0, 0f, Wide, cars, Dt);
                Assert.AreEqual(0f, t, 1e-5f, "step " + k);
                Assert.IsFalse(p.IsOvertaking(0));
            }
        }

        [Test]
        public void SlowCarAhead_PicksFreeSide_NearerHome()
        {
            foreach (float blockerLat in new[] { 0.8f, -0.8f })
            {
                var p = new OvertakePlanner(2);
                var cars = new[] { Car(100f, 0f, 35f), Car(112f, blockerLat, 25f) };
                float t = p.Plan(0, 0f, Wide, cars, Dt);
                Assert.IsTrue(p.IsOvertaking(0), "lat " + blockerLat);
                float expected = blockerLat > 0f ? blockerLat - 2.8f : blockerLat + 2.8f;
                Assert.AreEqual(expected, t, 1e-4f, "lat " + blockerLat);
            }

            // Yakın taraf dolu → serbest olan uzak taraf.
            var q = new OvertakePlanner(3);
            var c3 = new[] { Car(100f, 0f, 35f), Car(112f, 0.8f, 25f), Car(104f, -2.2f, 34f) };
            float t3 = q.Plan(0, 0f, Wide, c3, Dt);
            Assert.IsTrue(q.IsOvertaking(0));
            Assert.AreEqual(3.6f, t3, 1e-4f);
        }

        [Test]
        public void BothSidesOccupied_StaysBehind()
        {
            var p = new OvertakePlanner(4);
            var cars = new[] { Car(100f, -3.2f, 35f), Car(125f, -3.2f, 20f), Car(125f, 0f, 20f), Car(125f, 3.2f, 20f) };
            for (int k = 0; k < 100; k++)
            {
                Assert.AreEqual(-3.2f, p.Plan(0, -3.2f, ThreeLane, cars, Dt), 1e-5f, "step " + k);
                Assert.IsFalse(p.IsOvertaking(0));
            }
        }

        [Test]
        public void NarrowRoad_NoRoom_StaysBehind()
        {
            var p = new OvertakePlanner(3);
            var cars = new[] { Car(100f, -1.6f, 35f), Car(115f, -1.6f, 20f), Car(115f, 1.6f, 20f) };
            for (int k = 0; k < 100; k++)
            {
                Assert.AreEqual(-1.6f, p.Plan(0, -1.6f, TwoLane, cars, Dt), 1e-5f, "step " + k);
                Assert.IsFalse(p.IsOvertaking(0));
            }
            // Tek araç ama yol iki şeritten de dar → yer yok.
            var q = new OvertakePlanner(2);
            var c2 = new[] { Car(100f, 0f, 35f), Car(115f, 0f, 20f) };
            Assert.AreEqual(0f, q.Plan(0, 0f, 1.6f, c2, Dt), 1e-5f);
            Assert.IsFalse(q.IsOvertaking(0));
        }

        [Test]
        public void NeverTargetsOffRoad()
        {
            var rnd = new Random(7);
            for (int trial = 0; trial < 2000; trial++)
            {
                int n = 2 + rnd.Next(4);
                float hw = new[] { Wide, ThreeLane, TwoLane }[rnd.Next(3)];
                var cars = new TrafficCar[n];
                for (int i = 0; i < n; i++)
                    cars[i] = Car(100f + (float)rnd.NextDouble() * 30f, ((float)rnd.NextDouble() * 2f - 1f) * hw, 15f + (float)rnd.NextDouble() * 25f);
                var p = new OvertakePlanner(n);
                float home = ((float)rnd.NextDouble() * 2f - 1f) * (hw - OvertakePlanner.DefaultEdgeMargin);
                for (int k = 0; k < 5; k++)
                {
                    float t = p.Plan(0, home, hw, cars, Dt);
                    if (p.IsOvertaking(0))
                        Assert.LessOrEqual(Math.Abs(t), hw - OvertakePlanner.DefaultEdgeMargin + 1e-4f, "trial " + trial);
                    else
                        Assert.AreEqual(home, t, 1e-5f, "trial " + trial);
                }
            }
        }

        [Test]
        public void TargetIsStable_NoFlipFlop()
        {
            var p = new OvertakePlanner(2);
            float last = float.NaN, lastChange = -10f, time = 0f;
            int changes = 0;
            for (int k = 0; k < 500; k++)
            {
                time = k * Dt;
                // Engel çizgi üzerinde sağa-sola oynuyor; yanal mesafesi sürekli değişiyor.
                float bl = (float)Math.Sin(time * 9f) * 0.9f;
                var cars = new[] { Car(100f + time * 35f, 0f, 35f), Car(114f + time * 20f, bl, 20f) };
                float t = p.Plan(0, 0f, Wide, cars, Dt);
                if (!float.IsNaN(last) && Math.Abs(t - last) > 1e-4f)
                {
                    Assert.GreaterOrEqual(time - lastChange, OvertakePlanner.DefaultHold - Dt * 0.5f, "target changed again after " + (time - lastChange) + " s");
                    lastChange = time;
                    changes++;
                }
                last = t;
            }
            Assert.Greater(changes, 0, "never overtook");
        }

        [Test]
        public void EqualSpeedCarAhead_NoOvertake()
        {
            var p = new OvertakePlanner(2);
            var cars = new[] { Car(100f, 0f, 30f), Car(110f, 0.3f, 30f) };
            for (int k = 0; k < 100; k++)
            {
                Assert.AreEqual(0f, p.Plan(0, 0f, Wide, cars, Dt), 1e-5f);
                Assert.IsFalse(p.IsOvertaking(0));
            }
        }

        [Test]
        public void InactiveCarAhead_IsIgnored()
        {
            var p = new OvertakePlanner(2);
            var slow = Car(110f, 0f, 0f); slow.Active = false;
            var cars = new[] { Car(100f, 0f, 35f), slow };
            Assert.AreEqual(0f, p.Plan(0, 0f, Wide, cars, Dt), 1e-5f);
            Assert.IsFalse(p.IsOvertaking(0));
        }

        [Test]
        public void AfterPassing_ReturnsHome()
        {
            var p = new OvertakePlanner(2);
            float me = 100f, other = 112f;
            bool overtook = false;
            float t = 0f;
            for (int k = 0; k < 300; k++)
            {
                me += 35f * Dt; other += 25f * Dt;
                var cars = new[] { Car(me, p.IsOvertaking(0) ? t : 0f, 35f), Car(other, 0f, 25f) };
                t = p.Plan(0, 0f, Wide, cars, Dt);
                overtook |= p.IsOvertaking(0);
            }
            Assert.IsTrue(overtook);
            Assert.IsFalse(p.IsOvertaking(0));
            Assert.AreEqual(0f, t, 1e-5f);
        }
    }
}
