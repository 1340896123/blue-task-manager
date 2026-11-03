param([switch]$SkipRestore, [string]$QtPrefix = $env:QT_ROOT_DIR)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
function Invoke-Checked([scriptblock]$Command) { & $Command; if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" } }
if (-not $QtPrefix) { $QtPrefix = Join-Path $root 'output/Qt' }
if (-not (Test-Path (Join-Path $QtPrefix 'bin/qmake.exe'))) { & "$PSScriptRoot/install-qt-sdk.ps1" -Destination $QtPrefix }
$QtPrefix = (Resolve-Path -LiteralPath $QtPrefix).Path
if ((& "$QtPrefix/bin/qmake.exe" -query QT_VERSION).Trim() -ne '6.8.3') { throw 'Qt 6.8.3 MSVC x64 is required.' }
& "$PSScriptRoot/import-msvc.ps1"
if (-not $SkipRestore) { Invoke-Checked { dotnet restore Takao.sln --locked-mode } }
Invoke-Checked { dotnet build Takao.sln -c Release --no-restore }
$qtBuild = Join-Path $root 'output/qt-build'
Invoke-Checked { cmake -S src/Takao.Qt -B $qtBuild -G Ninja "-DCMAKE_PREFIX_PATH=$QtPrefix" -DCMAKE_BUILD_TYPE=Release }
Invoke-Checked { cmake --build $qtBuild --parallel 4 }
$destination = Join-Path $root 'output/Takao-win-x64'
if (Test-Path -LiteralPath $destination) {
    $resolved = (Resolve-Path -LiteralPath $destination).Path
    if ($resolved -ne [IO.Path]::GetFullPath((Join-Path $root 'output/Takao-win-x64')) -or (Get-Item -LiteralPath $resolved).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing cleanup outside the dedicated output directory.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$release = Join-Path $root 'src/Takao.Host/bin/Release/net10.0-windows10.0.19041.0'
# Copy the current dependency graph, not files left by a previous build.
$managedFiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($application in @('Host','Launcher')) {
    foreach ($extension in @('exe','dll','deps.json','runtimeconfig.json')) { [void]$managedFiles.Add("Takao.$application.$extension") }
    $dependencies = Get-Content (Join-Path $release "Takao.$application.deps.json") -Raw | ConvertFrom-Json
    foreach ($target in $dependencies.targets.PSObject.Properties.Value) {
        foreach ($library in $target.PSObject.Properties.Value) {
            foreach ($asset in $library.runtime.PSObject.Properties.Name) { [void]$managedFiles.Add([IO.Path]::GetFileName($asset)) }
            foreach ($asset in $library.native.PSObject.Properties.Name) { [void]$managedFiles.Add([IO.Path]::GetFileName($asset)) }
        }
    }
}
foreach ($file in $managedFiles) { if (Test-Path -LiteralPath (Join-Path $release $file)) { Copy-Item -LiteralPath (Join-Path $release $file) -Destination $destination -Force } }
foreach ($application in @('Host','Launcher')) { Copy-Item -LiteralPath (Join-Path $destination "Takao.$application.exe") -Destination (Join-Path $destination "TaskManager.$application.exe") -Force }
Copy-Item -LiteralPath (Join-Path $qtBuild 'Takao.Qt.exe') -Destination $destination
Invoke-Checked { & "$QtPrefix/bin/windeployqt.exe" --release --no-translations --no-system-d3d-compiler --no-opengl-sw --no-compiler-runtime (Join-Path $destination 'Takao.Qt.exe') }
Copy-Item -LiteralPath "$PSScriptRoot/start.cmd","$PSScriptRoot/restore-windows-task-manager.cmd",(Join-Path $root 'README.md') -Destination $destination
Copy-Item -LiteralPath (Join-Path $root 'docs'),(Join-Path $root 'design-system') -Destination $destination -Recurse
Copy-Item -LiteralPath (Join-Path $root 'assets/branding/takao.ico') -Destination $destination
& "$PSScriptRoot/copy-qt-licenses.ps1" -QtPrefix $QtPrefix -Destination $destination
Write-Host "Qt + .NET desktop ready: $destination/start.cmd"
