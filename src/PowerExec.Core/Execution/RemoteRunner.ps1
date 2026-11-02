# Power-Exec Remote-Runner
# Läuft lokal in Windows PowerShell 5.1 und führt ein Script parallel auf mehreren Rechnern aus.
# Eingabe über stdin:
#   Zeile 1: Konfiguration als Base64(UTF-8-JSON)
#   Zeile 2+3 (nur wenn UseCredential): Benutzername, Kennwort – jeweils Base64(UTF-8)
# Ausgabe über stdout: ein JSON-Objekt pro Zeile (type = start | result | done).
# Anmeldedaten werden nur im Speicher gehalten und nirgends geschrieben.

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

function Emit($obj) {
    [Console]::Out.WriteLine(($obj | ConvertTo-Json -Compress -Depth 5))
    [Console]::Out.Flush()
}

$cfgJson = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String([Console]::In.ReadLine()))
$cfg = $cfgJson | ConvertFrom-Json

$cred = $null
if ($cfg.UseCredential) {
    $user = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String([Console]::In.ReadLine()))
    $pwBytes = [Convert]::FromBase64String([Console]::In.ReadLine())
    $pwChars = [System.Text.Encoding]::UTF8.GetChars($pwBytes)
    [Array]::Clear($pwBytes, 0, $pwBytes.Length)
    $sec = New-Object System.Security.SecureString
    foreach ($ch in $pwChars) { $sec.AppendChar($ch) }
    [Array]::Clear($pwChars, 0, $pwChars.Length)
    $sec.MakeReadOnly()
    $cred = New-Object System.Management.Automation.PSCredential($user, $sec)
}

# Parameter als Hashtable für Splatting
$params = @{}
if ($cfg.Parameters) {
    foreach ($p in $cfg.Parameters.PSObject.Properties) {
        $v = $p.Value
        if ($v -is [System.Array]) { $v = @($v) }
        $params[$p.Name] = $v
    }
}

$scriptText = [System.IO.File]::ReadAllText($cfg.ScriptPath)

$worker = {
    param($computer, $cfg, $cred, $params, $scriptText)

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $result = [ordered]@{
        type = 'result'; computer = $computer; status = 'Success'
        durationMs = 0; output = ''; error = ''; note = ''; engine = '5.1'
    }

    function Limit([string]$s) {
        if ($s.Length -gt 65536) { return $s.Substring(0, 65536) + "`n… (gekürzt)" }
        return $s
    }

    $session = $null
    try { do {
        $opt = New-PSSessionOption -OpenTimeout ([int]$cfg.OpenTimeoutSec * 1000)
        $common = @{ ComputerName = $computer; SessionOption = $opt; ErrorAction = 'Stop' }
        if ($cred) { $common.Credential = $cred }
        if ($cfg.UseSsl) { $common.UseSSL = $true }

        if ($cfg.Engine -eq 'PowerShell7') {
            try {
                $session = New-PSSession @common -ConfigurationName 'PowerShell.7'
                $result.engine = '7'
            } catch {
                $result.note = 'PowerShell 7 ist auf dem Rechner nicht verfügbar, verwendet wurde 5.1.'
            }
        }
        if (-not $session) {
            try {
                $session = New-PSSession @common
            } catch {
                $ex = $_.Exception
                $result.note = ''
                $code = 0
                if ($ex.PSObject.Properties['ErrorCode']) { $code = $ex.ErrorCode }
                if ($code -eq 5 -or $ex.Message -match 'Access is denied|Zugriff verweigert') {
                    $result.status = 'Error'
                    $result.error = 'Zugriff verweigert.'
                } elseif ($ex.Message -match 'TrustedHosts') {
                    $result.status = 'Error'
                    $result.error = 'Rechner nicht vertrauenswürdig (TrustedHosts): vollständigen Domänennamen verwenden, HTTPS aktivieren oder den Rechner in TrustedHosts eintragen.'
                } elseif ($ex.Message -match 'cannot find the computer|kann den Computer .* nicht finden|nicht gefunden') {
                    $result.status = 'Unreachable'
                    $result.error = 'Rechnername nicht auflösbar (DNS).'
                } else {
                    $result.status = 'Unreachable'
                    $result.error = 'Verbindung fehlgeschlagen (WinRM).'
                }
                $result.output = Limit($ex.Message)
                break
            }
        }

        # Benötigte Module prüfen (#Requires -Modules)
        if ($cfg.RequiredModules -and @($cfg.RequiredModules).Count -gt 0) {
            $missing = Invoke-Command -Session $session -ArgumentList (,@($cfg.RequiredModules)) -ScriptBlock {
                param($mods)
                $mods | Where-Object { -not (Get-Module -ListAvailable -Name $_) }
            }
            if (@($missing).Count -gt 0) {
                $result.status = 'Error'
                $result.error = 'Modul fehlt: ' + (@($missing) -join ', ')
                $result.output = $result.error
                break
            }
        }

        if ($cfg.CopyFolder) {
            $remoteTemp = Invoke-Command -Session $session -ScriptBlock {
                $d = Join-Path ([System.IO.Path]::GetTempPath()) ('PowerExec\' + [guid]::NewGuid().ToString('N'))
                New-Item -ItemType Directory -Path $d -Force | Out-Null
                $d
            }
            try {
                Copy-Item -Path (Join-Path $cfg.ScriptDirectory '*') -Destination $remoteTemp -ToSession $session -Recurse -Force
                $remoteScript = Join-Path $remoteTemp $cfg.ScriptFileName
                $out = Invoke-Command -Session $session -ArgumentList $remoteScript, $params -ScriptBlock {
                    param($path, $p)
                    Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force -ErrorAction SilentlyContinue
                    & $path @p
                } 2>&1
            } finally {
                Invoke-Command -Session $session -ArgumentList $remoteTemp -ScriptBlock {
                    param($d) Remove-Item -LiteralPath $d -Recurse -Force -ErrorAction SilentlyContinue
                } -ErrorAction SilentlyContinue
            }
        } else {
            $out = Invoke-Command -Session $session -ArgumentList $scriptText, $params -ScriptBlock {
                param($code, $p)
                $sb = [scriptblock]::Create($code)
                & $sb @p
            } 2>&1
        }

        $errors = @($out | Where-Object { $_ -is [System.Management.Automation.ErrorRecord] })
        $result.output = Limit(($out | Out-String -Width 220).TrimEnd())
        if ($errors.Count -gt 0) {
            $result.status = 'Error'
            $result.error = $errors[0].ToString()
        }
    } while ($false) } catch {
        $result.status = 'Error'
        $result.error = $_.Exception.Message
        if (-not $result.output) { $result.output = Limit(($_ | Out-String).TrimEnd()) }
    } finally {
        if ($session) { Remove-PSSession -Session $session -ErrorAction SilentlyContinue }
        $sw.Stop()
        $result.durationMs = [int]$sw.ElapsedMilliseconds
    }
    return [pscustomobject]$result
}

$targets = @($cfg.Targets)
Emit @{ type = 'start'; count = $targets.Count }

$pool = [runspacefactory]::CreateRunspacePool(1, [Math]::Max(1, [int]$cfg.ThrottleLimit))
$pool.Open()
$jobs = New-Object System.Collections.ArrayList
foreach ($t in $targets) {
    $ps = [powershell]::Create()
    $ps.RunspacePool = $pool
    [void]$ps.AddScript($worker).AddArgument($t).AddArgument($cfg).AddArgument($cred).AddArgument($params).AddArgument($scriptText)
    [void]$jobs.Add([pscustomobject]@{ Computer = $t; PS = $ps; Handle = $ps.BeginInvoke() })
}

while ($jobs.Count -gt 0) {
    foreach ($j in @($jobs)) {
        if ($j.Handle.IsCompleted) {
            try {
                $r = $j.PS.EndInvoke($j.Handle)
                if ($r -and $r.Count -gt 0) { Emit $r[$r.Count - 1] }
                else { Emit @{ type = 'result'; computer = $j.Computer; status = 'Error'; durationMs = 0; output = ''; error = 'Keine Rückgabe.'; note = ''; engine = '' } }
            } catch {
                Emit @{ type = 'result'; computer = $j.Computer; status = 'Error'; durationMs = 0; output = ($_ | Out-String); error = $_.Exception.Message; note = ''; engine = '' }
            }
            $j.PS.Dispose()
            $jobs.Remove($j)
        }
    }
    Start-Sleep -Milliseconds 100
}
$pool.Close()
Emit @{ type = 'done' }
