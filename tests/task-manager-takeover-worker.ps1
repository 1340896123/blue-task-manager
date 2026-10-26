param([string]$ReleaseDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root
$output = Join-Path $root 'output/playwright/task-manager-takeover'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$probe = Join-Path $root 'tests/TaskManager.WindowProbe/bin/Release/net10.0-windows10.0.19041.0/TaskManager.WindowProbe.exe'
if (-not (& $probe process-elevation $PID | ConvertFrom-Json).elevated) { throw 'Takeover acceptance requires a real elevated Windows token.' }
if (-not $ReleaseDirectory) { $ReleaseDirectory = Join-Path $root 'output/takeover/TaskManager-win-x64' }
$env:TASKMANAGER_TAKEOVER_RELEASE = [IO.Path]::GetFullPath($ReleaseDirectory)
$status = Join-Path $output 'worker-status.json'
[IO.File]::WriteAllText($status, (@{elevated=$true;pid=$PID;running='task-manager-takeover'} | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
$info = [Diagnostics.ProcessStartInfo]::new((Get-Command node.exe).Source, ('"' + (Join-Path $root 'tests/task-manager-takeover.mjs') + '"'))
$info.UseShellExecute=$false; $info.CreateNoWindow=$true; $info.WorkingDirectory=$root
$info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
$info.StandardOutputEncoding=[Text.UTF8Encoding]::new($false); $info.StandardErrorEncoding=[Text.UTF8Encoding]::new($false)
$started=[DateTime]::UtcNow; $child=[Diagnostics.Process]::Start($info)
$stdout=$child.StandardOutput.ReadToEndAsync(); $stderr=$child.StandardError.ReadToEndAsync()
$child.WaitForExit()
$safe=($stdout.Result + "`n" + $stderr.Result) -replace '(?i)((?:electronAuthToken|authToken|X-TaskManager-Token|authorization|secret)["'']?\s*[=:]\s*)[^\s,;]+','$1[REDACTED]'
[IO.File]::WriteAllText((Join-Path $output 'acceptance.log'), $safe, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($status, (@{elevated=$true;pid=$PID;complete=$true;exitCode=$child.ExitCode;durationSeconds=([DateTime]::UtcNow-$started).TotalSeconds} | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
$code=$child.ExitCode; $child.Dispose(); exit $code
