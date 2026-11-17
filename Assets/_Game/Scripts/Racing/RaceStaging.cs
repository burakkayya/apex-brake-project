using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ApexBrake.Racing
{
    /// <summary>
    /// Geliştirme/doğrulama kancası (oynanışa etkisi yok; boşta hiçbir şey yapmaz): Editor'den betikle oyuncu için fren planı
    /// (mesafe bölgeleri, "kayınca fren"), mesafede ya da olayda ekran görüntüsü ve yarış boyunca telemetri kaydı (CSV).
    /// Fren yalnızca TouchBrakeInput.ForceBrake üzerinden verilir (parmakla aynı yol).
    /// </summary>
    [DefaultExecutionOrder(-60)]
    public sealed class RaceStaging : MonoBehaviour
    {
        public RaceManager race;
        public TouchBrakeInput input;
        public ChaseCameraRig cameraRig;
        [Tooltip("Telemetri kaydı hangi yarışçı için (0 = oyuncu)")]
        public int recordRacer;
        public bool logEvents;

        readonly List<Vector2> _zones = new List<Vector2>();
        bool _brakeOnSlide;
        float _slideHeld; bool _slideBraking;
        readonly List<(string when, float arg, string path, float delay)> _shots = new List<(string, float, string, float)>();
        readonly List<(string path, float at)> _pending = new List<(string, float)>();
        StringBuilder _log;
        string _logPath;
        float _t;
        bool _wasOut;
        readonly List<string> _results = new List<string>();

        public bool Active => _zones.Count > 0 || _brakeOnSlide || _shots.Count > 0 || _log != null;
        public IReadOnlyList<string> Results => _results;

        void OnEnable() { if (race != null) { race.Ended += OnEnded; race.Restarted += OnRestarted; race.RacerContact += OnContact; race.RacerOut += OnOut; } }
        void OnDisable() { if (race != null) { race.Ended -= OnEnded; race.Restarted -= OnRestarted; race.RacerContact -= OnContact; race.RacerOut -= OnOut; } }

        void OnContact(int a, int b, float v)
        {
            if (logEvents) Debug.Log($"[RaceStaging] contact {race.Racers[a].Name}-{race.Racers[b].Name} v={v:0.0} d={race.Racers[a].Distance:0}");
        }

        void OnOut(int i, OutReason reason)
        {
            var e = race.Racers[i];
            if (logEvents) Debug.Log($"[RaceStaging] out {e.Name} {reason} d={e.Distance:0} lat={e.Lateral:0.0} t={race.ElapsedTime:0.0}");
        }

        /// <summary>Oyuncu fren bölgeleri [from_i, to_i] (pist mesafesi, m) ve "kayma başlayınca fren".</summary>
        public void Plan(float[] from, float[] to, bool brakeOnSlide)
        {
            _zones.Clear();
            for (int i = 0; i < from.Length; i++) _zones.Add(new Vector2(from[i], to[i]));
            _brakeOnSlide = brakeOnSlide;
            _slideBraking = false;
        }

        public void ClearPlan() { _zones.Clear(); _brakeOnSlide = false; if (input != null) input.ForceBrake = false; }

        /// <summary>when: "distance" (arg = m), "out", "ended", "air", "slide" (arg = en az kayma derecesi), "brake" (arg = fren süresi sn), "time" (arg = yarış süresi sn), "overtake", "beside" (arg = m).</summary>
        public void ShotWhen(string when, float arg, string path, float delay = 0f) => _shots.Add((when, arg, path, delay));

        public void Record(string csvPath)
        {
            _logPath = csvPath;
            _t = 0f;
            _log = new StringBuilder("t,state,d,lat,kmh,slip,air,grounded,brake,rank,cam_x,cam_y,cam_z,fov,dutch,pitch,roll\n");
        }

        void OnRestarted() { _t = 0f; _wasOut = false; _brakeHeld = 0f; }

        void OnEnded()
        {
            var inv = CultureInfo.InvariantCulture;
            string line = $"seed {race.CurrentSeed} player {race.ElapsedTime.ToString("0.00", inv)}s: " + string.Join(", ", race.Racers.OrderBy(e => e.Rank).Select(e =>
                $"{e.Rank}.{e.Name} " + (e.Finished ? e.FinishTime.ToString("0.00", inv) + "s" : e.IsOut ? $"{RaceResultPanel.Status(e)} {e.Distance:0}m" : $"{e.Distance:0}m")));
            _results.Add(line);
            Debug.Log("[RaceStaging] ended " + line);
            Flush();
        }

        float _brakeHeld;

        void FixedUpdate()
        {
            if (race == null || race.Racers.Count == 0) return;
            var p = race.Player;
            float d = p.Distance;
            if (_zones.Count > 0 || _brakeOnSlide)
            {
                bool brake = false;
                foreach (var z in _zones) if (d >= z.x && d <= z.y) brake = true;
                if (_brakeOnSlide && p.Telemetry != null)
                {
                    if (!_slideBraking && p.Telemetry.IsSliding) { _slideBraking = true; _slideHeld = 0f; }
                    if (_slideBraking) { _slideHeld += Time.fixedDeltaTime; if (_slideHeld > 0.4f && Mathf.Abs(p.Telemetry.SlipAngleDeg) < 4f) _slideBraking = false; }
                    brake |= _slideBraking;
                }
                if (input != null) input.ForceBrake = brake;
            }
            _brakeHeld = p.Motor.Brake && race.State == RaceState.Racing ? _brakeHeld + Time.fixedDeltaTime : 0f;

            if (_log != null && race.State != RaceState.Countdown)
            {
                var rr = race.Racers[Mathf.Clamp(recordRacer, 0, race.Racers.Count - 1)];
                var tel = rr.Telemetry;
                var inv = CultureInfo.InvariantCulture;
                d = rr.Distance;
                Vector3 off = cameraRig != null ? cameraRig.CurrentOffset : Vector3.zero;
                _log.AppendLine(string.Format(inv, "{0:F2},{1},{2:F1},{3:F2},{4:F1},{5:F1},{6},{7},{8},{9},{10:F2},{11:F2},{12:F2},{13:F1},{14:F2},{15:F1},{16:F1}",
                    _t, race.State, d, rr.Lateral, tel.SpeedKmh, tel.SlipAngleDeg, tel.IsAirborne ? 1 : 0, tel.GroundedWheels, rr.Motor.Brake ? 1 : 0, rr.Rank,
                    off.x, off.y, off.z, cameraRig != null ? cameraRig.CurrentFov : 0f, cameraRig != null ? cameraRig.CurrentDutch : 0f, tel.PitchDeg, tel.RollDeg));
                _t += Time.fixedDeltaTime;
                if (Mathf.Repeat(_t, 1f) < Time.fixedDeltaTime) Flush();
            }
        }

        void Update()
        {
            if (race == null || race.Racers.Count == 0) return;
            var p = race.Player;
            var tel = p.Telemetry;
            bool outNow = p.IsOut && !_wasOut; _wasOut = p.IsOut;
            for (int i = _shots.Count - 1; i >= 0; i--)
            {
                var s = _shots[i];
                bool fire = false;
                switch (s.when)
                {
                    case "distance": fire = race.State == RaceState.Racing && p.Distance >= s.arg; break;
                    case "out": fire = p.IsOut; break;
                    case "ended": fire = race.State == RaceState.Ended; break;
                    case "air": fire = tel.IsAirborne && !p.IsOut && p.Distance >= s.arg; break;
                    case "slide": fire = tel.IsSliding && Mathf.Abs(tel.SlipAngleDeg) >= s.arg && !p.IsOut; break;
                    case "brake": fire = _brakeHeld >= s.arg && !p.IsOut; break;
                    case "time": fire = race.State == RaceState.Countdown ? s.arg < 0f && race.CountdownRemaining <= -s.arg : race.ElapsedTime >= s.arg; break;
                    case "overtake": fire = p.IsOvertaking; break;
                    case "beside":
                        fire = race.State == RaceState.Racing && race.Racers.Any(o => o != p && !o.IsOut && Mathf.Abs(o.Distance - p.Distance) < s.arg && Mathf.Abs(o.Lateral - p.Lateral) < 4.5f);
                        break;
                }
                if (!fire) continue;
                _pending.Add((s.path, Time.time + s.delay));
                _shots.RemoveAt(i);
            }
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (Time.time < _pending[i].at) continue;
                ScreenCapture.CaptureScreenshot(_pending[i].path);
                string status = string.Format(CultureInfo.InvariantCulture, "d={0:F1} lat={1:F2} v={2:F0}kmh slip={3:F1} air={4} brake={5} pitch={6:F1} roll={7:F1} rank={8} out={9} state={10} cam={11} fov={12:F1} dutch={13:F1}",
                    p.Distance, p.Lateral, tel.SpeedKmh, tel.SlipAngleDeg, tel.IsAirborne, p.Motor.Brake, tel.PitchDeg, tel.RollDeg, p.Rank, p.IsOut ? p.OutReason.ToString() : "-", race.State,
                    cameraRig != null ? cameraRig.CurrentOffset.ToString("F2") : "-", cameraRig != null ? cameraRig.CurrentFov : 0f, cameraRig != null ? cameraRig.CurrentDutch : 0f);
                Debug.Log("[RaceStaging] shot " + _pending[i].path + " " + status);
                try { System.IO.File.WriteAllText(_pending[i].path + ".txt", status + "\n"); } catch { }
                _pending.RemoveAt(i);
            }
        }

        public void Flush()
        {
            if (_log == null || string.IsNullOrEmpty(_logPath)) return;
            try { System.IO.File.WriteAllText(_logPath, _log.ToString()); } catch { }
        }
    }
}
