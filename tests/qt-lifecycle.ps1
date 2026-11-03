param(
    [string]$ReleaseDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'output/Takao-win-x64'),
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'output/qt-lifecycle')
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$probe = Join-Path $root 'tests/Takao.WindowProbe/bin/Release/net10.0-windows10.0.19041.0/Takao.WindowProbe.exe'
$workload = Join-Path $root 'tests/Takao.TestWorkload/bin/Release/net10.0-windows10.0.19041.0/Takao.TestWorkload.exe'
$origin = 'http://127.0.0.1:5214'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('Takao-Qt-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary,$OutputDirectory -Force | Out-Null
$checks = [Collections.Generic.List[string]]::new()
$hostProcess = $null; $qtProcess = $null; $target = $null; $token = $null; $stderr = $null
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message }; $checks.Add($Message) }
function Read-Api([string]$Route) { Invoke-RestMethod "$origin/api/$Route" -TimeoutSec 8 }
function Write-Api([string]$Route, $Body) { Invoke-RestMethod "$origin/api/$Route" -Method Post -Headers @{Origin=$origin;'X-TaskManager-Token'=$token} -ContentType 'application/json' -Body ($Body | ConvertTo-Json -Depth 8 -Compress) -TimeoutSec 15 }
function Read-OwnedTraceSessions([string]$Prefix) {
    for ($attempt=0; $attempt -lt 4; $attempt++) {
        $listing = @(& logman.exe query -ets 2>&1)
        if ($LASTEXITCODE -eq 0) { return @([regex]::Matches(($listing -join "`n"), ([regex]::Escape($Prefix) + '[a-f0-9]{32}')) | ForEach-Object Value) }
        if ($attempt -eq 3) { throw 'ETW enumeration failed; exact crash cleanup ownership cannot be verified.' }
        Start-Sleep -Milliseconds 150
    }
}
try {
    $elevation = & $probe process-elevation $PID | ConvertFrom-Json
    Check $elevation.elevated 'actual test token is elevated'
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $ReleaseDirectory 'Takao.Host.exe'), '--port=5214')
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardError = $true
    $start.EnvironmentVariables['TASKMANAGER_DATA_DIR'] = Join-Path $temporary 'preferences'
    $start.EnvironmentVariables['QT_SCALE_FACTOR'] = '1'
    $start.EnvironmentVariables['QT_SCREEN_SCALE_FACTORS'] = '1'
    $start.EnvironmentVariables['TASKMANAGER_QT_1080P'] = '1'
    $hostProcess = [Diagnostics.Process]::Start($start)
    $stderr = $hostProcess.StandardError.ReadToEndAsync()
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        if ($hostProcess.HasExited) { throw 'Qt desktop Host exited before becoming ready.' }
        try { $bootstrap = Read-Api 'bootstrap'; $state = Read-Api 'desktop/window-state'; $snapshot = Read-Api 'snapshot' } catch { $state = $null }
        if ($state -and $state.available -and $snapshot) { break }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    Check ($state -and $state.available) 'real Qt main window becomes available'
    Check ($bootstrap.desktopFramework -eq 'Qt 6 Widgets' -and $bootstrap.hostPid -eq $hostProcess.Id) 'Qt desktop is attached to this exact Host'
    $token = $bootstrap.token
    $qtProcess = Get-Process -Id $bootstrap.desktopProcessId
    Check (([IO.Path]::GetFullPath(($hostProcess.Modules | Where-Object ModuleName -eq 'coreclr.dll' | Select-Object -First 1).FileName)) -eq [IO.Path]::GetFullPath((Join-Path $ReleaseDirectory 'coreclr.dll'))) 'the Host loads its self-contained coreclr from the published directory'
    Check (([IO.Path]::GetFullPath(($qtProcess.Modules | Where-Object ModuleName -eq 'Qt6Widgets.dll' | Select-Object -First 1).FileName)) -eq [IO.Path]::GetFullPath((Join-Path $ReleaseDirectory 'Qt6Widgets.dll'))) 'the native window loads the published Qt Widgets runtime'
    $qtIdentity = & $probe process-elevation $qtProcess.Id | ConvertFrom-Json
    Check $qtIdentity.elevated 'Qt inherits the production elevated token'
    $window = & $probe "$($qtProcess.Id):$($qtIdentity.creationTimeFileTime)" resize-frame 1920 1080 | ConvertFrom-Json
    Check ($LASTEXITCODE -eq 0 -and $window.title -eq 'Takao' -and $window.width -eq 1920 -and $window.height -eq 1080) 'the only Win32 display geometry is a 1920x1080 Qt window'
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (@($snapshot.processes | Where-Object pid -eq $qtProcess.Id).Count -ne 1 -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 200; $snapshot = Read-Api 'snapshot' }
    Check (@($snapshot.processes | Where-Object pid -eq $qtProcess.Id).Count -eq 1) 'real CsWin32 sample includes Qt'
    Check (-not ($snapshot.processes | Where-Object pid -eq $qtProcess.Id).canTerminate) 'Qt child is protected'
    Check ($snapshot.system.totalPhysicalBytes -gt 0 -and $snapshot.system.processCount -gt 0) 'physical memory and process count are real'
    $previous = $snapshot.sampleId
    Start-Sleep -Milliseconds 1300
    $snapshot = Read-Api 'snapshot'
    Check ($snapshot.sampleId -gt $previous) 'real sampling advances while Qt renders'
    $target = Write-Api 'processes/start' @{executable=$workload;arguments=('"' + (Join-Path $temporary 'workload') + '" 45')}
    Check ($target.pid -gt 0 -and $target.creationTimeFileTime) 'a dedicated real workload is created'
    $finished = Write-Api "processes/$($target.pid)/terminate" @{creationTimeFileTime=$target.creationTimeFileTime}
    Check ($finished.completed -eq $true) 'termination is confirmed by the production identity-checked path'
    $target = $null
    # A fresh profile can expose the first-launch native modal. Close only that
    # exact owned dialog with WM_CLOSE before closing its main window.
    $prompts = @(& $probe windows '替换标准任务管理器' | ConvertFrom-Json | Where-Object pid -eq $qtProcess.Id)
    foreach ($prompt in $prompts) {
        & $probe native-ui $prompt.hwnd $qtProcess.Id $qtIdentity.creationTimeFileTime close | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not dismiss the exact test-owned first-launch dialog.' }
    }
    if ($prompts.Count) { Start-Sleep -Milliseconds 700 }
    [void](Write-Api 'window/close' @{})
    Check ($hostProcess.WaitForExit(15000)) 'closing Qt stops the Host'
    Check ($qtProcess.WaitForExit(5000)) 'Qt child exits without an orphan'
    Check ($hostProcess.ExitCode -eq 0) "the desktop lifecycle exits successfully (actual code $($hostProcess.ExitCode))"
    $client = [Net.Sockets.TcpClient]::new()
    try { $client.Connect('127.0.0.1',5214); $listening = $true } catch { $listening = $false } finally { $client.Dispose() }
    Check (-not $listening) 'the Host releases its listening port'
    # Independently verify KILL_ON_JOB_CLOSE with another exact test instance.
    $hostProcess.Dispose(); $qtProcess.Dispose()
    $start.EnvironmentVariables['TASKMANAGER_DATA_DIR'] = Join-Path $temporary 'crash-preferences'
    $hostProcess = [Diagnostics.Process]::Start($start)
    $stderr = $hostProcess.StandardError.ReadToEndAsync()
    $deadline = [DateTime]::UtcNow.AddSeconds(35)
    do {
        try { $bootstrap = Read-Api 'bootstrap'; $state = Read-Api 'desktop/window-state' } catch { $state=$null }
        if ($state -and $state.available -and $bootstrap.hostPid -eq $hostProcess.Id) { break }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $deadline)
    Check ($state -and $state.available -and $bootstrap.hostPid -eq $hostProcess.Id) 'a second exact Host owns its real Qt child for crash cleanup'
    $qtProcess = Get-Process -Id $bootstrap.desktopProcessId
    $hostIdentity = & $probe process-elevation $hostProcess.Id | ConvertFrom-Json
    $prefix = "Takao-Network-$($hostProcess.Id)-$($hostIdentity.creationTimeFileTime)-"
    $ownedSessions = @(Read-OwnedTraceSessions $prefix)
    $hostProcess.Kill(); [void]$hostProcess.WaitForExit(5000)
    Check ($qtProcess.WaitForExit(7000)) 'abrupt Host termination closes only its owned Qt job child'
    $ownedSessions += @(Read-OwnedTraceSessions $prefix)
    foreach ($session in ($ownedSessions | Select-Object -Unique)) {
        if (@(Read-OwnedTraceSessions $prefix) -contains $session) { & logman.exe stop $session -ets | Out-Null }
    }
    Check (@(Read-OwnedTraceSessions $prefix).Count -eq 0) 'no exact test-owned ETW logger remains after the deliberate crash'
    @{result='PASS';assertions=$checks.Count;checks=@($checks);display='1920x1080';scope='Production Qt lifecycle and real native API; WM_CLOSE is window management, not a physical input assertion'} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $OutputDirectory 'results.json') -Encoding utf8
} catch {
    @{result='FAIL';assertions=$checks.Count;checks=@($checks);error=$_.Exception.Message;stack=$_.ScriptStackTrace} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $OutputDirectory 'results.json') -Encoding utf8
    throw
} finally {
    if ($target -and $token) { try { [void](Write-Api "processes/$($target.pid)/terminate" @{creationTimeFileTime=$target.creationTimeFileTime}) } catch {} }
    if ($hostProcess -and -not $hostProcess.HasExited) {
        try { [void](Write-Api 'window/close' @{}); [void]$hostProcess.WaitForExit(10000) } catch {}
        if (-not $hostProcess.HasExited) { $hostProcess.Kill(); [void]$hostProcess.WaitForExit(5000) }
    }
    if ($stderr -and $stderr.IsCompleted) { $stderr.GetAwaiter().GetResult() | Set-Content (Join-Path $OutputDirectory 'host-stderr.txt') -Encoding utf8 }
    if ($qtProcess) { $qtProcess.Dispose() }; if ($hostProcess) { $hostProcess.Dispose() }
    $startupDiagnostic = Join-Path $temporary 'preferences/startup-error.log'
    if (Test-Path -LiteralPath $startupDiagnostic) { Copy-Item -LiteralPath $startupDiagnostic -Destination (Join-Path $OutputDirectory 'startup-error.log') -Force }
    # The random directory is owned by this exact test run.
    if ([IO.Path]::GetFullPath($temporary).StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()) + 'Takao-Qt-', [StringComparison]::OrdinalIgnoreCase)) { Remove-Item -LiteralPath $temporary -Recurse -Force }
}
