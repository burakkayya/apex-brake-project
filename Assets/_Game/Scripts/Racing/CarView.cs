using UnityEngine;
using Unity.Cinemachine;
using ApexBrake.Simulation;

namespace ApexBrake.Racing
{
    /// <summary>CarController'ı dinler; aracı çizgi üzerinde konumlar ve görsel durumları (uçuş, savrulma, çakılma) çizer.</summary>
    public sealed class CarView : MonoBehaviour
    {
        public CarController controller;
        public Transform body;
        public CinemachineCamera vcam;
        public float baseHeight = 0.5f;
        public float fovMin = 60f;
        public float fovMax = 68f;

        void LateUpdate()
        {
            var c = controller;
            if (c == null || c.Sim == null || c.track == null || c.track.Path == null) return;
            var sim = c.Sim;

            float d = Mathf.Lerp(c.PrevDistance, sim.Distance, c.Clock.Alpha);
            c.track.Path.Evaluate(d, out Vector3 pos, out Quaternion rot);
            rot = Quaternion.Euler(0f, rot.eulerAngles.y, 0f);
            transform.SetPositionAndRotation(pos + Vector3.up * baseHeight, rot);

            float phaseTime = sim.PhaseTime + c.Clock.Alpha * CarSim.Step;
            Vector3 localPos = Vector3.zero;
            Quaternion localRot = Quaternion.identity;

            switch (sim.Phase)
            {
                case CarPhase.Airborne:
                {
                    var def = c.track.Layout.SpanAtDistance(sim.Distance).Def;
                    float flight = def.GapLength / Mathf.Max(sim.Speed, 1f);
                    float t = Mathf.Clamp(phaseTime, 0f, flight);
                    localPos.y = 0.5f * c.Tuning.Gravity * t * (flight - t);
                    localRot = Quaternion.Euler(-8f * Mathf.Sin(Mathf.PI * t / Mathf.Max(flight, 0.01f)), 0f, 0f);
                    break;
                }
                case CarPhase.Skidding:
                {
                    float yaw = 25f * Mathf.Sin(phaseTime / Mathf.Max(c.Tuning.SkidDuration, 0.01f) * Mathf.PI * 4f);
                    localRot = Quaternion.Euler(0f, yaw, 0f);
                    break;
                }
                case CarPhase.Crashed:
                {
                    float t = phaseTime;
                    localPos = new Vector3(Mathf.Min(t, 1f) * 7f, Mathf.Max(0f, 5f * t - 6f * t * t), Mathf.Min(t, 1f) * 4f);
                    localRot = Quaternion.Euler(Mathf.Min(t, 1.2f) * 540f, 0f, Mathf.Min(t, 1.2f) * 360f);
                    break;
                }
            }

            if (body != null)
            {
                body.localPosition = localPos;
                body.localRotation = localRot;
            }

            if (vcam != null)
            {
                float k = Mathf.Clamp01(sim.Speed / 30f);
                var lens = vcam.Lens;
                lens.FieldOfView = Mathf.Lerp(fovMin, fovMax, k);
                vcam.Lens = lens;
            }
        }
    }
}
