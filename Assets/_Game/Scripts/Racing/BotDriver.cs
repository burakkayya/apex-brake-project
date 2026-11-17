using UnityEngine;
using ApexBrake.Simulation;
using ApexBrake.Track;

namespace ApexBrake.Racing
{
    /// <summary>
    /// Botun fren ayağı: her fizik adımında <see cref="VehicleMotor.Brake"/>'i yazar. Bu bir oyun kuralı değil, sürücü
    /// muhakemesidir: bot ileriye bakar (CurvatureAt/SlopeAt), önündeki virajın kendi çizgisindeki yarıçapından ve aracın
    /// tutunmasına dair kendi kanaatinden "rahat geçebileceğini düşündüğü" hızı tahmin eder, fren mesafesini tahmin eder
    /// ve gerekiyorsa frene basar. Tahmin bottan bota, virajdan viraja ve yarıştan yarışa değişir ve yanlış olabilir;
    /// sonucu (kayma, yoldan çıkma) yine fizik verir. Kayınca beceriye göre tepki verir. Diğer araçlara bakmaz (lastik bant yok).
    /// </summary>
    [DefaultExecutionOrder(-30)]
    [RequireComponent(typeof(VehicleMotor))]
    public sealed class BotDriver : MonoBehaviour
    {
        /// <summary>Sürücülerin bu araç için "referans" viraj tutunması kanaati (m/s²); bkz. ledger Ruling (ölçümle ayarlandı).</summary>
        public static float ReferenceGrip = 27f;
        /// <summary>Referans fren yavaşlaması kanaati (m/s²).</summary>
        public static float ReferenceBrake = 21f;
        /// <summary>Yerçekimi kanaati (aracın ağırlık hissi; tepe/tümsek için) (m/s²).</summary>
        public const float FeltGravity = 24f;
        /// <summary>Dış kenara hiç yer kalmamışken tutunma kanaatinin çarpanı (yer arttıkça 1'e çıkar).</summary>
        public const float OutsideCaution = 0.75f;

        public BotProfile Profile;
        public TrackBuilder Track;

        /// <summary>RaceManager yazar: yarışta, oyun dışı değil, bitirmemiş. false iken fren pedalına dokunmaz.</summary>
        public bool Driving { get; set; }
        /// <summary>−1..+1, yarış başına. Pozitif = iyi gün.</summary>
        public float Form { get; private set; }
        /// <summary>Ev çizgisine eklenecek gezinme (m).</summary>
        public float LineOffset { get; private set; }
        public int MistakeCount { get; private set; }
        public int HeroCount { get; private set; }
        /// <summary>Kayma tepkisi (fren) sürüyor.</summary>
        public bool SlideBraking => _slideBrakeUntil > _t;
        /// <summary>Son kararda frene basmasının nedeni (ölçüm/günlük için).</summary>
        public string Reason { get; private set; } = "";

        VehicleMotor _motor;
        LineDriver _line;
        VehicleTelemetry _tel;
        SimRandom _rnd;

        float _t;
        float _startDelay;
        float _wanderPeriod, _wanderPhase;
        float _brakeHoldUntil;
        float _slideReactAt = -1f, _slideBrakeUntil;
        bool _wasSliding;

        // Engel (parça) başına kanaat
        float[] _grip;          // viraj tutunması kanaati (m/s²)
        float[] _shift;         // fren noktası kayması (m; + geç)
        bool[] _hero;
        bool[] _mistake;
        bool[] _counted;
        float _brakeGuess;

        // Tepki süresi: karar bu kadar eski durumla verilir.
        float[] _histD, _histV;
        int _histHead, _histCount;

        void Awake()
        {
            _motor = GetComponent<VehicleMotor>();
            _line = GetComponent<LineDriver>();
            _tel = GetComponent<VehicleTelemetry>();
        }

        /// <summary>Profilin motor/hız/lastik çarpanları.</summary>
        public static VehicleUpgrades UpgradesFor(BotProfile p) => p == null ? VehicleUpgrades.Base
            : new VehicleUpgrades { Engine = p.EngineFactor, TopSpeed = p.TopSpeedFactor, Tire = p.TireFactor };

        /// <summary>Yeni yarış: tohumdan form, start tepkisi, gezinme ve her engel için kanaat/hata/kusursuz geçiş zarları.</summary>
        public void Begin(uint seed)
        {
            if (_motor == null) Awake();
            var p = Profile ?? BotProfile.Master();
            _rnd = new SimRandom(seed * 2654435761u + 12345u);
            float f = (_rnd.NextFloat() + _rnd.NextFloat()) - 1f;
            Form = Mathf.Clamp(f, -1f, 1f);
            _startDelay = (0.05f + 0.35f * _rnd.NextFloat()) * (1.3f - p.Skill);
            _wanderPeriod = 4f + 5f * _rnd.NextFloat();
            _wanderPhase = 2f * Mathf.PI * _rnd.NextFloat();
            _brakeGuess = ReferenceBrake * (0.85f + 0.15f * p.Skill) * (1f + 0.03f * Form);

            int n = Track != null && Track.Layout != null ? Track.Layout.PieceCount : 0;
            _grip = new float[n]; _shift = new float[n]; _hero = new bool[n]; _mistake = new bool[n]; _counted = new bool[n];
            float spread = (1f - p.Skill) * 0.14f;
            for (int k = 0; k < n; k++)
            {
                float noise = (_rnd.NextFloat() + _rnd.NextFloat() - 1f);           // −1..+1, ortaya yığılı
                float g = ReferenceGrip * p.Courage * (1f + 0.04f * Form) * (1f + spread * noise);
                float shift = (_rnd.NextFloat() * 2f - 1f) * (1f - p.Skill) * 6f;
                float roll = _rnd.NextFloat();
                float kind = _rnd.NextFloat(), amount = _rnd.NextFloat();
                if (roll < p.MistakeChance)
                {
                    // Büyük hata: ya virajı olduğundan kolay sanır ya da fren noktasını kaçırır.
                    _mistake[k] = true;
                    if (kind < 0.5f) g *= 1.3f + 0.4f * amount;
                    else shift = Mathf.Max(shift, 0f) + 8f + 10f * amount;
                }
                else if (roll > 1f - p.HeroChance)
                {
                    // Kusursuz geçiş: sınıra yakın ve isabetli kanaat, gecikmesiz karar.
                    _hero[k] = true;
                    g = ReferenceGrip * 1.05f;
                    shift = 0f;
                }
                _grip[k] = g; _shift[k] = shift;
            }

            int delay = Mathf.Max(0, Mathf.RoundToInt(p.ReactionTime / 0.02f));
            _histD = new float[delay + 1]; _histV = new float[delay + 1];
            _histHead = 0; _histCount = 0;
            _t = 0f; _brakeHoldUntil = 0f; _slideReactAt = -1f; _slideBrakeUntil = 0f; _wasSliding = false;
            MistakeCount = 0; HeroCount = 0; LineOffset = 0f; Reason = "";
        }

        void FixedUpdate()
        {
            if (!Driving || _motor == null || Track == null || Track.Path == null || _grip == null) return;
            var p = Profile ?? BotProfile.Master();
            float dt = Time.fixedDeltaTime;
            _t += dt;
            var path = Track.Path;
            var layout = Track.Layout;
            float d = _line.Distance;
            float v = Mathf.Max(0f, Vector3.Dot(_motor.Body.linearVelocity, path.ForwardAt(d)));
            Record(d, v);

            // Çizgide gezinme (daralmada küçük)
            int lanes = Mathf.Min(layout.SpanAtDistance(d).Def.LaneCount, layout.SpanAtDistance(d + 25f).Def.LaneCount);
            float amp = p.Wander * (lanes < LaneMap.HomeLanes ? 0.3f : 1f);
            LineOffset = amp * Mathf.Sin(2f * Mathf.PI * _t / _wanderPeriod + _wanderPhase);

            // Start: tepki süresi kadar geç kalkış
            if (_t < _startDelay) { _motor.Brake = true; Reason = "start"; return; }

            // Rampaya girdikten sonra iniş tamamlanana kadar frene dokunmaz (fren burnu düşürür; havada fren işe yaramaz).
            if (OnRampCommit(layout, d)) { _motor.Brake = false; Reason = "ramp"; _slideReactAt = -1f; _slideBrakeUntil = 0f; return; }

            // Kayma tepkisi: kaymayı tepki süresi sonra fark eder; usta kısa, acemi uzun (ya da geç) frenler.
            bool sliding = _tel != null && _tel.IsSliding;
            if (sliding && !_wasSliding && _slideBrakeUntil <= _t)
            {
                float late = p.ReactionTime + (1f - p.Skill) * 0.25f * _rnd.NextFloat();
                _slideReactAt = _t + late;
            }
            _wasSliding = sliding;
            if (_slideReactAt >= 0f && _t >= _slideReactAt)
            {
                _slideReactAt = -1f;
                if (sliding)
                {
                    float hold = 0.18f + (1f - p.Skill) * (0.25f + 0.9f * _rnd.NextFloat());
                    _slideBrakeUntil = _t + hold;
                }
            }
            if (_slideBrakeUntil > _t) { _motor.Brake = true; Reason = "slide"; return; }

            // Önündeki engel için karar (kusursuz geçişte güncel durumla, değilse tepki süresi kadar eskiyle)
            Delayed(out float d0, out float v0);
            bool need = NeedsBrake(path, layout, d, v, d0, v0, out string why);
            if (need) { _brakeHoldUntil = _t + 0.1f; Reason = why; }
            bool brake = need || _t < _brakeHoldUntil;
            if (!brake) Reason = "";
            _motor.Brake = brake;
        }

        bool NeedsBrake(TrackPath path, TrackLayout layout, float dNow, float vNow, float d0, float v0, out string why)
        {
            why = "";
            var p = Profile;
            float lineLat = _line.TargetLateral;
            float horizon = Mathf.Max(45f, v0 * 2.5f + 15f);
            float total = path.TotalLength;
            for (float s = d0; s <= d0 + horizon && s < total; s += 1.5f)
            {
                var span = layout.SpanAtDistance(s);
                int k = span.Index;
                bool hero = _hero[k];
                float dd = hero ? dNow : d0, vv = hero ? vNow : v0;
                if (s < dd - 0.5f) continue;

                float allow = float.MaxValue;
                if (span.Def.Kind == PieceKind.Corner)
                {
                    float c = path.CurvatureAt(s);
                    if (Mathf.Abs(c) < 0.004f) continue;
                    float r = 1f / Mathf.Abs(c) - Mathf.Sign(c) * lineLat;
                    // Dış kenara az yer kaldıysa (dış şerit) daha temkinli: kayarsa gidecek yer yok.
                    float room = path.HalfWidthAt(s) + TrackPieceGeometry.ShoulderWidth + Mathf.Sign(c) * lineLat - 1f;
                    float caution = Mathf.Lerp(OutsideCaution, 1f, Mathf.Clamp01(room / 6f));
                    allow = Mathf.Sqrt(_grip[k] * caution * Mathf.Max(r, 5f));
                }
                else if (span.Def.Kind == PieceKind.Bump)
                {
                    // Tepe: yol yüzeyinin dikey eğriliği (SlopeAt farkı). Yüzey aşağı kıvrılınca araç hafifler.
                    float kv = -(path.SlopeAt(s + 1f) - path.SlopeAt(s - 1f)) * 0.5f;
                    if (kv < 0.01f) continue;
                    float tolerance = 1.6f + 6f * (p.Courage - 0.85f);   // cesaret: tepeye ne kadar hızlı girilir
                    allow = Mathf.Sqrt(FeltGravity / kv) * Mathf.Max(1.1f, tolerance);
                }
                else continue;

                if (vv <= allow) continue;
                CountFeature(k);
                float dist = s - dd + _shift[k] - AirborneBetween(layout, dd, s);
                float needDist = (vv * vv - allow * allow) / (2f * _brakeGuess) + vv * 0.04f;
                if (needDist >= dist)
                {
                    why = span.Def.Kind == PieceKind.Corner ? "corner" : "crest";
                    return true;
                }
            }
            return false;
        }

        // Rampa: yükselişin başından iniş yüzeyinin biraz ötesine kadar (yay uzunluğu).
        static bool RampWindow(PieceSpan span, out float from, out float to)
        {
            from = span.Start + TrackPieceGeometry.RampApproach - 2f;
            to = span.Start + TrackPieceGeometry.RampGapEndArc + 8f;
            return span.Def.Kind == PieceKind.Ramp;
        }

        static bool OnRampCommit(TrackLayout layout, float d)
        {
            var span = layout.SpanAtDistance(d);
            if (!RampWindow(span, out float a, out float b))
            {
                if (span.Index == 0) return false;
                span = layout.SpanAt(span.Index - 1);   // iniş yüzeyi parçanın sonuna yakınsa bir sonraki parçaya taşar
                if (!RampWindow(span, out a, out b)) return false;
            }
            return d >= a && d <= b;
        }

        /// <summary>[from, to] arasında rampanın havada geçilen kısmı (fren yapılamayan mesafe; kalkıştan iniş sonrası oturmaya kadar).</summary>
        static float AirborneBetween(TrackLayout layout, float from, float to)
        {
            float sum = 0f;
            for (int i = 0; i < layout.PieceCount; i++)
            {
                var span = layout.SpanAt(i);
                if (span.Def.Kind != PieceKind.Ramp || span.Start > to || span.End + 10f < from) continue;
                float a = span.Start + TrackPieceGeometry.RampTakeoffArc, b = span.Start + TrackPieceGeometry.RampGapEndArc + 10f;
                sum += Mathf.Max(0f, Mathf.Min(b, to) - Mathf.Max(a, from));
            }
            return sum;
        }

        void CountFeature(int k)
        {
            if (_counted[k]) return;
            _counted[k] = true;
            if (_hero[k]) HeroCount++;
            else if (_mistake != null && _mistake[k]) MistakeCount++;
        }

        void Record(float d, float v)
        {
            int n = _histD.Length;
            _histHead = (_histHead + 1) % n;
            _histD[_histHead] = d; _histV[_histHead] = v;
            if (_histCount < n) _histCount++;
        }

        void Delayed(out float d, out float v)
        {
            int n = _histD.Length;
            int back = Mathf.Min(_histCount, n) - 1;
            int idx = ((_histHead - back) % n + n) % n;
            d = _histD[idx]; v = _histV[idx];
        }
    }
}
