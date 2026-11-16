using System;
using UnityEngine;
using ApexBrake.Simulation;
using ApexBrake.Track;

namespace ApexBrake.Racing
{
    public sealed class CarController : MonoBehaviour
    {
        public TrackBuilder track;
        public TouchBrakeInput input;
        public CrashPolicy policy = CrashPolicy.Eliminate;

        public CarSim Sim { get; private set; }
        public SimClock Clock { get; private set; }
        public SimTuning Tuning { get; private set; }
        public float PrevDistance { get; private set; }
        public float ElapsedTime { get; private set; }
        public SimEventKind? LastEvent { get; private set; }
        public bool IsBraking { get; private set; }

        public event Action<SimEvent> SimEventRaised;
        public event Action Restarted;

        void Start()
        {
            Application.targetFrameRate = 60;
            track.Build();
            Restart();
        }

        public void Restart()
        {
            Tuning = new SimTuning();
            Sim = new CarSim(track.Layout, new CarStats(), Tuning, policy);
            Clock = new SimClock();
            PrevDistance = 0f;
            ElapsedTime = 0f;
            LastEvent = null;
            Restarted?.Invoke();
        }

        void Update()
        {
            if (Sim == null) return;
            IsBraking = input != null && input.IsBraking;

            int ticks = Clock.Advance(Time.unscaledDeltaTime);
            for (int i = 0; i < ticks; i++)
            {
                PrevDistance = Sim.Distance;
                bool running = Sim.Phase != CarPhase.Finished && !Sim.IsOut;
                Sim.Tick(IsBraking);
                if (running) ElapsedTime += CarSim.Step;
                var events = Sim.Events;
                for (int e = 0; e < events.Count; e++)
                {
                    LastEvent = events[e].Kind;
                    SimEventRaised?.Invoke(events[e]);
                }
            }
        }
    }
}
