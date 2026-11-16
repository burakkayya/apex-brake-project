using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ApexBrake.Simulation;

namespace ApexBrake.Tests.EditMode
{
    public sealed class BotBrainTests
    {
        static TrackPieceDef S(float len) => new TrackPieceDef { Kind = PieceKind.Straight, Length = len };
        static TrackPieceDef C(float len, float safe, int turn = 1) => new TrackPieceDef { Kind = PieceKind.Corner, Length = len, SafeSpeed = safe, TurnSign = turn };
        static TrackPieceDef B(float len, float safe) => new TrackPieceDef { Kind = PieceKind.Bump, Length = len, SafeSpeed = safe };
        static TrackPieceDef R(float len, float min, float safe, float gap) =>
            new TrackPieceDef { Kind = PieceKind.Ramp, Length = len, MinJumpSpeed = min, SafeSpeed = safe, GapLength = gap };

        // Pist R:
        // S100 [0,100], C60 [100,160], S100 [160,260], B12 [260,272], S50 [272,322], C40 [322,362],
        // S100 [362,462], R40 [462,502] (kalkış 490), S100 [502,602], C60 [602,662], S100 [662,762]
        internal static List<TrackPieceDef> TrackRPieces() => new List<TrackPieceDef>
        {
            S(100), C(60, 21.9f, 1), S(100), B(12, 20f), S(50), C(40, 17.3f, -1), S(100),
            R(40, 15f, 26f, 12f), S(100), C(60, 21.9f, 1), S(100)
        };

        static TrackLayout TrackR() => new TrackLayout(TrackRPieces());

        sealed class RunResult
        {
            public bool Finished;
            public bool Out;
            public int Ticks;
            public float Distance;
            public readonly List<SimEvent> Events = new List<SimEvent>();
            public readonly List<(SimEvent evt, float speed)> Entries = new List<(SimEvent, float)>();
            public bool Has(SimEventKind k) => Events.Any(e => e.Kind == k);
        }

        static RunResult RunBot(BotProfile profile, uint seed, TrackLayout layout = null)
        {
            layout = layout ?? TrackR();
            var car = new CarSim(layout, profile.ToStats(), new SimTuning(), CrashPolicy.Eliminate);
            var brain = new BotBrain(profile, layout, seed);
            var r = new RunResult();
            for (int i = 0; i < 10000; i++)
            {
                float v = car.Speed;
                car.Tick(brain.ShouldBrake(car));
                r.Ticks++;
                foreach (var e in car.Events)
                {
                    r.Events.Add(e);
                    if (e.Kind == SimEventKind.CleanPass || e.Kind == SimEventKind.Skid || e.Kind == SimEventKind.Crash || e.Kind == SimEventKind.Takeoff)
                        r.Entries.Add((e, v));
                }
                if (car.Phase == CarPhase.Finished || car.IsOut)
                {
                    r.Finished = car.Phase == CarPhase.Finished;
                    r.Out = car.IsOut;
                    r.Distance = car.Distance;
                    return r;
                }
            }
            Assert.Fail($"bot did not finish within 10000 Ticks (seed {seed})");
            return r;
        }

        [Test]
        public void SimRandom_SameSeed_SameSequence()
        {
            var a = new SimRandom(42); var b = new SimRandom(42); var c = new SimRandom(43);
            bool differs = false;
            for (int i = 0; i < 1000; i++)
            {
                float x = a.NextFloat();
                Assert.AreEqual(x, b.NextFloat());
                differs |= x != c.NextFloat();
            }
            Assert.IsTrue(differs);
            // Tohum 0 → 1
            var z = new SimRandom(0); var one = new SimRandom(1);
            for (int i = 0; i < 10; i++) Assert.AreEqual(one.NextFloat(), z.NextFloat());
        }

        [Test]
        public void SimRandom_ValuesIn0To1()
        {
            var r = new SimRandom(12345);
            float min = 1f, max = 0f;
            for (int i = 0; i < 10000; i++)
            {
                float x = r.NextFloat();
                Assert.GreaterOrEqual(x, 0f); Assert.Less(x, 1f);
                min = System.Math.Min(min, x); max = System.Math.Max(max, x);
            }
            Assert.Less(min, 0.01f); Assert.Greater(max, 0.99f);
        }

        [Test]
        public void TryGetNextEvalPoint_Corner_IsStart_Ramp_IsTakeoff()
        {
            var l = TrackR();
            Assert.IsTrue(l.TryGetNextEvalPoint(0f, out float d, out var span));
            Assert.AreEqual(100f, d, 1e-3f); Assert.AreEqual(1, span.Index);
            Assert.IsTrue(l.TryGetNextEvalPoint(100f, out d, out span)); // tam üstündeyken bir sonrakine geçer
            Assert.AreEqual(260f, d, 1e-3f); Assert.AreEqual(3, span.Index);
            Assert.IsTrue(l.TryGetNextEvalPoint(470f, out d, out span));
            Assert.AreEqual(490f, d, 1e-3f); Assert.AreEqual(7, span.Index);
            Assert.IsTrue(l.TryGetNextEvalPoint(495f, out d, out span));
            Assert.AreEqual(602f, d, 1e-3f); Assert.AreEqual(9, span.Index);
            Assert.IsFalse(l.TryGetNextEvalPoint(700f, out _, out _));
        }

        [Test]
        public void ZeroErrorBot_NeverBrakesOnOpenStraight()
        {
            var l = new TrackLayout(new List<TrackPieceDef> { S(2000) });
            var p = new BotProfile { Name = "T", BrakeError = 0f, Risk = 0.95f };
            var car = new CarSim(l, p.ToStats(), new SimTuning(), CrashPolicy.Eliminate);
            var brain = new BotBrain(p, l, 1);
            for (int i = 0; i < 10000 && car.Phase != CarPhase.Finished; i++)
            {
                bool brake = brain.ShouldBrake(car);
                Assert.IsFalse(brake, "braked at " + car.Distance);
                car.Tick(brake);
            }
            Assert.AreEqual(CarPhase.Finished, car.Phase);
        }

        [Test]
        public void ZeroErrorBot_EntersCornerAtOrBelowTarget()
        {
            var p = new BotProfile { Name = "T", BrakeError = 0f, Risk = 0.95f };
            var l = TrackR();
            var r = RunBot(p, 1, l);
            Assert.IsTrue(r.Finished);
            var corners = r.Entries.Where(x => l.SpanAt(x.evt.PieceIndex).Def.Kind == PieceKind.Corner).ToList();
            Assert.AreEqual(3, corners.Count);
            foreach (var (evt, speed) in corners)
            {
                float safe = l.SpanAt(evt.PieceIndex).Def.SafeSpeed;
                Assert.AreEqual(SimEventKind.CleanPass, evt.Kind);
                Assert.LessOrEqual(speed, 0.95f * safe + 0.2f, "piece " + evt.PieceIndex);
                Assert.GreaterOrEqual(speed, 0.85f * safe, "piece " + evt.PieceIndex);
            }
        }

        [Test]
        public void Master_FinishesWithoutCrash_On20Seeds()
        {
            for (uint seed = 1; seed <= 20; seed++)
            {
                var r = RunBot(BotProfile.Master(), seed);
                Assert.IsTrue(r.Finished, "seed " + seed);
                Assert.IsFalse(r.Has(SimEventKind.Crash), "Crash, seed " + seed);
                Assert.IsFalse(r.Has(SimEventKind.FellShort), "FellShort, seed " + seed);
            }
        }

        [Test]
        public void Master_FinishTimeIsReasonable()
        {
            for (uint seed = 1; seed <= 5; seed++)
            {
                var r = RunBot(BotProfile.Master(), seed);
                Assert.IsTrue(r.Finished);
                float t = r.Ticks * CarSim.Step;
                Assert.GreaterOrEqual(t, 25f, "seed " + seed); Assert.LessOrEqual(t, 60f, "seed " + seed);
            }
        }

        [Test]
        public void Reckless_CrashesOnSomeSeedsButNotAll()
        {
            int eliminated = 0;
            for (uint seed = 1; seed <= 40; seed++)
                if (RunBot(BotProfile.Reckless(), seed).Out) eliminated++;
            Assert.GreaterOrEqual(eliminated, 6); Assert.LessOrEqual(eliminated, 34);
        }

        [Test]
        public void Slow_FinishesLaterThanMaster()
        {
            for (uint seed = 1; seed <= 5; seed++)
            {
                var m = RunBot(BotProfile.Master(), seed);
                var s = RunBot(BotProfile.Slow(), seed);
                Assert.IsTrue(m.Finished); Assert.IsTrue(s.Finished, "slow, seed " + seed);
                Assert.Greater(s.Ticks, m.Ticks, "seed " + seed);
            }
        }

        [Test]
        public void SameSeed_SameResult()
        {
            foreach (var make in new System.Func<BotProfile>[] { BotProfile.Master, BotProfile.Reckless, BotProfile.Balanced })
            {
                var a = RunBot(make(), 7);
                var b = RunBot(make(), 7);
                Assert.AreEqual(a.Ticks, b.Ticks);
                Assert.AreEqual(a.Distance, b.Distance);
            }
        }

        [Test]
        public void Bot_ClearsRamp_NeverFallsShort()
        {
            for (uint seed = 1; seed <= 20; seed++)
            {
                var r = RunBot(BotProfile.Master(), seed);
                Assert.IsFalse(r.Has(SimEventKind.FellShort), "seed " + seed);
                Assert.IsTrue(r.Has(SimEventKind.Takeoff) && r.Has(SimEventKind.Landed), "seed " + seed);
            }
        }
    }
}
