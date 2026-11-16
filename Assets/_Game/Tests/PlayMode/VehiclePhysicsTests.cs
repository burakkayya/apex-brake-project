using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ApexBrake.Racing;
using ApexBrake.Track;

namespace ApexBrake.Tests.PlayMode
{
    /// <summary>
    /// Fizik aracın his hedeflerini doğrular. Bunlar oyun kuralı DEĞİLDİR: fiziksel parametrelerin (VehicleConfig,
    /// pist geometrisi) hedeflenen davranışı ürettiğini ölçer. Bantlar geniştir.
    /// </summary>
    public sealed class VehiclePhysicsTests
    {
        CarTestRig _rig;

        [TearDown] public void TearDown() { _rig?.Dispose(); _rig = null; }

        CarTestRig Rig(float timeScale = CarTestRig.FastTimeScale) => _rig = new CarTestRig(timeScale);

        const float Kmh100 = 100f / 3.6f;
        const float ShoulderAndHalfCar = TrackPieceGeometry.ShoulderWidth + 1.0f;

        bool OffRoad(CarTestRig r)
        {
            var p = r.Pose;
            return r.Tel.TouchesGround || Mathf.Abs(p.Lateral) > p.HalfWidth + ShoulderAndHalfCar;
        }

        static void Log(string msg)
        {
            Debug.Log("[VehiclePhysicsTests] " + msg);
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(Application.dataPath, "../Logs/faz2c-tests.log"), msg + "\n"); } catch { }
        }

        // ---------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Stationary_WithEngineOff_DoesNotMove()
        {
            var r = Rig();
            r.BuildTrack(r.Kit.Straight(100f));
            r.SpawnCar(20f, 0f);
            r.Motor.EngineOn = false;
            Vector3 start = r.Motor.Body.position;
            yield return r.Run(3f);
            float moved = Vector3.Distance(start, r.Motor.Body.position);
            Log($"stationary moved {moved:F3} m, grounded {r.Tel.GroundedWheels}");
            Assert.Less(moved, 0.2f);
            Assert.AreEqual(4, r.Tel.GroundedWheels);
        }

        [UnityTest]
        public IEnumerator Straight_AcceleratesTo100Kmh_In2To5s()
        {
            var r = Rig();
            r.BuildTrack(r.Kit.Straight(600f));
            r.SpawnCar(5f, 0f);
            float t = 0f;
            yield return r.Run(8f, () => r.Speed >= Kmh100, () => t += 0.02f);
            Log($"0-100 km/h: {t:F2} s");
            Assert.That(t, Is.InRange(2f, 5f));
        }

        [UnityTest]
        public IEnumerator Straight_TopSpeed_Between130And160Kmh()
        {
            var r = Rig();
            r.BuildTrack(r.Kit.Straight(1800f));
            r.SpawnCar(5f, 0f);
            float top = 0f;
            yield return r.Run(45f, () => r.Driver.Distance > 1750f, () => top = Mathf.Max(top, r.Speed));
            Log($"top speed {top * 3.6f:F1} km/h");
            Assert.That(top * 3.6f, Is.InRange(130f, 160f));
        }

        [UnityTest]
        public IEnumerator Straight_HoldsItsLine()
        {
            var r = Rig();
            r.BuildTrack(r.Kit.Straight(800f));
            r.SpawnCar(5f, 3.2f);
            float worst = 0f;
            yield return r.Run(40f, () => r.Driver.Distance > 700f, () =>
            {
                if (r.Driver.Distance > 100f) worst = Mathf.Max(worst, Mathf.Abs(r.Driver.Lateral - 3.2f));
            });
            Log($"straight line worst deviation {worst:F3} m");
            Assert.Greater(r.Driver.Distance, 700f);
            Assert.Less(worst, 0.6f);
        }

        [UnityTest]
        public IEnumerator Brake_FromTopSpeed_StopsIn1_2To2_8s_WithoutSpinning()
        {
            var r = Rig();
            r.BuildTrack(r.Kit.Straight(1500f));
            r.SpawnCar(5f, 0f);
            yield return r.Run(40f, () => r.Driver.Distance > 900f);
            float v0 = r.Speed;
            float d0 = r.Driver.Distance;
            r.Motor.Brake = true;
            float t = 0f, minPitch = 0f;
            yield return r.Run(8f, () => r.Speed < 0.3f, () =>
            {
                t += 0.02f;
                minPitch = Mathf.Min(minPitch, -Vector3.Dot(r.Motor.transform.forward, Vector3.up)); // burun aşağı = pozitif
            });
            float heading = Vector3.Angle(Flat(r.Motor.transform.forward), Flat(r.Track.Path.ForwardAt(r.Driver.Distance)));
            Log($"brake from {v0 * 3.6f:F1} km/h: {t:F2} s, {r.Driver.Distance - d0:F1} m, heading off {heading:F1} deg");
            Assert.That(t, Is.InRange(1.2f, 2.8f));
            Assert.Less(heading, 20f);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.normalized; }

        [UnityTest]
        public IEnumerator LaneOffset_CarMovesToNewTargetLine()
        {
            var r = Rig();
            r.BuildTrack(r.Kit.Straight(800f));
            r.SpawnCar(5f, 0f);
            yield return r.Run(10f, () => r.Driver.Distance > 150f);
            r.Driver.TargetLateral = 3.2f;
            yield return r.Run(3f);
            Log($"lane change: lateral after 3 s {r.Driver.Lateral:F2} at {r.Speed * 3.6f:F0} km/h");
            Assert.Less(Mathf.Abs(r.Driver.Lateral - 3.2f), 0.6f);
            float worst = 0f;
            yield return r.Run(2f, null, () => worst = Mathf.Max(worst, Mathf.Abs(r.Driver.Lateral - 3.2f)));
            Assert.Less(worst, 0.6f, "settles on the new line");
        }

        // ---- virajlar -------------------------------------------------------------------

        struct CornerResult { public bool LeftRoad; public float MaxDev; public float MaxSlip; public float EntrySpeed; public float LeftAt; }

        IEnumerator DriveCorner(CarTestRig r, float radius, bool right, float approach, System.Action<float, float> brakePolicy, CornerResult[] result,
            VehicleConfig config = null, float exit = 200f)
        {
            r.BuildTrack(r.Kit.Straight(approach), r.Kit.Corner(radius, right), r.Kit.Straight(exit));
            r.SpawnCar(5f, 0f, 0f, config);
            float cStart = r.Track.Layout.SpanAt(1).Start, cEnd = r.Track.Layout.SpanAt(1).End;
            var res = new CornerResult { EntrySpeed = -1f };
            yield return r.Run(60f, () => res.LeftRoad || r.Driver.Distance > cEnd + exit - 20f, () =>
            {
                float d = r.Driver.Distance;
                brakePolicy?.Invoke(d - cStart, d - cEnd);
                if (res.EntrySpeed < 0f && d >= cStart) res.EntrySpeed = r.Speed;
                if (d >= cStart - 5f)
                {
                    res.MaxDev = Mathf.Max(res.MaxDev, Mathf.Abs(r.Driver.Lateral - r.Driver.TargetLateral));
                    res.MaxSlip = Mathf.Max(res.MaxSlip, Mathf.Abs(r.Tel.SlipAngleDeg));
                }
                if (OffRoad(r)) { res.LeftRoad = true; res.LeftAt = d - cStart; }
            });
            result[0] = res;
            Log($"R{radius} entry {res.EntrySpeed * 3.6f:F0} km/h: leftRoad={res.LeftRoad} (at {res.LeftAt:F0} m into corner) maxDev={res.MaxDev:F2} m maxSlip={res.MaxSlip:F1} deg");
        }

        [UnityTest]
        public IEnumerator WideCorner_R60_FlatOut_StaysOnRoad()
        {
            var r = Rig();
            var res = new CornerResult[1];
            yield return DriveCorner(r, 60f, true, 400f, null, res);
            Assert.Greater(res[0].EntrySpeed * 3.6f, 125f, "precondition: near top speed at entry");
            Assert.IsFalse(res[0].LeftRoad);
        }

        [UnityTest]
        public IEnumerator Corner_R40_FlatOut_SlidesWide()
        {
            var r = Rig();
            var res = new CornerResult[1];
            yield return DriveCorner(r, 40f, false, 400f, null, res);
            Assert.Greater(res[0].EntrySpeed * 3.6f, 125f, "precondition: near top speed at entry");
            Assert.IsTrue(res[0].MaxDev >= 2f || res[0].LeftRoad, "slides wide or leaves the road");
        }

        [UnityTest]
        public IEnumerator Corner_R40_BrakedBeforeEntry_StaysOnRoad()
        {
            var r = Rig();
            var res = new CornerResult[1];
            float brakeT = 0f;
            yield return DriveCorner(r, 40f, true, 400f, (toStart, toEnd) =>
            {
                if (toStart >= -40f && brakeT < 1f) { r.Motor.Brake = true; brakeT += 0.02f; }
                else r.Motor.Brake = false;
            }, res);
            Assert.IsFalse(res[0].LeftRoad);
        }

        [UnityTest]
        public IEnumerator TightCorner_R25_FlatOut_LeavesRoad()
        {
            var r = Rig();
            var res = new CornerResult[1];
            yield return DriveCorner(r, 25f, true, 400f, null, res);
            Assert.IsTrue(res[0].LeftRoad);
        }

        [UnityTest]
        public IEnumerator Slide_ThenBrake_CarRecoversToLine()
        {
            var r = Rig();
            var res = new CornerResult[1];
            float held = 0f, afterExit = -1f, devAfter = 99f;
            bool braking = false;
            yield return DriveCorner(r, 40f, true, 400f, (toStart, toEnd) =>
            {
                // Oyuncu gibi: kayma başlayınca frene bas, kayma geçince bırak.
                if (!braking && toStart > 0f && r.Tel.IsSliding) { braking = true; held = 0f; }
                if (braking)
                {
                    held += 0.02f;
                    if (held > 0.4f && Mathf.Abs(r.Tel.SlipAngleDeg) < 3f) braking = false;
                }
                r.Motor.Brake = braking;
                if (toEnd >= 0f)
                {
                    if (afterExit < 0f) afterExit = 0f;
                    afterExit += 0.02f;
                    if (afterExit >= 4f && devAfter > 98f) devAfter = Mathf.Abs(r.Driver.Lateral - r.Driver.TargetLateral);
                }
            }, res, null, 250f);
            Log($"slide-then-brake: deviation 4 s after exit {devAfter:F2} m");
            Assert.IsFalse(res[0].LeftRoad);
            Assert.Less(devAfter, 1f);
        }

        // ---- rampa, tümsek ----------------------------------------------------------------

        [UnityTest]
        public IEnumerator Ramp_AtSpeed_LandsOnFarSide()
        {
            var r = Rig();
            r.BuildTrack(r.Kit.Straight(150f), r.Kit.Ramp(), r.Kit.Straight(150f));
            r.SpawnCar(5f, 0f);
            float rampStart = r.Track.Layout.SpanAt(1).Start;
            float takeoffV = 0f, airborneFrom = -1f, landedAt = -1f;
            bool touchedGround = false;
            yield return r.Run(25f, () => r.Driver.Distance > r.Track.Path.TotalLength - 15f, () =>
            {
                float d = r.Driver.Distance;
                if (d < rampStart + TrackPieceGeometry.RampTakeoffArc) takeoffV = r.Speed;
                if (r.Tel.GroundedWheels == 0 && airborneFrom < 0f && d > rampStart) airborneFrom = d;
                if (airborneFrom >= 0f && landedAt < 0f && r.Tel.GroundedWheels > 0) landedAt = d;
                touchedGround |= r.Tel.TouchesGround;
            });
            Log($"ramp: takeoff {takeoffV * 3.6f:F0} km/h, flight {airborneFrom - rampStart:F1} -> {landedAt - rampStart:F1} m into piece ({landedAt - airborneFrom:F1} m), upDot {r.Tel.UpDot:F2}");
            Assert.IsFalse(touchedGround, "never touched the ground below");
            Assert.Greater(landedAt, rampStart + TrackPieceGeometry.RampGapEndArc, "landed beyond the gap");
            Assert.Greater(r.Driver.Distance, r.Track.Path.TotalLength - 15f, "continued");
            Assert.Greater(r.Tel.UpDot, 0.8f, "upright");
        }

        [UnityTest]
        public IEnumerator Ramp_Crawling_FallsIntoGap()
        {
            var r = Rig();
            r.BuildTrack(r.Kit.Straight(60f), r.Kit.Ramp(), r.Kit.Straight(60f));
            r.SpawnCar(45f, 0f, 7f);
            yield return r.Run(15f, () => r.OnGround, () => r.Motor.Brake = r.Speed > 7f); // oyuncu ≈25 km/s'te tutuyor
            Log($"crawl: touched ground {r.OnGround} at d={r.Driver.Distance:F1}");
            Assert.IsTrue(r.OnGround);
        }

        [UnityTest]
        public IEnumerator Bump_AtSpeed_GoesAirborneBriefly_AndContinues()
        {
            var r = Rig();
            r.BuildTrack(r.Kit.Straight(300f), r.Kit.Bump(), r.Kit.Straight(150f));
            r.SpawnCar(5f, 0f);
            bool airborne = false, touched = false;
            float v = 0f, airTime = 0f, maxAir = 0f;
            yield return r.Run(30f, () => r.Driver.Distance > r.Track.Path.TotalLength - 15f, () =>
            {
                if (r.Driver.Distance < 300f) v = r.Speed;
                airborne |= r.Tel.IsAirborne;
                airTime = r.Tel.GroundedWheels == 0 ? airTime + 0.02f : 0f;
                maxAir = Mathf.Max(maxAir, airTime);
                touched |= r.Tel.TouchesGround;
            });
            Log($"bump at {v * 3.6f:F0} km/h: airborne {airborne}, longest air {maxAir:F2} s, upDot {r.Tel.UpDot:F2}");
            Assert.IsTrue(airborne);
            Assert.Less(maxAir, 1.5f, "briefly");
            Assert.IsFalse(touched);
            Assert.Greater(r.Tel.UpDot, 0.8f);
        }

        [UnityTest]
        public IEnumerator NeverTunnelsThroughRoad_AtTopSpeed()
        {
            var r = Rig();
            r.BuildTrack(r.Kit.Straight(1000f), r.Kit.Ramp(), r.Kit.Straight(150f));
            r.SpawnCar(5f, 0f);
            float rampStart = r.Track.Layout.SpanAt(1).Start;
            float minH = 99f;
            yield return r.Run(60f, () => r.Driver.Distance > r.Track.Path.TotalLength - 15f || (r.Driver.Distance > 1000f && r.Speed < 0.5f), () =>
            {
                var com = r.Motor.Body.worldCenterOfMass;
                var p = r.Track.Path.Project(com, r.Driver.Distance);
                bool overGap = p.Distance > rampStart + TrackPieceGeometry.RampTakeoffArc && p.Distance < rampStart + TrackPieceGeometry.RampGapEndArc;
                if (!overGap) minH = Mathf.Min(minH, p.HeightAboveRoad);
            });
            Log($"tunnel: min COM height above road {minH:F3} m, finished at d={r.Driver.Distance:F0}");
            Assert.Greater(minH, 0f);
            // En yüksek hızda uzun uçuş sert inebilir (his hedefi: takla atabilir); burada yalnızca iniş bölgesinin geçilmesi beklenir.
            Assert.Greater(r.Driver.Distance, rampStart + TrackPieceGeometry.RampGapEndArc + 20f, "passed the landing zone");
        }

        // ---- yükseltmeler, kare hızı, telemetri ------------------------------------------


        [UnityTest]
        public IEnumerator Upgrades_EngineAndTopSpeedAndTire_ChangeBehaviour()
        {
            float[] t100 = new float[2], top = new float[2], dev = new float[2];
            var ups = new[] { VehicleUpgrades.Base, new VehicleUpgrades { Engine = 1.3f, TopSpeed = 1f, Tire = 1f } };
            for (int i = 0; i < 2; i++)
            {
                var r = Rig();
                r.BuildTrack(r.Kit.Straight(400f));
                r.Upgrades = ups[i];
                r.SpawnCar(5f, 0f);
                float t = 0f;
                yield return r.Run(8f, () => r.Speed >= Kmh100, () => t += 0.02f);
                t100[i] = t;
                _rig.Dispose(); _rig = null;
            }
            ups[1] = new VehicleUpgrades { Engine = 1f, TopSpeed = 1.2f, Tire = 1f };
            for (int i = 0; i < 2; i++)
            {
                var r = Rig();
                r.BuildTrack(r.Kit.Straight(2000f));
                r.Upgrades = ups[i];
                r.SpawnCar(5f, 0f);
                yield return r.Run(50f, () => r.Driver.Distance > 1950f, () => top[i] = Mathf.Max(top[i], r.Speed));
                _rig.Dispose(); _rig = null;
            }
            ups[1] = new VehicleUpgrades { Engine = 1f, TopSpeed = 1f, Tire = 1.3f };
            for (int i = 0; i < 2; i++)
            {
                var r = Rig();
                var res = new CornerResult[1];
                r.Upgrades = ups[i];
                yield return DriveCorner(r, 40f, true, 400f, null, res);
                dev[i] = res[0].MaxDev + (res[0].LeftRoad ? 100f - res[0].LeftAt : 0f);
                _rig.Dispose(); _rig = null;
            }
            Log($"upgrades: 0-100 {t100[0]:F2} -> {t100[1]:F2} s; top {top[0] * 3.6f:F1} -> {top[1] * 3.6f:F1} km/h; R40 score {dev[0]:F2} -> {dev[1]:F2}");
            Assert.Less(t100[1], t100[0] - 0.2f);
            Assert.Greater(top[1], top[0] + 2f);
            Assert.Less(dev[1], dev[0]);
        }

        [UnityTest]
        public IEnumerator LowFrameRate_SameOutcome()
        {
            var dist = new float[2];
            var fps = new[] { 15, 60 };
            for (int i = 0; i < 2; i++)
            {
                var r = Rig(1f);
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = fps[i];
                r.BuildTrack(r.Kit.Straight(400f));
                r.SpawnCar(5f, 0f);
                float start = r.Driver.Distance;
                float t0 = Time.fixedTime;
                int frames = 0;
                while (Time.fixedTime - t0 < 5f - 1e-4f) { yield return null; frames++; }
                dist[i] = r.Driver.Distance - start;
                Log($"fps {fps[i]}: {dist[i]:F2} m in {Time.fixedTime - t0:F2} s over {frames} frames");
                _rig.Dispose(); _rig = null;
            }
            Assert.Less(Mathf.Abs(dist[0] - dist[1]) / dist[1], 0.05f);
        }

        [UnityTest]
        public IEnumerator Telemetry_ReportsAirborneAndSliding()
        {
            var r = Rig();
            r.BuildTrack(r.Kit.Straight(150f), r.Kit.Ramp(), r.Kit.Straight(150f), r.Kit.Corner(40f, true), r.Kit.Straight(100f));
            r.SpawnCar(5f, 0f);
            yield return r.Run(1.5f);
            Assert.AreEqual(4, r.Tel.GroundedWheels);
            Assert.IsFalse(r.Tel.IsAirborne);
            Assert.Greater(r.Tel.UpDot, 0.98f);
            Assert.AreEqual(r.Speed * 3.6f, r.Tel.SpeedKmh, 1.5f); // telemetri adım başında ölçer (bir adımlık ivme farkı)
            Assert.Less(Mathf.Abs(r.Tel.SlipAngleDeg), 3f, "straight: no slip");
            bool air = false, slide = false;
            yield return r.Run(40f, () => (air && slide) || r.Tel.GroundContactWheels > 0, () =>
            {
                air |= r.Tel.IsAirborne;
                slide |= r.Tel.IsSliding;
            });
            Assert.IsTrue(air, "airborne over the ramp");
            Assert.IsTrue(slide, "sliding in the R40 corner at full throttle");
        }
    }
}
