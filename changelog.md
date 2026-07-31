# Changelog

## v1.4.1
- Removed obsolete normal-buff Learning Mode source files.
- Fixed compile errors involving LearningCandidate and LearningDecision.
- Removed stale LearnSelfPattern/LearnTargetPattern assignments.
- Removed stale indefinite-buff UI code.
- Normal buffs now use manually entered durations and configured landing patterns.
- Retained automatic, rank-specific HoT duration learning.
- Retained responsive timer cards, pet/player targets, and death cleanup.

EQ Legends Spell Timer v7.1

- Uses the duration you enter as the authoritative spell duration.
- Anchors timer starts to timestamps written in the EverQuest log.
- HoTs resynchronize once on their first server tick for roughly one-second accuracy.
- Final Trigger lines remove HoT timers immediately.
- Removed adaptive duration learning from active timer calculations.
- Hidden VBS launcher still prevents a PowerShell window from remaining open.
