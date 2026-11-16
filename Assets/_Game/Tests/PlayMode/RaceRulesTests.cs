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
    /// <summary>Yarış kuralları: tek kural "yol dışı / düşme / ters dönme → oyun dışı"; start, sıra, bitiş, yeniden doğma, temas.</summary>
    public sealed class RaceRulesTests
    {
        sealed class Rig : IDisposable
        {
            public readonly CarTestRig Base = new CarTestRig();
            public readonly TrackBuilder Track;
            public readonly RaceManager Race;
            public readonly TouchBrakeInput Input;
            public readonly List<(int racer, OutReason reason, float t)> Outs = new List<(int, OutReason, float)>();
            public readonly List<(int racer, float t)> Respawns = new List<(int, float)>();
            public readonly List<(int a, int b, float v)> Contacts = new List<(int, int, float)>();
            public readonly List<(int racer, float t)> Finishes = new List<(int, float)>();
            public float EndedAt = -1f, Clock;
            public int RestartedCount;

            public Rig(OutPolicy player, OutPolicy bots, Func<TestTrackKit, TrackPieceAsset[]> pieces, VehicleUpgrades[] ups = null)
            {
                Track = Base.Kit.Build(pieces(Base.Kit));
                Input = Base.Kit.Track(new GameObject("Input")).AddComponent<TouchBrakeInput>();
                var go = Base.Kit.Track(new GameObject("RaceManager"));
                go.SetActive(false);
                Race = go.AddComponent<RaceManager>();
                Race.track = Track;
                Race.input = Input;
                Race.playerPolicy = player;
                Race.botPolicy = bots;
                Race.racerUpgrades = ups;
                Race.RacerOut += (i, r) => Outs.Add((i, r, Clock));
                Race.RacerRespawned += i => Respawns.Add((i, Clock));
                Race.RacerContact += (a, b, v) => Contacts.Add((a, b, v));
                Race.RacerFinished += i => Finishes.Add((i, Clock));
                Race.Ended += () => EndedAt = Clock;
                Race.Restarted += () => RestartedCount++;
                go.SetActive(true);
            }

            public RacerEntry this[int i] => Race.Racers[i];
            public float Speed(int i) => this[i].Motor.Body.linearVelocity.magnitude;

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

            public IEnumerator UntilRacing(float extra = 0f)
            {
                yield return Run(6f, () => Race.Racers != null && Race.Racers.Count > 0 && Race.State == RaceState.Racing);
                if (extra > 0f) yield return Run(extra);
            }

            /// <summary>Test kurulumu: aracı yol dışındaki zemine (Ground) koyar.</summary>
            public void PutOffRoad(int i, float distance, float lateral = 25f)
            {
                var p = Track.Path.PointAt(distance, lateral);
                p.y = Track.transform.position.y - TrackPieceGeometry.GroundDrop;
                var f = Track.Path.ForwardAt(distance); f.y = 0f;
                this[i].Motor.PlaceAt(p, Quaternion.LookRotation(f.normalized, Vector3.up));
            }

            public void Dispose() => Base.Dispose();
        }

        Rig _rig;
        [TearDown] public void TearDown() { _rig?.Dispose(); _rig = null; }

        Rig New(OutPolicy player, OutPolicy bots, Func<TestTrackKit, TrackPieceAsset[]> pieces, VehicleUpgrades[] ups = null)
            => _rig = new Rig(player, bots, pieces, ups);

        static TrackPieceAsset[] Straight(TestTrackKit k, float len) => new[] { k.Straight(len) };

        // ---------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Countdown_CarsStayStill_ThenGo()
        {
            var r = New(OutPolicy.Eliminate, OutPolicy.Eliminate, k => Straight(k, 600f));
            yield return r.Run(0.1f);
            Assert.AreEqual(5, r.Race.Racers.Count);
            Assert.AreEqual(RaceState.Countdown, r.Race.State);
            var start = r.Race.Racers.Select(e => e.Motor.Body.position).ToArray();
            float maxMove = 0f, prevCountdown = r.Race.CountdownRemaining;
            bool decreasing = true;
            yield return r.Run(4f, () => r.Race.State == RaceState.Racing, () =>
            {
                for (int i = 0; i < 5; i++) maxMove = Mathf.Max(maxMove, Vector3.Distance(start[i], r[i].Motor.Body.position));
                if (r.Race.State == RaceState.Countdown) { decreasing &= r.Race.CountdownRemaining <= prevCountdown; prevCountdown = r.Race.CountdownRemaining; }
            });
            Assert.AreEqual(RaceState.Racing, r.Race.State);
            Assert.Less(maxMove, 0.1f, "still during countdown");
            Assert.IsTrue(decreasing);
            Assert.That(r.Clock, Is.InRange(2.9f, 3.2f), "countdown ~3 s");
            // aynı hizada, ev çizgilerinde
            for (int i = 0; i < 5; i++)
            {
                Assert.AreEqual(r[0].Distance, r[i].Distance, 0.3f, "aligned " + i);
                Assert.AreEqual(LaneMap.LateralOffset(r[i].HomeLane, 5, 3.2f), r[i].Lateral, 0.3f, "home line " + i);
            }
            yield return r.Run(2f);
            for (int i = 0; i < 5; i++) Assert.Greater(Vector3.Distance(start[i], r[i].Motor.Body.position), 5f, "moves after go " + i);
        }

        [UnityTest]
        public IEnumerator Start_FiveCars_NoContactInFirst2Seconds()
        {
            var r = New(OutPolicy.Eliminate, OutPolicy.Eliminate, k => Straight(k, 600f));
            yield return r.UntilRacing();
            yield return r.Run(2f);
            Assert.AreEqual(0, r.Contacts.Count);
            Assert.AreEqual(0, r.Outs.Count);
            Assert.IsTrue(r.Race.Racers.All(e => e.Distance > 15f));
        }

        [UnityTest]
        public IEnumerator DrivingOffTheSide_IsOut_OffRoad()
        {
            var r = New(OutPolicy.Eliminate, OutPolicy.Eliminate, k => Straight(k, 800f));
            yield return r.UntilRacing(1f);
            yield return DropBehind(r);
            var body = r[0].Motor.Body;
            yield return r.Run(8f, () => r.Outs.Count > 0, () =>
            {
                // dış kuvvet: yola dik itiş (test)
                var right = r.Track.Path.RightAt(r[0].Distance);
                body.AddForce(right * body.mass * 60f, ForceMode.Force);
            });
            Assert.AreEqual(1, r.Outs.Count);
            Assert.AreEqual(0, r.Outs[0].racer);
            Assert.AreEqual(OutReason.OffRoad, r.Outs[0].reason);
            Assert.IsTrue(r[0].IsOut);
            Assert.AreEqual(OutReason.OffRoad, r[0].OutReason);
        }

        [UnityTest]
        public IEnumerator OnShoulder_IsNotOut()
        {
            var r = New(OutPolicy.Eliminate, OutPolicy.Eliminate, k => Straight(k, 800f));
            yield return r.UntilRacing(1f);
            yield return DropBehind(r);
            float hw = r.Track.Path.HalfWidthAt(100f);
            // araç 2 m/sn ile kayarak banketin üstüne çıkar (tümüyle banket: 1,5 m banket, gövde yarı genişliği 0,9 m)
            float target = r[0].Lateral, maxLat = 0f;
            yield return r.Run(6f, null, () =>
            {
                target = Mathf.Min(hw + 0.5f, target + 2f * 0.02f);
                r[0].TargetLateralOverride = target;
                maxLat = Mathf.Max(maxLat, r[0].Lateral);
            });
            yield return r.Run(5f, null, () =>
            {
                target = Mathf.Max(0f, target - 2f * 0.02f);   // yola geri döner
                r[0].TargetLateralOverride = target;
            });
            r[0].TargetLateralOverride = null;
            yield return r.Run(2f);
            Assert.Greater(maxLat, hw + 0.3f, "precondition: car was on the shoulder");
            Assert.IsFalse(r.Outs.Any(o => o.racer == 0));
            Assert.IsFalse(r[0].IsOut);
            Assert.Less(Mathf.Abs(r[0].Lateral - LaneMap.LateralOffset(2, 5, 3.2f)), 1f, "back on its line");
        }

        [UnityTest]
        public IEnumerator AirborneOverGap_IsNotOut()
        {
            var r = New(OutPolicy.Eliminate, OutPolicy.Eliminate, k => new[] { k.Straight(150f), k.Ramp(), k.Straight(150f) });
            yield return r.UntilRacing();
            bool flew = false;
            yield return r.Run(25f, () => r.Race.Racers.All(e => e.Finished || e.IsOut), () => flew |= r[0].Telemetry.IsAirborne);
            Assert.IsTrue(flew, "precondition: player flew over the gap");
            Assert.AreEqual(0, r.Outs.Count, "nobody is out while flying over the gap");
            Assert.IsTrue(r.Race.Racers.All(e => e.Finished));
        }

        [UnityTest]
        public IEnumerator FallingIntoGap_IsOut()
        {
            var r = New(OutPolicy.Eliminate, OutPolicy.Eliminate, k => new[] { k.Straight(120f), k.Ramp(), k.Straight(100f) });
            yield return r.UntilRacing();
            // oyuncu ≈25 km/s'te sürünür (fren aç/kapa)
            yield return r.Run(30f, () => r.Outs.Any(o => o.racer == 0), () => r.Input.ForceBrake = r.Speed(0) > 7f);
            var o0 = r.Outs.FirstOrDefault(o => o.racer == 0);
            Assert.IsTrue(r[0].IsOut);
            Assert.That(o0.reason, Is.EqualTo(OutReason.Fell).Or.EqualTo(OutReason.OffRoad));
            float gapStart = r.Track.Layout.SpanAt(1).Start + TrackPieceGeometry.RampTakeoffArc;
            Assert.That(r[0].Distance, Is.InRange(gapStart - 3f, gapStart + TrackPieceGeometry.RampGap + 1f));
        }

        [UnityTest]
        public IEnumerator Upturned_For2s_IsOut_Flipped()
        {
            var r = New(OutPolicy.Eliminate, OutPolicy.Eliminate, k => Straight(k, 800f));
            yield return r.UntilRacing(1f);
            float d = r[0].Distance + 20f;
            var f = r.Track.Path.ForwardAt(d); f.y = 0f;
            var rot = Quaternion.LookRotation(f.normalized, Vector3.up) * Quaternion.Euler(0f, 0f, 180f);
            r[0].Motor.PlaceAt(r.Track.Path.PointAt(d, 0f) + Vector3.up * 1.6f, rot);   // ters çevrilip bırakılır
            float t0 = r.Clock;
            yield return r.Run(5f, () => r.Outs.Any(o => o.racer == 0));
            var o0 = r.Outs.First(o => o.racer == 0);
            Assert.AreEqual(OutReason.Flipped, o0.reason);
            Assert.That(o0.t - t0, Is.InRange(1.9f, 3.0f));
        }

        [UnityTest]
        public IEnumerator BrieflyTilted_IsNotOut()
        {
            var r = New(OutPolicy.Eliminate, OutPolicy.Eliminate, k => Straight(k, 800f));
            yield return r.UntilRacing(0.5f);
            r.Input.ForceBrake = true;
            yield return r.Run(2f, () => r.Speed(0) < 0.2f);
            var body = r[0].Motor.Body;
            var basePos = body.position;
            var upright = body.rotation;
            float minUp = 1f;
            // 1 sn yan yatırılır (test kurulumu), sonra bırakılır
            yield return r.Run(1f, null, () =>
            {
                body.position = basePos + Vector3.up * 0.9f;
                body.rotation = upright * Quaternion.Euler(0f, 0f, 80f);
                body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero;
                minUp = Mathf.Min(minUp, r[0].Telemetry.UpDot);
            });
            body.rotation = upright * Quaternion.Euler(0f, 0f, 45f);
            yield return r.Run(4f);
            Assert.Less(minUp, 0.3f, "precondition: tilted beyond the flip angle");
            Assert.Greater(r[0].Telemetry.UpDot, 0.9f, "back on its wheels");
            Assert.IsFalse(r.Outs.Any(o => o.racer == 0));
        }

        /// <summary>Oyuncu frenleyip grubun arkasına düşer (yanındaki araçlara çarpmadan yana gidebilsin).</summary>
        static IEnumerator DropBehind(Rig r)
        {
            r.Input.ForceBrake = true;
            yield return r.Run(1.5f);
            r.Input.ForceBrake = false;
            yield return r.Run(0.5f);
        }

        IEnumerator PushOff(Rig r, int i, float maxSeconds = 8f)
        {
            var body = r[i].Motor.Body;
            yield return r.Run(maxSeconds, () => r[i].IsOut, () =>
            {
                if (r[i].IsOut) return;
                body.AddForce(r.Track.Path.RightAt(r[i].Distance) * body.mass * 60f, ForceMode.Force);
            });
        }

        [UnityTest]
        public IEnumerator Eliminate_StopsCar_KeepsDistance_RankDrops()
        {
            var r = New(OutPolicy.Eliminate, OutPolicy.Eliminate, k => Straight(k, 1200f));
            yield return r.UntilRacing(1f);
            yield return DropBehind(r);
            yield return PushOff(r, 0);
            Assert.IsTrue(r[0].IsOut);
            float frozen = r[0].Distance;
            Assert.IsFalse(r[0].Motor.EngineOn, "engine off");
            yield return r.Run(5f);
            Assert.AreEqual(frozen, r[0].Distance, 0.01f, "keeps its distance");
            Assert.Less(r.Speed(0), 1f, "stopped");
            Assert.AreEqual(5, r[0].Rank, "everyone passed");
            // 1,5 sn sonra diğer araçlarla çarpışması kapalı
            var mine = r[0].Motor.GetComponentInChildren<BoxCollider>();
            var other = r[1].Motor.GetComponentInChildren<BoxCollider>();
            Assert.IsTrue(Physics.GetIgnoreCollision(mine, other));
            Assert.AreEqual(1, r.Outs.Count(o => o.racer == 0), "out raised once");
        }

        [UnityTest]
        public IEnumerator Respawn_After1_5s_30mBack_OnHomeLine_Upright_Stopped()
        {
            var r = New(OutPolicy.Respawn, OutPolicy.Eliminate, k => Straight(k, 1200f));
            yield return r.UntilRacing(4f);
            yield return DropBehind(r);
            yield return PushOff(r, 0);
            float outT = r.Outs.First(o => o.racer == 0).t;
            float speedAtRespawn = -1f;
            r.Race.RacerRespawned += i => { if (i == 0) speedAtRespawn = r.Speed(0); };
            float outD = r[0].Distance;
            Assert.IsTrue(r[0].IsRespawning);
            yield return r.Run(3f, () => r.Respawns.Count > 0);
            Assert.AreEqual(1, r.Respawns.Count);
            Assert.AreEqual(1.5f, r.Respawns[0].t - outT, 0.05f);
            Assert.IsFalse(r[0].IsOut);
            Assert.IsFalse(r[0].IsRespawning);
            Assert.AreEqual(outD - 30f, r[0].Distance, 1.5f);
            Assert.AreEqual(LaneMap.LateralOffset(2, 5, 3.2f), r[0].Lateral, 0.3f);
            Assert.Greater(r[0].Telemetry.UpDot, 0.95f);
            Assert.AreEqual(0f, speedAtRespawn, 0.01f, "placed at rest");
            var fwd = r[0].Motor.transform.forward; fwd.y = 0f;
            var road = r.Track.Path.ForwardAt(r[0].Distance); road.y = 0f;
            Assert.Less(Vector3.Angle(fwd, road), 3f, "faces the road direction");
        }

        [UnityTest]
        public IEnumerator Respawn_NeverOnRampOrBump()
        {
            var r = New(OutPolicy.Eliminate, OutPolicy.Respawn, k => new[] { k.Straight(100f), k.Bump(), k.Straight(100f), k.Ramp(), k.Straight(100f) });
            yield return r.UntilRacing();
            var bump = r.Track.Layout.SpanAt(1);
            var ramp = r.Track.Layout.SpanAt(3);
            foreach (var (span, racer) in new[] { (bump, 1), (ramp, 2) })
            {
                float outAt = span.Start + 30f + Mathf.Min(5f, (span.End - span.Start) * 0.5f);   // 30 m geri → parçanın üstü
                r.PutOffRoad(racer, outAt);
                int before = r.Respawns.Count;
                yield return r.Run(4f, () => r.Respawns.Count > before);
                Assert.Greater(r.Respawns.Count, before, "respawned " + racer);
                float d = r[racer].Distance;
                Assert.IsFalse(d > span.Start - 3f && d < span.End, $"racer {racer} respawned at {d:F1} on {span.Def.Kind} [{span.Start:F1},{span.End:F1}]");
                Assert.Greater(r[racer].Telemetry.UpDot, 0.95f);
            }
        }

        [UnityTest]
        public IEnumerator Respawn_CarCanContinueAndFinish()
        {
            var r = New(OutPolicy.Respawn, OutPolicy.Eliminate, k => Straight(k, 300f));
            yield return r.UntilRacing(1f);
            r.PutOffRoad(0, 150f);
            yield return r.Run(4f, () => r.Respawns.Count > 0);
            Assert.AreEqual(1, r.Respawns.Count);
            yield return r.Run(20f, () => r[0].Finished);
            Assert.IsTrue(r[0].Finished);
            Assert.IsFalse(r[0].IsOut);
        }

        [UnityTest]
        public IEnumerator Rank_OrdersByDistance_FinishedFirst()
        {
            var ups = new[]
            {
                new VehicleUpgrades { Engine = 1.4f, TopSpeed = 1.2f, Tire = 1f },
                new VehicleUpgrades { Engine = 0.7f, TopSpeed = 1f, Tire = 1f },
                new VehicleUpgrades { Engine = 0.8f, TopSpeed = 1f, Tire = 1f },
                new VehicleUpgrades { Engine = 0.9f, TopSpeed = 1f, Tire = 1f },
                new VehicleUpgrades { Engine = 1.0f, TopSpeed = 1f, Tire = 1f },
            };
            var r = New(OutPolicy.Eliminate, OutPolicy.Eliminate, k => Straight(k, 400f), ups);
            yield return r.UntilRacing();
            int violations = 0;
            bool sawMixed = false;
            yield return r.Run(30f, () => r.Race.Racers.All(e => e.Finished), () =>
            {
                var list = r.Race.Racers.ToList();
                var expected = list.OrderByDescending(e => e.Finished).ThenBy(e => e.Finished ? e.FinishTime : 0f).ThenByDescending(e => e.Distance).ToList();
                for (int i = 0; i < expected.Count; i++) if (expected[i].Rank != i + 1) violations++;
                sawMixed |= list.Any(e => e.Finished) && list.Any(e => !e.Finished);
            });
            Assert.AreEqual(0, violations);
            Assert.IsTrue(sawMixed, "precondition: finished and racing cars at the same time");
            Assert.AreEqual(1, r[0].Rank);
            Assert.AreEqual(5, r[1].Rank, "weakest engine last");
            Assert.AreEqual(5, r.Finishes.Count);
        }

        [UnityTest]
        public IEnumerator PlayerFinish_EndsRaceAfterDelay()
        {
            var r = New(OutPolicy.Eliminate, OutPolicy.Eliminate, k => Straight(k, 200f));
            yield return r.UntilRacing();
            yield return r.Run(20f, () => r.EndedAt >= 0f);
            var fin = r.Finishes.First(f => f.racer == 0);
            Assert.AreEqual(1.5f, r.EndedAt - fin.t, 0.05f);
            Assert.AreEqual(RaceState.Ended, r.Race.State);
            Assert.IsTrue(r[0].Finished);
            Assert.Greater(r[0].FinishTime, 3f);
            float elapsed = r.Race.ElapsedTime;
            yield return r.Run(1f);
            Assert.AreEqual(elapsed, r.Race.ElapsedTime, 1e-4f, "clock stops");
        }

        [UnityTest]
        public IEnumerator Restart_ResetsEverything_NoStaleEvents()
        {
            var r = New(OutPolicy.Eliminate, OutPolicy.Eliminate, k => Straight(k, 800f));
            yield return r.UntilRacing(3f);
            r.PutOffRoad(0, r[0].Distance);   // çıkış kesinleşmeden (0,4 sn) yeniden başlat
            r.PutOffRoad(1, r[1].Distance);
            yield return r.Run(0.1f);
            int outsBefore = r.Outs.Count;
            r.Race.Restart();
            Assert.AreEqual(RaceState.Countdown, r.Race.State);
            Assert.AreEqual(2, r.RestartedCount);
            yield return r.Run(2f);
            Assert.AreEqual(outsBefore, r.Outs.Count, "no stale out events");
            Assert.AreEqual(RaceState.Countdown, r.Race.State);
            Assert.AreEqual(0f, r.Race.ElapsedTime, 1e-4f);
            for (int i = 0; i < 5; i++)
            {
                Assert.IsFalse(r[i].IsOut || r[i].Finished || r[i].IsRespawning, "reset " + i);
                Assert.IsFalse(r[i].Motor.EngineOn);
                Assert.AreEqual(r.Race.Racers[0].Distance, r[i].Distance, 0.5f);
                Assert.Less(r.Speed(i), 0.3f);
            }
            yield return r.Run(4f, () => r.Race.State == RaceState.Racing);
            Assert.AreEqual(RaceState.Racing, r.Race.State);
        }

        [UnityTest]
        public IEnumerator CarsCollide_PhysicallyAndRaiseContact()
        {
            var r = New(OutPolicy.Eliminate, OutPolicy.Eliminate, k => Straight(k, 800f));
            yield return r.UntilRacing(0.5f);
            // Öndeki araç (1) durur; oyuncu aynı çizgide arkadan gelir.
            r[1].TargetLateralOverride = 0f;
            r[0].TargetLateralOverride = 0f;
            float d = r[0].Distance + 40f;
            var f = r.Track.Path.ForwardAt(d); f.y = 0f;
            r[1].Motor.PlaceAt(r.Track.Path.PointAt(d, 0f), Quaternion.LookRotation(f.normalized, Vector3.up));
            r[1].Motor.EngineOn = false;
            yield return r.Run(0.5f);
            Vector3 before = r[1].Motor.Body.position;
            yield return r.Run(8f, () => r.Contacts.Count > 0);
            Assert.Greater(r.Contacts.Count, 0);
            var c = r.Contacts[0];
            Assert.IsTrue((c.a == 0 && c.b == 1) || (c.a == 1 && c.b == 0), $"contact pair {c.a},{c.b}");
            Assert.Greater(c.v, 3f, "relative speed");
            yield return r.Run(1f);
            Assert.Greater(Vector3.Distance(before, r[1].Motor.Body.position), 1f, "front car pushed forward");
        }
    }
}
