using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ApexBrake.Racing;
using ApexBrake.Simulation;
using ApexBrake.Track;

namespace ApexBrake.Tests.PlayMode
{
    /// <summary>
    /// Bot sürücüler (hızlandırılmış). Pist: Recipe_Graybox_Race eşdeğeri (GrayboxLayouts.Race), kodla kurulur.
    /// "Tek başına" koşular: aynı pistte birden çok bot, birbirleriyle çarpışmaları kapalı (her biri tek başına sürer).
    /// Sonuçlar istatistiksel ve geniş bantlıdır; bot kararları sürücü muhakemesidir, oyun kuralı değildir.
    /// </summary>
    public sealed class BotDriverTests
    {
        internal sealed class Runner
        {
            public BotProfile Profile; public uint Seed; public int Lane;
            public VehicleMotor Motor; public BotDriver Bot; public LineDriver Driver; public VehicleTelemetry Tel;
            public readonly OutDetector Detector = new OutDetector();
            public Collider[] Colliders;
            public bool Finished, Out; public float FinishTime, OutDistance; public OutReason Reason;
            public float PassTime = -1f;            // 100 m'yi geçtiği an (farklı start mesafeleri için)
            public float MaxSlip;
            public readonly List<Vector2> BrakeTrace = new List<Vector2>();   // (mesafe, fren 0/1) her adım
            public bool Done => Finished || Out;
        }

        /// <summary>Çok araçlı "tek başına" koşu düzeneği.</summary>
        internal sealed class SoloRig : IDisposable
        {
            public readonly CarTestRig Base = new CarTestRig();
            public readonly TrackBuilder Track;
            public readonly List<Runner> Runners = new List<Runner>();
            readonly SimTuning _lanes = new SimTuning();
            public const float StartDistance = 10f;
            public float Clock;

            public SoloRig(string[] layout) { Track = Base.Kit.BuildLayout(layout); }

            public Runner Add(BotProfile profile, uint seed, int lane, float start = StartDistance)
            {
                var cfg = Base.Kit.Track(ScriptableObject.CreateInstance<VehicleConfig>());
                var motor = VehicleBuilder.Create(cfg, Color.HSVToRGB(Runners.Count * 0.17f % 1f, 0.8f, 0.9f), profile.Name + seed);
                Base.Kit.Track(motor.gameObject);
                var r = new Runner
                {
                    Profile = profile, Seed = seed, Lane = lane, Motor = motor,
                    Driver = motor.GetComponent<LineDriver>(), Tel = motor.GetComponent<VehicleTelemetry>(),
                    Colliders = motor.GetComponentsInChildren<Collider>(true),
                };
                foreach (var o in Runners)
                    foreach (var a in r.Colliders) foreach (var b in o.Colliders) Physics.IgnoreCollision(a, b, true);
                var path = Track.Path;
                float lat = Home(lane, start);
                var f = path.ForwardAt(start); f.y = 0f;
                motor.PlaceAt(path.PointAt(start, lat), Quaternion.LookRotation(f.normalized, Vector3.up));
                r.Driver.Path = path;
                r.Driver.TargetLateral = lat;
                r.Bot = motor.gameObject.AddComponent<BotDriver>();
                r.Bot.Track = Track;
                r.Bot.Profile = profile;
                r.Bot.Begin(seed);
                motor.Apply(BotDriver.UpgradesFor(profile));
                motor.EngineOn = true;
                Runners.Add(r);
                return r;
            }

            public float Home(int lane, float d) => LaneMap.LateralAt(Track.Layout, lane, d, _lanes);

            public IEnumerator Race(float maxSeconds = 120f)
            {
                float total = Track.Path.TotalLength;
                foreach (var r in Runners) r.Bot.Driving = true;
                int steps = Mathf.CeilToInt(maxSeconds / 0.02f);
                for (int k = 0; k < steps && Runners.Any(x => !x.Done); k++)
                {
                    yield return new WaitForFixedUpdate();
                    Clock += 0.02f;
                    foreach (var r in Runners)
                    {
                        if (r.Done) continue;
                        r.BrakeTrace.Add(new Vector2(r.Driver.Distance, r.Motor.Brake ? 1f : 0f));
                        if (r.PassTime < 0f && r.Driver.Distance >= 100f) r.PassTime = Clock;
                        r.MaxSlip = Mathf.Max(r.MaxSlip, Mathf.Abs(r.Tel.SlipAngleDeg));
                        r.Driver.TargetLateral = Home(r.Lane, r.Driver.Distance) + r.Bot.LineOffset;
                        if (r.Driver.Distance >= total - 0.01f)
                        {
                            r.Finished = true; r.FinishTime = Clock;
                            r.Bot.Driving = false; r.Motor.Brake = true;
                            continue;
                        }
                        var com = r.Motor.Body.worldCenterOfMass;
                        var pose = Track.Path.Project(com, r.Driver.Distance);
                        var reason = r.Detector.Step(0.02f, r.Tel, pose.HeightAboveRoad, OutDetector.RoadBelow(com));
                        if (reason != OutReason.None)
                        {
                            r.Out = true; r.Reason = reason; r.OutDistance = r.Driver.Distance;
                            r.Bot.Driving = false; r.Motor.EngineOn = false;
                        }
                    }
                }
            }

            public void Dispose() => Base.Dispose();
        }

        static string Describe(IEnumerable<Runner> rs) => string.Join("; ", rs.Select(r =>
            $"{r.Profile.Name} seed {r.Seed} lane {r.Lane} form {r.Bot.Form:+0.00;-0.00}: " +
            (r.Finished ? $"finished {r.FinishTime:0.00}s" : r.Out ? $"OUT {r.Reason} at {r.OutDistance:0}m" : "running") +
            $" mistakes {r.Bot.MistakeCount} heroes {r.Bot.HeroCount}"));

        // Aynı profil ve tohumlar birden çok testte kullanılır; koşu bir kez yapılır.
        static readonly Dictionary<string, List<Runner>> Cache = new Dictionary<string, List<Runner>>();
        static readonly List<SoloRig> CachedRigs = new List<SoloRig>();

        [OneTimeTearDown]
        public void DisposeCache()
        {
            foreach (var r in CachedRigs) r.Dispose();
            CachedRigs.Clear();
            Cache.Clear();
        }

        static IEnumerator Solo(string key, Func<BotProfile> profile, uint[] seeds, List<Runner> result)
        {
            if (Cache.TryGetValue(key, out var cached)) { result.AddRange(cached); yield break; }
            var all = new List<Runner>();
            for (int start = 0; start < seeds.Length; start += 5)
            {
                var rig = new SoloRig(GrayboxLayouts.Race);
                for (int i = start; i < Math.Min(seeds.Length, start + 5); i++) rig.Add(profile(), seeds[i], i - start);
                yield return rig.Race();
                all.AddRange(rig.Runners);
                // Sonuçlar okunduktan sonra nesneler gerekmez; zaman ayarlarını geri yükle, pisti temizle.
                rig.Dispose();
            }
            Debug.Log("[BotDriverTests] " + key + ": " + Describe(all));
            Cache[key] = all;
            result.AddRange(all);
        }

        static readonly uint[] FiveSeeds = { 11, 22, 33, 44, 55 };
        static readonly uint[] EightSeeds = { 101, 202, 303, 404, 505, 606, 707, 808 };

        // ---------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Master_Alone_FinishesOnAtLeast4Of5Seeds()
        {
            var rs = new List<Runner>();
            yield return Solo("Master5", BotProfile.Master, FiveSeeds, rs);
            Assert.GreaterOrEqual(rs.Count(r => r.Finished), 4, Describe(rs));
        }

        [UnityTest]
        public IEnumerator Master_BrakesBeforeTightCorner()
        {
            using (var rig = new SoloRig(GrayboxLayouts.Race))
            {
                var r = rig.Add(BotProfile.Master(), 7, 2);
                PieceSpan tight = default;
                for (int i = 0; i < rig.Track.Layout.PieceCount; i++)
                {
                    var s = rig.Track.Layout.SpanAt(i);
                    if (s.Def.Kind == PieceKind.Corner && s.Def.Radius <= 25f) { tight = s; break; }
                }
                Assert.Greater(tight.End, 0f, "track has an R25 corner");
                yield return rig.Race(60f);
                bool braked = r.BrakeTrace.Any(p => p.y > 0f && p.x > tight.Start - 120f && p.x < tight.Start + 5f);
                Assert.IsTrue(braked, "braked before R25; " + Describe(rig.Runners));
                Assert.IsFalse(r.Out && r.OutDistance < tight.End + 10f, "stayed on road through R25; " + Describe(rig.Runners));
            }
        }

        [UnityTest]
        public IEnumerator Reckless_GoesOutOnSomeSeeds()
        {
            var rs = new List<Runner>();
            yield return Solo("Reckless8", BotProfile.Reckless, EightSeeds, rs);
            int outs = rs.Count(r => r.Out);
            Assert.That(outs, Is.InRange(1, 7), Describe(rs));
        }

        [UnityTest]
        public IEnumerator FinishTimes_VaryAcrossSeeds()
        {
            var rs = new List<Runner>();
            yield return Solo("Master5", BotProfile.Master, FiveSeeds, rs);
            var times = rs.Where(r => r.Finished).Select(r => r.FinishTime).ToList();
            Assert.GreaterOrEqual(times.Count, 2, Describe(rs));
            Assert.GreaterOrEqual(times.Max() - times.Min(), 0.3f, Describe(rs));
        }

        [UnityTest]
        public IEnumerator Slow_IsSlowerThanMaster_OnAverage()
        {
            var master = new List<Runner>();
            yield return Solo("Master5", BotProfile.Master, FiveSeeds, master);
            var slow = new List<Runner>();
            yield return Solo("Slow5", BotProfile.Slow, FiveSeeds, slow);
            var mt = master.Where(r => r.Finished).Select(r => r.FinishTime).ToList();
            var st = slow.Where(r => r.Finished).Select(r => r.FinishTime).ToList();
            Assert.IsNotEmpty(mt, Describe(master));
            Assert.IsNotEmpty(st, Describe(slow));
            Assert.Greater(st.Average(), mt.Average(), Describe(master) + " | " + Describe(slow));
        }

        // ---- 5 araçlı yarış (RaceManager) --------------------------------------------------

        internal sealed class RaceRig : IDisposable
        {
            public readonly CarTestRig Base = new CarTestRig();
            public readonly TrackBuilder Track;
            public readonly RaceManager Race;
            public readonly List<(int a, int b, float v)> Contacts = new List<(int, int, float)>();
            public float Clock;

            public RaceRig(string[] layout, Action<RaceManager> configure = null)
            {
                Track = layout != null ? Base.Kit.BuildLayout(layout) : null;
                var go = Base.Kit.Track(new GameObject("RaceManager"));
                go.SetActive(false);
                Race = go.AddComponent<RaceManager>();
                Race.track = Track;
                Race.input = Base.Kit.Track(new GameObject("Input")).AddComponent<TouchBrakeInput>();
                Race.RacerContact += (a, b, v) => Contacts.Add((a, b, v));
                configure?.Invoke(Race);
                go.SetActive(true);
            }

            public IEnumerator Run(float seconds, Func<bool> until = null, Action step = null)
            {
                int steps = Mathf.CeilToInt(seconds / 0.02f);
                for (int k = 0; k < steps; k++)
                {
                    yield return new WaitForFixedUpdate();
                    Clock += 0.02f;
                    step?.Invoke();
                    if (until != null && until()) yield break;
                }
            }

            public bool AllDone => Race.Racers.Count > 0 && Race.Racers.All(e => e.Finished || e.IsOut);

            public string Summary() => $"seed {Race.CurrentSeed}: " + string.Join(", ", Race.Racers.OrderBy(e => e.Rank).Select(e =>
                $"{e.Rank}.{e.Name} " + (e.Finished ? $"{e.FinishTime:0.00}s" : e.IsOut ? $"OUT {e.OutReason} {e.Distance:0}m" : $"{e.Distance:0}m")));

            public void Dispose() => Base.Dispose();
        }

        [UnityTest]
        public IEnumerator FiveCarRace_Completes_Within120s()
        {
            using (var rig = new RaceRig(GrayboxLayouts.Race, m => m.raceSeed = 4242))
            {
                yield return rig.Run(125f, () => rig.AllDone);
                Debug.Log("[BotDriverTests] race " + rig.Summary());
                Assert.IsTrue(rig.AllDone, rig.Summary());
                Assert.LessOrEqual(rig.Clock, 123f, rig.Summary());
                Assert.GreaterOrEqual(rig.Race.Racers.Count(e => !e.IsPlayer && e.Finished), 2, "most bots finish; " + rig.Summary());
            }
        }

        [UnityTest]
        public IEnumerator FiveCarRace_WinnerVaries()
        {
            var winners = new List<string>();
            var log = new List<string>();
            using (var rig = new RaceRig(GrayboxLayouts.Race, m => m.raceSeed = 0))
            {
                for (int race = 0; race < 6; race++)
                {
                    if (race > 0) rig.Race.Restart();
                    yield return rig.Run(125f, () => rig.AllDone);
                    var winner = rig.Race.Racers.Where(e => e.Finished).OrderBy(e => e.FinishTime).FirstOrDefault();
                    winners.Add(winner != null ? winner.Name : "-");
                    log.Add(rig.Summary());
                }
            }
            Debug.Log("[BotDriverTests] winners " + string.Join(",", winners) + "\n" + string.Join("\n", log));
            Assert.GreaterOrEqual(winners.Where(w => w != "-").Distinct().Count(), 2, string.Join("\n", log));
        }

        [UnityTest]
        public IEnumerator FasterCar_OvertakesSlowerCar_WithoutCrashingIt()
        {
            // İki araç aynı şeritte; öndeki bot daha düşük hız tavanlı. Arkadaki (oyuncu, tam gaz) yana kayıp geçmeli.
            using (var rig = new RaceRig(new[] { "Straight100", "Straight100", "Straight100", "Straight100", "Straight100", "Straight100", "Straight100", "Straight100" }, m =>
            {
                m.racerCount = 2;
                m.homeLanes = new[] { 2, 2 };
                m.racerUpgrades = new[] { VehicleUpgrades.Base, new VehicleUpgrades { Engine = 0.8f, TopSpeed = 0.6f, Tire = 1f } };
                m.raceSeed = 5;
            }))
            {
                yield return rig.Run(0.2f);
                rig.Race.PlaceOnGrid(1, 60f);
                var player = rig.Race.Racers[0]; var slow = rig.Race.Racers[1];
                bool overtook = false, passed = false;
                float passedAt = -1f, maxSide = 0f;
                yield return rig.Run(40f, () => (passed && rig.Clock - passedAt > 3f) || player.IsOut || slow.IsOut || player.Finished, () =>
                {
                    overtook |= player.IsOvertaking;
                    maxSide = Mathf.Max(maxSide, Mathf.Abs(player.Lateral - slow.Lateral));
                    if (!passed && player.Distance > slow.Distance + 10f) { passed = true; passedAt = rig.Clock; }
                });
                string info = $" maxSide {maxSide:0.0} m; " + rig.Summary();
                Assert.IsFalse(player.IsOut, "player stayed on road;" + info);
                Assert.IsFalse(slow.IsOut, "slow car stayed on road;" + info);
                Assert.IsTrue(overtook, "overtake planned;" + info);
                Assert.IsTrue(passed, "passed; " + rig.Summary());
                Assert.IsEmpty(rig.Contacts, "no contact; " + rig.Summary());
            }
        }
    }
}
