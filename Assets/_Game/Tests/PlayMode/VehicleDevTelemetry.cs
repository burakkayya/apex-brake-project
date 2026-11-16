using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ApexBrake.Racing;
using ApexBrake.Track;

namespace ApexBrake.Tests.PlayMode
{
    /// <summary>
    /// Geliştirme ölçümü (Explicit; normal koşuda çalışmaz): senaryoları sürer ve zaman serisini
    /// Logs/faz2c-telemetry/&lt;senaryo&gt;.csv dosyasına yazar. Hiçbir şey doğrulamaz.
    /// </summary>
    [Explicit]
    public sealed class VehicleDevTelemetry
    {
        CarTestRig _rig;
        StringBuilder _csv;
        float _t;

        [TearDown] public void TearDown() { _rig?.Dispose(); _rig = null; }

        static string Dir => Path.Combine(Application.dataPath, "../Logs/faz2c-telemetry");

        void Begin() { _csv = new StringBuilder("t,d,lat,target,speed_kmh,slip_deg,grounded,road,ground,pitch_deg,roll_deg,updot,steer_deg,brake,height\n"); _t = 0f; }

        void Sample()
        {
            var r = _rig;
            var com = r.Motor.Body.worldCenterOfMass;
            var p = r.Track.Path.Project(com, r.Driver.Distance);
            _csv.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:F2},{1:F2},{2:F2},{3:F2},{4:F1},{5:F1},{6},{7},{8},{9:F1},{10:F1},{11:F2},{12:F1},{13},{14:F2}",
                _t, r.Driver.Distance, r.Driver.Lateral, r.Driver.TargetLateral, r.Tel.SpeedKmh, r.Tel.SlipAngleDeg,
                r.Tel.GroundedWheels, r.Tel.RoadContactWheels, r.Tel.GroundContactWheels, r.Tel.PitchDeg, r.Tel.RollDeg, r.Tel.UpDot,
                r.Motor.SteerAngle, r.Motor.Brake ? 1 : 0, p.HeightAboveRoad));
            _t += 0.02f;
        }

        void End(string name)
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(Path.Combine(Dir, name + ".csv"), _csv.ToString());
        }

        IEnumerator Drive(string name, float maxSeconds, System.Action policy = null, System.Func<bool> until = null)
        {
            Begin();
            yield return _rig.Run(maxSeconds, () => (until != null && until()) || _rig.Driver.Distance > _rig.Track.Path.TotalLength - 10f,
                () => { policy?.Invoke(); Sample(); });
            End(name);
        }

        [UnityTest] public IEnumerator Launch_And_Brake()
        {
            _rig = new CarTestRig();
            _rig.BuildTrack(_rig.Kit.Straight(1500f));
            _rig.SpawnCar(5f, 0f);
            yield return Drive("launch_brake", 50f, () => _rig.Motor.Brake = _rig.Driver.Distance > 900f, () => _rig.Driver.Distance > 900f && _rig.Speed < 0.2f);
        }

        IEnumerator Corner(string name, float radius, bool brakeBefore, bool brakeOnSlide)
        {
            _rig = new CarTestRig();
            _rig.BuildTrack(_rig.Kit.Straight(400f), _rig.Kit.Corner(radius, true), _rig.Kit.Straight(250f));
            _rig.SpawnCar(5f, 0f);
            float cStart = _rig.Track.Layout.SpanAt(1).Start;
            float bt = 0f, held = 0f; bool braking = false;
            yield return Drive(name, 40f, () =>
            {
                float d = _rig.Driver.Distance;
                if (brakeBefore) { if (d >= cStart - 40f && bt < 1f) { _rig.Motor.Brake = true; bt += 0.02f; } else _rig.Motor.Brake = false; }
                if (brakeOnSlide)
                {
                    if (!braking && d > cStart && _rig.Tel.IsSliding) { braking = true; held = 0f; }
                    if (braking) { held += 0.02f; if (held > 0.4f && Mathf.Abs(_rig.Tel.SlipAngleDeg) < 3f) braking = false; }
                    _rig.Motor.Brake = braking;
                }
            }, () => _rig.Tel.GroundContactWheels >= 2 && _t > 1f && _rig.Speed < 3f);
        }

        [UnityTest] public IEnumerator Corner_R60_Flat() => Corner("r60_flat", 60f, false, false);
        [UnityTest] public IEnumerator Corner_R40_Flat() => Corner("r40_flat", 40f, false, false);
        [UnityTest] public IEnumerator Corner_R25_Flat() => Corner("r25_flat", 25f, false, false);
        [UnityTest] public IEnumerator Corner_R40_Braked() => Corner("r40_braked", 40f, true, false);
        [UnityTest] public IEnumerator Corner_R40_SlideThenBrake() => Corner("r40_slide_brake", 40f, false, true);

        [UnityTest] public IEnumerator Ramp_AtSpeed()
        {
            _rig = new CarTestRig();
            _rig.BuildTrack(_rig.Kit.Straight(150f), _rig.Kit.Ramp(), _rig.Kit.Straight(150f));
            _rig.SpawnCar(5f, 0f);
            yield return Drive("ramp_speed", 20f);
        }

        [UnityTest] public IEnumerator RampVariants()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 8; i++)
            {
                foreach (float approach in new[] { 100f, 150f, 700f })
                {
                    _rig?.Dispose();
                    _rig = new CarTestRig(10f);
                    _rig.BuildTrack(_rig.Kit.Straight(approach), _rig.Kit.Ramp(), _rig.Kit.Straight(150f));
                    _rig.SpawnCar(5f + i * 0.37f, 0f);
                    float minUp = 1f, pmin = 0f, vTake = 0f, air0 = -1f, air1 = -1f;
                    string name = $"rampvar_{approach}_{i}";
                    yield return Drive(name, 40f, () =>
                    {
                        minUp = Mathf.Min(minUp, _rig.Tel.UpDot); pmin = Mathf.Min(pmin, _rig.Tel.PitchDeg);
                        if (_rig.Driver.Distance > approach && _rig.Tel.GroundedWheels == 0) { if (air0 < 0) { air0 = _rig.Driver.Distance; vTake = _rig.Speed; } air1 = _rig.Driver.Distance; }
                    }, () => _t > 3f && _rig.Speed < 1f);
                    sb.AppendLine($"{name}: v {vTake * 3.6f:F0} air {air0 - approach:F1}->{air1 - approach:F1} pmin {pmin:F0} minUp {minUp:F2} end d={_rig.Driver.Distance:F0}");
                }
            }
            File.WriteAllText(Path.Combine(Dir, "rampvariants.txt"), sb.ToString());
        }

        [UnityTest] public IEnumerator BumpVariants()
        {
            var sb = new StringBuilder();
            foreach (var (len, h) in new[] { (12f, 0.6f), (12f, 0.8f), (10f, 0.6f), (10f, 0.7f), (8f, 0.6f) })
            {
                foreach (float approach in new[] { 100f, 300f })
                {
                    _rig?.Dispose();
                    _rig = new CarTestRig(10f);
                    float L = len, H = h;
                    var mats = TrackPieceGeometry.DefaultMaterials(Color.gray);
                    var prefab = TrackPieceGeometry.CreatePiece("BumpVar", TrackPieceGeometry.ProfileKnots(L, s => s <= 0f || s >= L ? 0f : H * 0.5f * (1f - Mathf.Cos(2f * Mathf.PI * s / L))), 5, null, mats);
                    var bump = _rig.Kit.Track(ScriptableObject.CreateInstance<TrackPieceAsset>());
                    _rig.Kit.Track(prefab);
                    bump.kind = ApexBrake.Simulation.PieceKind.Bump; bump.prefab = prefab;
                    _rig.BuildTrack(_rig.Kit.Straight(approach), bump, _rig.Kit.Straight(150f));
                    _rig.SpawnCar(5f, 0f);
                    float air = 0f, maxAir = 0f, v = 0f, pmin = 0f, pmax = 0f, minUp = 1f;
                    string name = $"bumpvar_{len}_{h}_{approach}";
                    yield return Drive(name, 30f, () =>
                    {
                        if (_rig.Driver.Distance < approach) v = _rig.Speed;
                        air = _rig.Tel.GroundedWheels == 0 ? air + 0.02f : 0f; maxAir = Mathf.Max(maxAir, air);
                        if (_rig.Driver.Distance > approach - 5f) { pmin = Mathf.Min(pmin, _rig.Tel.PitchDeg); pmax = Mathf.Max(pmax, _rig.Tel.PitchDeg); minUp = Mathf.Min(minUp, _rig.Tel.UpDot); }
                    });
                    sb.AppendLine($"{name}: v {v * 3.6f:F0} maxAir {maxAir:F2}s pitch {pmin:F1}..{pmax:F1} minUp {minUp:F2} end d={_rig.Driver.Distance:F0}");
                }
            }
            File.WriteAllText(Path.Combine(Dir, "bumpvariants.txt"), sb.ToString());
        }

        [UnityTest] public IEnumerator Ramp_TopSpeed()
        {
            _rig = new CarTestRig();
            _rig.BuildTrack(_rig.Kit.Straight(700f), _rig.Kit.Ramp(), _rig.Kit.Straight(150f));
            _rig.SpawnCar(5f, 0f);
            yield return Drive("ramp_top", 40f);
        }

        [UnityTest] public IEnumerator Ramp_Crawl()
        {
            _rig = new CarTestRig();
            _rig.BuildTrack(_rig.Kit.Straight(60f), _rig.Kit.Ramp(), _rig.Kit.Straight(60f));
            _rig.SpawnCar(45f, 0f, 7f);
            yield return Drive("ramp_crawl", 8f, () => _rig.Motor.Brake = _rig.Speed > 7f);
        }

        [UnityTest] public IEnumerator Bump_AtSpeed()
        {
            _rig = new CarTestRig();
            _rig.BuildTrack(_rig.Kit.Straight(300f), _rig.Kit.Bump(), _rig.Kit.Straight(150f));
            _rig.SpawnCar(5f, 0f);
            yield return Drive("bump_speed", 30f);
        }
    }
}
