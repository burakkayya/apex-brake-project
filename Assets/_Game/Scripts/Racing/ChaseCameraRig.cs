using UnityEngine;
using Unity.Cinemachine;
using ApexBrake.Simulation;

namespace ApexBrake.Racing
{
    /// <summary>
    /// Oyuncunun takip kamerası (spec §11.6). Cinemachine kamerasının takip ofsetini, görüş açısını ve yatmasını sürer;
    /// sarsıntıyı ve elemedeki "takibi bırakma"yı kendi pipeline geri çağrısında uygular. Hepsi yumuşatılır.
    /// FOV yalnızca burada yazılır. RacerView'dan sonra, CinemachineBrain'den önce çalışır.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    [RequireComponent(typeof(CinemachineCamera))]
    public sealed class ChaseCameraRig : CinemachineExtension
    {
        public RaceController controller;
        public RacerView target;                        // oyuncu aracı

        [Header("Hiz")]
        public Vector3 slowOffset = new Vector3(0f, 6.5f, -11.5f);   // hız 0
        public Vector3 fastOffset = new Vector3(0f, 8f, -15f);       // hız fastSpeed
        public float fastSpeed = 30f;
        public float fovSlow = 60f;
        public float fovFast = 68f;
        public float lookAtHeight = 2.6f;

        [Header("Fren")]
        public float brakeCloser = 1.2f;                // ofset z'ye eklenir (yaklaşır)
        public float brakeFov = -2f;

        [Header("Kayma")]
        public float slideShiftMax = 1.5f;              // m; SlipAngle yönünün tersine
        public float slideDutchMax = 4f;                // derece
        public float slipForFullEffect = 0.2f;          // rad; bu kayma açısında tam etki

        [Header("Boost")]
        public float boostFov = 6f;

        [Header("Sarsinti")]
        public float shakeDuration = 0.25f;
        public float shakeAmplitude = 0.25f;            // m, en çok

        [Header("Eleme")]
        public float crashStopTime = 0.6f;              // kamera bu sürede durur
        public float crashFov = -8f;

        [Header("Bitis")]
        public Vector3 finishOffset = new Vector3(6f, 4f, -8f);
        public float finishTime = 1.5f;

        [Header("Yumusatma (sn)")]
        public float offsetSmooth = 0.45f;
        public float fovSmooth = 0.35f;
        public float slideSmooth = 0.4f;
        public float dutchSmooth = 0.4f;

        CinemachineCamera _vcam;
        CinemachineFollow _follow;
        CinemachineHardLookAt _look;
        RaceController _subscribed;

        Vector3 _offset, _offsetVel;
        float _fov, _fovVel, _shift, _shiftVel, _dutch, _dutchVel;
        float _shakeStart = -1f;
        bool _snap;

        // Eleme: kamera konumu kendi hızıyla süzülüp durur.
        bool _crashed;
        float _crashTime;
        Vector3 _crashPos, _crashVel;
        float _crashFovBase;
        Vector3 _lastRaw, _lastRawVel;
        bool _haveRaw;

        // Bitiş: ofset 1,5 sn'de çapraza döner.
        bool _finished;
        float _finishTime;
        Vector3 _finishFrom;

        // Ölçüm (doğrulama için; oynanışa etkisi yok)
        public Vector3 CurrentOffset => _offset + new Vector3(_shift, 0f, 0f);
        public float CurrentFov => _fov;
        public float CurrentDutch => _dutch;
        public bool IsCrashView => _crashed;
        public Vector3 CameraPosition { get; private set; }
        public float MaxFrameStep { get; set; }         // kamera konumunun kare başına en büyük değişimi (m)
        Vector3 _prevCamPos;
        bool _havePrevCam;

        protected override void Awake()
        {
            base.Awake();
            CacheComponents();
        }

        void CacheComponents()
        {
            if (_vcam == null) _vcam = GetComponent<CinemachineCamera>();
            if (_follow == null) _follow = GetComponent<CinemachineFollow>();
            if (_look == null) _look = GetComponent<CinemachineHardLookAt>();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            Subscribe();
            ResetState(true);
        }

        void Start() => Subscribe();

        void OnDisable()
        {
            if (_subscribed == null) return;
            _subscribed.RacerEvent -= OnRacerEvent;
            _subscribed.Restarted -= OnRestarted;
            _subscribed = null;
        }

        void Subscribe()
        {
            if (controller == null || _subscribed == controller) return;
            OnDisable();
            controller.RacerEvent += OnRacerEvent;
            controller.Restarted += OnRestarted;
            _subscribed = controller;
        }

        void OnRestarted() => ResetState(true);

        void ResetState(bool snap)
        {
            CacheComponents();
            _offset = slowOffset; _offsetVel = Vector3.zero;
            _fov = fovSlow; _fovVel = 0f;
            _shift = _shiftVel = _dutch = _dutchVel = 0f;
            _shakeStart = -1f;
            _crashed = false; _finished = false; _haveRaw = false; _havePrevCam = false;
            if (_vcam != null && target != null)
            {
                _vcam.Target.TrackingTarget = target.transform;
                _vcam.Target.LookAtTarget = target.transform;
            }
            if (_look != null) _look.LookAtOffset = new Vector3(0f, lookAtHeight, 0f);
            _snap = snap;   // Faz 2 davranışı: araç konumlanınca kamera anında arkasına alınır
        }

        void OnRacerEvent(int index, SimEvent e)
        {
            if (index != RaceController.PlayerIndex) return;
            switch (e.Kind)
            {
                case SimEventKind.RearHit:
                case SimEventKind.Bumped:
                case SimEventKind.SideHit:
                case SimEventKind.Light:
                    _shakeStart = Time.time;
                    break;
                case SimEventKind.Crash:
                case SimEventKind.FellShort:
                    if (controller != null && controller.Player != null && controller.Player.Car.IsOut) BeginCrash();
                    break;
                case SimEventKind.Finished:
                    _finished = true; _finishTime = Time.time; _finishFrom = _offset + new Vector3(_shift, 0f, 0f);
                    _shift = 0f; _shiftVel = 0f;
                    break;
            }
        }

        void BeginCrash()
        {
            if (_crashed) return;
            _crashed = true;
            _crashTime = Time.time;
            _crashPos = _haveRaw ? _lastRaw : transform.position;
            _crashVel = _haveRaw ? _lastRawVel : Vector3.zero;
            _crashFovBase = _fov;
            if (_vcam != null && target != null) _vcam.Target.LookAtTarget = target.Body;
            if (_look != null) _look.LookAtOffset = Vector3.zero;
        }

        void LateUpdate()
        {
            var c = controller;
            if (c == null || c.Race == null || target == null) return;
            CacheComponents();
            Subscribe();
            var car = c.Race.Racers[RaceController.PlayerIndex].Car;
            float dt = Time.deltaTime;
            if (car.IsOut && !_crashed) BeginCrash();   // olay kaçtıysa

            bool braking = c.State == RaceState.Racing && c.Race.Racers[RaceController.PlayerIndex].IsBraking && !car.IsOut
                           && car.Phase != CarPhase.Finished;
            float sp = Mathf.Clamp01(car.Speed / Mathf.Max(fastSpeed, 0.01f));

            // Ofset: hız + fren (yaklaşır), bitişte çapraz
            if (_finished)
            {
                float k = Mathf.Clamp01((Time.time - _finishTime) / Mathf.Max(finishTime, 0.01f));
                k = k * k * (3f - 2f * k);
                _offset = Vector3.Lerp(_finishFrom, finishOffset, k);
                _offsetVel = Vector3.zero;
            }
            else if (!_crashed)
            {
                Vector3 want = Vector3.Lerp(slowOffset, fastOffset, sp);
                if (braking) want.z += brakeCloser;
                _offset = _snap ? want : Vector3.SmoothDamp(_offset, want, ref _offsetVel, offsetSmooth, Mathf.Infinity, dt);
            }

            // Kayma: SlipAngle yönünün tersine yanal kayma ve yatma; Recovered (faz Driving) sonrası 0'a döner.
            float shiftWant = 0f, dutchWant = 0f;
            if (!_crashed && !_finished && car.Phase == CarPhase.Skidding)
            {
                float s = Mathf.Clamp(car.SlipAngle / Mathf.Max(slipForFullEffect, 1e-3f), -1f, 1f);
                shiftWant = -s * slideShiftMax;
                dutchWant = s * slideDutchMax;
            }
            if (!_finished) _shift = _snap ? shiftWant : Mathf.SmoothDamp(_shift, shiftWant, ref _shiftVel, slideSmooth, Mathf.Infinity, dt);
            _dutch = _snap ? dutchWant : Mathf.SmoothDamp(_dutch, dutchWant, ref _dutchVel, dutchSmooth, Mathf.Infinity, dt);

            // FOV: hız + fren + boost (+ eleme)
            float fovWant = Mathf.Lerp(fovSlow, fovFast, sp);
            if (braking) fovWant += brakeFov;
            if (car.IsBoosting) fovWant += boostFov;
            if (_crashed) fovWant = _crashFovBase + crashFov;
            _fov = _snap ? fovWant : Mathf.SmoothDamp(_fov, fovWant, ref _fovVel, fovSmooth, Mathf.Infinity, dt);

            if (_follow != null) _follow.FollowOffset = _offset + new Vector3(_shift, 0f, 0f);
            if (_vcam != null)
            {
                var lens = _vcam.Lens;
                lens.FieldOfView = _fov;
                lens.Dutch = _dutch;
                _vcam.Lens = lens;
            }

            if (_snap) Snap();
        }

        void Snap()
        {
            _snap = false;
            if (_vcam == null || target == null) return;
            var t = target.transform;
            var yaw = Quaternion.Euler(0f, t.eulerAngles.y, 0f);
            Vector3 camPos = t.position + yaw * (_offset + new Vector3(_shift, 0f, 0f));
            var look = t.position + Vector3.up * lookAtHeight - camPos;
            _vcam.ForceCameraPosition(camPos, Quaternion.LookRotation(look, Vector3.up));
            _vcam.PreviousStateIsValid = false;
            _haveRaw = false; _havePrevCam = false;
        }

        protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam,
            CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
        {
            if (stage == CinemachineCore.Stage.Body)
            {
                if (_crashed)
                {
                    // Takibi bırak: elendiği andaki hızla süzül, crashStopTime'da dur.
                    float t = Time.time - _crashTime;
                    float stop = Mathf.Max(crashStopTime, 0.01f);
                    if (deltaTime > 0f && t < stop)
                    {
                        float k0 = Mathf.Clamp01((t - deltaTime) / stop), k1 = Mathf.Clamp01(t / stop);
                        // hız doğrusal azalır: konum = v · stop · (k − k²/2)
                        float a0 = k0 - k0 * k0 * 0.5f, a1 = k1 - k1 * k1 * 0.5f;
                        _crashPos += _crashVel * stop * (a1 - a0);
                    }
                    state.RawPosition = _crashPos;
                }
                else if (deltaTime > 0f)
                {
                    if (_haveRaw) _lastRawVel = (state.RawPosition - _lastRaw) / deltaTime;
                    _lastRaw = state.RawPosition;
                    _haveRaw = true;
                }
            }
            else if (stage == CinemachineCore.Stage.Noise)
            {
                if (_shakeStart >= 0f && !_crashed)
                {
                    float t = Time.time - _shakeStart;
                    if (t < shakeDuration)
                    {
                        float env = 1f - t / shakeDuration;       // sönümlü
                        float w = t * 2f * Mathf.PI;
                        var local = new Vector3(Mathf.Sin(w * 23f), Mathf.Sin(w * 31f + 1.3f) * 0.6f, 0f) * (shakeAmplitude * env);
                        state.PositionCorrection += state.RawOrientation * local;
                    }
                    else _shakeStart = -1f;
                }
                Vector3 p = state.GetCorrectedPosition();
                if (_havePrevCam && deltaTime > 0f) MaxFrameStep = Mathf.Max(MaxFrameStep, (p - _prevCamPos).magnitude);
                _prevCamPos = p; _havePrevCam = true;
                CameraPosition = p;
            }
        }
    }
}
