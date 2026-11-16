using System.Globalization;
using TMPro;
using UnityEngine;
using ApexBrake.Simulation;

namespace ApexBrake.Racing
{
    /// <summary>Yarış bitince (Ended) açılır: sıra ya da ELENDIN, süre, Perfect/Good, 5 satır sıralama, TEKRAR düğmesi.</summary>
    public sealed class RaceResultPanel : MonoBehaviour
    {
        public RaceController controller;
        public GameObject panel;                 // açılıp kapanan kök (tam ekran, dokunuşları yutar)
        public TMP_Text titleText;
        public TMP_Text timeText;
        public TMP_Text countsText;
        public TMP_Text[] rowRank;               // 5 satır: sıra
        public TMP_Text[] rowName;               //          ad
        public TMP_Text[] rowStatus;             //          durum
        public UnityEngine.UI.Button retryButton;
        public Color playerRowColor = new Color(1f, 0.85f, 0.3f);
        public Color rowColor = Color.white;

        RaceController _subscribed;

        public bool IsOpen => panel != null && panel.activeSelf;

        void Awake()
        {
            if (retryButton != null) retryButton.onClick.AddListener(OnRetry);
        }

        void OnEnable() => Subscribe();

        void Start()
        {
            Subscribe();
            if (controller != null && controller.State == RaceState.Ended) Show(); else Hide();
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
            if (controller == null || _subscribed == controller) return;
            OnDisable();
            controller.Ended += Show;
            controller.Restarted += Hide;
            _subscribed = controller;
        }

        void OnRetry()
        {
            if (controller != null) controller.Restart();   // Restarted → Hide
            Hide();
        }

        void Hide()
        {
            if (panel != null) panel.SetActive(false);
        }

        void Show()
        {
            var c = controller;
            if (c == null || c.Race == null || panel == null) return;
            var racers = c.Race.Racers;
            var player = racers[RaceController.PlayerIndex];
            var inv = CultureInfo.InvariantCulture;

            titleText.text = player.Car.Phase == CarPhase.Finished ? player.Rank + "." : "ELENDIN";
            timeText.text = c.ElapsedTime.ToString("0.0", inv) + " sn";
            countsText.text = "Perfect: " + player.Car.PerfectCount + "  Good: " + player.Car.GoodCount;

            int rows = rowName != null ? rowName.Length : 0;
            for (int row = 0; row < rows; row++)
            {
                int idx = -1;
                for (int i = 0; i < racers.Count; i++)
                    if (racers[i].Rank == row + 1) { idx = i; break; }
                bool has = idx >= 0;
                var col = has && idx == RaceController.PlayerIndex ? playerRowColor : rowColor;
                SetRow(rowRank, row, has ? (row + 1) + "." : "", col);
                SetRow(rowName, row, has ? racers[idx].Name : "", col);
                SetRow(rowStatus, row, has ? Status(racers[idx].Car) : "", col);
            }
            panel.SetActive(true);
        }

        static string Status(CarSim car) =>
            car.Phase == CarPhase.Finished ? "Bitti" : car.IsOut ? "Elendi" : "Yarista";

        static void SetRow(TMP_Text[] texts, int row, string value, Color color)
        {
            if (texts == null || row >= texts.Length || texts[row] == null) return;
            texts[row].text = value;
            texts[row].color = color;
        }
    }
}
