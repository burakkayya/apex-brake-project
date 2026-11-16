using UnityEngine;

namespace ApexBrake.Racing
{
    public enum DriveLayout { RearWheel, AllWheel, FrontWheel }

    /// <summary>
    /// Aracın yalnızca fiziksel değerleri. Davranış (hızlanma, fren, kayma, uçma, takla) bunlardan ve
    /// pist geometrisinden doğar; hiçbir hız eşiği yoktur.
    /// </summary>
    [CreateAssetMenu(menuName = "Apex Brake/Vehicle Config")]
    public sealed class VehicleConfig : ScriptableObject
    {
        [Header("Govde")]
        public float mass = 1000f;                          // kg
        [Tooltip("Ağırlık merkezi: teker temas düzleminin üstünde (m)")]
        public float centerOfMassHeight = 0.30f;
        [Tooltip("Ağırlık merkezi: dingil ortasından ileri (m)")]
        public float centerOfMassForward = 0f;
        public Vector3 bodySize = new Vector3(1.8f, 0.5f, 4.1f);
        public float bodyGroundClearance = 0.3f;            // gövde kutusunun alt yüzü (dinlenmede, m)
        public Vector3 cabinSize = new Vector3(1.45f, 0.45f, 2.0f);
        [Tooltip("Gövdenin (sac) zemine sürtünmesi; kayan/devrilen gövde için")]
        public float bodyFriction = 0.35f;

        [Header("Tekerlek")]
        public float wheelBase = 2.6f;
        public float trackWidth = 1.7f;
        public float wheelRadius = 0.34f;
        public float wheelMass = 20f;

        [Header("Motor")]
        [Tooltip("Tekerleklere toplam itiş kuvveti, düşük hızda (N)")]
        public float engineForce = 11500f;
        [Tooltip("Motor gücünün bittiği hız (m/s): itiş bu hıza yaklaştıkça azalır, burada sıfırdır")]
        public float topSpeed = 43f;
        [Tooltip("İtiş eğrisi üssü: F = engineForce * (1 - (v/topSpeed)^n)")]
        public float torqueCurveExponent = 2f;
        public DriveLayout drive = DriveLayout.AllWheel;
        [Range(0f, 1f), Tooltip("Dört çekerde arka dingile giden pay")]
        public float rearDriveShare = 0.7f;

        [Header("Fren")]
        [Tooltip("Dört tekerde toplam fren kuvveti (N)")]
        public float brakeForce = 26000f;
        [Range(0f, 1f)] public float frontBrakeShare = 0.75f;
        [Tooltip("Kilitlenen tekerin fren torkunu kısar (yalnızca tork sınırlar)")]
        public bool abs = true;
        public float absSlip = 0.3f;
        [Tooltip("Motor kapalıyken (geri sayım) tekerleri tutan tork (Nm)")]
        public float holdBrakeTorque = 4000f;

        [Header("Hava")]
        [Tooltip("Hava direnci F = c v^2 (N)")]
        public float dragCoefficient = 0.35f;
        [Tooltip("Aşağı bastırma F = k v^2 (N)")]
        public float downforceCoefficient = 5f;
        [Tooltip("Yuvarlanma direnci katsayısı (ağırlığın oranı)")]
        public float rollingResistance = 0.012f;

        [Header("Suspansiyon")]
        public float suspensionDistance = 0.3f;
        public float spring = 30000f;
        public float damper = 3500f;
        [Range(0f, 1f)] public float targetPosition = 0.3f;
        [Tooltip("Viraj denge çubuğu (N / tam sıkışma farkı)")]
        public float antiRoll = 3000f;

        [Header("Lastik (ileri)")]
        public float fwdExtremumSlip = 0.4f;
        public float fwdExtremumValue = 1f;
        public float fwdAsymptoteSlip = 0.8f;
        public float fwdAsymptoteValue = 0.65f;
        public float fwdStiffness = 1.3f;

        [Header("Lastik (yanal)")]
        public float sideExtremumSlip = 0.2f;
        public float sideExtremumValue = 1f;
        public float sideAsymptoteSlip = 0.5f;
        public float sideAsymptoteValue = 0.75f;
        public float frontSideStiffness = 1.5f;
        public float rearSideStiffness = 1.45f;

        [Header("Direksiyon")]
        public float maxSteerAngle = 30f;                   // derece
        public float steerRate = 150f;                      // derece / sn

        [Header("Yercekimi")]
        [Tooltip("Araca uygulanan yerçekimi çarpanı (ek kuvvet olarak; arcade ağırlık hissi)")]
        public float gravityMultiplier = 2.5f;
    }

    /// <summary>Yükseltmeler: 1 = taban. Motor kuvvetine, hız tavanına ve lastik sürtünmesine çarpan olarak uygulanır.</summary>
    public struct VehicleUpgrades
    {
        public float Engine, TopSpeed, Tire;
        public static VehicleUpgrades Base => new VehicleUpgrades { Engine = 1f, TopSpeed = 1f, Tire = 1f };
    }
}
