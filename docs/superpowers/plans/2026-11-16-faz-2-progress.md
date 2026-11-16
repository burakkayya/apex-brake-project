# Ledger — plan: docs/superpowers/plans/2026-11-16-faz-2-yaris.md

Ruling: Task 0 — MultipleBoundariesInOneTick_AllEvaluated (changed) and the two PlayMode tests (direction continuity in Build_PiecesAreContinuous, new Build_CornerExit_RotatesNextPiece) already passed before any code change — the existing code was already correct there; only the tests were strengthened — cost if wrong: none (tests remain as regression guards).
Ruling: Task 0 — Respawn walk-back iterates Straight pieces backwards from the crashed piece (not "first Straight whose Start < point"), so the existing Respawn_NeverLandsInsideAnotherObstacle expectation (80) is preserved — the plan requires that expectation to hold — cost if wrong: a respawn could land slightly ahead of the computed run-up point when the nearest earlier Straight starts after it (only possible for corners, where no run-up condition applies).
Task 0: complete — EditMode 51/51, PlayMode 7/7
