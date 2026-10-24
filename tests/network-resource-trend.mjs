import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { delay, until, processTree, ownedAlive, portOpen } from './e2e-support.mjs';
import { probe } from './native-ui-support.mjs';
import { networkSessions, stopOwnedHost } from './etw-support.mjs';

const root=path.resolve(import.meta.dirname,'..');
const release=process.env.TASKMANAGER_NETWORK_RELEASE_DIRECTORY??path.join(root,'output/TaskManager-win-x64');
const output=path.join(root,'output/playwright/network-throughput');
const directory=await mkdtemp(path.join(tmpdir(),'taskmanager-network-resources-'));
const port=5418,origin=`http://127.0.0.1:${port}`;
let host,tree=[],failure,result,token;
const get=async route=>{const response=await fetch(`${origin}/api/${route}`,{signal:AbortSignal.timeout(10000)});assert.ok(response.ok);return response.json();};
try{
  assert.ok(!await portOpen(port),'dedicated resource test port is free');
  await writeFile(path.join(directory,'preferences.json'),JSON.stringify({version:1,intervalMs:1000,ui:{version:1,density:'compact',sidebarCollapsed:false,processView:'flat',taskManagerPromptDismissed:true,pages:{}}}));
  const environment={...process.env,TASKMANAGER_DATA_DIR:directory};
  if(process.env.TASKMANAGER_VERIFY_SELF_CONTAINED==='1')for(const key of Object.keys(environment))if(key.startsWith('DOTNET_ROOT'))delete environment[key];
  host=spawn(path.join(release,'TaskManager.Host.exe'),[`--port=${port}`],{cwd:release,env:environment,windowsHide:true,stdio:'ignore'});
  await until(async()=>(await get('health')).ready,'real resource test Host',45000);
  token=(await get('bootstrap')).token;
  await until(async()=>{const value=await get('snapshot');return value.sampleId>=5&&value.processNetwork.status==='available';},'ETW warmup before thread measurement',45000);
  tree=processTree(host.pid);
  const nativeCreation=new Map(tree.map(entry=>[entry.pid,probe('process-elevation',entry.pid).creationTimeFileTime]));
  async function capture(){
    const value=await get('snapshot');
    const rows=tree.map(entry=>value.processes.find(row=>row.pid===entry.pid&&row.creationTimeFileTime===nativeCreation.get(entry.pid)));
    assert.ok(rows.every(Boolean),'every original application identity is still observed');
    return{capturedAtUtc:value.capturedAtUtc,appThreads:rows.reduce((sum,row)=>sum+row.threadCount,0),hostThreads:rows.find(row=>row.pid===host.pid).threadCount,appHandles:rows.every(row=>row.handleCount!==null)?rows.reduce((sum,row)=>sum+row.handleCount,0):null,networkStatus:value.processNetwork.status,samplingMs:value.samplingDurationMs};
  }
  const samples=[await capture()];
  for(let index=0;index<3;index++){await delay(10000);samples.push(await capture());}
  const after=processTree(host.pid);
  assert.ok(after.length===tree.length&&after.every(entry=>tree.some(original=>original.pid===entry.pid&&original.fileTime===entry.fileTime)),'original whole-application identity set remains stable');
  const identity=probe('process-elevation',host.pid),prefix=`BlueDomain-Network-${host.pid}-${identity.creationTimeFileTime}-`;
  assert.equal((await networkSessions()).filter(name=>name.startsWith(prefix)).length,1);
  const response=await fetch(`${origin}/api/window/close`,{method:'POST',headers:{Origin:origin,'Content-Type':'application/json','X-TaskManager-Token':token},body:'{}'});
  assert.ok(response.ok);
  await until(()=>host.exitCode!==null||host.signalCode!==null,'normal resource test window close',25000);
  await until(async()=>!await portOpen(port),'resource test listener released');
  assert.ok(!(await networkSessions()).some(name=>name.startsWith(prefix))&&tree.every(entry=>!ownedAlive(entry)),'production close releases its trace and every original application identity');
  result={result:'PASS',checkedAtUtc:new Date().toISOString(),durationMs:Date.parse(samples.at(-1).capturedAtUtc)-Date.parse(samples[0].capturedAtUtc),appProcessCount:tree.length,samples,clean:true};
}catch(error){failure=error;result={result:'FAIL',message:error.message};}
finally{
  if(host?.exitCode===null&&host.signalCode===null)await stopOwnedHost(host);
  for(const entry of [...tree].reverse())if(ownedAlive(entry))process.kill(entry.pid);
  await rm(directory,{recursive:true,force:true});await mkdir(output,{recursive:true});
  await writeFile(path.join(output,'resource-trend.json'),JSON.stringify(result,null,2));
}
console.log(JSON.stringify(result,null,2));
if(failure)throw failure;
