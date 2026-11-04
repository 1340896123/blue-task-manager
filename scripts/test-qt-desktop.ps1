param(
    [string]$ReleaseDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'output/Takao-win-x64'),
    [string]$ControlSkill = (Join-Path $env:USERPROFILE '.codex/skills/windows-computer-control'),
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'output/qt-acceptance/desktop'),
    [string]$WorkloadPath,
    [switch]$Extended,
    [switch]$NativeFileDialogs
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $WorkloadPath) {
    # Reuse the real CPU/memory/disk/socket fixture as a GUI-subsystem EXE so
    # creating it does not bring an unrelated console host over the tested UI.
    $fixture = Join-Path $root 'output/qt-ui-workload'
    dotnet build (Join-Path $root 'tests/Takao.TestWorkload/Takao.TestWorkload.csproj') -c Release --no-restore -p:OutputType=WinExe -o $fixture
    if ($LASTEXITCODE -ne 0) { throw 'GUI-subsystem workload fixture build failed.' }
    $WorkloadPath = Join-Path $fixture 'Takao.TestWorkload.exe'
}
$python = Join-Path $ControlSkill '.venv/Scripts/python.exe'
if (-not (Test-Path -LiteralPath $python)) { throw 'Install the windows-computer-control skill runtime first; WGC and FYLHelper are required.' }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try { $elevated = ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) } finally { $identity.Dispose() }
if (-not $elevated) {
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '" -ReleaseDirectory "' + $ReleaseDirectory + '" -ControlSkill "' + $ControlSkill + '" -OutputDirectory "' + $OutputDirectory + '" -WorkloadPath "' + $WorkloadPath + '"'
    if ($Extended) { $arguments += ' -Extended' }
    if ($NativeFileDialogs) { $arguments += ' -NativeFileDialogs' }
    $worker = Start-Process (Get-Process -Id $PID).Path -Verb RunAs -WindowStyle Hidden -ArgumentList $arguments -PassThru -Wait
    exit $worker.ExitCode
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$testArguments = @((Join-Path $root 'tests/qt-desktop-e2e.py'),'--release',$ReleaseDirectory,'--control-skill',$ControlSkill,'--output',$OutputDirectory,'--workload',$WorkloadPath)
if ($Extended) { $testArguments += '--extended' }
if ($NativeFileDialogs) { $testArguments += '--file-dialogs' }
& $python @testArguments *> (Join-Path $OutputDirectory 'runner.log')
if ($LASTEXITCODE -ne 0) { throw "Qt desktop acceptance failed: $LASTEXITCODE. See $OutputDirectory/results.json" }
