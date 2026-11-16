#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using ApexBrake.Simulation;

namespace ApexBrake.Racing
{
    /// <summary>
    /// Yalnızca Editor: oynatma sırasında betikle eklenen doğrulama sondası. Oyuncu ve kamera değerlerini kare kare
    /// kaydeder, belirli anlarda (olaydan sonra, hız koşulunda, sollama anında) ekran görüntüsü alır. Sahnede yoktur;
    /// oynanışa etkisi yoktur.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class RaceDebugProbe : MonoBehaviour
    {
        public RaceController controller;
        public ChaseCameraRig rig;
        public RacerView playerView;
        public string logPath;                        // boş değilse kayıt WriteLog ile buraya yazılır

        readonly StringBuilder _log = new StringBuilder();
        readonly List<string> _taken = new List<string>();
        readonly List<(SimEventKind kind, float delay, string path)> _eventCaps = new List<(SimEventKind, float, string)>();
        readonly List<(float at, string path)> _pending = new List<(float, string)>();
        string _slowPath, _fastPath, _overtakePath;
        float _slowSpeed, _fastSpeed, _speedTol;
        int _logFrames;
        RaceController _subscribed;

        public IReadOnlyList<string> Taken => _taken;
        public int LoggedFrames => _logFrames;

        public static RaceDebugProbe Attach(string logPath)
        {
            var c = Object.FindAnyObjectByType<RaceController>();
            var go = new GameObject("RaceDebugProbe");
            var p = go.AddComponent<RaceDebugProbe>();
            p.controller = c;
            p.rig = Object.FindAnyObjectByType<ChaseCameraRig>();
            foreach (var v in Object.FindObjectsByType<RacerView>(FindObjectsSortMode.None))
                if (v.racerIndex == RaceController.PlayerIndex) p.playerView = v;
            p.logPath = logPath;
            p._log.AppendLine("time,tick,d,speed,lat,latVel,target,slipDeg,phase,sliding,braking,bodyYaw,roll,pitch,offX,offY,offZ,fov,dutch,camX,camY,camZ,crashView,overtaking");
            return p;
        }

        public void CaptureAfterEvent(SimEventKind kind, float delay, string path) => _eventCaps.Add((kind, delay, path));
        public void CaptureAtSpeed(float slow, string slowPath, float fast, string fastPath, float tol)
        {
            _slowSpeed = slow; _slowPath = slowPath; _fastSpeed = fast; _fastPath = fastPath; _speedTol = tol;
        }
        public void CaptureOvertake(string path) => _overtakePath = path;

        void Update()
        {
            if (controller != null && _subscribed != controller)
            {
                controller.RacerEvent += OnEvent;
                _subscribed = controller;
            }
        }

        void OnDestroy()
        {
            if (_subscribed != null) _subscribed.RacerEvent -= OnEvent;
        }

        void OnEvent(int index, SimEvent e)
        {
            if (index != RaceController.PlayerIndex) return;
            _log.AppendLine("#event," + Time.time.ToString("0.000", CultureInfo.InvariantCulture) + "," + e.Kind + "," + e.PieceIndex);
            for (int i = _eventCaps.Count - 1; i >= 0; i--)
                if (_eventCaps[i].kind == e.Kind)
                {
                    _pending.Add((Time.time + _eventCaps[i].delay, _eventCaps[i].path));
                    _eventCaps.RemoveAt(i);
                }
        }

        void Shot(string path)
        {
            ScreenCapture.CaptureScreenshot(path);
            _taken.Add(path + " @" + Time.time.ToString("0.00", CultureInfo.InvariantCulture));
        }

        void LateUpdate()
        {
            var c = controller;
            if (c == null || c.Race == null) return;
            var racer = c.Race.Racers[RaceController.PlayerIndex];
            var car = racer.Car;

            for (int i = _pending.Count - 1; i >= 0; i--)
                if (Time.time >= _pending[i].at) { Shot(_pending[i].path); _pending.RemoveAt(i); }

            if (c.State == RaceState.Racing && car.Phase == CarPhase.Driving)
            {
                var piece = c.track.Layout.SpanAtDistance(car.Distance).Def;
                bool straight = piece.Kind == PieceKind.Straight && piece.LaneCount == 5;
                if (_slowPath != null && straight && Mathf.Abs(car.Speed - _slowSpeed) < _speedTol && Mathf.Abs(rig.CurrentOffset.z - Expected(_slowSpeed, rig)) < 0.15f)
                { Shot(_slowPath); _slowPath = null; }
                if (_fastPath != null && straight && Mathf.Abs(car.Speed - _fastSpeed) < _speedTol && Mathf.Abs(rig.CurrentOffset.z - Expected(_fastSpeed, rig)) < 0.15f)
                { Shot(_fastPath); _fastPath = null; }
            }
            if (_overtakePath != null && c.State == RaceState.Racing)
            {
                var rs = c.Race.Racers;
                for (int i = 0; i < rs.Count && _overtakePath != null; i++)
                {
                    if (!rs[i].IsOvertaking || rs[i].Car.IsOut) continue;
                    for (int j = 0; j < rs.Count; j++)
                    {
                        if (j == i || rs[j].Car.IsOut) continue;
                        float dd = rs[i].Car.Distance - rs[j].Car.Distance;
                        float dl = Mathf.Abs(rs[i].Lateral - rs[j].Lateral);
                        float ahead = rs[i].Car.Distance - car.Distance;
                        if (Mathf.Abs(dd) < 2.5f && dl > 1.8f && dl < 4f && rs[i].Car.Speed > rs[j].Car.Speed + 1f && ahead > -2f && ahead < 30f)
                        {
                            Shot(_overtakePath);
                            _log.AppendLine("#overtake," + i + " passes " + j + ",dd=" + dd.ToString("0.00", CultureInfo.InvariantCulture) + ",dl=" + dl.ToString("0.00", CultureInfo.InvariantCulture));
                            _overtakePath = null;
                            break;
                        }
                    }
                }
            }

            if (rig == null) return;
            var inv = CultureInfo.InvariantCulture;
            var o = rig.CurrentOffset;
            var cp = rig.CameraPosition;
            bool anyOvertake = false;
            foreach (var r in c.Race.Racers) anyOvertake |= r.IsOvertaking;
            _log.Append(Time.time.ToString("0.000", inv)).Append(',').Append(c.Race.TickCount).Append(',')
                .Append(car.Distance.ToString("0.00", inv)).Append(',').Append(car.Speed.ToString("0.00", inv)).Append(',')
                .Append(car.Lateral.ToString("0.000", inv)).Append(',').Append(car.LateralVel.ToString("0.000", inv)).Append(',')
                .Append(car.TargetLateral.ToString("0.00", inv)).Append(',')
                .Append((car.SlipAngle * Mathf.Rad2Deg).ToString("0.00", inv)).Append(',').Append(car.Phase).Append(',')
                .Append(car.IsSliding ? 1 : 0).Append(',').Append(racer.IsBraking ? 1 : 0).Append(',')
                .Append(playerView != null ? playerView.BodyYaw.ToString("0.00", inv) : "").Append(',')
                .Append(playerView != null ? playerView.BodyRoll.ToString("0.00", inv) : "").Append(',')
                .Append(playerView != null ? playerView.BodyPitch.ToString("0.00", inv) : "").Append(',')
                .Append(o.x.ToString("0.000", inv)).Append(',').Append(o.y.ToString("0.000", inv)).Append(',').Append(o.z.ToString("0.000", inv)).Append(',')
                .Append(rig.CurrentFov.ToString("0.00", inv)).Append(',').Append(rig.CurrentDutch.ToString("0.00", inv)).Append(',')
                .Append(cp.x.ToString("0.000", inv)).Append(',').Append(cp.y.ToString("0.000", inv)).Append(',').Append(cp.z.ToString("0.000", inv)).Append(',')
                .Append(rig.IsCrashView ? 1 : 0).Append(',').Append(anyOvertake ? 1 : 0).AppendLine();
            _logFrames++;
        }

        static float Expected(float speed, ChaseCameraRig rig) =>
            Mathf.Lerp(rig.slowOffset.z, rig.fastOffset.z, Mathf.Clamp01(speed / rig.fastSpeed));

        public void WriteLog()
        {
            if (string.IsNullOrEmpty(logPath)) return;
            System.IO.File.WriteAllText(logPath, _log.ToString());
        }
    }
}
#endif
