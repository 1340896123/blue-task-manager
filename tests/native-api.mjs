import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import http from 'node:http';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const framework = 'net10.0-windows10.0.19041.0';
const hostExe = path.join(root, 'src', 'TaskManager.Host', 'bin', 'Release', framework, 'TaskManager.Host.exe');
const workloadExe = path.join(root, 'tests', 'TaskManager.TestWorkload', 'bin', 'Release', framework, 'TaskManager.TestWorkload.exe');
const port = Number(process.env.TASKMANAGER_TEST_PORT ?? 5191);
const origin = `http://127.0.0.1:${port}`;
const directory = await mkdtemp(path.join(tmpdir(), 'taskmanager-native-'));
const host = spawn(hostExe, ['--headless', `--port=${port}`], { cwd: root, env: { ...process.env, TASKMANAGER_DATA_DIR: path.join(directory, 'preferences') }, windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
let startupFailure = '';
host.stderr.on('data', value => { startupFailure += value; });
host.stdout.on('data', () => {}); // Do not persist native snapshots, tokens or unrelated process listings.
let token;
let target;
let assertions = 0;
const passed = [];
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
const check = (condition, message) => { assert.ok(condition, message); assertions++; };
async function request(route, { method = 'GET', body, headers = {}, status = 200 } = {}) {
  if (headers.Host) {
    const result = await new Promise((resolve, reject) => {
      const request = http.request(origin + '/api/' + route, { method, headers: { Origin: origin, ...headers } }, response => {
        let text = ''; response.setEncoding('utf8'); response.on('data', value => { text += value; }); response.on('end', () => resolve({ status: response.statusCode, value: JSON.parse(text) }));
      });
      request.on('error', reject); request.end();
    });
    assert.equal(result.status, status, 'raw Host boundary'); assertions++;
    return result.value;
  }
  const response = await fetch(origin + '/api/' + route, { method, headers: { Origin: origin, ...(body === undefined ? {} : { 'Content-Type': 'application/json' }), ...headers }, body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(10000) });
  const value = await response.json();
  assert.equal(response.status, status, `${route}: ${response.status}, expected ${status}: ${JSON.stringify(value)}`);
  assertions++;
  return value;
}
const write = (route, body, status = 200, headers = {}) => request(route, { method: 'POST', body, status, headers: { 'X-TaskManager-Token': token, ...headers } });
const alive = pid => { try { process.kill(pid, 0); return true; } catch { return false; } };
async function snapshotWhere(predicate, timeout = 15000) {
  const started = Date.now();
  while (Date.now() - started < timeout) {
    const snapshot = await request('snapshot');
    if (predicate(snapshot)) return snapshot;
    await delay(120);
  }
  throw new Error('Timed out waiting for a real Windows sample.');
}
try {
  let health;
  for (let attempt = 0; attempt < 150; attempt++) {
    if (host.exitCode !== null) throw new Error('Host exited during startup: ' + startupFailure);
    try { health = await request('health'); if (health.ready) break; } catch {}
    await delay(100);
  }
  check(health?.ready && health.platform === 'Windows', 'Windows host ready');
  const bootstrap = await request('bootstrap'); token = bootstrap.token;
  check(typeof token === 'string' && token.length >= 64, 'in-memory token');
  let first = await request('snapshot');
  check(first.processes.some(row => row.pid === health.processId), 'actual host PID present');
  if(first.slowDataCapturedAtUtc===null)check(first.warnings.some(w=>w.code==='loading'),'first slow sources explicitly warm while basic snapshot is ready');
  if (first.sampleId === 1) check(first.system.cpuPercent === null && first.system.unavailable.cpuPercent.code === 'warmingUp', 'first CPU warmup');
  const second = await snapshotWhere(value => value.sampleId > first.sampleId && value.slowDataCapturedAtUtc !== null);
  check(second.services.length > 0 && second.users.length > 0, 'SCM and WTS real rows after actual deferred source read');
  check(second.capturedAtUtc > first.capturedAtUtc && second.system.cpuPercent >= 0 && second.system.cpuPercent <= 100, 'advancing valid sample');
  check(second.processes.every(row => row.networkBytesPerSecond === null && (row.gpuPercent === null ? !!row.unavailable.gpuPercent : row.gpuPercent >= 0 && row.gpuPercent <= 100)), 'network remains unavailable; real GPU is valid or has an explicit reason');
  check(second.processes.every(row => row.ioReadBytesPerSecond === null || row.ioReadBytesPerSecond >= 0), 'I/O never negative');
  passed.push('A02 real snapshots, A03 native PID, A04 warmup and deltas, A08 explicit unavailable network and valid/partial GPU');

  await request('settings', { method: 'POST', body: { intervalMs: 1000, paused: false }, status: 403 });
  await write('settings', { intervalMs: 1000, paused: false }, 403, { 'X-TaskManager-Token': 'wrong' });
  await write('settings', { intervalMs: 1000, paused: false }, 403, { Origin: 'https://example.invalid' });
  await request('health', { status: 403, headers: { Host: '127.0.0.1:1' } });
  await write('settings', { intervalMs: 100, paused: false }, 400);
  passed.push('A12 missing/wrong token, external Origin and wrong Host port refused');

  target = await write('processes/start', { executable: workloadExe, arguments: `"${directory}" 90` });
  check(alive(target.pid), 'test workload independently alive');
  check(target.identity === `${target.pid}:${target.creationTimeFileTime}`, 'exact creation identity');
  let sampled = await snapshotWhere(value => value.processes.some(row => row.identity === target.identity));
  const fresh = sampled.processes.find(row => row.identity === target.identity);
  check(fresh.name === 'TaskManager.TestWorkload.exe' && fresh.cpuPercent === null && fresh.unavailable.cpuPercent.code === 'warmingUp', 'new identity starts at warmup');
  sampled = await snapshotWhere(value => value.processes.some(row => row.identity === target.identity && row.cpuPercent > 0 && row.ioWriteBytesPerSecond > 0));
  const row = sampled.processes.find(row => row.identity === target.identity);
  check(row.canTerminate, 'registered real Run identity remains actionable after later native sampling');
  const independent = spawnSync('powershell.exe', ['-NoProfile', '-Command', `$p=Get-Process -Id ${target.pid}; Write-Output $p.Id; Write-Output $p.ProcessName; Write-Output $p.WorkingSet64`], { windowsHide: true, encoding: 'utf8' });
  check(independent.status === 0, 'independent Get-Process succeeded');
  const independentFields = independent.stdout.trim().split(/\r?\n/);
  check(Number(independentFields[0]) === target.pid && independentFields[1] + '.exe' === row.name, 'native and independent PID/name match');
  check(row.workingSetBytes >= 48 * 1024 * 1024 && Math.abs(Number(independentFields[2]) - row.workingSetBytes) < 32 * 1024 * 1024, 'real allocated working set matches independent query');
  const history = sampled.history.find(value => value.identity === target.identity);
  check(BigInt(history.observedCpuTime100ns) > 0n && BigInt(history.observedIoWriteBytes) > 0n, 'observed session CPU and I/O accumulate');
  await write(`processes/${target.pid}/terminate`, { creationTimeFileTime: String(BigInt(target.creationTimeFileTime) + 1n) }, 409);
  check(alive(target.pid), 'wrong creation time leaves target alive');
  for (const protectedPid of [0, 4, health.processId]) await write(`processes/${protectedPid}/terminate`, { creationTimeFileTime: target.creationTimeFileTime }, 403);
  await write('processes/start', { executable: path.join(directory, 'does-not-exist.exe') }, 400);
  passed.push('A03 independent process/name/memory match, A04 CPU/I/O load, A09 real CreateProcess, A11 identity and protected targets');

  const beforeReset = sampled.sessionStartedAtUtc;
  const reset = await write('history/reset', {});
  check(reset.sessionStartedAtUtc > beforeReset, 'history reset advances session start');
  const afterReset = await request('snapshot');
  check(afterReset.history.length === 0, 'history clear immediate');
  const updated = await write('settings', { intervalMs: 2000, paused: true });
  check(updated.paused && updated.intervalMs === 2000, 'settings applied');
  const paused = await request('snapshot');
  await delay(2200);
  const stillPaused = await request('snapshot');
  check(paused.sampleId === stillPaused.sampleId && stillPaused.paused && stillPaused.intervalMs === 2000, 'pause never fabricates samples');
  await write('settings', { intervalMs: 1000, paused: false });
  await snapshotWhere(value => value.sampleId > stillPaused.sampleId && !value.paused);
  passed.push('A13 session history reset, A14 pause/interval/resume');

  await write(`processes/${target.pid}/terminate`, { creationTimeFileTime: target.creationTimeFileTime });
  for (let attempt = 0; attempt < 40 && alive(target.pid); attempt++) await delay(100);
  check(!alive(target.pid), 'only test workload exited');
  await snapshotWhere(value => !value.processes.some(processRow => processRow.identity === target.identity));
  target = null;
  passed.push('A10 test-owned target terminated and disappeared');
  console.log(JSON.stringify({ result: 'PASS', assertions, passed, platform: process.platform, node: process.version, hostPid: health.processId, firstSampleId: first.sampleId, source: 'production CsWin32 collector; no mocks' }, null, 2));
} finally {
  if (target && alive(target.pid)) {
    // Cleanup still reuses the saved identity; never terminate by process name.
    try { await write(`processes/${target.pid}/terminate`, { creationTimeFileTime: target.creationTimeFileTime }); } catch { process.kill(target.pid); }
  }
  if (host.exitCode === null) host.kill();
  await new Promise(resolve => { if (host.exitCode !== null) resolve(); else { host.once('exit', resolve); setTimeout(resolve, 5000).unref(); } });
  await rm(directory, { recursive: true, force: true });
}
