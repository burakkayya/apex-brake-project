using TMPro;
using UnityEngine;

namespace ApexBrake.Racing
{
    /// <summary>
    /// Geri sayım, sıra, hız (km/s), geri bildirim metni (KAYIYORSUN / HAVADA / TEMAS), sollama göstergesi ve fren şeridi.
    /// Yalnızca sunumdur: telemetriyi ve RaceManager olaylarını okur. Güvenli alanı da uygular.
    /// </summary>
    public sealed class RaceHud : MonoBehaviour
    {
        public RaceManager race;
        public TouchBrakeInput input;
        public RectTransform safeArea;            // tüm HUD öğelerinin ebeveyni; Screen.safeArea'ya oturtulur
        public TMP_Text countdownText;
        public TMP_Text rankText;
        public TMP_Text speedText;
        public TMP_Text feedbackText;
        public TMP_Text overtakeText;             // sollarken SOLLAMA
        public UnityEngine.UI.Image brakeBar;

        public const float GoDuration = 0.7f;
        public const float ContactDuration = 0.9f;
        public const string SlideText = "KAYIYORSUN";
        public const string AirText = "HAVADA";
        public const string ContactText = "TEMAS";

        RaceManager _subscribed;
        Rect _lastSafe;
        Vector2Int _lastScreen;
        RaceState _lastState = RaceState.Ended;
        float _goTimer;
        float _contactTimer;
        float _slideHold;                         // kayma kısa kesilince metin titremesin
        string _shownFeedback = "";
        bool _shownOvertake;
        int _shownCount = -1, _shownRank = -1, _shownRacers = -1, _shownSpeed = -1;

        public string FeedbackText => _shownFeedback;

        void OnEnable() => Subscribe();
        void Start() { Subscribe(); ResetView(); }

        void OnDisable()
        {
            if (_subscribed == null) return;
            _subscribed.Restarted -= ResetView;
            _subscribed.RacerContact -= OnContact;
            _subscribed = null;
        }

        void Subscribe()
        {
            if (race == null || _subscribed == race) return;
            OnDisable();
            race.Restarted += ResetView;
            race.RacerContact += OnContact;
            _subscribed = race;
        }

        void OnContact(int a, int b, float v)
        {
            if (a == RaceManager.PlayerIndex || b == RaceManager.PlayerIndex) _contactTimer = ContactDuration;
        }

        void ResetView()
        {
            _goTimer = 0f; _contactTimer = 0f; _slideHold = 0f;
            _shownOvertake = false;
            if (overtakeText != null) { overtakeText.text = "SOLLAMA"; overtakeText.enabled = false; }
            _shownCount = _shownRank = _shownRacers = _shownSpeed = -1;
            _lastState = race != null ? race.State : RaceState.Countdown;
            SetFeedback("");
            if (countdownText != null) countdownText.text = "";
            if (brakeBar != null) brakeBar.enabled = false;
        }

        void SetFeedback(string text)
        {
            if (feedbackText == null || text == _shownFeedback) return;
            _shownFeedback = text;
            feedbackText.text = text;
        }

        void Update()
        {
            ApplySafeArea();
            var c = race;
            if (c == null || c.Racers.Count == 0) return;
            Subscribe();
            float dt = Time.unscaledDeltaTime;

            // Geri sayım: 3, 2, 1, sonra 0,7 sn BASLA
            if (c.State != RaceState.Countdown && _lastState == RaceState.Countdown) _goTimer = GoDuration;
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

            var player = c.Player;
            int racers = c.Racers.Count;
            if (rankText != null && (player.Rank != _shownRank || racers != _shownRacers))
            {
                _shownRank = player.Rank; _shownRacers = racers;
                rankText.text = player.Rank + " / " + racers;
            }

            var tel = player.Telemetry;
            int speed = tel != null ? Mathf.RoundToInt(tel.SpeedKmh) : 0;
            if (speedText != null && speed != _shownSpeed)
            {
                _shownSpeed = speed;
                speedText.text = speed + " km/s";
            }

            // Geri bildirim: havada > kayma > temas
            bool active = c.State == RaceState.Racing && !player.IsOut && !player.Finished;
            if (_contactTimer > 0f) _contactTimer -= dt;
            if (active && tel != null && tel.IsSliding) _slideHold = 0.35f; else _slideHold -= dt;
            string fb = "";
            if (active && tel != null && tel.IsAirborne) fb = AirText;
            else if (active && _slideHold > 0f) fb = SlideText;
            else if (_contactTimer > 0f && !player.IsOut) fb = ContactText;
            SetFeedback(fb);
            if (feedbackText != null && fb == ContactText) feedbackText.alpha = Mathf.Clamp01(_contactTimer / ContactDuration * 2.5f);
            else if (feedbackText != null) feedbackText.alpha = 1f;

            if (overtakeText != null)
            {
                bool show = active && player.IsOvertaking;
                if (show != _shownOvertake) { _shownOvertake = show; overtakeText.enabled = show; }
            }

            if (brakeBar != null)
                brakeBar.enabled = c.State != RaceState.Ended && input != null && input.IsBraking;
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
