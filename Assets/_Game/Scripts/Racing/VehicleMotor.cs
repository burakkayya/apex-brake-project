using UnityEngine;
using ApexBrake.Track;

namespace ApexBrake.Racing
{
    /// <summary>
    /// Fizik araç: Rigidbody + 4 WheelCollider. Gaz/fren torku, direksiyon açısı, hava direnci, aşağı bastırma,
    /// viraj denge çubuğu ve yerçekimi çarpanı kuvvet olarak uygulanır. Hiçbir yerde hız/konum/dönüş yazılmaz
    /// (yalnızca PlaceAt: start dizilimi ve yeniden doğma).
    /// </summary>
    [DefaultExecutionOrder(0)]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class VehicleMotor : MonoBehaviour
    {
        public VehicleConfig config;

        /// <summary>Tek sürüş girdisi; false = tam gaz.</summary>
        public bool Brake { get; set; }
        /// <summary>false: araç frenle yerinde tutulur (geri sayım, oyun dışı).</summary>
        public bool EngineOn { get; set; }
        /// <summary>−1..+1; LineDriver yazar.</summary>
        public float SteerInput { get; set; }

        public Rigidbody Body { get; private set; }
        public VehicleUpgrades Upgrades { get; private set; } = VehicleUpgrades.Base;

        // 0 FL, 1 FR, 2 RL, 3 RR
        [SerializeField] WheelCollider[] wheels = new WheelCollider[4];
        [SerializeField] Transform[] wheelVisuals = new Transform[4];

        public WheelCollider[] Wheels => wheels;
        public float SteerAngle { get; private set; }
        public float DriveForce { get; private set; }       // son adımda istenen toplam itiş (N), ölçüm için

        public static bool IsFront(int i) => i < 2;

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            ConfigureBody();
        }

        public void Apply(VehicleUpgrades u)
        {
            Upgrades = u;
            ApplyFriction();
        }

        /// <summary>Kütle, ağırlık merkezi, süspansiyon ve lastik değerlerini config'ten uygular.</summary>
        public void ConfigureBody()
        {
            if (Body == null) Body = GetComponent<Rigidbody>();
            if (config == null) return;
            Body.mass = config.mass;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Body.linearDamping = 0f;
            Body.angularDamping = 0.05f;
            Body.centerOfMass = new Vector3(0f, config.centerOfMassHeight, config.centerOfMassForward);
            foreach (var w in wheels)
            {
                if (w == null) continue;
                w.mass = config.wheelMass;
                w.radius = config.wheelRadius;
                w.suspensionDistance = config.suspensionDistance;
                w.forceAppPointDistance = 0f;
                w.suspensionSpring = new JointSpring { spring = config.spring, damper = config.damper, targetPosition = config.targetPosition };
                w.wheelDampingRate = 0.25f;
            }
            if (wheels[0] != null) wheels[0].ConfigureVehicleSubsteps(5f, 12, 15);
            ApplyFriction();
        }

        void ApplyFriction()
        {
            if (config == null) return;
            for (int i = 0; i < 4; i++) SetFriction(i, 1f);
        }

        void SetFriction(int i, float surface)
        {
            var w = wheels[i];
            if (w == null) return;
            float tire = Upgrades.Tire * surface;
            w.forwardFriction = new WheelFrictionCurve
            {
                extremumSlip = config.fwdExtremumSlip, extremumValue = config.fwdExtremumValue,
                asymptoteSlip = config.fwdAsymptoteSlip, asymptoteValue = config.fwdAsymptoteValue,
                stiffness = config.fwdStiffness * tire,
            };
            w.sidewaysFriction = new WheelFrictionCurve
            {
                extremumSlip = config.sideExtremumSlip, extremumValue = config.sideExtremumValue,
                asymptoteSlip = config.sideAsymptoteSlip, asymptoteValue = config.sideAsymptoteValue,
                stiffness = (IsFront(i) ? config.frontSideStiffness : config.rearSideStiffness) * tire,
            };
        }

        /// <summary>Start dizilimi ve yeniden doğma için: aracı duran halde verilen yol noktasına (teker temas düzlemi) koyar.</summary>
        public void PlaceAt(Vector3 roadPoint, Quaternion rotation)
        {
            if (Body == null) Body = GetComponent<Rigidbody>();
            Vector3 p = roadPoint + rotation * Vector3.up * 0.02f;
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            transform.SetPositionAndRotation(p, rotation);
            Body.position = p;
            Body.rotation = rotation;
            SteerAngle = 0f;
            foreach (var w in wheels) if (w != null) { w.steerAngle = 0f; w.motorTorque = 0f; w.brakeTorque = config != null ? config.holdBrakeTorque : 0f; }
            Physics.SyncTransforms();
            Body.WakeUp();
        }

        void FixedUpdate()
        {
            if (config == null) return;
            var rb = Body;
            float dt = Time.fixedDeltaTime;

            // Yüzey sürtünmesi: her tekerin değdiği malzemenin sürtünme oranı (WheelCollider malzemeyi kendisi okumaz).
            for (int i = 0; i < 4; i++)
            {
                float surface = 1f;
                if (wheels[i].GetGroundHit(out var hit)) surface = TrackLayers.FrictionRatio(hit.collider);
                SetFriction(i, surface);
            }

            // Direksiyon: açı ve dönüş hızı sınırlı.
            float targetSteer = Mathf.Clamp(SteerInput, -1f, 1f) * config.maxSteerAngle;
            SteerAngle = Mathf.MoveTowards(SteerAngle, targetSteer, config.steerRate * dt);
            wheels[0].steerAngle = SteerAngle;
            wheels[1].steerAngle = SteerAngle;

            // Gaz / fren
            float vWheel = DrivenWheelSpeed();
            float top = config.topSpeed * Upgrades.TopSpeed;
            float x = Mathf.Clamp01(Mathf.Abs(vWheel) / Mathf.Max(top, 0.1f));
            float force = config.engineForce * Upgrades.Engine * Mathf.Max(0f, 1f - Mathf.Pow(x, config.torqueCurveExponent));
            bool drive = EngineOn && !Brake;
            DriveForce = drive ? force : 0f;
            for (int i = 0; i < 4; i++)
            {
                float share = DriveShare(i);
                wheels[i].motorTorque = drive ? force * share * config.wheelRadius : 0f;
                float brake = 0f;
                if (!EngineOn) brake = config.holdBrakeTorque;
                else if (Brake)
                {
                    float axle = IsFront(i) ? config.frontBrakeShare : 1f - config.frontBrakeShare;
                    brake = config.brakeForce * axle * 0.5f * config.wheelRadius;
                    if (config.abs && wheels[i].GetGroundHit(out var h) && h.forwardSlip > config.absSlip) brake *= 0.5f;
                }
                wheels[i].brakeTorque = brake;
            }

            // Yerçekimi çarpanı, hava direnci, aşağı bastırma, yuvarlanma direnci
            Vector3 v = rb.linearVelocity;
            float g = Physics.gravity.magnitude;
            rb.AddForce(Physics.gravity * (config.gravityMultiplier - 1f), ForceMode.Acceleration);
            rb.AddForce(-v * v.magnitude * config.dragCoefficient, ForceMode.Force);
            float vf = Vector3.Dot(v, transform.forward);
            int grounded = 0;
            foreach (var w in wheels) if (w.isGrounded) grounded++;
            rb.AddForce(-transform.up * config.downforceCoefficient * vf * vf, ForceMode.Force);
            if (grounded > 0)
            {
                if (v.sqrMagnitude > 0.01f)
                    rb.AddForce(-v.normalized * config.rollingResistance * config.mass * g * config.gravityMultiplier * grounded / 4f, ForceMode.Force);
            }

            // Viraj denge çubuğu
            AntiRoll(0, 1);
            AntiRoll(2, 3);
        }

        float DriveShare(int i)
        {
            switch (config.drive)
            {
                case DriveLayout.RearWheel: return IsFront(i) ? 0f : 0.5f;
                case DriveLayout.FrontWheel: return IsFront(i) ? 0.5f : 0f;
                default: return (IsFront(i) ? 1f - config.rearDriveShare : config.rearDriveShare) * 0.5f;
            }
        }

        /// <summary>Çekiş tekerlerinin ortalama çevre hızı (m/s); motor torku eğrisinin girdisi.</summary>
        float DrivenWheelSpeed()
        {
            float sum = 0f, wsum = 0f;
            for (int i = 0; i < 4; i++)
            {
                float s = DriveShare(i);
                if (s <= 0f) continue;
                sum += wheels[i].rpm * 2f * Mathf.PI / 60f * wheels[i].radius * s;
                wsum += s;
            }
            return wsum > 0f ? sum / wsum : 0f;
        }

        void AntiRoll(int left, int right)
        {
            var wl = wheels[left]; var wr = wheels[right];
            float travelL = 1f, travelR = 1f;
            bool gl = wl.GetGroundHit(out var hl), gr = wr.GetGroundHit(out var hr);
            if (gl) travelL = (-wl.transform.InverseTransformPoint(hl.point).y - wl.radius) / wl.suspensionDistance;
            if (gr) travelR = (-wr.transform.InverseTransformPoint(hr.point).y - wr.radius) / wr.suspensionDistance;
            float f = (travelL - travelR) * config.antiRoll;
            if (gl) Body.AddForceAtPosition(wl.transform.up * -f, wl.transform.position);
            if (gr) Body.AddForceAtPosition(wr.transform.up * f, wr.transform.position);
        }

        void LateUpdate()
        {
            // Görsel tekerlekler: fizik pozunu gövdeye göre yerelleştirip interpolasyonlu gövdeye bağla (titremesin).
            if (Body == null) return;
            var invRot = Quaternion.Inverse(Body.rotation);
            for (int i = 0; i < 4; i++)
            {
                if (wheelVisuals[i] == null || wheels[i] == null) continue;
                wheels[i].GetWorldPose(out Vector3 pos, out Quaternion rot);
                Vector3 local = invRot * (pos - Body.position);
                Quaternion lrot = invRot * rot;
                wheelVisuals[i].SetPositionAndRotation(transform.TransformPoint(local), transform.rotation * lrot);
            }
        }

        internal void Bind(WheelCollider[] w, Transform[] visuals)
        {
            wheels = w;
            wheelVisuals = visuals;
        }
    }
}
