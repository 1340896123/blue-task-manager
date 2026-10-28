param([string]$Suites = 'windows-layout', [switch]$Desktop, [ValidateSet('native','browser','desktop')][string]$HwInfoMode = 'native')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root
$output = Join-Path $root 'output/playwright/windows-layout'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$mode = if ($Desktop) { 'desktop' } else { 'browser' }
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$probe = Join-Path $root 'tests/Takao.WindowProbe/bin/Release/net10.0-windows10.0.19041.0/Takao.WindowProbe.exe'
$elevation = & $probe process-elevation $PID | ConvertFrom-Json
if (-not $elevation.elevated) { throw 'Acceptance worker must have a real elevated Windows token.' }
$allowed = @('windows-layout','windows-layout-audit','static-v11','native-api','native-metadata-v11','native-core-v11','native-management-v11','native-telemetry-v11','gpu-identity-v11','process-ui-v11','pages-ui-v11','ui-smoke','recovery-e2e','desktop-e2e','process-desktop-v11','native-desktop-v11','system-locations-v11','restricted-process-ui-v11','product-icon','taskbar-integration','desktop-performance-v11','grid-ui','shared-search','hwinfo-cpu','network-throughput')
$results = @()
if ($Desktop) {
    $env:TASKMANAGER_LAYOUT_DESKTOP='1'
    $env:TASKMANAGER_GRID_DESKTOP='1'
    $env:TASKMANAGER_SHARED_SEARCH_DESKTOP='1'
    $env:TASKMANAGER_GRID_RELEASE_DIRECTORY=Join-Path $root 'output/Takao-win-x64'
    $env:TASKMANAGER_RELEASE_DIRECTORY=Join-Path $root 'output/Takao-win-x64'
}
$node = (Get-Command node.exe).Source
$env:TASKMANAGER_HWINFO_MODE = if ($Desktop) { 'desktop' } else { $HwInfoMode }
foreach ($suite in $Suites.Split(',')) {
    if ($allowed -notcontains $suite) { throw 'Suite is not on the fixed acceptance allowlist.' }
    $started = [DateTime]::UtcNow
    [IO.File]::WriteAllText((Join-Path $output "worker-$mode-status.json"), (@{elevated=$true;pid=$PID;running=$suite;results=@($results)} | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    # Only the test controller's output is saved. Host stdout/stderr stays ignored
    # or uses the existing bounded redaction in desktop-e2e; no raw token logs.
    $info = [Diagnostics.ProcessStartInfo]::new($node, ('"' + (Join-Path $root "tests/$suite.mjs") + '"'))
    $info.UseShellExecute=$false; $info.CreateNoWindow=$true
    $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
    $info.StandardOutputEncoding=[Text.UTF8Encoding]::new($false); $info.StandardErrorEncoding=[Text.UTF8Encoding]::new($false)
    $info.WorkingDirectory=$root
    $child=[Diagnostics.Process]::Start($info)
    $stdout=$child.StandardOutput.ReadToEndAsync(); $stderr=$child.StandardError.ReadToEndAsync()
    $child.WaitForExit()
    $safe = ($stdout.Result + "`n" + $stderr.Result) -replace '(?i)((?:electronAuthToken|authToken|X-TaskManager-Token|authorization|secret)["'']?\s*[=:]\s*)[^\s,;]+','$1[REDACTED]'
    [IO.File]::WriteAllText((Join-Path $output "$suite-$mode.log"), $safe, [Text.UTF8Encoding]::new($false))
    $results += @{suite=$suite;exitCode=$child.ExitCode;durationSeconds=([DateTime]::UtcNow-$started).TotalSeconds}
    $child.Dispose()
}
[IO.File]::WriteAllText((Join-Path $output "worker-$mode-status.json"), (@{elevated=$true;pid=$PID;complete=$true;results=@($results)} | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $output "worker-$mode-status.json") -Destination (Join-Path $output "worker-$mode-$stamp.json")
