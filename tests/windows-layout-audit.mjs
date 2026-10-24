import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { writeFile } from 'node:fs/promises';
import path from 'node:path';
import { portOpen } from './e2e-support.mjs';

const root=path.resolve(import.meta.dirname,'..');
const quote=value=>"'"+value.replaceAll("'","''")+"'";
// Read only the dedicated application/runtime and workload paths; never save a
// full machine process listing or kill a process selected by name or port.
const script=`$root=${quote(root)}; $paths=@((Join-Path $root 'output/TaskManager-win-x64'),(Join-Path $root 'src/TaskManager.Host/bin/Release'),(Join-Path $root 'tests/TaskManager.TestWorkload/bin/Release')); $found=@(Get-CimInstance Win32_Process | Where-Object { $image=$_.ExecutablePath; $owned=$false; if($image){foreach($prefix in $paths){if($image.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){$owned=$true}};if($image.Contains([IO.Path]::DirectorySeparatorChar+'blue-native-v11-') -and [IO.Path]::GetFileName($image).StartsWith('=BlueNative-blue-native-v11-',[StringComparison]::OrdinalIgnoreCase)){$owned=$true}};$owned } | ForEach-Object { @{pid=$_.ProcessId;fileTime=$_.CreationDate.ToFileTimeUtc().ToString();imagePath=$_.ExecutablePath} }); ConvertTo-Json -InputObject $found -Compress`;
const native=spawnSync('powershell.exe',['-NoProfile','-Command',script],{encoding:'utf8',windowsHide:true,timeout:20000});
assert.equal(native.status,0,native.stderr);const processes=JSON.parse(native.stdout.trim()||'[]');
const ports=[5191,5192,5193,5194,5195,5196,5197,5198,5199,5200,5201,5202,5203,5204,5230,5290,5296,9224,9225,9226,9227,9228,9230,9231,9232,9250,9290,9296];
const open=[];for(const port of ports)if(await portOpen(port))open.push(port);
const result={result:processes.length||open.length?'FAIL':'PASS',checkedAtUtc:new Date().toISOString(),assertions:2,checks:['dedicated application/runtime/workload identities all exited','all acceptance HTTP/CDP listeners released'],processes,openPorts:open,checkedPorts:ports,scope:'read-only post-run audit of task paths and dedicated ports; no cleanup mutations'};
await writeFile(path.join(root,'output/playwright/windows-layout/cleanup-audit.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result,null,2));assert.equal(result.result,'PASS');
