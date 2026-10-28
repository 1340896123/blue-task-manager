param([ValidateSet('browser','desktop','regression','desktop-regression')][string]$Mode = 'browser', [string]$ReleaseDirectory = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root
$output = Join-Path $root 'output/playwright/startup-status'
New-Item -ItemType Directory -Path $output -Force | Out-Null
trap { [IO.File]::WriteAllText((Join-Path $output "worker-$Mode-error.txt"), ($_ | Out-String), [Text.UTF8Encoding]::new($false)); exit 1 }
$probe = Join-Path $root 'tests/TaskManager.WindowProbe/bin/Release/net10.0-windows10.0.19041.0/TaskManager.WindowProbe.exe'
if (-not (& $probe process-elevation $PID | ConvertFrom-Json).elevated) { throw 'Startup acceptance requires an actual elevated Windows token.' }
$env:TASKMANAGER_STARTUP_MODE = $Mode
if ($ReleaseDirectory) { $env:TASKMANAGER_STARTUP_RELEASE_DIRECTORY = [IO.Path]::GetFullPath($ReleaseDirectory); $env:TASKMANAGER_RELEASE_DIRECTORY = $env:TASKMANAGER_STARTUP_RELEASE_DIRECTORY }
$scriptName = switch($Mode) { 'regression' {'regression-v11.mjs'} 'desktop-regression' {'desktop-e2e.mjs'} default {'startup-status-e2e.mjs'} }
$info = [Diagnostics.ProcessStartInfo]::new((Get-Command node.exe).Source, ('"' + (Join-Path $root "tests/$scriptName") + '"'))
$info.UseShellExecute=$false; $info.CreateNoWindow=$true; $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true; $info.WorkingDirectory=$root
$info.StandardOutputEncoding=[Text.UTF8Encoding]::new($false); $info.StandardErrorEncoding=[Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $output "worker-$Mode.json"), (@{elevated=$true;pid=$PID;running=$true;script=$scriptName;releaseDirectory=$ReleaseDirectory} | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
$child = [Diagnostics.Process]::Start($info)
$stdout=$child.StandardOutput.ReadToEndAsync(); $stderr=$child.StandardError.ReadToEndAsync()
$child.WaitForExit()
[IO.File]::WriteAllText((Join-Path $output "$Mode.log"), ($stdout.Result + "`n" + $stderr.Result), [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $output "worker-$Mode.json"), (@{elevated=$true;pid=$PID;complete=$true;exitCode=$child.ExitCode;script=$scriptName;releaseDirectory=$ReleaseDirectory} | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
exit $child.ExitCode
