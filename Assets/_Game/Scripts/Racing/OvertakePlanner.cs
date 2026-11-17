using System;

namespace ApexBrake.Racing
{
    /// <summary>Planlayıcının gördüğü araç: pist üzerindeki mesafe, yanal konum (+ sağ), ileri hız. Active = yarışta (oyun dışı/bitmiş değil).</summary>
    public struct TrafficCar
    {
        public float Distance, Lateral, Speed;
        public bool Active;
    }

    /// <summary>
    /// Sollama (Faz 2b kuralı, girdiler artık gerçek araçların mesafe/yanal konum/hızı): önde, izlediği çizgiye yakın ve ondan
    /// belirgin yavaş bir araç varsa ve yanında yer varsa hedef çizgi o aracın yanına kayar; yer yoksa arkada kalınır.
    /// Hedef en az <see cref="Hold"/> sn değişmez ve hiçbir zaman yol kenarına <see cref="EdgeMargin"/>'dan yakın olmaz.
    /// Oyuncu ve botlar için aynıdır. Yalnızca izlenecek çizgiyi seçer; aracı hareket ettiren direksiyon ve fiziktir.
    /// Saf mantık (Unity'siz): EditMode'da test edilir.
    /// </summary>
    public sealed class OvertakePlanner
    {
        public const float DefaultEdgeMargin = 1.3f;
        public const float DefaultHold = 0.5f;

        public float Look = 25f;            // m; öndeki araca bakış
        public float CarLength = 4.5f;
        public float CarWidth = 2f;
        public float Gap = 0.8f;            // m; geçerken yanal boşluk
        public float MinSpeedDiff = 1.5f;   // m/s; "belirgin yavaş"
        public float Hold = DefaultHold;
        public float EdgeMargin = DefaultEdgeMargin;
        public float NearWindow = 10f;      // m; geçiş çizgisinde bu kadar yakın araç varsa yer yok

        struct State { public bool Overtaking; public int Blocker; public float Target; public float HoldTimer; public int Count; }
        State[] _s;

        public OvertakePlanner(int count) { _s = new State[Math.Max(0, count)]; Reset(); }

        public void Reset()
        {
            for (int i = 0; i < _s.Length; i++) _s[i] = new State { Blocker = -1 };
        }

        public bool IsOvertaking(int i) => i >= 0 && i < _s.Length && _s[i].Overtaking;
        /// <summary>Bu yarışta başlatılan sollama sayısı.</summary>
        public int OvertakeCount(int i) => i >= 0 && i < _s.Length ? _s[i].Count : 0;

        /// <summary>
        /// <paramref name="i"/> numaralı aracın izleyeceği çizgi. <paramref name="home"/>: ev çizgisi; <paramref name="halfWidth"/>:
        /// önündeki yolun (banket hariç) en dar yarı genişliği. Her fizik adımında bir kez çağrılır.
        /// </summary>
        public float Plan(int i, float home, float halfWidth, TrafficCar[] cars, float dt)
        {
            ref var me = ref _s[i];
            float limit = Math.Max(0f, halfWidth - EdgeMargin);
            if (!cars[i].Active)
            {
                me.Overtaking = false; me.Blocker = -1; me.HoldTimer = 0f;
                return home;
            }
            if (me.HoldTimer > 0f) me.HoldTimer -= dt;

            if (me.Overtaking)
            {
                if (me.HoldTimer > 0f) return Clamp(me.Target, limit);
                int b = me.Blocker;
                var bc = cars[b];
                float ahead = bc.Distance - cars[i].Distance;
                bool passed = !bc.Active || -ahead >= CarLength + 2f;
                bool gaveUp = !passed && ahead >= CarLength && !IsBlocker(i, b, home, cars);
                if (passed || gaveUp || Math.Abs(me.Target) > limit + 1e-4f && !TryPickSide(i, b, home, limit, cars, out _))
                {
                    me.Overtaking = false; me.Blocker = -1; me.HoldTimer = Hold;
                    return home;
                }
                // Seçilen yan yoldan taştıysa ya da başka bir araçla doldu ise yeniden seç; yer yoksa sürdür.
                if (!CandidateValid(i, b, me.Target, limit, cars) && TryPickSide(i, b, home, limit, cars, out float t)
                    && Math.Abs(t - me.Target) > 1e-3f)
                {
                    me.Target = t; me.HoldTimer = Hold;
                }
                return Clamp(me.Target, limit);
            }

            if (me.HoldTimer <= 0f)
            {
                int b = FindBlocker(i, home, cars);
                if (b >= 0 && TryPickSide(i, b, home, limit, cars, out float t))
                {
                    me.Overtaking = true; me.Blocker = b; me.Target = t; me.HoldTimer = Hold; me.Count++;
                    return t;
                }
            }
            return home;
        }

        static float Clamp(float v, float limit) => v < -limit ? -limit : (v > limit ? limit : v);

        bool IsBlocker(int i, int j, float home, TrafficCar[] cars)
        {
            if (j == i || !cars[j].Active) return false;
            var me = cars[i]; var o = cars[j];
            float dD = o.Distance - me.Distance;
            return dD > 0f && dD <= Look
                && Math.Abs(o.Lateral - home) < CarWidth + 0.4f
                && o.Speed < me.Speed - MinSpeedDiff;
        }

        int FindBlocker(int i, float home, TrafficCar[] cars)
        {
            int best = -1; float bestD = float.MaxValue;
            for (int j = 0; j < cars.Length; j++)
            {
                if (!IsBlocker(i, j, home, cars)) continue;
                float dD = cars[j].Distance - cars[i].Distance;
                if (dD < bestD) { bestD = dD; best = j; }
            }
            return best;
        }

        bool TryPickSide(int i, int blocker, float home, float limit, TrafficCar[] cars, out float target)
        {
            float bl = cars[blocker].Lateral;
            float off = CarWidth + Gap;
            float left = bl - off, right = bl + off;
            bool lv = CandidateValid(i, blocker, left, limit, cars), rv = CandidateValid(i, blocker, right, limit, cars);
            if (lv && rv) target = Math.Abs(left - home) <= Math.Abs(right - home) ? left : right;
            else if (lv) target = left;
            else if (rv) target = right;
            else { target = home; return false; }
            return true;
        }

        bool CandidateValid(int i, int blocker, float cand, float limit, TrafficCar[] cars)
        {
            if (Math.Abs(cand) > limit + 1e-4f) return false;
            var me = cars[i];
            for (int j = 0; j < cars.Length; j++)
            {
                if (j == i || j == blocker || !cars[j].Active) continue;
                var o = cars[j];
                bool near = Math.Abs(o.Distance - me.Distance) < NearWindow
                    || (blocker >= 0 && Math.Abs(o.Distance - cars[blocker].Distance) < NearWindow);
                if (near && Math.Abs(o.Lateral - cand) < CarWidth) return false;
            }
            return true;
        }
    }
}
