param(
    [string]$ReleaseDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'output/Takao-win-x64'),
    [string]$ControlSkill = (Join-Path $env:USERPROFILE '.codex/skills/windows-computer-control'),
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'output/qt-page-capture'),
    [string]$QtPrefix = $env:QT_ROOT_DIR,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$ReleaseDirectory = [IO.Path]::GetFullPath($ReleaseDirectory)
$ControlSkill = [IO.Path]::GetFullPath($ControlSkill)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $QtPrefix) { $QtPrefix = Join-Path $root 'output/Qt' }
if (-not $SkipBuild) {
    & "$PSScriptRoot/import-msvc.ps1"
    cmake -S src/Takao.Qt -B output/qt-tests-build -G Ninja "-DCMAKE_PREFIX_PATH=$QtPrefix" -DCMAKE_BUILD_TYPE=Release -DTAKAO_BUILD_INTEGRATION_TESTS=ON
    if ($LASTEXITCODE -ne 0) { throw 'Page-capture configuration failed.' }
    cmake --build output/qt-tests-build --target Takao.Qt.PageCapture --parallel 4
    if ($LASTEXITCODE -ne 0) { throw 'Page-capture compilation failed.' }
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try { $elevated = ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) } finally { $identity.Dispose() }
if (-not $elevated) {
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '" -ReleaseDirectory "' + $ReleaseDirectory + '" -ControlSkill "' + $ControlSkill + '" -OutputDirectory "' + $OutputDirectory + '" -SkipBuild'
    $worker = Start-Process (Get-Process -Id $PID).Path -Verb RunAs -WindowStyle Hidden -ArgumentList $arguments -PassThru -Wait
    exit $worker.ExitCode
}
$python = Join-Path $ControlSkill '.venv/Scripts/python.exe'
if (-not (Test-Path -LiteralPath $python)) { throw 'The WGC capture skill runtime is required; no input hardware is used.' }
& $python (Join-Path $root 'tests/qt-page-capture.py') --release $ReleaseDirectory --executable (Join-Path $root 'output/qt-tests-build/Takao.Qt.PageCapture.exe') --control-skill $ControlSkill --output $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw "Fast Qt page capture failed. See $OutputDirectory/results.json" }
