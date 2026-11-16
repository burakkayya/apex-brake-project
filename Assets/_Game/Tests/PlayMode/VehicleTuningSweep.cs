using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ApexBrake.Racing;
using ApexBrake.Track;

namespace ApexBrake.Tests.PlayMode
{
    /// <summary>
    /// Geliştirme aracı (Explicit): Logs/faz2c-sweep.txt içindeki her satır bir aday yapılandırmadır
    /// ("ad alan=değer alan=değer ..."; boş satır = varsayılan). Her aday için tüm his senaryolarını sürer ve
    /// tek satırlık özeti Logs/faz2c-sweep-out.txt dosyasına yazar. Hiçbir şey doğrulamaz.
    /// </summary>
    [Explicit]
    public sealed class VehicleTuningSweep
    {
        CarTestRig _rig;
        [TearDown] public void TearDown() { _rig?.Dispose(); _rig = null; }

        static string LogDir => Path.Combine(Application.dataPath, "../Logs");

        VehicleConfig Make(string spec)
        {
            var c = ScriptableObject.CreateInstance<VehicleConfig>();
            foreach (var kv in spec.Split(' '))
            {
                int eq = kv.IndexOf('=');
                if (eq < 0) continue;
                var f = typeof(VehicleConfig).GetField(kv.Substring(0, eq), BindingFlags.Public | BindingFlags.Instance);
                if (f == null) { Debug.LogError("unknown field " + kv); continue; }
                string v = kv.Substring(eq + 1);
                if (f.FieldType == typeof(float)) f.SetValue(c, float.Parse(v, CultureInfo.InvariantCulture));
                else if (f.FieldType == typeof(bool)) f.SetValue(c, bool.Parse(v));
                else if (f.FieldType.IsEnum) f.SetValue(c, System.Enum.Parse(f.FieldType, v));
            }
            return c;
        }

        CarTestRig NewRig(VehicleConfig cfg, params TrackPieceAsset[] dummy)
        {
            _rig?.Dispose();
            _rig = new CarTestRig(10f);
            return _rig;
        }

        static string F(float v) => v.ToString("F1", CultureInfo.InvariantCulture);

        [UnityTest]
        public IEnumerator Sweep()
        {
            var specs = new List<string>(File.Exists(Path.Combine(LogDir, "faz2c-sweep.txt"))
                ? File.ReadAllLines(Path.Combine(LogDir, "faz2c-sweep.txt")) : new[] { "default" });
            var outLines = new StringBuilder();
            foreach (var line in specs)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                string name = line.Split(' ')[0];
                var sb = new StringBuilder(name + ": ");

                // 1) kalkış, tavan, fren
                {
                    var r = NewRig(null);
                    r.BuildTrack(r.Kit.Straight(1500f));
                    r.SpawnCar(5f, 0f, 0f, Make(line));
                    float t = 0f, t100 = -1f, squat = 0f;
                    yield return r.Run(50f, () => r.Driver.Distance > 900f, () =>
                    {
                        t += 0.02f;
                        if (t100 < 0f && r.Speed >= 27.78f) t100 = t;
                        if (t < 2f) squat = Mathf.Max(squat, r.Tel.PitchDeg);
                    });
                    float top = r.Speed, d0 = r.Driver.Distance, tb = 0f, dive = 0f, maxDecel = 0f;
                    r.Motor.Brake = true;
                    yield return r.Run(8f, () => r.Speed < 0.3f, () =>
                    {
                        tb += 0.02f; dive = Mathf.Min(dive, r.Tel.PitchDeg);
                        maxDecel = Mathf.Max(maxDecel, -r.Tel.LongitudinalAccel);
                    });
                    sb.Append($"0-100 {t100:F2}s top {F(top * 3.6f)} squat {F(squat)} | brake {tb:F2}s {F(r.Driver.Distance - d0)}m dive {F(dive)} decel {F(maxDecel)} | ");
                }

                // 2) virajlar
                foreach (var (rad, mode) in new[] { (60f, 0), (40f, 0), (25f, 0), (40f, 1), (40f, 2) })
                {
                    var r = NewRig(null);
                    r.BuildTrack(r.Kit.Straight(400f), r.Kit.Corner(rad, true), r.Kit.Straight(250f));
                    r.SpawnCar(5f, 0f, 0f, Make(line));
                    float cs = r.Track.Layout.SpanAt(1).Start, ce = r.Track.Layout.SpanAt(1).End;
                    float bt = 0f, held = 0f, maxOut = 0f, maxSlip = 0f, maxRoll = 0f, maxLat = 0f, after = -1f, devAfter = 99f;
                    bool braking = false, off = false;
                    yield return r.Run(30f, () => off || r.Driver.Distance > ce + 230f, () =>
                    {
                        float d = r.Driver.Distance;
                        if (mode == 1) { if (d >= cs - 40f && bt < 1f) { r.Motor.Brake = true; bt += 0.02f; } else r.Motor.Brake = false; }
                        if (mode == 2)
                        {
                            if (!braking && d > cs && r.Tel.IsSliding) { braking = true; held = 0f; }
                            if (braking) { held += 0.02f; if (held > 0.4f && Mathf.Abs(r.Tel.SlipAngleDeg) < 3f) braking = false; }
                            r.Motor.Brake = braking;
                        }
                        if (d >= cs - 5f && d <= ce + 30f)
                        {
                            maxOut = Mathf.Max(maxOut, -r.Driver.Lateral);
                            maxSlip = Mathf.Max(maxSlip, Mathf.Abs(r.Tel.SlipAngleDeg));
                            maxRoll = Mathf.Max(maxRoll, Mathf.Abs(r.Tel.RollDeg));
                            maxLat = Mathf.Max(maxLat, Mathf.Abs(r.Tel.LateralAccel));
                        }
                        if (d >= ce) { after = after < 0f ? 0f : after + 0.02f; if (after >= 4f && devAfter > 98f) devAfter = Mathf.Abs(r.Driver.Lateral); }
                        if (r.Tel.GroundContactWheels > 0 || Mathf.Abs(r.Driver.Lateral) > r.Track.Path.HalfWidthAt(d) + 2.5f) off = true;
                    });
                    string tag = mode == 0 ? $"R{rad}" : mode == 1 ? "R40brk" : "R40slide";
                    sb.Append($"{tag} out {F(maxOut)} slip {F(maxSlip)} roll {F(maxRoll)} alat {F(maxLat)} {(off ? "OFF" : "ok")}{(mode == 2 ? " dev4s " + F(devAfter) : "")} | ");
                }

                // 3) rampa ve tümsek
                foreach (var kind in new[] { 0, 1, 2 })
                {
                    var r = NewRig(null);
                    if (kind == 2) r.BuildTrack(r.Kit.Straight(300f), r.Kit.Bump(), r.Kit.Straight(150f));
                    else r.BuildTrack(r.Kit.Straight(kind == 0 ? 150f : 700f), r.Kit.Ramp(), r.Kit.Straight(150f));
                    r.SpawnCar(5f, 0f, 0f, Make(line));
                    float ps = r.Track.Layout.SpanAt(1).Start, air0 = -1f, air1 = -1f, vTake = 0f, pmin = 0f, pmax = 0f, minUp = 1f, t = 0f;
                    bool ground = false;
                    yield return r.Run(40f, () => r.Driver.Distance > r.Track.Path.TotalLength - 10f || (t > 3f && r.Speed < 1f), () =>
                    {
                        t += 0.02f;
                        float d = r.Driver.Distance;
                        if (d > ps - 5f)
                        {
                            if (r.Tel.GroundedWheels == 0) { if (air0 < 0f) { air0 = d; vTake = r.Speed; } air1 = d; }
                            pmin = Mathf.Min(pmin, r.Tel.PitchDeg); pmax = Mathf.Max(pmax, r.Tel.PitchDeg); minUp = Mathf.Min(minUp, r.Tel.UpDot);
                        }
                        ground |= r.Tel.GroundContactWheels > 0;
                    });
                    string tag = kind == 0 ? "ramp150" : kind == 1 ? "ramp700" : "bump";
                    sb.Append($"{tag} v {F(vTake * 3.6f)} air {F(air1 - air0)}m pitch {F(pmin)}..{F(pmax)} minUp {minUp:F2} {(ground ? "GROUND" : "ok")} end {(r.Driver.Distance > r.Track.Path.TotalLength - 10f ? "fin" : "stop")} | ");
                }
                outLines.AppendLine(sb.ToString());
                File.WriteAllText(Path.Combine(LogDir, "faz2c-sweep-out.txt"), outLines.ToString());
            }
            yield return null;
        }
    }
}
