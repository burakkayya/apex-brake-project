using UnityEngine;
using Unity.Cinemachine;
using ApexBrake.Simulation;

namespace ApexBrake.Racing
{
    /// <summary>
    /// Bir yarışçıyı çizer: pist çizgisi + yanal konum, rampa/tümsek yükseklik profili, uçuş, savrulma,
    /// çakılma (kendi sayacıyla), temas sarsıntısı ve oyuncu kamerasında boost FOV'u.
    /// CinemachineBrain'den önce, RaceController'dan sonra çalışır.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class RacerView : MonoBehaviour
    {
        public RaceController controller;
        public int racerIndex;
        public Transform body;
        public CinemachineCamera vcam;   // yalnızca oyuncuda dolu

        public float baseHeight = 0.5f;
        public float fovMin = 60f;
        public float fovMax = 68f;
        public float boostFov = 6f;

        // Gri kutu parçalarının ölçüleri (GrayboxPieceFactory ile aynı)
        const float RampWedgeLength = 4f;   // kalkıştan önceki eğik bölüm
        const float RampHeight = 2f;
        const float RampLip = 0.2f;         // kama levhasının kalınlığı: tekerlek yüzeyin üstünde kalsın
        const float BumpHeight = 1f;

        const float ShakeDuration = 0.25f;
        const float CrashDuration = 1.2f;

        float _crashStart = -1f;
        float _crashSide = 1f;
        float _crashHeight;
        float _shakeUntil = -1f;
        float _boostFovNow;
        RaceController _subscribed;

        void OnEnable() => Subscribe();
        void Start() => Subscribe();

        void OnDisable()
        {
            if (_subscribed != null)
            {
                _subscribed.RacerEvent -= OnRacerEvent;
                _subscribed.Restarted -= OnRestarted;
                _subscribed = null;
            }
        }

        void Subscribe()
        {
            if (_subscribed == controller || controller == null) return;
            OnDisable();
            controller.RacerEvent += OnRacerEvent;
            controller.Restarted += OnRestarted;
            _subscribed = controller;
        }

        void OnRestarted()
        {
            _crashStart = -1f;
            _shakeUntil = -1f;
            _boostFovNow = 0f;
            // Kamera eski yarışın son konumundan start çizgisine sönümlü kaymasın: araç konumlanınca arkasına yerleştirilir.
            _snapCamera = vcam != null;
        }

        bool _snapCamera;

        void SnapCamera(Quaternion yaw)
        {
            _snapCamera = false;
            var follow = vcam.GetComponent<CinemachineFollow>();
            Vector3 offset = follow != null ? follow.FollowOffset : new Vector3(0f, 8f, -15f);
            Vector3 camPos = transform.position + yaw * offset;
            var look = transform.position + Vector3.up * 2.6f - camPos;
            vcam.ForceCameraPosition(camPos, Quaternion.LookRotation(look, Vector3.up));
            vcam.PreviousStateIsValid = false;
        }

        void OnRacerEvent(int index, SimEvent e)
        {
            if (index != racerIndex) return;
            switch (e.Kind)
            {
                case SimEventKind.RearHit:
                case SimEventKind.Bumped:
                case SimEventKind.SideHit:
                    _shakeUntil = Time.time + ShakeDuration;
                    break;
                case SimEventKind.Crash:
                case SimEventKind.FellShort:
                    BeginCrash(e.PieceIndex);
                    break;
                case SimEventKind.Respawned:
                    _crashStart = -1f;
                    break;
            }
        }

        void BeginCrash(int pieceIndex)
        {
            var c = controller;
            var racer = c.Race.Racers[racerIndex];
            _crashStart = Time.time;
            var layout = c.track.Layout;
            int turn = pieceIndex >= 0 && pieceIndex < layout.PieceCount ? layout.SpanAt(pieceIndex).Def.TurnSign : 0;
            // Virajın dışına: sağ viraj (+1) → sola (-1). Viraj değilse yakın kenara doğru.
            _crashSide = turn != 0 ? -turn : (racer.Lateral < 0f ? -1f : 1f);
            _crashHeight = HeightAt(racer.Car.Distance, racer.Car, out _);
        }

        void LateUpdate()
        {
            var c = controller;
            if (c == null || c.Race == null || c.track == null || c.track.Path == null) return;
            if (racerIndex < 0 || racerIndex >= c.Race.Racers.Count) return;
            Subscribe();
            var racer = c.Race.Racers[racerIndex];
            var car = racer.Car;

            float alpha = c.State == RaceState.Racing ? c.Clock.Alpha : 1f;
            float d = Mathf.Lerp(c.PrevDistance(racerIndex), car.Distance, alpha);
            float lat = Mathf.Lerp(c.PrevLateral(racerIndex), racer.Lateral, alpha);

            c.track.Path.Evaluate(d, out Vector3 pos, out Quaternion rot);
            var yaw = Quaternion.Euler(0f, rot.eulerAngles.y, 0f);
            Vector3 right = yaw * Vector3.right;

            bool crashed = car.Phase == CarPhase.Crashed;
            if (crashed && _crashStart < 0f) BeginCrash(c.track.Layout.SpanAtDistance(car.Distance).Index); // olay kaçtıysa
            float pitch = 0f;
            float h = crashed ? _crashHeight : HeightAt(d, car, out pitch);

            transform.SetPositionAndRotation(pos + right * lat + Vector3.up * (baseHeight + h),
                yaw * Quaternion.Euler(-pitch, 0f, 0f));

            Vector3 localPos = Vector3.zero;
            Quaternion localRot = Quaternion.identity;

            if (crashed)
            {
                // Kendi sayacı: CarSim.PhaseTime elenen araçta ilerlemez.
                float t = Mathf.Clamp(Time.time - _crashStart, 0f, CrashDuration);
                float k = t / CrashDuration;
                float ease = 1f - (1f - k) * (1f - k);
                float arc = 6f * t - 5f * t * t;          // t = 1.2'de 0
                localPos = new Vector3(_crashSide * 14f * ease, Mathf.Max(arc, 0f) - _crashHeight * k, 16f * ease);
                localRot = Quaternion.Euler(ease * 720f, 0f, -_crashSide * ease * 360f);
            }
            else if (car.Phase == CarPhase.Skidding)
            {
                float phaseTime = car.PhaseTime + alpha * CarSim.Step;
                float yawWobble = 25f * Mathf.Sin(phaseTime / Mathf.Max(c.Tuning.SkidDuration, 0.01f) * Mathf.PI * 4f);
                localRot = Quaternion.Euler(0f, yawWobble, 0f);
            }

            if (!crashed && Time.time < _shakeUntil)
            {
                float s = (_shakeUntil - Time.time) / ShakeDuration;
                float w = Time.time * 60f;
                localPos += new Vector3(Mathf.Sin(w) * 0.12f, Mathf.Abs(Mathf.Sin(w * 1.3f)) * 0.08f, 0f) * s;
                localRot *= Quaternion.Euler(0f, 0f, Mathf.Sin(w * 0.9f) * 4f * s);
            }

            if (body != null)
            {
                body.localPosition = localPos;
                body.localRotation = localRot;
            }

            if (vcam != null && _snapCamera) SnapCamera(yaw);

            if (vcam != null)
            {
                float target = car.IsBoosting ? boostFov : 0f;
                _boostFovNow = Mathf.MoveTowards(_boostFovNow, target, boostFov * 4f * Time.unscaledDeltaTime);
                float sp = Mathf.Clamp01(car.Speed / 30f);
                var lens = vcam.Lens;
                lens.FieldOfView = Mathf.Lerp(fovMin, fovMax, sp) + _boostFovNow;
                vcam.Lens = lens;
            }
        }

        /// <summary>
        /// Yolun mesafedeki yüksekliği (araç kökü için) ve eğim açısı (derece, burun yukarı pozitif).
        /// Rampa: kalkıştan önceki 4 m'de 0 → 2 m kama, ardından boşluk üstünde uçuş yayı (2 m'den iniş seviyesine).
        /// Tümsek: parça boyunca 1 m'lik yumuşak kambur.
        /// </summary>
        float HeightAt(float d, CarSim car, out float pitchDeg)
        {
            pitchDeg = 0f;
            var span = controller.track.Layout.SpanAtDistance(d);
            var def = span.Def;
            float local = d - span.Start;

            if (def.Kind == PieceKind.Bump)
            {
                // sin² kambur: uçlarda eğim 0, ortada 1 m (silindir tümseğin üstünden geçer).
                float u = Mathf.Clamp01(local / def.Length);
                float sn = Mathf.Sin(Mathf.PI * u);
                float slope = BumpHeight * Mathf.PI / def.Length * Mathf.Sin(2f * Mathf.PI * u);
                pitchDeg = Mathf.Atan(slope) * Mathf.Rad2Deg;
                return BumpHeight * sn * sn;
            }

            if (def.Kind == PieceKind.Ramp)
            {
                float takeoff = def.Length - def.GapLength;
                if (local <= takeoff)
                {
                    // Gövdenin ön ve arka ucu (±2 m) kama yüzeyine oturur; yüzey dışbükey olduğundan
                    // aradaki tekerlekler ve gövde yüzeyin hep üstünde kalır.
                    float sr = WedgeSurface(local - BodyHalfLength, takeoff);
                    float sf = WedgeSurface(local + BodyHalfLength, takeoff);
                    pitchDeg = Mathf.Atan((sf - sr) / (2f * BodyHalfLength)) * Mathf.Rad2Deg;
                    return (sr + sf) * 0.5f;
                }
                // Boşluk üstünde uçuş: kalkış yüksekliğinden iniş seviyesine (yol), yerçekimine göre tepe.
                float s = Mathf.Clamp01((local - takeoff) / def.GapLength);
                float start = WedgeSurface(takeoff, takeoff);
                float flight = def.GapLength / Mathf.Max(car.Speed, 1f);
                float apex = controller.Tuning.Gravity * flight * flight / 8f;
                float hArc = start * (1f - s) + 4f * apex * s * (1f - s);
                float dh = (-start + 4f * apex * (1f - 2f * s)) / def.GapLength;
                float rampPitch = Mathf.Atan(RampHeight / RampWedgeLength) * Mathf.Rad2Deg;
                pitchDeg = Mathf.Lerp(rampPitch, Mathf.Atan(dh) * Mathf.Rad2Deg * 0.6f, Mathf.Clamp01(s * 3f));
                return hArc;
            }

            return 0f;
        }

        const float BodyHalfLength = 2f;

        // Kama üst yüzeyi (+ küçük pay), parça içi yerel mesafede; kalkıştan sonrası doğrusal uzatılır.
        static float WedgeSurface(float local, float takeoff)
        {
            float slope = RampHeight / RampWedgeLength;
            return Mathf.Max(0f, (local - (takeoff - RampWedgeLength)) * slope + RampLip);
        }
    }
}
