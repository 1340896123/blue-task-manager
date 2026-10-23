param(
    [switch]$ReadOnly,
    [string]$SessionDirectory,
    [string]$ReleaseDirectory,
    [string]$ProbePath,
    [int]$ControllerPid
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$keyPath = 'SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\taskmgr.exe'
$base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
function Read-Key($Key) {
    if (-not $Key) { return [ordered]@{ exists=$false; values=@(); subkeys=@() } }
    $values = @()
    foreach ($name in @($Key.GetValueNames() | Sort-Object -CaseSensitive)) {
        $kind = $Key.GetValueKind($name).ToString()
        $data = $Key.GetValue($name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        if ($kind -eq 'Binary' -or $kind -eq 'None') { $data = [Convert]::ToBase64String($data) }
        elseif ($kind -eq 'DWord' -or $kind -eq 'QWord') { $data = $data.ToString([Globalization.CultureInfo]::InvariantCulture) }
        $values += [ordered]@{ name=$name; kind=$kind; data=$data }
    }
    $children = @()
    foreach ($name in @($Key.GetSubKeyNames() | Sort-Object -CaseSensitive)) {
        $child = $Key.OpenSubKey($name)
        try { $children += [ordered]@{ name=$name; state=(Read-Key $child) } } finally { $child.Dispose() }
    }
    return [ordered]@{ exists=$true; values=@($values); subkeys=@($children) }
}
function Snapshot {
    $key = $base.OpenSubKey($keyPath)
    try { return (Read-Key $key | ConvertTo-Json -Depth 50 -Compress) } finally { if ($key) { $key.Dispose() } }
}
if ($ReadOnly) { try { Snapshot } finally { $base.Dispose() }; exit 0 }
if (-not (& $ProbePath process-elevation $PID | ConvertFrom-Json).elevated) { throw 'Worker requires actual elevated Windows token.' }
$SessionDirectory = [IO.Path]::GetFullPath($SessionDirectory)
$ReleaseDirectory = [IO.Path]::GetFullPath($ReleaseDirectory)
if (-not ([IO.Path]::GetFileName($SessionDirectory) -match '^taskmanager-ifeo-[a-zA-Z0-9]+$')) { throw 'Invalid dedicated session directory.' }
$hostPath = Join-Path $ReleaseDirectory 'TaskManager.Host.exe'
$launcherPath = Join-Path $ReleaseDirectory 'TaskManager.Launcher.exe'
if (-not (Test-Path -LiteralPath $hostPath) -or -not (Test-Path -LiteralPath $launcherPath)) { throw 'Complete portable release required.' }
$startedAt = [DateTime]::UtcNow
$controller = Get-Process -Id $ControllerPid
$controllerTime = $controller.StartTime.ToFileTimeUtc()
$initial = Snapshot
$initialObject = $initial | ConvertFrom-Json
if ($initialObject.values.Count -ne 0 -or $initialObject.subkeys.Count -ne 0) { throw 'Third-party/nonempty IFEO configuration exists; no changes permitted.' }
[IO.File]::WriteAllText((Join-Path $SessionDirectory 'registry-before.json'), $initial, [Text.UTF8Encoding]::new($false))
$roots = [Collections.Generic.List[object]]::new()
$known = [Collections.Generic.Dictionary[int,object]]::new()
$allowedStates = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
[void]$allowedStates.Add($initial)
$preferencesPath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'BlueDomainTaskManager/preferences.json'
$preferencesCaptured = $false
$preferencesBefore = $null
$entryBoundary = $null
function Remember-State([string]$State) {
    [void]$allowedStates.Add($State)
    $parsed = $State | ConvertFrom-Json
    $withoutDebugger = [ordered]@{ exists=$parsed.exists; values=@($parsed.values | Where-Object { $_.name -cne 'Debugger' }); subkeys=@($parsed.subkeys) }
    [void]$allowedStates.Add(($withoutDebugger | ConvertTo-Json -Depth 50 -Compress))
}
function Assert-OwnedState {
    $state = Snapshot
    if ($allowedStates.Contains($state)) { return }
    $parsed = $state | ConvertFrom-Json
    $commands = @(('"' + $hostPath + '" --task-manager-entry'), ('"' + $launcherPath + '" --task-manager-entry'))
    if ($parsed.exists -and $parsed.subkeys.Count -eq 0 -and $parsed.values.Count -eq 1 -and $parsed.values[0].name -ceq 'Debugger' -and $parsed.values[0].kind -ceq 'String' -and $commands -ccontains $parsed.values[0].data) { return }
    throw 'Unknown concurrent IFEO values/subkeys detected: preserving configuration instead of clearing it.'
}
function Identity($Process) { return [ordered]@{ pid=[int]$Process.ProcessId; fileTime=$Process.CreationDate.ToFileTimeUtc().ToString(); imagePath=$Process.ExecutablePath; commandLine=$Process.CommandLine } }
function Matches($Identity) {
    $process = Get-CimInstance Win32_Process -Filter ('ProcessId = ' + $Identity.pid)
    return ($process -and $process.CreationDate.ToFileTimeUtc().ToString() -eq $Identity.fileTime)
}
function Track-Host([int]$TargetPid) {
    $process = Get-CimInstance Win32_Process -Filter ('ProcessId = ' + $TargetPid)
    if (-not $process -or $process.ExecutablePath -ine $hostPath -or $process.CreationDate.ToUniversalTime() -lt $startedAt) { throw 'Refusing to adopt a process outside this test release/time boundary.' }
    $identity = Identity $process
    $roots.Add($identity)
    return $identity
}
function Capture-Trees {
    $all = @(Get-CimInstance Win32_Process)
    $ids = [Collections.Generic.HashSet[int]]::new()
    foreach ($root in $roots) { if (Matches $root) { [void]$ids.Add($root.pid) } }
    do {
        $changed = $false
        foreach ($process in $all) {
            if ($ids.Contains([int]$process.ParentProcessId) -and $ids.Add([int]$process.ProcessId)) { $changed = $true }
        }
    } while ($changed)
    foreach ($process in $all) { if ($ids.Contains([int]$process.ProcessId)) { $known[[int]$process.ProcessId] = Identity $process } }
    return @($known.Values | Sort-Object pid)
}
function Clear-TestKey {
    Assert-OwnedState
    $key = $base.OpenSubKey($keyPath, $true)
    if (-not $key) { return }
    try {
        foreach ($name in $key.GetValueNames()) { $key.DeleteValue($name) }
        foreach ($name in $key.GetSubKeyNames()) { $key.DeleteSubKeyTree($name) }
    } finally { $key.Dispose() }
}
function Restore-Initial {
    Clear-TestKey
    if (-not $initialObject.exists) { $base.DeleteSubKey($keyPath, $false) }
    if ((Snapshot) -cne $initial) { throw 'Independent full-key restoration comparison failed.' }
}
function Reply([string]$Path, $Data) {
    $temporary = $Path + '.tmp'
    [IO.File]::WriteAllText($temporary, ($Data | ConvertTo-Json -Depth 50 -Compress), [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporary -Destination $Path -Force
}
function Invoke-Probe([string[]]$Arguments) {
    $text = @($Arguments | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' }) -join ' '
    $start = [Diagnostics.ProcessStartInfo]::new($ProbePath, $text)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    try {
        if (-not $process.WaitForExit(20000)) { $process.Kill(); throw 'Test-owned native probe timed out.' }
        if ($process.ExitCode -ne 0) { throw ('Independent native probe failed: ' + $stderr.Result) }
        return ($stdout.Result | ConvertFrom-Json)
    } finally { $process.Dispose() }
}
$failure = $null
try {
    Reply (Join-Path $SessionDirectory 'worker-ready.json') @{ pid=$PID; elevated=$true; initial=$initial }
    $deadline = [DateTime]::UtcNow.AddMinutes(12)
    while ([DateTime]::UtcNow -lt $deadline) {
        $parent = Get-Process -Id $ControllerPid -ErrorAction SilentlyContinue
        if (-not $parent -or $parent.StartTime.ToFileTimeUtc() -ne $controllerTime) { break }
        $requests = @(Get-ChildItem -LiteralPath $SessionDirectory -Filter 'request-*.json' -File | Sort-Object Name)
        foreach ($file in $requests) {
            $response = Join-Path $SessionDirectory ($file.BaseName.Replace('request-', 'response-') + '.json')
            if (Test-Path -LiteralPath $response) { continue }
            try {
                $request = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
                if ($request.expectedState -and (Snapshot) -cne $request.expectedState) { throw 'IFEO changed outside the expected test transaction; no mutation made.' }
                $data = $null
                switch ($request.operation) {
                    'fixture' {
                        Clear-TestKey
                        $key = $base.CreateSubKey($keyPath)
                        try {
                            if ($null -ne $request.debugger) {
                                $kind = [Microsoft.Win32.RegistryValueKind]([Enum]::Parse([Microsoft.Win32.RegistryValueKind], $request.kind))
                                $value = if ($request.kind -eq 'DWord') { [int]$request.debugger } else { [string]$request.debugger }
                                $key.SetValue('Debugger', $value, $kind)
                            }
                            if ($request.special) {
                                $key.SetValue('', 'dedicated IFEO test', [Microsoft.Win32.RegistryValueKind]::String)
                                $key.SetValue('IfeoE2EBinary', [byte[]]@(0,1,127,255), [Microsoft.Win32.RegistryValueKind]::Binary)
                                $key.SetValue('IfeoE2EMulti', [string[]]@('one','two'), [Microsoft.Win32.RegistryValueKind]::MultiString)
                                $child = $key.CreateSubKey('IfeoE2ESubkey')
                                try { $child.SetValue('Sentinel', [long]1234567890123, [Microsoft.Win32.RegistryValueKind]::QWord) } finally { $child.Dispose() }
                            }
                            if ($request.useFilter) { $key.SetValue('UseFilter', 1, [Microsoft.Win32.RegistryValueKind]::DWord) }
                        } finally { $key.Dispose() }
                        Remember-State (Snapshot)
                    }
                    'start-host' {
                        $start = [Diagnostics.ProcessStartInfo]::new($hostPath, '--port=' + [int]$request.port)
                        $start.UseShellExecute = $false
                        $start.CreateNoWindow = $true
                        $start.EnvironmentVariables['TASKMANAGER_CDP_PORT'] = [string]$request.cdp
                        $start.EnvironmentVariables['TASKMANAGER_DATA_DIR'] = Join-Path $SessionDirectory 'settings-preferences'
                        $process = [Diagnostics.Process]::Start($start)
                        $data = Track-Host $process.Id
                        $process.Dispose()
                    }
                    'adopt-host' { $data = Track-Host ([int]$request.pid) }
                    'adopt-entry' {
                        $entryDeadline = [DateTime]::UtcNow.AddSeconds(10)
                        do {
                            $candidate = @(Get-CimInstance Win32_Process -Filter ("ParentProcessId = " + [int]$request.launcherPid) | Where-Object { $_.ExecutablePath -ieq $hostPath -and $_.CreationDate.ToUniversalTime() -ge $startedAt })
                            if ($candidate.Count -eq 1) { break }
                            Start-Sleep -Milliseconds 100
                        } while ([DateTime]::UtcNow -lt $entryDeadline)
                        if ($candidate.Count -ne 1) { throw 'Unable to identify the exact test Launcher child Host.' }
                        $data = Track-Host $candidate[0].ProcessId
                    }
                    'tree' { $data = @(Capture-Trees) }
                    'elevation' { $data = (& $ProbePath process-elevation ([string]$request.pid) | ConvertFrom-Json) }
                    'native-ui' {
                        if (-not $known.ContainsKey([int]$request.pid) -or -not (Matches $known[[int]$request.pid])) { throw 'Native UI target is not an owned identity.' }
                        $arguments = @('native-ui', [string]$request.hwnd, [string]$request.pid, [string]$request.fileTime, [string]$request.command)
                        if ($request.argument) { $arguments += [string]$request.argument }
                        $data = Invoke-Probe $arguments
                    }
                    'prepare-real-entry' {
                        if ([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners().Port -contains 5188) { throw 'Default entry port is occupied; do not interfere with an existing application.' }
                        $preferencesBefore = if (Test-Path -LiteralPath $preferencesPath) { [IO.File]::ReadAllBytes($preferencesPath) } else { $null }
                        $preferencesCaptured = $true
                        $entryBoundary = [DateTime]::UtcNow
                    }
                    'maintenance' {
                        $start = [Diagnostics.ProcessStartInfo]::new($hostPath, '--uninstall-task-manager-integration')
                        $start.UseShellExecute = $false
                        $start.CreateNoWindow = $true
                        $start.RedirectStandardOutput = $true
                        $start.RedirectStandardError = $true
                        $process = [Diagnostics.Process]::Start($start)
                        if (-not $process.WaitForExit(15000)) { throw 'Maintenance process timeout.' }
                        $data = @{ exitCode=$process.ExitCode; stdout=$process.StandardOutput.ReadToEnd(); stderr=$process.StandardError.ReadToEnd() }
                        $process.Dispose()
                    }
                    'sync' { }
                    'finish' { $deadline = [DateTime]::MinValue }
                    default { throw 'Unknown test worker operation.' }
                }
                Reply $response @{ ok=$true; data=$data; state=(Snapshot) }
            } catch { Reply $response @{ ok=$false; message=$_.Exception.Message; state=(Snapshot) } }
        }
        Start-Sleep -Milliseconds 100
    }
} catch { $failure = $_.Exception.Message }
finally {
    try {
        if ($entryBoundary) {
            foreach ($candidate in @(Get-CimInstance Win32_Process -Filter "Name='TaskManager.Host.exe'" | Where-Object { $_.ExecutablePath -ieq $hostPath -and $_.CreationDate.ToUniversalTime() -ge $entryBoundary -and $_.CommandLine -match '\s--task-manager-entry\s*$' })) {
                if (-not ($roots | Where-Object { $_.pid -eq [int]$candidate.ProcessId -and $_.fileTime -eq $candidate.CreationDate.ToFileTimeUtc().ToString() })) { [void](Track-Host $candidate.ProcessId) }
            }
        }
        $tree = @(Capture-Trees)
        foreach ($entry in @($tree | Sort-Object pid -Descending)) { if (Matches $entry) { Stop-Process -Id $entry.pid -Force -ErrorAction Stop } }
        $exitDeadline = [DateTime]::UtcNow.AddSeconds(10)
        do {
            $remaining = @($known.Values | Where-Object { Matches $_ })
            if ($remaining.Count -eq 0) { break }
            Start-Sleep -Milliseconds 100
        } while ([DateTime]::UtcNow -lt $exitDeadline)
        if ($remaining.Count -ne 0) { throw 'Test-owned process identities did not exit.' }
        Restore-Initial
        $preferencesRestored = $null
        if ($preferencesCaptured) {
            if ($null -ne $preferencesBefore) { [IO.File]::WriteAllBytes($preferencesPath, $preferencesBefore) }
            elseif (Test-Path -LiteralPath $preferencesPath) { Remove-Item -LiteralPath $preferencesPath }
            $preferencesRestored = if ($null -eq $preferencesBefore) { -not (Test-Path -LiteralPath $preferencesPath) } else { [Convert]::ToBase64String([IO.File]::ReadAllBytes($preferencesPath)) -ceq [Convert]::ToBase64String($preferencesBefore) }
            if (-not $preferencesRestored) { throw 'Default preferences byte restoration failed.' }
        }
        Reply (Join-Path $SessionDirectory 'worker-finished.json') @{ ok=($null -eq $failure); error=$failure; state=(Snapshot); preferencesRestored=$preferencesRestored; allTrackedExited=$true; tracked=@($known.Values) }
    } catch { Reply (Join-Path $SessionDirectory 'worker-finished.json') @{ ok=$false; error=$_.Exception.Message; state=(Snapshot) } }
    $base.Dispose()
}
