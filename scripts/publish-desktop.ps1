param([switch]$SkipRestore, [string]$QtPrefix = $env:QT_ROOT_DIR)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
function Invoke-Checked([scriptblock]$Command) { & $Command; if ($LASTEXITCODE -ne 0) { throw "Publish failed: $LASTEXITCODE" } }
& "$PSScriptRoot/build.ps1" -SkipRestore:$SkipRestore -QtPrefix $QtPrefix
if (-not $QtPrefix) { $QtPrefix = Join-Path $root 'output/Qt' }
$destination = Join-Path $root 'output/Takao-win-x64'
foreach ($name in @('Host','Launcher')) {
    $project = Join-Path $root "src/Takao.$name/Takao.$name.csproj"
    $publish = Join-Path $root "output/publish-$($name.ToLowerInvariant())-win-x64"
    Invoke-Checked { dotnet restore $project -r win-x64 --locked-mode }
    Invoke-Checked { dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -o $publish -p:UseAppHost=true -p:PublishTrimmed=false -p:PublishSingleFile=false -p:PublishAot=false }
    if ($name -eq 'Host') {
        Get-ChildItem -LiteralPath $publish -File | Where-Object { $_.Extension -ne '.pdb' -and $_.Name -notlike 'Electron*' } | Copy-Item -Destination $destination -Force
    } else {
        foreach ($file in @('Takao.Launcher.exe','Takao.Launcher.dll','Takao.Launcher.deps.json','Takao.Launcher.runtimeconfig.json')) { Copy-Item -LiteralPath (Join-Path $publish $file) -Destination $destination -Force }
    }
}
Copy-Item -LiteralPath (Join-Path $destination 'Takao.Host.exe') -Destination (Join-Path $destination 'TaskManager.Host.exe') -Force
Copy-Item -LiteralPath (Join-Path $destination 'Takao.Launcher.exe') -Destination (Join-Path $destination 'TaskManager.Launcher.exe') -Force
Invoke-Checked { & "$QtPrefix/bin/windeployqt.exe" --release --no-translations --no-system-d3d-compiler --no-opengl-sw --compiler-runtime (Join-Path $destination 'Takao.Qt.exe') }
# windeployqt supplies the CRT installer. Use Microsoft's app-local CRT layout
# so the portable directory can start without running an additional installer.
$redistRoot = Join-Path $env:VCINSTALLDIR 'Redist/MSVC'
$crt = Get-ChildItem -Path (Join-Path $redistRoot '*/x64/Microsoft.VC*.CRT') -Directory | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $crt) { throw 'MSVC x64 app-local CRT redistributables were not found.' }
Get-ChildItem -LiteralPath $crt.FullName -Filter '*.dll' -File | Copy-Item -Destination $destination -Force
if (-not (Test-Path "$destination/msvcp140.dll") -or -not (Test-Path "$destination/vcruntime140.dll")) { throw 'The Qt portable CRT is incomplete.' }
if (Test-Path -LiteralPath "$destination/vc_redist.x64.exe") { Remove-Item -LiteralPath "$destination/vc_redist.x64.exe" -Force }
$config = Get-Content -LiteralPath (Join-Path $destination 'Takao.Host.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($config.runtimeOptions.framework -or $config.runtimeOptions.frameworks -or @($config.runtimeOptions.includedFrameworks).Count -lt 2 -or -not (Test-Path "$destination/coreclr.dll")) { throw 'Publish must contain self-contained .NET and ASP.NET runtimes.' }
[xml]$projectXml = Get-Content (Join-Path $root 'src/Takao.Host/Takao.Host.csproj') -Raw
$runtimeVersion = [string]$projectXml.Project.PropertyGroup.DesktopRuntimeVersion
$assets = Get-Content (Join-Path $root 'src/Takao.Host/obj/project.assets.json') -Raw | ConvertFrom-Json
foreach ($runtimeName in @('Microsoft.NETCore.App','Microsoft.AspNetCore.App')) {
    $packageName = $runtimeName.ToLowerInvariant() + '.runtime.win-x64'
    $package = @($assets.packageFolders.PSObject.Properties.Name | ForEach-Object { Join-Path $_ "$packageName/$runtimeVersion" } | Where-Object { Test-Path -LiteralPath $_ }) | Select-Object -First 1
    if (-not $package) { throw "Runtime license source missing: $runtimeName" }
    $licenses = Join-Path $destination "licenses/$runtimeName"
    New-Item -ItemType Directory -Path $licenses -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $package 'LICENSE.txt'),(Join-Path $package 'THIRD-PARTY-NOTICES.TXT') -Destination $licenses -Force
}
if (Test-Path -LiteralPath "$destination/.electron") { throw 'Unexpected obsolete renderer payload.' }
Write-Host "Self-contained Qt + .NET Windows x64 ready: $destination"
