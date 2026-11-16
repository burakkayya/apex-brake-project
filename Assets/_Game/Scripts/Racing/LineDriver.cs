using UnityEngine;
using ApexBrake.Track;

namespace ApexBrake.Racing
{
    /// <summary>
    /// Sanal sürücünün direksiyonu: ileriye bakan saf takip (pure pursuit). Hıza bağlı mesafe (≈ 0,35 sn × hız,
    /// en az 6 m) ilerideki hedef çizgi noktasına yönelir. Lastikler tutunamazsa araç yine kayar; kayma sırasındaki
    /// karşı direksiyon bu takibin doğal sonucudur. Hızı, konumu ya da dönüşü yazmaz.
    /// </summary>
    [DefaultExecutionOrder(-20)]
    [RequireComponent(typeof(VehicleMotor))]
    public sealed class LineDriver : MonoBehaviour
    {
        public float lookAheadTime = 0.35f;
        public float minLookAhead = 6f;

        public TrackPath Path { get; set; }
        /// <summary>İzlenecek çizgi (pist merkezinden, + sağ).</summary>
        public float TargetLateral { get; set; }
        /// <summary>Son izdüşüm.</summary>
        public float Distance { get; private set; }
        public float Lateral { get; private set; }
        public TrackPose Pose { get; private set; }

        VehicleMotor _motor;
        bool _hasPose;

        void Awake() => _motor = GetComponent<VehicleMotor>();

        /// <summary>İzdüşüm ipucunu sıfırlar (yeniden doğma / yeni pist).</summary>
        public void ResetProjection() { _hasPose = false; Refresh(); }

        void Refresh()
        {
            if (Path == null || _motor == null || _motor.Body == null) return;
            Pose = Path.Project(_motor.Body.position, _hasPose ? Distance : -1f);
            _hasPose = true;
            Distance = Pose.Distance;
            Lateral = Pose.Lateral;
        }

        void FixedUpdate()
        {
            if (Path == null) return;
            Refresh();
            var rb = _motor.Body;
            var cfg = _motor.config;
            float wheelBase = cfg != null ? cfg.wheelBase : 2.6f;
            float maxSteer = cfg != null ? cfg.maxSteerAngle : 30f;

            Vector3 v = rb.linearVelocity; v.y = 0f;
            float look = Mathf.Max(minLookAhead, lookAheadTime * v.magnitude);
            Vector3 target = Path.PointAt(Distance + look, TargetLateral);
            Vector3 rear = rb.position - rb.rotation * Vector3.forward * (wheelBase * 0.5f);
            Vector3 local = Quaternion.Inverse(rb.rotation) * (target - rear);
            local.y = 0f;
            float ld = Mathf.Max(local.magnitude, 0.5f);
            float alpha = Mathf.Atan2(local.x, local.z);
            float steer = Mathf.Atan(2f * wheelBase * Mathf.Sin(alpha) / ld) * Mathf.Rad2Deg;
            _motor.SteerInput = Mathf.Clamp(steer / maxSteer, -1f, 1f);
        }
    }
}
