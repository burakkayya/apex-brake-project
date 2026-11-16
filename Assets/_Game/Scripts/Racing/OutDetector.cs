using UnityEngine;
using ApexBrake.Track;

namespace ApexBrake.Racing
{
    /// <summary>
    /// Tek oyun kuralının ölçümü (spec §12.2): araç yol dışındaki zemine değiyor, boşluğa düştü ya da ters/yan döndü mü?
    /// Hız ya da konum eşiği yoktur; yalnızca temas, yükseklik ve yönelim kesinleşme süreleriyle değerlendirilir.
    /// </summary>
    public sealed class OutDetector
    {
        /// <summary>Ground temasının kesintisiz sürmesi gereken süre.</summary>
        public const float OffRoadTime = 0.4f;
        /// <summary>Gövde merkezinin izdüşümdeki yol yüzeyinin altında olması gereken derinlik.</summary>
        public const float FellDepth = 3f;
        public const float FlipUpDot = 0.3f;
        public const float FlipTime = 2f;

        float _offRoad, _flip;

        public float OffRoadTimer => _offRoad;
        public float FlipTimer => _flip;

        public void Reset() { _offRoad = 0f; _flip = 0f; }

        /// <summary>Bir fizik adımı ilerletir; kesinleşen sonucu döndürür (yoksa None).</summary>
        /// <param name="roadBelow">gövde merkezinin altında Road yüzeyi var mı</param>
        public OutReason Step(float dt, VehicleTelemetry tel, float heightAboveRoad, bool roadBelow)
        {
            if (heightAboveRoad < -FellDepth) return OutReason.Fell;

            bool airborne = tel.GroundedWheels == 0 && !tel.BodyOnSurface;   // hiçbir teker ve gövde hiçbir yüzeye değmiyor

            // Yol dışı: en az iki teker (ya da gövde) Ground'a değiyor ve altında yol yok; kesintisiz.
            bool offRoad = (tel.GroundContactWheels >= 2 || tel.BodyOnGround) && !roadBelow;
            _offRoad = offRoad ? _offRoad + dt : 0f;
            if (_offRoad >= OffRoadTime - 1e-4f) return OutReason.OffRoad;

            // Ters/yan: havadayken sayaç bekler (sıfırlanmaz da artmaz da).
            if (tel.UpDot < FlipUpDot) { if (!airborne) _flip += dt; }
            else _flip = 0f;
            if (_flip >= FlipTime - 1e-4f) return OutReason.Flipped;

            return OutReason.None;
        }

        /// <summary>Gövde merkezinin altında (30 m'ye kadar) Road yüzeyi var mı.</summary>
        public static bool RoadBelow(Vector3 com) =>
            Physics.Raycast(com + Vector3.up * 1f, Vector3.down, 31f, TrackLayers.RoadMask, QueryTriggerInteraction.Ignore);
    }
}
