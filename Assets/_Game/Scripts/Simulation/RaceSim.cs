using System;
using System.Collections.Generic;

namespace ApexBrake.Simulation
{
    public sealed class RacerSetup
    {
        public string Name; public int HomeLane; public CarStats Stats; public CrashPolicy Policy;
        public BotProfile Bot;    // null = oyuncu
        public uint Seed;
    }

    public sealed class Racer
    {
        internal Racer(string name, int homeLane, CarSim car, BotBrain brain)
        {
            Name = name; HomeLane = homeLane; Car = car; Brain = brain; FinishTick = -1;
        }

        public string Name { get; }
        public int HomeLane { get; }
        public bool IsBot => Brain != null;
        public CarSim Car { get; }
        public int FinishTick { get; internal set; }   // bitirmediyse -1
        public int Rank { get; internal set; }         // 1 tabanlı, her Tick güncellenir
        /// <summary>Gerçek yanal konum (= Car.Lateral), m, + = sağ.</summary>
        public float Lateral => Car.Lateral;
        /// <summary>Önündeki yavaş aracı yandan geçmek için ev çizgisinden ayrıldı mı.</summary>
        public bool IsOvertaking { get; internal set; }
        /// <summary>Son Tick'te fren basılı mıydı (oyuncu girdisi ya da bot kararı).</summary>
        public bool IsBraking { get; internal set; }

        // Sollama durumu (RaceSim yazar)
        internal int OvertakeBlocker = -1;
        internal float OvertakeTarget;
        internal int TargetHoldTicks;          // hedef değişince bu kadar Tick yeniden değişmez

        internal BotBrain Brain { get; }
    }

    /// <summary>N aracı aynı sabit adımda yürütür; her aracın hedef çizgisini yazar, teması araçların gerçek
    /// konumundan çözer ve sıralamayı tutar.</summary>
    public sealed class RaceSim
    {
        // Yan temas bir kez başlayınca araçlar bu kadar (m) fazladan açılana dek sürer; yan yana sürtünen
        // araçlar her Tick yeni bir çarpma saymaz.
        const float SideContactRelease = 0.3f;
        // Üst üste binmiş araçlar en çok bu yanal hızla (m/s, her biri) ve bu sürede (sn) ayrılır.
        const float SeparateMaxSpeed = 1.5f;
        const float SeparateTime = 0.25f;

        readonly TrackLayout _layout;
        readonly SimTuning _tuning;
        readonly Racer[] _racers;
        readonly int[] _order;                                   // sıralama/temas için yeniden kullanılan dizi
        readonly int _n;
        bool[] _rearContacts, _nextRear;                         // [arkadaki * n + öndeki]
        bool[] _sideContacts, _nextSide;                         // [küçük indeks * n + büyük indeks]
        readonly List<int> _bumps = new List<int>();
        readonly float[] _prevDistance;                          // Tick başındaki mesafeler
        readonly float[] _targets;

        public RaceSim(TrackLayout layout, SimTuning tuning, IReadOnlyList<RacerSetup> setups)
        {
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            _tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
            if (setups == null || setups.Count < 1 || setups.Count > LaneMap.HomeLanes)
                throw new ArgumentException("Race rule: 1..5 racers required.");

            var used = new bool[LaneMap.HomeLanes];
            _racers = new Racer[setups.Count];
            for (int i = 0; i < setups.Count; i++)
            {
                var s = setups[i] ?? throw new ArgumentException($"Race rule: racer {i} setup must not be null.");
                if (s.HomeLane < 0 || s.HomeLane >= LaneMap.HomeLanes)
                    throw new ArgumentException($"Race rule: racer {i} home lane must be 0..4.");
                if (used[s.HomeLane])
                    throw new ArgumentException($"Race rule: home lane {s.HomeLane} is used twice.");
                used[s.HomeLane] = true;

                var stats = s.Stats ?? (s.Bot != null ? s.Bot.ToStats() : new CarStats());
                var car = new CarSim(layout, stats, tuning, s.Policy);
                var brain = s.Bot != null ? new BotBrain(s.Bot, layout, s.Seed) : null;
                _racers[i] = new Racer(s.Name, s.HomeLane, car, brain);
                car.PlaceOnLine(HomeLine(i));
            }
            _n = _racers.Length;
            _order = new int[_n];
            _prevDistance = new float[_n];
            _targets = new float[_n];
            _rearContacts = new bool[_n * _n]; _nextRear = new bool[_n * _n];
            _sideContacts = new bool[_n * _n]; _nextSide = new bool[_n * _n];
            UpdateRank();
        }

        /// <summary>Test kancası: aracı mesafe d'ye, hız v ile, o noktadaki ev çizgisine oturtur.</summary>
        internal void SetStateForTest(int i, float d, float v)
        {
            float line = LaneMap.LateralAt(_layout, _racers[i].HomeLane, d, _tuning);
            _racers[i].Car.SetStateForTest(d, v, line, line);
        }

        public IReadOnlyList<Racer> Racers => _racers;
        public int TickCount { get; private set; }

        public bool AllDone
        {
            get
            {
                foreach (var r in _racers)
                    if (r.Car.Phase != CarPhase.Finished && !r.Car.IsOut) return false;
                return true;
            }
        }

        /// <summary>İndeks = yarışçı; botlar için değer yok sayılır.</summary>
        public void Tick(IReadOnlyList<bool> playerBraking)
        {
            TickCount++;

            // (0) hedef çizgileri (ev çizgisi ya da sollama) — hepsi Tick'ten önce, aynı durumdan
            for (int i = 0; i < _n; i++) _targets[i] = ChooseTarget(i);
            for (int i = 0; i < _n; i++) _racers[i].Car.TargetLateral = _targets[i];

            // (1) fren kararları — hepsi Tick'ten önce, aynı durumdan
            var brake = new bool[_n];
            for (int i = 0; i < _n; i++)
            {
                var r = _racers[i];
                brake[i] = r.IsBot
                    ? r.Brain.ShouldBrake(r.Car)
                    : playerBraking != null && i < playerBraking.Count && playerBraking[i];
                r.IsBraking = brake[i];
            }

            // (2) araçlar
            for (int i = 0; i < _n; i++)
            {
                var r = _racers[i];
                _prevDistance[i] = r.Car.Distance;
                r.Car.Tick(brake[i]);
                if (r.FinishTick < 0 && r.Car.Phase == CarPhase.Finished) r.FinishTick = TickCount;
            }

            // (3) temas
            ResolveContacts();

            // (4) sıralama
            UpdateRank();
        }

        float HomeLine(int i) => LaneMap.LateralAt(_layout, _racers[i].HomeLane, _racers[i].Car.Distance, _tuning);

        int HoldTicks => (int)(_tuning.OvertakeHold / CarSim.Step + 0.5f);

        // Ev çizgisi; önde yavaş bir araç varsa ve yanında yer varsa onun yanı (sollama).
        float ChooseTarget(int i)
        {
            var me = _racers[i];
            var car = me.Car;
            float home = HomeLine(i);
            if (me.IsBot) home += me.Brain.LineOffset(car);     // botlar çizgide hafifçe gezinir
            if (car.IsOut || car.Phase == CarPhase.Finished || car.Phase == CarPhase.Crashed)
            {
                me.IsOvertaking = false; me.OvertakeBlocker = -1; me.TargetHoldTicks = 0;
                return home;
            }
            if (!InContact(i)) return me.IsOvertaking ? me.OvertakeTarget : home; // havada: karar yok

            if (me.TargetHoldTicks > 0) me.TargetHoldTicks--;
            if (me.IsOvertaking)
            {
                if (me.TargetHoldTicks > 0) return me.OvertakeTarget;
                int b = me.OvertakeBlocker;
                var bc = _racers[b].Car;
                bool passed = !InContact(b) || car.Distance - bc.Distance >= _tuning.CarLength + 2f;
                // Engel artık engel değilse (hızlandı) ve hâlâ öndeyse vazgeç.
                bool gaveUp = !passed && bc.Distance - car.Distance >= _tuning.CarLength && !IsBlocker(i, b, home);
                if (passed || gaveUp)
                {
                    me.IsOvertaking = false; me.OvertakeBlocker = -1; me.TargetHoldTicks = HoldTicks;
                    return home;
                }
                // Seçilen yan yoldan taştıysa ya da başka bir araçla doldu ise yeniden seç; yer yoksa sürdür.
                if (!CandidateValid(i, b, me.OvertakeTarget) && TryPickSide(i, b, home, out float t)
                    && Math.Abs(t - me.OvertakeTarget) > 1e-3f)
                {
                    me.OvertakeTarget = t; me.TargetHoldTicks = HoldTicks;
                }
                return me.OvertakeTarget;
            }

            if (me.TargetHoldTicks == 0)
            {
                int b = FindBlocker(i, home);
                if (b >= 0 && TryPickSide(i, b, home, out float t))
                {
                    me.IsOvertaking = true; me.OvertakeBlocker = b; me.OvertakeTarget = t; me.TargetHoldTicks = HoldTicks;
                    return t;
                }
            }
            return home;
        }

        // Öndeki 0 < ΔD ≤ OvertakeLook, ev çizgisine CarWidth + 0.4'ten yakın ve OvertakeMinSpeedDiff'ten yavaş araç mı.
        bool IsBlocker(int i, int j, float home)
        {
            if (j == i || !InContact(j)) return false;
            var me = _racers[i].Car; var o = _racers[j].Car;
            float dD = o.Distance - me.Distance;
            return dD > 0f && dD <= _tuning.OvertakeLook
                && Math.Abs(o.Lateral - home) < _tuning.CarWidth + 0.4f
                && o.Speed < me.Speed - _tuning.OvertakeMinSpeedDiff;
        }

        int FindBlocker(int i, float home)
        {
            int best = -1; float bestD = float.MaxValue;
            for (int j = 0; j < _n; j++)
            {
                if (!IsBlocker(i, j, home)) continue;
                float dD = _racers[j].Car.Distance - _racers[i].Car.Distance;
                if (dD < bestD) { bestD = dD; best = j; }   // eşitlikte düşük indeks
            }
            return best;
        }

        // Engelin iki yanından geçerli olup ev çizgisine yakın olanı.
        bool TryPickSide(int i, int blocker, float home, out float target)
        {
            float bl = _racers[blocker].Car.Lateral;
            float off = _tuning.CarWidth + _tuning.OvertakeGap;
            float left = bl - off, right = bl + off;
            bool lv = CandidateValid(i, blocker, left), rv = CandidateValid(i, blocker, right);
            if (lv && rv) target = Math.Abs(left - home) <= Math.Abs(right - home) ? left : right;
            else if (lv) target = left;
            else if (rv) target = right;
            else { target = home; return false; }
            return true;
        }

        bool CandidateValid(int i, int blocker, float cand)
        {
            var me = _racers[i].Car;
            float hw = Math.Min(_layout.SpanAtDistance(me.Distance).Def.HalfWidth,
                                _layout.SpanAtDistance(me.Distance + 20f).Def.HalfWidth);
            if (Math.Abs(cand) > hw - 1.2f) return false;
            for (int j = 0; j < _n; j++)
            {
                if (j == i || j == blocker || !InContact(j)) continue;
                var o = _racers[j].Car;
                // Geçiş çizgisinde, bize ya da engele 10 m'den yakın araç varsa yer yok.
                bool near = Math.Abs(o.Distance - me.Distance) < 10f
                    || (blocker >= 0 && Math.Abs(o.Distance - _racers[blocker].Car.Distance) < 10f);
                if (near && Math.Abs(o.Lateral - cand) < _tuning.CarWidth) return false;
            }
            return true;
        }

        bool InContact(int i)
        {
            var c = _racers[i].Car;
            return !c.IsOut && (c.Phase == CarPhase.Driving || c.Phase == CarPhase.Skidding);
        }

        // b, a'nın önünde mi? Mesafeler eşitse düşük indeksli öndedir.
        bool IsAhead(int b, int a)
        {
            float db = _racers[b].Car.Distance, da = _racers[a].Car.Distance;
            return db > da || (db == da && b < a);
        }

        void ResolveContacts()
        {
            // Önden arkaya: öndeki önce kendi önündekine göre sınırlanır, arkadaki onun yeni hızını görür.
            int n = SortByPosition(includeAll: false);
            Array.Clear(_nextRear, 0, _nextRear.Length);
            Array.Clear(_nextSide, 0, _nextSide.Length);
            _bumps.Clear();
            float sideZone = 0.6f * _tuning.CarLength;

            for (int fi = 0; fi < n; fi++)
            {
                int a = _order[fi];
                for (int li = 0; li < fi; li++)
                {
                    int b = _order[li];                       // b, a'nın önünde (ya da eşit mesafede)
                    var carA = _racers[a].Car; var carB = _racers[b].Car;
                    float dD = carB.Distance - carA.Distance;  // ≥ 0
                    float dL = Math.Abs(carB.Lateral - carA.Lateral);
                    if (!(dD < _tuning.CarLength)) continue;

                    int lo = a < b ? a : b, hi = a < b ? b : a;
                    int sideKey = lo * _n + hi;
                    int key = a * _n + b;
                    // Arkadan yaklaşan araç bir Tick'te yan bölgeye girse de (yüksek kapanma hızı) temas arkadandır;
                    // arkadan temas, araçlar üst üste kaldıkça arkadan sürer.
                    bool fromBehind = _rearContacts[key]
                        || (!_sideContacts[sideKey] && _prevDistance[b] - _prevDistance[a] >= sideZone);
                    if (dD < sideZone && !(fromBehind && dL < _tuning.CarWidth))
                    {
                        bool wasSide = _sideContacts[sideKey];
                        if (dL < _tuning.CarWidth || (wasSide && dL < _tuning.CarWidth + SideContactRelease))
                        {
                            _nextSide[sideKey] = true;
                            SideContact(lo, hi, !wasSide);
                        }
                        continue;
                    }

                    if (!(dL < _tuning.CarWidth)) continue;
                    // Arkadan temas
                    carA.LimitSpeed(carB.Speed * _tuning.RearHitSpeedFactor);
                    _nextRear[key] = true;
                    if (!_rearContacts[key])
                    {
                        carA.RaiseEvent(SimEventKind.RearHit);
                        carB.RaiseEvent(SimEventKind.Bumped);
                        _bumps.Add(b);
                    }
                }
            }
            foreach (int b in _bumps) _racers[b].Car.AddSpeed(_tuning.RearBump);

            var t = _rearContacts; _rearContacts = _nextRear; _nextRear = t;
            t = _sideContacts; _sideContacts = _nextSide; _nextSide = t;
        }

        // lo < hi (indeks). Yanal eşitlikte düşük indeksli sola (−), diğeri sağa itilir.
        void SideContact(int lo, int hi, bool isNew)
        {
            var cl = _racers[lo].Car; var ch = _racers[hi].Car;
            float d = ch.Lateral - cl.Lateral;
            float sh = d < 0f ? -1f : 1f;                     // hi'nin itildiği yön
            float sl = -sh;

            if (isNew)
            {
                cl.AddLateralVel(sl * _tuning.SideHitKick);
                ch.AddLateralVel(sh * _tuning.SideHitKick);
                float keep = 1f - _tuning.SideHitSpeedLoss;
                cl.ScaleSpeed(keep);
                ch.ScaleSpeed(keep);
                cl.RaiseEvent(SimEventKind.SideHit);
                ch.RaiseEvent(SimEventKind.SideHit);
            }
            else
            {
                // Temas sürüyor: birbirine yaklaşan bağıl yanal hız sıfırlanır (ikisi ortalamaya çekilir);
                // üst üste binmişlerse en çok SeparateMaxSpeed ile ayrılırlar. Konum hiç değiştirilmez.
                float rel = (ch.LateralVel - cl.LateralVel) * sh; // > 0 = açılıyorlar
                float overlap = _tuning.CarWidth - Math.Abs(d);
                float wanted = overlap > 0f ? Math.Min(overlap / SeparateTime, 2f * SeparateMaxSpeed) : 0f;
                if (rel < wanted)
                {
                    float fix = 0.5f * (wanted - rel);
                    cl.AddLateralVel(sl * fix);
                    ch.AddLateralVel(sh * fix);
                }
            }
            // Başka bir aracın çarpması kimseyi yolun dışına atamaz.
            cl.ProtectFromEdge(_tuning.ContactProtect);
            ch.ProtectFromEdge(_tuning.ContactProtect);
        }

        // _order'ı doldurur. includeAll: sıralama için herkes (önce bitirenler FinishTick'e göre, sonra mesafe);
        // değilse yalnızca temasa girebilen araçlar, mesafeye göre önden arkaya. Dönüş: dolu eleman sayısı.
        int SortByPosition(bool includeAll)
        {
            int n = 0;
            for (int i = 0; i < _n; i++)
                if (includeAll || InContact(i)) _order[n++] = i;

            // Kararlı, küçük N için ekleme sıralaması.
            for (int i = 1; i < n; i++)
            {
                int x = _order[i];
                int j = i - 1;
                while (j >= 0 && Before(x, _order[j], includeAll)) { _order[j + 1] = _order[j]; j--; }
                _order[j + 1] = x;
            }
            return n;
        }

        bool Before(int x, int y, bool useFinish)
        {
            if (useFinish)
            {
                int fx = _racers[x].FinishTick, fy = _racers[y].FinishTick;
                if (fx >= 0 || fy >= 0)
                {
                    if (fx < 0) return false;
                    if (fy < 0) return true;
                    if (fx != fy) return fx < fy;
                    return x < y;
                }
            }
            return IsAhead(x, y);
        }

        void UpdateRank()
        {
            int n = SortByPosition(includeAll: true);
            for (int k = 0; k < n; k++) _racers[_order[k]].Rank = k + 1;
        }
    }
}
