EQ Legends Companion v1.4 — Manual buffs + ranked HoT learning

Copy every .cs file into:
    src\EQSpellTimer

Replace existing files.

Delete obsolete files from the project:
    LearningEngine.cs
    FadeLearningEngine.cs
    Any duplicate MainForm/TimerEngine files

Then:
    Build > Clean Solution
    Build > Rebuild Solution

Behavior:
- Normal buffs use manually entered M:SS durations.
- No buff landing/fade learning popups.
- Buff landing patterns are still data-driven with || and {target}.
- HoTs retain automatic Legends-specific detection.
- HoT durations are learned by exact ranked spell name.
- Example: Snails Healing I, II, and III each keep separate observations.
- Learned values are saved in hot-durations.json beside the application.
- The latest median of up to 9 observations is used.
- The checkbox "Learn HoT durations by rank" enables/disables this.
