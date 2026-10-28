param([string]$RunDirectory, [string]$MakeNsisPath = 'C:\nsis\makensis.exe')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$validationRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'output/update-installer-validation'))
if (-not $RunDirectory) { $RunDirectory = Join-Path $validationRoot ('harness-' + [Guid]::NewGuid().ToString('N')) }
$RunDirectory = [IO.Path]::GetFullPath($RunDirectory)
if ([IO.Path]::GetDirectoryName($RunDirectory) -cne $validationRoot -or [IO.Path]::GetFileName($RunDirectory) -cnotmatch '\Aharness-[0-9a-f]{32}\z') { throw 'A dedicated output/update-installer-validation/harness-<guid> directory is required.' }
$currentPath = $RunDirectory
while ($currentPath) {
    if ((Test-Path -LiteralPath $currentPath) -and ((Get-Item -LiteralPath $currentPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Harness paths cannot contain filesystem links.' }
    $currentPath = [IO.Path]::GetDirectoryName($currentPath)
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try {
    if (-not [Security.Principal.WindowsPrincipal]::new($identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this isolated mechanism test with an actual elevated Windows token.' }
} finally { $identity.Dispose() }
if (Test-Path -LiteralPath $RunDirectory) { throw 'The dedicated harness directory must not already exist.' }
New-Item -ItemType Directory -Path $RunDirectory -Force | Out-Null
$fixtureId = [IO.Path]::GetFileName($RunDirectory).Substring(8)
$productId = 'Takao.UpdateHarness.' + $fixtureId
$productKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\' + $productId
$installDirectory = Join-Path $RunDirectory 'Takao'
$payload = Join-Path $RunDirectory 'payload'
$generated = Join-Path $RunDirectory 'generated'
$source = Join-Path $RunDirectory 'source'
$installer = Join-Path $RunDirectory 'update-fixture.exe'
$launchLog = Join-Path $RunDirectory 'host-launches.txt'
$utf8 = [Text.UTF8Encoding]::new($false)
$checks = [Collections.Generic.List[string]]::new()
$hive = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
$report = [ordered]@{ result='RUNNING'; mechanismOnly=$true; productId=$productId; runDirectory=$RunDirectory; checks=$checks; failure=$null; cleanupError=$null; startedAtUtc=[DateTime]::UtcNow.ToString('O') }
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message }; $checks.Add($Message); Write-Host "PASS $Message" }
function Read-Registration {
    $key = $hive.OpenSubKey($productKey)
    if (-not $key) { return $null }
    try {
        return [pscustomobject]@{ directory=$key.GetValue('InstallLocation'); tag=$key.GetValue('ReleaseTag'); commit=$key.GetValue('SourceCommit'); attempt=$key.GetValue('LastUpdateAttemptId'); result=$key.GetValue('LastUpdateResult'); error=$key.GetValue('LastUpdateError') }
    } finally { $key.Dispose() }
}
function Fingerprint-Key([string]$KeyPath) {
    $key = $hive.OpenSubKey($KeyPath)
    try {
        function Read-Tree($Node) {
            if (-not $Node) { return [ordered]@{exists=$false} }
            $values=@(); foreach($name in @($Node.GetValueNames()|Sort-Object -CaseSensitive)) { $values += [ordered]@{name=$name;kind=$Node.GetValueKind($name).ToString();data=$Node.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)} }
            $children=@(); foreach($name in @($Node.GetSubKeyNames()|Sort-Object -CaseSensitive)) { $child=$Node.OpenSubKey($name); try{$children += [ordered]@{name=$name;state=(Read-Tree $child)}}finally{$child.Dispose()} }
            return [ordered]@{exists=$true;values=@($values);subkeys=@($children)}
        }
        $bytes=[Text.Encoding]::UTF8.GetBytes((Read-Tree $key | ConvertTo-Json -Depth 50 -Compress))
        return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
    } finally { if($key){$key.Dispose()} }
}
function Run-Installer([string]$Arguments) {
    $timer=[Diagnostics.Stopwatch]::StartNew()
    $process=Start-Process -FilePath $installer -ArgumentList $Arguments -WindowStyle Hidden -Wait -PassThru
    try { return [pscustomobject]@{code=$process.ExitCode;seconds=$timer.Elapsed.TotalSeconds} } finally { $process.Dispose() }
}
function Launch-Count { if(Test-Path -LiteralPath $launchLog) { return @(Get-Content -LiteralPath $launchLog).Count }; return 0 }
function Wait-Launch([int]$Previous) {
    $deadline=[DateTime]::UtcNow.AddSeconds(10)
    while((Launch-Count) -le $Previous -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 100}
    Check ((Launch-Count) -gt $Previous) 'Successful update starts the fixed installed fixture Host'
}
function Escape-Nsis([string]$Value) { return $Value.Replace('$','$$').Replace('"','$\"') }
$productionKey='Software\Microsoft\Windows\CurrentVersion\Uninstall\BlueDomainTaskManager'
$ifeoKey='SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\taskmgr.exe'
$productionBefore=Fingerprint-Key $productionKey
$ifeoBefore=Fingerprint-Key $ifeoKey
$markerBytes=$null
$heldProcess=$null
try {
    Check ($null -eq (Read-Registration)) 'The dedicated fixture product key does not exist'
    New-Item -ItemType Directory -Path $source,$generated,$payload -Force | Out-Null
    $project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><AssemblyName>Takao.Host</AssemblyName><UseAppHost>true</UseAppHost><ImplicitUsings>enable</ImplicitUsings><DebugType>none</DebugType><DebugSymbols>false</DebugSymbols></PropertyGroup></Project>'
    [IO.File]::WriteAllText((Join-Path $source 'Fixture.csproj'),$project,$utf8)
    $literalLog=$launchLog.Replace('"','""')
    $program=@'
using System.Diagnostics;
if (args.Contains("--uninstall-task-manager-integration")) return;
var current = Process.GetCurrentProcess();
File.AppendAllText(@"LOG_PATH", $"{Environment.ProcessId}|{current.StartTime.ToUniversalTime().ToFileTimeUtc()}|{string.Join(" ",args)}\n");
if (args.Length == 1 && args[0].StartsWith("--hold=") && int.TryParse(args[0][7..], out var delay) && delay is > 0 and <= 10000) await Task.Delay(delay);
'@.Replace('LOG_PATH',$literalLog)
    [IO.File]::WriteAllText((Join-Path $source 'Program.cs'),$program,$utf8)
    Push-Location ([IO.Path]::GetTempPath())
    try { & dotnet build (Join-Path $source 'Fixture.csproj') -c Release -o $payload -v quiet; if($LASTEXITCODE -ne 0){throw 'Fixture build failed.'} } finally { Pop-Location }
    Copy-Item -LiteralPath (Join-Path $payload 'Takao.Host.exe') -Destination (Join-Path $payload 'Takao.Launcher.exe')
    $electron=Join-Path $payload '.electron/node_modules/electron/dist'
    New-Item -ItemType Directory -Path $electron -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $payload 'Takao.Host.exe') -Destination (Join-Path $electron 'electron.exe')
    $entries=@(Get-ChildItem -LiteralPath $payload -Recurse -Force)
    $install=@();$delete=@();$paths=@();$verify=@()
    foreach($entry in $entries){$relative=$entry.FullName.Substring($payload.Length+1);$paths+='!insertmacro CheckPayloadPath "'+(Escape-Nsis $relative)+'"';if($entry.PSIsContainer){continue};$parent=Split-Path -Parent $relative;$install+='SetOutPath "$INSTDIR'+$(if($parent){'\'+(Escape-Nsis $parent)})+'"';$install+='File "'+(Escape-Nsis $entry.FullName)+'"';$delete+='Delete "$INSTDIR\'+(Escape-Nsis $relative)+'"';if(-not $parent){$verify+='Push "$INSTDIR\'+(Escape-Nsis $relative)+'"';$verify+='Push "'+(Get-FileHash -LiteralPath $entry.FullName -Algorithm SHA256).Hash+'"';$verify+='Call un.VerifyFile'}}
    $directories=@($entries|Where-Object PSIsContainer|Sort-Object {$_.FullName.Length} -Descending|ForEach-Object {'RMDir "$INSTDIR\'+(Escape-Nsis $_.FullName.Substring($payload.Length+1))+'"'})
    foreach($pair in @{ 'install-files.nsh'=$install;'delete-files.nsh'=$delete;'check-paths.nsh'=$paths;'verify-maintenance.nsh'=$verify;'remove-directories.nsh'=$directories }.GetEnumerator()){[IO.File]::WriteAllLines((Join-Path $generated $pair.Key),[string[]]$pair.Value,$utf8)}
    $sourceCommit=(& git -C $projectRoot rev-parse HEAD).Trim()
    & $MakeNsisPath /V2 /WX /INPUTCHARSET UTF8 /DTASKMANAGER_INSTALLER_TEST "/DTEST_PRODUCT_ID=$productId" "/DTEST_PRODUCT_KEY=$productKey" /DPRODUCT_VERSION=1.1.1 /DRELEASE_TAG=v1.1.1-build.0 "/DSOURCE_COMMIT=$sourceCommit" "/DPAYLOAD_DIR=$payload" "/DGENERATED_DIR=$generated" "/DINSTALLER_PATH=$installer" "/DPROJECT_ROOT=$projectRoot" /DPAYLOAD_SIZE_KB=1000 (Join-Path $PSScriptRoot 'installer/Takao.nsi')
    if($LASTEXITCODE -ne 0){throw 'Fixture NSIS compilation failed.'}
    Check $true 'The production NSIS script compiles with an isolated compile-time fixture identity'
    foreach($argument in @('/TASKMANAGERUPDATE=bad','/TASKMANAGERUPDATE','/TASKMANAGERUPDATE=ABCDEF0123456789abcdef0123456789ab')){
        Check ((Run-Installer ("/S "+$argument)).code -eq 2) 'Malformed update attempts fail before ordinary installation'
        Check ($null -eq (Read-Registration)) 'Malformed update attempts create no fixture installation record'
    }
    $attempt=[Guid]::NewGuid().ToString('N')
    Check ((Run-Installer "/S /TASKMANAGERUPDATE=$attempt").code -eq 2 -and $null -eq (Read-Registration)) 'A valid update request cannot create a new installation'
    Check ((Run-Installer "/S /D=$installDirectory").code -eq 0) 'Ordinary silent fixture installation succeeds'
    Check ((Launch-Count) -eq 0) 'Ordinary silent installation does not launch the Host'
    $registered=Read-Registration
    Check ($registered.directory -ceq $installDirectory -and $registered.tag -ceq 'v1.1.1-build.0' -and $registered.commit -ceq $sourceCommit) 'Installed identity records the exact ReleaseTag and SourceCommit'
    $marker=Join-Path $installDirectory '.bluedomain-install.ini';$markerBytes=[IO.File]::ReadAllBytes($marker)
    [IO.File]::WriteAllText($marker,"[Product]`r`nId=incorrect-fixture-marker`r`nDirectory=$installDirectory`r`n",$utf8)
    $attempt=[Guid]::NewGuid().ToString('N')
    Check ((Run-Installer "/S /TASKMANAGERUPDATE=$attempt").code -eq 2) 'An update rejects a damaged product marker'
    $registered=Read-Registration
    Check ($registered.attempt -ceq $attempt -and $registered.result -ceq 'failed' -and $registered.error) 'A valid product registration records an early marker failure'
    Check ((Launch-Count) -eq 0) 'A failed identity check does not launch the Host'
    [IO.File]::WriteAllBytes($marker,$markerBytes)
    $heldProcess=Start-Process -FilePath (Join-Path $installDirectory 'Takao.Host.exe') -ArgumentList '--hold=2200' -WindowStyle Hidden -PassThru
    $plain=Run-Installer '/S'
    Check ($plain.code -eq 2 -and -not $heldProcess.HasExited) 'Ordinary installation immediately rejects an actually running fixture Host'
    [void]$heldProcess.WaitForExit(10000);$heldProcess.Dispose();$heldProcess=$null
    $heldProcess=Start-Process -FilePath (Join-Path $installDirectory 'Takao.Host.exe') -ArgumentList '--hold=2200' -WindowStyle Hidden -PassThru
    Start-Sleep -Milliseconds 150
    $beforeRestart=Launch-Count;$attempt=[Guid]::NewGuid().ToString('N')
    $updated=Run-Installer "/S /TASKMANAGERUPDATE=$attempt"
    Check ($updated.code -eq 0 -and $updated.seconds -ge 1) 'Update mode waits for the old mapped Host to exit and then succeeds'
    [void]$heldProcess.WaitForExit(10000);$heldProcess.Dispose();$heldProcess=$null
    Wait-Launch $beforeRestart
    $registered=Read-Registration
    Check ($registered.attempt -ceq $attempt -and $registered.result -ceq 'success' -and -not $registered.error) 'A complete update records its matching successful attempt'
    $beforeRestart=Launch-Count;$attempt=[Guid]::NewGuid().ToString('N')
    $lock=[IO.File]::Open((Join-Path $installDirectory 'Takao.Host.exe'),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None)
    try{$timeout=Run-Installer "/S /TASKMANAGERUPDATE=$attempt"}finally{$lock.Dispose()}
    Check ($timeout.code -eq 2 -and $timeout.seconds -ge 59 -and $timeout.seconds -lt 70) 'Update image waiting ends with a bounded 60-second timeout'
    $registered=Read-Registration
    Check ($registered.attempt -ceq $attempt -and $registered.result -ceq 'failed' -and $registered.error -ceq 'imageLockTimeout') 'Timeout records a fixed failure result for the exact attempt'
    Check ((Launch-Count) -eq $beforeRestart) 'A timed-out update does not restart the Host'
    $report.result='PASS'
} catch { $report.result='FAIL';$report.failure=$_.Exception.Message }
finally {
    if($heldProcess){[void]$heldProcess.WaitForExit(10000);$heldProcess.Dispose()}
    try {
        if($markerBytes -and (Test-Path -LiteralPath (Join-Path $installDirectory '.bluedomain-install.ini'))){[IO.File]::WriteAllBytes((Join-Path $installDirectory '.bluedomain-install.ini'),$markerBytes)}
        if(Read-Registration){$uninstaller=Join-Path $installDirectory 'Uninstall.exe';if(-not (Test-Path -LiteralPath $uninstaller)){throw 'Fixture uninstaller missing.'};$process=Start-Process -FilePath $uninstaller -ArgumentList '/S' -WindowStyle Hidden -PassThru -Wait;$process.Dispose();$deadline=[DateTime]::UtcNow.AddSeconds(20);while((Read-Registration) -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 100};if(Read-Registration){throw 'Dedicated fixture registration remains after uninstall.'}}
        Check ((Fingerprint-Key $productionKey) -ceq $productionBefore) 'The real product uninstall key is unchanged'
        Check ((Fingerprint-Key $ifeoKey) -ceq $ifeoBefore) 'The complete real taskmgr IFEO key is unchanged'
    } catch { $report.cleanupError=$_.Exception.Message;$report.result='FAIL' }
    $hive.Dispose();$report.completedAtUtc=[DateTime]::UtcNow.ToString('O');$report.assertions=$checks.Count
    [IO.File]::WriteAllText((Join-Path $RunDirectory 'results.json'),($report|ConvertTo-Json -Depth 8),$utf8)
    Write-Host ("Result: "+$report.result+"; "+$checks.Count+" assertions; "+(Join-Path $RunDirectory 'results.json'))
}
if($report.result -ne 'PASS'){throw ($report.failure+' '+$report.cleanupError)}
