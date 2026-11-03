$ErrorActionPreference = 'Stop'
if (-not $env:VCINSTALLDIR) {
    $root = Split-Path -Parent $PSScriptRoot
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $vs = & $vswhere -latest -prerelease -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $vs) { throw 'Visual Studio C++ x64 build tools are required.' }
    $vcvars = Join-Path $vs 'VC/Auxiliary/Build/vcvars64.bat'
    $environmentScript = Join-Path $root 'output/initialize-msvc.cmd'
    New-Item -ItemType Directory -Path (Split-Path -Parent $environmentScript) -Force | Out-Null
    @('@echo off', ('call "' + $vcvars + '" >nul'), 'if errorlevel 1 exit /b 1', 'set') | Set-Content -LiteralPath $environmentScript -Encoding ascii
    & $env:ComSpec /d /c $environmentScript | ForEach-Object {
        if ($_ -match '^([^=]+)=(.*)$' -and $Matches[1] -notin @('HOME','CODEX_HOME')) { [Environment]::SetEnvironmentVariable($Matches[1], $Matches[2], 'Process') }
    }
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize MSVC x64.' }
}
