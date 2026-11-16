using System;
using System.Collections.Generic;
using UnityEngine;
using ApexBrake.Simulation;
using ApexBrake.Track;

namespace ApexBrake.Racing
{
    /// <summary>
    /// Yarış yöneticisi: 5 fizik aracı kurar, geri sayımı yürütür, ilerlemeyi dünya konumunun izdüşümünden ölçer,
    /// sırayı tutar ve tek oyun kuralını uygular: yol dışına çıkan, düşen ya da ters dönen araç oyun dışıdır (spec §12.2).
    /// Hiçbir hız eşiği kullanmaz; aracın konumunu yalnızca start diziliminde ve yeniden doğmada yazar.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class RaceManager : MonoBehaviour
    {
        public TrackBuilder track;
        public TouchBrakeInput input;
        public OutPolicy playerPolicy = OutPolicy.Eliminate;
        public OutPolicy botPolicy = OutPolicy.Eliminate;

        [Header("Araçlar")]
        [Tooltip("Boşsa VehicleBuilder ile vehicleConfig'ten kurulur")]
        public GameObject carPrefab;
        public VehicleConfig vehicleConfig;
        [Tooltip("Araç başına yükseltme (dizin = yarışçı); boşsa taban")]
        public VehicleUpgrades[] racerUpgrades;
        public float startDistance = 10f;

        public const int PlayerIndex = 0;
        public const int RacerCount = 5;
        public const float CountdownSeconds = 3f;
        public const float EndDelaySeconds = 1.5f;
        public const float OutActionDelay = 1.5f;     // oyun dışı kesinleştikten sonra yeniden doğma / çarpışmayı kapatma
        public const float RespawnBack = 30f;

        static readonly string[] Names = { "Sen", "Master", "Balanced", "Reckless", "Slow" };
        static readonly int[] Lanes = { 2, 0, 1, 3, 4 };
        static readonly Color[] Colors =
        {
            new Color(0.85f, 0.12f, 0.10f), new Color(0.15f, 0.35f, 0.85f), new Color(0.95f, 0.80f, 0.15f),
            new Color(0.20f, 0.70f, 0.30f), new Color(0.55f, 0.25f, 0.75f),
        };

        readonly List<RacerEntry> _racers = new List<RacerEntry>();
        readonly SimTuning _lanes = new SimTuning();
        int _generation;
        float _raceTime;
        float _endTimer = -1f;

        public IReadOnlyList<RacerEntry> Racers => _racers;
        public RaceState State { get; private set; }
        public float CountdownRemaining { get; private set; }
        /// <summary>Oyuncunun yarış süresi; oyuncu bitirince ya da elenince durur.</summary>
        public float ElapsedTime { get; private set; }
        public RacerEntry Player => _racers.Count > 0 ? _racers[PlayerIndex] : null;

        public event Action<int, OutReason> RacerOut;
        public event Action<int> RacerRespawned;
        public event Action<int> RacerFinished;
        /// <summary>(a, b, bağıl hız m/s) — yalnızca HUD/kamera için.</summary>
        public event Action<int, int, float> RacerContact;
        public event Action Restarted;
        public event Action Ended;

        void Start()
        {
            if (track != null && track.Path == null) track.Build();
            Restart();
        }

        // ---- kurulum ----------------------------------------------------------------

        void EnsureRacers()
        {
            if (_racers.Count == RacerCount) return;
            var cfg = vehicleConfig != null ? vehicleConfig : ScriptableObject.CreateInstance<VehicleConfig>();
            for (int i = 0; i < RacerCount; i++)
            {
                VehicleMotor motor;
                if (carPrefab != null)
                {
                    motor = Instantiate(carPrefab).GetComponent<VehicleMotor>();
                    motor.name = "Car_" + Names[i];
                    Tint(motor, Colors[i]);
                }
                else motor = VehicleBuilder.Create(cfg, Colors[i], "Car_" + Names[i]);
                var relay = motor.gameObject.AddComponent<RacerContactRelay>();
                relay.Manager = this; relay.Index = i;
                var e = new RacerEntry
                {
                    Index = i, Name = Names[i], IsPlayer = i == PlayerIndex, HomeLane = Lanes[i],
                    Motor = motor, Driver = motor.GetComponent<LineDriver>(), Telemetry = motor.GetComponent<VehicleTelemetry>(),
                    Colliders = motor.GetComponentsInChildren<Collider>(true),
                };
                e.Driver.Path = track.Path;
                _racers.Add(e);
            }
        }

        static void Tint(VehicleMotor m, Color c)
        {
            var body = m.transform.Find("Body");
            if (body == null) return;
            var r = body.GetComponent<Renderer>();
            if (r == null) return;
            var mat = r.material;   // örnek malzeme
            mat.color = c;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        }

        float HomeLateral(RacerEntry e, float distance) => LaneMap.LateralAt(track.Layout, e.HomeLane, distance, _lanes);

        /// <summary>Her şeyi başa alır: araçlar start çizgisinde, motorlar kapalı, geri sayım 3 sn. Bekleyen olaylar düşer.</summary>
        public void Restart()
        {
            _generation++;
            if (track == null) return;
            if (track.Path == null) track.Build();
            EnsureRacers();
            SetAllCollisions(true);
            foreach (var e in _racers)
            {
                e.Finished = false; e.FinishTime = 0f; e.IsOut = false; e.OutReason = OutReason.None; e.IsRespawning = false;
                e.OutElapsed = 0f; e.CollisionsDisabled = false; e.Detector.Reset();
                e.Policy = e.IsPlayer ? playerPolicy : botPolicy;
                e.Driver.Path = track.Path;
                var ups = racerUpgrades != null && e.Index < racerUpgrades.Length ? racerUpgrades[e.Index] : VehicleUpgrades.Base;
                e.Motor.Apply(ups);
                e.Motor.Brake = false;
                Place(e, startDistance);
                e.Motor.EngineOn = false;
            }
            State = RaceState.Countdown;
            CountdownRemaining = CountdownSeconds;
            ElapsedTime = 0f;
            _raceTime = 0f;
            _endTimer = -1f;
            UpdateRanks();
            Restarted?.Invoke();
        }

        void Place(RacerEntry e, float distance)
        {
            var path = track.Path;
            float lat = HomeLateral(e, distance);
            var f = path.ForwardAt(distance); f.y = 0f;
            e.Driver.TargetLateral = lat;
            e.Motor.PlaceAt(path.PointAt(distance, lat), Quaternion.LookRotation(f.normalized, Vector3.up));
            e.Distance = distance;
            e.Lateral = lat;
        }

        void SetAllCollisions(bool enabled)
        {
            for (int a = 0; a < _racers.Count; a++)
                for (int b = a + 1; b < _racers.Count; b++)
                    SetCollision(_racers[a], _racers[b], enabled);
        }

        static void SetCollision(RacerEntry a, RacerEntry b, bool enabled)
        {
            foreach (var ca in a.Colliders)
                foreach (var cb in b.Colliders)
                    if (ca != null && cb != null) Physics.IgnoreCollision(ca, cb, !enabled);
        }

        // ---- yarış döngüsü ------------------------------------------------------------

        void FixedUpdate()
        {
            if (_racers.Count == 0 || track == null || track.Path == null) return;
            float dt = Time.fixedDeltaTime;
            int gen = _generation;

            if (State == RaceState.Countdown)
            {
                CountdownRemaining -= dt;
                if (CountdownRemaining <= 1e-4f)
                {
                    CountdownRemaining = 0f;
                    State = RaceState.Racing;
                    foreach (var e in _racers) e.Motor.EngineOn = !e.IsOut;
                }
            }
            else
            {
                _raceTime += dt;
                var p = Player;
                bool playerDone = p.Finished || (p.IsOut && p.Policy != OutPolicy.Respawn);
                if (!playerDone) ElapsedTime += dt;
                if (_endTimer >= 0f && State == RaceState.Racing)
                {
                    _endTimer -= dt;
                    if (_endTimer <= 1e-4f)
                    {
                        State = RaceState.Ended;
                        Ended?.Invoke();
                        if (gen != _generation) return;
                    }
                }
            }

            float total = track.Path.TotalLength;
            for (int i = 0; i < _racers.Count; i++)
            {
                var e = _racers[i];
                if (!e.IsOut) e.Distance = e.Driver.Distance;   // oyun dışı araç mesafesini korur
                e.Lateral = e.Driver.Lateral;
                e.Driver.TargetLateral = e.TargetLateralOverride ?? HomeLateral(e, e.Distance);

                if (State == RaceState.Countdown) { e.Motor.EngineOn = false; continue; }

                if (!e.IsOut && !e.Finished && e.Distance >= total - 0.01f)
                {
                    e.Finished = true;
                    e.FinishTime = _raceTime;
                    RacerFinished?.Invoke(i);
                    if (gen != _generation) return;
                    if (e.IsPlayer && _endTimer < 0f) _endTimer = EndDelaySeconds;
                }

                if (e.Finished) { e.Motor.Brake = true; continue; }   // çizgiden sonra sürücü durur
                if (e.IsPlayer && !e.IsOut) e.Motor.Brake = input != null && input.IsBraking;

                if (!e.IsOut)
                {
                    var com = e.Motor.Body.worldCenterOfMass;
                    var pose = track.Path.Project(com, e.Distance);
                    var reason = e.Detector.Step(dt, e.Telemetry, pose.HeightAboveRoad, OutDetector.RoadBelow(com));
                    if (reason != OutReason.None)
                    {
                        GoOut(e, reason);
                        RacerOut?.Invoke(i, reason);
                        if (gen != _generation) return;
                        if (e.IsPlayer && e.Policy != OutPolicy.Respawn && _endTimer < 0f) _endTimer = EndDelaySeconds;
                    }
                    continue;
                }

                e.OutElapsed += dt;
                if (e.OutElapsed < OutActionDelay - 1e-4f) continue;
                if (e.Policy == OutPolicy.Respawn)
                {
                    Respawn(e);
                    RacerRespawned?.Invoke(i);
                    if (gen != _generation) return;
                }
                else if (!e.CollisionsDisabled)
                {
                    foreach (var o in _racers) if (o != e) SetCollision(e, o, false);
                    e.CollisionsDisabled = true;
                }
            }
            UpdateRanks();
        }

        void GoOut(RacerEntry e, OutReason reason)
        {
            e.IsOut = true;
            e.OutReason = reason;
            e.OutElapsed = 0f;
            e.IsRespawning = e.Policy == OutPolicy.Respawn;
            e.Motor.EngineOn = false;
            e.Motor.Brake = false;
        }

        void Respawn(RacerEntry e)
        {
            float d = Mathf.Max(startDistance, e.Distance - RespawnBack);
            // Rampa yükselişi/boşluğu ya da tümsek üstüne düşüyorsa ondan önceki düz yüzeye.
            for (int k = 0; k < track.Layout.PieceCount; k++)
            {
                var s = track.Layout.SpanAt(k);
                if (s.Def.Kind != PieceKind.Ramp && s.Def.Kind != PieceKind.Bump) continue;
                if (d > s.Start - 4f && d < s.End) d = Mathf.Max(0f, s.Start - 6f);
            }
            if (e.CollisionsDisabled) { foreach (var o in _racers) if (o != e) SetCollision(e, o, true); e.CollisionsDisabled = false; }
            Place(e, d);
            e.IsOut = false;
            e.OutReason = OutReason.None;
            e.IsRespawning = false;
            e.OutElapsed = 0f;
            e.Detector.Reset();
            e.Motor.EngineOn = State != RaceState.Countdown;
        }

        void UpdateRanks()
        {
            var order = new List<RacerEntry>(_racers);
            order.Sort((a, b) =>
            {
                if (a.Finished != b.Finished) return a.Finished ? -1 : 1;
                if (a.Finished && a.FinishTime != b.FinishTime) return a.FinishTime.CompareTo(b.FinishTime);
                int c = b.Distance.CompareTo(a.Distance);
                return c != 0 ? c : a.Index.CompareTo(b.Index);
            });
            for (int r = 0; r < order.Count; r++) order[r].Rank = r + 1;
        }

        internal void ReportContact(int a, int b, float relativeSpeed)
        {
            if (State == RaceState.Countdown) return;
            RacerContact?.Invoke(a, b, relativeSpeed);
        }

        void OnDestroy()
        {
            foreach (var e in _racers) if (e.Motor != null) Destroy(e.Motor.gameObject);
        }
    }
}
