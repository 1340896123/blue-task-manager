import { spawn } from 'node:child_process';
import { mkdir, mkdtemp, writeFile, rm } from 'node:fs/promises';
import { tmpdir, availableParallelism } from 'node:os';
import path from 'node:path';
import http from 'node:http';
import { gunzipSync } from 'node:zlib';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';
import { until, delay, processTree, ownedAlive, portOpen, check } from './e2e-support.mjs';
import { probe } from './native-ui-support.mjs';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const port = 5196, cdp = 9226, origin = `http://127.0.0.1:${port}`;
if (await portOpen(port) || await portOpen(cdp)) throw new Error('Performance ports in use');
const temporary = await mkdtemp(path.join(tmpdir(), 'blue-performance-'));
const host = spawn(path.join(root, 'output/TaskManager-win-x64/TaskManager.Host.exe'), [`--port=${port}`], { cwd: tmpdir(), env: { ...process.env, TASKMANAGER_DATA_DIR: temporary, TASKMANAGER_CDP_PORT: String(cdp) }, windowsHide: true, stdio: 'ignore' });
const started = performance.now(); let browser, page, tree = [], last;
const percentile = (values, q) => [...values].sort((a,b) => a-b)[Math.min(values.length-1,Math.floor(values.length*q))];
function resources(entries) { return entries.map(p => ({ pid: p.pid, ...probe('process-resources', p.pid, p.fileTime) })); }
function summary(rows) { return { privateBytes: rows.reduce((n,r)=>n+r.privateBytes,0), workingSetBytes: rows.reduce((n,r)=>n+r.workingSetBytes,0), handles: rows.reduce((n,r)=>n+r.handles,0) }; }
async function wire(encoding, etag) { const start = performance.now(); return new Promise((resolve,reject) => { const request = http.get(origin+'/api/snapshot', { headers: { 'Accept-Encoding': encoding, ...(etag?{'If-None-Match':etag}:{}) } }, response => { const buffers=[]; response.on('data',b=>buffers.push(b)); response.on('end',()=>{const bytes=Buffer.concat(buffers); const decoded=response.headers['content-encoding']==='gzip'?gunzipSync(bytes):bytes; resolve({status:response.statusCode,etag:response.headers.etag,encoding:response.headers['content-encoding']??'identity',wireBytes:bytes.length,decodedBytes:decoded.length,latencyMs:performance.now()-start, decoded});}); }); request.on('error',reject); }); }
try {
  await until(async () => (await (await fetch(origin+'/api/health')).json()).ready && await portOpen(cdp), 'release desktop ready', 45000); const startupMs = performance.now()-started;
  browser = await chromium.connectOverCDP(`http://127.0.0.1:${cdp}`, { noDefaults: true }); await until(()=>browser.contexts()[0]?.pages().length,'desktop page'); page=browser.contexts()[0].pages()[0];
  await page.getByText('已连接 · 实时采样',{exact:true}).waitFor(); await page.keyboard.press('Control+0');
  await until(async()=>{last=await (await fetch(origin+'/api/snapshot')).json(); return last.performanceHistory.length===120;},'fill120 actual one-second history points',160000);
  check(last.performanceHistory.length===120,'history fully warmed'); console.log('120 real history points warmed');
  const measurements=[], pdh=new Map(), httpCounts={full200:0,unchanged304:0}, errors=[]; let measuring=true;
  page.on('pageerror',e=>errors.push(e.message));
  page.on('response',async response=>{
    if(!measuring || response.url()!==origin+'/api/snapshot')return;
    if(response.status()===304){httpCounts.unchanged304++;return;}
    if(response.status()!==200)return;
    try { const s=await response.json(); if(!measuring)return; httpCounts.full200++;last=s;measurements.push({sampleId:s.sampleId,processCount:s.processes.length,samplingMs:s.samplingDurationMs,history:s.performanceHistory.length});pdh.set(s.telemetry.sampleId,s.telemetry); } catch{}
  });
  tree=processTree(host.pid); const before=resources(tree); const measureStarted=performance.now();
  await delay(32000);
  const after=resources(tree); const elapsedMs=performance.now()-measureStarted;measuring=false;
  check(measurements.length>=20 && measurements.every(s=>s.history===120),'real full-history steady-state measure');
  const cpu=(a,b)=>Number(BigInt(b.cpuTime100ns)-BigInt(a.cpuTime100ns))/10000/elapsedMs*100;
  const appCpu=after.reduce((n,r)=>n+cpu(before.find(b=>b.pid===r.pid),r),0); const hostCpu=cpu(before.find(b=>b.pid===host.pid),after.find(b=>b.pid===host.pid));
  const nativeMetrics=await page.evaluate(()=>({jsHeapBytes:performance.memory?.usedJSHeapSize??null,totalJsHeapBytes:performance.memory?.totalJSHeapSize??null,domNodes:document.querySelectorAll('*').length,mountedRows:document.querySelectorAll('tbody tr[data-row-key]').length,logicalViewport:{width:innerWidth,height:innerHeight},dpr:devicePixelRatio}));
  const interactions=[];const search=page.getByRole('searchbox');
  for(let i=0;i<10;i++){const t=await page.evaluate(()=>performance.now());await search.fill(i%2?'':'TaskManager');await page.evaluate(()=>new Promise(r=>requestAnimationFrame(()=>r())));interactions.push(await page.evaluate(t=>performance.now()-t,t));}
  await search.fill(''); const payloads=[];
  for(let i=0;i<12;i++) { const response=await wire(i%2?'gzip':'identity');payloads.push({encoding:response.encoding,wireBytes:response.wireBytes,decodedBytes:response.decodedBytes,latencyMs:response.latencyMs});const repeat=await wire('gzip',response.etag);check(repeat.status===304&&repeat.wireBytes===0,'actual weak ETag304 empty wire body'); }
  check(nativeMetrics.mountedRows<100 && nativeMetrics.mountedRows<last.processes.length,'full-history app keeps process rows virtualized');check(errors.length===0,'performance run renderer errors0');
  const budgetResults={hostMachineCpuPercent:{target:1,actual:hostCpu/availableParallelism()},samplingP95Ms:{target:500,actual:percentile(measurements.map(s=>s.samplingMs),.95)},interactionP95Ms:{target:100,actual:percentile(interactions,.95)}}; const targetsMet=Object.values(budgetResults).every(v=>v.actual<=v.target);
  const report={result:targetsMet?'PASS':'TARGETS_EXCEEDED',budgetResults,checkedAtUtc:new Date().toISOString(),scope:'production portable Electron + Host + all exact descendant identities, visible default flat processes; no test workload descendants; 120 real history points; resource window only normal UI polls (wire benchmark follows)',startupMs,warmupMs:measureStarted-started,measurementMs:elapsedMs,logicalProcessors:availableParallelism(),appProcessCount:tree.length,processCount:{min:Math.min(...measurements.map(s=>s.processCount)),max:Math.max(...measurements.map(s=>s.processCount))},samples:measurements.length,pdhSamples:pdh.size,samplingMs:{p50:percentile(measurements.map(s=>s.samplingMs),.5),p95:percentile(measurements.map(s=>s.samplingMs),.95),excludes:'HTTP serialization/transfer'},pdhCollectionMs:{p50:percentile([...pdh.values()].map(s=>s.collectionDurationMs),.5),p95:percentile([...pdh.values()].map(s=>s.collectionDurationMs),.95)},pdhQueryMs:{p50:percentile([...pdh.values()].map(s=>s.queryDurationMs),.5),p95:percentile([...pdh.values()].map(s=>s.queryDurationMs),.95)},cpu:{hostOneCorePercent:hostCpu,hostMachinePercent:hostCpu/availableParallelism(),wholeAppOneCorePercent:appCpu,wholeAppMachinePercent:appCpu/availableParallelism()},resources:{before:summary(before),after:summary(after),host:after.find(p=>p.pid===host.pid),semantics:'privateBytes sums private committed memory; workingSet sum includes shared pages multiple times; handles sum is process handles'},renderer:nativeMetrics,interactionMs:{method:'actual search fill through Playwright to next painted animation frame, includes automation protocol',p50:percentile(interactions,.5),p95:percentile(interactions,.95),samples:interactions.length},http:{uiResponses:httpCounts,benchmarkSamples:payloads.length,latencyMs:{p50:percentile(payloads.map(s=>s.latencyMs),.5),p95:percentile(payloads.map(s=>s.latencyMs),.95)},representations:payloads,conditional304:{requests:12,wireBodyBytes:0}},boundaries:{serviceSuccess:'SCM create permission denied5; no user services changed',processNetwork:'not collected; TCP/UDP endpoints are not throughput'},cleanup:'native close verified below; no token/full machine process list saved'};
  // Do not archive per-app PIDs/CPU FILETIME counters; only aggregate resource fields.
  delete report.resources.host.pid; delete report.resources.host.cpuTime100ns;
  const output=path.join(root,'output/performance');await mkdir(output,{recursive:true});await writeFile(path.join(output,'desktop-v11.json'),JSON.stringify(report,null,2));
  await page.getByRole('button',{name:'关闭窗口',exact:true}).click(); await until(()=>host.exitCode!==null,'performance native close',20000); await until(async()=>!await portOpen(port)&&!await portOpen(cdp),'performance ports released');check(tree.every(p=>!ownedAlive(p)),'all performance app identities cleaned'); console.log(JSON.stringify(report,null,2)); if(!targetsMet)process.exitCode=1;
} finally { if(host.exitCode===null)for(const p of processTree(host.pid).reverse())if(ownedAlive(p)){try{process.kill(p.pid);}catch{}} await browser?.close().catch(()=>{});await delay(400);await rm(temporary,{recursive:true,force:true}); }
