param(
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'output/system-information/stage2'),
    [switch]$Worker
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try { $elevated = ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) }
finally { $identity.Dispose() }
if (-not $elevated) {
    if ($Worker) { throw 'Elevated native test worker is required.' }
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '" -OutputDirectory "' + $OutputDirectory + '" -Worker'
    $workerProcess = Start-Process (Get-Process -Id $PID).Path -Verb RunAs -WindowStyle Hidden -ArgumentList $arguments -PassThru -Wait
    exit $workerProcess.ExitCode
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$report = @{ result = 'RUNNING'; checks = [Collections.Generic.List[string]]::new(); source = 'production Host HTTP / JSON.NET / CsWin32; independent CIM comparison only' }
function Check([bool]$condition, [string]$message) { if (-not $condition) { throw $message }; $report.checks.Add($message) }
$taskHost = $null
$profile = Join-Path $OutputDirectory ('profile-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $profile | Out-Null
$originalProfile = $env:TASKMANAGER_DATA_DIR
$env:TASKMANAGER_DATA_DIR = $profile
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
try { $listener.Start(); $port = $listener.LocalEndpoint.Port } finally { $listener.Stop() }
$origin = "http://127.0.0.1:$port"
$client = [Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromSeconds(12)
try {
    $application = Join-Path $root 'src/Takao.Host/bin/Release/net10.0-windows10.0.19041.0/Takao.Host.exe'
    $taskHost = Start-Process $application -ArgumentList "--headless --port=$port" -WorkingDirectory (Split-Path $application) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $OutputDirectory 'host.log') -RedirectStandardError (Join-Path $OutputDirectory 'host-stderr.log')
    $report.hostPid = $taskHost.Id; $report.port = $port
    $deadline = [DateTime]::UtcNow.AddSeconds(25)
    do {
        try { $bootstrap = Invoke-RestMethod "$origin/api/bootstrap" -TimeoutSec 2 } catch { Start-Sleep -Milliseconds 150 }
    } until ($bootstrap -or [DateTime]::UtcNow -gt $deadline -or $taskHost.HasExited)
    Check ($bootstrap.hostPid -eq $taskHost.Id) 'Exact isolated production Host owns the HTTP endpoint'
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        try { $before = Invoke-RestMethod "$origin/api/snapshot" -TimeoutSec 2 } catch { Start-Sleep -Milliseconds 150 }
    } until ($before -or [DateTime]::UtcNow -gt $deadline)
    Check ($before.system.totalPhysicalBytes -gt 0) 'Production snapshot is live before hardware reads'
    $first = Invoke-RestMethod "$origin/api/system-information" -TimeoutSec 12
    Check ($first.status -in @('ready','partial')) 'Hardware overview finishes with actual ready/partial status'
    Check ($first.processors.Count -gt 0 -and $first.memory.modules.Count -gt 0 -and $first.disks.Count -gt 0 -and $first.displays.Count -gt 0) 'Real CPU, DIMM, physical disk and display rows are present'
    $cached = Invoke-RestMethod "$origin/api/system-information" -TimeoutSec 12
    Check ($cached.capturedAtUtc -eq $first.capturedAtUtc) 'Normal reads reuse the independent 60-second hardware cache'
    $tasks = @(); for ($index = 0; $index -lt 8; $index++) { $tasks += $client.GetStringAsync("$origin/api/system-information?refresh=true") }
    $values = @($tasks | ForEach-Object { $_.GetAwaiter().GetResult() | ConvertFrom-Json })
    $uniqueTimes = @($values.capturedAtUtc | Sort-Object -Unique)
    Check ($uniqueTimes.Count -eq 1) 'Overlapping explicit refresh requests share one native collection'
    Check ([DateTime]$values[0].capturedAtUtc -gt [DateTime]$first.capturedAtUtc) 'Explicit refresh advances the actual collection timestamp'
    $latest = $values[0]
    $independentFile = Join-Path $root 'output/system-information/independent-windows.json'
    if (-not (Test-Path -LiteralPath $independentFile)) {
        @{
            source='Independent read-only Windows CIM; comparison only'
            processors=@(Get-CimInstance Win32_Processor | Select-Object Name,Manufacturer,NumberOfCores,NumberOfLogicalProcessors)
            motherboard=@(Get-CimInstance Win32_BaseBoard | Select-Object Manufacturer,Product,Version)
            memory=@(Get-CimInstance Win32_PhysicalMemory | Select-Object DeviceLocator,Manufacturer,PartNumber,Capacity,Speed,ConfiguredClockSpeed)
            disks=@(Get-CimInstance Win32_DiskDrive | Select-Object Index,Model,Size)
            displays=@(Get-CimInstance Win32_VideoController | Select-Object CurrentHorizontalResolution,CurrentVerticalResolution,CurrentRefreshRate)
            operatingSystem=@(Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,BuildNumber,OSArchitecture)
        } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $independentFile -Encoding utf8
    }
    $independent = Get-Content $independentFile -Raw | ConvertFrom-Json
    Check ($latest.processors[0].name -eq $independent.processors[0].Name -and $latest.processors[0].coreCount -eq $independent.processors[0].NumberOfCores -and $latest.processors[0].logicalProcessorCount -eq $independent.processors[0].NumberOfLogicalProcessors) 'SMBIOS CPU model/core/thread counts match independent Windows CIM'
    Check ($latest.motherboard.product -eq $independent.motherboard[0].Product -and $latest.motherboard.manufacturer -eq $independent.motherboard[0].Manufacturer) 'Motherboard identity matches independent Windows CIM'
    Check ($latest.memory.moduleCount -eq $independent.memory.Count) 'Installed DIMM count matches independent Windows CIM'
    foreach ($module in $latest.memory.modules) {
        $comparison = @($independent.memory | Where-Object DeviceLocator -eq $module.deviceLocator)
        Check ($comparison.Count -eq 1 -and [decimal]$module.capacityBytes -eq [decimal]$comparison[0].Capacity -and $module.partNumber -eq $comparison[0].PartNumber.Trim() -and $module.configuredSpeedMtPerSecond -eq $comparison[0].ConfiguredClockSpeed) ('DIMM slot/capacity/part/configured speed match independent CIM: ' + $module.deviceLocator)
    }
    foreach ($disk in $latest.disks) {
        $comparison = @($independent.disks | Where-Object Index -eq $disk.deviceNumber)
        Check ($comparison.Count -eq 1 -and $disk.model -and [decimal]$disk.capacityBytes -gt 0 -and [Math]::Abs([double]$disk.capacityBytes - [double]$comparison[0].Size) / [double]$disk.capacityBytes -lt .001) ('Verified physical disk model and actual capacity match Windows geometry within 0.1%: ' + $disk.deviceNumber)
    }
    $active = @($independent.displays | Where-Object { $_.CurrentHorizontalResolution -gt 0 })
    Check (@($latest.displays | Where-Object { $_.widthPixels -eq $active[0].CurrentHorizontalResolution -and $_.heightPixels -eq $active[0].CurrentVerticalResolution -and $_.refreshRateHz -eq $active[0].CurrentRefreshRate }).Count -gt 0) 'Current display mode matches independent Windows CIM'
    Check ($latest.operatingSystem.build.Split('.')[0] -eq $independent.operatingSystem[0].BuildNumber -and $latest.operatingSystem.architecture -eq 'x64') 'Actual Windows build matches independent CIM and native system architecture is x64'
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do { Start-Sleep -Milliseconds 250; $after = Invoke-RestMethod "$origin/api/snapshot" -TimeoutSec 3 } until ($after.sampleId -gt $before.sampleId -or [DateTime]::UtcNow -gt $deadline)
    Check ($after.sampleId -gt $before.sampleId) 'Real-time snapshot continues advancing independently of hardware refresh'
    $saved = Invoke-RestMethod "$origin/api/preferences" -Method Post -Headers @{ Origin=$origin; 'X-TaskManager-Token'=$bootstrap.token } -ContentType 'application/json' -Body '{"version":1,"density":"compact","sidebarCollapsed":false,"processView":"flat","pages":{"systemInformation":{"query":"memory","sortKey":"name","sortDirection":"ascending","scrollTop":40},"performance":{"query":"","sortKey":"name","sortDirection":"ascending","resourceType":"hwinfo"}},"taskManagerPromptDismissed":true}' -TimeoutSec 3
    Check ($saved.preferences.pages.systemInformation.query -eq 'memory' -and $saved.preferences.pages.performance.resourceType -eq 'hwinfo') 'System information preferences save while legacy HWiNFO resource preferences remain accepted'
    $report.hardware = $latest
    $report.concurrentRefreshTimestamps = $uniqueTimes
    $report.snapshotBefore = $before.sampleId; $report.snapshotAfter = $after.sampleId
    $report.result = 'PASS'
} catch { $report.result = 'FAIL'; $report.error = $_.Exception.Message }
finally {
    $client.Dispose()
    if ($taskHost -and -not $taskHost.HasExited) { $taskHost.Kill(); [void]$taskHost.WaitForExit(5000) }
    $report.ownedHostExited = if ($taskHost) { $taskHost.HasExited } else { $true }
    if ($taskHost) { $taskHost.Dispose() }
    $env:TASKMANAGER_DATA_DIR = $originalProfile
    $report | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $OutputDirectory 'api-results.json') -Encoding utf8
}
if ($report.result -ne 'PASS') { Write-Error $report.error; exit 1 }
Write-Host "System information PASS: $($report.checks.Count) checks; $OutputDirectory/api-results.json"
