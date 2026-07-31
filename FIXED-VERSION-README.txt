EQ Legends Companion v1.4.1 — Fixed Source

This is a clean, internally consistent project copy.

What changed:
- Normal buff Learning Mode was removed.
- Normal buffs use the M:SS duration entered in Spell Setup.
- Landing patterns remain manual and support:
      self pattern || {target} pattern
- Automatic HoT detection remains.
- Rank-specific HoT duration learning remains and saves to:
      hot-durations.json

Open:
    EQSpellTimer.sln

Then use:
    Build > Clean Solution
    Build > Rebuild Solution

If replacing files in an older project folder, first delete:
    LearningEngine.cs
    FadeLearningEngine.cs
    Any duplicate MainForm or TimerEngine files

The cleanest method is to extract this ZIP to a new folder and open its
solution directly.
