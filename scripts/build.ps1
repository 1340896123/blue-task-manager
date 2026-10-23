param([switch]$SkipRestore)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
function Invoke-Checked([scriptblock]$Command) { & $Command; if ($LASTEXITCODE -ne 0) { throw "Build command failed with exit code $LASTEXITCODE" } }
if (-not $SkipRestore) {
    Invoke-Checked { npm ci }
    Invoke-Checked { npm --prefix src/TaskManager.Web ci }
    Invoke-Checked { dotnet restore TaskManager.sln --locked-mode }
}
Invoke-Checked { npm run typecheck }
Invoke-Checked { npm run build }
Invoke-Checked { dotnet build TaskManager.sln -c Release --no-restore -p:ElectronSkipExecCommands=true }
$release = Join-Path $root 'src/TaskManager.Host/bin/Release/net10.0-windows10.0.19041.0'
$electron = Join-Path $release '.electron'
Copy-Item -LiteralPath (Join-Path $root 'scripts/electron-runtime/package-lock.json') -Destination (Join-Path $electron 'package-lock.json') -Force
Push-Location $electron
try {
    # The framework's unused splash parser pins a vulnerable 1.x range. Keep its
    # public dependency on a patched version; no framework source is changed.
    Invoke-Checked { npm pkg set dependencies.image-size=2.0.4 }
    Invoke-Checked { npm ci --no-bin-links }
    # Electron >=42 downloads its binary in a separate explicit installation step.
    Invoke-Checked { node node_modules/electron/install.js }
} finally { Pop-Location }
$destination = Join-Path $root 'output/TaskManager-win-x64'
if (Test-Path -LiteralPath $destination) {
    $resolved = (Resolve-Path -LiteralPath $destination).Path
    $expected = [System.IO.Path]::GetFullPath((Join-Path $root 'output/TaskManager-win-x64'))
    if ($resolved -ne $expected -or -not $resolved.StartsWith([System.IO.Path]::GetFullPath((Join-Path $root 'output')) + [System.IO.Path]::DirectorySeparatorChar)) { throw 'Refusing cleanup outside expected release directory.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
# Copy dotfiles as well: .electron contains the official Electron.NET runtime.
Get-ChildItem -LiteralPath $release -Force | Where-Object { $_.Name -ne 'win-x64' } | Copy-Item -Destination $destination -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'start.cmd') -Destination $destination -Force
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $destination -Force
Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination $destination -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root 'design-system') -Destination $destination -Recurse -Force
Write-Host "Ready: $destination/start.cmd (Windows x64, Microsoft.NETCore.App 10 and Microsoft.AspNetCore.App 10 required)."

