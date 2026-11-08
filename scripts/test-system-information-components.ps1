param(
    [string]$ReleaseDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'output/system-information/release'),
    [string]$QtExecutable = (Join-Path (Split-Path -Parent $PSScriptRoot) 'output/system-information/qt-build/Takao.Qt.SystemInformation.exe'),
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'output/system-information/stage3/components'),
    [switch]$CopyOnly
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$taskIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
try { $taskElevated = ([Security.Principal.WindowsPrincipal]::new($taskIdentity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) }
finally { $taskIdentity.Dispose() }
if (-not $taskElevated) {
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '" -ReleaseDirectory "' + $ReleaseDirectory + '" -QtExecutable "' + $QtExecutable + '" -OutputDirectory "' + $OutputDirectory + '"'
    if ($CopyOnly) { $arguments += ' -CopyOnly' }
    $worker = Start-Process (Get-Process -Id $PID).Path -Verb RunAs -WindowStyle Hidden -ArgumentList $arguments -PassThru -Wait
    exit $worker.ExitCode
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$resultPath = Join-Path $OutputDirectory 'results.json'
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath -Force }
$runDirectory = Join-Path $OutputDirectory ([guid]::NewGuid().ToString('N'))
$application = Join-Path $runDirectory 'application'
$profile = Join-Path $runDirectory 'preferences'
New-Item -ItemType Directory -Path $application,$profile -Force | Out-Null
Copy-Item -Path (Join-Path $ReleaseDirectory '*') -Destination $application -Recurse
Copy-Item -LiteralPath $QtExecutable -Destination (Join-Path $application 'Takao.Qt.exe') -Force
@{ version=1; intervalMs=1000; ui=@{ version=1; density='compact'; sidebarCollapsed=$false; processView='flat'; pages=@{ processes=@{query='Takao-si-loaded';sortKey='name';sortDirection='ascending';scrollTop=0}; performance=@{query='';sortKey='name';sortDirection='ascending';scrollTop=0;resourceType='hwinfo';resourceId=''} }; searchQuery=''; taskManagerPromptDismissed=$true } } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $profile 'preferences.json') -Encoding utf8
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
try { $listener.Start(); $port = $listener.LocalEndpoint.Port } finally { $listener.Stop() }
$variables = @{
    TASKMANAGER_DATA_DIR=$profile
    TASKMANAGER_QT_SYSTEM_INFORMATION_RESULT=(Join-Path $OutputDirectory 'results.json')
    TASKMANAGER_QT_PREFERENCE_DIAGNOSTIC=(Join-Path $OutputDirectory 'rejected-preferences.json')
    TASKMANAGER_QT_1080P='1'; QT_SCALE_FACTOR='1'; QT_SCREEN_SCALE_FACTORS='1'
    TASKMANAGER_QT_COPY_ONLY=$(if ($CopyOnly) { '1' } else { $null })
}
$previous = @{}
$testHost = $null
try {
    foreach ($name in $variables.Keys) {
        $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $variables[$name], 'Process')
    }
    $testHost = Start-Process (Join-Path $application 'Takao.Host.exe') -ArgumentList "--port=$port" -WorkingDirectory $application -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $OutputDirectory 'host.log') -RedirectStandardError (Join-Path $OutputDirectory 'host-stderr.log')
    @{ pid=$testHost.Id; path=$testHost.Path; creationTimeFileTime=$testHost.StartTime.ToUniversalTime().ToFileTimeUtc().ToString(); port=$port } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'owned-host.json') -Encoding utf8
    $deadline = [DateTime]::UtcNow.AddSeconds(120)
    while (-not $testHost.WaitForExit(1000)) {
        if ([DateTime]::UtcNow -gt $deadline) { throw 'System information component check timed out.' }
    }
    $testExit = $testHost.ExitCode
    $result = Get-Content -LiteralPath $resultPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($testExit -ne 0 -or $result.result -ne 'PASS') { throw "System information component check failed: $($result.error) (Host exit $testExit)." }
    Write-Host "System information components PASS: $($result.checks.Count) real component checks. $OutputDirectory/results.json"
}
finally {
    if ($testHost -and -not $testHost.HasExited) {
        try {
            $origin = "http://127.0.0.1:$port"
            $bootstrap = Invoke-RestMethod "$origin/api/bootstrap" -TimeoutSec 3
            if ($bootstrap.hostPid -eq $testHost.Id) {
                Invoke-RestMethod "$origin/api/window/close" -Method Post -Headers @{Origin=$origin; 'X-TaskManager-Token'=$bootstrap.token} -ContentType 'application/json' -Body '{}' -TimeoutSec 3 | Out-Null
                [void]$testHost.WaitForExit(10000)
            }
        } catch { Write-Warning 'Could not gracefully close this exact test-owned Host; cleaning its original process handle.' }
        if (-not $testHost.HasExited) {
            $testHost.Kill()
            [void]$testHost.WaitForExit(5000)
        }
    }
    if ($testHost) {
        @{ pid=$testHost.Id; exited=$testHost.HasExited; exitCode=$testHost.ExitCode } | ConvertTo-Json |
            Set-Content -LiteralPath (Join-Path $OutputDirectory 'cleanup.json') -Encoding utf8
    }
    foreach ($name in $variables.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
    if ($testHost) { $testHost.Dispose() }
}
