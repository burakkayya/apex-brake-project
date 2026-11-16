using System;
using System.Collections.Generic;

namespace ApexBrake.Simulation
{
    public readonly struct PieceSpan
    {
        public readonly int Index;
        public readonly TrackPieceDef Def;
        public readonly float Start;
        public readonly float End;

        public PieceSpan(int index, TrackPieceDef def, float start, float end)
        {
            Index = index; Def = def; Start = start; End = end;
        }
    }

    public sealed class TrackLayout
    {
        readonly PieceSpan[] _spans;

        public float TotalLength { get; }
        public int PieceCount => _spans.Length;

        public TrackLayout(IReadOnlyList<TrackPieceDef> pieces)
        {
            Validate(pieces);
            _spans = new PieceSpan[pieces.Count];
            float d = 0f;
            for (int i = 0; i < pieces.Count; i++)
            {
                _spans[i] = new PieceSpan(i, pieces[i], d, d + pieces[i].Length);
                d += pieces[i].Length;
            }
            TotalLength = d;
        }

        public PieceSpan SpanAt(int index) => _spans[index];

        public PieceSpan SpanAtDistance(float distance)
        {
            if (!(distance > 0f)) return _spans[0];
            if (distance >= TotalLength) return _spans[_spans.Length - 1];
            int lo = 0, hi = _spans.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (_spans[mid].Start <= distance) lo = mid; else hi = mid - 1;
            }
            return _spans[lo];
        }

        public bool TryGetNextObstacle(float distance, out PieceSpan span)
        {
            for (int i = 0; i < _spans.Length; i++)
            {
                if (_spans[i].Def.Kind != PieceKind.Straight && _spans[i].Start >= distance)
                {
                    span = _spans[i];
                    return true;
                }
            }
            span = default;
            return false;
        }

        static void Validate(IReadOnlyList<TrackPieceDef> pieces)
        {
            if (pieces == null || pieces.Count == 0)
                throw new ArgumentException("Layout rule: piece list must not be empty.");
            for (int i = 0; i < pieces.Count; i++)
                if (pieces[i] == null) throw new ArgumentException($"Layout rule: piece {i} must not be null.");
            if (pieces[0].Kind != PieceKind.Straight)
                throw new ArgumentException("Layout rule: first piece must be a Straight.");
            if (pieces[pieces.Count - 1].Kind != PieceKind.Straight)
                throw new ArgumentException("Layout rule: last piece must be a Straight.");
            for (int i = 0; i < pieces.Count; i++)
            {
                var p = pieces[i];
                if (!LaneMap.IsValidLaneCount(p.LaneCount))
                    throw new ArgumentException($"Layout rule: piece {i} LaneCount must be 1, 2, 3 or 5.");
                if (p.LaneCount < LaneMap.HomeLanes && p.Kind != PieceKind.Straight)
                    throw new ArgumentException($"Layout rule: piece {i} ({p.Kind}) must have 5 lanes; only Straight pieces may narrow.");
                if (i == 0 && p.LaneCount != LaneMap.HomeLanes)
                    throw new ArgumentException("Layout rule: first piece must have 5 lanes.");
                if (!(p.Length > 0f)) throw new ArgumentException($"Layout rule: piece {i} Length must be > 0.");
                if ((p.Kind == PieceKind.Corner || p.Kind == PieceKind.Bump) && !(p.SafeSpeed > 0f))
                    throw new ArgumentException($"Layout rule: piece {i} ({p.Kind}) SafeSpeed must be > 0.");
                if (p.Kind == PieceKind.Ramp)
                {
                    if (!(p.MinJumpSpeed > 0f && p.MinJumpSpeed < p.SafeSpeed))
                        throw new ArgumentException($"Layout rule: piece {i} (Ramp) requires 0 < MinJumpSpeed < SafeSpeed.");
                    if (!(p.GapLength > 0f && p.GapLength < p.Length))
                        throw new ArgumentException($"Layout rule: piece {i} (Ramp) requires 0 < GapLength < Length.");
                }
            }
        }
    }
}
