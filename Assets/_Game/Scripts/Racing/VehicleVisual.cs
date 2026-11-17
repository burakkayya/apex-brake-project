using UnityEngine;

namespace ApexBrake.Racing
{
    /// <summary>
    /// Aracın sunum katmanı (sonucu etkilemez): frende stop lambaları kırmızı yanar; kayarken lastiklerin altından duman
    /// çıkar. Gövde fizik gövdesinin kendisidir; tekerlekleri VehicleMotor döndürür ve süspansiyonla oynatır.
    /// </summary>
    [RequireComponent(typeof(VehicleMotor))]
    public sealed class VehicleVisual : MonoBehaviour
    {
        [SerializeField] Renderer[] brakeLights = new Renderer[0];
        [SerializeField] ParticleSystem[] tireSmoke = new ParticleSystem[0];   // 0..3: FL, FR, RL, RR
        public Color lightOff = new Color(0.35f, 0.04f, 0.04f);
        public Color lightOn = new Color(1f, 0.08f, 0.05f);
        public float lightOnEmission = 3f;
        [Tooltip("Duman: kayma açısı bu dereceden sonra tam yoğunluk")]
        public float smokeFullSlip = 18f;
        public float smokeRate = 60f;               // parçacık / sn (tam yoğunlukta, teker başına)

        VehicleMotor _motor;
        VehicleTelemetry _tel;
        Material[] _mats;
        bool _lit;
        bool _init;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        public bool BrakeLightsOn => _lit;

        internal void Bind(Renderer[] lights, ParticleSystem[] smoke) { brakeLights = lights; tireSmoke = smoke; }

        void Awake()
        {
            _motor = GetComponent<VehicleMotor>();
            _tel = GetComponent<VehicleTelemetry>();
            // Lamba başına örnek malzeme, ışıma açık (kayıtlı varlıkta anahtar kelime korunmuyor).
            _mats = new Material[brakeLights.Length];
            for (int i = 0; i < brakeLights.Length; i++)
            {
                if (brakeLights[i] == null) continue;
                _mats[i] = brakeLights[i].material;
                _mats[i].EnableKeyword("_EMISSION");
                _mats[i].globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
        }

        void Update()
        {
            bool lit = _motor.Brake && _motor.EngineOn;
            if (lit != _lit || !_init) { _lit = lit; _init = true; SetLights(lit); }

            float slip = _tel != null ? Mathf.Abs(_tel.SlipAngleDeg) : 0f;
            float speed = _tel != null ? _tel.SpeedKmh : 0f;
            for (int i = 0; i < tireSmoke.Length && i < 4; i++)
            {
                var ps = tireSmoke[i];
                if (ps == null) continue;
                var w = _motor.Wheels[i];
                WheelHit hit = default;
                bool grounded = w != null && w.GetGroundHit(out hit);
                float k = 0f;
                if (grounded && speed > 15f)
                {
                    // Yanal kayma (drift) ya da kilitlenen/patinaj yapan teker (ileri kayma) duman çıkarır.
                    float side = Mathf.InverseLerp(6f, smokeFullSlip, slip);
                    float fwd = Mathf.InverseLerp(0.35f, 0.8f, Mathf.Abs(hit.forwardSlip));
                    k = Mathf.Max(side, fwd);
                    if (VehicleMotor.IsFront(i)) k *= 0.5f;
                    ps.transform.position = hit.point + Vector3.up * 0.1f;
                }
                var em = ps.emission;
                em.rateOverTime = k * smokeRate;
            }
        }

        void SetLights(bool on)
        {
            foreach (var m in _mats)
            {
                if (m == null) continue;
                m.SetColor(BaseColor, on ? lightOn : lightOff);
                m.SetColor(EmissionColor, on ? lightOn * lightOnEmission : Color.black);
            }
        }
    }
}
