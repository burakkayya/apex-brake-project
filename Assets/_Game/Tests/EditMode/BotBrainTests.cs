using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ApexBrake.Simulation;

namespace ApexBrake.Tests.EditMode
{
    public sealed class BotBrainTests
    {
        static TrackPieceDef S(float len, int lanes = 5) => new TrackPieceDef { Kind = PieceKind.Straight, Length = len, LaneCount = lanes };
        static TrackPieceDef C(float len, float radius, int turn) => new TrackPieceDef { Kind = PieceKind.Corner, Length = len, Radius = radius, TurnSign = turn };
        static TrackPieceDef B(float len, float safe) => new TrackPieceDef { Kind = PieceKind.Bump, Length = len, SafeSpeed = safe };
        static TrackPieceDef R(float len, float min, float safe, float gap) =>
            new TrackPieceDef { Kind = PieceKind.Ramp, Length = len, MinJumpSpeed = min, SafeSpeed = safe, GapLength = gap };

        // Pist R:
        // S100 [0,100], C60 [100,160], S100 [160,260], B12 [260,272], S50 [272,322], C40 [322,362],
        // S100 [362,462], R40 [462,502] (kalkış 490), S100 [502,602], C60 [602,662], S100 [662,762]
        // Faz 2'deki güvenli hızları (21.9 ve 17.3) varsayılan tutunmayla (12) veren yarıçaplar.
        const float R219 = 21.9f * 21.9f / 12f, R173 = 17.3f * 17.3f / 12f;

        internal static List<TrackPieceDef> TrackRPieces() => new List<TrackPieceDef>
        {
            S(100), C(60, R219, 1), S(100), B(12, 20f), S(50), C(40, R173, -1), S(100),
            R(40, 15f, 26f, 12f), S(100), C(60, R219, 1), S(100)
        };

        static TrackLayout TrackR() => new TrackLayout(TrackRPieces());

        sealed class RunResult
        {
            public bool Finished;
            public bool Out;
            public int Ticks;
            public float Distance;
            public float Form;
            public readonly List<SimEvent> Events = new List<SimEvent>();
            public readonly List<bool> Brakes = new List<bool>();
            public readonly List<(int piece, float speed)> CornerEntries = new List<(int, float)>();
            public bool Has(SimEventKind k) => Events.Any(e => e.Kind == k);
            public int FirstBrakeAfter(int tick) { for (int i = tick; i < Brakes.Count; i++) if (Brakes[i]) return i; return -1; }
        }

        static RacerSetup BotSetup(BotProfile p, int lane, uint seed) =>
            new RacerSetup { Name = p.Name, HomeLane = lane, Stats = p.ToStats(), Policy = CrashPolicy.Eliminate, Bot = p, Seed = seed };

        // Faz 2'deki gibi, RaceSim içinde tek araç (ev şeridi 2).
        static RunResult RunBot(BotProfile profile, uint seed, TrackLayout layout = null, RacerSetup other = null)
        {
            layout = layout ?? TrackR();
            var setups = new List<RacerSetup> { BotSetup(profile, 2, seed) };
            if (other != null) setups.Add(other);
            var race = new RaceSim(layout, new SimTuning(), setups);
            var me = race.Racers[0];
            var car = me.Car;
            var r = new RunResult { Form = me.Brain.Form };
            var noBrake = new bool[setups.Count];
            if (other != null) noBrake[1] = true; // diğer araç startta durur
            for (int i = 0; i < 10000; i++)
            {
                float v = car.Speed, d = car.Distance;
                race.Tick(noBrake);
                r.Ticks++;
                r.Brakes.Add(me.IsBraking);
                r.Events.AddRange(car.Events);
                for (int p = 0; p < layout.PieceCount; p++)
                {
                    var sp = layout.SpanAt(p);
                    if (sp.Def.Kind == PieceKind.Corner && sp.Start > d && sp.Start <= car.Distance) r.CornerEntries.Add((p, v));
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

        static BotProfile Calm(float risk = 0.95f) => new BotProfile
        {
            Name = "T", BrakeError = 0f, Risk = risk, RiskJitter = 0f, MistakeChance = 0f, HeroChance = 0f, ReactionTime = 0f, Wander = 0f
        };

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
                min = Math.Min(min, x); max = Math.Max(max, x);
            }
            Assert.Less(min, 0.01f); Assert.Greater(max, 0.99f);
        }

        [Test]
        public void TryGetNextEvalPoint_Corner_IsStart_Ramp_IsTakeoff()
        {
            var l = TrackR();
            Assert.IsTrue(l.TryGetNextEvalPoint(0f, out float d, out var span));
            Assert.AreEqual(100f, d, 1e-3f); Assert.AreEqual(1, span.Index);
            Assert.IsTrue(l.TryGetNextEvalPoint(100f, out d, out span));
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
            for (uint seed = 1; seed <= 5; seed++)
            {
                var r = RunBot(Calm(), seed, l);
                Assert.IsTrue(r.Finished);
                Assert.AreEqual(-1, r.FirstBrakeAfter(15), "braked after the start, seed " + seed);
            }
        }

        [Test]
        public void ZeroErrorBot_EntersCornerAtOrBelowTarget()
        {
            var l = TrackR();
            for (uint seed = 1; seed <= 5; seed++)
            {
                var r = RunBot(Calm(), seed, l);
                Assert.IsTrue(r.Finished, "seed " + seed);
                Assert.IsFalse(r.Has(SimEventKind.Crash));
                Assert.AreEqual(3, r.CornerEntries.Count);
                float risk = 0.95f + 0.03f * r.Form;
                foreach (var (piece, speed) in r.CornerEntries)
                {
                    float limit = (float)Math.Sqrt(12f * l.SpanAt(piece).Def.Radius);
                    Assert.LessOrEqual(speed, risk * limit + 0.25f, $"piece {piece} seed {seed}");
                    Assert.GreaterOrEqual(speed, 0.85f * limit, $"piece {piece} seed {seed}");
                }
            }
        }

        [Test]
        public void Master_FinishesOnAtLeast18Of20Seeds()
        {
            int finished = 0;
            for (uint seed = 1; seed <= 20; seed++) if (RunBot(BotProfile.Master(), seed).Finished) finished++;
            Assert.GreaterOrEqual(finished, 18);
        }

        [Test]
        public void Master_FinishTimeIsReasonable()
        {
            int n = 0;
            for (uint seed = 1; seed <= 10; seed++)
            {
                var r = RunBot(BotProfile.Master(), seed);
                if (!r.Finished) continue;
                n++;
                float t = r.Ticks * CarSim.Step;
                Assert.GreaterOrEqual(t, 25f, "seed " + seed); Assert.LessOrEqual(t, 60f, "seed " + seed);
            }
            Assert.GreaterOrEqual(n, 8);
        }

        [Test]
        public void Master_FinishTimesVary()
        {
            var times = new List<float>();
            for (uint seed = 1; seed <= 20; seed++)
            {
                var r = RunBot(BotProfile.Master(), seed);
                if (r.Finished) times.Add(r.Ticks * CarSim.Step);
            }
            Assert.GreaterOrEqual(times.Count, 2);
            Assert.GreaterOrEqual(times.Max() - times.Min(), 0.5f);
        }

        [Test]
        public void Reckless_EliminatedOnSomeSeedsButNotAll()
        {
            int eliminated = 0;
            for (uint seed = 1; seed <= 40; seed++)
                if (RunBot(BotProfile.Reckless(), seed).Out) eliminated++;
            Assert.GreaterOrEqual(eliminated, 6); Assert.LessOrEqual(eliminated, 34);
        }

        [Test]
        public void Balanced_FinishesOnAtLeast14Of20Seeds()
        {
            int finished = 0;
            for (uint seed = 1; seed <= 20; seed++) if (RunBot(BotProfile.Balanced(), seed).Finished) finished++;
            Assert.GreaterOrEqual(finished, 14);
        }

        [Test]
        public void Slow_IsSlowerThanMaster_OnAverage()
        {
            float m = 0f, s = 0f; int nm = 0, ns = 0;
            for (uint seed = 1; seed <= 10; seed++)
            {
                var a = RunBot(BotProfile.Master(), seed);
                var b = RunBot(BotProfile.Slow(), seed);
                if (a.Finished) { m += a.Ticks; nm++; }
                if (b.Finished) { s += b.Ticks; ns++; }
            }
            Assert.Greater(nm, 0); Assert.Greater(ns, 0);
            Assert.Greater(s / ns, m / nm);
        }

        [Test]
        public void SameSeed_SameResult()
        {
            foreach (var make in new Func<BotProfile>[] { BotProfile.Master, BotProfile.Reckless, BotProfile.Balanced })
            {
                var a = RunBot(make(), 7);
                var b = RunBot(make(), 7);
                Assert.AreEqual(a.Ticks, b.Ticks);
                Assert.AreEqual(a.Distance, b.Distance);
                CollectionAssert.AreEqual(a.Brakes, b.Brakes);
            }
        }

        [Test]
        public void DifferentSeeds_DifferentForm()
        {
            var forms = new HashSet<float>();
            for (uint seed = 1; seed <= 20; seed++)
            {
                float f = new BotBrain(BotProfile.Master(), TrackR(), seed).Form;
                Assert.GreaterOrEqual(f, -1f); Assert.LessOrEqual(f, 1f);
                forms.Add(f);
            }
            Assert.GreaterOrEqual(forms.Count, 10);
        }

        [Test]
        public void ReactionTime_DelaysBraking()
        {
            for (uint seed = 1; seed <= 5; seed++)
            {
                var fast = Calm(); fast.ReactionTime = 0f;
                var slow = Calm(); slow.ReactionTime = 0.3f;
                int a = RunBot(fast, seed).FirstBrakeAfter(16);
                int b = RunBot(slow, seed).FirstBrakeAfter(16);
                Assert.Greater(a, 0);
                Assert.Greater(b, a, "seed " + seed);
            }
        }

        [Test]
        public void SlidingBot_Brakes()
        {
            // Pist V (sağa viraj R 40). Bot viraja %120 ile konur.
            var l = LateralPhysicsTests.TrackV();
            var race = new RaceSim(l, new SimTuning(), new[] { BotSetup(Calm(), 2, 1) });
            for (int i = 0; i < 20; i++) race.Tick(new[] { false });   // start gecikmesi geçsin
            race.SetStateForTest(0, 201f, 1.2f * 21.909f);             // viraj içinde, sıradaki engel uzakta
            var me = race.Racers[0];
            int checkedTicks = 0;
            for (int i = 0; i < 300; i++)
            {
                bool panic = me.Car.IsSliding && Math.Abs(me.Car.Lateral - me.Car.TargetLateral) > 1f;
                race.Tick(new[] { false });
                if (panic) { Assert.IsTrue(me.IsBraking, "tick " + i); checkedTicks++; }
                if (me.Car.IsOut) break;
            }
            Assert.Greater(checkedTicks, 0, "bot never slid");
            Assert.IsFalse(me.Car.IsOut);
        }

        [Test]
        public void LateStart_HoldsBrakeBriefly()
        {
            int lateSeeds = 0;
            var holds = new HashSet<int>();
            for (uint seed = 1; seed <= 20; seed++)
            {
                var r = RunBot(Calm(), seed);
                if (r.Brakes.Take(15).Any(x => x)) lateSeeds++;
                holds.Add(r.Brakes.Take(15).Count(x => x));
                for (int i = 15; i < 100; i++) Assert.IsFalse(r.Brakes[i], $"start brake at tick {i + 1}, seed {seed}");
            }
            Assert.Greater(lateSeeds, 0);
            Assert.GreaterOrEqual(holds.Count, 4, "start delay does not vary between seeds");
        }

        [Test]
        public void Wander_StaysWithinAmplitude()
        {
            // 2 şeritli daralma içeren pist: daralmada genlik Wander × 0.3.
            var l = new TrackLayout(new List<TrackPieceDef> { S(400), S(200, 2), S(1000) });
            var p = Calm(); p.Wander = 0.35f;
            var race = new RaceSim(l, new SimTuning(), new[] { BotSetup(p, 2, 9) });
            var me = race.Racers[0];
            float max = 0f;
            for (int i = 0; i < 3000 && me.Car.Phase != CarPhase.Finished; i++)
            {
                race.Tick(new[] { false });
                float off = me.Brain.LineOffset(me.Car);
                float amp = l.SpanAtDistance(me.Car.Distance).Def.LaneCount < 5 ? 0.3f * 0.35f : 0.35f;
                Assert.LessOrEqual(Math.Abs(off), amp + 1e-4f, "tick " + i);
                max = Math.Max(max, Math.Abs(off));
                Assert.IsFalse(me.Car.IsOut);
            }
            Assert.Greater(max, 0.2f, "bot does not wander");
        }

        [Test]
        public void BotDecision_IgnoresOtherCars()
        {
            for (uint seed = 1; seed <= 3; seed++)
            {
                var alone = RunBot(BotProfile.Balanced(), seed);
                // Yanında uzak şeritte (4, +6.4) startta duran bir oyuncu: temas etmeyecek kadar uzakta.
                var other = new RacerSetup { Name = "P", HomeLane = 4, Stats = new CarStats(), Policy = CrashPolicy.Eliminate };
                var crowd = RunBot(BotProfile.Balanced(), seed, null, other);
                CollectionAssert.AreEqual(alone.Brakes, crowd.Brakes, "seed " + seed);
            }
        }

        [Test]
        public void Bot_ClearsRamp_NeverFallsShort()
        {
            for (uint seed = 1; seed <= 20; seed++)
            {
                var r = RunBot(BotProfile.Master(), seed);
                Assert.IsFalse(r.Has(SimEventKind.FellShort), "seed " + seed);
                if (r.Finished) Assert.IsTrue(r.Has(SimEventKind.Takeoff) && r.Has(SimEventKind.Landed), "seed " + seed);
            }
        }
    }
}
