using System;
using System.Collections;
using UnityEngine;
using ApexBrake.Racing;
using ApexBrake.Track;

namespace ApexBrake.Tests.PlayMode
{
    /// <summary>Tek araçlı fizik testi düzeneği: pist + araç + hızlandırılmış zaman. Test sonunda zaman ayarlarını geri yükler.</summary>
    internal sealed class CarTestRig : IDisposable
    {
        public readonly TestTrackKit Kit = new TestTrackKit();
        public TrackBuilder Track;
        public VehicleMotor Motor;
        public LineDriver Driver;
        public VehicleTelemetry Tel;
        public VehicleConfig Config;
        public VehicleUpgrades Upgrades = VehicleUpgrades.Base;

        readonly float _timeScale, _maxDt, _fixedDt;
        readonly int _targetFps, _vsync;

        public const float FastTimeScale = 6f;

        public CarTestRig(float timeScale = FastTimeScale)
        {
            _timeScale = Time.timeScale; _maxDt = Time.maximumDeltaTime; _fixedDt = Time.fixedDeltaTime;
            _targetFps = Application.targetFrameRate; _vsync = QualitySettings.vSyncCount;
            Time.fixedDeltaTime = 0.02f;
            Time.maximumDeltaTime = 1f;
            Time.timeScale = timeScale;
        }

        public void BuildTrack(params TrackPieceAsset[] pieces) => Track = Kit.Build(pieces);

        public VehicleMotor SpawnCar(float distance, float lateral, float speed = 0f, VehicleConfig config = null)
        {
            Config = config != null ? config : Kit.Track(ScriptableObject.CreateInstance<VehicleConfig>());
            Motor = VehicleBuilder.Create(Config, Color.red, "TestCar");
            Kit.Track(Motor.gameObject);
            Driver = Motor.GetComponent<LineDriver>();
            Tel = Motor.GetComponent<VehicleTelemetry>();
            var path = Track.Path;
            var rot = Quaternion.LookRotation(Flat(path.ForwardAt(distance)), Vector3.up);
            Motor.PlaceAt(path.PointAt(distance, lateral), rot);
            Driver.Path = path;
            Driver.TargetLateral = lateral;
            Motor.EngineOn = true;
            Motor.Apply(Upgrades);
            if (speed > 0f) Motor.Body.linearVelocity = rot * Vector3.forward * speed; // test başlangıç koşulu
            return Motor;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.normalized; }

        public float Speed => Motor.Body.linearVelocity.magnitude;
        public TrackPose Pose => Track.Path.Project(Motor.Body.position, Driver.Distance);
        public bool OnGround => Tel.TouchesGround;

        /// <summary>Fizik adımlarını yürütür (her adımdan sonra <paramref name="step"/>); süre dolunca ya da <paramref name="until"/> true olunca durur.</summary>
        public IEnumerator Run(float seconds, Func<bool> until = null, Action step = null)
        {
            int steps = Mathf.CeilToInt(seconds / 0.02f);
            for (int i = 0; i < steps; i++)
            {
                yield return new WaitForFixedUpdate();
                step?.Invoke();
                if (until != null && until()) yield break;
            }
        }

        public void Dispose()
        {
            Time.timeScale = _timeScale;
            Time.maximumDeltaTime = _maxDt;
            Time.fixedDeltaTime = _fixedDt;
            Application.targetFrameRate = _targetFps;
            QualitySettings.vSyncCount = _vsync;
            Kit.Dispose();
        }
    }
}
