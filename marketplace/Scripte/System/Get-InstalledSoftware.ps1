#Requires -Version 5.1
<#
.SYNOPSIS
    Listet installierte Programme mit Version und Hersteller.

.DESCRIPTION
    Liest die installierten Programme aus der Registrierung (64- und 32-Bit, auf Wunsch auch die nur für
    den Benutzer installierten). Schnell und ohne die Nebenwirkungen von Win32_Product. Gut auch remote
    auf mehreren Rechnern, um Versionen zu vergleichen.

.PARAMETER Name
    Filter auf den Namen, Platzhalter erlaubt (z. B. *Chrome* oder Microsoft*).

.PARAMETER IncludeUserInstalls
    Auch Programme, die nur für den angemeldeten Benutzer installiert sind.
#>
[CmdletBinding()]
param(
    [string]$Name = '*',

    [switch]$IncludeUserInstalls
)

$keys = @(
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
)
if ($IncludeUserInstalls) { $keys += 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*' }

Get-ItemProperty -Path $keys -ErrorAction SilentlyContinue |
    Where-Object { $_.DisplayName -and $_.DisplayName -like $Name -and -not $_.SystemComponent } |
    Sort-Object DisplayName -Unique |
    ForEach-Object {
        [pscustomobject]@{
            Programm   = $_.DisplayName
            Version    = $_.DisplayVersion
            Hersteller = $_.Publisher
            Installiert = if ($_.InstallDate -match '^\d{8}$') { [datetime]::ParseExact($_.InstallDate, 'yyyyMMdd', $null).ToString('yyyy-MM-dd') } else { '' }
        }
    } |
    Format-Table -AutoSize
