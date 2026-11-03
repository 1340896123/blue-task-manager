param([Parameter(Mandatory)][string]$QtPrefix, [Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference = 'Stop'
$source = Join-Path (Split-Path -Parent $PSScriptRoot) 'assets/licenses/Qt'
$target = Join-Path $Destination 'licenses/Qt'
New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item -LiteralPath $source -Destination (Join-Path $Destination 'licenses') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $QtPrefix 'sbom') -Destination $target -Recurse -Force
