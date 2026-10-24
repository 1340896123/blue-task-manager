param([string]$ReleaseDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root
$output = Join-Path $root 'output/playwright/first-launch'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$probe = Join-Path $root 'tests/TaskManager.WindowProbe/bin/Release/net10.0-windows10.0.19041.0/TaskManager.WindowProbe.exe'
$elevation = & $probe process-elevation $PID | ConvertFrom-Json
if (-not $elevation.elevated) { throw 'First-launch acceptance requires a real elevated Windows token.' }
if (-not $ReleaseDirectory) { $ReleaseDirectory = Join-Path $root 'output/first-launch/TaskManager-win-x64' }
$env:TASKMANAGER_FIRST_LAUNCH_RELEASE = [IO.Path]::GetFullPath($ReleaseDirectory)
if (-not (Test-Path -LiteralPath (Join-Path $env:TASKMANAGER_FIRST_LAUNCH_RELEASE 'TaskManager.Host.exe'))) { throw 'Complete dedicated release directory is required.' }
$status = Join-Path $output 'worker-status.json'
[IO.File]::WriteAllText($status, (@{ elevated=$true; pid=$PID; running='first-launch-integration' } | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
$info = [Diagnostics.ProcessStartInfo]::new((Get-Command node.exe).Source, ('"' + (Join-Path $root 'tests/first-launch-integration.mjs') + '"'))
$info.UseShellExecute=$false; $info.CreateNoWindow=$true; $info.WorkingDirectory=$root
$info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
$info.StandardOutputEncoding=[Text.UTF8Encoding]::new($false); $info.StandardErrorEncoding=[Text.UTF8Encoding]::new($false)
$started=[DateTime]::UtcNow; $child=[Diagnostics.Process]::Start($info)
$stdout=$child.StandardOutput.ReadToEndAsync(); $stderr=$child.StandardError.ReadToEndAsync()
$child.WaitForExit()
$safe=($stdout.Result + "`n" + $stderr.Result) -replace '(?i)((?:electronAuthToken|authToken|X-TaskManager-Token|authorization|secret)["'']?\s*[=:]\s*)[^\s,;]+','$1[REDACTED]'
[IO.File]::WriteAllText((Join-Path $output 'integration.log'), $safe, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($status, (@{ elevated=$true; pid=$PID; complete=$true; exitCode=$child.ExitCode; durationSeconds=([DateTime]::UtcNow-$started).TotalSeconds } | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
$code=$child.ExitCode; $child.Dispose(); exit $code
