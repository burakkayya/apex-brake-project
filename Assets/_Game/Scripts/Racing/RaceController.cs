using System;
using System.Collections.Generic;
using UnityEngine;
using ApexBrake.Simulation;
using ApexBrake.Track;

namespace ApexBrake.Racing
{
    public enum RaceState { Countdown, Racing, Ended }

    /// <summary>5 araçlı yarışı sürer: geri sayım, sabit adımlı RaceSim, bitiş gecikmesi, yeniden başlatma.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed class RaceController : MonoBehaviour
    {
        public TrackBuilder track;
        public TouchBrakeInput input;

        public const int PlayerIndex = 0;
        public const float CountdownSeconds = 3f;
        public const float EndDelaySeconds = 1.5f;

        public RaceSim Race { get; private set; }
        public SimClock Clock { get; private set; }
        public SimTuning Tuning { get; private set; }
        public RaceState State { get; private set; }
        public float CountdownRemaining { get; private set; }   // 3 → 0
        public float ElapsedTime { get; private set; }          // oyuncu bitirince/elenince durur

        public event Action<int, SimEvent> RacerEvent;          // (yarışçı indeksi, olay)
        public event Action Restarted;
        public event Action Ended;

        float[] _prevDistance = new float[0];
        float[] _prevLateral = new float[0];
        readonly List<bool> _braking = new List<bool>();
        int _endTicks = -1;      // oyuncu bitti/elendi: kalan bitiş gecikmesi (Tick); -1 = henüz değil
        int _generation;         // Restart sayacı: eski yarışın olay döngüsü yeni yarışa sızmasın

        public float PrevDistance(int racer) => _prevDistance[racer];
        public float PrevLateral(int racer) => _prevLateral[racer];

        public Racer Player => Race != null ? Race.Racers[PlayerIndex] : null;

        void Start()
        {
            Application.targetFrameRate = 60;
            track.Build();
            Restart();
        }

        public void Restart()
        {
            _generation++;
            Tuning = new SimTuning();
            uint baseSeed = unchecked((uint)Environment.TickCount);
            var bots = new[] { BotProfile.Master(), BotProfile.Balanced(), BotProfile.Reckless(), BotProfile.Slow() };
            var botLanes = new[] { 0, 1, 3, 4 };
            var setups = new List<RacerSetup>
            {
                new RacerSetup { Name = "Sen", HomeLane = 2, Stats = new CarStats(), Policy = CrashPolicy.Eliminate },
            };
            for (int i = 0; i < bots.Length; i++)
                setups.Add(new RacerSetup
                {
                    Name = bots[i].Name, HomeLane = botLanes[i], Stats = bots[i].ToStats(), Policy = CrashPolicy.Eliminate,
                    Bot = bots[i], Seed = unchecked(baseSeed * 2654435761u + (uint)(i + 1) * 40503u),
                });

            Race = new RaceSim(track.Layout, Tuning, setups);
            Clock = new SimClock();
            int n = Race.Racers.Count;
            _prevDistance = new float[n];
            _prevLateral = new float[n];
            SyncPrev();
            _braking.Clear();
            for (int i = 0; i < n; i++) _braking.Add(false);

            State = RaceState.Countdown;
            CountdownRemaining = CountdownSeconds;
            ElapsedTime = 0f;
            _endTicks = -1;
            Restarted?.Invoke();
        }

        void SyncPrev()
        {
            for (int i = 0; i < Race.Racers.Count; i++)
            {
                _prevDistance[i] = Race.Racers[i].Car.Distance;
                _prevLateral[i] = Race.Racers[i].Lateral;
            }
        }

        void Update()
        {
            if (Race == null) return;
            float dt = Time.unscaledDeltaTime;

            if (State == RaceState.Countdown)
            {
                CountdownRemaining -= dt;
                if (CountdownRemaining > 0f) return;
                CountdownRemaining = 0f;
                State = RaceState.Racing;
                Clock = new SimClock();
                return;
            }
            if (State != RaceState.Racing) return;

            int gen = _generation;
            int ticks = Clock.Advance(dt);
            for (int t = 0; t < ticks; t++)
            {
                SyncPrev();
                var player = Race.Racers[PlayerIndex].Car;
                bool running = player.Phase != CarPhase.Finished && !player.IsOut;
                _braking[PlayerIndex] = running && input != null && input.IsBraking;

                Race.Tick(_braking);
                if (running) ElapsedTime += CarSim.Step;

                for (int r = 0; r < Race.Racers.Count; r++)
                {
                    var events = Race.Racers[r].Car.Events;
                    for (int e = 0; e < events.Count; e++)
                    {
                        // Yeniden doğan araç geri kaymasın: önceki konum anında yeni konuma eşitlenir.
                        if (events[e].Kind == SimEventKind.Respawned)
                        {
                            _prevDistance[r] = Race.Racers[r].Car.Distance;
                            _prevLateral[r] = Race.Racers[r].Lateral;
                        }
                        RacerEvent?.Invoke(r, events[e]);
                        if (gen != _generation) return; // olay dinleyicisi yarışı yeniden başlattı
                    }
                }

                if (_endTicks < 0 && (player.Phase == CarPhase.Finished || player.IsOut))
                    _endTicks = Mathf.RoundToInt(EndDelaySeconds / CarSim.Step);
                if (_endTicks >= 0 && _endTicks-- == 0)
                {
                    State = RaceState.Ended;
                    Ended?.Invoke();
                    return;
                }
            }
        }
    }
}
