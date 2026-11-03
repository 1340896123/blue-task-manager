param(
    [string]$ReleaseDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'output/Takao-win-x64'),
    [string]$ControlSkill = (Join-Path $env:USERPROFILE '.codex/skills/windows-computer-control'),
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'output/qt-acceptance/desktop')
)
$ErrorActionPreference = 'Stop'
$python = Join-Path $ControlSkill '.venv/Scripts/python.exe'
if (-not (Test-Path -LiteralPath $python)) { throw 'Install the windows-computer-control skill runtime first; WGC and FYLHelper are required.' }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try { $elevated = ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) } finally { $identity.Dispose() }
if (-not $elevated) {
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '" -ReleaseDirectory "' + $ReleaseDirectory + '" -ControlSkill "' + $ControlSkill + '" -OutputDirectory "' + $OutputDirectory + '"'
    $worker = Start-Process (Get-Process -Id $PID).Path -Verb RunAs -WindowStyle Hidden -ArgumentList $arguments -PassThru -Wait
    exit $worker.ExitCode
}
& $python (Join-Path (Split-Path -Parent $PSScriptRoot) 'tests/qt-desktop-e2e.py') --release $ReleaseDirectory --control-skill $ControlSkill --output $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw "Qt desktop acceptance failed: $LASTEXITCODE. See $OutputDirectory/results.json" }
