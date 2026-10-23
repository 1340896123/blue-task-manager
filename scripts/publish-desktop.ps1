param([switch]$SkipRestore)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
function Invoke-Checked([scriptblock]$Command) { & $Command; if ($LASTEXITCODE -ne 0) { throw "Publish command failed: $LASTEXITCODE" } }
# Keep the ordinary build directory intact for the existing desktop tests and
# prepare Electron.NET's official Host-first bridge before publishing .NET.
& (Join-Path $PSScriptRoot 'build.ps1') -SkipRestore:$SkipRestore
if ($LASTEXITCODE -ne 0) { throw "Desktop build failed: $LASTEXITCODE" }
$project = Join-Path $root 'src/TaskManager.Host/TaskManager.Host.csproj'
Invoke-Checked { dotnet restore $project -r win-x64 --locked-mode }
$publish = (Join-Path $root 'output/publish-win-x64') + [IO.Path]::DirectorySeparatorChar
# Electron.NET's publish target deliberately writes the .NET payload under bin
# and moves its bridge to app, even with ElectronSkipExecCommands. Reassemble
# from those real publish files, retaining the complete build bridge.
Invoke-Checked { dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -o $publish -p:UseAppHost=true -p:PublishTrimmed=false -p:PublishSingleFile=false -p:PublishAot=false -p:ElectronSkipExecCommands=true }
$runtime = Join-Path $publish 'bin'
if (-not (Test-Path -LiteralPath (Join-Path $runtime 'coreclr.dll'))) { throw 'Self-contained publish did not produce bin/coreclr.dll.' }
$destination = Join-Path $root 'output/TaskManager-win-x64'
# This directory was freshly recreated by build.ps1. Replace the managed output
# with publish output without deleting or relocating the Electron runtime.
Get-ChildItem -LiteralPath $runtime -Force | Where-Object { $_.Name -ne '.electron' } | Copy-Item -Destination $destination -Recurse -Force
$launcherProject = Join-Path $root 'src/TaskManager.Launcher/TaskManager.Launcher.csproj'
$launcherPublish = (Join-Path $root 'output/publish-launcher-win-x64') + [IO.Path]::DirectorySeparatorChar
Invoke-Checked { dotnet restore $launcherProject -r win-x64 --locked-mode }
Invoke-Checked { dotnet publish $launcherProject -c Release -r win-x64 --self-contained true --no-restore -o $launcherPublish -p:UseAppHost=true -p:PublishTrimmed=false -p:PublishSingleFile=false -p:PublishAot=false }
# Both executables use this exact pinned runtime; retain the Host's complete
# .NET/ASP.NET tree and add the Launcher's own self-contained application files.
foreach ($name in @('TaskManager.Launcher.exe','TaskManager.Launcher.dll','TaskManager.Launcher.deps.json','TaskManager.Launcher.runtimeconfig.json')) {
    Copy-Item -LiteralPath (Join-Path $launcherPublish $name) -Destination $destination -Force
}
$launcherConfig = Get-Content -LiteralPath (Join-Path $destination 'TaskManager.Launcher.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($launcherConfig.runtimeOptions.framework -or $launcherConfig.runtimeOptions.frameworks -or @($launcherConfig.runtimeOptions.includedFrameworks).Count -ne 1 -or $launcherConfig.runtimeOptions.includedFrameworks[0].version -ne '10.0.12') { throw 'Launcher must use the pinned self-contained runtime.' }
Get-ChildItem -LiteralPath $destination -File -Filter '*.pdb' | Remove-Item -Force
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$runtimeVersion = [string]$projectXml.Project.PropertyGroup.DesktopRuntimeVersion
$assets = Get-Content -LiteralPath (Join-Path $root 'src/TaskManager.Host/obj/project.assets.json') -Raw | ConvertFrom-Json
foreach ($runtimeName in @('Microsoft.NETCore.App','Microsoft.AspNetCore.App')) {
    $packageName = $runtimeName.ToLowerInvariant() + '.runtime.win-x64'
    $package = @($assets.packageFolders.PSObject.Properties.Name | ForEach-Object { Join-Path $_ "$packageName/$runtimeVersion" } | Where-Object { Test-Path -LiteralPath $_ -PathType Container }) | Select-Object -First 1
    if (-not $package) { throw "Cannot locate restored runtime licenses: $packageName/$runtimeVersion" }
    $licenseDirectory = Join-Path $destination "licenses/$runtimeName"
    New-Item -ItemType Directory -Path $licenseDirectory -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $package 'LICENSE.txt'),(Join-Path $package 'THIRD-PARTY-NOTICES.TXT') -Destination $licenseDirectory -Force
}
$config = Get-Content -LiteralPath (Join-Path $destination 'TaskManager.Host.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($config.runtimeOptions.framework -or $config.runtimeOptions.frameworks -or -not $config.runtimeOptions.includedFrameworks) { throw 'Publish is not self-contained.' }
Write-Host "Self-contained Windows x64 desktop ready: $destination"
