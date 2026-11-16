using UnityEngine;

namespace ApexBrake.Racing
{
    /// <summary>Araçlar arası çarpışmayı RaceManager'a bildirir (yalnızca HUD/kamera için; sonucu fizik verir).</summary>
    public sealed class RacerContactRelay : MonoBehaviour
    {
        internal RaceManager Manager;
        internal int Index;

        void OnCollisionEnter(Collision c)
        {
            if (Manager == null || c.rigidbody == null) return;
            var other = c.rigidbody.GetComponent<RacerContactRelay>();
            if (other == null || other.Manager != Manager || other.Index <= Index) return;   // çift başına bir kez
            Manager.ReportContact(Index, other.Index, c.relativeVelocity.magnitude);
        }
    }
}
