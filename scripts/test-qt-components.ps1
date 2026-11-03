param(
    [string]$ReleaseDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'output/Takao-win-x64'),
    [string]$ControlSkill = (Join-Path $env:USERPROFILE '.codex/skills/windows-computer-control'),
    [string]$QtPrefix = $env:QT_ROOT_DIR,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
if (-not $QtPrefix) { $QtPrefix = Join-Path $root 'output/Qt' }
if (-not $SkipBuild) {
    & "$PSScriptRoot/import-msvc.ps1"
    cmake -S src/Takao.Qt -B output/qt-tests-build -G Ninja "-DCMAKE_PREFIX_PATH=$QtPrefix" -DCMAKE_BUILD_TYPE=Release -DTAKAO_BUILD_INTEGRATION_TESTS=ON
    if ($LASTEXITCODE -ne 0) { throw 'Qt component test configuration failed.' }
    cmake --build output/qt-tests-build --target Takao.Qt.Integration --parallel 4
    if ($LASTEXITCODE -ne 0) { throw 'Qt component test compilation failed.' }
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try { $elevated = ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) } finally { $identity.Dispose() }
if (-not $elevated) {
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '" -ReleaseDirectory "' + $ReleaseDirectory + '" -ControlSkill "' + $ControlSkill + '" -SkipBuild'
    $worker = Start-Process (Get-Process -Id $PID).Path -Verb RunAs -WindowStyle Hidden -ArgumentList $arguments -PassThru -Wait
    exit $worker.ExitCode
}
$python = Join-Path $ControlSkill '.venv/Scripts/python.exe'
& $python (Join-Path $root 'tests/qt-component-capture.py') --release $ReleaseDirectory --control-skill $ControlSkill
if ($LASTEXITCODE -ne 0) { throw 'Qt component integration failed; see output/qt-acceptance/components.' }
