param([ValidateSet('create','inspect','mutate','cleanup')][string]$Action, [Parameter(Mandatory=$true)][string]$ManifestPath, [int]$State = 3)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
$runPath = 'Software\Microsoft\Windows\CurrentVersion\Run'
$approvalPath = 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved'
$utf8 = [Text.UTF8Encoding]::new($false)
function Save-Manifest { [IO.File]::WriteAllText($ManifestPath, ($script:manifest | ConvertTo-Json -Depth 12), $utf8) }
function Open-Root($Hive, $View) { [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::$Hive, [Microsoft.Win32.RegistryView]::$View) }
function Payload($Data, $Kind) { if ($Kind -eq 'Binary') { [Convert]::ToBase64String([byte[]]$Data) } else { [string]$Data } }
function Binary-State([int]$Value, [int]$Length=12) { $bytes = [byte[]]::new($Length); [BitConverter]::GetBytes($Value).CopyTo($bytes,0); return ,$bytes }
function Add-Value($Hive,$View,$Key,$Name,$Data,$Kind='String') {
    $root = Open-Root $Hive $View
    try {
        $existing = $root.OpenSubKey($Key)
        if ($existing) { try { if ($existing.GetValueNames() -contains $Name) { throw 'Refusing to overwrite an existing startup fixture value.' } } finally { $existing.Dispose() } }
        elseif (-not ($script:manifest.keys | Where-Object { $_.hive -eq $Hive -and $_.view -eq $View -and $_.key -eq $Key })) {
            $script:manifest.keys += @{hive=$Hive;view=$View;key=$Key}; Save-Manifest
        }
        $record = @{hive=$Hive;view=$View;key=$Key;name=$Name;kind=$Kind;allowed=@((Payload $Data $Kind))}
        $script:manifest.values += $record; Save-Manifest
        $handle = $root.CreateSubKey($Key)
        try { $handle.SetValue($Name,$Data,[Microsoft.Win32.RegistryValueKind]::$Kind) } finally { $handle.Dispose() }
    } finally { $root.Dispose() }
}
function Add-Run($Suffix,$Value,$Kind='Binary',$Length=12) {
    $name=$script:manifest.prefix+$Suffix
    Add-Value 'CurrentUser' 'Registry64' $runPath $name $script:manifest.command
    if ($null -ne $Value) { $data = if ($Kind -eq 'Binary') { Binary-State $Value $Length } else { 'invalid approval type' }; Add-Value 'CurrentUser' 'Registry64' "$approvalPath\Run" $name $data $Kind }
}
function Add-StartupFile($Suffix,$Common,$Value) {
    $folder = [Environment]::GetFolderPath($(if($Common){[Environment+SpecialFolder]::CommonStartup}else{[Environment+SpecialFolder]::Startup}))
    if (-not $folder -or -not [IO.Directory]::Exists($folder)) { throw 'Windows startup folder is unavailable.' }
    $name = $script:manifest.prefix+$Suffix+'.lnk'; $file=Join-Path $folder $name
    if ([IO.File]::Exists($file)) { throw 'Refusing to replace a startup file.' }
    $target=$env:SystemRoot+'\System32\cmd.exe';$arguments='/d /c exit 0';$description=$script:manifest.prefix
    $script:manifest.files += @{path=$file;target=$target;arguments=$arguments;description=$description}; Save-Manifest
    $shell=New-Object -ComObject WScript.Shell
    try{$shortcut=$shell.CreateShortcut($file);try{$shortcut.TargetPath=$target;$shortcut.Arguments=$arguments;$shortcut.Description=$description;$shortcut.Save()}finally{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)}}finally{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)}
    Add-Value $(if($Common){'LocalMachine'}else{'CurrentUser'}) 'Registry64' "$approvalPath\StartupFolder" $name (Binary-State $Value) 'Binary'
}
if ($Action -eq 'create') {
    if (Test-Path -LiteralPath $ManifestPath) { throw 'Fixture manifest already exists.' }
    $script:manifest = @{prefix=('TakaoStartupQA-'+[Guid]::NewGuid().ToString('N')+'-');values=@();files=@();keys=@();command=('"'+$env:SystemRoot+'\System32\cmd.exe" /d /c exit 0')}
    Save-Manifest
    Add-Run 'enabled' 2; Add-Run 'disabled' 3; Add-Run 'unknown1' 1; Add-Run 'unknown6' 6; Add-Run 'unknown7' 7
    Add-Run 'badtype' 2 'String'; Add-Run 'short' 2 'Binary' 4; Add-Run 'long' 2 'Binary' 16; Add-Run 'missing' $null
    $once=$manifest.prefix+'once'; Add-Value 'CurrentUser' 'Registry64' ($runPath+'Once') $once $manifest.command
    Add-Value 'CurrentUser' 'Registry64' "$approvalPath\Run" $once (Binary-State 3) 'Binary'
    $views=$manifest.prefix+'views'
    Add-Value 'LocalMachine' 'Registry64' $runPath $views $manifest.command; Add-Value 'LocalMachine' 'Registry32' $runPath $views $manifest.command
    Add-Value 'LocalMachine' 'Registry64' "$approvalPath\Run" $views (Binary-State 2) 'Binary'; Add-Value 'LocalMachine' 'Registry64' "$approvalPath\Run32" $views (Binary-State 3) 'Binary'
    foreach($suffix in @('conflict','default-conflict')) {
        $name=$manifest.prefix+$suffix; Add-Value 'LocalMachine' 'Registry64' $runPath $name $manifest.command
        if($suffix -eq 'conflict'){ Add-Value 'LocalMachine' 'Registry64' "$approvalPath\Run" $name (Binary-State 2) 'Binary' }
        Add-Value 'CurrentUser' 'Registry64' "$approvalPath\Run" $name (Binary-State 3) 'Binary'
    }
    Add-StartupFile 'folder-enabled' $false 2; Add-StartupFile 'folder-disabled' $false 3; Add-StartupFile 'common-disabled' $true 3
    # An intentionally opposite suffixless record proves matching uses the real file name.
    Add-Value 'CurrentUser' 'Registry64' "$approvalPath\StartupFolder" ($manifest.prefix+'folder-enabled') (Binary-State 3) 'Binary'
    @{prefix=$manifest.prefix;values=$manifest.values.Count;files=$manifest.files.Count} | ConvertTo-Json -Compress
    exit 0
}
$script:manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
if ($manifest.prefix -notmatch '^TakaoStartupQA-[0-9a-f]{32}-$') { throw 'Invalid fixture identity.' }
if ($Action -eq 'mutate') {
    if($State -notin @(2,3)){throw 'Only the known enabled fixture may be toggled during acceptance.'}
    $record=$manifest.values | Where-Object {$_.hive -eq 'CurrentUser' -and $_.key -eq "$approvalPath\Run" -and $_.name -eq ($manifest.prefix+'enabled')}
    $root=Open-Root $record.hive $record.view
    try {
        $key=$root.OpenSubKey($record.key,$true)
        try {
            if($key.GetValueKind($record.name).ToString() -ne $record.kind -or (Payload ($key.GetValue($record.name)) $record.kind) -notin $record.allowed){throw 'Fixture changed externally; refusing to mutate.'}
            $bytes=Binary-State $State; $record.allowed=@($record.allowed)+@((Payload $bytes 'Binary')); Save-Manifest
            $key.SetValue($record.name,$bytes,[Microsoft.Win32.RegistryValueKind]::Binary)
        }finally{$key.Dispose()}
    }finally{$root.Dispose()}
    @{state=$State} | ConvertTo-Json -Compress; exit 0
}
if ($Action -eq 'cleanup') {
    $deleted=0; $issues=@()
    foreach($record in @($manifest.values) | Sort-Object -Property name -Descending) {
        if(-not $record.name.StartsWith($manifest.prefix,[StringComparison]::Ordinal)){throw 'Refusing non-owned value cleanup.'}
        $root=Open-Root $record.hive $record.view
        try { $key=$root.OpenSubKey($record.key,$true); if($key){try{
            if($key.GetValueNames() -contains $record.name){
                $kind=$key.GetValueKind($record.name).ToString(); $actual=Payload ($key.GetValue($record.name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)) $kind
                if($kind -ne $record.kind -or $actual -notin $record.allowed){$issues+='value identity changed';continue}
                $key.DeleteValue($record.name); $deleted++
            }
        }finally{$key.Dispose()}} }finally{$root.Dispose()}
    }
    foreach($file in $manifest.files){
        if(-not [IO.Path]::GetFileName($file.path).StartsWith($manifest.prefix,[StringComparison]::Ordinal)){throw 'Refusing non-owned file cleanup.'}
        if([IO.File]::Exists($file.path)){
            $shell=New-Object -ComObject WScript.Shell
            try{$shortcut=$shell.CreateShortcut($file.path);try{$same=$shortcut.TargetPath -eq $file.target -and $shortcut.Arguments -ceq $file.arguments -and $shortcut.Description -ceq $file.description}finally{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)}}finally{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)}
            if(-not $same){$issues+='file identity changed';continue};[IO.File]::Delete($file.path);$deleted++
        }
    }
    foreach($record in $manifest.keys){$root=Open-Root $record.hive $record.view;try{$key=$root.OpenSubKey($record.key);if($key){try{$empty=$key.ValueCount -eq 0 -and $key.SubKeyCount -eq 0}finally{$key.Dispose()};if($empty){$root.DeleteSubKey($record.key,$false)}}}finally{$root.Dispose()}}
    if($issues.Count){throw ($issues -join '; ')}
    @{cleanup=$true;deleted=$deleted;registryValues=$manifest.values.Count;files=$manifest.files.Count} | ConvertTo-Json -Compress; exit 0
}
# Independent managed Registry reads are used only by the test oracle, never by production.
function Read-Approval($Hive,$Group,$Name) {
    $root=Open-Root $Hive 'Registry64'
    try {
        $key=$root.OpenSubKey("$approvalPath\$Group")
        if(-not $key){return @{present=$false;enabled=$true;kind='missing';length=0;state=$null}}
        try {
            if($key.GetValueNames() -notcontains $Name){return @{present=$false;enabled=$true;kind='missing';length=0;state=$null}}
            $kind=$key.GetValueKind($Name).ToString();$data=$key.GetValue($Name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            $length=if($data -is [byte[]]){$data.Length}else{0};$value=if($length -ge 4){[BitConverter]::ToUInt32($data,0)}else{$null}
            $enabled=$null;if($kind -eq 'Binary' -and $length -eq 12){if($value -eq 2){$enabled=$true}elseif($value -eq 3){$enabled=$false}}
            return @{present=$true;enabled=$enabled;kind=$kind;length=$length;state=$value}
        }finally{$key.Dispose()}
    }catch{return @{present=$true;enabled=$null;kind='error';length=0;state=$null}}finally{$root.Dispose()}
}
try{$rows=ConvertFrom-Json ([Console]::In.ReadToEnd())}catch{throw 'Fixture oracle input JSON could not be parsed; input omitted.'};$result=@()
foreach($row in $rows){
    $hive='CurrentUser';$group='Run';$name=$row.name;$once=$false
    if($row.source -match '^(HKCU|HKLM)\\Software\\Microsoft\\Windows\\CurrentVersion\\(Run|RunOnce) \(Registry(32|64)\)$'){
        if($Matches[1] -eq 'HKLM'){$hive='LocalMachine';if($Matches[3] -eq '32'){$group='Run32'}}
        $once=$Matches[2] -eq 'RunOnce'
    }elseif([IO.Path]::IsPathRooted($row.id)){
        $group='StartupFolder';$name=[IO.Path]::GetFileName($row.id)
        if([IO.Path]::GetDirectoryName($row.id) -eq [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonStartup)){$hive='LocalMachine'}
    }else{throw 'Unrecognized real startup source.'}
    if($once){$result+=@{id=$row.id;enabled=$null;kind='RunOnce';length=0;state=$null};continue}
    $own=Read-Approval $hive $group $name; $other=Read-Approval $(if($hive -eq 'CurrentUser'){'LocalMachine'}else{'CurrentUser'}) $group $name
    $enabled=$own.enabled
    if($null -eq $other.enabled -or ($other.present -and $own.enabled -ne $other.enabled)){$enabled=$null}
    $result+=@{id=$row.id;enabled=$enabled;kind=$own.kind;length=$own.length;state=$own.state}
}
ConvertTo-Json -InputObject @($result) -Depth 6 -Compress
