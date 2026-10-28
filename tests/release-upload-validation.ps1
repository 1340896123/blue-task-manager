param([string]$OutputPath = '')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$workflow = [IO.File]::ReadAllText((Join-Path $root '.github/workflows/manual-windows-installer.yml'))
$functions = @()
foreach ($match in [regex]::Matches($workflow, '(?m)^ {8}run: \|\r?\n(?<body>(?:(?: {10}.*)?\r?\n)+)')) {
    $source = $match.Groups['body'].Value -replace '(?m)^ {10}', ''
    $tokens = $null; $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$errors)
    if ($errors.Count) { throw 'Workflow PowerShell must parse before its validation function can be tested.' }
    $functions += @($ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq 'Assert-UploadedRelease'}, $true))
}
if ($functions.Count -ne 1) { throw 'Expected one actual release validation function in the workflow.' }
# Test the exact workflow function without executing any publish/network commands.
. ([scriptblock]::Create($functions[0].Extent.Text))
$fixtures = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'fixtures/release-url-metadata.json') -Raw | ConvertFrom-Json
$previousServer = $env:GITHUB_SERVER_URL; $previousRepository = $env:GITHUB_REPOSITORY; $previousSource = $env:SOURCE_SHA
$checks = [Collections.Generic.List[object]]::new()
function Check-Release([string]$Name, [object]$Fixture, [scriptblock]$Mutation, [bool]$Reject) {
    $script:releaseId = $Fixture.release.id
    $script:tag = $Fixture.release.tag_name
    $script:isPrerelease = $Fixture.release.prerelease.ToString().ToLowerInvariant()
    $script:expectedAssets = @($Fixture.release.assets | ForEach-Object name)
    $script:expectedAssetMetadata = @{}
    foreach ($asset in $Fixture.release.assets) { $script:expectedAssetMetadata[$asset.name] = @{size=$asset.size;digest=$asset.digest} }
    $env:SOURCE_SHA = $Fixture.release.target_commitish
    $candidate = $Fixture.release | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    if ($Mutation) { & $Mutation $candidate }
    $rejected = $false
    try { Assert-UploadedRelease $candidate ([bool]$Fixture.release.draft) } catch { $rejected = $true }
    if ($rejected -ne $Reject) { throw "Unexpected release validation outcome: $Name" }
    $checks.Add([ordered]@{name=$Name;result='PASS';expected=$(if($Reject){'rejected'}else{'accepted'})})
}
try {
    $env:GITHUB_SERVER_URL = 'https://github.com'
    $env:GITHUB_REPOSITORY = '1340896123/blue-task-manager'
    foreach ($kind in @('draft', 'published')) {
        $fixture = $fixtures.$kind
        Check-Release "$kind real GitHub metadata" $fixture $null $false
        $negative = [ordered]@{
            'wrong hash' = {param($r) $r.assets[0].digest = 'sha256:' + ('0' * 64)}
            'wrong installer hash' = {param($r) ($r.assets | Where-Object name -Like '*.exe' | Select-Object -First 1).digest = 'sha256:' + ('0' * 64)}
            'wrong size' = {param($r) $r.assets[0].size++}
            'empty asset' = {param($r) $r.assets[0].size = 0}
            'wrong asset host' = {param($r) $r.assets[0].browser_download_url = $r.assets[0].browser_download_url.Replace('https://github.com/', 'https://example.com/')}
            'wrong asset repository' = {param($r) $r.assets[0].browser_download_url = $r.assets[0].browser_download_url.Replace('/1340896123/blue-task-manager/', '/other/repository/')}
            'wrong asset reference' = {param($r) $r.assets[0].browser_download_url = $r.assets[0].browser_download_url -replace '/releases/download/[^/]+/', '/releases/download/wrong-ref/'}
            'wrong URL filename' = {param($r) $r.assets[0].browser_download_url = $r.assets[0].browser_download_url -replace '[^/]+$', 'unexpected.exe'}
            'unknown filename' = {param($r) $r.assets[0].name = 'unexpected.exe'}
            'duplicate filename' = {param($r) $r.assets[1].name = $r.assets[0].name}
            'missing asset' = {param($r) $r.assets = @($r.assets | Select-Object -Skip 1)}
            'incomplete upload' = {param($r) $r.assets[0].state = 'new'}
            'HTTP asset' = {param($r) $r.assets[0].browser_download_url = $r.assets[0].browser_download_url.Replace('https://', 'http://')}
            'asset URL credentials' = {param($r) $r.assets[0].browser_download_url = $r.assets[0].browser_download_url.Replace('https://', 'https://user@')}
            'asset URL query' = {param($r) $r.assets[0].browser_download_url += '?download=1'}
            'asset URL fragment' = {param($r) $r.assets[0].browser_download_url += '#asset'}
            'wrong source' = {param($r) $r.target_commitish = '0' * 40}
            'wrong release ID' = {param($r) $r.id++}
            'wrong tag' = {param($r) $r.tag_name = 'v1.1.1-build.999'}
            'wrong draft state' = {param($r) $r.draft = -not $r.draft}
            'wrong prerelease state' = {param($r) $r.prerelease = -not $r.prerelease}
            'wrong page host' = {param($r) $r.html_url = $r.html_url.Replace('https://github.com/', 'https://example.com/')}
            'wrong page repository' = {param($r) $r.html_url = $r.html_url.Replace('/1340896123/blue-task-manager/', '/other/repository/')}
            'wrong page reference' = {param($r) $r.html_url = $r.html_url -replace '[^/]+$', 'wrong-ref'}
            'page URL query' = {param($r) $r.html_url += '?draft=1'}
        }
        foreach ($case in $negative.GetEnumerator()) { Check-Release "$kind $($case.Key)" $fixture $case.Value $true }
    }
    Check-Release 'draft asset cannot use final tag while its page is untagged' $fixtures.draft {param($r) $r.assets[0].browser_download_url = $r.assets[0].browser_download_url -replace '/untagged-[^/]+/', ('/' + $r.tag_name + '/')} $true
    Check-Release 'published page cannot remain untagged' $fixtures.published {param($r) $r.html_url = $r.html_url -replace '[^/]+$', 'untagged-1212ae520a9db1e1484b'} $true
} finally {
    $env:GITHUB_SERVER_URL = $previousServer; $env:GITHUB_REPOSITORY = $previousRepository; $env:SOURCE_SHA = $previousSource
}
$report = [ordered]@{result='PASS';assertions=$checks.Count;checkedAtUtc=[DateTime]::UtcNow.ToString('o');workflowFunction='Assert-UploadedRelease';draftFixture=$fixtures.draft.sourceApi;publishedFixture=$fixtures.published.sourceApi;checks=$checks}
if (-not $OutputPath) { $OutputPath = Join-Path $root 'output/release-preflight/release-url-validation.json' }
$parent = Split-Path -Parent ([IO.Path]::GetFullPath($OutputPath))
New-Item -ItemType Directory -Path $parent -Force | Out-Null
[IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), ($report | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
$report | ConvertTo-Json -Depth 5
