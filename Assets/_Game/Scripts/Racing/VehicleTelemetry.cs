using UnityEngine;
using ApexBrake.Track;

namespace ApexBrake.Racing
{
    /// <summary>Yalnızca sunum/ölçüm (HUD, kamera, testler, oyun dışı denetimi); aracın davranışını etkilemez.</summary>
    [DefaultExecutionOrder(20)]
    [RequireComponent(typeof(VehicleMotor))]
    public sealed class VehicleTelemetry : MonoBehaviour
    {
        public const float AirborneDelay = 0.15f;
        public const float SlideAngle = 8f;
        public const float SlideMinKmh = 20f;

        public float SpeedKmh { get; private set; }
        public float ForwardSpeed { get; private set; }
        /// <summary>Hız vektörü ile burun arasındaki işaretli açı (derece; + hız burnun sağında).</summary>
        public float SlipAngleDeg { get; private set; }
        public int GroundedWheels { get; private set; }
        /// <summary>Ground (yol dışı) katmanına değen teker sayısı.</summary>
        public int GroundContactWheels { get; private set; }
        /// <summary>Road katmanına değen teker sayısı.</summary>
        public int RoadContactWheels { get; private set; }
        /// <summary>Gövde (sac) Ground katmanına değiyor (son fizik adımında; uyuyan gövdede son değer korunur).</summary>
        public bool BodyOnGround { get; private set; }
        /// <summary>Teker ya da gövde yol dışı zemine değiyor.</summary>
        public bool TouchesGround => GroundContactWheels > 0 || BodyOnGround;
        public bool IsAirborne { get; private set; }
        public bool IsSliding { get; private set; }
        public float UpDot { get; private set; } = 1f;
        public float PitchDeg { get; private set; }     // + burun yukarı
        public float RollDeg { get; private set; }      // + sağa yatmış
        /// <summary>Gövde eksenlerinde ivme (m/s², yatay; + sağ / + ileri). Hızdaki değişimden ölçülür.</summary>
        public float LateralAccel { get; private set; }
        public float LongitudinalAccel { get; private set; }

        VehicleMotor _motor;
        float _airTime;
        Vector3 _prevV; bool _hasPrev;
        bool _bodyGroundThisStep;

        void OnCollisionStay(Collision c) { if (c.collider.gameObject.layer == TrackLayers.Ground) _bodyGroundThisStep = true; }
        void OnCollisionEnter(Collision c) { if (c.collider.gameObject.layer == TrackLayers.Ground) _bodyGroundThisStep = true; }

        void Awake() => _motor = GetComponent<VehicleMotor>();

        public void ResetState() { _airTime = 0f; IsAirborne = false; _hasPrev = false; BodyOnGround = false; _bodyGroundThisStep = false; }

        void FixedUpdate()
        {
            var rb = _motor.Body;
            if (rb == null) return;
            Vector3 v = rb.linearVelocity;
            SpeedKmh = v.magnitude * 3.6f;
            Vector3 fwd = rb.rotation * Vector3.forward, up = rb.rotation * Vector3.up, right = rb.rotation * Vector3.right;
            ForwardSpeed = Vector3.Dot(v, fwd);
            if (_hasPrev)
            {
                Vector3 a = (v - _prevV) / Time.fixedDeltaTime;
                LateralAccel = Vector3.Dot(a, right);
                LongitudinalAccel = Vector3.Dot(a, fwd);
            }
            _prevV = v; _hasPrev = true;
            UpDot = Vector3.Dot(up, Vector3.up);
            PitchDeg = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
            RollDeg = -Mathf.Asin(Mathf.Clamp(right.y, -1f, 1f)) * Mathf.Rad2Deg;

            Vector3 fv = new Vector3(v.x, 0f, v.z), ff = new Vector3(fwd.x, 0f, fwd.z);
            SlipAngleDeg = fv.sqrMagnitude > 1f && ff.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(ff, fv, Vector3.up) : 0f;

            int grounded = 0, onGround = 0, onRoad = 0;
            foreach (var w in _motor.Wheels)
            {
                if (w == null || !w.GetGroundHit(out var hit)) continue;
                grounded++;
                int layer = hit.collider.gameObject.layer;
                if (layer == TrackLayers.Ground) onGround++;
                else if (layer == TrackLayers.Road) onRoad++;
            }
            if (!rb.IsSleeping()) BodyOnGround = _bodyGroundThisStep;
            _bodyGroundThisStep = false;
            GroundedWheels = grounded;
            GroundContactWheels = onGround;
            RoadContactWheels = onRoad;
            _airTime = grounded == 0 ? _airTime + Time.fixedDeltaTime : 0f;
            IsAirborne = _airTime >= AirborneDelay - 1e-4f;
            IsSliding = Mathf.Abs(SlipAngleDeg) >= SlideAngle && SpeedKmh >= SlideMinKmh;
        }
    }
}
