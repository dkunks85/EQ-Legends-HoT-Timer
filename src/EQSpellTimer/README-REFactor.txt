EQ Legends Companion - Refactored engine

Replace these existing files:
  ConfigStore.cs
  MainForm.cs
  Models.cs
  TimerEngine.cs

Add these new files to the same project folder:
  BuffTracker.cs
  CastTracker.cs
  EngineContext.cs
  HotTracker.cs
  LearningEngine.cs
  LogMessage.cs
  PatternMatcher.cs

Important behavior:
- Automatic HoTs are processed before Learning Mode.
- Flowering Heal and the other HoT families should never trigger a learning popup.
- HoTs are tracked from you and visible other players.
- Normal buffs are tracked only when cast by you, but may target you, another player, or a pet.
- Learning Mode handles only manual non-HoT self-casts whose landing pattern is blank.
- Clicking Learn saves the suggested pattern and starts the timer on that same cast.
- Spell Setup has horizontal and vertical scrolling.
- Minimum window size remains 640 x 500.

Build:
1. Copy the files into src\EQSpellTimer.
2. In Visual Studio, use Project > Add Existing Item for the new .cs files if they do not appear automatically.
3. Delete or exclude the old TimerEngine file if Visual Studio shows two copies.
4. Build with Ctrl+Shift+B.
