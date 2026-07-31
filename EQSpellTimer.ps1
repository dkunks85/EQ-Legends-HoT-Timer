# EverQuest Legends Spell Timer v7.1
# Windows PowerShell 5.1+

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

function Enable-DoubleBuffer($control) {
    try {
        $property = $control.GetType().GetProperty('DoubleBuffered',[System.Reflection.BindingFlags]'Instance,NonPublic')
        if ($null -ne $property) { $property.SetValue($control,$true,$null) }
    } catch { }
}

$script:Version = '7.1'
$script:AppDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$script:ConfigPath = Join-Path $script:AppDir 'spells.json'
$script:SettingsPath = Join-Path $script:AppDir 'settings.json'
$script:LearningPath = Join-Path $script:AppDir 'hot-learning.json'
$script:Spells = @()
$script:Timers = @{}
$script:TimerViews = @{}
$script:PendingCasts = New-Object System.Collections.ArrayList
$script:LogStream = $null
$script:LogReader = $null
$script:IsWatching = $false
$script:LastLogPath = ''
$script:LearnedHotDurations = @{}

function New-DefaultSpells {
    @(
        [pscustomobject]@{ Name='Budding Heal'; Category='HoT'; DurationSeconds=27; DetectionMode='Auto HoT Family'; MatchName='Budding Heal'; TickDelaySeconds=0; LandingPattern=''; FadePattern=''; Enabled=$true },
        [pscustomobject]@{ Name='Sprouting Heal'; Category='HoT'; DurationSeconds=27; DetectionMode='Auto HoT Family'; MatchName='Sprouting Heal'; TickDelaySeconds=0; LandingPattern=''; FadePattern=''; Enabled=$true },
        [pscustomobject]@{ Name='Flowering Heal'; Category='HoT'; DurationSeconds=27; DetectionMode='Auto HoT Family'; MatchName='Flowering Heal'; TickDelaySeconds=0; LandingPattern=''; FadePattern=''; Enabled=$true },
        [pscustomobject]@{ Name='Blooming Heal'; Category='HoT'; DurationSeconds=27; DetectionMode='Auto HoT Family'; MatchName='Blooming Heal'; TickDelaySeconds=0; LandingPattern=''; FadePattern=''; Enabled=$true },
        [pscustomobject]@{ Name='Blossoming Heal'; Category='HoT'; DurationSeconds=27; DetectionMode='Auto HoT Family'; MatchName='Blossoming Heal'; TickDelaySeconds=0; LandingPattern=''; FadePattern=''; Enabled=$true },
        [pscustomobject]@{ Name='Efflorescing Heal'; Category='HoT'; DurationSeconds=27; DetectionMode='Auto HoT Family'; MatchName='Efflorescing Heal'; TickDelaySeconds=0; LandingPattern=''; FadePattern=''; Enabled=$true },
        [pscustomobject]@{ Name='Snails Healing'; Category='HoT'; DurationSeconds=27; DetectionMode='Auto HoT Family'; MatchName='Snails Healing'; TickDelaySeconds=0; LandingPattern=''; FadePattern=''; Enabled=$true },
        [pscustomobject]@{ Name='Tortoises Healing'; Category='HoT'; DurationSeconds=27; DetectionMode='Auto HoT Family'; MatchName='Tortoises Healing'; TickDelaySeconds=0; LandingPattern=''; FadePattern=''; Enabled=$true },
        [pscustomobject]@{ Name='Slugs Healing'; Category='HoT'; DurationSeconds=27; DetectionMode='Auto HoT Family'; MatchName='Slugs Healing'; TickDelaySeconds=0; LandingPattern=''; FadePattern=''; Enabled=$true },
        [pscustomobject]@{ Name='Example Haste'; Category='Buff'; DurationSeconds=180; DetectionMode='Landing Message'; MatchName='Example Haste'; TickDelaySeconds=0; LandingPattern='{target} begins to move with unnatural speed.'; FadePattern=''; Enabled=$false },
        [pscustomobject]@{ Name='Alacrity'; Category='Buff'; DurationSeconds=180; DetectionMode='Landing Message'; MatchName='Alacrity'; TickDelaySeconds=0; LandingPattern='You feel much faster.'; FadePattern=''; Enabled=$false }
    )
}

function Ensure-SpellProperties($spell) {
    # Convert every loaded row into a predictable PSCustomObject first. This avoids
    # Add-Member collisions when spells.json was created by an older app version.
    if ($null -eq $spell) { $spell = [pscustomobject]@{} }

    $defaults = [ordered]@{
        Name='New Spell'; Category='Buff'; DurationSeconds=60; DetectionMode='Landing Message'
        MatchName=''; TickDelaySeconds=0; LandingPattern=''; FadePattern=''; Enabled=$true
    }

    foreach ($key in $defaults.Keys) {
        $existing = @($spell.PSObject.Properties | Where-Object { $_.Name -ieq $key } | Select-Object -First 1)
        if ($existing.Count -eq 0) {
            $spell | Add-Member -MemberType NoteProperty -Name $key -Value $defaults[$key]
        }
    }

    if ([string]::IsNullOrWhiteSpace([string]$spell.MatchName)) {
        $spell.MatchName = [string]$spell.Name
    }
    return $spell
}

function Load-Config {
    if (Test-Path -LiteralPath $script:ConfigPath) {
        try { $script:Spells = @(Get-Content -LiteralPath $script:ConfigPath -Raw | ConvertFrom-Json) }
        catch { $script:Spells = New-DefaultSpells }
    } else { $script:Spells = New-DefaultSpells }
    $normalized = @()
    foreach ($s in $script:Spells) { $normalized += Ensure-SpellProperties $s }
    $script:Spells = $normalized

    # Migrate the older Flowering Heal setup. Legends exposes the recipient in the
    # immediate landing message, and the in-game buff window reports 27 seconds.
    foreach ($s in $script:Spells) {
        if ([string]::Equals([string]$s.Name,'Flowering Heal V',[System.StringComparison]::OrdinalIgnoreCase) -and
            [string]$s.DetectionMode -eq 'HoT Tick') {
            $s.DetectionMode = 'Flowering Heal Landing'
            $s.DurationSeconds = 27
            $s.TickDelaySeconds = 0
            $s.MatchName = 'Flowering Heal'
        }
    }
    # Alacrity ranks all share the same self-buff landing line. Existing rows named
    # Alacrity, Alacrity I, Alacrity II, etc. are upgraded automatically.
    foreach ($s in $script:Spells) {
        if ([string]::Equals((Get-BaseSpellName ([string]$s.Name)),'Alacrity',[System.StringComparison]::OrdinalIgnoreCase)) {
            $s.Category='Buff'
            $s.DetectionMode='Landing Message'
            $s.MatchName='Alacrity'
            if ([string]::IsNullOrWhiteSpace([string]$s.LandingPattern)) { $s.LandingPattern='You feel much faster.' }
            if ([int]$s.DurationSeconds -lt 1) { $s.DurationSeconds=180 }
        }
    }
    Save-Config

    if (Test-Path -LiteralPath $script:SettingsPath) {
        try {
            $settings = Get-Content -LiteralPath $script:SettingsPath -Raw | ConvertFrom-Json
            $script:LastLogPath = [string]$settings.LogPath
        } catch { }
    }
    if (Test-Path -LiteralPath $script:LearningPath) {
        try {
            $learned = Get-Content -LiteralPath $script:LearningPath -Raw | ConvertFrom-Json
            foreach ($prop in $learned.PSObject.Properties) { $script:LearnedHotDurations[$prop.Name] = @($prop.Value) }
        } catch { }
    }
}

function Save-Config {
    try { @($script:Spells) | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $script:ConfigPath -Encoding UTF8 }
    catch { [System.Windows.Forms.MessageBox]::Show("Could not save spells.json:`r`n$($_.Exception.Message)",'Save Error') | Out-Null }
}

function Save-Settings {
    try { [pscustomobject]@{LogPath=$txtLogPath.Text} | ConvertTo-Json | Set-Content -LiteralPath $script:SettingsPath -Encoding UTF8 }
    catch { }
}

function Save-HotLearning {
    try {
        $obj=[ordered]@{}
        foreach ($key in $script:LearnedHotDurations.Keys) { $obj[$key]=@($script:LearnedHotDurations[$key]) }
        [pscustomobject]$obj | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $script:LearningPath -Encoding UTF8
    } catch { }
}

function Get-HotLearningKey([string]$baseName) { return (Get-BaseSpellName $baseName).ToLowerInvariant() }

function Get-LearnedHotSeconds([string]$baseName,[int]$fallback) {
    $key=Get-HotLearningKey $baseName
    if ($script:LearnedHotDurations.ContainsKey($key)) {
        $values=@($script:LearnedHotDurations[$key] | ForEach-Object {[double]$_} | Where-Object {$_ -ge 6 -and $_ -le 180} | Sort-Object)
        if ($values.Count -gt 0) {
            $mid=[int][math]::Floor($values.Count/2)
            if (($values.Count % 2) -eq 1) { return [int][math]::Round($values[$mid]) }
            return [int][math]::Round(($values[$mid-1]+$values[$mid])/2)
        }
    }
    return $fallback
}

function Record-HotDuration([string]$baseName,[double]$seconds) {
    if ($seconds -lt 6 -or $seconds -gt 180) { return }
    $key=Get-HotLearningKey $baseName
    $vals=New-Object System.Collections.ArrayList
    if ($script:LearnedHotDurations.ContainsKey($key)) { foreach ($v in @($script:LearnedHotDurations[$key])) { [void]$vals.Add([double]$v) } }
    [void]$vals.Add([math]::Round($seconds,1))
    while ($vals.Count -gt 12) { $vals.RemoveAt(0) }
    $script:LearnedHotDurations[$key]=@($vals)
    Save-HotLearning
    Log-Activity ("Learned {0} duration: {1:N1}s (next guess {2}s)" -f $baseName,$seconds,(Get-LearnedHotSeconds $baseName 27))
}

function Log-Activity([string]$message) {
    $stamp = Get-Date -Format 'HH:mm:ss'
    $txtActivity.AppendText("[$stamp] $message`r`n")
    $txtActivity.SelectionStart = $txtActivity.TextLength
    $txtActivity.ScrollToCaret()
}

function Find-Spell([string]$name) {
    foreach ($s in $script:Spells) {
        if ([string]::Equals([string]$s.Name,$name,[System.StringComparison]::OrdinalIgnoreCase)) { return $s }
    }
    return $null
}

function Strip-LogTimestamp([string]$line) {
    return ($line -replace '^\[[^\]]+\]\s*','').Trim()
}

function Get-LogTimestamp([string]$line) {
    if ($line -match '^\[(?<stamp>[^\]]+)\]') {
        $parsed = [datetime]::MinValue
        if ([datetime]::TryParseExact($Matches['stamp'], 'ddd MMM dd HH:mm:ss yyyy', [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::None, [ref]$parsed)) { return $parsed }
    }
    return Get-Date
}

function Get-TimerKey([string]$spell,[string]$target) {
    return (($spell.Trim() + '|' + $target.Trim()).ToLowerInvariant())
}

function Get-CharacterNameFromLogPath {
    $leaf = [System.IO.Path]::GetFileNameWithoutExtension($txtLogPath.Text.Trim())
    if ($leaf -match '^eqlog_([^_]+)_') { return $Matches[1] }
    return 'You'
}

function Remove-PendingForSpellName([string]$spellName,[string]$reason) {
    for ($i=$script:PendingCasts.Count-1; $i -ge 0; $i--) {
        if ([string]::Equals([string]$script:PendingCasts[$i].Spell.Name,$spellName,[System.StringComparison]::OrdinalIgnoreCase)) {
            $script:PendingCasts.RemoveAt($i)
            if (-not [string]::IsNullOrWhiteSpace($reason)) { Log-Activity "Pending cast cancelled: $spellName ($reason)" }
            return $true
        }
    }
    return $false
}

function Template-ToRegex([string]$template) {
    if ([string]::IsNullOrWhiteSpace($template)) { return $null }
    $rx = [regex]::Escape($template.Trim())
    $rx = $rx.Replace('\{target\}','(?<target>.+?)')
    return '^' + $rx + '$'
}

function Get-BaseSpellName([string]$spellName) {
    if ([string]::IsNullOrWhiteSpace($spellName)) { return '' }
    return ($spellName.Trim().TrimEnd('.') -replace '\s+(?:I|II|III|IV|V|VI|VII|VIII|IX|X|XI|XII|XIII|XIV|XV|\d+)$','').Trim()
}

function Get-AutoHotTemplate([string]$spellName) {
    $base = Get-BaseSpellName $spellName
    $families = @('Budding Heal','Sprouting Heal','Flowering Heal','Blooming Heal','Blossoming Heal','Efflorescing Heal','Snails Healing','Tortoises Healing','Slugs Healing')
    foreach ($family in $families) {
        if ([string]::Equals($base,$family,[System.StringComparison]::OrdinalIgnoreCase)) {
            foreach ($spell in $script:Spells) {
                $configuredBase = Get-BaseSpellName ([string]$spell.Name)
                if ([bool]$spell.Enabled -and [string]::Equals($configuredBase,$family,[System.StringComparison]::OrdinalIgnoreCase)) { return $spell }
            }
            return [pscustomobject]@{ Name=$family; Category='HoT'; DurationSeconds=27; DetectionMode='Auto HoT Family'; MatchName=$family; TickDelaySeconds=0; LandingPattern=''; FadePattern=''; Enabled=$true }
        }
    }
    return $null
}

function Normalize-LogTarget([string]$target,[string]$caster) {
    $value=$target.Trim()
    if ($value -match '^(?i:you|yourself)$') { return Get-CharacterNameFromLogPath }
    if ($value -match '^(?i:himself|herself|itself|themselves)$') { return $caster }
    return $value
}

function Add-AutoHotPending([string]$caster,[string]$castName,$template,[datetime]$castTime) {
    [void]$script:PendingCasts.Add([pscustomobject]@{
        Spell=$template; CastName=$castName; BaseName=(Get-BaseSpellName $castName); Caster=$caster
        CastTime=$castTime; Expires=$castTime.AddSeconds(20)
    })
    Log-Activity "Pending HoT: $castName by $caster"
}

function Find-AutoHotPending([string]$caster,[string]$effectName) {
    $base=Get-BaseSpellName $effectName
    for ($i=$script:PendingCasts.Count-1; $i -ge 0; $i--) {
        $p=$script:PendingCasts[$i]
        if ($null -eq $p.PSObject.Properties['Caster']) { continue }
        if ([string]::Equals([string]$p.Caster,$caster,[System.StringComparison]::OrdinalIgnoreCase) -and
            [string]::Equals([string]$p.BaseName,$base,[System.StringComparison]::OrdinalIgnoreCase)) {
            $script:PendingCasts.RemoveAt($i)
            return $p
        }
    }
    return $null
}

function Get-AutoHotTimerKey([string]$caster,[string]$baseName,[string]$target) {
    # Legends only allows one of these Druid/Shaman HoTs on a target at a time.
    return (("hot|$target").ToLowerInvariant())
}

function Start-AutoHotTimer($pending,[string]$target,[datetime]$effectStart,[bool]$tickSynced=$false) {
    if ($null -eq $pending -or [string]::IsNullOrWhiteSpace($target)) { return }
    $configured=[math]::Max(1,[int]$pending.Spell.DurationSeconds)
    # The configured value is the authoritative duration. Log timestamps determine
    # the exact start point; no fractional-second learning or gradual adjustment.
    $duration=$configured
    $key=Get-AutoHotTimerKey ([string]$pending.Caster) ([string]$pending.BaseName) $target
    $displaySpell=[string]$pending.CastName
    $source=[string]$pending.Caster
    $firstTick=$null
    if ($tickSynced) { $firstTick=$effectStart }
    $script:Timers[$key]=[pscustomobject]@{
        Key=$key; Spell=$displaySpell; BaseName=[string]$pending.BaseName; Target=$target.Trim(); Source=$source; Category='HoT'
        Start=$effectStart; FirstTick=$firstTick; End=$effectStart.AddSeconds($duration); Duration=$duration; TickSynced=$tickSynced
    }
    Log-Activity "Started $displaySpell on $target from $source ($duration sec)"
    Refresh-TimerPanels
}

function Start-SpellTimer($spell,[string]$target,[datetime]$effectStart) {
    if ([string]::IsNullOrWhiteSpace($target)) { return }
    $key = Get-TimerKey ([string]$spell.Name) $target
    $duration = [math]::Max(1,[int]$spell.DurationSeconds)
    $script:Timers[$key] = [pscustomobject]@{
        Key=$key; Spell=[string]$spell.Name; Target=$target.Trim(); Category=[string]$spell.Category
        Start=$effectStart; End=$effectStart.AddSeconds($duration); Duration=$duration; TickSynced=$false
    }
    Log-Activity "Started $($spell.Name) on $target ($duration sec)"
    Refresh-TimerPanels
}

function Remove-ExpiredTimers {
    $now = Get-Date
    $removed = $false
    foreach ($key in @($script:Timers.Keys)) {
        if ($script:Timers[$key].End -le $now) {
            $t = $script:Timers[$key]
            $script:Timers.Remove($key)
            $removed = $true
            Log-Activity "Expired $($t.Spell) on $($t.Target)"
        }
    }
    return $removed
}

function Add-PendingCast($spell) {
    # One pending entry per cast. Newest casts are matched first for fast recasts.
    [void]$script:PendingCasts.Add([pscustomobject]@{Spell=$spell; CastTime=Get-Date; Expires=(Get-Date).AddSeconds(20)})
    Log-Activity "Pending: $($spell.Name) — waiting for target detection"
}

function Cleanup-Pending {
    $now = Get-Date
    for ($i=$script:PendingCasts.Count-1; $i -ge 0; $i--) {
        if ($script:PendingCasts[$i].Expires -lt $now) { $script:PendingCasts.RemoveAt($i) }
    }
}

function Find-PendingForSpell($spell) {
    for ($i=$script:PendingCasts.Count-1; $i -ge 0; $i--) {
        if ([string]::Equals([string]$script:PendingCasts[$i].Spell.Name,[string]$spell.Name,[System.StringComparison]::OrdinalIgnoreCase)) {
            $p = $script:PendingCasts[$i]
            $script:PendingCasts.RemoveAt($i)
            return $p
        }
    }
    return $null
}

function Process-LogLine([string]$rawLine) {
    if ([string]::IsNullOrWhiteSpace($rawLine)) { return }
    $line = Strip-LogTimestamp $rawLine
    $eventTime = Get-LogTimestamp $rawLine

    # Auto-detect all supported Druid/Shaman HoT ranks from anyone in the log.
    # Examples: You begin casting Flowering Heal V. / Gryff begins casting Snails Healing V.
    $caster=$null; $castName=$null
    if ($line -match '^You begin casting (?<spell>.+?)\.?$') {
        $caster='You'; $castName=$Matches['spell'].Trim().TrimEnd('.')
    } elseif ($line -match '^(?<caster>.+?) begins casting (?<spell>.+?)\.?$') {
        $caster=$Matches['caster'].Trim(); $castName=$Matches['spell'].Trim().TrimEnd('.')
    }
    if (-not [string]::IsNullOrWhiteSpace($castName)) {
        $hotTemplate=Get-AutoHotTemplate $castName
        if ($null -ne $hotTemplate) {
            Add-AutoHotPending $caster $castName $hotTemplate $eventTime
            return
        }
        if ($caster -eq 'You') {
            foreach ($spell in $script:Spells) {
                if (-not [bool]$spell.Enabled) { continue }
                $configuredBase=Get-BaseSpellName ([string]$spell.Name)
                $castBase=Get-BaseSpellName $castName
                if ([string]::Equals([string]$spell.Name,$castName,[System.StringComparison]::OrdinalIgnoreCase) -or
                    [string]::Equals($configuredBase,$castBase,[System.StringComparison]::OrdinalIgnoreCase)) {
                    Add-PendingCast $spell
                    return
                }
            }
        }
    }

    # Cancel your pending casts that never landed.
    if ($line -match '^Your (?<spell>.+?) spell is interrupted\.?$') {
        $failedBase=Get-BaseSpellName $Matches['spell'].Trim()
        for ($i=$script:PendingCasts.Count-1; $i -ge 0; $i--) {
            $p=$script:PendingCasts[$i]
            if (($null -eq $p.PSObject.Properties['Caster'] -or [string]$p.Caster -eq 'You') -and
                [string]::Equals((Get-BaseSpellName ([string]$p.Spell.Name)),$failedBase,[System.StringComparison]::OrdinalIgnoreCase)) {
                $script:PendingCasts.RemoveAt($i); Log-Activity "Pending cast cancelled: $($p.CastName) (interrupted)"; break
            }
        }
        return
    }
    if ($line -match '^Your spell fizzles!$') {
        for ($i=$script:PendingCasts.Count-1; $i -ge 0; $i--) {
            $p=$script:PendingCasts[$i]
            if ($null -eq $p.PSObject.Properties['Caster'] -or [string]$p.Caster -eq 'You') {
                $script:PendingCasts.RemoveAt($i); Log-Activity 'Pending cast cancelled (fizzled)'; break
            }
        }
        return
    }
    if ($line -match '^You have attempted to cast (?<spell>.+?) on .+? but the effect is currently blocked\.?$') {
        $failedBase=Get-BaseSpellName $Matches['spell']
        for ($i=$script:PendingCasts.Count-1; $i -ge 0; $i--) {
            $p=$script:PendingCasts[$i]
            if (($null -eq $p.PSObject.Properties['Caster'] -or [string]$p.Caster -eq 'You') -and
                [string]::Equals([string]$p.BaseName,$failedBase,[System.StringComparison]::OrdinalIgnoreCase)) {
                $script:PendingCasts.RemoveAt($i); Log-Activity "Pending cast cancelled: $failedBase (blocked)"; break
            }
        }
        return
    }

    # Immediate Druid landing messages. These can only be safely assigned to your newest pending Druid HoT.
    $landingTarget=$null
    if ($line -match '^(?<target>.+?) is seeded with healing energy\.?$') { $landingTarget=$Matches['target'].Trim() }
    elseif ($line -match '^You feel a heal (?:budding|sprouting|flowering|blooming|blossoming|efflorescing) within you\.?$') { $landingTarget=Get-CharacterNameFromLogPath }
    if (-not [string]::IsNullOrWhiteSpace($landingTarget)) {
        for ($i=$script:PendingCasts.Count-1; $i -ge 0; $i--) {
            $p=$script:PendingCasts[$i]
            if ($null -ne $p.PSObject.Properties['Caster'] -and [string]$p.Caster -eq 'You' -and [string]$p.BaseName -match ' Heal$') {
                $script:PendingCasts.RemoveAt($i)
                Start-AutoHotTimer $p $landingTarget $eventTime $false
                return
            }
        }
    }

    # HoT ticks identify caster, target, family, and therefore the exact ranked pending cast.
    if ($line -match '^(?<caster>You|.+?) healed (?<target>.+?) over time for .+? hit points by (?<effect>.+?)(?: \(Critical\))?\.?$') {
        $tickCaster=$Matches['caster'].Trim()
        $tickTarget=Normalize-LogTarget $Matches['target'] $tickCaster
        $effect=Get-BaseSpellName ($Matches['effect'].Trim().TrimEnd('.'))
        $template=Get-AutoHotTemplate $effect
        if ($null -ne $template) {
            $pending=Find-AutoHotPending $tickCaster $effect
            if ($null -ne $pending) {
                Start-AutoHotTimer $pending $tickTarget $eventTime $true
                $key=Get-AutoHotTimerKey $tickCaster $effect $tickTarget
                if ($script:Timers.ContainsKey($key)) {
                    # HoT landing-to-first-tick delay varies with the six-second server tick.
                    # For the normal 27-second HoTs, 24 seconds remain after the first tick.
                    $configured=[math]::Max(1,[int]$pending.Spell.DurationSeconds)
                    $remainingAfterFirstTick=[math]::Max(1,$configured-3)
                    $script:Timers[$key].FirstTick=$eventTime
                    $script:Timers[$key].End=$eventTime.AddSeconds($remainingAfterFirstTick)
                    $script:Timers[$key].Duration=[math]::Max(1,($script:Timers[$key].End-$script:Timers[$key].Start).TotalSeconds)
                    if ($script:TimerViews.ContainsKey($key)) { $script:TimerViews[$key].Duration=[double]$script:Timers[$key].Duration }
                }
                Log-Activity "$($pending.CastName) target detected: $tickTarget; synchronized to server tick"
                return
            }

            # If a timer was started from a landing message, synchronize it on its first actual tick.
            $key=Get-AutoHotTimerKey $tickCaster $effect $tickTarget
            if ($script:Timers.ContainsKey($key)) {
                $active=$script:Timers[$key]
                if (-not [bool]$active.TickSynced -and $eventTime -ge $active.Start) {
                    $templateSpell=Get-AutoHotTemplate $effect
                    $configured=if ($null -ne $templateSpell) { [math]::Max(1,[int]$templateSpell.DurationSeconds) } else { [math]::Max(1,[int]$active.Duration) }
                    $remainingAfterFirstTick=[math]::Max(1,$configured-3)
                    $active.FirstTick=$eventTime
                    $active.End=$eventTime.AddSeconds($remainingAfterFirstTick)
                    $active.Duration=[math]::Max(1,($active.End-$active.Start).TotalSeconds)
                    $active.TickSynced=$true
                    if ($script:TimerViews.ContainsKey($key)) { $script:TimerViews[$key].Duration=[double]$active.Duration }
                    Log-Activity "$($active.Spell) on $tickTarget synchronized to server tick"
                }
                return
            }
        }
    }

    # Final trigger heal marks the exact end of a Druid/Shaman HoT. Use it to learn future casts.
    if ($line -match '^(?<target>.+?) healed (?:himself|herself|itself|themselves) for .+? hit points by (?<effect>.+?) Trigger(?: \(Critical\))?\.?$' -or
        $line -match '^You healed (?<target>.+?) for .+? hit points by (?<effect>.+?) Trigger(?: \(Critical\))?\.?$') {
        $triggerTarget=Normalize-LogTarget $Matches['target'] 'You'
        $triggerEffect=Get-BaseSpellName $Matches['effect']
        $key=Get-AutoHotTimerKey '' $triggerEffect $triggerTarget
        if ($script:Timers.ContainsKey($key)) {
            $active=$script:Timers[$key]
            # The trigger is authoritative evidence that the HoT ended. Remove the timer
            # immediately, but do not rewrite or learn a different configured duration.
            $script:Timers.Remove($key)
            Log-Activity "$($active.Spell) on $triggerTarget ended on trigger"
            Refresh-TimerPanels
            return
        }
    }

    # Generic configurable landing message for non-HoT buffs.
    foreach ($spell in $script:Spells) {
        if (-not [bool]$spell.Enabled -or [string]$spell.DetectionMode -ne 'Landing Message') { continue }
        $rx = Template-ToRegex ([string]$spell.LandingPattern)
        if ($null -eq $rx) { continue }
        $m = [regex]::Match($line,$rx,[System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if ($m.Success) {
            $pending = Find-PendingForSpell $spell
            if ($null -ne $pending) {
                # Patterns containing {target} capture another character. A pattern with
                # no {target}, such as 'You feel much faster.', is a self-buff.
                if ($m.Groups['target'].Success -and -not [string]::IsNullOrWhiteSpace($m.Groups['target'].Value)) {
                    $target = Normalize-LogTarget $m.Groups['target'].Value 'You'
                } else {
                    $target = Get-CharacterNameFromLogPath
                }
                Start-SpellTimer $spell $target $eventTime
            }
            return
        }
    }

    foreach ($spell in $script:Spells) {
        if (-not [bool]$spell.Enabled -or [string]::IsNullOrWhiteSpace([string]$spell.FadePattern)) { continue }
        $rx = Template-ToRegex ([string]$spell.FadePattern)
        $m = [regex]::Match($line,$rx,[System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if ($m.Success) {
            $target = $m.Groups['target'].Value.Trim()
            $key = Get-TimerKey ([string]$spell.Name) $target
            if ($script:Timers.ContainsKey($key)) {
                $script:Timers.Remove($key)
                Log-Activity "Fade detected: $($spell.Name) on $target"
                Refresh-TimerPanels
            }
            return
        }
    }
}

function New-HotCard($timer,[double]$remaining) {
    $card = New-Object System.Windows.Forms.Panel
    $card.Height = 92; $card.Width = 720; $card.Padding = New-Object System.Windows.Forms.Padding -ArgumentList 12,8,12,8; $card.Margin = New-Object System.Windows.Forms.Padding -ArgumentList 0,0,0,8
    $card.BackColor = [System.Drawing.Color]::FromArgb(37,42,50)

    $lblName = New-Object System.Windows.Forms.Label
    $sourceText = if ($timer.PSObject.Properties['Source'] -and [string]$timer.Source -ne 'You') { "  •  cast by $($timer.Source)" } else { '' }
    $lblName.Text = "$($timer.Spell)  •  $($timer.Target)$sourceText"
    $lblName.ForeColor = [System.Drawing.Color]::White
    $lblName.Font = New-Object System.Drawing.Font('Segoe UI Semibold',14)
    $lblName.AutoSize=$true; $lblName.Location=New-Object System.Drawing.Point(12,9)

    $lblTime = New-Object System.Windows.Forms.Label
    $lblTime.Text = ('{0:0.0}s' -f [math]::Max(0,$remaining))
    $lblTime.ForeColor = [System.Drawing.Color]::FromArgb(115,220,145)
    $lblTime.Font = New-Object System.Drawing.Font('Segoe UI Semibold',17)
    $lblTime.AutoSize=$true; $lblTime.Anchor='Top,Right'; $lblTime.Location=New-Object System.Drawing.Point(615,7)

    $barBack = New-Object System.Windows.Forms.Panel
    $barBack.Location=New-Object System.Drawing.Point(12,52); $barBack.Size=New-Object System.Drawing.Size(680,20)
    $barBack.BackColor=[System.Drawing.Color]::FromArgb(70,76,86)
    $barFill = New-Object System.Windows.Forms.Panel
    $pct=[math]::Max(0,[math]::Min(1,$remaining/$timer.Duration))
    $barFill.Location=New-Object System.Drawing.Point(0,0); $barFill.Size=New-Object System.Drawing.Size([int](680*$pct),20)
    $barFill.BackColor=[System.Drawing.Color]::FromArgb(62,184,105)
    $barBack.Controls.Add($barFill)

    $card.Controls.Add($lblName); $card.Controls.Add($lblTime); $card.Controls.Add($barBack)
    $script:TimerViews[$timer.Key] = [pscustomobject]@{
        Kind='HoT'; Panel=$card; TimeLabel=$lblTime; Fill=$barFill; BarWidth=680; Duration=[double]$timer.Duration
    }
    return $card
}

function New-BuffRow($timer,[double]$remaining) {
    $row = New-Object System.Windows.Forms.Panel
    $row.Height=44; $row.Width=720; $row.Padding = New-Object System.Windows.Forms.Padding -ArgumentList 8,4,8,4; $row.BackColor=[System.Drawing.Color]::FromArgb(43,48,57)
    $lbl=New-Object System.Windows.Forms.Label
    $lbl.Text="$($timer.Spell)  —  $($timer.Target)"
    $lbl.ForeColor=[System.Drawing.Color]::White; $lbl.Font=New-Object System.Drawing.Font('Segoe UI',10)
    $lbl.AutoSize=$true; $lbl.Location=New-Object System.Drawing.Point(10,12)
    $time=New-Object System.Windows.Forms.Label
    $time.Text=('{0:0}s' -f [math]::Max(0,$remaining)); $time.ForeColor=[System.Drawing.Color]::FromArgb(120,190,255)
    $time.Font=New-Object System.Drawing.Font('Segoe UI Semibold',11); $time.AutoSize=$true; $time.Location=New-Object System.Drawing.Point(635,10)
    $row.Controls.Add($lbl); $row.Controls.Add($time)
    $script:TimerViews[$timer.Key] = [pscustomobject]@{
        Kind='Buff'; Panel=$row; TimeLabel=$time; Fill=$null; BarWidth=0; Duration=[double]$timer.Duration
    }
    return $row
}

function Refresh-TimerPanels {
    $pnlHots.SuspendLayout(); $pnlBuffs.SuspendLayout()
    $script:TimerViews = @{}
    $pnlHots.Controls.Clear(); $pnlBuffs.Controls.Clear()
    $now=Get-Date
    $hots=@(); $buffs=@()
    foreach ($t in $script:Timers.Values) {
        $remaining=($t.End-$now).TotalSeconds
        if ($remaining -le 0) { continue }
        $entry=[pscustomobject]@{Timer=$t; Remaining=$remaining}
        if ([string]$t.Category -eq 'HoT') { $hots += $entry } else { $buffs += $entry }
    }
    foreach ($e in @($hots | Sort-Object Remaining)) { $pnlHots.Controls.Add((New-HotCard $e.Timer $e.Remaining)) }
    foreach ($e in @($buffs | Sort-Object Remaining)) { $pnlBuffs.Controls.Add((New-BuffRow $e.Timer $e.Remaining)) }
    $lblHotEmpty.Visible=($hots.Count -eq 0); $lblBuffEmpty.Visible=($buffs.Count -eq 0)
    $pnlHots.ResumeLayout(); $pnlBuffs.ResumeLayout()
}

function Update-TimerDisplays {
    $now = Get-Date
    foreach ($key in @($script:TimerViews.Keys)) {
        if (-not $script:Timers.ContainsKey($key)) { continue }
        $timer = $script:Timers[$key]
        $view = $script:TimerViews[$key]
        $remaining = [math]::Max(0, ($timer.End - $now).TotalSeconds)

        if ($view.Kind -eq 'HoT') {
            $newText = ('{0:0.0}s' -f $remaining)
            if ($view.TimeLabel.Text -ne $newText) { $view.TimeLabel.Text = $newText }
            $pct = [math]::Max(0, [math]::Min(1, $remaining / [math]::Max(1,$view.Duration)))
            $newWidth = [int]($view.BarWidth * $pct)
            if ($view.Fill.Width -ne $newWidth) { $view.Fill.Width = $newWidth }
        } else {
            $newText = ('{0:0}s' -f $remaining)
            if ($view.TimeLabel.Text -ne $newText) { $view.TimeLabel.Text = $newText }
        }
    }
}

function Format-Duration([int]$seconds) {
    $seconds = [math]::Max(0,$seconds)
    $minutes = [math]::Floor($seconds / 60)
    $secs = $seconds % 60
    return ('{0}:{1:00}' -f $minutes,$secs)
}

function Parse-Duration([string]$text,[int]$defaultSeconds=60) {
    if ([string]::IsNullOrWhiteSpace($text)) { return $defaultSeconds }
    $value = $text.Trim()
    $plain = 0
    if ([int]::TryParse($value,[ref]$plain)) { return [math]::Max(1,$plain) }
    if ($value -match '^(?<m>\d+):(?<s>\d{1,2})$') {
        $m=[int]$Matches['m']; $sec=[int]$Matches['s']
        if ($sec -lt 60) { return [math]::Max(1,($m*60)+$sec) }
    }
    if ($value -match '^(?<h>\d+):(?<m>\d{1,2}):(?<s>\d{1,2})$') {
        $h=[int]$Matches['h']; $m=[int]$Matches['m']; $sec=[int]$Matches['s']
        if ($m -lt 60 -and $sec -lt 60) { return [math]::Max(1,($h*3600)+($m*60)+$sec) }
    }
    return $defaultSeconds
}

function Get-AutoMatchName([string]$spellName) {
    if ([string]::IsNullOrWhiteSpace($spellName)) { return '' }
    # Logs often omit spell rank suffixes such as V, IV, II, or a trailing numeric rank.
    return ($spellName.Trim() -replace '\s+(?:I|II|III|IV|V|VI|VII|VIII|IX|X|\d+)$','').Trim()
}

function AutoFill-SpellRow($row,[bool]$force=$false) {
    if ($null -eq $row -or $row.IsNewRow) { return }
    $name=[string]$row.Cells['Spell'].Value
    if ([string]::IsNullOrWhiteSpace($name)) { return }
    $name=$name.Trim()
    $baseName=Get-BaseSpellName $name
    $isAutoHot=($baseName -in @('Budding Heal','Sprouting Heal','Flowering Heal','Blooming Heal','Blossoming Heal','Efflorescing Heal','Snails Healing','Tortoises Healing','Slugs Healing'))

    if ($null -eq $row.Cells['Enabled'].Value) { $row.Cells['Enabled'].Value=$true }
    if ($isAutoHot) {
        $row.Cells['Category'].Value='HoT'
        $row.Cells['Mode'].Value='Auto HoT Family'
        $row.Cells['MatchName'].Value=$baseName
        $row.Cells['TickDelay'].Value='0'
        if ($force -or [string]::IsNullOrWhiteSpace([string]$row.Cells['Duration'].Value)) { $row.Cells['Duration'].Value='0:27' }
        return
    }

    if ([string]::Equals($baseName,'Alacrity',[System.StringComparison]::OrdinalIgnoreCase)) {
        $row.Cells['Category'].Value='Buff'
        $row.Cells['Mode'].Value='Landing Message'
        $row.Cells['MatchName'].Value='Alacrity'
        $row.Cells['TickDelay'].Value='0'
        $row.Cells['Landing'].Value='You feel much faster.'
        if ($force -or [string]::IsNullOrWhiteSpace([string]$row.Cells['Duration'].Value)) { $row.Cells['Duration'].Value='3:00' }
        return
    }

    $category=[string]$row.Cells['Category'].Value
    if ([string]::IsNullOrWhiteSpace($category)) {
        $category = if ($name -match '(?i)heal|renew|regen|regrowth|chloroplast') { 'HoT' } else { 'Buff' }
        $row.Cells['Category'].Value=$category
    }
    if ($force -or [string]::IsNullOrWhiteSpace([string]$row.Cells['Mode'].Value)) {
        $row.Cells['Mode'].Value = if ($category -eq 'HoT') { 'HoT Tick' } else { 'Landing Message' }
    }
    if ($force -or [string]::IsNullOrWhiteSpace([string]$row.Cells['MatchName'].Value)) {
        $row.Cells['MatchName'].Value=Get-AutoMatchName $name
    }
    if ([string]::IsNullOrWhiteSpace([string]$row.Cells['TickDelay'].Value)) { $row.Cells['TickDelay'].Value='0' }
    if ([string]::IsNullOrWhiteSpace([string]$row.Cells['Duration'].Value)) { $row.Cells['Duration'].Value='1:00' }
}

function Refresh-SpellGrid {
    $grid.Rows.Clear()
    foreach ($s in $script:Spells) {
        [void]$grid.Rows.Add([bool]$s.Enabled,[string]$s.Name,[string]$s.Category,(Format-Duration ([int]$s.DurationSeconds)),[string]$s.DetectionMode,[string]$s.MatchName,[int]$s.TickDelaySeconds,[string]$s.LandingPattern,[string]$s.FadePattern)
    }
}

function Sync-Grid {
    $items=@()
    foreach ($row in $grid.Rows) {
        if ($row.IsNewRow) { continue }
        AutoFill-SpellRow $row $false
        $name=[string]$row.Cells['Spell'].Value
        if ([string]::IsNullOrWhiteSpace($name)) { continue }
        $duration=Parse-Duration ([string]$row.Cells['Duration'].Value) 60
        $tick=0; [void][int]::TryParse([string]$row.Cells['TickDelay'].Value,[ref]$tick)
        $items += [pscustomobject]@{
            Enabled=[bool]$row.Cells['Enabled'].Value; Name=$name.Trim(); Category=[string]$row.Cells['Category'].Value
            DurationSeconds=[math]::Max(1,$duration); DetectionMode=[string]$row.Cells['Mode'].Value
            MatchName=[string]$row.Cells['MatchName'].Value; TickDelaySeconds=[math]::Max(0,$tick)
            LandingPattern=[string]$row.Cells['Landing'].Value; FadePattern=[string]$row.Cells['Fade'].Value
        }
    }
    $script:Spells=$items; Save-Config; Log-Activity 'Spell setup saved.'
}

function Start-Watching {
    $path=$txtLogPath.Text.Trim()
    if (-not (Test-Path -LiteralPath $path)) { [System.Windows.Forms.MessageBox]::Show('Select a valid EverQuest log file.','Log file') | Out-Null; return }
    Stop-Watching
    try {
        $script:LogStream=[System.IO.File]::Open($path,'Open','Read','ReadWrite')
        [void]$script:LogStream.Seek(0,[System.IO.SeekOrigin]::End)
        $script:LogReader=New-Object System.IO.StreamReader($script:LogStream)
        $script:IsWatching=$true; $btnWatch.Text='Stop Watching'; $lblStatus.Text='WATCHING'; $lblStatus.ForeColor=[System.Drawing.Color]::FromArgb(70,210,110)
        Save-Settings; Log-Activity "Watching $path"
    } catch { [System.Windows.Forms.MessageBox]::Show($_.Exception.Message,'Could not open log') | Out-Null }
}
function Stop-Watching {
    $script:IsWatching=$false
    if ($null -ne $script:LogReader) { $script:LogReader.Dispose(); $script:LogReader=$null }
    if ($null -ne $script:LogStream) { $script:LogStream.Dispose(); $script:LogStream=$null }
    if ($null -ne $btnWatch) { $btnWatch.Text='Start Watching'; $lblStatus.Text='STOPPED'; $lblStatus.ForeColor=[System.Drawing.Color]::Tomato }
}

# ---------------- GUI ----------------
Load-Config
$form=New-Object System.Windows.Forms.Form
$form.Text="EQL Spell Timer" v$script:Version"
$form.Size=New-Object System.Drawing.Size(820,760); $form.MinimumSize=New-Object System.Drawing.Size(760,650)
$form.StartPosition='CenterScreen'; $form.BackColor=[System.Drawing.Color]::FromArgb(27,31,38)
$form.Font=New-Object System.Drawing.Font('Segoe UI',9)

$top=New-Object System.Windows.Forms.Panel; $top.Dock='Top'; $top.Height=92; $top.Padding = New-Object System.Windows.Forms.Padding -ArgumentList 14,10,14,8; $top.BackColor=[System.Drawing.Color]::FromArgb(34,39,47)
$lblTitle=New-Object System.Windows.Forms.Label; $lblTitle.Text='EQ LEGENDS SPELL TIMER'; $lblTitle.ForeColor=[System.Drawing.Color]::White; $lblTitle.Font=New-Object System.Drawing.Font('Segoe UI Semibold',15); $lblTitle.AutoSize=$true; $lblTitle.Location=New-Object System.Drawing.Point(14,8)
$txtLogPath=New-Object System.Windows.Forms.TextBox; $txtLogPath.Location=New-Object System.Drawing.Point(14,46); $txtLogPath.Size=New-Object System.Drawing.Size(590,25); $txtLogPath.Text=$script:LastLogPath
$btnBrowse=New-Object System.Windows.Forms.Button; $btnBrowse.Text='Browse'; $btnBrowse.Location=New-Object System.Drawing.Point(612,44); $btnBrowse.Size=New-Object System.Drawing.Size(75,28)
$btnWatch=New-Object System.Windows.Forms.Button; $btnWatch.Text='Start Watching'; $btnWatch.Location=New-Object System.Drawing.Point(694,44); $btnWatch.Size=New-Object System.Drawing.Size(105,28)
$lblStatus=New-Object System.Windows.Forms.Label; $lblStatus.Text='STOPPED'; $lblStatus.ForeColor=[System.Drawing.Color]::Tomato; $lblStatus.Font=New-Object System.Drawing.Font('Segoe UI Semibold',10); $lblStatus.AutoSize=$true; $lblStatus.Location=New-Object System.Drawing.Point(700,12)
$top.Controls.Add($lblTitle);$top.Controls.Add($txtLogPath);$top.Controls.Add($btnBrowse);$top.Controls.Add($btnWatch);$top.Controls.Add($lblStatus)
$form.Controls.Add($top)

$tabs=New-Object System.Windows.Forms.TabControl; $tabs.Dock='Fill'; $tabs.Appearance='Normal'
$tabTimers=New-Object System.Windows.Forms.TabPage; $tabTimers.Text='Timers'; $tabTimers.BackColor=[System.Drawing.Color]::FromArgb(27,31,38)
$tabSetup=New-Object System.Windows.Forms.TabPage; $tabSetup.Text='Spell Setup'; $tabSetup.BackColor=[System.Drawing.Color]::FromArgb(27,31,38)
$tabLog=New-Object System.Windows.Forms.TabPage; $tabLog.Text='Activity Log'; $tabLog.BackColor=[System.Drawing.Color]::FromArgb(27,31,38)
$tabs.TabPages.Add($tabTimers);$tabs.TabPages.Add($tabSetup);$tabs.TabPages.Add($tabLog)
$form.Controls.Add($tabs); $tabs.BringToFront()

$split=New-Object System.Windows.Forms.SplitContainer; $split.Dock='Fill'; $split.Orientation='Horizontal'; $split.SplitterDistance=390; $split.BackColor=[System.Drawing.Color]::FromArgb(27,31,38)
$tabTimers.Controls.Add($split)
$hotHeader=New-Object System.Windows.Forms.Label; $hotHeader.Text='HEAL-OVER-TIME'; $hotHeader.Dock='Top'; $hotHeader.Height=38; $hotHeader.Padding = New-Object System.Windows.Forms.Padding -ArgumentList 12,10,0,0; $hotHeader.ForeColor=[System.Drawing.Color]::FromArgb(115,220,145); $hotHeader.Font=New-Object System.Drawing.Font('Segoe UI Semibold',12)
$pnlHots=New-Object System.Windows.Forms.FlowLayoutPanel; Enable-DoubleBuffer $pnlHots; $pnlHots.Dock='Fill'; $pnlHots.FlowDirection='TopDown'; $pnlHots.WrapContents=$false; $pnlHots.AutoScroll=$true; $pnlHots.Padding = New-Object System.Windows.Forms.Padding -ArgumentList 10; $pnlHots.BackColor=[System.Drawing.Color]::FromArgb(27,31,38)
$lblHotEmpty=New-Object System.Windows.Forms.Label; $lblHotEmpty.Text='No active HoTs'; $lblHotEmpty.ForeColor=[System.Drawing.Color]::Gray; $lblHotEmpty.AutoSize=$true; $lblHotEmpty.Location=New-Object System.Drawing.Point(22,55)
$split.Panel1.Controls.Add($pnlHots);$split.Panel1.Controls.Add($lblHotEmpty);$split.Panel1.Controls.Add($hotHeader);$lblHotEmpty.BringToFront()
$buffHeader=New-Object System.Windows.Forms.Label; $buffHeader.Text='OTHER BUFFS'; $buffHeader.Dock='Top'; $buffHeader.Height=34; $buffHeader.Padding = New-Object System.Windows.Forms.Padding -ArgumentList 12,8,0,0; $buffHeader.ForeColor=[System.Drawing.Color]::FromArgb(120,190,255); $buffHeader.Font=New-Object System.Drawing.Font('Segoe UI Semibold',11)
$pnlBuffs=New-Object System.Windows.Forms.FlowLayoutPanel; Enable-DoubleBuffer $pnlBuffs; $pnlBuffs.Dock='Fill'; $pnlBuffs.FlowDirection='TopDown'; $pnlBuffs.WrapContents=$false; $pnlBuffs.AutoScroll=$true; $pnlBuffs.Padding = New-Object System.Windows.Forms.Padding -ArgumentList 10; $pnlBuffs.BackColor=[System.Drawing.Color]::FromArgb(27,31,38)
$lblBuffEmpty=New-Object System.Windows.Forms.Label; $lblBuffEmpty.Text='No active buffs'; $lblBuffEmpty.ForeColor=[System.Drawing.Color]::Gray; $lblBuffEmpty.AutoSize=$true; $lblBuffEmpty.Location=New-Object System.Drawing.Point(22,48)
$split.Panel2.Controls.Add($pnlBuffs);$split.Panel2.Controls.Add($lblBuffEmpty);$split.Panel2.Controls.Add($buffHeader);$lblBuffEmpty.BringToFront()

$grid=New-Object System.Windows.Forms.DataGridView; $grid.Dock='Fill'; $grid.BackgroundColor=[System.Drawing.Color]::FromArgb(34,39,47); $grid.ForeColor=[System.Drawing.Color]::Black; $grid.AutoGenerateColumns=$false; $grid.AllowUserToAddRows=$true; $grid.RowHeadersVisible=$false; $grid.AutoSizeRowsMode='AllCells'
function Add-TextColumn($name,$header,$width) { $c=New-Object System.Windows.Forms.DataGridViewTextBoxColumn; $c.Name=$name;$c.HeaderText=$header;$c.Width=$width;[void]$grid.Columns.Add($c) }
$cEnabled=New-Object System.Windows.Forms.DataGridViewCheckBoxColumn; $cEnabled.Name='Enabled';$cEnabled.HeaderText='On';$cEnabled.Width=35;[void]$grid.Columns.Add($cEnabled)
Add-TextColumn 'Spell' 'Cast spell name' 145
$cCat=New-Object System.Windows.Forms.DataGridViewComboBoxColumn; $cCat.Name='Category';$cCat.HeaderText='Section';$cCat.Width=65;[void]$cCat.Items.Add('HoT');[void]$cCat.Items.Add('Buff');[void]$grid.Columns.Add($cCat)
Add-TextColumn 'Duration' 'Duration M:SS' 90
$cMode=New-Object System.Windows.Forms.DataGridViewComboBoxColumn; $cMode.Name='Mode';$cMode.HeaderText='Target detection';$cMode.Width=115;[void]$cMode.Items.Add('Auto HoT Family');[void]$cMode.Items.Add('Flowering Heal Landing');[void]$cMode.Items.Add('HoT Tick');[void]$cMode.Items.Add('Landing Message');[void]$grid.Columns.Add($cMode)
Add-TextColumn 'MatchName' 'Effect name in log' 135
Add-TextColumn 'TickDelay' 'Tick delay' 65
Add-TextColumn 'Landing' 'Landing pattern ({target})' 220
Add-TextColumn 'Fade' 'Fade pattern ({target})' 200
$setupBottom=New-Object System.Windows.Forms.Panel; $setupBottom.Dock='Bottom'; $setupBottom.Height=88; $setupBottom.BackColor=[System.Drawing.Color]::FromArgb(34,39,47)
$btnSave=New-Object System.Windows.Forms.Button; $btnSave.Text='Save Spell Setup'; $btnSave.Size=New-Object System.Drawing.Size(130,30); $btnSave.Location=New-Object System.Drawing.Point(12,10)
$btnAuto=New-Object System.Windows.Forms.Button; $btnAuto.Text='Auto-fill Selected'; $btnAuto.Size=New-Object System.Drawing.Size(130,30); $btnAuto.Location=New-Object System.Drawing.Point(150,10)
$lblHelp=New-Object System.Windows.Forms.Label; $lblHelp.Text='Enter duration as M:SS (examples: 0:27 or 3:00). Name + Section auto-fill the common detection fields. Supported Druid/Shaman HoT families and all Roman-numeral ranks are detected automatically. Ranked buffs such as Alacrity IV match a base Alacrity row; landing patterns without {target} are treated as self-buffs. Advanced landing/fade patterns remain optional.'; $lblHelp.ForeColor=[System.Drawing.Color]::Gainsboro; $lblHelp.AutoSize=$true; $lblHelp.Location=New-Object System.Drawing.Point(12,51)
$setupBottom.Controls.Add($btnSave);$setupBottom.Controls.Add($btnAuto);$setupBottom.Controls.Add($lblHelp);$tabSetup.Controls.Add($grid);$tabSetup.Controls.Add($setupBottom)

$txtActivity=New-Object System.Windows.Forms.TextBox; $txtActivity.Dock='Fill'; $txtActivity.Multiline=$true; $txtActivity.ReadOnly=$true; $txtActivity.ScrollBars='Vertical'; $txtActivity.BackColor=[System.Drawing.Color]::FromArgb(20,23,28); $txtActivity.ForeColor=[System.Drawing.Color]::Gainsboro; $txtActivity.Font=New-Object System.Drawing.Font('Consolas',9); $tabLog.Controls.Add($txtActivity)

$dialog=New-Object System.Windows.Forms.OpenFileDialog; $dialog.Filter='EverQuest logs (*.txt)|*.txt|All files (*.*)|*.*'
$btnBrowse.Add_Click({ if ($dialog.ShowDialog() -eq 'OK') { $txtLogPath.Text=$dialog.FileName } })
$btnWatch.Add_Click({ if ($script:IsWatching) { Stop-Watching } else { Start-Watching } })
$btnSave.Add_Click({ Sync-Grid; Refresh-SpellGrid })
$btnAuto.Add_Click({ if ($grid.CurrentRow -and -not $grid.CurrentRow.IsNewRow) { AutoFill-SpellRow $grid.CurrentRow $true } })
$grid.Add_CellEndEdit({ param($sender,$e)
    if ($e.RowIndex -lt 0) { return }
    $row=$grid.Rows[$e.RowIndex]
    $columnName=$grid.Columns[$e.ColumnIndex].Name
    if ($columnName -eq 'Spell' -or $columnName -eq 'Category') { AutoFill-SpellRow $row $false }
    if ($columnName -eq 'Duration') {
        $seconds=Parse-Duration ([string]$row.Cells['Duration'].Value) 60
        $row.Cells['Duration'].Value=Format-Duration $seconds
    }
})
$grid.Add_DefaultValuesNeeded({ param($sender,$e)
    $e.Row.Cells['Enabled'].Value=$true
    $e.Row.Cells['Category'].Value='Buff'
    $e.Row.Cells['Duration'].Value='1:00'
    $e.Row.Cells['Mode'].Value='Landing Message'
    $e.Row.Cells['TickDelay'].Value='0'
})

$poll=New-Object System.Windows.Forms.Timer; $poll.Interval=200
$poll.Add_Tick({
    Cleanup-Pending
    if ($script:IsWatching -and $null -ne $script:LogReader) {
        while (-not $script:LogReader.EndOfStream) { Process-LogLine ($script:LogReader.ReadLine()) }
    }
})
$display=New-Object System.Windows.Forms.Timer; $display.Interval=250
$display.Add_Tick({ if (Remove-ExpiredTimers) { Refresh-TimerPanels } else { Update-TimerDisplays } })
$form.Add_FormClosing({ Stop-Watching; Save-Settings })

Refresh-SpellGrid; Refresh-TimerPanels; $poll.Start(); $display.Start()
Log-Activity 'Ready. Ranked buff matching and self-buff landing detection enabled; shared HoT durations learn automatically.'
[void]$form.ShowDialog()
