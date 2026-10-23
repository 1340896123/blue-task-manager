#Requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InstallerPath,
    [Parameter(Mandatory)][string]$PayloadDirectory,
    [string]$UninstallerName = 'Uninstall.exe',
    [string]$UninstallRegistryKeyName = 'BlueDomainTaskManager',
    [string]$StartMenuRelativePath = '蓝域任务管理器.lnk',
    [string]$DesktopShortcutName = '蓝域任务管理器.lnk'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# This script installs and removes an application. It must never be used as a
# developer-machine smoke test or be enabled locally by fabricating these values.
if (-not $IsWindows -or $env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'Installer acceptance is restricted to disposable GitHub-hosted Windows runners.'
}
$windowsIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
try {
    $principal = [Security.Principal.WindowsPrincipal]::new($windowsIdentity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'The actual Windows token is not elevated; installer acceptance cannot run.'
    }
} finally { $windowsIdentity.Dispose() }
if (-not [Environment]::Is64BitProcess) { throw 'Installer acceptance requires a 64-bit PowerShell process.' }

$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$output = Join-Path $projectRoot 'output/installer-ci'
$runnerTemporary = [IO.Path]::GetFullPath($env:RUNNER_TEMP)
if (-not (Test-Path -LiteralPath $runnerTemporary -PathType Container)) { throw 'RUNNER_TEMP is missing.' }
$testRoot = Join-Path $runnerTemporary ('Blue Domain Installer Acceptance ' + [Guid]::NewGuid().ToString('N'))
$installDirectory = Join-Path $testRoot 'BlueDomainTaskManager'
$ownedProcesses = [Collections.Generic.List[object]]::new()
$report = [ordered]@{
    result = 'RUNNING'
    startedAtUtc = [DateTime]::UtcNow.ToString('O')
    completedAtUtc = $null
    sourceCommit = $env:GITHUB_SHA
    workflowRunId = $env:GITHUB_RUN_ID
    environment = 'GitHub-hosted Windows; actual elevated token'
    installer = $null
    payloadFiles = 0
    checks = [Collections.Generic.List[object]]::new()
    ifeoFingerprints = [ordered]@{}
    headlessRuns = [Collections.Generic.List[object]]::new()
    desktopAssertions = 0
    screenshots = [Collections.Generic.List[string]]::new()
    installationRetainedOnFailure = $false
    error = $null
    cleanupErrors = [Collections.Generic.List[string]]::new()
    limitations = @('No global IFEO value is enabled or modified by the test.', 'Unsigned package; no signing credentials are used.', 'Hosted acceptance does not exercise interactive UAC consent or a non-elevated desktop.')
}

function Assert-Condition([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $report.checks.Add([ordered]@{ result = 'PASS'; message = $Message })
    Write-Host "PASS: $Message"
}

function Assert-ContainedPath([string]$Path, [string]$Parent) {
    $full = [IO.Path]::GetFullPath($Path)
    $prefix = [IO.Path]::GetFullPath($Parent).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Path is outside the explicitly owned test directory.'
    }
    return $full
}

function Assert-NoLinks([string]$Path) {
    $current = Get-Item -LiteralPath $Path -Force
    while ($null -ne $current) {
        if ($current.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Filesystem links are not allowed in installer acceptance paths.' }
        $current = if ($current -is [IO.DirectoryInfo]) { $current.Parent } else { $current.Directory }
    }
}

function Get-IfeoFingerprint {
    # reg.exe export retains every registry type, the default value, and all
    # descendants. Only the SHA256 is retained; the private export is deleted.
    $registryPath = 'SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\taskmgr.exe'
    $hive = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
    try {
        $key = $hive.OpenSubKey($registryPath, $false)
        if ($null -eq $key) {
            return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes('HKLM64 taskmgr.exe key does not exist'))).ToLowerInvariant()
        }
        $key.Dispose()
    } finally { $hive.Dispose() }
    $export = Assert-ContainedPath (Join-Path $testRoot ('ifeo-' + [Guid]::NewGuid().ToString('N') + '.reg')) $testRoot
    try {
        & (Join-Path $env:SystemRoot 'System32/reg.exe') export "HKLM\$registryPath" $export /y /reg:64 *> $null
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $export -PathType Leaf)) { throw 'Independent complete HKLM64 IFEO export failed.' }
        return (Get-FileHash -LiteralPath $export -Algorithm SHA256).Hash.ToLowerInvariant()
    } finally {
        if (Test-Path -LiteralPath $export -PathType Leaf) { Remove-Item -LiteralPath $export -Force }
    }
}

function Assert-IfeoUnchanged([string]$Stage) {
    $fingerprint = Get-IfeoFingerprint
    $report.ifeoFingerprints[$Stage] = $fingerprint
    Assert-Condition ($fingerprint -ceq $report.ifeoFingerprints['before']) "Complete HKLM64 IFEO configuration is unchanged $Stage"
}

function Get-PayloadManifest([string]$Directory) {
    $entries = @(Get-ChildItem -LiteralPath $Directory -Recurse -Force)
    if ($entries.Count -eq 0) { throw 'The self-contained payload is empty.' }
    foreach ($entry in $entries) {
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Payload contains a filesystem link.' }
        if ($entry.Name -eq '.git' -or $entry.Name -eq 'preferences.json' -or $entry.Name -match '^\.env(?:\.|$)') { throw 'Payload contains local repository or preference state.' }
        if (-not $entry.PSIsContainer) {
            [pscustomobject]@{
                RelativePath = $entry.FullName.Substring($Directory.TrimEnd('\', '/').Length + 1)
                Length = $entry.Length
                Hash = (Get-FileHash -LiteralPath $entry.FullName -Algorithm SHA256).Hash
            }
        }
    }
}

function Assert-InstalledPayload([object[]]$Manifest, [string]$Stage) {
    Assert-NoLinks $installDirectory
    $links = @(Get-ChildItem -LiteralPath $installDirectory -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })
    if ($links.Count -ne 0) { throw 'The installed tree contains a filesystem link.' }
    foreach ($item in $Manifest) {
        $file = Assert-ContainedPath (Join-Path $installDirectory $item.RelativePath) $installDirectory
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Installed payload file is missing: $($item.RelativePath)" }
        if ((Get-Item -LiteralPath $file).Length -ne $item.Length -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -cne $item.Hash) {
            throw "Installed payload hash differs: $($item.RelativePath)"
        }
    }
    Assert-Condition $true "All $($Manifest.Count) payload file lengths and SHA256 hashes match $Stage"
}

function Get-UninstallRegistration {
    $hive = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
    try {
        $key = $hive.OpenSubKey("SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\$UninstallRegistryKeyName", $false)
        if ($null -eq $key) { return $null }
        try {
            return [pscustomobject]@{
                InstallLocation = $key.GetValue('InstallLocation', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                DisplayVersion = $key.GetValue('DisplayVersion', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                UninstallString = $key.GetValue('UninstallString', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            }
        } finally { $key.Dispose() }
    } finally { $hive.Dispose() }
}

function Assert-Shortcut([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw 'The expected installation shortcut is missing.' }
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $null
    try {
        $shortcut = $shell.CreateShortcut($Path)
        $target = [IO.Path]::GetFullPath($shortcut.TargetPath)
        Assert-Condition ($target.Equals($hostExecutable, [StringComparison]::OrdinalIgnoreCase) -and [string]::IsNullOrEmpty($shortcut.Arguments)) 'Shortcut directly targets this installation Host without extra arguments'
    } finally {
        if ($null -ne $shortcut) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shortcut) }
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
    }
}

function Assert-Registration {
    $registration = Get-UninstallRegistration
    Assert-Condition ($null -ne $registration) 'Machine-wide 64-bit uninstall registration exists'
    Assert-Condition ([string]$registration.DisplayVersion -ceq $version) 'Uninstall registration version equals the Host project version'
    Assert-Condition ([IO.Path]::GetFullPath([string]$registration.InstallLocation).TrimEnd('\').Equals($installDirectory, [StringComparison]::OrdinalIgnoreCase)) 'Uninstall registration identifies only the dedicated test installation'
    $expected = '"' + $uninstallerPath + '"'
    Assert-Condition ([string]$registration.UninstallString -ceq $expected) 'Uninstall registration points to the fixed installed uninstaller'
    Assert-Shortcut $startMenuShortcut
    Assert-Shortcut $desktopShortcut
}

function Add-OwnedProcess([Diagnostics.Process]$Process, [int]$Depth = 0) {
    [void]$Process.SafeHandle
    $created = $Process.StartTime.ToUniversalTime().ToFileTimeUtc().ToString()
    if (-not @($ownedProcesses | Where-Object { $_.Id -eq $Process.Id -and $_.Created -ceq $created }).Count) {
        $ownedProcesses.Add([pscustomobject]@{ Id = $Process.Id; Created = $created; Depth = $Depth })
    }
}

function Get-MatchingOwnedProcess([object]$Identity) {
    $process = Get-Process -Id $Identity.Id -ErrorAction SilentlyContinue
    if ($null -eq $process) { return $null }
    try {
        [void]$process.SafeHandle
        if ($process.StartTime.ToUniversalTime().ToFileTimeUtc().ToString() -ceq $Identity.Created) { return $process }
    } catch {
        $exited = $false
        try { $exited = $process.HasExited } catch { }
        $process.Dispose()
        if ($exited) { return $null }
        throw 'A test-owned process identity could not be revalidated.'
    }
    $process.Dispose()
    return $null
}

function Stop-OwnedIdentity([object]$Identity) {
    $process = Get-MatchingOwnedProcess $Identity
    if ($null -eq $process) { return }
    try {
        # The process object retains the handle whose creation time was checked.
        $process.Kill()
        if (-not $process.WaitForExit(15000)) { throw 'A test-owned process did not exit.' }
    } finally { $process.Dispose() }
}

function Update-OwnedDescendants {
    # Windows parent IDs are used only while the exact saved parent identity is
    # still alive, and child creation must follow the parent creation time.
    $all = @(Get-CimInstance Win32_Process)
    $changed = $true
    while ($changed) {
        $changed = $false
        foreach ($parent in @($ownedProcesses.ToArray())) {
            $matching = Get-MatchingOwnedProcess $parent
            if ($null -eq $matching) { continue }
            try {
                foreach ($child in @($all | Where-Object { $_.ParentProcessId -eq $parent.Id })) {
                    $process = Get-Process -Id $child.ProcessId -ErrorAction SilentlyContinue
                    if ($null -eq $process) { continue }
                    try {
                        [void]$process.SafeHandle
                        if ($process.HasExited) { continue }
                        # CIM can truncate FILETIME ticks. Use it only to discover
                        # lineage, then retain the native Process creation time.
                        $created = $process.StartTime.ToUniversalTime().ToFileTimeUtc().ToString()
                        if ([long]$created -lt [long]$parent.Created) { continue }
                        if (@($ownedProcesses | Where-Object { $_.Id -eq $process.Id -and $_.Created -ceq $created }).Count) { continue }
                        $current = Get-CimInstance Win32_Process -Filter "ProcessId = $($process.Id)"
                        if ($null -eq $current -or $current.ParentProcessId -ne $parent.Id -or -not $current.ExecutablePath) { continue }
                        if (-not $process.MainModule.FileName.Equals([string]$current.ExecutablePath, [StringComparison]::OrdinalIgnoreCase)) { continue }
                        # Keep the exact parent's SafeHandle alive while recording
                        # the child, and confirm the same parent has not exited.
                        if ($matching.HasExited) { continue }
                        Add-OwnedProcess $process ($parent.Depth + 1)
                        $changed = $true
                    } catch {
                        if (-not $process.HasExited) { throw 'A discovered test descendant could not be revalidated.' }
                    } finally { $process.Dispose() }
                }
            } finally { $matching.Dispose() }
        }
    }
}

function Assert-NoInstalledProcess([string]$Stage) {
    $prefix = $installDirectory + [IO.Path]::DirectorySeparatorChar
    $discovered = @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and ([string]$_.ExecutablePath).StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) })
    $running = 0
    # This path did not exist before the test. Any instance in it was produced by
    # this installation/test; record the exact identity before failure cleanup.
    foreach ($entry in $discovered) {
        $process = Get-Process -Id $entry.ProcessId -ErrorAction SilentlyContinue
        if ($null -eq $process) { continue }
        try {
            [void]$process.SafeHandle
            if ($process.HasExited) { continue }
            $image = $process.MainModule.FileName
            if (-not $image.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or -not $image.Equals([string]$entry.ExecutablePath, [StringComparison]::OrdinalIgnoreCase)) { continue }
            Add-OwnedProcess $process
            $running++
        } catch {
            if (-not $process.HasExited) { throw 'An installed test process could not be revalidated.' }
        } finally { $process.Dispose() }
    }
    Assert-Condition ($running -eq 0) "No installed application process is running $Stage"
}

function Invoke-SilentInstall {
    # NSIS /D consumes the rest of the raw command line, including spaces. It is
    # intentionally the final argument with no quotes around the directory.
    $child = Start-Process -FilePath $installer -ArgumentList "/S /D=$installDirectory" -WorkingDirectory $projectRoot -WindowStyle Hidden -Wait -PassThru
    try { Assert-Condition ($child.ExitCode -eq 0) 'Standard NSIS silent installation exits successfully' }
    finally { $child.Dispose() }
    Start-Sleep -Milliseconds 700
    Assert-NoInstalledProcess 'after silent installation (no automatic launch)'
}

function Get-FreePort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    try { $listener.Start(); return ([Net.IPEndPoint]$listener.LocalEndpoint).Port }
    finally { $listener.Stop() }
}

function Invoke-HeadlessAcceptance([string]$Stage) {
    $port = Get-FreePort
    $origin = "http://127.0.0.1:$port"
    $start = [Diagnostics.ProcessStartInfo]::new($hostExecutable)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WorkingDirectory = $testRoot
    $start.ArgumentList.Add('--headless')
    $start.ArgumentList.Add("--port=$port")
    $start.Environment['TASKMANAGER_DATA_DIR'] = Join-Path $testRoot "preferences-$Stage"
    $missingRuntime = Join-Path $testRoot 'No Global DotNet'
    $start.Environment['DOTNET_ROOT'] = $missingRuntime
    $start.Environment['DOTNET_ROOT_X64'] = $missingRuntime
    $start.Environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'
    [void]$start.Environment.Remove('TASKMANAGER_CDP_PORT')
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $child = [Diagnostics.Process]::new()
    $child.StartInfo = $start
    if (-not $child.Start()) { throw 'The installed self-contained Host could not be started.' }
    $stdout = $child.StandardOutput.ReadToEndAsync()
    $stderr = $child.StandardError.ReadToEndAsync()
    Add-OwnedProcess $child
    $identity = [pscustomobject]@{ Id = $child.Id; Created = $child.StartTime.ToUniversalTime().ToFileTimeUtc().ToString(); Depth = 0 }
    try {
        $health = $null
        $deadline = [DateTime]::UtcNow.AddSeconds(60)
        while ([DateTime]::UtcNow -lt $deadline) {
            if ($child.HasExited) { throw 'The installed Host exited before its real API was ready.' }
            try {
                $health = Invoke-RestMethod -Uri "$origin/api/health" -TimeoutSec 3
                if ($health.ready) { break }
            } catch { $health = $null }
            Start-Sleep -Milliseconds 200
        }
        Assert-Condition ($null -ne $health -and $health.ready -and $health.platform -ceq 'Windows' -and $health.processId -eq $child.Id) "Installed Host exposes its own ready Windows API $Stage"
        Assert-Condition ($health.nativeSource -ceq 'Microsoft.Windows.CsWin32' -and $health.json -ceq 'Newtonsoft.Json') 'The installed API reports the production native collector and JSON.NET'
        $snapshot = Invoke-RestMethod -Uri "$origin/api/snapshot" -TimeoutSec 10
        $self = @($snapshot.processes | Where-Object { $_.pid -eq $child.Id -and [string]$_.creationTimeFileTime -ceq $identity.Created })
        Assert-Condition ($self.Count -eq 1 -and ([string]$self[0].path).Equals($hostExecutable, [StringComparison]::OrdinalIgnoreCase)) 'The real Windows snapshot contains the exact newly started Host PID, creation time, and installed path'
        $firstSampleId = $snapshot.sampleId
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        do {
            Start-Sleep -Milliseconds 350
            $snapshot = Invoke-RestMethod -Uri "$origin/api/snapshot" -TimeoutSec 10
        } while ($snapshot.sampleId -eq $firstSampleId -and [DateTime]::UtcNow -lt $deadline)
        Assert-Condition ($snapshot.sampleId -ne $firstSampleId) 'The installed native sampler advances with real Windows observations'
        $child.Refresh()
        $runtimeModules = @($child.Modules | Where-Object { $_.ModuleName -ieq 'coreclr.dll' })
        Assert-Condition ($runtimeModules.Count -eq 1 -and $runtimeModules[0].FileName.Equals((Join-Path $installDirectory 'coreclr.dll'), [StringComparison]::OrdinalIgnoreCase)) 'Actual coreclr.dll is loaded from this installation with invalid DOTNET_ROOT and multilevel lookup disabled'
        $report.headlessRuns.Add([ordered]@{ stage = $Stage; processId = $identity.Id; creationTimeFileTime = $identity.Created; coreclrFromInstallation = $true; nativeSnapshotAdvanced = $true })
    } finally {
        Stop-OwnedIdentity $identity
        $child.Dispose()
        # The redirected streams are drained privately and never become reports.
        [void]$stdout.GetAwaiter().GetResult()
        [void]$stderr.GetAwaiter().GetResult()
    }
}

function Copy-DesktopScreenshots([DateTime]$Since) {
    $destination = Join-Path $output 'screenshots'
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $candidates = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'output/playwright') -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^final-desktop[-a-z0-9]+\.png$' -and $_.LastWriteTimeUtc -ge $Since.AddSeconds(-2) })
    foreach ($file in $candidates) {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $destination $file.Name) -Force
        if (-not $report.screenshots.Contains($file.Name)) { $report.screenshots.Add($file.Name) }
    }
}

function Invoke-DesktopAcceptance {
    $started = [DateTime]::UtcNow
    $node = (Get-Command node -CommandType Application).Source
    $start = [Diagnostics.ProcessStartInfo]::new($node)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WorkingDirectory = $projectRoot
    $start.ArgumentList.Add('tests/desktop-e2e.mjs')
    $start.Environment['TASKMANAGER_RELEASE_DIRECTORY'] = $installDirectory
    $start.Environment['TASKMANAGER_DESKTOP_PORT'] = '5214'
    $start.Environment['TASKMANAGER_CDP_PORT'] = '9242'
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $child = [Diagnostics.Process]::new()
    $child.StartInfo = $start
    if (-not $child.Start()) { throw 'The installed real Electron acceptance process could not be started.' }
    Add-OwnedProcess $child
    $stdout = $child.StandardOutput.ReadToEndAsync()
    $stderr = $child.StandardError.ReadToEndAsync()
    try {
        $deadline = [DateTime]::UtcNow.AddMinutes(8)
        while (-not $child.WaitForExit(800)) {
            Update-OwnedDescendants
            if ([DateTime]::UtcNow -ge $deadline) { throw 'The installed Electron acceptance exceeded its time limit.' }
        }
        Assert-Condition ($child.ExitCode -eq 0) 'Existing real Electron desktop acceptance exits successfully against the installed directory'
        $resultPath = Join-Path $projectRoot 'output/playwright/final-desktop-results.json'
        if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf) -or (Get-Item -LiteralPath $resultPath).LastWriteTimeUtc -lt $started) { throw 'The installed Electron acceptance did not create a fresh result.' }
        $desktopResult = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
        Assert-Condition ($desktopResult.result -ceq 'PASS' -and $desktopResult.assertions -ge 42) 'At least 42 existing production Electron/UI/native assertions pass after installation'
        $report.desktopAssertions = [int]$desktopResult.assertions
        Assert-NoInstalledProcess 'after the desktop close flow'
    } finally {
        Copy-DesktopScreenshots $started
        if (-not $child.HasExited) {
            Update-OwnedDescendants
            foreach ($entry in @($ownedProcesses | Sort-Object -Property Depth -Descending)) { Stop-OwnedIdentity $entry }
        }
        $child.Dispose()
        [void]$stdout.GetAwaiter().GetResult()
        [void]$stderr.GetAwaiter().GetResult()
    }
}

function Save-AcceptanceReport {
    $report.completedAtUtc = [DateTime]::UtcNow.ToString('O')
    $json = $report | ConvertTo-Json -Depth 12
    [IO.File]::WriteAllText((Join-Path $output 'acceptance.json'), $json + "`n", [Text.UTF8Encoding]::new($false))
    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add('# Windows installer acceptance')
    $lines.Add('')
    $lines.Add("Result: **$($report.result)**")
    $lines.Add("Source commit: $($report.sourceCommit)")
    $lines.Add("Completed: $($report.completedAtUtc)")
    $lines.Add('')
    foreach ($entry in $report.checks) { $lines.Add("- PASS: $($entry.message)") }
    if ($report.error) { $lines.Add(''); $lines.Add("Failure: $($report.error)") }
    foreach ($issue in $report.cleanupErrors) { $lines.Add("Cleanup failure: $issue") }
    $lines.Add('')
    foreach ($limit in $report.limitations) { $lines.Add("- $limit") }
    [IO.File]::WriteAllText((Join-Path $output 'acceptance.md'), ($lines -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
    $delivery = Join-Path $output 'delivery'
    New-Item -ItemType Directory -Path $delivery -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $output 'acceptance.json') -Destination $delivery -Force
    Copy-Item -LiteralPath (Join-Path $output 'acceptance.md') -Destination $delivery -Force
}

New-Item -ItemType Directory -Path $output -Force | Out-Null
if (Test-Path -LiteralPath $testRoot) { throw 'The dedicated test directory must not already exist.' }
[void](Assert-ContainedPath $testRoot $runnerTemporary)
[void](Assert-ContainedPath $installDirectory $testRoot)
Assert-NoLinks $runnerTemporary
New-Item -ItemType Directory -Path $testRoot | Out-Null

try {
    if ($UninstallerName -notmatch '^[A-Za-z0-9_.-]+\.exe$' -or $UninstallRegistryKeyName -notmatch '^[A-Za-z0-9_{}.-]+$') { throw 'Invalid fixed uninstaller identity.' }
    if ([IO.Path]::GetFileName($DesktopShortcutName) -cne $DesktopShortcutName -or -not $DesktopShortcutName.EndsWith('.lnk', [StringComparison]::OrdinalIgnoreCase)) { throw 'Desktop shortcut must be a single .lnk filename.' }
    $installer = (Resolve-Path -LiteralPath $InstallerPath).Path
    $payload = (Resolve-Path -LiteralPath $PayloadDirectory).Path.TrimEnd('\', '/')
    [void](Assert-ContainedPath $installer (Join-Path $projectRoot 'output'))
    [void](Assert-ContainedPath $payload (Join-Path $projectRoot 'output'))
    Assert-NoLinks $installer
    Assert-NoLinks $payload
    [xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot 'src/TaskManager.Host/TaskManager.Host.csproj') -Raw
    $version = [string]$project.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw 'Invalid Host project version.' }
    $installerName = "BlueDomainTaskManager-v$version-win-x64-setup.exe"
    Assert-Condition ([IO.Path]::GetFileName($installer) -ceq $installerName) 'Installer filename uses the actual Host project version'
    $installerHash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    $report.installer = [ordered]@{ filename = $installerName; version = $version; sha256 = $installerHash; bytes = (Get-Item -LiteralPath $installer).Length }
    $delivery = Join-Path $output 'delivery'
    New-Item -ItemType Directory -Path $delivery -Force | Out-Null
    Copy-Item -LiteralPath $installer -Destination (Join-Path $delivery $installerName) -Force
    [IO.File]::WriteAllText((Join-Path $delivery 'SHA256SUMS'), "$installerHash  $installerName`n", [Text.UTF8Encoding]::new($false))
    Assert-Condition ((Get-FileHash -LiteralPath (Join-Path $delivery $installerName) -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $installerHash) 'Downloadable Setup copy matches the SHA256SUMS entry'

    $required = @(
        'TaskManager.Host.exe', 'TaskManager.Host.dll', 'TaskManager.Host.deps.json', 'TaskManager.Host.runtimeconfig.json',
        'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'System.Private.CoreLib.dll', 'Microsoft.AspNetCore.Server.Kestrel.Core.dll',
        'Newtonsoft.Json.dll', 'wwwroot/index.html', 'wwwroot/favicon.ico',
        '.electron/main.js', '.electron/custom_main.js', '.electron/task-manager.ico', '.electron/package-lock.json',
        '.electron/node_modules/electron/dist/electron.exe', '.electron/node_modules/electron/dist/LICENSE', '.electron/node_modules/electron/dist/LICENSES.chromium.html'
    )
    foreach ($relative in $required) {
        if (-not (Test-Path -LiteralPath (Join-Path $payload $relative) -PathType Leaf)) { throw "Self-contained payload is incomplete: $relative" }
    }
    $runtime = Get-Content -LiteralPath (Join-Path $payload 'TaskManager.Host.runtimeconfig.json') -Raw | ConvertFrom-Json
    $frameworks = @($runtime.runtimeOptions.includedFrameworks | ForEach-Object { $_.name })
    Assert-Condition ($frameworks -ccontains 'Microsoft.NETCore.App' -and $frameworks -ccontains 'Microsoft.AspNetCore.App') 'Self-contained runtimeconfig includes .NET and ASP.NET Core frameworks'
    $manifest = @(Get-PayloadManifest $payload)
    $report.payloadFiles = $manifest.Count
    $hostExecutable = Join-Path $installDirectory 'TaskManager.Host.exe'
    $uninstallerPath = Join-Path $installDirectory $UninstallerName
    $commonPrograms = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonPrograms)
    $commonDesktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonDesktopDirectory)
    $startMenuShortcut = Assert-ContainedPath (Join-Path $commonPrograms $StartMenuRelativePath) $commonPrograms
    $desktopShortcut = Assert-ContainedPath (Join-Path $commonDesktop $DesktopShortcutName) $commonDesktop
    Assert-Condition ($null -eq (Get-UninstallRegistration)) 'The target product uninstall registration did not exist before this disposable test'
    Assert-Condition (-not (Test-Path -LiteralPath $startMenuShortcut) -and -not (Test-Path -LiteralPath $desktopShortcut)) 'The target product shortcuts did not exist before this disposable test'
    $report.ifeoFingerprints['before'] = Get-IfeoFingerprint

    Invoke-SilentInstall
    Assert-IfeoUnchanged 'after installation'
    Assert-InstalledPayload $manifest 'after installation'
    Assert-Registration
    Invoke-HeadlessAcceptance 'installed'
    Invoke-DesktopAcceptance

    $notePath = Join-Path $installDirectory 'User-created acceptance note.txt'
    [IO.File]::WriteAllText($notePath, 'This file was created by the acceptance test, never by the installer.', [Text.UTF8Encoding]::new($false))
    $noteHash = (Get-FileHash -LiteralPath $notePath -Algorithm SHA256).Hash
    Invoke-SilentInstall
    Assert-IfeoUnchanged 'after same-path reinstall'
    Assert-InstalledPayload $manifest 'after same-path reinstall'
    Assert-Registration
    Assert-Condition ((Get-FileHash -LiteralPath $notePath -Algorithm SHA256).Hash -ceq $noteHash) 'Same-path reinstall preserves an unrelated user-created file'
    Invoke-HeadlessAcceptance 'reinstalled'
    Assert-NoInstalledProcess 'before uninstall'

    if (-not (Test-Path -LiteralPath $uninstallerPath -PathType Leaf)) { throw 'The fixed installed uninstaller is missing.' }
    Assert-NoLinks $uninstallerPath
    $uninstall = Start-Process -FilePath $uninstallerPath -ArgumentList '/S' -WorkingDirectory $testRoot -WindowStyle Hidden -Wait -PassThru
    try { Assert-Condition ($uninstall.ExitCode -eq 0) 'The fixed installed NSIS uninstaller exits successfully' }
    finally { $uninstall.Dispose() }
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    while ((Test-Path -LiteralPath $uninstallerPath) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 250 }
    foreach ($item in $manifest) {
        if (Test-Path -LiteralPath (Join-Path $installDirectory $item.RelativePath)) { throw "Uninstaller left a package-owned file: $($item.RelativePath)" }
    }
    Assert-Condition (-not (Test-Path -LiteralPath $uninstallerPath)) 'The NSIS uninstaller removes its own installed executable'
    Assert-Condition ($null -eq (Get-UninstallRegistration)) 'The product uninstall registration is removed'
    Assert-Condition (-not (Test-Path -LiteralPath $startMenuShortcut) -and -not (Test-Path -LiteralPath $desktopShortcut)) 'Only this product installation shortcuts are removed'
    Assert-Condition ((Test-Path -LiteralPath $notePath -PathType Leaf) -and (Get-FileHash -LiteralPath $notePath -Algorithm SHA256).Hash -ceq $noteHash) 'Uninstall preserves the exact unrelated user-created file instead of recursively deleting the installation directory'
    $remaining = @(Get-ChildItem -LiteralPath $installDirectory -Recurse -Force -File)
    Assert-Condition ($remaining.Count -eq 1 -and $remaining[0].FullName -ceq $notePath) 'Only the test-owned user note remains; no packaged or installer-owned files remain'
    Assert-IfeoUnchanged 'after uninstall'
    Assert-NoInstalledProcess 'after uninstall'
    $report.result = 'PASS'
} catch {
    $report.result = 'FAIL'
    $report.error = $_.Exception.Message
    $report.installationRetainedOnFailure = Test-Path -LiteralPath $installDirectory
    Write-Host "FAIL: $($report.error)"
} finally {
    foreach ($entry in @($ownedProcesses | Sort-Object -Property Depth -Descending)) {
        try { Stop-OwnedIdentity $entry }
        catch { $report.cleanupErrors.Add('An exact test-owned process could not be stopped.'); $report.result = 'FAIL' }
    }
    if ($report.ifeoFingerprints.Contains('before')) {
        try { Assert-IfeoUnchanged 'at final cleanup' }
        catch { $report.cleanupErrors.Add('Complete HKLM64 IFEO configuration differs or cannot be read at final cleanup.'); $report.result = 'FAIL' }
    }
    Save-AcceptanceReport
}

if ($report.result -cne 'PASS') { throw 'Installer acceptance failed; see output/installer-ci/acceptance.json and acceptance.md.' }
