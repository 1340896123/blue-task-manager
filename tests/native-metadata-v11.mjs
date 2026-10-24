import { stopOwnedHost } from './etw-support.mjs';
import { spawn, spawnSync } from 'node:child_process';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { check, assertions, until, delay, portOpen } from './e2e-support.mjs';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..'), framework='net10.0-windows10.0.19041.0';
const temporary=await mkdtemp(path.join(tmpdir(),'blue-metadata-')),port=5200,origin=`http://127.0.0.1:${port}`;
if(await portOpen(port))throw new Error('Metadata test port busy');
const host=spawn(path.join(root,'src/TaskManager.Host/bin/Release',framework,'TaskManager.Host.exe'),['--headless',`--port=${port}`],{env:{...process.env,TASKMANAGER_DATA_DIR:temporary},stdio:'ignore',windowsHide:true});
const target=spawn(path.join(root,'tests/TaskManager.TestWorkload/bin/Release',framework,'TaskManager.TestWorkload.exe'),[path.join(temporary,'load'),'90'],{stdio:'ignore',windowsHide:true});
try{
 await until(async()=>(await(await fetch(origin+'/api/health')).json()).ready,'metadata host ready');
 let snapshot;
 await until(async()=>{snapshot=await(await fetch(origin+'/api/snapshot')).json();return snapshot.slowDataCapturedAtUtc&&snapshot.processes.some(p=>p.pid===target.pid&&p.publisher)&&snapshot.processes.some(p=>p.path?.toLowerCase()==='c:\\windows\\explorer.exe'&&p.publisher);},'real owned and Windows CompanyName metadata',30000);
 const own=snapshot.processes.find(p=>p.pid===target.pid),system=snapshot.processes.find(p=>p.path?.toLowerCase()==='c:\\windows\\explorer.exe'&&p.publisher);
 // Independent managed metadata/Windows account consumers are test oracles only.
 const command=`$ErrorActionPreference='Stop'; $p=Get-CimInstance Win32_Process -Filter 'ProcessId = ${target.pid}'; $o=Invoke-CimMethod -InputObject $p -MethodName GetOwner; [pscustomobject]@{own=[System.Diagnostics.FileVersionInfo]::GetVersionInfo('${own.path.replaceAll("'","''")}').CompanyName;system=[System.Diagnostics.FileVersionInfo]::GetVersionInfo('${system.path.replaceAll("'","''")}').CompanyName;current=[System.Security.Principal.WindowsIdentity]::GetCurrent().Name;owner=$o.Domain+'\\'+$o.User} | ConvertTo-Json -Compress`;
 const result=spawnSync('powershell.exe',['-NoProfile','-Command',command],{encoding:'utf8',windowsHide:true});if(result.status!==0)throw new Error(result.stderr);const actual=JSON.parse(result.stdout);
 const bootstrap=await(await fetch(origin+'/api/bootstrap')).json();
 check(own.publisher===actual.own,'CsWin32 Translation/CompanyName matches independent file-version query for owned executable');
 check(system.publisher===actual.system,'CsWin32 Translation/CompanyName matches independent Windows Explorer metadata');
 check(bootstrap.currentUserName===actual.current,'CsWin32 GetUserNameEx matches independent current Windows account');
 check(own.userName===actual.owner&&own.userName===bootstrap.currentUserName,'CsWin32 process token owner agrees with current-account filter identity');

 const registryScript=`[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false);$items=@();foreach($h in @([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryHive]::LocalMachine)){foreach($v in @([Microsoft.Win32.RegistryView]::Registry64,[Microsoft.Win32.RegistryView]::Registry32)){foreach($n in @('Software\\Microsoft\\Windows\\CurrentVersion\\Run','Software\\Microsoft\\Windows\\CurrentVersion\\RunOnce')){$r=[Microsoft.Win32.RegistryKey]::OpenBaseKey($h,$v);try{$k=$r.OpenSubKey($n,$false);if($k){try{foreach($name in $k.GetValueNames()){$value=$k.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames);if($value -is [string]){$items+=[pscustomobject]@{source= $(if($h -eq [Microsoft.Win32.RegistryHive]::CurrentUser){'HKCU'}else{'HKLM'})+'\\'+$n;name=$name;command=$value}}}}finally{$k.Dispose()}}}finally{$r.Dispose()}}}};ConvertTo-Json -InputObject @($items) -Compress`;
 const registryProbe=spawnSync('powershell.exe',['-NoProfile','-Command',registryScript],{encoding:'utf8',windowsHide:true});if(registryProbe.status!==0)throw new Error(registryProbe.stderr);const oracle=JSON.parse(registryProbe.stdout);const canonical=v=>JSON.stringify([v.source.replace(/ \(Registry(?:32|64)\)$/,''),v.name,v.command]);const expected=[...new Set(oracle.map(canonical))].sort();const observed=[...new Set(snapshot.startupItems.filter(v=>/^HK(?:CU|LM)\\/.test(v.source)).map(canonical))].sort();

 check(JSON.stringify(expected)===JSON.stringify(observed),'CsWin32 read-only Run/RunOnce 32/64 views match independent managed registry enumeration and deduplication');
 console.log(JSON.stringify({result:'PASS',assertions:assertions.length,checks:assertions},null,2));
}finally{target.kill();await stopOwnedHost(host);await until(async()=>!await portOpen(port),'metadata cleanup');await delay(300);await rm(temporary,{recursive:true,force:true});}
