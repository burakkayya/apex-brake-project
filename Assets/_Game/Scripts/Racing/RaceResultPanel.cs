using System.Globalization;
using TMPro;
using UnityEngine;

namespace ApexBrake.Racing
{
    /// <summary>Yarış bitince (Ended) açılır: sıra ya da ELENDIN, süre, 5 satır sıralama (durum: Bitti / Yol disi / Takla / Dustu / Yarista), TEKRAR.</summary>
    public sealed class RaceResultPanel : MonoBehaviour
    {
        public RaceManager race;
        public GameObject panel;                 // açılıp kapanan kök (tam ekran, dokunuşları yutar)
        public TMP_Text titleText;
        public TMP_Text timeText;
        public TMP_Text[] rowRank;               // 5 satır: sıra
        public TMP_Text[] rowName;               //          ad
        public TMP_Text[] rowStatus;             //          durum
        public UnityEngine.UI.Button retryButton;
        public Color playerRowColor = new Color(1f, 0.85f, 0.3f);
        public Color rowColor = Color.white;

        RaceManager _subscribed;

        public bool IsOpen => panel != null && panel.activeSelf;

        void Awake()
        {
            if (retryButton != null) retryButton.onClick.AddListener(OnRetry);
        }

        void OnEnable() => Subscribe();

        void Start()
        {
            Subscribe();
            if (race != null && race.State == RaceState.Ended) Show(); else Hide();
        }

        void OnDisable()
        {
            if (_subscribed == null) return;
            _subscribed.Ended -= Show;
            _subscribed.Restarted -= Hide;
            _subscribed = null;
        }

        void Subscribe()
        {
            if (race == null || _subscribed == race) return;
            OnDisable();
            race.Ended += Show;
            race.Restarted += Hide;
            _subscribed = race;
        }

        void OnRetry()
        {
            if (race != null) race.Restart();   // Restarted → Hide
            Hide();
        }

        void Hide()
        {
            if (panel != null) panel.SetActive(false);
        }

        public void Show()
        {
            var c = race;
            if (c == null || c.Racers.Count == 0 || panel == null) return;
            var racers = c.Racers;
            var player = c.Player;
            var inv = CultureInfo.InvariantCulture;

            titleText.text = player.Finished ? player.Rank + "." : "ELENDIN";
            timeText.text = c.ElapsedTime.ToString("0.0", inv) + " sn";

            int rows = rowName != null ? rowName.Length : 0;
            for (int row = 0; row < rows; row++)
            {
                RacerEntry e = null;
                for (int i = 0; i < racers.Count; i++)
                    if (racers[i].Rank == row + 1) { e = racers[i]; break; }
                bool has = e != null;
                var col = has && e.IsPlayer ? playerRowColor : rowColor;
                SetRow(rowRank, row, has ? (row + 1) + "." : "", col);
                SetRow(rowName, row, has ? e.Name : "", col);
                SetRow(rowStatus, row, has ? Status(e) : "", col);
            }
            panel.SetActive(true);
        }

        public static string Status(RacerEntry e)
        {
            if (e.Finished) return "Bitti";
            if (!e.IsOut) return "Yarista";
            switch (e.OutReason)
            {
                case OutReason.Flipped: return "Takla";
                case OutReason.Fell: return "Dustu";
                default: return "Yol disi";
            }
        }

        static void SetRow(TMP_Text[] texts, int row, string value, Color color)
        {
            if (texts == null || row >= texts.Length || texts[row] == null) return;
            texts[row].text = value;
            texts[row].color = color;
        }
    }
}
