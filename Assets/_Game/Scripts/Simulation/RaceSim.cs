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
        public float Lateral { get; internal set; }    // LaneMap.LateralAt sonucu, her Tick güncellenir

        internal BotBrain Brain { get; }
    }

    /// <summary>N aracı aynı sabit adımda yürütür; şerit içi teması çözer, yanal konumu ve sıralamayı tutar.</summary>
    public sealed class RaceSim
    {
        readonly TrackLayout _layout;
        readonly SimTuning _tuning;
        readonly Racer[] _racers;
        readonly int[] _order;                                   // sıralama/temas için yeniden kullanılan dizi
        HashSet<int> _rearContacts = new HashSet<int>();         // önceki Tick'te temasta olan (arkadaki, öndeki) çiftleri
        HashSet<int> _nextContacts = new HashSet<int>();

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
            }
            _order = new int[_racers.Length];
            UpdateLateralAndRank();
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

            // (1) fren kararları — hepsi Tick'ten önce, aynı durumdan
            var brake = new bool[_racers.Length];
            for (int i = 0; i < _racers.Length; i++)
            {
                var r = _racers[i];
                brake[i] = r.IsBot
                    ? r.Brain.ShouldBrake(r.Car)
                    : playerBraking != null && i < playerBraking.Count && playerBraking[i];
            }

            // (2) araçlar
            for (int i = 0; i < _racers.Length; i++)
            {
                var r = _racers[i];
                r.Car.Tick(brake[i]);
                if (r.FinishTick < 0 && r.Car.Phase == CarPhase.Finished) r.FinishTick = TickCount;
            }

            // (3) temas
            ResolveRearContacts();
            ResolveSideContacts();

            // (4) yanal konum ve sıralama
            UpdateLateralAndRank();
        }

        bool InContact(int i)
        {
            var c = _racers[i].Car;
            return !c.IsOut && (c.Phase == CarPhase.Driving || c.Phase == CarPhase.Skidding);
        }

        void LaneOf(int i, out int effectiveLane, out int laneCount)
        {
            var car = _racers[i].Car;
            laneCount = _layout.SpanAtDistance(car.Distance).Def.LaneCount;
            effectiveLane = LaneMap.EffectiveLane(_racers[i].HomeLane, laneCount);
        }

        // b, a'nın önünde mi? Mesafeler eşitse düşük indeksli öndedir.
        bool IsAhead(int b, int a)
        {
            float db = _racers[b].Car.Distance, da = _racers[a].Car.Distance;
            return db > da || (db == da && b < a);
        }

        void ResolveRearContacts()
        {
            // Önden arkaya: öndeki önce kendi önündekine göre sınırlanır, arkadaki onun yeni hızını görür.
            int n = SortByPosition(includeAll: false);
            _nextContacts.Clear();
            var bumps = new List<int>();
            for (int fi = 0; fi < n; fi++)
            {
                int a = _order[fi];
                LaneOf(a, out int laneA, out int countA);
                for (int li = 0; li < fi; li++)
                {
                    int b = _order[li];
                    LaneOf(b, out int laneB, out int countB);
                    if (laneA != laneB || countA != countB) continue;
                    float gap = _racers[b].Car.Distance - _racers[a].Car.Distance;
                    if (!(gap >= 0f && gap < _tuning.CarLength) || !IsAhead(b, a)) continue;

                    _racers[a].Car.LimitSpeed(_racers[b].Car.Speed * _tuning.RearHitSpeedFactor);
                    int key = a * LaneMap.HomeLanes + b;
                    _nextContacts.Add(key);
                    if (!_rearContacts.Contains(key))
                    {
                        _racers[a].Car.RaiseEvent(SimEventKind.RearHit);
                        _racers[b].Car.RaiseEvent(SimEventKind.Bumped);
                        bumps.Add(b);
                    }
                }
            }
            foreach (int b in bumps) _racers[b].Car.AddSpeed(_tuning.RearBump);

            var t = _rearContacts; _rearContacts = _nextContacts; _nextContacts = t;
        }

        void ResolveSideContacts()
        {
            for (int a = 0; a < _racers.Length; a++)
            {
                if (!InContact(a)) continue;
                var carA = _racers[a].Car;
                int skidPiece = -1;
                foreach (var e in carA.Events)
                    if (e.Kind == SimEventKind.Skid) { skidPiece = e.PieceIndex; break; }
                if (skidPiece < 0) continue;

                var def = _layout.SpanAt(skidPiece).Def;
                if (def.TurnSign == 0) continue;
                int spill = LaneMap.EffectiveLane(_racers[a].HomeLane, def.LaneCount) - def.TurnSign;
                if (spill < 0 || spill >= def.LaneCount) continue;

                for (int b = 0; b < _racers.Length; b++)
                {
                    if (b == a || !InContact(b)) continue;
                    LaneOf(b, out int laneB, out int countB);
                    if (countB != def.LaneCount || laneB != spill) continue;
                    if (!(Math.Abs(_racers[b].Car.Distance - carA.Distance) < _tuning.CarLength)) continue;

                    float keep = 1f - _tuning.SideHitSpeedLoss;
                    carA.ScaleSpeed(keep);
                    _racers[b].Car.ScaleSpeed(keep);
                    carA.RaiseEvent(SimEventKind.SideHit);
                    _racers[b].Car.RaiseEvent(SimEventKind.SideHit);
                }
            }
        }

        // _order'ı doldurur. includeAll: sıralama için herkes (önce bitirenler FinishTick'e göre, sonra mesafe);
        // değilse yalnızca temasa girebilen araçlar, mesafeye göre önden arkaya. Dönüş: dolu eleman sayısı.
        int SortByPosition(bool includeAll)
        {
            int n = 0;
            for (int i = 0; i < _racers.Length; i++)
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

        void UpdateLateralAndRank()
        {
            for (int i = 0; i < _racers.Length; i++)
                _racers[i].Lateral = LaneMap.LateralAt(_layout, _racers[i].HomeLane, _racers[i].Car.Distance, _tuning);
            int n = SortByPosition(includeAll: true);
            for (int k = 0; k < n; k++) _racers[_order[k]].Rank = k + 1;
        }
    }
}
