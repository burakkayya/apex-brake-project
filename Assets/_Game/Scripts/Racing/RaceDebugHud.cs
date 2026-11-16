using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ApexBrake.Simulation;

namespace ApexBrake.Racing
{
    /// <summary>Gri kutu yardımcısı (sonraki fazlarda kaldırılır): uGUI + TMP ile simülasyon değerlerini gösterir.</summary>
    public sealed class RaceDebugHud : MonoBehaviour
    {
        public CarController controller;
        public int fontSize = 44;

        TMP_Text _info;
        TMP_Text _center;
        Image _brakeBar;
        bool _wasBraking = true; // başlangıçta basılıysa ilk karede "yeni dokunuş" sayılmasın
        float _endTime;
        readonly StringBuilder _sb = new StringBuilder(256);

        void Start()
        {
            var canvasGo = new GameObject("HudCanvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            _info = MakeText("Info", new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -30), TextAlignmentOptions.TopLeft, canvasGo.transform);
            _center = MakeText("Center", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 150), TextAlignmentOptions.Center, canvasGo.transform);
            _center.fontSize = fontSize + 20;

            var barGo = new GameObject("BrakeBar");
            barGo.transform.SetParent(canvasGo.transform, false);
            _brakeBar = barGo.AddComponent<Image>();
            var rt = _brakeBar.rectTransform;
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(0, 40);
            rt.anchoredPosition = Vector2.zero;
        }

        TMP_Text MakeText(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, TextAlignmentOptions align, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.fontSize = fontSize;
            t.alignment = align;
            t.color = Color.white;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            var rt = t.rectTransform;
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = anchorMin;
            rt.sizeDelta = new Vector2(1000, 600);
            rt.anchoredPosition = pos;
            if (name == "Center") rt.pivot = new Vector2(0.5f, 0.5f);
            return t;
        }

        void Update()
        {
            var c = controller;
            if (c == null || c.Sim == null || _info == null) return;
            var sim = c.Sim;

            _sb.Clear();
            _sb.Append("Hiz: ").Append(sim.Speed.ToString("0.0")).Append(" m/s\n");
            if (c.track.Layout.TryGetNextObstacle(sim.Distance, out var span))
            {
                var def = span.Def;
                float safe = def.Kind == PieceKind.Ramp ? def.MinJumpSpeed : def.SafeSpeed;
                string label = def.Kind == PieceKind.Ramp ? "min atlama" : "guvenli";
                float takeoff = def.Kind == PieceKind.Ramp ? span.End - def.GapLength : span.Start;
                _sb.Append("Siradaki: ").Append(def.Kind).Append("  ").Append(label).Append(' ').Append(safe.ToString("0.0"));
                if (def.Kind == PieceKind.Ramp) _sb.Append("-").Append(def.SafeSpeed.ToString("0.0"));
                _sb.Append("\nUzaklik: ").Append(Mathf.Max(0f, takeoff - sim.Distance).ToString("0")).Append(" m\n");
            }
            else _sb.Append("Siradaki: -\n");
            _sb.Append("Son olay: ").Append(c.LastEvent.HasValue ? c.LastEvent.Value.ToString() : "-").Append('\n');
            _sb.Append("Sure: ").Append(c.ElapsedTime.ToString("0.0")).Append(" s  ").Append(sim.Phase);
            _info.text = _sb.ToString();

            _brakeBar.color = c.IsBraking ? new Color(1f, 0.2f, 0.2f, 0.9f) : new Color(0.2f, 1f, 0.3f, 0.5f);

            bool over = sim.Phase == CarPhase.Finished || sim.IsOut;
            if (over)
            {
                _endTime += Time.unscaledDeltaTime;
                _center.text = (sim.Phase == CarPhase.Finished ? "BITTI  " + c.ElapsedTime.ToString("0.0") + " s" : "CAKILDIN") + "\nTekrar icin dokun";
                if (_endTime > 0.4f && c.IsBraking && !_wasBraking)
                {
                    c.Restart();
                    _endTime = 0f;
                    _center.text = "";
                }
            }
            else
            {
                _endTime = 0f;
                _center.text = "";
            }
            _wasBraking = c.IsBraking;
        }
    }
}
