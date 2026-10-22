import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir, availableParallelism } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const framework = 'net10.0-windows10.0.19041.0';
const executable = path.join(root, 'src/TaskManager.Host/bin/Release', framework, 'TaskManager.Host.exe');
const workload = path.join(root, 'tests/TaskManager.TestWorkload/bin/Release', framework, 'TaskManager.TestWorkload.exe');
const probe = path.join(root, 'tests/TaskManager.WindowProbe/bin/Release', framework, 'TaskManager.WindowProbe.exe');
const port = Number(process.env.TASKMANAGER_TEST_PORT ?? 5199), origin = `http://127.0.0.1:${port}`;
const directory = await mkdtemp(path.join(tmpdir(), 'taskmanager-telemetry-'));
const started = performance.now();
const host = spawn(executable, ['--headless', `--port=${port}`], { env: { ...process.env, TASKMANAGER_DATA_DIR: path.join(directory, 'preferences') }, windowsHide: true, stdio: 'ignore' });
let token, target, assertions = 0;
const checks = [];
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
function check(value, message) { assert.ok(value, message); assertions++; checks.push(message); }
async function api(route, body) {
  const response = await fetch(origin + '/api/' + route, { method: body === undefined ? 'GET' : 'POST', headers: { Origin: origin, ...(body === undefined ? {} : { 'Content-Type': 'application/json', 'X-TaskManager-Token': token }) }, body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(20000) });
  assert.equal(response.status, 200, route + ': ' + response.status); return response.json();
}
async function until(predicate, name, duration = 20000) {
  const begin = performance.now();
  while (performance.now() - begin < duration) { const value = await api('snapshot'); if (predicate(value)) return value; await delay(150); }
  throw new Error('Timed out: ' + name);
}
function native(...args) { const result = spawnSync(probe, args, { windowsHide: true, encoding: 'utf8' }); assert.equal(result.status, 0, result.stderr); return JSON.parse(result.stdout); }
const percentile = (values, fraction) => [...values].sort((a,b) => a-b)[Math.min(values.length-1, Math.ceil(values.length*fraction)-1)];
try {
  for (let i = 0; i < 150; i++) { try { if ((await api('health')).ready) break; } catch {} await delay(100); }
  const startupMs = performance.now() - started;
  const first = await api('snapshot');
  token = (await api('bootstrap')).token;
  check(first.sampleId >= 1 && first.processes.length > 0, 'basic snapshot ready while deferred PDH initializes');
  check(first.telemetry.sampleId === 0 || first.telemetry.capturedAtUtc !== null, 'PDH timeline is explicit and independent of the basic snapshot');
  let sample = await until(s => s.telemetry.sampleId >= 3 && s.processes.some(p => Number.isFinite(p.gpuPercent)) && s.gpuAdapters.length > 0 && s.gpuAdapters.every(g => Number.isFinite(g.utilizationPercent)) && s.physicalDisks.length > 0 && s.physicalDisks.every(d => Number.isFinite(d.activePercent) && Number.isFinite(d.readBytesPerSecond) && Number.isFinite(d.writeBytesPerSecond)), 'real GPU and PhysicalDisk warmup');
  check(sample.telemetry.gpuInstanceCount > 0 && sample.gpuAdapters.length > 0 && sample.gpuAdapters.length < sample.telemetry.gpuInstanceCount, 'real GPU wildcard instances reduce to adapters instead of full engine payload');
  check(sample.processes.every(p => p.gpuPercent === null ? !!p.unavailable.gpuPercent : p.gpuPercent >= 0 && p.gpuPercent <= 100 && !!p.gpuEngine && !!p.gpuCapturedAtUtc), 'process GPU has valid source, engine label and timestamp or an explicit unavailable reason');
  check(sample.processes.some(p => p.gpuPercent === null && p.unavailable.gpuPercent.code === 'noObservedEngine'), 'a PID with no observed GPU engine is unknown instead of a synthetic zero');
  check(sample.processes.every(p => p.networkBytesPerSecond === null), 'GPU and connections do not invent per-process network throughput');
  check(sample.gpuAdapters.every(g => Number.isFinite(g.utilizationPercent) && g.utilizationPercent >= 0 && g.utilizationPercent <= 100), 'adapter GPU is a valid busiest-engine percentage');
  check(sample.system.gpuPercent === Math.max(...sample.gpuAdapters.map(g => g.utilizationPercent)), 'system GPU selects the busiest adapter engine including software adapters');
  check(sample.physicalDisks.length > 0 && sample.physicalDisks.every(d => !d.isTotal && d.id !== '_Total' && Number.isFinite(d.activePercent) && d.activePercent >= 0 && d.activePercent <= 100 && Number.isFinite(d.readBytesPerSecond) && d.readBytesPerSecond >= 0 && Number.isFinite(d.writeBytesPerSecond) && d.writeBytesPerSecond >= 0), 'physical disk real instances exclude _Total and expose real valid rates/activity');
  check(sample.system.diskActivePercent === Math.max(...sample.physicalDisks.map(d => d.activePercent)), 'system disk activity is the busiest physical disk, not summed percentages');
  check(sample.system.diskReadBytesPerSecond === sample.physicalDisks.reduce((sum,d) => sum+d.readBytesPerSecond,0) && sample.system.diskWriteBytesPerSecond === sample.physicalDisks.reduce((sum,d) => sum+d.writeBytesPerSecond,0), 'system disk byte rates sum physical instances once');
  check(sample.networkAdapters.length > 0 && sample.networkAdapters.every(a => /^\d+$/.test(a.id) && typeof a.alias === 'string' && typeof a.description === 'string' && /^\d+$/.test(a.receivedBytes) && /^\d+$/.test(a.receiveLinkSpeedBitsPerSecond)), 'per-adapter LUID, alias, counters and link speeds preserve UInt64 strings');
  const independent = native('network-counters');
  const next = await until(s => s.sampleId > sample.sampleId, 'next real network sample');
  check(independent.some(a => { const before = sample.networkAdapters.find(v => v.id === a.id), after = next.networkAdapters.find(v => v.id === a.id); return before && after && before.alias === a.alias && before.isHardware === a.hardware && before.receiveLinkSpeedBitsPerSecond === a.receiveLinkSpeed && BigInt(before.receivedBytes) <= BigInt(a.receivedBytes) && BigInt(a.receivedBytes) <= BigInt(after.receivedBytes); }), 'independent CsWin32 probe matches adapter identity, hardware, link and monotonic raw byte counters');
  check(sample.networkAdapters.every(a => a.receiveBytesPerSecond === null ? !!a.unavailable.receiveBytesPerSecond : a.receiveBytesPerSecond >= 0), 'disconnected, warming and reset adapter rates have reasons, never a fallback zero');
  const representative = sample.processes.find(p => p.gpuPercent !== null && p.creationTimeFileTime);
  const details = await api(`processes/${representative.pid}/details?creationTimeFileTime=${representative.creationTimeFileTime}`);
  check(details.process.gpuPercent !== null && details.process.gpuEngine && details.process.gpuCapturedAtUtc && !details.process.unavailable.gpuPercent, 'on-demand process details preserve real sampled GPU and its independent timestamp');

  // A separate Windows performance consumer validates provider instances and values, never supplies production data.
  const counterScript = path.join(directory, 'counter-probe.ps1');
  await writeFile(counterScript, `$ErrorActionPreference='Stop'\n$samples=@(Get-Counter -Counter '\\GPU Engine(*)\\Utilization Percentage','\\PhysicalDisk(*)\\Disk Read Bytes/sec','\\PhysicalDisk(*)\\Disk Write Bytes/sec','\\PhysicalDisk(*)\\% Idle Time' -SampleInterval 1 -MaxSamples 2 -ErrorAction SilentlyContinue)\nif($samples.Count -ne 2){throw 'Independent counter samples missing'}\n$valid=@($samples[-1].CounterSamples | Where-Object {$_.Status -eq 0})\n[pscustomobject]@{gpuInstances=@($valid|Where-Object {$_.Path -like '*GPU Engine*'}).Count;diskInstances=@($valid|Where-Object {$_.Path -like '*PhysicalDisk*'}|ForEach-Object {$_.InstanceName}|Sort-Object -Unique);gpuMax=($valid|Where-Object {$_.Path -like '*GPU Engine*'}|Measure-Object CookedValue -Maximum).Maximum} | ConvertTo-Json -Compress\n`);
  const counters = spawnSync('powershell.exe', ['-NoProfile', '-File', counterScript], { encoding: 'utf8', windowsHide: true, timeout: 45000 });
  assert.equal(counters.status, 0, String(counters.error ?? '') + counters.stderr); const source = JSON.parse(counters.stdout.trim());
  check(source.gpuInstances > 0 && Number.isFinite(source.gpuMax) && source.gpuMax >= 0 && sample.telemetry.gpuInstanceCount >= source.gpuInstances * .5 && sample.telemetry.gpuInstanceCount <= source.gpuInstances * 2, 'independent Get-Counter confirms GPU provider and instance scale');
  check(sample.physicalDisks.every(d => source.diskInstances.includes(d.name.toLowerCase())), 'independent Get-Counter confirms actual PhysicalDisk instance names');

  target = await api('processes/start', { executable: workload, arguments: `"${path.join(directory,'io')}" 90` });
  const observed = await until(s => s.processes.some(p => p.identity === target.identity && p.ioWriteBytesPerSecond > 0) && s.physicalDisks.some(d => d.writeBytesPerSecond > 0), 'test-owned WriteThrough workload and real physical disk traffic');
  check(observed.processes.find(p => p.identity === target.identity).gpuPercent === null, 'non-GPU test workload does not receive a made-up zero GPU');
  const self = observed.processes.find(p => p.pid === host.pid);
  const before = native('process-resources', String(host.pid), self.creationTimeFileTime);
  const measureStarted = performance.now(), records = [], pdh = new Map();
  let bytes = 0, repeat304 = 0, lastSnapshotBytes = 0, maxSnapshotBytes = 0;
  while (performance.now() - measureStarted < 30000) {
    const response = await fetch(origin + '/api/snapshot'); const etag = response.headers.get('etag'); const text = await response.text(); lastSnapshotBytes = Buffer.byteLength(text); maxSnapshotBytes = Math.max(maxSnapshotBytes,lastSnapshotBytes); bytes += lastSnapshotBytes; const s = JSON.parse(text);
    if (!records.some(r => r.id === s.sampleId)) records.push({id:s.sampleId,duration:s.samplingDurationMs,count:s.processes.length});
    if (s.telemetry.sampleId > 0) pdh.set(s.telemetry.sampleId, s.telemetry);
    const unchanged = await fetch(origin + '/api/snapshot', {headers:{'If-None-Match':etag}});
    if (unchanged.status === 304 && await unchanged.text() === '') repeat304++;
    else await unchanged.arrayBuffer();
    sample = s; await delay(500);
  }
  const elapsedMs = performance.now() - measureStarted;
  const after = native('process-resources', String(host.pid), self.creationTimeFileTime);
  const cpuMs = Number(BigInt(after.cpuTime100ns)-BigInt(before.cpuTime100ns))/10000;
  check(records.length >= 20 && pdh.size >= 20 && pdh.size <= records.length+2, '30-second basic and deferred PDH samples remain serial with bounded cadence');
  check(sample.performanceHistory.length <= 120 && sample.performanceHistory.some(h => h.telemetryCapturedAtUtc && h.physicalDisks.length && h.gpuAdapters.length && h.networkAdapters.length), 'bounded real resource history keeps separate PDH timestamps and adapter IDs');
  check(repeat304 > 20, 'conditional GET repeatedly avoids retransmitting the full resource history');
  check(after.handles < before.handles + 128, 'query and array/native resource handles remain bounded during sustained sampling');
  const pauseStarted = performance.now(); await api('settings', {intervalMs:1000,paused:true});
  check(performance.now()-pauseStarted < 1000, 'settings stays responsive without waiting for the PDH worker');
  await delay(1300); const paused = await api('snapshot'); await delay(2200); const stillPaused = await api('snapshot');
  check(paused.sampleId === stillPaused.sampleId && paused.telemetry.sampleId === stillPaused.telemetry.sampleId, 'paused sampling does not keep publishing PDH/background history');
  await api('settings', {intervalMs:2000,paused:false});
  await until(s => s.sampleId > stillPaused.sampleId && s.telemetry.sampleId > stillPaused.telemetry.sampleId, 'PDH follows resumed interval');
  const report = { result:'PASS', assertions, checks, source:'production CsWin32 PDH/GetIfTable2; independent Get-Counter and native probe; no mocks', performance:{scope:'headless host with real WriteThrough test workload; 500ms reads; no renderer',startupMs,measurementMs:elapsedMs,logicalProcessors:availableParallelism(),processCount:{min:Math.min(...records.map(r=>r.count)),max:Math.max(...records.map(r=>r.count))},samples:records.length,pdhSamples:pdh.size,samplingMs:{p50:percentile(records.map(r=>r.duration),.5),p95:percentile(records.map(r=>r.duration),.95)},pdhCollectionMs:{p50:percentile([...pdh.values()].map(r=>r.collectionDurationMs).filter(Number.isFinite),.5),p95:percentile([...pdh.values()].map(r=>r.collectionDurationMs).filter(Number.isFinite),.95)},pdhQueryMs:{p50:percentile([...pdh.values()].map(r=>r.queryDurationMs).filter(Number.isFinite),.5),p95:percentile([...pdh.values()].map(r=>r.queryDurationMs).filter(Number.isFinite),.95)},pdhInitializationMs:sample.telemetry.initializationDurationMs,cpuOneCorePercent:cpuMs/elapsedMs*100,cpuMachinePercent:cpuMs/elapsedMs*100/availableParallelism(),privateBytes:after.privateBytes,workingSetBytes:after.workingSetBytes,handles:{before:before.handles,after:after.handles},totalSnapshotBytes:bytes,lastSnapshotBytes,maxSnapshotBytes,conditional304:repeat304,gpuInstanceCount:sample.telemetry.gpuInstanceCount,gpuAdapters:sample.gpuAdapters.length,physicalDisks:sample.physicalDisks.length,networkAdapters:sample.networkAdapters.length} };
  if (process.env.TASKMANAGER_TELEMETRY_REPORT) await writeFile(path.resolve(root,process.env.TASKMANAGER_TELEMETRY_REPORT),JSON.stringify(report,null,2)+'\n');
  console.log(JSON.stringify(report,null,2));
} finally {
  if (target) try { await api(`processes/${target.pid}/terminate`, {creationTimeFileTime:target.creationTimeFileTime}); } catch {}
  if (host.exitCode === null) { const exit = new Promise(resolve=>host.once('exit',resolve)); host.kill(); await exit; }
  await rm(directory,{recursive:true,force:true});
}
