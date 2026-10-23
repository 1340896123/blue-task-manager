param([string]$InstallerPath)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $taskRoot 'output/admin-installer'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$reportPath = Join-Path $output 'installer-lifecycle.json'
$checks = [Collections.Generic.List[string]]::new()
$runs = [Collections.Generic.List[object]]::new()
$registration = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\BlueDomainTaskManager'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('Blue Domain NSIS Test ' + [guid]::NewGuid().ToString('N'))
$installation = Join-Path $testRoot 'BlueDomainTaskManager'
$probeExe = Join-Path $taskRoot 'tests/TaskManager.WindowProbe/bin/Release/net10.0-windows10.0.19041.0/TaskManager.WindowProbe.exe'
$workerToken = (& $probeExe process-elevation $PID | ConvertFrom-Json)
if (-not $workerToken.elevated) { throw 'Run this test through standard Windows RunAs with an elevated token.' }
if (Test-Path -LiteralPath $registration) { throw 'An existing installation is registered; lifecycle test refuses to replace it.' }
if (-not $InstallerPath) { $InstallerPath = Join-Path $taskRoot 'output/releases/BlueDomainTaskManager-v1.1.1-win-x64-setup.exe' }
$InstallerPath = (Resolve-Path -LiteralPath $InstallerPath).Path
$shell = New-Object -ComObject WScript.Shell
$commonMenu = Join-Path ([Environment]::GetFolderPath('CommonStartMenu')) 'Programs/蓝域任务管理器.lnk'
$commonDesktop = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) '蓝域任务管理器.lnk'
foreach ($link in @($commonMenu,$commonDesktop)) { if (Test-Path -LiteralPath $link) { throw 'A product shortcut already exists; test refuses to replace it.' } }
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message }; $checks.Add($Message) }
function Run-Setup([string]$Executable, [string]$Arguments) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable, $Arguments)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $process = [Diagnostics.Process]::Start($start)
    if (-not $process.WaitForExit(60000)) { throw "Test-owned installer timed out: $($process.Id)" }
    $runs.Add(@{executable=[IO.Path]::GetFileName($Executable);arguments=$Arguments;exitCode=$process.ExitCode})
    return $process.ExitCode
}
function Ifeo-Fingerprint {
    $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,[Microsoft.Win32.RegistryView]::Registry64)
    $key = $base.OpenSubKey('SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\taskmgr.exe')
    try {
        $values = @()
        if ($key) { foreach ($name in ($key.GetValueNames() | Sort-Object)) { $values += @{name=$name;kind=$key.GetValueKind($name).ToString();data=$key.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)} } }
        return (@{exists=($null -ne $key);values=$values;subkeys=$(if ($key) { @($key.GetSubKeyNames() | Sort-Object) } else { @() })} | ConvertTo-Json -Depth 8 -Compress)
    } finally { if ($key) { $key.Dispose() }; $base.Dispose() }
}
$beforeIfeo = Ifeo-Fingerprint
$runtimeConfig = $null
try {
    Check ((Run-Setup $InstallerPath "/S /D=$installation") -eq 0) 'silent NSIS installation succeeds in a dedicated path containing spaces'
    $installedHost = Join-Path $installation 'TaskManager.Host.exe'
    Check ((Test-Path -LiteralPath $installedHost) -and (Test-Path -LiteralPath (Join-Path $installation 'coreclr.dll'))) 'installed payload contains the real Host and local .NET runtime'
    $record = Get-ItemProperty -LiteralPath $registration
    Check ($record.InstallLocation -eq $installation -and $record.DisplayVersion -eq '1.1.1') 'HKLM64 uninstall entry records the exact installed directory and version'
    foreach ($link in @($commonMenu,$commonDesktop)) { Check ((Test-Path -LiteralPath $link) -and $shell.CreateShortcut($link).TargetPath -eq $installedHost) 'all-users shortcut targets the actual administrator Host' }
    $mt = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\mt.exe'
    & $mt ('-inputresource:' + (Join-Path $installation 'Uninstall.exe') + ';#1') ('-out:' + (Join-Path $output 'uninstaller.manifest.xml')) *> (Join-Path $output 'uninstaller-manifest.log')
    Check ($LASTEXITCODE -eq 0 -and (Get-Content (Join-Path $output 'uninstaller.manifest.xml') -Raw).Contains('requireAdministrator')) 'actual installed uninstaller manifest requires administrator privileges'
    $hashRows = Get-Content (Join-Path $taskRoot 'output/nsis/payload.sha256')
    foreach ($row in $hashRows) {
        $hash,$relative = $row -split '  ',2
        if ((Get-FileHash -LiteralPath (Join-Path $installation $relative) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) { throw "Installed payload mismatch: $relative" }
    }
    Check $true 'every installed payload file matches the build SHA256 inventory'
    Check ((Ifeo-Fingerprint) -eq $beforeIfeo) 'installation leaves the complete actual taskmgr IFEO key unchanged'
    $env:TASKMANAGER_RELEASE_DIRECTORY = $installation
    $env:TASKMANAGER_ADMIN_EVIDENCE_PREFIX = 'installed-administrator-desktop'
    & node.exe (Join-Path $PSScriptRoot 'admin-desktop.mjs') *> (Join-Path $output 'installed-desktop.log')
    Check ($LASTEXITCODE -eq 0) 'installed production Electron desktop passes real elevated token, local coreclr, workload and close checks'
    $sentinel = Join-Path $installation 'user-added-file.txt'
    [IO.File]::WriteAllText($sentinel, 'This file belongs to this test and is outside the package inventory.')
    Check ((Run-Setup $InstallerPath "/S /D=$installation") -eq 0) 'same-version installation repairs the existing dedicated installation'
    Check (Test-Path -LiteralPath $sentinel) 'reinstallation preserves an extra file outside the package inventory'
    $other = Join-Path $testRoot 'Other/BlueDomainTaskManager'
    Check ((Run-Setup $InstallerPath "/S /D=$other") -ne 0 -and -not (Test-Path -LiteralPath $other)) 'an explicit alternate installation path is refused while an installation is registered'
    if ([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners().Port -contains 5214) { throw 'Lifecycle test port 5214 is already in use.' }
    $hostStart = [Diagnostics.ProcessStartInfo]::new($installedHost, '--headless --port=5214')
    $hostStart.UseShellExecute = $false
    $hostStart.CreateNoWindow = $true
    $hostStart.EnvironmentVariables['TASKMANAGER_DATA_DIR'] = Join-Path $testRoot 'isolated-preferences'
    $ownHost = [Diagnostics.Process]::Start($hostStart)
    try {
        $ready = $false
        $deadline = [DateTime]::UtcNow.AddSeconds(30)
        while (-not $ready -and [DateTime]::UtcNow -lt $deadline) {
            try { $ready = (Invoke-RestMethod 'http://127.0.0.1:5214/api/health' -TimeoutSec 2).ready } catch { Start-Sleep -Milliseconds 150 }
        }
        Check $ready 'test-owned installed headless instance starts with the required administrator token'
        Check ((Run-Setup $InstallerPath "/S /D=$installation") -ne 0 -and -not $ownHost.HasExited) 'running installed Host blocks reinstallation without terminating the test process'
        Check ((Run-Setup (Join-Path $installation 'Uninstall.exe') ('/S _?=' + $installation)) -ne 0 -and -not $ownHost.HasExited) 'running installed Host blocks uninstall without terminating the test process'
    } finally { if (-not $ownHost.HasExited) { $ownHost.Kill(); $ownHost.WaitForExit(10000) | Out-Null }; $ownHost.Dispose() }
    $runtimeConfig = Join-Path $installation 'TaskManager.Host.runtimeconfig.json'
    $originalConfig = [IO.File]::ReadAllBytes($runtimeConfig)
    try {
        [IO.File]::AppendAllText($runtimeConfig, ' ')
        # Standard NSIS _? keeps the refusal in this process so its real error
        # code is observable. Successful uninstall below uses the normal clone.
        Check ((Run-Setup (Join-Path $installation 'Uninstall.exe') ('/S _?=' + $installation)) -ne 0) 'uninstall refuses a modified maintenance runtime configuration'
        Check ((Test-Path -LiteralPath $installedHost) -and (Test-Path -LiteralPath $registration)) 'failed maintenance verification preserves the application and uninstall entry'
    } finally { [IO.File]::WriteAllBytes($runtimeConfig, $originalConfig) }
    Check ((Run-Setup (Join-Path $installation 'Uninstall.exe') '/S') -eq 0) 'verified uninstall executes the fixed production maintenance command successfully'
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (((Test-Path -LiteralPath $registration) -or (Test-Path -LiteralPath (Join-Path $installation 'Uninstall.exe'))) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 150 }
    Check (-not (Test-Path -LiteralPath $registration) -and -not (Test-Path -LiteralPath $installedHost)) 'uninstall removes its HKLM record and packaged Host'
    Check ((Test-Path -LiteralPath $sentinel) -and -not (Test-Path -LiteralPath $commonMenu) -and -not (Test-Path -LiteralPath $commonDesktop)) 'uninstall removes product shortcuts while preserving the extra file'
    $remaining = @(Get-ChildItem -LiteralPath $installation -Force -Recurse -File)
    Check ($remaining.Count -eq 1 -and $remaining[0].FullName -eq $sentinel) 'normal NSIS uninstall removes its own executable and all package files, leaving only the extra file'
    Check ((Ifeo-Fingerprint) -eq $beforeIfeo) 'install, repair and uninstall preserve the complete actual taskmgr IFEO key'
    @{ result='PASS'; checkedAtUtc=[DateTime]::UtcNow.ToString('O'); assertions=$checks.Count; checks=@($checks); runs=@($runs); installerSha256=(Get-FileHash -LiteralPath $InstallerPath -Algorithm SHA256).Hash.ToLowerInvariant(); payloadFiles=$hashRows.Count; actualIfeoMutation=$false; globalIfeoOwnershipVariants='not executed: no real global takeover written'; olderVersionUpgrade='not executed: no older NSIS installer exists'; testInstallation=$installation } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding UTF8
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    if (-not $resolvedTestRoot.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing cleanup outside test temporary directory.' }
    Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
} catch {
    @{ result='FAIL'; checkedAtUtc=[DateTime]::UtcNow.ToString('O'); message=$_.Exception.Message; completedChecks=@($checks); runs=@($runs); testInstallation=$installation; cleanup='preserved for diagnosis; only this test directory may be repaired or removed' } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding UTF8
    throw
} finally { Remove-Item Env:TASKMANAGER_RELEASE_DIRECTORY,Env:TASKMANAGER_ADMIN_EVIDENCE_PREFIX -ErrorAction SilentlyContinue }
