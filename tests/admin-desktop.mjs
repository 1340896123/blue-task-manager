import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';
import { until, processTree, ownedAlive, portOpen } from './e2e-support.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const directory = path.resolve(process.env.TASKMANAGER_RELEASE_DIRECTORY ?? path.join(root, 'output/TaskManager-win-x64'));
const framework = 'net10.0-windows10.0.19041.0';
const probeExe = path.join(root, 'tests/TaskManager.WindowProbe/bin/Release', framework, 'TaskManager.WindowProbe.exe');
const workloadExe = path.join(root, 'tests/TaskManager.TestWorkload/bin/Release', framework, 'TaskManager.TestWorkload.exe');
const temporary = await mkdtemp(path.join(tmpdir(), 'taskmanager-admin-'));
const output = path.join(root, 'output/admin-installer');
const evidenceName = process.env.TASKMANAGER_ADMIN_EVIDENCE_PREFIX ?? 'administrator-desktop';
if (!/^[a-z0-9-]+$/.test(evidenceName)) throw new Error('Invalid administrator evidence prefix.');
const port = 5212, cdp = 9233, origin = `http://127.0.0.1:${port}`;
const checks = [];
const check = (condition, message) => { assert.ok(condition, message); checks.push(message); };
const powershell = command => {
  const result = spawnSync('powershell.exe', ['-NoProfile', '-Command', command], { windowsHide: true, encoding: 'utf8', timeout: 15000 });
  assert.equal(result.status, 0, result.stderr); return result.stdout.trim();
};
const elevation = pid => {
  const result = spawnSync(probeExe, ['process-elevation', String(pid)], { windowsHide: true, encoding: 'utf8' });
  assert.equal(result.status, 0, result.stderr); return JSON.parse(result.stdout).elevated;
};
assert.ok(elevation(process.pid), 'Run this acceptance with a Windows elevated token.');
if (await portOpen(port) || await portOpen(cdp)) throw new Error('Administrator acceptance ports occupied.');
await mkdir(output, { recursive: true });
const host = spawn(path.join(directory, 'TaskManager.Host.exe'), [`--port=${port}`], {
  cwd: tmpdir(), windowsHide: true, stdio: 'ignore',
  env: { ...process.env, TASKMANAGER_DATA_DIR: path.join(temporary, 'preferences'), TASKMANAGER_CDP_PORT: String(cdp), DOTNET_ROOT: path.join(temporary, 'no-global-dotnet'), DOTNET_ROOT_X64: path.join(temporary, 'no-global-dotnet'), DOTNET_MULTILEVEL_LOOKUP: '0' }
});
let browser, page, tree = [], target, token;
const api = async (route, body) => {
  const response = await fetch(origin + '/api/' + route, { method: body === undefined ? 'GET' : 'POST', headers: { Origin: origin, ...(body === undefined ? {} : { 'Content-Type': 'application/json', 'X-TaskManager-Token': token }) }, body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(10000) });
  assert.equal(response.status, 200, route); return response.json();
};
try {
  await until(async () => (await api('health')).ready && await portOpen(cdp), 'elevated self-contained host ready', 45000);
  const bootstrap = await api('bootstrap'); token = bootstrap.token;
  check(bootstrap.version === '1.1.1' && bootstrap.hostPid === host.pid, 'production host reports version and exact test PID');
  check(elevation(host.pid), 'independent CsWin32 probe confirms Host TokenIsElevated');
  const browserPid = Number(powershell(`Get-CimInstance Win32_Process -Filter 'ParentProcessId = ${host.pid}' | Where-Object { $_.Name -eq 'electron.exe' } | Select-Object -First 1 -ExpandProperty ProcessId`));
  check(browserPid > 0 && elevation(browserPid), 'official Electron browser child inherits the elevated token');
  const coreclr = powershell(`[Diagnostics.Process]::GetProcessById(${host.pid}).Modules | Where-Object { $_.ModuleName -eq 'coreclr.dll' } | Select-Object -First 1 -ExpandProperty FileName`);
  check(path.resolve(coreclr).toLowerCase() === path.join(directory, 'coreclr.dll').toLowerCase(), 'Host loads coreclr from its own directory with invalid global DOTNET_ROOT');
  browser = await chromium.connectOverCDP(`http://127.0.0.1:${cdp}`);
  await until(() => browser.contexts()[0]?.pages().length > 0, 'real elevated Electron window');
  page = browser.contexts()[0].pages()[0];
  await page.getByText('已连接 · 实时采样', { exact: true }).waitFor();
  tree = processTree(host.pid);
  check(await page.evaluate(() => typeof require === 'undefined' && typeof process === 'undefined'), 'renderer retains sandbox and no Node globals');
  const first = await api('snapshot');
  await until(async () => (await api('snapshot')).sampleId > first.sampleId, 'real native sampling advances');
  check(first.processes.some(row => row.pid === host.pid), 'real Windows collector includes the actual elevated host');
  target = await api('processes/start', { executable: workloadExe, arguments: `"${path.join(temporary, 'workload')}" 60` });
  check(elevation(target.pid), 'dedicated new-task workload inherits administrator privileges');
  await api(`processes/${target.pid}/terminate`, { creationTimeFileTime: target.creationTimeFileTime }); target = undefined;
  await page.getByRole('button', { name: '设置', exact: true }).click();
  check(await page.getByText('启动必需管理员权限；新任务默认继承管理员权限', { exact: true }).count() === 1, 'settings describes actual administrator requirement and task inheritance');
  await page.getByText('启动必需管理员权限；新任务默认继承管理员权限', { exact: true }).scrollIntoViewIfNeeded();
  await page.screenshot({ path: path.join(output, evidenceName + '.png') });
  tree = processTree(host.pid);
  await page.getByRole('button', { name: '关闭窗口', exact: true }).click();
  await until(() => host.exitCode !== null, 'elevated host closes', 15000);
  await until(async () => !await portOpen(port) && !await portOpen(cdp), 'elevated instance ports close');
  await until(() => tree.every(entry => !ownedAlive(entry)), 'elevated instance descendants exit', 20000);
  check(true, 'native window close releases the exact host tree and both listeners');
  const result = { result: 'PASS', checkedAtUtc: new Date().toISOString(), assertions: checks.length, checks, directory, coreclr, screenshot: evidenceName + '.png', serviceSuccessChain: 'not executed: no dedicated test service provisioned', globalIfeoMutation: false };
  await writeFile(path.join(output, evidenceName + '.json'), JSON.stringify(result, null, 2));
  console.log(JSON.stringify(result, null, 2));
} finally {
  if (target) { try { await api(`processes/${target.pid}/terminate`, { creationTimeFileTime: target.creationTimeFileTime }); } catch {} }
  if (host.exitCode === null) {
    try { if (page) await page.getByRole('button', { name: '关闭窗口', exact: true }).click({ timeout: 3000 }); } catch {}
    try { await until(() => host.exitCode !== null, 'cleanup elevated instance', 10000); } catch { host.kill(); }
  }
  if (browser) { try { await browser.close(); } catch {} }
  for (const entry of tree.reverse()) { if (ownedAlive(entry)) { try { process.kill(entry.pid); } catch {} } }
  await rm(temporary, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 });
}
