param([ValidateSet('browser','desktop','resources')][string]$Mode = 'browser', [string]$ReleaseDirectory = '', [string]$RuntimeDirectory = '', [switch]$VerifySelfContained)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root
$output = Join-Path $root 'output/playwright/network-throughput'
New-Item -ItemType Directory -Path $output -Force | Out-Null
trap { [IO.File]::WriteAllText((Join-Path $output "worker-$Mode-error.txt"), ($_ | Out-String), [Text.UTF8Encoding]::new($false)); exit 1 }
if ($RuntimeDirectory) {
    $networkRuntime = [IO.Path]::GetFullPath($RuntimeDirectory)
    if (-not (Test-Path -LiteralPath (Join-Path $networkRuntime 'host/fxr'))) { throw 'The optional .NET runtime directory must contain host/fxr.' }
    $env:DOTNET_ROOT = $networkRuntime; $env:DOTNET_ROOT_X64 = $networkRuntime
}
$probe = Join-Path $root 'tests/Takao.WindowProbe/bin/Release/net10.0-windows10.0.19041.0/Takao.WindowProbe.exe'
if (-not (& $probe process-elevation $PID | ConvertFrom-Json).elevated) { throw 'Network acceptance requires an actual elevated Windows token.' }
$env:TASKMANAGER_NETWORK_MODE = $Mode
if ($VerifySelfContained) { $env:TASKMANAGER_VERIFY_SELF_CONTAINED = '1' }
if ($ReleaseDirectory) { $env:TASKMANAGER_NETWORK_RELEASE_DIRECTORY = [IO.Path]::GetFullPath($ReleaseDirectory) }
$node = (Get-Command node.exe).Source
$networkSuite = if ($Mode -eq 'resources') { 'tests/network-resource-trend.mjs' } else { 'tests/network-throughput-ui.mjs' }
$info = [Diagnostics.ProcessStartInfo]::new($node, ('"' + (Join-Path $root $networkSuite) + '"'))
$info.UseShellExecute=$false; $info.CreateNoWindow=$true; $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true; $info.WorkingDirectory=$root
$info.StandardOutputEncoding=[Text.UTF8Encoding]::new($false); $info.StandardErrorEncoding=[Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $output "worker-$Mode.json"), (@{elevated=$true;pid=$PID;running=$true} | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
$child = [Diagnostics.Process]::Start($info)
$stdout=$child.StandardOutput.ReadToEndAsync(); $stderr=$child.StandardError.ReadToEndAsync()
$child.WaitForExit()
[IO.File]::WriteAllText((Join-Path $output "$Mode.log"), ($stdout.Result + "`n" + $stderr.Result), [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $output "worker-$Mode.json"), (@{elevated=$true;pid=$PID;complete=$true;exitCode=$child.ExitCode} | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
exit $child.ExitCode
