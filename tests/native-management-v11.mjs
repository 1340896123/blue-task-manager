import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..'), framework = 'net10.0-windows10.0.19041.0';
const hostExe = path.join(root, 'src/TaskManager.Host/bin/Release', framework, 'TaskManager.Host.exe');
const workloadExe = path.join(root, 'tests/TaskManager.TestWorkload/bin/Release', framework, 'TaskManager.TestWorkload.exe');
const probeExe = path.join(root, 'tests/TaskManager.WindowProbe/bin/Release', framework, 'TaskManager.WindowProbe.exe');
const directory = await mkdtemp(path.join(tmpdir(), 'taskmanager-management-'));
const port = Number(process.env.TASKMANAGER_TEST_PORT ?? 5198), origin = `http://127.0.0.1:${port}`;
const host = spawn(hostExe, ['--headless', `--port=${port}`], { env: { ...process.env, TASKMANAGER_DATA_DIR: path.join(directory, 'preferences') }, windowsHide: true, stdio: 'ignore' });
const owned = new Map(); let token, assertions = 0; const checks = [];
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
function check(value, message) { assert.ok(value, message); assertions++; checks.push(message); }
async function api(route, body, status = 200) {
  const response = await fetch(origin + '/api/' + route, { method: body === undefined ? 'GET' : 'POST', headers: { Origin: origin, ...(body === undefined ? {} : { 'Content-Type': 'application/json', 'X-TaskManager-Token': token }) }, body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(20000) });
  const value = await response.json(); assert.equal(response.status, status, `${route}: ${JSON.stringify(value)}`); assertions++; return value;
}
async function until(callback, text) { for (let i = 0; i < 120; i++) { try { const value = await callback(); if (value) return value; } catch {} await delay(100); } throw new Error('Timed out: ' + text); }
function probe(...args) { const result = spawnSync(probeExe, args, { windowsHide: true, encoding: 'utf8' }); assert.equal(result.status, 0, result.stderr); assertions++; return JSON.parse(result.stdout); }
function alive(pid) { try { process.kill(pid, 0); return true; } catch { return false; } }
try {
  await until(async () => (await (await fetch(origin + '/api/health')).json()).ready, 'host');
  const bootstrap = await api('bootstrap'); token = bootstrap.token;
  check(bootstrap.hostPid === host.pid && typeof bootstrap.currentUserName === 'string' && typeof bootstrap.currentSessionId === 'number', 'bootstrap identifies the actual host account and session');
  const target = await api('processes/start', { executable: workloadExe, arguments: `"${path.join(directory, 'tree')}" 180 tree` }); owned.set(target.pid, target.creationTimeFileTime);
  const endpoints = await until(async () => Object.fromEntries((await readFile(path.join(directory, 'tree/endpoints.txt'), 'utf8')).trim().split(/\r?\n/).map(line => line.split('=').map((value, i) => i ? Number(value) : value))), 'network endpoints');
  const childPids = await until(async () => { const rows = (await readFile(path.join(directory, 'tree/children.txt'), 'utf8')).trim().split(/\r?\n/).map(row => row.split(':')); if (rows.length !== 2) return false; for (const [pid,time] of rows) owned.set(Number(pid), time); return rows.map(([pid]) => Number(pid)); }, 'children');
  let preview = await api(`processes/${target.pid}/tree-preview`, { creationTimeFileTime: target.creationTimeFileTime });
  for (const child of preview.members) owned.set(child.pid, child.creationTimeFileTime);
  check(preview.members.filter(m => m.name === 'TaskManager.TestWorkload.exe').length === 3 && preview.canTerminate && preview.members.every(m => m.creationTimeFileTime && m.canTerminate), 'real parent, two children and their actual console hosts appear in safe tree preview');
  check(childPids.every(pid => preview.members.some(m => m.pid === pid && m.parentIdentity === target.identity)), 'tree preview matches independent test-created child PIDs');
  const detailsRoute = `processes/${target.pid}/details?creationTimeFileTime=${target.creationTimeFileTime}`;
  const details = await api(detailsRoute);
  check(details.process.identity === target.identity && details.process.path === workloadExe && details.handleCount > 0 && details.privateBytes >= 48 * 1024 * 1024 && details.uptimeSeconds >= 0, 'on-demand real details include file, handles, private commit and lifetime');
  await api(`processes/${target.pid}/details?creationTimeFileTime=${BigInt(target.creationTimeFileTime) + 1n}`, undefined, 409);
  const connections = await api(`processes/${target.pid}/connections?creationTimeFileTime=${target.creationTimeFileTime}`);
  for (const [key, port] of Object.entries(endpoints)) {
    if (!port) continue;
    const row = connections.connections.find(c => c.protocol === key.slice(0, 3) && c.addressFamily === `IPv${key[3]}` && c.localPort === port);
    check(row && row.pid === target.pid && (key.startsWith('TCP') || row.remotePort === null && row.remoteAddress === null), 'IP Helper resolves actual ' + key + ' socket and network-byte-order port');
  }
  check(!connections.truncated && Object.keys(connections.unavailable).length === 0, 'all four true endpoint sources read successfully');
  const icon = await fetch(origin + `/api/processes/${target.pid}/icon?creationTimeFileTime=${target.creationTimeFileTime}`);
  const png = Buffer.from(await icon.arrayBuffer());
  check(icon.status === 200 && icon.headers.get('content-type') === 'image/png' && png.subarray(0, 8).equals(Buffer.from([137,80,78,71,13,10,26,10])), 'SHGetFileInfo returns real PNG without embedding it into snapshots');
  const original = probe('process-state', String(target.pid), target.creationTimeFileTime);
  for (const [priority, number] of [['idle', 0x40], ['belowNormal', 0x4000], ['normal', 0x20], ['aboveNormal', 0x8000], ['high', 0x80]]) {
    const result = await api(`processes/${target.pid}/priority`, { creationTimeFileTime: target.creationTimeFileTime, priority });
    check(result.priority === priority && probe('process-state', String(target.pid), target.creationTimeFileTime).priority === number, 'priority ' + priority + ' read back independently');
  }
  await api(`processes/${target.pid}/priority`, { creationTimeFileTime: target.creationTimeFileTime, priority: 'normal' });
  await api(`processes/${target.pid}/priority`, { creationTimeFileTime: target.creationTimeFileTime, priority: 'realtime' }, 400);
  await api(`processes/${target.pid}/priority`, { creationTimeFileTime: String(BigInt(target.creationTimeFileTime) + 1n), priority: 'idle' }, 409);
  await api(`processes/${host.pid}/priority`, { creationTimeFileTime: target.creationTimeFileTime, priority: 'idle' }, 403);
  const efficiency = await api(`processes/${target.pid}/efficiency?creationTimeFileTime=${target.creationTimeFileTime}`);
  if (efficiency.available) {
    const enabled = await api(`processes/${target.pid}/efficiency`, { creationTimeFileTime: target.creationTimeFileTime, enabled: true });
    const actual = probe('process-state', String(target.pid), target.creationTimeFileTime);
    check(enabled.enabled && enabled.canRestore && actual.priority === 0x40 && (actual.controlMask & actual.stateMask & 1) !== 0, 'efficiency mode reads back actual Windows execution throttling and idle priority');
    await api(`processes/${target.pid}/priority`, { creationTimeFileTime: target.creationTimeFileTime, priority: 'normal' }, 409);
    const restored = await api(`processes/${target.pid}/efficiency`, { creationTimeFileTime: target.creationTimeFileTime, enabled: false });
    const actualRestored = probe('process-state', String(target.pid), target.creationTimeFileTime);
    check(!restored.canRestore && actualRestored.priority === 0x20 && actualRestored.controlMask === original.controlMask && actualRestored.stateMask === original.stateMask, 'efficiency mode restores saved power-throttling and priority state');
  } else check(!!efficiency.reason, 'unsupported efficiency mode gives native capability reason');
  const members = preview.members.map(({ pid, creationTimeFileTime }) => ({ pid, creationTimeFileTime }));
  await api(`processes/${target.pid}/terminate-tree`, { creationTimeFileTime: target.creationTimeFileTime, members: members.slice(1) }, 409);
  await api(`processes/${target.pid}/terminate-tree`, { creationTimeFileTime: target.creationTimeFileTime, members: members.map((m,i) => i ? m : { ...m, creationTimeFileTime: String(BigInt(m.creationTimeFileTime) + 1n) }) }, 409);
  check([...owned.keys()].every(alive), 'tree member or identity mismatch begins no termination');
  await api('desktop/browse-executable', {}, 503);
  await api('desktop/open-system-location', { location: 'file:///arbitrary' }, 400);
  await api(`processes/${target.pid}/file-action`, { creationTimeFileTime: target.creationTimeFileTime, action: 'run' }, 400);
  await api(`processes/${target.pid}/file-action`, { creationTimeFileTime: String(BigInt(target.creationTimeFileTime) + 1n), action: 'properties' }, 409);
  await api('startup/open-location', { id: 'missing-not-a-real-source' }, 404);
  const createPermission = probe('service-create-permission');
  check(createPermission.available ? createPermission.nativeErrorCode === 0 : createPermission.nativeErrorCode === 5, 'SCM create-service permission reflects the actual elevated or restricted token');
  const snapshot = await api('snapshot'); let forbiddenService;
  for (const service of snapshot.services) {
    const detail = await api(`services/${encodeURIComponent(service.name)}/details`);
    if (detail.unavailable.stop?.code === 'accessDenied') { forbiddenService = detail; break; }
  }
  if (forbiddenService) {
  check(typeof forbiddenService.binaryPath === 'string', 'SCM service details expose configuration and real action-permission limits');
  // A deliberately impossible expected PID also prevents mutation if ACLs change after the probe.
  await api(`services/${encodeURIComponent(forbiddenService.name)}/control`, { command: 'stop', expectedState: forbiddenService.state, expectedPid: 4294967295 }, 403);
  const after = await api(`services/${encodeURIComponent(forbiddenService.name)}/details`);
  check(after.state === forbiddenService.state && after.pid === forbiddenService.pid, 'permission rejection leaves user service state unchanged');
  } else check(createPermission.available, 'elevated SCM query exposes no stop-denied service; no user service action is attempted');
  await api('services/not-a-real-service-management-test/control', { command: 'start', expectedState: 'SERVICE_STOPPED', expectedPid: 0 }, 404);
  const terminated = await api(`processes/${target.pid}/terminate-tree`, { creationTimeFileTime: target.creationTimeFileTime, members });
  check(terminated.completed && terminated.results.length === preview.members.length && terminated.results.at(-1).pid === target.pid, 'validated complete tree terminates leaf-to-root with actual exit confirmation');
  await until(async () => [...owned.keys()].every(pid => !alive(pid)), 'test tree exits');
  await api(detailsRoute, undefined, 404);
  owned.clear();
  console.log(JSON.stringify({ result: 'PASS', assertions, checks, source: 'real Win32 / independent native probe; no mocks', serviceSuccessChain: createPermission.available ? 'not executed: no dedicated test service provisioned; no user services changed' : 'not executed: SCM CREATE_SERVICE denied (5)' }, null, 2));
} catch (error) { console.error(error); throw error; } finally {
  for (const [pid, creationTimeFileTime] of owned) { try { if (alive(pid)) await api(`processes/${pid}/terminate`, { creationTimeFileTime }); } catch {} }
  const exited = new Promise(resolve => host.once('exit', resolve)); if (host.exitCode === null) { host.kill(); await exited; }
  await until(async () => [...owned.keys()].every(pid => !alive(pid)), 'cleanup test-owned processes');
  await rm(directory, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 });
}
