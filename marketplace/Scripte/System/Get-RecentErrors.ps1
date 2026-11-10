#Requires -Version 5.1
<#
.SYNOPSIS
    Zeigt die letzten Fehler aus der Ereignisanzeige.

.DESCRIPTION
    Holt Fehler (und auf Wunsch Warnungen) aus den Protokollen System und Anwendung der letzten Stunden,
    gruppiert nach Quelle. Hilfreich als erster Blick, wenn ein Rechner „spinnt“ – auch remote.

.PARAMETER Hours
    Zeitraum in Stunden.

.PARAMETER LogName
    Welches Protokoll durchsucht wird.

.PARAMETER IncludeWarnings
    Auch Warnungen anzeigen.

.PARAMETER MaxEvents
    Höchstens so viele Ereignisse lesen.
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 720)]
    [int]$Hours = 24,

    [ValidateSet('System', 'Application', 'Beide')]
    [string]$LogName = 'Beide',

    [switch]$IncludeWarnings,

    [ValidateRange(10, 5000)]
    [int]$MaxEvents = 200
)

$logs = if ($LogName -eq 'Beide') { 'System', 'Application' } else { $LogName }
$levels = if ($IncludeWarnings) { 1, 2, 3 } else { 1, 2 }   # 1 kritisch, 2 Fehler, 3 Warnung

$events = Get-WinEvent -FilterHashtable @{ LogName = $logs; Level = $levels; StartTime = (Get-Date).AddHours(-$Hours) } `
    -MaxEvents $MaxEvents -ErrorAction SilentlyContinue

if (-not $events) { "Keine Fehler in den letzten $Hours Stunden."; return }

"Zusammenfassung nach Quelle:"
$events | Group-Object ProviderName | Sort-Object Count -Descending |
    Select-Object @{ n = 'Anzahl'; e = { $_.Count } }, @{ n = 'Quelle'; e = { $_.Name } } |
    Format-Table -AutoSize

"Neueste Ereignisse:"
$events | Select-Object -First 25 |
    Select-Object @{ n = 'Zeit'; e = { $_.TimeCreated.ToString('dd.MM. HH:mm') } },
                  @{ n = 'Stufe'; e = { $_.LevelDisplayName } },
                  @{ n = 'Quelle'; e = { $_.ProviderName } },
                  @{ n = 'ID'; e = { $_.Id } },
                  @{ n = 'Meldung'; e = { ($_.Message -split "`r?`n")[0] } } |
    Format-Table -AutoSize -Wrap
