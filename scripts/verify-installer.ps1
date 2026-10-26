param(
    [string]$Version,
    [string]$SevenZipPath = 'C:\Program Files\7-Zip\7z.exe',
    [string]$ManifestToolPath,
    [string]$InstallerPath,
    [string]$PayloadDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
[xml]$project = Get-Content -LiteralPath (Join-Path $root 'src/TaskManager.Host/TaskManager.Host.csproj') -Raw
if (-not $Version) { $Version = [string]$project.Project.PropertyGroup.Version }
if ($Version -ne [string]$project.Project.PropertyGroup.Version) { throw 'Version must match the project.' }
if (-not $ManifestToolPath) {
    $sdk = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
    $ManifestToolPath = Get-ChildItem -LiteralPath $sdk -Directory | Where-Object Name -Match '^10\.' | Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName 'x64/mt.exe' } | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if (-not $ManifestToolPath -or -not (Test-Path -LiteralPath $ManifestToolPath -PathType Leaf)) { throw 'Windows SDK mt.exe is required.' }
if (-not (Test-Path -LiteralPath $SevenZipPath -PathType Leaf)) { throw '7-Zip is required for independent NSIS extraction.' }
if (-not $InstallerPath) { $InstallerPath = Join-Path $root "output/releases/BlueDomainTaskManager-v$Version-win-x64-setup.exe" }
if (-not $PayloadDirectory) { $PayloadDirectory = Join-Path $root 'output/TaskManager-win-x64' }
$installer = (Resolve-Path -LiteralPath $InstallerPath).Path
$payload = (Resolve-Path -LiteralPath $PayloadDirectory).Path.TrimEnd('\', '/')
$identity = & (Join-Path $PSScriptRoot 'installer/Read-AssemblyReleaseIdentity.ps1') -Path (Join-Path $payload 'TaskManager.Host.dll')
$sourceCommit = [string]$identity.SourceCommit
if (-not $sourceCommit -and $identity.InformationalVersion -cmatch '\+([0-9a-f]{40})(?:\.|$)') { $sourceCommit = $Matches[1] }
$expectedIdentity = "ReleaseTag=$($identity.ReleaseTag); SourceCommit=$sourceCommit"
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($installer).Comments -cne $expectedIdentity) { throw 'Installer release identity differs from the actual Host assembly.' }
$evidence = Join-Path $root ('output/installer-verification-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$extracted = Join-Path $evidence 'extracted'
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
& $SevenZipPath 'x' '-y' "-o$extracted" $installer | Out-File -LiteralPath (Join-Path $evidence '7z-extract.txt') -Encoding utf8
if ($LASTEXITCODE -ne 0) { throw "7-Zip extraction failed: $LASTEXITCODE" }
$manifests = [ordered]@{}
foreach ($target in @(@{ Name='host'; Path=(Join-Path $payload 'TaskManager.Host.exe'); Level='requireAdministrator' }, @{ Name='launcher'; Path=(Join-Path $payload 'TaskManager.Launcher.exe'); Level='asInvoker' }, @{ Name='installer'; Path=$installer; Level='requireAdministrator' })) {
    $manifest = Join-Path $evidence ($target.Name + '.manifest.xml')
    & $ManifestToolPath "-inputresource:$($target.Path);#1" "-out:$manifest" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Manifest extraction failed: $($target.Name)" }
    [xml]$document = Get-Content -LiteralPath $manifest -Raw
    $level = $document.SelectSingleNode('//*[local-name()="requestedExecutionLevel"]')
    if ($level.level -ne $target.Level -or $level.uiAccess -ne 'false') { throw "Execution manifest mismatch: $($target.Name), expected $($target.Level) / uiAccess=false" }
    $manifests[$target.Name] = $level.level
}
$files = @(Get-ChildItem -LiteralPath $payload -Recurse -Force -File)
$verified = 0
foreach ($file in $files) {
    $relative = $file.FullName.Substring($payload.Length + 1)
    $unpacked = Join-Path $extracted $relative
    if (-not (Test-Path -LiteralPath $unpacked -PathType Leaf)) { throw "NSIS payload missing: $relative" }
    if ($file.Length -ne (Get-Item -LiteralPath $unpacked).Length -or (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $unpacked -Algorithm SHA256).Hash) { throw "NSIS payload content mismatch: $relative" }
    $verified++
}
# 7-Zip versions differ in whether the WriteUninstaller delta is reconstructed.
# Verify it when available; otherwise report the exact remaining lifecycle check.
$uninstaller = @(Get-ChildItem -LiteralPath $extracted -Recurse -Force -File -Filter 'Uninstall.exe') | Select-Object -First 1
$uninstallerState = 'not available from static extraction; verify after isolated installation'
if ($uninstaller) {
    $manifest = Join-Path $evidence 'uninstaller.manifest.xml'
    & $ManifestToolPath "-inputresource:$($uninstaller.FullName);#1" "-out:$manifest" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Extracted uninstaller manifest could not be read.' }
    [xml]$document = Get-Content -LiteralPath $manifest -Raw
    $level = $document.SelectSingleNode('//*[local-name()="requestedExecutionLevel"]')
    if ($level.level -ne 'requireAdministrator' -or $level.uiAccess -ne 'false') { throw 'Uninstaller administrator manifest missing.' }
    $uninstallerState = 'requireAdministrator / uiAccess=false'
}
$report = [ordered]@{
    version = $Version
    installer = $installer
    installerBytes = (Get-Item -LiteralPath $installer).Length
    installerSha256 = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    releaseIdentity = @{ releaseTag=$identity.ReleaseTag; sourceCommit=$sourceCommit }
    payloadFilesVerified = $verified
    manifests = $manifests
    uninstallerManifest = $uninstallerState
    signature = [string](Get-AuthenticodeSignature -LiteralPath $installer).Status
    runtimeConfiguration = (Get-Content -LiteralPath (Join-Path $payload 'TaskManager.Host.runtimeconfig.json') -Raw | ConvertFrom-Json).runtimeOptions
    lifecycle = 'Not executed by this static verifier. No installer, uninstaller or IFEO change is run.'
}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'results.json') -Encoding utf8
Write-Host "Verified $verified NSIS payload files. Evidence: $evidence"
