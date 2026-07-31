# EQ Legends Spell Timer — C# Edition

Native Windows spell timer for EverQuest Legends. It tails the live EQ log, detects supported HoTs and configurable buffs, and displays smooth countdown timers without PowerShell or a background console window.

## Included now

- Native .NET 8 WinForms application (`WinExe`, so no command prompt)
- Real-time log tailing with `FileSystemWatcher`
- Log-timestamp-based timer starts
- Druid and Shaman HoT family/rank detection
- Shared HoT slot per target (new HoT replaces the old one)
- First-tick synchronization for roughly one-second accuracy
- Alacrity self-buff support (`You feel much faster.`)
- Editable spell setup stored in `spells.json`
- Activity log and remembered EQ log path
- Single-file, self-contained Windows publishing

## Build it

### Visual Studio

1. Install **Visual Studio 2022 Community**.
2. Select the **.NET desktop development** workload.
3. Open `EQSpellTimer.sln`.
4. Press **F5**.

### Command line

Install the .NET 8 SDK, then run:

```powershell
dotnet run --project .\src\EQSpellTimer\EQSpellTimer.csproj
```

## Make the distributable EXE

Run:

```powershell
.\publish-win-x64.ps1
```

The finished application will be in:

```text
publish\win-x64\EQSpellTimer.exe
```

No PowerShell or .NET installation is required on the player's PC because the release is self-contained.

## GitHub

Copy this entire folder into your GitHub repository, then:

```powershell
git add .
git commit -m "Initial C# rewrite"
git push
```

The included GitHub Actions workflow builds a Windows ZIP whenever you push a version tag such as `v1.0.0`.
