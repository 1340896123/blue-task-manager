import { stopOwnedHost } from './etw-support.mjs';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdtemp, readFile, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const framework = 'net10.0-windows10.0.19041.0';
const executable = path.join(root, 'src/Takao.Host/bin/Release', framework, 'Takao.Host.exe');
const port = Number(process.env.TASKMANAGER_TEST_PORT ?? 5197), origin = `http://127.0.0.1:${port}`;
const directory = await mkdtemp(path.join(tmpdir(), 'taskmanager-core11-'));
let host, token, assertions = 0;
const checks = [];
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
function check(condition, message) { assert.ok(condition, message); assertions++; checks.push(message); }
async function api(route, body, status = 200) {
  const response = await fetch(origin + '/api/' + route, { method: body === undefined ? 'GET' : 'POST', headers: { Origin: origin, ...(body === undefined ? {} : { 'Content-Type': 'application/json', 'X-TaskManager-Token': token }) }, body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(20000) });
  const value = await response.json(); assert.equal(response.status, status, `${route}: ${JSON.stringify(value)}`); assertions++; return value;
}
async function start() {
  host = spawn(executable, ['--headless', `--port=${port}`], { env: { ...process.env, TASKMANAGER_DATA_DIR: directory }, windowsHide: true, stdio: 'ignore' });
  for (let i = 0; i < 150; i++) { try { if ((await (await fetch(origin + '/api/health')).json()).ready) { token = (await api('bootstrap')).token; return; } } catch {} await delay(100); }
  throw new Error('Host not ready');
}
async function stop() { if (!host || host.exitCode !== null) return; const exited = new Promise(resolve => host.once('exit', resolve)); await stopOwnedHost(host); await exited; }
try {
  await start();
  await api('settings', { intervalMs: 2000, paused: true }); await delay(300);
  let response = await fetch(origin + '/api/snapshot'), etag = response.headers.get('etag'), snapshot = await response.json();
  check(etag?.startsWith('W/"') && snapshot.paused, 'standard weak ETag attached to actual paused representation across negotiated encodings');
  response = await fetch(origin + '/api/snapshot', { headers: { 'If-None-Match': etag } });
  check(response.status === 304 && (await response.text()) === '', 'unchanged representation 304 has no body');
  await api('history/reset', {});
  response = await fetch(origin + '/api/snapshot', { headers: { 'If-None-Match': etag } });
  check(response.status === 200 && response.headers.get('etag') !== etag, 'same-sample history reset changes ETag');
  etag = response.headers.get('etag');
  await api('settings', { intervalMs: 5000, paused: true });
  response = await fetch(origin + '/api/snapshot', { headers: { 'If-None-Match': etag } });
  check(response.status === 200 && response.headers.get('etag') !== etag, 'same-sample interval change changes ETag');
  const pref = { version: 1, density: 'standard', sidebarCollapsed: true, processView: 'tree', pages: { processes: { query: 'core-test', sortKey: 'workingSetBytes', sortDirection: 'descending', visibleColumns: ['name', 'pid', 'workingSetBytes'], filters: { currentUser: true, sessionId: null, state: 'running' }, scrollTop: 128, selectedIdentity: null } } };
  await api('preferences', pref);
  await api('preferences', { ...pref, pages: { processes: { ...pref.pages.processes, visibleColumns: ['name'] } } }, 400);
  const text = await readFile(path.join(directory, 'preferences.json'), 'utf8');
  check(!text.includes(token) && !text.includes('sampleId') && !text.includes('paused'), 'allowlist persistence excludes token, snapshots and paused state');
  const beforeRestartEtag = response.headers.get('etag');
  await stop(); await start();
  check((await api('preferences')).preferences.pages.processes.query === 'core-test', 'UI preferences survive host restart');
  const bootstrap = await api('bootstrap');
  check(bootstrap.settings.intervalMs === 5000 && !bootstrap.settings.paused, 'interval persists while paused state resets');
  response = await fetch(origin + '/api/snapshot', { headers: { 'If-None-Match': beforeRestartEtag } });
  check(response.status === 200 && response.headers.get('etag') !== beforeRestartEtag, 'instance nonce prevents stale ETag reuse across restart');
  snapshot = await response.json();
  const self = snapshot.processes.find(p => p.pid === (host.pid));
  check(self?.privateBytes > 0 && self?.handleCount > 0 && self?.workingSetBytes > 0, 'limited-query process handle reads working set, private commit and handle count');
  check(snapshot.system.commitBytes > 0 && snapshot.system.commitLimitBytes >= snapshot.system.commitBytes && snapshot.system.threadCount > 0 && snapshot.system.handleCount > 0, 'GetPerformanceInfo provides real system counters');
  check(Number.isFinite(snapshot.samplingDurationMs) && snapshot.samplingDurationMs > 0, 'sample duration exposes actual monotonic measurement');
  await stop(); await writeFile(path.join(directory, 'preferences.json'), '{broken'); await start();
  check((await api('preferences')).warning?.code === 'preferencesRecovered', 'corrupt preference file recovers with visible reason');
  check((await api('bootstrap')).settings.intervalMs === 1000, 'corrupt preferences restore default interval');
  console.log(JSON.stringify({ result: 'PASS', assertions, checks, source: 'real CsWin32 host; isolated preference directory; no mocks' }, null, 2));
} finally { await stop(); await rm(directory, { recursive: true, force: true }); }
