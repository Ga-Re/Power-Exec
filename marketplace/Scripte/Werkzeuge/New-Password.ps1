#Requires -Version 5.1
<#
.SYNOPSIS
    Erzeugt sichere Kennwörter und kopiert das erste in die Zwischenablage.

.DESCRIPTION
    Erzeugt zufällige Kennwörter mit einem kryptografisch sicheren Zufallsgenerator. Leicht verwechselbare
    Zeichen (0/O, 1/l/I) können weggelassen werden. Das erste Kennwort landet in der Zwischenablage.

.PARAMETER Length
    Länge jedes Kennworts.

.PARAMETER Count
    Wie viele Kennwörter erzeugt werden.

.PARAMETER NoSymbols
    Nur Buchstaben und Ziffern (für Systeme, die keine Sonderzeichen mögen).

.PARAMETER Readable
    Leicht verwechselbare Zeichen weglassen (0, O, 1, l, I).
#>
[CmdletBinding()]
param(
    [ValidateRange(8, 128)]
    [int]$Length = 20,

    [ValidateRange(1, 50)]
    [int]$Count = 5,

    [switch]$NoSymbols,

    [switch]$Readable
)

$upper = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ'
$lower = 'abcdefghijklmnopqrstuvwxyz'
$digits = '0123456789'
$symbols = '!#$%&*+-=?@_'
if ($Readable) {
    $upper = $upper -replace '[OI]', ''
    $lower = $lower -replace '[l]', ''
    $digits = $digits -replace '[01]', ''
}
$sets = @($upper, $lower, $digits)
if (-not $NoSymbols) { $sets += $symbols }
$all = -join $sets

$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
function Get-RandomIndex([int]$max) {
    $buffer = New-Object byte[] 4
    $rng.GetBytes($buffer)
    [int]([BitConverter]::ToUInt32($buffer, 0) % [uint32]$max)
}

$passwords = for ($n = 0; $n -lt $Count; $n++) {
    # Aus jeder Zeichengruppe mindestens ein Zeichen, der Rest zufällig, dann mischen
    $chars = New-Object System.Collections.Generic.List[char]
    foreach ($set in $sets) { $chars.Add($set[(Get-RandomIndex $set.Length)]) }
    while ($chars.Count -lt $Length) { $chars.Add($all[(Get-RandomIndex $all.Length)]) }
    for ($i = $chars.Count - 1; $i -gt 0; $i--) {
        $j = Get-RandomIndex ($i + 1)
        $tmp = $chars[$i]; $chars[$i] = $chars[$j]; $chars[$j] = $tmp
    }
    -join $chars
}
$rng.Dispose()

$passwords
try {
    Set-Clipboard -Value $passwords[0]
    "Das erste Kennwort ist in der Zwischenablage."
}
catch { Write-Verbose 'Zwischenablage nicht verfügbar.' }
