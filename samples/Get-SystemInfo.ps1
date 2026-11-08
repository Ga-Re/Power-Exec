#Requires -Version 5.1
<#
.SYNOPSIS
    Zeigt die wichtigsten Systemdaten: Windows-Version, Prozessor, Arbeitsspeicher, Laufzeit.

.DESCRIPTION
    Sammelt Betriebssystem, Build, Prozessor, Arbeitsspeicher, letzte Anmeldung und Laufzeit seit dem
    letzten Neustart. Lokal oder auf mehreren Rechnern über „Remote“ im Panel – das Ergebnis jedes
    Rechners erscheint dann in der Ergebnisliste.

.PARAMETER AsJson
    Ausgabe als JSON statt als Tabelle (z. B. zum Weiterverarbeiten).

.EXAMPLE
    .\Get-SystemInfo.ps1
#>
[CmdletBinding()]
param(
    [switch]$AsJson
)

$os = Get-CimInstance -ClassName Win32_OperatingSystem
$cpu = Get-CimInstance -ClassName Win32_Processor | Select-Object -First 1
$cs = Get-CimInstance -ClassName Win32_ComputerSystem
$uptime = (Get-Date) - $os.LastBootUpTime

$info = [pscustomobject]@{
    Rechner        = $env:COMPUTERNAME
    Windows        = $os.Caption
    Build          = $os.BuildNumber
    Prozessor      = $cpu.Name.Trim()
    Kerne          = $cpu.NumberOfCores
    ArbeitsspeicherGB = [math]::Round($cs.TotalPhysicalMemory / 1GB, 1)
    FreiGB         = [math]::Round($os.FreePhysicalMemory * 1KB / 1GB, 1)
    Benutzer       = $cs.UserName
    Laufzeit       = '{0} Tage {1} Std. {2} Min.' -f $uptime.Days, $uptime.Hours, $uptime.Minutes
    LetzterStart   = $os.LastBootUpTime
}

if ($AsJson) { $info | ConvertTo-Json }
else { $info | Format-List }
