param([switch]$Desktop)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root
$output = Join-Path $root 'output/playwright/application-icons-adapters'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$mode = if ($Desktop) { 'desktop' } else { 'browser' }
$probe = Join-Path $root 'tests/TaskManager.WindowProbe/bin/Release/net10.0-windows10.0.19041.0/TaskManager.WindowProbe.exe'
$elevation = & $probe process-elevation $PID | ConvertFrom-Json
if (-not $elevation.elevated) { throw 'Acceptance worker requires a real elevated Windows token.' }
$env:TASKMANAGER_ICONS_DESKTOP = if ($Desktop) { '1' } else { '0' }
$suites = @('application-icons-adapters')
$results = @()
foreach ($suite in $suites) {
    [IO.File]::WriteAllText((Join-Path $output "worker-$mode-status.json"), (@{elevated=$true;pid=$PID;running=$suite;results=@($results)} | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
    $started = [DateTime]::UtcNow
    $info = [Diagnostics.ProcessStartInfo]::new((Get-Command node.exe).Source, ('"' + (Join-Path $root "tests/$suite.mjs") + '"'))
    $info.UseShellExecute=$false; $info.CreateNoWindow=$true; $info.WorkingDirectory=$root
    $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
    $info.StandardOutputEncoding=[Text.UTF8Encoding]::new($false); $info.StandardErrorEncoding=[Text.UTF8Encoding]::new($false)
    $child=[Diagnostics.Process]::Start($info)
    $stdout=$child.StandardOutput.ReadToEndAsync(); $stderr=$child.StandardError.ReadToEndAsync(); $child.WaitForExit()
    $safe=($stdout.Result + "`n" + $stderr.Result) -replace '(?i)((?:electronAuthToken|authToken|X-TaskManager-Token|authorization|secret)["'']?\s*[=:]\s*)[^\s,;]+','$1[REDACTED]'
    [IO.File]::WriteAllText((Join-Path $output "$suite-$mode.log"),$safe,[Text.UTF8Encoding]::new($false))
    $results+=@{suite=$suite;exitCode=$child.ExitCode;durationSeconds=([DateTime]::UtcNow-$started).TotalSeconds};$child.Dispose()
}
[IO.File]::WriteAllText((Join-Path $output "worker-$mode-status.json"), (@{elevated=$true;complete=$true;results=@($results)} | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
