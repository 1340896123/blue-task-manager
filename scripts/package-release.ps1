param(
    [string]$Version,
    [string]$PayloadDirectory,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
[xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot 'src/TaskManager.Host/TaskManager.Host.csproj') -Raw
if (-not $Version) { $Version = [string]$project.Project.PropertyGroup.Version }
if ($Version -ne [string]$project.Project.PropertyGroup.Version) { throw 'Release version must match the project.' }
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw 'Invalid release version.' }
if (-not $PayloadDirectory) { $PayloadDirectory = Join-Path $projectRoot 'output/TaskManager-win-x64' }
$source = [IO.Path]::GetFullPath($PayloadDirectory).TrimEnd('\', '/')
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'output/releases' }
$destination = [IO.Path]::GetFullPath($OutputDirectory)
if ($destination -eq $source -or $destination.StartsWith($source + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Archive output must be outside the portable directory.' }
if (-not (Test-Path -LiteralPath $source -PathType Container)) { throw 'Build the portable directory first.' }
if ([Reflection.AssemblyName]::GetAssemblyName((Join-Path $source 'TaskManager.Host.dll')).Version.ToString() -ne "$Version.0") { throw 'Actual Host payload version differs from release version.' }
$entries = @(Get-ChildItem -LiteralPath $source -Recurse -Force)
if ($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Portable tree contains a filesystem link.' }
if ($entries | Where-Object { $_.Name -eq '.git' -or $_.Name -eq 'preferences.json' -or $_.Name -match '^\.env(?:\.|$)' }) { throw 'Portable tree contains local repository/configuration state.' }
$required = @(
    'TaskManager.Host.exe', 'TaskManager.Host.dll', 'TaskManager.Host.runtimeconfig.json',
    'TaskManager.Launcher.exe', 'TaskManager.Launcher.dll', 'TaskManager.Launcher.deps.json', 'TaskManager.Launcher.runtimeconfig.json',
    'Newtonsoft.Json.dll', 'start.cmd', 'restore-windows-task-manager.cmd', 'README.md', 'docs/ACCEPTANCE-RESULTS.md',
    'docs/TASKBAR-INTEGRATION.md', 'design-system/task-manager/MASTER.md',
    'wwwroot/index.html', 'wwwroot/icon.svg', 'wwwroot/favicon.ico',
    '.electron/main.js', '.electron/custom_main.js', '.electron/task-manager.ico',
    '.electron/node_modules/electron/dist/electron.exe',
    '.electron/node_modules/electron/dist/LICENSE',
    '.electron/node_modules/electron/dist/LICENSES.chromium.html'
)
foreach ($relative in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $relative) -PathType Leaf)) { throw "Missing portable file: $relative" }
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$archivePath = Join-Path $destination "BlueDomainTaskManager-v$Version-win-x64.zip"
if (Test-Path -LiteralPath $archivePath) { throw "Archive already exists: $archivePath" }
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$writer = [IO.Compression.ZipFile]::Open($archivePath, [IO.Compression.ZipArchiveMode]::Create)
try {
    # Older Windows PowerShell/.NET Framework writes backslashes through
    # CreateFromDirectory. Explicit portable entry names follow the ZIP standard.
    foreach ($item in $entries) {
        $name = 'TaskManager-win-x64/' + $item.FullName.Substring($source.Length + 1).Replace('\', '/')
        if ($item.PSIsContainer) { [void]$writer.CreateEntry($name + '/') }
        else { [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($writer, $item.FullName, $name, [IO.Compression.CompressionLevel]::Optimal) }
    }
} finally { $writer.Dispose() }
$archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $files = @($entries | Where-Object { -not $_.PSIsContainer })
    $zipFiles = @($archive.Entries | Where-Object { $_.Name })
    if ($zipFiles.Count -ne $files.Count) { throw 'Archive file count differs from portable tree.' }
    $zipIndex = @{}
    foreach ($entry in $zipFiles) { $zipIndex.Add($entry.FullName, $entry) }
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($source.Length + 1).Replace('\', '/')
        $zipEntry = $zipIndex['TaskManager-win-x64/' + $relative]
        if (-not $zipEntry -or $zipEntry.Length -ne $file.Length) { throw "Archive entry differs: $relative" }
        $stream = $zipEntry.Open()
        $algorithm = [Security.Cryptography.SHA256]::Create()
        try { $archivedHash = [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') }
        finally { $algorithm.Dispose(); $stream.Dispose() }
        if ($archivedHash -ne (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) { throw "Archive content differs: $relative" }
    }
} finally { $archive.Dispose() }
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
$releaseSums = @(Get-ChildItem -LiteralPath $destination -File | Where-Object { $_.Extension -in @('.exe','.zip') } | Sort-Object Name | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name })
[IO.File]::WriteAllLines((Join-Path $destination 'SHA256SUMS'), $releaseSums, [Text.UTF8Encoding]::new($false))
Write-Host "Verified $($files.Count) files, including .electron and dependency licenses."
Write-Host $archivePath
Write-Host "SHA256 $hash"
