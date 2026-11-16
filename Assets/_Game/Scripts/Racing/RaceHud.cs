using TMPro;
using UnityEngine;
using ApexBrake.Simulation;

namespace ApexBrake.Racing
{
    /// <summary>Geri sayım, sıra, hız, geri bildirim metni ve fren göstergesi. Güvenli alanı da uygular.</summary>
    public sealed class RaceHud : MonoBehaviour
    {
        public RaceController controller;
        public RectTransform safeArea;            // tüm HUD öğelerinin ebeveyni; Screen.safeArea'ya oturtulur
        public TMP_Text countdownText;
        public TMP_Text rankText;
        public TMP_Text speedText;
        public TMP_Text feedbackText;
        public UnityEngine.UI.Image brakeBar;

        public const float GoDuration = 0.7f;
        public const float FeedbackDuration = 0.9f;

        RaceController _subscribed;
        Rect _lastSafe;
        Vector2Int _lastScreen;
        RaceState _lastState = RaceState.Ended;
        float _goTimer;                // > 0 iken BASLA görünür
        float _feedbackTimer;          // > 0 iken geri bildirim görünür
        int _shownCount = -1, _shownRank = -1, _shownRacers = -1, _shownSpeed = -1;

        public string FeedbackText => feedbackText != null && _feedbackTimer > 0f ? feedbackText.text : "";

        void OnEnable() => Subscribe();
        void Start() { Subscribe(); ResetView(); }

        void OnDisable()
        {
            if (_subscribed == null) return;
            _subscribed.RacerEvent -= OnRacerEvent;
            _subscribed.Restarted -= ResetView;
            _subscribed = null;
        }

        void Subscribe()
        {
            if (controller == null || _subscribed == controller) return;
            OnDisable();
            controller.RacerEvent += OnRacerEvent;
            controller.Restarted += ResetView;
            _subscribed = controller;
        }

        void ResetView()
        {
            _goTimer = 0f;
            _feedbackTimer = 0f;
            _shownCount = _shownRank = _shownRacers = _shownSpeed = -1;
            _lastState = controller != null ? controller.State : RaceState.Countdown;
            if (feedbackText != null) { feedbackText.text = ""; feedbackText.alpha = 0f; }
            if (countdownText != null) countdownText.text = "";
            if (brakeBar != null) brakeBar.enabled = false;
        }

        void OnRacerEvent(int index, SimEvent e)
        {
            if (index != RaceController.PlayerIndex) return;
            string msg = null;
            switch (e.Kind)
            {
                case SimEventKind.PerfectBrake: msg = "PERFECT!"; break;
                case SimEventKind.GoodBrake: msg = "GOOD"; break;
                case SimEventKind.Skid: msg = "SAVRULDUN"; break;
                case SimEventKind.RearHit:
                case SimEventKind.SideHit: msg = "TEMAS"; break;
            }
            if (msg == null || feedbackText == null) return;
            feedbackText.text = msg;
            _feedbackTimer = FeedbackDuration;
        }

        void Update()
        {
            ApplySafeArea();
            var c = controller;
            if (c == null || c.Race == null) return;
            Subscribe();
            float dt = Time.unscaledDeltaTime;

            // Geri sayım: 3, 2, 1, sonra 0,7 sn BASLA
            if (c.State == RaceState.Racing && _lastState == RaceState.Countdown) _goTimer = GoDuration;
            _lastState = c.State;
            if (countdownText != null)
            {
                if (c.State == RaceState.Countdown)
                {
                    int n = Mathf.Clamp(Mathf.CeilToInt(c.CountdownRemaining), 1, 3);
                    if (n != _shownCount) { _shownCount = n; countdownText.text = n.ToString(); }
                }
                else if (_goTimer > 0f)
                {
                    _goTimer -= dt;
                    if (_shownCount != 0) { _shownCount = 0; countdownText.text = "BASLA"; }
                    if (_goTimer <= 0f) { countdownText.text = ""; _shownCount = -2; }
                }
                else if (_shownCount != -2) { _shownCount = -2; countdownText.text = ""; }
            }

            var player = c.Race.Racers[RaceController.PlayerIndex];
            int racers = c.Race.Racers.Count;
            if (rankText != null && (player.Rank != _shownRank || racers != _shownRacers))
            {
                _shownRank = player.Rank; _shownRacers = racers;
                rankText.text = player.Rank + " / " + racers;
            }

            int speed = Mathf.RoundToInt(player.Car.Speed);
            if (speedText != null && speed != _shownSpeed)
            {
                _shownSpeed = speed;
                speedText.text = speed + " m/s";
            }

            if (feedbackText != null)
            {
                if (_feedbackTimer > 0f) _feedbackTimer -= dt;
                float k = Mathf.Clamp01(_feedbackTimer / FeedbackDuration);
                feedbackText.alpha = Mathf.Clamp01(k * 2.5f);   // ilk %60 tam görünür, sonra solar
                if (_feedbackTimer <= 0f && feedbackText.text.Length > 0) feedbackText.text = "";
            }

            if (brakeBar != null)
                brakeBar.enabled = c.State != RaceState.Ended && c.input != null && c.input.IsBraking;
        }

        void ApplySafeArea()
        {
            if (safeArea == null) return;
            var safe = Screen.safeArea;
            var screen = new Vector2Int(Screen.width, Screen.height);
            if (safe == _lastSafe && screen == _lastScreen) return;
            _lastSafe = safe; _lastScreen = screen;
            if (screen.x <= 0 || screen.y <= 0) return;
            safeArea.anchorMin = new Vector2(safe.xMin / screen.x, safe.yMin / screen.y);
            safeArea.anchorMax = new Vector2(safe.xMax / screen.x, safe.yMax / screen.y);
            safeArea.offsetMin = Vector2.zero;
            safeArea.offsetMax = Vector2.zero;
        }
    }
}
