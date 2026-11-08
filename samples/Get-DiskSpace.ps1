#Requires -Version 5.1
<#
.SYNOPSIS
    Zeigt den freien Speicherplatz aller Laufwerke an.

.DESCRIPTION
    Prüft die Laufwerke und warnt, wenn der freie Speicher unter einen Schwellwert fällt.
    Beispielscript für Power-Exec: Synopsis, Beschreibung und Parameter erscheinen im Panel
    und im Ausführen-Dialog.

.PARAMETER MinFreePercent
    Warnschwelle in Prozent freiem Speicher.

.PARAMETER DriveType
    Welche Laufwerke geprüft werden.

.PARAMETER ExportPath
    Optional: Ergebnis zusätzlich als CSV speichern.

.PARAMETER IncludeSystemReserved
    Auch Partitionen ohne Laufwerksbuchstaben prüfen.

.NOTES
    Mit der folgenden Zeile sendet Power-Exec bei Remote-Ausführung standardmäßig
    den ganzen Scriptordner mit (für Begleitdateien und Module):
    PowerExec: CopyFolder
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateRange(1, 99)]
    [int]$MinFreePercent = 15,

    [ValidateSet('Fixed', 'Removable', 'Network', 'All')]
    [string]$DriveType = 'Fixed',

    [string]$ExportPath,

    [switch]$IncludeSystemReserved
)

$typeMap = @{ Fixed = 3; Removable = 2; Network = 4 }
$filter = if ($DriveType -eq 'All') { $null } else { "DriveType = $($typeMap[$DriveType])" }

$volumes = Get-CimInstance -ClassName Win32_LogicalDisk -Filter $filter |
    Where-Object { $_.Size -gt 0 } |
    ForEach-Object {
        $free = [math]::Round($_.FreeSpace / $_.Size * 100, 1)
        [pscustomobject]@{
            Laufwerk  = $_.DeviceID
            FreiProz  = $free
            FreiGB    = [math]::Round($_.FreeSpace / 1GB, 1)
            GroesseGB = [math]::Round($_.Size / 1GB, 1)
            Warnung   = if ($free -lt $MinFreePercent) { "unter $MinFreePercent %" } else { '' }
        }
    }

if ($IncludeSystemReserved) {
    $volumes += Get-CimInstance -ClassName Win32_Volume -Filter 'DriveLetter IS NULL' |
        Where-Object { $_.Capacity -gt 0 } |
        ForEach-Object {
            $free = [math]::Round($_.FreeSpace / $_.Capacity * 100, 1)
            [pscustomobject]@{
                Laufwerk  = $_.Label
                FreiProz  = $free
                FreiGB    = [math]::Round($_.FreeSpace / 1GB, 1)
                GroesseGB = [math]::Round($_.Capacity / 1GB, 1)
                Warnung   = if ($free -lt $MinFreePercent) { "unter $MinFreePercent %" } else { '' }
            }
        }
}

$volumes | Format-Table -AutoSize
if ($ExportPath) {
    $volumes | Export-Csv -Path $ExportPath -NoTypeInformation -Delimiter ';' -Encoding UTF8
    "Exportiert nach $ExportPath"
}
