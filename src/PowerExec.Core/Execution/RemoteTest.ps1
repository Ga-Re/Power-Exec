# Power-Exec Verbindungstest
# Prüft je Rechner: WinRM erreichbar, Anmeldung möglich, PowerShell-Version, Endpunkt PowerShell.7.
# Eingabe über stdin wie beim Remote-Runner (Zeile 1: Base64-JSON, Zeile 2+3: Benutzer/Kennwort als Base64).
# Ausgabe: ein JSON-Objekt pro Rechner und Zeile.

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

function Emit($obj) {
    [Console]::Out.WriteLine(($obj | ConvertTo-Json -Compress -Depth 4))
    [Console]::Out.Flush()
}

$cfg = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String([Console]::In.ReadLine())) | ConvertFrom-Json
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

$worker = {
    param($computer, $cfg, $cred)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $r = [ordered]@{ type = 'test'; computer = $computer; wsman = $false; auth = $false; ps7 = $false; version = ''; latencyMs = 0; error = '' }
    try { do {
        $wsArgs = @{ ComputerName = $computer; ErrorAction = 'Stop' }
        if ($cfg.UseSsl) { $wsArgs.UseSSL = $true }
        try {
            Test-WSMan @wsArgs | Out-Null
            $r.wsman = $true
        } catch {
            $r.error = 'WinRM nicht erreichbar' + $(if ($cfg.UseSsl) { ' (HTTPS/5986)' } else { ' (HTTP/5985)' }) + ': ' + $_.Exception.Message.Split("`n")[0]
            break
        }
        $opt = New-PSSessionOption -OpenTimeout ([int]$cfg.OpenTimeoutSec * 1000)
        $common = @{ ComputerName = $computer; SessionOption = $opt; ErrorAction = 'Stop' }
        if ($cred) { $common.Credential = $cred }
        if ($cfg.UseSsl) { $common.UseSSL = $true }
        try {
            $s = New-PSSession @common
            $r.auth = $true
            $r.version = Invoke-Command -Session $s -ScriptBlock { $PSVersionTable.PSVersion.ToString() }
            Remove-PSSession $s
        } catch {
            $m = $_.Exception.Message
            if ($m -match 'TrustedHosts') { $r.error = 'Anmeldung nicht möglich: Rechner nicht vertrauenswürdig (TrustedHosts). Vollständigen Domänennamen verwenden oder HTTPS aktivieren.' }
            elseif ($m -match 'Access is denied|Zugriff verweigert') { $r.error = 'Zugriff verweigert – das Konto hat keine Remoterechte auf diesem Rechner.' }
            else { $r.error = 'Anmeldung fehlgeschlagen: ' + $m.Split("`n")[0] }
            break
        }
        try {
            $s7 = New-PSSession @common -ConfigurationName 'PowerShell.7'
            $r.ps7 = $true
            Remove-PSSession $s7
        } catch { $r.ps7 = $false }
    } while ($false) } finally {
        $sw.Stop()
        $r.latencyMs = [int]$sw.ElapsedMilliseconds
    }
    [pscustomobject]$r
}

$pool = [runspacefactory]::CreateRunspacePool(1, [Math]::Max(1, [int]$cfg.ThrottleLimit))
$pool.Open()
$jobs = New-Object System.Collections.ArrayList
foreach ($t in @($cfg.Targets)) {
    $ps = [powershell]::Create()
    $ps.RunspacePool = $pool
    [void]$ps.AddScript($worker).AddArgument($t).AddArgument($cfg).AddArgument($cred)
    [void]$jobs.Add([pscustomobject]@{ Computer = $t; PS = $ps; Handle = $ps.BeginInvoke() })
}
while ($jobs.Count -gt 0) {
    foreach ($j in @($jobs)) {
        if ($j.Handle.IsCompleted) {
            try {
                $res = $j.PS.EndInvoke($j.Handle)
                if ($res -and $res.Count -gt 0) { Emit $res[$res.Count - 1] }
            } catch {
                Emit @{ type = 'test'; computer = $j.Computer; wsman = $false; auth = $false; ps7 = $false; version = ''; latencyMs = 0; error = $_.Exception.Message }
            }
            $j.PS.Dispose()
            $jobs.Remove($j)
        }
    }
    Start-Sleep -Milliseconds 100
}
$pool.Close()
Emit @{ type = 'done' }
