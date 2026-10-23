import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';
import { alive, delay, ownedAlive, portOpen, processTree, until } from './e2e-support.mjs';
import { nativeUi, probe, windows } from './native-ui-support.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const directory = path.resolve(process.env.TASKMANAGER_RELEASE_DIRECTORY ?? path.join(root, 'output/TaskManager-win-x64')), output = path.join(root, 'output/taskbar-integration');
const executable = path.join(directory, 'TaskManager.Host.exe');
const temporary = await mkdtemp(path.join(tmpdir(), 'taskmanager-entry-'));
const port = 5203, cdp = 9231, forwardPort = 5204, forwardCdp = 9232, origin = `http://127.0.0.1:${port}`;
const target = path.join(process.env.SystemRoot, 'System32', 'taskmgr.exe');
const checks = [], errors = [], writes = []; let host, browser, page, tree = [], token;
function check(condition, message) { assert.ok(condition, message); checks.push(message); }
function independentRegistry() {
  const command = String.raw`$root=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,[Microsoft.Win32.RegistryView]::Registry64);$key=$root.OpenSubKey('SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\taskmgr.exe',$false);$rows=@();$subkeys=@();if($key){foreach($name in ($key.GetValueNames()|Sort-Object)){$rows+=@{name=$name;kind=$key.GetValueKind($name).ToString();data=$key.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)}};$subkeys=@($key.GetSubKeyNames()|Sort-Object)};@{keyExists=($null -ne $key);values=@($rows);subkeys=@($subkeys)}|ConvertTo-Json -Compress -Depth 8;if($key){$key.Dispose()};$root.Dispose()`;
  const result = spawnSync('powershell.exe', ['-NoProfile', '-Command', command], { windowsHide: true, encoding: 'utf8', timeout: 15000 });
  assert.equal(result.status, 0, result.stderr); return JSON.parse(result.stdout);
}
const fingerprint = value => createHash('sha256').update(JSON.stringify(value)).digest('hex');
async function api(route, body, expected = 200, headers = {}) {
  const response = await fetch(`${origin}/api/${route}`, { method: body === undefined ? 'GET' : 'POST', headers: { Origin: origin, ...(body === undefined ? {} : { 'Content-Type': 'application/json', 'X-TaskManager-Token': token }), ...headers }, body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(10000) });
  const value = await response.json(); assert.equal(response.status, expected, `${route}: ${JSON.stringify(value)}`); return value;
}
async function entry(args, environment = {}) {
  const child = spawn(executable, args, { cwd: tmpdir(), env: { ...process.env, TASKMANAGER_DATA_DIR: path.join(temporary, 'forward'), TASKMANAGER_PORT: String(forwardPort), TASKMANAGER_CDP_PORT: String(forwardCdp), ...environment }, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
  let text = ''; child.stdout.on('data', data => { text += data; }); child.stderr.on('data', data => { text += data; });
  try { await until(() => child.exitCode !== null, 'entry forwarding process did not exit', 12000); }
  catch (error) { if (alive(child.pid)) child.kill(); throw error; }
  return { code: child.exitCode, text, pid: child.pid };
}
await mkdir(output, { recursive: true });
const before = independentRegistry(), beforeHash = fingerprint(before);
let state, foregroundState;
try {
  for (const listener of [port, cdp, forwardPort, forwardCdp]) if (await portOpen(listener)) throw new Error('Integration acceptance port is occupied: ' + listener);
  host = spawn(executable, ['--task-manager-entry', target, '/7'], { cwd: tmpdir(), env: { ...process.env, TASKMANAGER_DATA_DIR: path.join(temporary, 'preferences'), TASKMANAGER_PORT: String(port), TASKMANAGER_CDP_PORT: String(cdp) }, windowsHide: true, stdio: 'ignore' });
  await until(async () => (await api('health')).ready && await portOpen(cdp), 'first real IFEO-style entry startup', 45000);
  let bootstrap;
  await until(async () => { bootstrap = await api('bootstrap'); return bootstrap.desktop; }, 'actual Electron BrowserWindow created', 45000);
  token = bootstrap.token;
  check(bootstrap.desktop && bootstrap.hostPid === host.pid, 'first entry creates the real Electron desktop after removing the original system taskmgr path and /7 arguments');
  state = await api('desktop/task-manager-integration');
  const actualDebugger = before.values.find(row => row.name === 'Debugger');
  const actualFilter = before.values.find(row => row.name === 'UseFilter');
  check(state.canRead && state.currentDebugger === (actualDebugger?.data ?? null) && state.useFilter === (actualFilter?.data ?? null), 'CsWin32 Debugger and UseFilter status match independent HKLM64 read');
  check(state.registrationCommand === `"${path.join(directory, 'TaskManager.Launcher.exe')}" --task-manager-entry` && state.executableAvailable, 'registration command is the quoted trusted portable Launcher plus fixed marker');
  check(state.scope === 'machine' && typeof state.canWrite === 'boolean', 'readability and actual machine-wide write permission are reported separately');
  const badRevision = state.revision === '0'.repeat(64) ? '1'.repeat(64) : '0'.repeat(64);
  await api('desktop/task-manager-integration', { enabled: true, expectedRevision: badRevision }, 409);
  check(true, 'stale expected revision rejects before any registry mutation');
  await api('desktop/task-manager-integration', { enabled: true, expectedRevision: state.revision }, 403, { 'X-TaskManager-Token': '' });
  await api('desktop/task-manager-integration', { enabled: true, expectedRevision: state.revision }, 403, { Origin: 'https://example.invalid' });
  check(true, 'integration writes require the real same-origin operation token');
  if (state.status === 'disabled' && state.canWrite === false) {
    const denied = await api('desktop/task-manager-integration', { enabled: true, expectedRevision: state.revision }, 403);
    check(denied.error.nativeErrorCode === 5 && denied.error.code === 'integrationAccessDenied', 'standard-user registration attempt returns real Windows access denied 5; no UAC or pretend success');
  }
  check(fingerprint(independentRegistry()) === beforeHash, 'permission and conflict tests leave all target-key values, default value and subkeys unchanged');
  browser = await chromium.connectOverCDP(`http://127.0.0.1:${cdp}`);
  await until(() => browser.contexts()[0]?.pages().length > 0, 'real entry BrowserWindow page');
  page = browser.contexts()[0].pages()[0]; page.on('pageerror', error => errors.push(error.message));
  page.on('request', request => { if (request.method() === 'POST' && request.url().endsWith('/api/desktop/task-manager-integration')) writes.push(request.url()); });
  await page.getByText('已连接 · 实时采样', { exact: true }).waitFor();
  await page.getByRole('button', { name: '设置', exact: true }).click();
  const card = page.getByRole('heading', { name: '任务栏右键中的任务管理器', exact: true }).locator('..');
  await card.scrollIntoViewIfNeeded();
  await until(async () => !(await card.innerText()).includes('正在读取实际状态'), 'settings actual registry state');
  const cardText = await card.innerText();
  check(cardText.includes('Ctrl+Shift+Esc') && cardText.includes('所有用户') && cardText.includes('管理员') && cardText.includes('移动或删除'), 'settings states shortcut, machine-wide scope, permission and move-directory consequences');
  if (state.status === 'disabled' && state.canWrite === false) check(await card.getByRole('button', { name: '启用接管…', exact: true }).isDisabled() && cardText.includes('当前账户没有写权限'), 'no-write-permission UI disables actual registration and explains the observed permission');
  await card.getByRole('button', { name: state.ownsDebugger ? '查看取消接管的影响' : '查看启用接管的影响', exact: true }).click();
  const dialog = page.getByRole('dialog'); await dialog.waitFor();
  check((await dialog.innerText()).includes('不是新增菜单项') && (await dialog.innerText()).includes('Ctrl+Shift+Esc') && (await dialog.innerText()).includes('原目录'), 'confirmation explains real takeover and safe restoration before moving the program');
  if (state.canWrite === false) check(await dialog.getByRole('button', { name: state.ownsDebugger ? '确认取消接管' : '确认启用接管', exact: true }).isDisabled(), 'permission explanation cannot bypass the disabled native write action');
  await page.screenshot({ path: path.join(output, 'taskbar-integration-confirmation.png') });
  await dialog.getByRole('button', { name: '暂不更改', exact: true }).click();
  check(await dialog.count() === 0 && writes.length === 0, 'canceling the impact dialog sends no write request');
  await card.getByRole('button', { name: '刷新入口实际状态', exact: true }).click();
  await until(async () => await card.getByRole('button', { name: '刷新入口实际状态', exact: true }).isEnabled(), 'settings state refresh');
  await page.screenshot({ path: path.join(output, 'taskbar-integration-settings.png') });
  tree = processTree(host.pid); const ids = new Set(tree.map(row => row.pid));
  const candidates = windows('蓝域', true).filter(row => ids.has(row.pid));
  const observed = candidates.map(window => ({ ...window, state: nativeUi(window, 'state') }));
  await writeFile(path.join(output, 'window-diagnostic.json'), JSON.stringify({ hostPid: host.pid, descendants: tree.map(({ pid, imagePath, fileTime }) => ({ pid, imagePath, fileTime })), windows: observed, actualDocumentTitle: await page.title() }, null, 2));
  const window = candidates.find(row => observed.find(item => item.hwnd === row.hwnd)?.state.visible); check(Boolean(window), 'independent HWND query identifies only this test-owned visible product window');
  for (const zoom of [false, true]) {
    probe(`${window.pid}:${window.fileTime}`, 'resize', 960, 640);
    if (zoom) await api('window/zoom-in', {});
    await delay(300); await card.scrollIntoViewIfNeeded();
    check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth && document.documentElement.scrollHeight <= innerHeight), `integration settings preserve page bounds at 960×640${zoom ? ' and 125%' : ''}`);
    if (zoom) await api('window/zoom-reset', {});
  }
  await page.screenshot({ path: path.join(output, 'taskbar-integration-settings-960.png') });
  await api('window/minimize', {});
  await until(() => nativeUi(window, 'state').minimized, 'real product window minimized');
  const forwarded = await entry(['--task-manager-entry', target, '/7']);
  check(forwarded.code === 0 && !alive(forwarded.pid), 'second marked entry exits after reusing existing product window');
  await until(() => !nativeUi(window, 'state').minimized, 'existing native window restored by production entry');
  foregroundState = nativeUi(window, 'state');
  check(foregroundState.visible && !foregroundState.minimized && (await api('health')).processId === host.pid, 'entry restores the existing HWND while preserving the original host PID');
  check(!await portOpen(forwardPort) && !await portOpen(forwardCdp), 'forwarded entry creates neither a duplicate HTTP host nor another Electron CDP listener');
  check(windows('蓝域').filter(row => ids.has(row.pid)).length === 1, 'same-directory repeated entry retains exactly one test-owned native product window');
  const invalid = await entry(['--task-manager-entry', 'C:\\untrusted\\not-taskmgr.exe', '--port=1']);
  check(invalid.code === 2 && !await portOpen(forwardPort), 'unrecognized appended executable is rejected before ASP.NET startup');
  check(fingerprint(independentRegistry()) === beforeHash && errors.length === 0, 'UI/entry E2E changes no global IFEO value and raises no renderer runtime error');
  await page.getByRole('button', { name: '关闭窗口', exact: true }).click();
  await until(async () => !alive(host.pid) && !await portOpen(port) && !await portOpen(cdp), 'entry desktop closes all owned listeners', 20000);
  check(tree.every(row => !ownedAlive(row)), 'closing the test desktop leaves no test-owned host or Electron descendants');
  const result = { result: 'PASS', checkedAtUtc: new Date().toISOString(), assertions: checks.length, checks, canWrite: state.canWrite, nativeWriteDenied: state.status === 'disabled' && state.canWrite === false ? 5 : null, globalRegistryUnchanged: true, registrationSuccessChain: 'not executed: global takeover remains explicitly opt-in; current machine was not modified', foregroundState, errors, evidence: ['taskbar-integration-settings.png', 'taskbar-integration-confirmation.png', 'taskbar-integration-settings-960.png'] };
  await writeFile(path.join(output, 'results.json'), JSON.stringify(result, null, 2)); console.log(JSON.stringify(result, null, 2));
} catch (error) { await page?.screenshot({ path: path.join(output, 'failure.png') }).catch(() => {}); throw error; }
finally {
  await browser?.close().catch(() => {});
  if (host && alive(host.pid)) { if (!tree.length) tree = processTree(host.pid); host.kill(); }
  for (const row of tree.reverse()) if (ownedAlive(row)) { try { process.kill(row.pid); } catch {} }
  await delay(350); await rm(temporary, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
  assert.equal(fingerprint(independentRegistry()), beforeHash, 'final global IFEO state must remain unchanged even after a failed test');
}
