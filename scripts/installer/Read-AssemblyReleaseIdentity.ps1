param([Parameter(Mandatory)][string]$Path, [switch]$AsJson)
$ErrorActionPreference = 'Stop'

# PE metadata is read without loading or executing the application assembly.
# Release builds run under pwsh in CI; use the same runtime for local identity
# verification when the caller is Windows PowerShell.
if ($PSVersionTable.PSVersion.Major -lt 7) {
    $pwsh = Get-Command pwsh.exe -ErrorAction Stop
    $json = & $pwsh.Source -NoProfile -File $PSCommandPath -Path $Path -AsJson
    if ($LASTEXITCODE -ne 0) { throw 'Could not read the Host release identity with PowerShell 7.' }
    if ($AsJson) { return $json }
    return ($json | ConvertFrom-Json)
}

function Read-Identity([string]$AssemblyPath) {
    $stream = [IO.File]::OpenRead([IO.Path]::GetFullPath($AssemblyPath))
    $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        $metadata = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        $assembly = $metadata.GetAssemblyDefinition()
        $values = @{}
        foreach ($handle in $assembly.GetCustomAttributes()) {
            $attribute = $metadata.GetCustomAttribute($handle)
            if ($attribute.Constructor.Kind -ne [Reflection.Metadata.HandleKind]::MemberReference) { continue }
            $constructor = $metadata.GetMemberReference([Reflection.Metadata.MemberReferenceHandle]$attribute.Constructor)
            if ($constructor.Parent.Kind -ne [Reflection.Metadata.HandleKind]::TypeReference) { continue }
            $type = $metadata.GetTypeReference([Reflection.Metadata.TypeReferenceHandle]$constructor.Parent)
            if ($metadata.GetString($type.Namespace) -cne 'System.Reflection' -or $metadata.GetString($type.Name) -cne 'AssemblyMetadataAttribute') { continue }
            $blob = $metadata.GetBlobReader($attribute.Value)
            if ($blob.ReadUInt16() -ne 1) { throw 'Invalid AssemblyMetadata attribute prolog.' }
            $key = $blob.ReadSerializedString()
            $value = $blob.ReadSerializedString()
            if ($key -in @('ReleaseTag', 'SourceCommit')) {
                if ($values.ContainsKey($key)) { throw "Duplicate assembly release identity: $key" }
                $values[$key] = $value
            }
        }
        return [pscustomobject]@{
            ReleaseTag = [string]$values['ReleaseTag']
            SourceCommit = [string]$values['SourceCommit']
            InformationalVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo([IO.Path]::GetFullPath($AssemblyPath)).ProductVersion
        }
    } finally { $pe.Dispose(); $stream.Dispose() }
}

$identity = Read-Identity $Path
if ($AsJson) { $identity | ConvertTo-Json -Compress }
else { $identity }
