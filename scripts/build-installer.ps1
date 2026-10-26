param(
    [string]$Version,
    [switch]$SkipBuild,
    [switch]$SkipRestore,
    [string]$ReleaseTag,
    [string]$SourceCommit,
    [string]$PayloadDirectory,
    [string]$OutputDirectory,
    [string]$MakeNsisPath = 'makensis.exe'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
[xml]$project = Get-Content -LiteralPath (Join-Path $root 'src/TaskManager.Host/TaskManager.Host.csproj') -Raw
$projectVersion = [string]$project.Project.PropertyGroup.Version
if (-not $Version) { $Version = $projectVersion }
if ($Version -notmatch '^\d+\.\d+\.\d+$' -or $Version -ne $projectVersion) { throw 'Installer Version must exactly match the project Version (major.minor.patch).' }
if (-not $PSBoundParameters.ContainsKey('ReleaseTag')) { $ReleaseTag = [string]$env:TASKMANAGER_RELEASE_TAG }
if (-not $PSBoundParameters.ContainsKey('SourceCommit')) { $SourceCommit = [string]$env:TASKMANAGER_SOURCE_COMMIT }
function Assert-ReleaseIdentity([string]$Tag, [string]$Commit) {
    $identifier = '(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)'
    $semver = '\Av(?<core>(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*))(?:-' + $identifier + '(?:\.' + $identifier + ')*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z'
    if ($Tag) {
        $parsed = [regex]::Match($Tag, $semver)
        if (-not $parsed.Success -or $Tag.Length -gt 128 -or $parsed.Groups['core'].Value -cne $Version) { throw 'ReleaseTag must be a valid v-prefixed SemVer tag matching the Host base version.' }
        if (-not $Commit) { throw 'A release tag requires the complete source commit.' }
    }
    if ($Commit -and $Commit -cnotmatch '\A[0-9a-f]{40}\z') { throw 'SourceCommit must be the complete lowercase Git SHA.' }
}
Assert-ReleaseIdentity $ReleaseTag $SourceCommit
if ($PayloadDirectory -and -not $SkipBuild) { throw 'An explicit PayloadDirectory requires -SkipBuild; publish the complete payload first.' }
$requestedTag = $ReleaseTag
$requestedCommit = $SourceCommit
if (-not $SkipBuild) {
    $previousTag = $env:TASKMANAGER_RELEASE_TAG
    $previousCommit = $env:TASKMANAGER_SOURCE_COMMIT
    try {
        $env:TASKMANAGER_RELEASE_TAG = $ReleaseTag
        $env:TASKMANAGER_SOURCE_COMMIT = $SourceCommit
        & (Join-Path $PSScriptRoot 'publish-desktop.ps1') -SkipRestore:$SkipRestore
    } finally {
        $env:TASKMANAGER_RELEASE_TAG = $previousTag
        $env:TASKMANAGER_SOURCE_COMMIT = $previousCommit
    }
}
if (-not $PayloadDirectory) { $PayloadDirectory = Join-Path $root 'output/TaskManager-win-x64' }
$payload = (Resolve-Path -LiteralPath $PayloadDirectory).Path.TrimEnd('\', '/')
$customOutput = -not [string]::IsNullOrWhiteSpace($OutputDirectory)
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'output/releases' }
$releases = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\', '/')
if ($releases.Equals($payload, [StringComparison]::OrdinalIgnoreCase) -or $releases.StartsWith($payload + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Installer output must be outside the payload directory.' }
$hostExe = Join-Path $payload 'TaskManager.Host.exe'
$assembly = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $payload 'TaskManager.Host.dll'))
if ($assembly.Version.ToString() -ne "$Version.0" -or (Get-Item -LiteralPath $hostExe).VersionInfo.FileVersion -ne "$Version.0") { throw 'Actual Host payload version does not match installer version.' }
$identity = & (Join-Path $PSScriptRoot 'installer/Read-AssemblyReleaseIdentity.ps1') -Path (Join-Path $payload 'TaskManager.Host.dll')
if (($requestedTag -and $identity.ReleaseTag -cne $requestedTag) -or ($requestedCommit -and $identity.SourceCommit -cne $requestedCommit)) {
    throw 'Host AssemblyMetadata does not match the requested ReleaseTag/SourceCommit. Rebuild the payload before packaging.'
}
if (-not $requestedTag) { $ReleaseTag = [string]$identity.ReleaseTag }
if (-not $requestedCommit) {
    $SourceCommit = [string]$identity.SourceCommit
    if (-not $SourceCommit -and $identity.InformationalVersion -cmatch '\+([0-9a-f]{40})(?:\.|$)') { $SourceCommit = $Matches[1] }
}
Assert-ReleaseIdentity $ReleaseTag $SourceCommit
$config = Get-Content -LiteralPath (Join-Path $payload 'TaskManager.Host.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($config.runtimeOptions.framework -or $config.runtimeOptions.frameworks -or @($config.runtimeOptions.includedFrameworks).Count -lt 2) { throw 'Payload must include self-contained .NET and ASP.NET frameworks.' }
foreach ($framework in $config.runtimeOptions.includedFrameworks) {
    if ($framework.version -ne [string]$project.Project.PropertyGroup.DesktopRuntimeVersion) { throw "Runtime version mismatch: $($framework.name)" }
}
foreach ($file in @('restore-windows-task-manager.cmd','TaskManager.Launcher.exe','TaskManager.Launcher.dll','TaskManager.Launcher.deps.json','TaskManager.Launcher.runtimeconfig.json','coreclr.dll','hostfxr.dll','hostpolicy.dll','System.Private.CoreLib.dll','Microsoft.AspNetCore.dll','licenses/Microsoft.NETCore.App/LICENSE.txt','licenses/Microsoft.NETCore.App/THIRD-PARTY-NOTICES.TXT','licenses/Microsoft.AspNetCore.App/LICENSE.txt','licenses/Microsoft.AspNetCore.App/THIRD-PARTY-NOTICES.TXT','wwwroot/index.html','.electron/main.js','.electron/custom_main.js','.electron/node_modules/electron/dist/electron.exe','.electron/node_modules/electron/dist/LICENSE','.electron/node_modules/electron/dist/LICENSES.chromium.html')) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $file) -PathType Leaf)) { throw "Missing installer payload file: $file" }
}
$launcherConfig = Get-Content -LiteralPath (Join-Path $payload 'TaskManager.Launcher.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($launcherConfig.runtimeOptions.framework -or $launcherConfig.runtimeOptions.frameworks -or @($launcherConfig.runtimeOptions.includedFrameworks).Count -ne 1 -or $launcherConfig.runtimeOptions.includedFrameworks[0].version -ne [string]$project.Project.PropertyGroup.DesktopRuntimeVersion) { throw 'Launcher must share the pinned self-contained .NET runtime.' }
$bridge = Get-Content -LiteralPath (Join-Path $payload '.electron/package.json') -Raw | ConvertFrom-Json
if ($bridge.version -ne $Version -or $bridge.devDependencies.electron -ne [string]$project.Project.PropertyGroup.ElectronVersion) { throw 'Electron bridge version does not match project.' }
if ((Get-Content -LiteralPath (Join-Path $payload '.electron/node_modules/electron/dist/version') -Raw).Trim() -ne [string]$project.Project.PropertyGroup.ElectronVersion) { throw 'Actual Electron distribution version differs.' }
$entries = @(Get-ChildItem -LiteralPath $payload -Recurse -Force)
if ($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Payload cannot contain filesystem links.' }
if ($entries | Where-Object { $_.Name -in @('.git','preferences.json','test-results','tests') -or $_.Name -match '^\.env(?:\.|$)' -or $_.Extension -in @('.pdb','.log','.pid') }) { throw 'Payload contains repository, test, debug, or private state.' }
$files = @($entries | Where-Object { -not $_.PSIsContainer } | Sort-Object FullName)
$staging = if ($customOutput) { Join-Path $releases '.nsis' } else { Join-Path $root 'output/nsis' }
New-Item -ItemType Directory -Path $staging,$releases -Force | Out-Null
function Escape-Nsis([string]$Value) { $Value.Replace('$','$$').Replace('"','$\"') }
$install = [Collections.Generic.List[string]]::new()
$delete = [Collections.Generic.List[string]]::new()
$removeDirectories = [Collections.Generic.List[string]]::new()
$checkPaths = [Collections.Generic.List[string]]::new()
$verify = [Collections.Generic.List[string]]::new()
$hashes = [Collections.Generic.List[string]]::new()
$directory = $null
foreach ($file in $files) {
    $relative = $file.FullName.Substring($payload.Length + 1)
    if ($relative -match '[\r\n]' -or $relative.Length -gt 850) { throw "Unsupported NSIS filename: $relative" }
    $parent = Split-Path -Parent $relative
    if ($parent -ne $directory) { $install.Add('SetOutPath "$INSTDIR' + $(if ($parent) { '\' + (Escape-Nsis $parent) }) + '"'); $directory = $parent }
    $install.Add('File "' + (Escape-Nsis $file.FullName) + '"')
    $delete.Add('Delete "$INSTDIR\' + (Escape-Nsis $relative) + '"')
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    $hashes.Add($hash.ToLowerInvariant() + '  ' + $relative.Replace('\','/'))
    # The maintenance CLI loads the real self-contained .NET runtime. Verify all
    # its executable/configuration inputs before executing anything installed.
    if (-not $parent -and $file.Extension -in @('.exe','.dll','.json')) {
        $verify.Add('Push "$INSTDIR\' + (Escape-Nsis $relative) + '"')
        $verify.Add('Push "' + $hash + '"')
        $verify.Add('Call un.VerifyFile')
    }
}
foreach ($entry in $entries) { $checkPaths.Add('!insertmacro CheckPayloadPath "' + (Escape-Nsis $entry.FullName.Substring($payload.Length + 1)) + '"') }
foreach ($dir in @($entries | Where-Object PSIsContainer | Sort-Object { $_.FullName.Length } -Descending)) { $removeDirectories.Add('RMDir "$INSTDIR\' + (Escape-Nsis $dir.FullName.Substring($payload.Length + 1)) + '"') }
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllLines((Join-Path $staging 'install-files.nsh'), $install, $utf8)
[IO.File]::WriteAllLines((Join-Path $staging 'delete-files.nsh'), $delete, $utf8)
[IO.File]::WriteAllLines((Join-Path $staging 'remove-directories.nsh'), $removeDirectories, $utf8)
[IO.File]::WriteAllLines((Join-Path $staging 'check-paths.nsh'), $checkPaths, $utf8)
[IO.File]::WriteAllLines((Join-Path $staging 'verify-maintenance.nsh'), $verify, $utf8)
[IO.File]::WriteAllLines((Join-Path $staging 'payload.sha256'), $hashes, $utf8)
[IO.File]::WriteAllText((Join-Path $staging 'release-identity.json'), (@{ version=$Version; releaseTag=$ReleaseTag; sourceCommit=$SourceCommit } | ConvertTo-Json), $utf8)
$installer = Join-Path $releases "BlueDomainTaskManager-v$Version-win-x64-setup.exe"
$size = [int][Math]::Ceiling(($files | Measure-Object Length -Sum).Sum / 1024)
& $MakeNsisPath '/V3' '/WX' '/INPUTCHARSET' 'UTF8' "/DPRODUCT_VERSION=$Version" "/DRELEASE_TAG=$ReleaseTag" "/DSOURCE_COMMIT=$SourceCommit" "/DPAYLOAD_DIR=$payload" "/DGENERATED_DIR=$staging" "/DINSTALLER_PATH=$installer" "/DPROJECT_ROOT=$root" "/DPAYLOAD_SIZE_KB=$size" (Join-Path $PSScriptRoot 'installer/TaskManager.nsi')
if ($LASTEXITCODE -ne 0) { throw "makensis failed: $LASTEXITCODE" }
# Reuse the exact tree for ZIP and installer. Existing differently named ZIPs
# stay in place; overwrite only the artifacts for this exact version.
$archive = Join-Path $releases "BlueDomainTaskManager-v$Version-win-x64.zip"
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
& (Join-Path $PSScriptRoot 'package-release.ps1') -Version $Version -PayloadDirectory $payload -OutputDirectory $releases
$sums = @(Get-ChildItem -LiteralPath $releases -File | Where-Object { $_.Extension -in @('.exe','.zip') } | Sort-Object Name | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name })
[IO.File]::WriteAllLines((Join-Path $releases 'SHA256SUMS'), $sums, $utf8)
Write-Host "Installer ready (unsigned): $installer"
