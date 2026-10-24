param([string]$ReleaseDirectory, [switch]$Smoke)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root
$output = Join-Path $root $(if($Smoke){'output/playwright/connection-recovery-installed'}else{'output/playwright/connection-recovery'})
New-Item -ItemType Directory -Path $output -Force | Out-Null
$probe = Join-Path $root 'tests/TaskManager.WindowProbe/bin/Release/net10.0-windows10.0.19041.0/TaskManager.WindowProbe.exe'
$sdk = Join-Path $root 'output/tools/network-sdk-10.0.401'
$env:DOTNET_ROOT=$sdk
$env:DOTNET_ROOT_X64=$sdk
$env:PATH=$sdk+';'+$env:PATH
$elevation = & $probe process-elevation $PID | ConvertFrom-Json
if (-not $elevation.elevated) { throw 'Recovery acceptance requires an actual elevated Windows token.' }
if (-not $ReleaseDirectory) { $ReleaseDirectory = Join-Path $root 'output/connection-recovery/TaskManager-win-x64' }
$env:TASKMANAGER_RECOVERY_RELEASE=[IO.Path]::GetFullPath($ReleaseDirectory)
$env:TASKMANAGER_RELEASE_DIRECTORY=$env:TASKMANAGER_RECOVERY_RELEASE
if (-not (Test-Path -LiteralPath (Join-Path $ReleaseDirectory 'TaskManager.Host.exe'))) { throw 'Complete dedicated runtime required.' }
$status=Join-Path $output 'worker-status.json'
$results=@()
$suites=if($Smoke){@('connection-recovery')}else{@('connection-recovery','native-api','recovery-e2e','desktop-e2e')}
foreach($suite in $suites) {
    [IO.File]::WriteAllText($status,(@{elevated=$true;pid=$PID;running=$suite;results=@($results)}|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
    $arguments='"'+(Join-Path $root "tests/$suite.mjs")+'"'+$(if($Smoke){' --smoke'}else{''})
    $info=[Diagnostics.ProcessStartInfo]::new((Get-Command node.exe).Source,$arguments)
    $info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.WorkingDirectory=$root
    $info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    $info.StandardOutputEncoding=[Text.UTF8Encoding]::new($false);$info.StandardErrorEncoding=[Text.UTF8Encoding]::new($false)
    $started=[DateTime]::UtcNow;$child=[Diagnostics.Process]::Start($info)
    $stdout=$child.StandardOutput.ReadToEndAsync();$stderr=$child.StandardError.ReadToEndAsync();$child.WaitForExit()
    $safe=($stdout.Result+"`n"+$stderr.Result) -replace '(?i)((?:electronAuthToken|authToken|X-TaskManager-Token|authorization|secret)["'']?\s*[=:]\s*)[^\s,;]+','$1[REDACTED]'
    [IO.File]::WriteAllText((Join-Path $output "$suite.log"),$safe,[Text.UTF8Encoding]::new($false))
    $results+=@{suite=$suite;exitCode=$child.ExitCode;durationSeconds=([DateTime]::UtcNow-$started).TotalSeconds}
    $code=$child.ExitCode;$child.Dispose()
    if($code -ne 0){break}
}
[IO.File]::WriteAllText($status,(@{elevated=$true;pid=$PID;complete=$true;results=@($results)}|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
if(@($results|Where-Object {$_.exitCode -ne 0}).Count){exit 1}
