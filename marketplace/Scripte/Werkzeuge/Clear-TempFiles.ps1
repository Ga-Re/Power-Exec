#Requires -Version 5.1
<#
.SYNOPSIS
    Räumt alte temporäre Dateien auf – mit Probelauf.

.DESCRIPTION
    Löscht Dateien im Temp-Ordner des Benutzers (auf Wunsch auch im Windows-Temp-Ordner), die älter als
    die angegebene Zahl von Tagen sind. Gesperrte Dateien werden übersprungen. Mit „Probelauf“ wird nur
    angezeigt, was gelöscht würde.

.PARAMETER OlderThanDays
    Nur Dateien löschen, die seit so vielen Tagen nicht geändert wurden.

.PARAMETER IncludeWindowsTemp
    Auch C:\Windows\Temp aufräumen (braucht Administratorrechte – im Panel „Admin“ wählen).

.PARAMETER Probelauf
    Nichts löschen, nur auflisten (entspricht -WhatIf).
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateRange(1, 365)]
    [int]$OlderThanDays = 7,

    [switch]$IncludeWindowsTemp,

    [switch]$Probelauf
)

if ($Probelauf) { $WhatIfPreference = $true }

$folders = @($env:TEMP)
if ($IncludeWindowsTemp) { $folders += Join-Path $env:SystemRoot 'Temp' }
$limit = (Get-Date).AddDays(-$OlderThanDays)

$count = 0
$bytes = 0
foreach ($folder in $folders) {
    Get-ChildItem -Path $folder -File -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -lt $limit } |
        ForEach-Object {
            if ($PSCmdlet.ShouldProcess($_.FullName, 'Löschen')) {
                try {
                    $size = $_.Length
                    Remove-Item -LiteralPath $_.FullName -Force -ErrorAction Stop
                    $count++
                    $bytes += $size
                }
                catch { Write-Verbose "Übersprungen (in Benutzung): $($_.Exception.Message)" }
            }
            else {
                $count++
                $bytes += $_.Length
            }
        }
}

$verb = if ($WhatIfPreference) { 'würden gelöscht' } else { 'gelöscht' }
'{0} Dateien ({1:N1} MB) {2}.' -f $count, ($bytes / 1MB), $verb
