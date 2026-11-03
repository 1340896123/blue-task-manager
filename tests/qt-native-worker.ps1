param([string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'output/qt-acceptance'))
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try { if (-not ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'A real elevated Windows token is required.' } }
finally { $identity.Dispose() }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$results = @()
foreach ($suite in @('native-api','startup','network','disk')) {
    $log = Join-Path $OutputDirectory "$suite.txt"
    if ($suite -eq 'native-api') { & node tests/native-api.mjs *> $log }
    else {
        $project = @{startup='Takao.StartupTests';network='Takao.NetworkTests';disk='Takao.DiskTests'}[$suite]
        & dotnet run --project "tests/$project" -c Release --no-build *> $log
    }
    $results += @{ suite=$suite; exitCode=$LASTEXITCODE; evidence=$log }
    @{ elevated=$true; results=$results } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutputDirectory 'native-results.json') -Encoding utf8
}
if (@($results | Where-Object exitCode -ne 0).Count) { exit 1 }
