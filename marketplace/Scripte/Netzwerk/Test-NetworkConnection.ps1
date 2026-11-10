#Requires -Version 5.1
<#
.SYNOPSIS
    Prüft, ob Rechner oder Dienste erreichbar sind (Ping oder TCP-Port).

.DESCRIPTION
    Testet eine oder mehrere Adressen nacheinander und zeigt Erreichbarkeit und Antwortzeit. Mit „TCP“
    wird ein bestimmter Port geprüft (z. B. 443 für HTTPS, 3389 für Remotedesktop, 5985 für WinRM).

.PARAMETER ComputerName
    Rechnernamen oder Adressen, mehrere mit Komma getrennt.

.PARAMETER Method
    ICMP (Ping) oder TCP (Port).

.PARAMETER Port
    Port für die TCP-Prüfung.

.PARAMETER TimeoutMs
    Wartezeit je Versuch in Millisekunden.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string[]]$ComputerName,

    [ValidateSet('ICMP', 'TCP')]
    [string]$Method = 'TCP',

    [ValidateRange(1, 65535)]
    [int]$Port = 443,

    [ValidateRange(100, 30000)]
    [int]$TimeoutMs = 2000
)

# Eingaben wie „srv1, srv2“ in einem Feld zulassen
$targets = $ComputerName | ForEach-Object { $_ -split '[,; ]+' } | Where-Object { $_ }

foreach ($target in $targets) {
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $ok = $false
    $detail = ''
    try {
        if ($Method -eq 'ICMP') {
            $reply = (New-Object System.Net.NetworkInformation.Ping).Send($target, $TimeoutMs)
            $ok = $reply.Status -eq 'Success'
            $detail = "$($reply.Status)"
        }
        else {
            $client = New-Object System.Net.Sockets.TcpClient
            try {
                $task = $client.ConnectAsync($target, $Port)
                $ok = $task.Wait($TimeoutMs) -and $client.Connected
                $detail = if ($ok) { "Port $Port offen" } else { "Port $Port nicht erreichbar" }
            }
            finally { $client.Dispose() }
        }
    }
    catch { $detail = $_.Exception.InnerException.Message; if (-not $detail) { $detail = $_.Exception.Message } }
    $watch.Stop()

    [pscustomobject]@{
        Ziel        = $target
        Erreichbar  = $ok
        ZeitMs      = if ($ok) { $watch.ElapsedMilliseconds } else { $null }
        Hinweis     = $detail
    }
}
