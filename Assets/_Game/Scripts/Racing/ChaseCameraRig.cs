using UnityEngine;
using ApexBrake.Track;

namespace ApexBrake.Racing
{
    /// <summary>
    /// Oyuncunun takip kamerası (spec §11.6): fizik gövdesini izler. Hız arttıkça uzaklaşır ve FOV genişler, yavaşlayınca
    /// yaklaşır ve alçalır; frende hafif öne gelir; kayarken viraj dışına hafifçe kayar ve en çok 4° yatar; havadayken hafif
    /// yukarı kalkar; temas ve sert inişte kısa sarsıntı; oyuncu oyun dışı kalınca takibi bırakıp durur, araca bakar ve
    /// yakınlaşır; bitişte aracın yanına doğru döner. Yalnızca sunumdur, yarışın sonucunu etkilemez.
    /// Kamera yönü aracın hız yönünü (burnunu değil) izler: kayan araç kadrajda yan döner, drift görünür.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(Camera))]
    public sealed class ChaseCameraRig : MonoBehaviour
    {
        public RaceManager race;

        [Header("Hiz")]
        public Vector3 slowOffset = new Vector3(0f, 4.2f, -8f);     // hız 0
        public Vector3 fastOffset = new Vector3(0f, 5.6f, -11f);    // hız fastSpeed
        [Tooltip("Geri sayımda: grid görünsün diye yüksek ve geride; start ile yarış ofsetine süzülür")]
        public Vector3 countdownOffset = new Vector3(0f, 8.5f, -17f);
        public float fastSpeed = 40f;                               // m/s
        public float fovSlow = 62f;
        public float fovFast = 78f;
        public float lookHeight = 0.6f;                             // aracın üstünde bakılan nokta
        public float lookAheadPerSpeed = 0.12f;                     // bakış noktası hızla öne kayar (m / (m/s))

        [Header("Fren")]
        public float brakeCloser = 0.9f;                            // ofset z'ye eklenir (yaklaşır)
        public float brakeFov = -2f;

        [Header("Kayma")]
        public float slideShiftMax = 1.4f;                          // m; viraj dışına
        public float slideDutchMax = 4f;                            // derece
        public float slipForFullEffect = 20f;                       // derece

        [Header("Havada")]
        public float airLift = 0.8f;                                // m

        [Header("Sarsinti")]
        public float shakeDuration = 0.28f;
        public float shakeAmplitude = 0.18f;                        // m, en çok
        public float hardLandingSpeed = 5f;                         // m/s düşey hızla inince sarsıntı (yalnızca sunum)

        [Header("Eleme")]
        public float outStopTime = 0.7f;
        public float outFov = -14f;

        [Header("Bitis")]
        public Vector3 finishOffset = new Vector3(5.5f, 2.6f, -5.5f);
        public float finishTime = 1.6f;

        [Header("Yumusatma (sn)")]
        public float positionSmooth = 0.12f;
        public float yawSmooth = 0.28f;
        public float offsetSmooth = 0.5f;
        public float fovSmooth = 0.45f;
        public float slideSmooth = 0.45f;
        public float minRoadClearance = 1.4f;

        Camera _cam;
        RaceManager _subscribed;
        RacerEntry _player;

        Vector3 _offset, _offsetVel;
        float _fov, _fovVel, _shift, _shiftVel, _dutch, _dutchVel, _lift, _liftVel;
        float _yaw, _yawVel;
        Vector3 _pos, _posVel;
        bool _snap = true;
        float _shakeStart = -1f, _shakeScale = 1f;
        bool _wasAir;
        float _minAirVy;

        bool _out; float _outTime; Vector3 _outVel; float _outFovBase;
        bool _finished; float _finishStart; Vector3 _finishFrom;

        // Ölçüm (doğrulama için)
        public Vector3 CurrentOffset => _finished ? _offset : _offset + new Vector3(_shift, _lift, 0f);
        public float CurrentFov => _fov;
        public float CurrentDutch => _dutch;
        public bool IsOutView => _out;
        public bool IsFinishView => _finished;

        void Awake() { _cam = GetComponent<Camera>(); }
        void OnEnable() { Subscribe(); _snap = true; }

        void OnDisable()
        {
            if (_subscribed == null) return;
            _subscribed.Restarted -= OnRestarted;
            _subscribed.RacerOut -= OnOut;
            _subscribed.RacerFinished -= OnFinished;
            _subscribed.RacerContact -= OnContact;
            _subscribed = null;
        }

        void Subscribe()
        {
            if (race == null || _subscribed == race) return;
            OnDisable();
            race.Restarted += OnRestarted;
            race.RacerOut += OnOut;
            race.RacerFinished += OnFinished;
            race.RacerContact += OnContact;
            _subscribed = race;
        }

        void OnRestarted() { _snap = true; _out = false; _finished = false; _shakeStart = -1f; }

        void OnOut(int i, OutReason r)
        {
            if (i != RaceManager.PlayerIndex || _out || _player == null) return;
            _out = true; _outTime = Time.time; _outVel = _posVel; _outFovBase = _fov;
        }

        void OnFinished(int i)
        {
            if (i != RaceManager.PlayerIndex) return;
            _finished = true; _finishStart = Time.time; _finishFrom = _offset + new Vector3(_shift, _lift, 0f);
        }

        void OnContact(int a, int b, float v)
        {
            if (a != RaceManager.PlayerIndex && b != RaceManager.PlayerIndex) return;
            Shake(Mathf.Clamp01(v / 8f) * 0.8f + 0.3f);
        }

        void Shake(float scale) { _shakeStart = Time.time; _shakeScale = scale; }

        void LateUpdate()
        {
            Subscribe();
            if (race == null || race.Racers.Count == 0) return;
            _player = race.Player;
            var motor = _player.Motor; var tel = _player.Telemetry;
            if (motor == null) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            var t = motor.transform;                       // interpolasyonlu gövde
            Vector3 v = motor.Body.linearVelocity;
            Vector3 flatV = new Vector3(v.x, 0f, v.z);
            float speed = flatV.magnitude;
            bool racing = race.State != RaceState.Countdown;
            bool braking = racing && motor.Brake && motor.EngineOn && !_player.Finished;

            if (_snap) { ResetTo(t, speed); }

            // Yön: hız yönü (yavaşken burun); dünya yukarısı korunur.
            Vector3 fwd = t.forward; fwd.y = 0f;
            Vector3 dir = speed > 4f ? flatV : fwd;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            float yawWant = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            if (!_out) _yaw = Mathf.SmoothDampAngle(_yaw, yawWant, ref _yawVel, yawSmooth, Mathf.Infinity, dt);

            float sp = Mathf.Clamp01(speed / Mathf.Max(fastSpeed, 0.01f));
            Vector3 offWant = Vector3.Lerp(slowOffset, fastOffset, sp * sp * (3f - 2f * sp) * 0.5f + sp * 0.5f);
            if (braking) offWant.z += brakeCloser;
            if (race.State == RaceState.Countdown) offWant = countdownOffset;
            _offset = Vector3.SmoothDamp(_offset, offWant, ref _offsetVel, offsetSmooth, Mathf.Infinity, dt);

            float slip = tel != null && racing && !_out ? tel.SlipAngleDeg : 0f;
            float s = tel != null && tel.IsSliding ? Mathf.Clamp(slip / slipForFullEffect, -1f, 1f) : 0f;
            // SlipAngle + = hız burnun sağında → araç sola dönüyor, viraj dışı sağ; kamera dışa (hız tarafına) kayar.
            _shift = Mathf.SmoothDamp(_shift, s * slideShiftMax, ref _shiftVel, slideSmooth, Mathf.Infinity, dt);
            _dutch = Mathf.SmoothDamp(_dutch, -s * slideDutchMax, ref _dutchVel, slideSmooth, Mathf.Infinity, dt);
            bool air = tel != null && tel.IsAirborne;
            _lift = Mathf.SmoothDamp(_lift, air ? airLift : 0f, ref _liftVel, 0.35f, Mathf.Infinity, dt);

            // Sert iniş sarsıntısı
            if (air) _minAirVy = Mathf.Min(_minAirVy, v.y);
            if (_wasAir && !air && tel.GroundedWheels > 0)
            {
                if (-_minAirVy >= hardLandingSpeed) Shake(Mathf.Clamp01(-_minAirVy / 12f) + 0.2f);
                _minAirVy = 0f;
            }
            if (!air && tel != null && tel.GroundedWheels > 0) _minAirVy = 0f;
            _wasAir = air;

            float fovWant = Mathf.Lerp(fovSlow, fovFast, sp);
            if (braking) fovWant += brakeFov;
            if (_out) fovWant = _outFovBase + outFov * Mathf.Clamp01((Time.time - _outTime) / 2.5f);
            _fov = Mathf.SmoothDamp(_fov, fovWant, ref _fovVel, fovSmooth, Mathf.Infinity, dt);

            var yawRot = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 carPos = t.position;
            Vector3 lookAt = carPos + Vector3.up * lookHeight + dir.normalized * Mathf.Min(speed * lookAheadPerSpeed, 5f);

            if (_out)
            {
                // Takibi bırak: elendiği andaki hızla süzülüp dur; araca bak.
                float k = Mathf.Clamp01((Time.time - _outTime) / Mathf.Max(outStopTime, 0.01f));
                _pos += _outVel * (1f - k) * dt;
                _posVel = Vector3.zero;
                lookAt = motor.Body.worldCenterOfMass;
            }
            else
            {
                Vector3 off;
                if (_finished)
                {
                    float k = Mathf.Clamp01((Time.time - _finishStart) / Mathf.Max(finishTime, 0.01f));
                    k = k * k * (3f - 2f * k);
                    off = Vector3.Lerp(_finishFrom, finishOffset, k);
                    lookAt = carPos + Vector3.up * lookHeight;
                }
                else off = _offset + new Vector3(_shift, _lift, 0f);
                Vector3 want = carPos + yawRot * off;
                _pos = Vector3.SmoothDamp(_pos, want, ref _posVel, positionSmooth, Mathf.Infinity, dt);
            }

            // Yol yüzeyine gömülme: kamera altındaki yüzeyin en az minRoadClearance üstünde kalır.
            if (Physics.Raycast(_pos + Vector3.up * 30f, Vector3.down, out var hit, 60f, TrackLayers.SurfaceMask, QueryTriggerInteraction.Ignore))
                if (_pos.y < hit.point.y + minRoadClearance) _pos.y = hit.point.y + minRoadClearance;

            Vector3 shake = Vector3.zero;
            if (_shakeStart >= 0f)
            {
                float st = Time.time - _shakeStart;
                if (st < shakeDuration)
                {
                    float env = 1f - st / shakeDuration;
                    float w = st * 2f * Mathf.PI;
                    shake = new Vector3(Mathf.Sin(w * 23f), Mathf.Sin(w * 31f + 1.3f) * 0.7f, 0f) * (shakeAmplitude * _shakeScale * env);
                }
                else _shakeStart = -1f;
            }

            var rot = Quaternion.LookRotation(lookAt - _pos, Vector3.up) * Quaternion.Euler(0f, 0f, _dutch);
            transform.SetPositionAndRotation(_pos + rot * shake, rot);
            _cam.fieldOfView = _fov;
        }

        void ResetTo(Transform t, float speed)
        {
            _snap = false;
            Vector3 fwd = t.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            _yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg; _yawVel = 0f;
            _offset = race != null && race.State == RaceState.Countdown ? countdownOffset : slowOffset; _offsetVel = Vector3.zero;
            _fov = fovSlow; _fovVel = 0f;
            _shift = _shiftVel = _dutch = _dutchVel = _lift = _liftVel = 0f;
            _pos = t.position + Quaternion.Euler(0f, _yaw, 0f) * _offset; _posVel = Vector3.zero;
            _wasAir = false; _minAirVy = 0f;
        }
    }
}
