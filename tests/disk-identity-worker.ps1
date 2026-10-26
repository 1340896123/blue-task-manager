param([ValidateSet('browser','desktop')][string]$Mode = 'browser', [string]$ReleaseDirectory = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root
$output = Join-Path $root 'output/playwright/disk-identity'
New-Item -ItemType Directory -Path $output -Force | Out-Null
trap { [IO.File]::WriteAllText((Join-Path $output "worker-$Mode-error.txt"), ($_ | Out-String), [Text.UTF8Encoding]::new($false)); exit 1 }
$probe = Join-Path $root 'tests/TaskManager.WindowProbe/bin/Release/net10.0-windows10.0.19041.0/TaskManager.WindowProbe.exe'
if (-not (& $probe process-elevation $PID | ConvertFrom-Json).elevated) { throw 'Disk acceptance requires an actual elevated Windows token.' }
$env:TASKMANAGER_DISK_MODE = $Mode
if ($ReleaseDirectory) { $env:TASKMANAGER_DISK_RELEASE_DIRECTORY = [IO.Path]::GetFullPath($ReleaseDirectory) }
$info = [Diagnostics.ProcessStartInfo]::new((Get-Command node.exe).Source, ('"' + (Join-Path $root 'tests/disk-identity-e2e.mjs') + '"'))
$info.UseShellExecute=$false; $info.CreateNoWindow=$true; $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true; $info.WorkingDirectory=$root
$info.StandardOutputEncoding=[Text.UTF8Encoding]::new($false); $info.StandardErrorEncoding=[Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $output "worker-$Mode.json"), (@{elevated=$true;pid=$PID;running=$true} | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
$child = [Diagnostics.Process]::Start($info)
$stdout=$child.StandardOutput.ReadToEndAsync(); $stderr=$child.StandardError.ReadToEndAsync()
$child.WaitForExit()
[IO.File]::WriteAllText((Join-Path $output "$Mode.log"), ($stdout.Result + "`n" + $stderr.Result), [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $output "worker-$Mode.json"), (@{elevated=$true;pid=$PID;complete=$true;exitCode=$child.ExitCode} | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
exit $child.ExitCode
