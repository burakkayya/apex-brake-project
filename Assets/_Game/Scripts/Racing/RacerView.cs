using UnityEngine;
using ApexBrake.Simulation;

namespace ApexBrake.Racing
{
    /// <summary>
    /// Bir yarışçıyı çizer: pist çizgisi + yanal konum (CarSim.Lateral), rampa/tümsek yükseklik profili, uçuş,
    /// drift açısı (SlipAngle), yatma/yunuslama, tümsekte hafifleme zıplaması, yoldan çıkınca takla (kendi sayacıyla)
    /// ve temas sarsıntısı. Kamera ChaseCameraRig'dedir. CinemachineBrain'den önce, RaceController'dan sonra çalışır.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class RacerView : MonoBehaviour
    {
        public RaceController controller;
        public int racerIndex;
        public Transform body;

        public float baseHeight = 0.5f;
        [Tooltip("Gövde yönü = SlipAngle × bu (viraj dışına kayarken, ters işaretle: kuyruk dışa)")]
        public float driftYawFactor = 1.6f;
        public float maxDriftYaw = 35f;     // derece
        public float maxRoll = 6f;          // derece; viraj dışına yatma
        public float maxPitch = 3f;         // derece; fren/gaz
        const float LightDuration = 0.6f;
        const float LightHop = 0.35f;       // m

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
        float _crashYaw;
        float _lightStart = -1f;
        float _yawNow, _yawVel, _rollNow, _rollVel, _pitchNow, _pitchVel, _driftWeight;
        RaceController _subscribed;

        /// <summary>Takla atan gövde (kamera elemede buna bakar).</summary>
        public Transform Body => body != null ? body : transform;

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
            _lightStart = -1f;
            _yawNow = _yawVel = _rollNow = _rollVel = _pitchNow = _pitchVel = _driftWeight = 0f;
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
                case SimEventKind.Light:
                    _lightStart = Time.time;
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
            _crashYaw = _yawNow;
            // Savrulma yönü: çıkış anındaki yanal hızın işareti (yoldan çıkan araç bunu korur).
            // Yanal hız yoksa (ör. tümsekte havalanıp çakılma) yakın kenara doğru.
            float lv = racer.Car.LateralVel;
            _crashSide = Mathf.Abs(lv) > 0.05f ? Mathf.Sign(lv) : (racer.Lateral < 0f ? -1f : 1f);
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
            float dt = Time.deltaTime;

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
                // Kaymadaki drift açısından başlar, savrulma yönünde takla atar.
                localPos = new Vector3(_crashSide * 14f * ease, Mathf.Max(arc, 0f) - _crashHeight * k, 16f * ease);
                localRot = Quaternion.Euler(ease * 720f, _crashYaw * (1f - k), -_crashSide * ease * 360f);
            }
            else
            {
                UpdateBodyPose(c, racer, car, d, dt);
                localRot = Quaternion.Euler(_pitchNow, _yawNow, _rollNow);
                if (_lightStart >= 0f)
                {
                    float t = Time.time - _lightStart;
                    if (t < LightDuration) localPos.y += LightHop * Mathf.Sin(Mathf.PI * t / LightDuration);
                    else _lightStart = -1f;
                }
            }

            if (!crashed && Time.time < _shakeUntil)
            {
                float s = (_shakeUntil - Time.time) / ShakeDuration;
                float w = Time.time * 60f;
                localPos += new Vector3(Mathf.Sin(w) * 0.08f, Mathf.Abs(Mathf.Sin(w * 1.3f)) * 0.05f, 0f) * s;
                localRot *= Quaternion.Euler(0f, 0f, Mathf.Sin(w * 0.9f) * 3f * s);
            }

            if (body != null)
            {
                body.localPosition = localPos;
                body.localRotation = localRot;
            }
        }

        /// <summary>Gövdenin gösterim yönü (derece, + = sağa): yönlendirmenin son değeri. Ölçüm/doğrulama içindir.</summary>
        public float BodyYaw => _yawNow;
        public float BodyRoll => _rollNow;
        public float BodyPitch => _pitchNow;

        /// <summary>
        /// Drift açısı, yatma ve yunuslama. Araç viraj dışına (merkezkaç yönüne) kayarken kuyruk dışa savrulur:
        /// burun virajın içine döner (gövde = yol yönü − SlipAngle × 1,6). Çizgisine dönerken ya da düzlükte yana kayarken
        /// burun gidiş yönüne bakar (yol yönü + SlipAngle). İkisi arasındaki geçiş yumuşatılır; iki formül de
        /// SlipAngle = 0'da 0 olduğundan sıçrama olmaz.
        /// </summary>
        void UpdateBodyPose(RaceController c, Racer racer, CarSim car, float d, float dt)
        {
            var def = c.track.Layout.SpanAtDistance(d).Def;
            float slipDeg = car.SlipAngle * Mathf.Rad2Deg;
            float centrifugal = 0f;                     // + = sağa doğru ivme
            if (def.Kind == PieceKind.Corner && def.Radius > 0f && car.Speed >= 1f)
                centrifugal = -def.TurnSign * car.Speed * car.Speed / def.Radius;

            bool outward = def.Kind == PieceKind.Corner && car.LateralVel * -def.TurnSign > 0f && car.IsSliding;
            if (car.Phase == CarPhase.Airborne) outward = false;
            _driftWeight = Mathf.MoveTowards(_driftWeight, outward ? 1f : 0f, dt / 0.3f);
            float yawTarget = slipDeg * Mathf.Lerp(1f, -driftYawFactor, _driftWeight);
            yawTarget = Mathf.Clamp(yawTarget, -maxDriftYaw, maxDriftYaw);
            _yawNow = Mathf.SmoothDamp(_yawNow, yawTarget, ref _yawVel, 0.08f, Mathf.Infinity, dt);

            // Yatma: yanal ivmeye göre viraj dışına (sağa ivme → gövdenin sağı alçalır: z negatif).
            float grip = Mathf.Max(car.Stats.Grip, 0.1f);
            float rollTarget = car.Phase == CarPhase.Airborne ? 0f : -Mathf.Clamp(centrifugal / grip, -1f, 1f) * maxRoll;
            _rollNow = Mathf.SmoothDamp(_rollNow, rollTarget, ref _rollVel, 0.18f, Mathf.Infinity, dt);

            // Yunuslama: frende burun dalar (x +), gazda hafif kalkar.
            float pitchTarget = 0f;
            if (car.Phase != CarPhase.Airborne && car.Phase != CarPhase.Finished)
            {
                if (racer.IsBraking && car.Speed > 0.5f) pitchTarget = maxPitch;
                else if (car.Speed < car.Stats.TopSpeed * car.BoostFactor - 0.5f) pitchTarget = -maxPitch * 0.5f * (1f - car.LateralLoad);
            }
            _pitchNow = Mathf.SmoothDamp(_pitchNow, pitchTarget, ref _pitchVel, 0.15f, Mathf.Infinity, dt);
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
