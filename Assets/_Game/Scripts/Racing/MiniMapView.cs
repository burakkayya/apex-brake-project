using UnityEngine;

namespace ApexBrake.Racing
{
    /// <summary>Sol kenarda dikey ilerleme çubuğu: altı start, üstü bitiş. Her yarışçı bir nokta; oyuncu noktası en üstte.</summary>
    public sealed class MiniMapView : MonoBehaviour
    {
        public RaceController controller;
        public RectTransform bar;                        // noktaların ebeveyni (yükseklik = ilerleme ekseni)
        public UnityEngine.UI.Image[] dots;              // indeks = yarışçı; oyuncu noktası (0) hiyerarşide en sonda
        public float eliminatedAlpha = 0.35f;
        public float laneSpacing = 16f;                  // px; noktalar ev şeridine göre yana kayar, yakın araçlar üst üste binmesin

        Color[] _baseColors;
        RaceController _subscribed;

        void Awake()
        {
            _baseColors = new Color[dots != null ? dots.Length : 0];
            for (int i = 0; i < _baseColors.Length; i++) _baseColors[i] = dots[i] != null ? dots[i].color : Color.white;
        }

        void OnEnable() => Subscribe();
        void Start() { Subscribe(); ResetView(); }

        void OnDisable()
        {
            if (_subscribed == null) return;
            _subscribed.Restarted -= ResetView;
            _subscribed = null;
        }

        void Subscribe()
        {
            if (controller == null || _subscribed == controller) return;
            OnDisable();
            controller.Restarted += ResetView;
            _subscribed = controller;
        }

        void ResetView()
        {
            if (dots == null) return;
            for (int i = 0; i < dots.Length; i++)
            {
                if (dots[i] == null) continue;
                dots[i].color = _baseColors[i];
                if (dots[i].transform.parent != bar && dots[i].transform.parent.TryGetComponent(out UnityEngine.UI.Image ring))
                {
                    var rc = ring.color; rc.a = 1f; ring.color = rc;
                }
                SetProgress(dots[i].rectTransform, 0f, 0f);
            }
            if (dots.Length > RaceController.PlayerIndex && dots[RaceController.PlayerIndex] != null)
            {
                var t = dots[RaceController.PlayerIndex].transform;
                (t.parent != bar ? t.parent : t).SetAsLastSibling();   // oyuncu noktası en üstte çizilir
            }
        }

        void LateUpdate()
        {
            var c = controller;
            if (c == null || c.Race == null || c.track == null || c.track.Layout == null || dots == null) return;
            Subscribe();
            float total = c.track.Layout.TotalLength;
            float alpha = c.State == RaceState.Racing ? c.Clock.Alpha : 1f;
            int n = Mathf.Min(dots.Length, c.Race.Racers.Count);
            for (int i = 0; i < n; i++)
            {
                if (dots[i] == null) continue;
                var r = c.Race.Racers[i];
                float d = Mathf.Lerp(c.PrevDistance(i), r.Car.Distance, alpha);
                SetProgress(dots[i].rectTransform, total > 0f ? d / total : 0f, (r.HomeLane - 2) * laneSpacing);
                var col = _baseColors[i];
                if (r.Car.IsOut) col.a *= eliminatedAlpha;
                if (dots[i].color != col)
                {
                    dots[i].color = col;
                    // Oyuncunun beyaz çerçevesi de aynı saydamlığı alır.
                    if (dots[i].transform.parent != bar && dots[i].transform.parent.TryGetComponent(out UnityEngine.UI.Image ring))
                    {
                        var rc = ring.color; rc.a = r.Car.IsOut ? eliminatedAlpha : 1f; ring.color = rc;
                    }
                }
            }
        }

        // Nokta (ya da oyuncu noktasının çerçevesi) çubuk boyunca konumlanır.
        void SetProgress(RectTransform dot, float t, float x)
        {
            var target = dot.parent != bar && dot.parent is RectTransform p ? p : dot;
            t = Mathf.Clamp01(t);
            target.anchorMin = target.anchorMax = new Vector2(0.5f, t);
            target.anchoredPosition = new Vector2(x, 0f);
        }
    }
}
