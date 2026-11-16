using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using ApexBrake.Track;

namespace ApexBrake.Racing
{
    /// <summary>
    /// Geliştirme sahnesi (Dev_PhysicsCar): pisti kurar, aracı başa koyar, fren girdisini (dokunma/fare/Space)
    /// araca verir ve basit bir takip kamerası sürer. Betikle deneme için fren bölgeleri, mesafede ekran görüntüsü
    /// ve telemetri kaydı vardır. Yarış kuralı içermez (RaceManager değildir).
    /// </summary>
    public sealed class DevDriveSession : MonoBehaviour
    {
        public TrackBuilder track;
        public VehicleMotor car;
        public TouchBrakeInput input;
        public Camera cam;
        public float startDistance = 5f;
        public float startLateral = 0f;
        public Vector3 cameraOffset = new Vector3(0f, 3.2f, -8f);

        LineDriver _driver;
        VehicleTelemetry _tel;
        readonly List<Vector2> _brakeZones = new List<Vector2>();
        readonly List<(float d, string path, float delay)> _shots = new List<(float, string, float)>();
        readonly List<(string path, float at)> _pendingShots = new List<(string, float)>();
        StringBuilder _log;
        string _logPath;
        float _t;
        bool _brakeOnSlide;
        float _slideBrakeHeld;
        bool _slideBraking;

        public float Distance => _driver != null ? _driver.Distance : 0f;
        public string Status => _driver == null ? "-" : string.Format(CultureInfo.InvariantCulture,
            "d={0:F1} lat={1:F2} v={2:F1}kmh slip={3:F1} grounded={4} ground={5} pitch={6:F1} roll={7:F1} up={8:F2} brake={9}",
            _driver.Distance, _driver.Lateral, _tel.SpeedKmh, _tel.SlipAngleDeg, _tel.GroundedWheels, _tel.TouchesGround, _tel.PitchDeg, _tel.RollDeg, _tel.UpDot, car.Brake);

        void Start()
        {
            Application.targetFrameRate = 60;
            track.Build();
            _driver = car.GetComponent<LineDriver>();
            _tel = car.GetComponent<VehicleTelemetry>();
            ResetCar();
        }

        public void ResetCar() => PlaceAt(startDistance, startLateral);

        public void PlaceAt(float distance, float lateral)
        {
            var path = track.Path;
            var f = path.ForwardAt(distance); f.y = 0f;
            car.PlaceAt(path.PointAt(distance, lateral), Quaternion.LookRotation(f.normalized, Vector3.up));
            _driver.Path = path;
            _driver.TargetLateral = lateral;
            _driver.ResetProjection();
            _tel.ResetState();
            car.EngineOn = true;
            if (cam != null)
            {
                cam.transform.position = car.transform.position + car.transform.rotation * cameraOffset;
                cam.transform.LookAt(car.transform.position);
            }
        }

        /// <summary>Betikli koşu: başlangıç, fren bölgeleri [from,to], "kayınca fren" ve mesafede ekran görüntüleri; telemetri CSV'ye.</summary>
        public void Script(float start, float[] brakeFrom, float[] brakeTo, bool brakeOnSlide, string logPath)
        {
            _brakeZones.Clear(); _shots.Clear(); _pendingShots.Clear();
            for (int i = 0; i < brakeFrom.Length; i++) _brakeZones.Add(new Vector2(brakeFrom[i], brakeTo[i]));
            _brakeOnSlide = brakeOnSlide; _slideBraking = false;
            _logPath = logPath;
            _log = new StringBuilder("t,d,lat,speed_kmh,slip_deg,grounded,touch_ground,pitch_deg,roll_deg,updot,steer_deg,brake,alat,along\n");
            _t = 0f;
            PlaceAt(start, 0f);
        }

        public void ShotAt(float distance, string path, float delay = 0f) => _shots.Add((distance, path, delay));

        void FixedUpdate()
        {
            bool brake = input != null && input.IsBraking;
            if (_log != null)
            {
                float d = _driver.Distance;
                foreach (var z in _brakeZones) if (d >= z.x && d <= z.y) brake = true;
                if (_brakeOnSlide)
                {
                    if (!_slideBraking && _tel.IsSliding) { _slideBraking = true; _slideBrakeHeld = 0f; }
                    if (_slideBraking) { _slideBrakeHeld += Time.fixedDeltaTime; if (_slideBrakeHeld > 0.4f && Mathf.Abs(_tel.SlipAngleDeg) < 3f) _slideBraking = false; }
                    brake |= _slideBraking;
                }
                _log.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:F2},{1:F2},{2:F2},{3:F1},{4:F1},{5},{6},{7:F1},{8:F1},{9:F2},{10:F1},{11},{12:F1},{13:F1}",
                    _t, d, _driver.Lateral, _tel.SpeedKmh, _tel.SlipAngleDeg, _tel.GroundedWheels, _tel.TouchesGround ? 1 : 0, _tel.PitchDeg, _tel.RollDeg, _tel.UpDot,
                    car.SteerAngle, car.Brake ? 1 : 0, _tel.LateralAccel, _tel.LongitudinalAccel));
                _t += Time.fixedDeltaTime;
                if (_t % 1f < Time.fixedDeltaTime) FlushLog();
            }
            car.Brake = brake;
        }

        public void FlushLog()
        {
            if (_log == null || string.IsNullOrEmpty(_logPath)) return;
            try { System.IO.File.WriteAllText(_logPath, _log.ToString()); } catch { }
        }

        void Update()
        {
            for (int i = _shots.Count - 1; i >= 0; i--)
            {
                if (_driver.Distance >= _shots[i].d)
                {
                    _pendingShots.Add((_shots[i].path, Time.time + _shots[i].delay));
                    _shots.RemoveAt(i);
                }
            }
            for (int i = _pendingShots.Count - 1; i >= 0; i--)
            {
                if (Time.time >= _pendingShots[i].at)
                {
                    ScreenCapture.CaptureScreenshot(_pendingShots[i].path);
                    Debug.Log("[DevDriveSession] shot " + _pendingShots[i].path + " " + Status);
                    try { System.IO.File.AppendAllText(_pendingShots[i].path + ".txt", Status + "\n"); } catch { }
                    _pendingShots.RemoveAt(i);
                }
            }
        }

        void LateUpdate()
        {
            if (cam == null || car == null) return;
            var t = car.transform;
            var v = car.Body.linearVelocity; v.y = 0f;
            var flat = v.sqrMagnitude > 4f ? v : t.forward; flat.y = 0f;
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            var rot = Quaternion.LookRotation(flat.normalized, Vector3.up);
            var target = t.position + rot * cameraOffset;
            cam.transform.position = Vector3.Lerp(cam.transform.position, target, 1f - Mathf.Exp(-6f * Time.deltaTime));
            float lead = Mathf.Abs(cameraOffset.x) > 1f ? 0f : 4f;   // yandan bakışta aracın kendisine
            cam.transform.rotation = Quaternion.LookRotation(t.position + Vector3.up * 0.7f + flat.normalized * lead - cam.transform.position, Vector3.up);
        }
    }
}
