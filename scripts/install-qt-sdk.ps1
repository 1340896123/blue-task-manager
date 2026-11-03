param([string]$Destination = (Join-Path (Split-Path -Parent $PSScriptRoot) 'output/Qt'))
$ErrorActionPreference = 'Stop'
$sevenZip = Join-Path $env:ProgramFiles '7-Zip/7z.exe'
if (-not (Test-Path -LiteralPath $sevenZip)) { $sevenZip = (Get-Command 7z.exe -ErrorAction Stop).Source }
$base = 'https://download.qt.io/online/qtsdkrepository/windows_x86/desktop/qt6_683/qt6_683/qt.qt6.683.win64_msvc2022_64'
$mirror = 'https://mirrors.ustc.edu.cn/qtproject/online/qtsdkrepository/windows_x86/desktop/qt6_683/qt6_683/qt.qt6.683.win64_msvc2022_64'
$archives = @{
    'qtbase' = '41688269fac0565db956c66d9eecae777d16197e0c02cd81b88640c1f5d73d3f'
    'qtsvg' = '48d1d798894cbf8696a0010fcfdeafea652b35159c56ea2060ee85ce3d0048f7'
}
$cache = Join-Path (Split-Path -Parent $PSScriptRoot) 'output/qt-archives'
New-Item -ItemType Directory -Path $cache,$Destination -Force | Out-Null
foreach ($module in $archives.Keys) {
    $file = "6.8.3-0-202503201308$module-Windows-Windows_11_23H2-MSVC2022-Windows-Windows_11_23H2-X86_64.7z"
    $path = Join-Path $cache $file
    if (-not (Test-Path $path) -or (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $archives[$module]) {
        try { Invoke-WebRequest "$base/$file" -OutFile $path -TimeoutSec 120 -UseBasicParsing }
        catch { Invoke-WebRequest "$mirror/$file" -OutFile $path -TimeoutSec 120 -UseBasicParsing }
    }
    if ((Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $archives[$module]) { throw "Qt archive checksum mismatch: $module" }
    & $sevenZip x -y "-o$Destination" $path | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Qt extraction failed: $module" }
}
Write-Host "Pinned Qt 6.8.3 MSVC x64 SDK: $Destination"
