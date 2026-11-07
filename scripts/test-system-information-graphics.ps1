param(
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'output/system-information/graphics'),
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
$report = @{ result='RUNNING'; checks=[Collections.Generic.List[string]]::new(); scope='Production GPU hardware overview only; independent CIM comparison' }
function Check([bool]$condition, [string]$message) { if (-not $condition) { throw $message }; $report.checks.Add($message) }
$taskHost = $null
$profile = Join-Path $OutputDirectory ('profile-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $profile | Out-Null
$originalProfile = $env:TASKMANAGER_DATA_DIR
$env:TASKMANAGER_DATA_DIR = $profile
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
try { $listener.Start(); $port = $listener.LocalEndpoint.Port } finally { $listener.Stop() }
$origin = "http://127.0.0.1:$port"
try {
    $application = Join-Path $root 'src/Takao.Host/bin/Release/net10.0-windows10.0.19041.0/Takao.Host.exe'
    $taskHost = Start-Process $application -ArgumentList "--headless --port=$port" -WorkingDirectory (Split-Path $application) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $OutputDirectory 'host.log') -RedirectStandardError (Join-Path $OutputDirectory 'host-stderr.log')
    $report.hostPid=$taskHost.Id
    $deadline=[DateTime]::UtcNow.AddSeconds(25)
    do { try { $bootstrap=Invoke-RestMethod "$origin/api/bootstrap" -TimeoutSec 2 } catch { Start-Sleep -Milliseconds 150 } }
    until ($bootstrap -or [DateTime]::UtcNow -gt $deadline -or $taskHost.HasExited)
    Check ($bootstrap.hostPid -eq $taskHost.Id) 'HTTP belongs to this exact isolated production Host'
    $result=Invoke-RestMethod "$origin/api/system-information?refresh=true" -TimeoutSec 12
    $rows=@($result.graphicsAdapters)
    $independent=@(Get-CimInstance Win32_VideoController | Select-Object Name,PNPDeviceID)
    $physical=@($rows | Where-Object adapterKind -eq 'physical')
    $pci=@($independent | Where-Object { $_.PNPDeviceID.StartsWith('PCI\',[StringComparison]::OrdinalIgnoreCase) })
    Check ($physical.Count -eq $pci.Count -and $physical.Count -gt 0) 'Unique physical GPU count matches independent PCI device instances'
    foreach($row in $physical) {
        Check (@($pci | Where-Object { $_.PNPDeviceID -eq $row.physicalDeviceId }).Count -eq 1) ('GPU maps to the exact independently enumerated PCI instance: ' + $row.name)
        Check ($row.logicalAdapterIds.Count -gt 0 -and [decimal]$row.dedicatedMemoryBytes -gt 0) 'Physical GPU keeps actual logical aliases and dedicated video memory'
    }
    $ids=@($rows | Where-Object physicalDeviceId | Select-Object -ExpandProperty physicalDeviceId)
    Check (@($ids | Sort-Object -Unique).Count -eq $ids.Count) 'Each verified physical PnP device appears once, independent of model name'
    Check (@($rows | Where-Object { $_.isSoftware -and $_.adapterKind -ne 'software' }).Count -eq 0) 'DXGI software adapters retain explicit software classification'
    Check (@($physical | Where-Object { $_.logicalAdapterIds.Count -gt 1 }).Count -gt 0) 'This machine demonstrates multiple DXGI LUID aliases collapsed into one verified physical card'
    $report.graphicsAdapters=$rows
    $report.independent=$independent
    $report.result='PASS'
} catch { $report.result='FAIL'; $report.error=$_.Exception.Message }
finally {
    if ($taskHost -and -not $taskHost.HasExited) { $taskHost.Kill(); [void]$taskHost.WaitForExit(5000) }
    $report.ownedHostExited=if($taskHost){$taskHost.HasExited}else{$true}
    if($taskHost){$taskHost.Dispose()}
    $env:TASKMANAGER_DATA_DIR=$originalProfile
    $report | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutputDirectory 'results.json') -Encoding utf8
}
if($report.result -ne 'PASS'){Write-Error $report.error; exit 1}
Write-Host "GPU hardware identity PASS: $($report.checks.Count) checks"
