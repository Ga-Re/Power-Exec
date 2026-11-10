#Requires -Version 5.1
<#
.SYNOPSIS
    Findet die größten Dateien in einem Ordner.

.DESCRIPTION
    Durchsucht einen Ordner samt Unterordnern und listet die größten Dateien mit Größe und letzter
    Änderung. Ordner ohne Zugriffsrecht werden übersprungen. Optional als CSV speichern.

.PARAMETER Path
    Ordner, der durchsucht wird.

.PARAMETER Top
    Wie viele Dateien angezeigt werden.

.PARAMETER MinSizeMB
    Nur Dateien ab dieser Größe.

.PARAMETER ExportPath
    Optional: Ergebnis als CSV speichern.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Path,

    [ValidateRange(1, 500)]
    [int]$Top = 20,

    [ValidateRange(0, 100000)]
    [int]$MinSizeMB = 100,

    [string]$ExportPath
)

if (-not (Test-Path -LiteralPath $Path -PathType Container)) { throw "Ordner nicht gefunden: $Path" }

$files = Get-ChildItem -LiteralPath $Path -File -Recurse -Force -ErrorAction SilentlyContinue |
    Where-Object { $_.Length -ge ($MinSizeMB * 1MB) } |
    Sort-Object Length -Descending |
    Select-Object -First $Top |
    ForEach-Object {
        [pscustomobject]@{
            GroesseMB  = [math]::Round($_.Length / 1MB, 1)
            Geaendert  = $_.LastWriteTime.ToString('yyyy-MM-dd')
            Datei      = $_.FullName
        }
    }

if (-not $files) { "Keine Dateien ab $MinSizeMB MB in $Path."; return }
$files | Format-Table -AutoSize
if ($ExportPath) {
    $files | Export-Csv -Path $ExportPath -NoTypeInformation -Delimiter ';' -Encoding UTF8
    "Exportiert nach $ExportPath"
}
