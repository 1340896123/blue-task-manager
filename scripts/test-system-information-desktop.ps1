param(
    [string]$ReleaseDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'output/system-information/release'),
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'output/system-information/desktop'),
    [string]$ControlSkill = (Join-Path $env:USERPROFILE '.codex/skills/windows-computer-control'),
    [switch]$Worker,
    [switch]$CopyOnly,
    [switch]$ButtonsOnly
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$taskIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
try { $taskElevated = ([Security.Principal.WindowsPrincipal]::new($taskIdentity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) }
finally { $taskIdentity.Dispose() }
if (-not $taskElevated) {
    if ($Worker) { throw 'The desktop test requires an actual elevated Windows token.' }
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '" -ReleaseDirectory "' + $ReleaseDirectory + '" -OutputDirectory "' + $OutputDirectory + '" -ControlSkill "' + $ControlSkill + '" -Worker'
    if ($CopyOnly) { $arguments += ' -CopyOnly' }
    if ($ButtonsOnly) { $arguments += ' -ButtonsOnly' }
    $taskWorker = Start-Process (Get-Process -Id $PID).Path -Verb RunAs -WindowStyle Hidden -ArgumentList $arguments -PassThru -Wait
    exit $taskWorker.ExitCode
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$taskCopyArguments = @()
if ($CopyOnly) { $taskCopyArguments += '--copy-only' }
if ($ButtonsOnly) { $taskCopyArguments += '--buttons-only' }
& (Join-Path $ControlSkill '.venv/Scripts/python.exe') (Join-Path $root 'tests/qt-system-information-desktop.py') --release $ReleaseDirectory --control-skill $ControlSkill --output $OutputDirectory @taskCopyArguments *> (Join-Path $OutputDirectory 'runner.log')
$taskExit = $LASTEXITCODE
$taskExit | Set-Content -LiteralPath (Join-Path $OutputDirectory 'runner.exit')
exit $taskExit
