import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { chromium } from 'playwright';
import { alive, delay, ownedAlive, portOpen, processTree, until } from './e2e-support.mjs';
import { stopOwnedHost } from './etw-support.mjs';
import { nativeUi, probe, windows } from './native-ui-support.mjs';

const root = path.resolve(import.meta.dirname, '..');
const release = path.resolve(process.env.TASKMANAGER_TAKEOVER_RELEASE ?? path.join(root, 'output/takeover/TaskManager-win-x64'));
const output = path.join(root, 'output/playwright/task-manager-takeover');
const port = 15333, cdp = 19333, origin = `http://127.0.0.1:${port}`;
const temporary = await mkdtemp(path.join(tmpdir(), 'taskmanager-takeover-'));
const checks = [], screenshots = [], geometry = [], errors = [], artifacts = [];
const check = (condition, message) => { assert.ok(condition, message); checks.push(message); console.log('PASS ' + message); };
const hash = text => createHash('sha256').update(text).digest('hex');
const checkoutAtRun = spawnSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8', windowsHide: true }).stdout.trim();
let host, tree = [], browser, page, token, failure, integrationPosts = 0, state, startupText = '', releaseInitialRead;
const firstLaunchInitialRead = { delayedRequests: 0, delayMs: 700 };

// Independent Windows RegistryKey reader; production reads use CsWin32.
// This suite performs no registry writes and compares every value/type/child.
function registry() {
  const command = String.raw`$base=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,[Microsoft.Win32.RegistryView]::Registry64);function Read-Key($key){if(-not $key){return [ordered]@{exists=$false;values=@();subkeys=@()}};$values=@();foreach($name in @($key.GetValueNames()|Sort-Object -CaseSensitive)){$kind=$key.GetValueKind($name).ToString();$data=$key.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames);if($kind -in @('Binary','None')){$data=[Convert]::ToBase64String($data)}elseif($kind -in @('DWord','QWord')){$data=$data.ToString([Globalization.CultureInfo]::InvariantCulture)};$values += [ordered]@{name=$name;kind=$kind;data=$data}};$children=@();foreach($name in @($key.GetSubKeyNames()|Sort-Object -CaseSensitive)){$child=$key.OpenSubKey($name);try{$children += [ordered]@{name=$name;state=(Read-Key $child)}}finally{$child.Dispose()}};return [ordered]@{exists=$true;values=@($values);subkeys=@($children)}};$key=$base.OpenSubKey('SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\taskmgr.exe');try{Read-Key $key|ConvertTo-Json -Compress -Depth 50}finally{if($key){$key.Dispose()};$base.Dispose()}`;
  const result = spawnSync('powershell.exe', ['-NoProfile', '-Command', command], { windowsHide: true, encoding: 'utf8', timeout: 15000 });
  assert.equal(result.status, 0, result.stderr); return result.stdout.trim();
}
const before = registry(), beforeHash = hash(before);
async function api(route, body, expected = 200, headers = {}) {
  const response = await fetch(`${origin}/api/${route}`, { method: body === undefined ? 'GET' : 'POST', headers: { Origin: origin, ...(body === undefined ? {} : { 'Content-Type': 'application/json', 'X-TaskManager-Token': token }), ...headers }, body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(10000) });
  const value = await response.json(); assert.equal(response.status, expected, `${route}: ${JSON.stringify(value)}`); return value;
}
async function capture(name) {
  // Playwright's viewport clip is incorrect after actual Electron zoom; the raw
  // compositor screenshot retains the full physical client surface.
  const session = await page.context().newCDPSession(page);
  const result = await session.send('Page.captureScreenshot', { format: 'png', fromSurface: true, captureBeyondViewport: false });
  await session.detach(); await writeFile(path.join(output, name + '.png'), Buffer.from(result.data, 'base64')); screenshots.push(name + '.png');
  if (name.endsWith('-125')) {
    const window = windows('蓝域').find(candidate => tree.some(row => row.pid === candidate.pid));
    assert.ok(window, 'native screenshot belongs to the dedicated test window');
    nativeUi(window, 'capture', path.join(output, name + '-native.png')); screenshots.push(name + '-native.png');
  }
}
async function geometryCheck(name, dialog) {
  const facts = await dialog.evaluate(el => {
    const rect = node => { const r = node.getBoundingClientRect(); return { x: r.x, y: r.y, right: r.right, bottom: r.bottom }; };
    return { viewport: [innerWidth, innerHeight], dialog: rect(el), buttons: [...el.querySelectorAll('.modal-actions button')].map(rect), documentOverflow: document.documentElement.scrollWidth > innerWidth + 1 };
  });
  geometry.push({ name, ...facts });
  check(!facts.documentOverflow && facts.dialog.x >= 0 && facts.dialog.y >= 0 && facts.dialog.right <= facts.viewport[0] + 1 && facts.dialog.bottom <= facts.viewport[1] + 1, `${name}: confirmation fits the real Electron viewport`);
  check(facts.buttons.every(r => r.x >= facts.dialog.x && r.right <= facts.dialog.right + 1 && r.y >= 0 && r.bottom <= facts.viewport[1] + 1), `${name}: footer actions remain visible`);
}
await mkdir(output, { recursive: true });
try {
  check(probe('process-elevation', process.pid).elevated, 'test controller has a real elevated Windows token');
  for (const listener of [port, cdp]) assert.ok(!await portOpen(listener), 'dedicated acceptance port is occupied: ' + listener);
  const index = await readFile(path.join(release, 'wwwroot/index.html'), 'utf8');
  for (const relative of ['TaskManager.Host.exe', 'TaskManager.Host.dll', 'TaskManager.Launcher.exe', 'wwwroot/index.html', ...[...index.matchAll(/(?:src|href)="([^\"]+\.(?:js|css))"/g)].map(match => 'wwwroot/' + match[1].replace(/^\//, ''))]) {
    const bytes = await readFile(path.join(release, relative)); artifacts.push({ path: relative, bytes: bytes.length, sha256: hash(bytes) });
  }
  host = spawn(path.join(release, 'TaskManager.Host.exe'), [`--port=${port}`], { cwd: release, env: { ...process.env, TASKMANAGER_DATA_DIR: temporary, TASKMANAGER_CDP_PORT: String(cdp) }, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
  const record = data => { startupText = (startupText + data).slice(-32768); };
  host.stdout.on('data', record); host.stderr.on('data', record);
  await until(async () => host.exitCode !== null || (await api('health')).ready && await portOpen(cdp), 'dedicated real Host/Electron ready', 60000);
  assert.equal(host.exitCode, null, `dedicated Host exited during startup: ${host.exitCode}`);
  const bootstrap = await api('bootstrap'); token = bootstrap.token;
  await until(async () => (await api('bootstrap')).desktop, 'dedicated BrowserWindow registered');
  tree = processTree(host.pid);
  check(bootstrap.hostPid === host.pid && probe('process-elevation', host.pid).elevated, 'production API belongs to the dedicated elevated Host');
  state = await api('desktop/task-manager-integration');
  const independent = JSON.parse(before), debuggerRow = independent.values.find(row => row.name.toLowerCase() === 'debugger');
  check(state.canRead && state.currentDebugger === (debuggerRow?.data ?? null), 'CsWin32 GET matches the independent HKLM64 Debugger');
  check(state.status === 'conflict' && !state.ownsDebugger && !state.canEnable, 'actual installed-directory entry requires explicit takeover');
  check(state.canTakeOver === (state.canWrite === true && state.executableAvailable), 'canTakeOver follows actual permission and complete application files');
  check(state.registrationCommand === `"${path.join(release, 'TaskManager.Launcher.exe')}" --task-manager-entry`, 'target command is the trusted quoted Launcher and fixed marker');
  for (const body of [{ enabled: false, takeOver: true }, { enabled: true, repair: true, takeOver: true }]) {
    const result = await api('desktop/task-manager-integration', { ...body, expectedRevision: state.revision }, 400);
    check(result.error.code === 'integrationTakeOverInvalid', 'invalid takeover combination is rejected before mutation');
  }
  await api('desktop/task-manager-integration', { enabled: true, takeOver: true }, 400);
  await api('desktop/task-manager-integration', { enabled: true, takeOver: true, expectedRevision: '0'.repeat(64) }, 409);
  await api('desktop/task-manager-integration', { enabled: true, expectedRevision: state.revision }, 409);
  await api('desktop/task-manager-integration', { enabled: false, expectedRevision: state.revision }, 409);
  check(true, 'missing/stale revision, implicit takeover and non-owned cancel are rejected');
  await api('desktop/task-manager-integration', { enabled: true, takeOver: true, expectedRevision: state.revision }, 403, { 'X-TaskManager-Token': '' });
  await api('desktop/task-manager-integration', { enabled: true, takeOver: true, expectedRevision: state.revision }, 403, { Origin: 'https://example.invalid' });
  check(hash(registry()) === beforeHash, 'all protected API requests preserve the full existing registry configuration');
  browser = await chromium.connectOverCDP(`http://127.0.0.1:${cdp}`);
  await until(() => browser.contexts().flatMap(context => context.pages()).some(candidate => candidate.url().startsWith(origin)), 'dedicated production renderer page');
  page = browser.contexts().flatMap(context => context.pages()).find(candidate => candidate.url().startsWith(origin));
  page.setDefaultTimeout(12000); page.on('pageerror', error => errors.push(error.message));
  page.on('request', request => { if (request.method() === 'POST' && request.url().endsWith('/api/desktop/task-manager-integration')) integrationPosts++; });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  const initialReadGate = new Promise(resolve => { releaseInitialRead = resolve; });
  const integrationRoute = '**/api/desktop/task-manager-integration';
  const delayRealRead = async route => {
    if (route.request().method() === 'GET') { firstLaunchInitialRead.delayedRequests++; await initialReadGate; }
    await route.continue();
  };
  await page.route(integrationRoute, delayRealRead);
  await page.addInitScript(() => {
    const seen = new WeakSet();
    window.__firstPromptMounts = 0;
    new MutationObserver(() => {
      for (const dialog of document.querySelectorAll('dialog')) {
        if (dialog.textContent?.includes('替换标准任务管理器？') && !seen.has(dialog)) { seen.add(dialog); window.__firstPromptMounts++; }
      }
    }).observe(document, { childList: true, subtree: true });
  });
  await page.reload();
  await until(() => firstLaunchInitialRead.delayedRequests > 0, 'real first-launch GET is held before continuing to the production API');
  await page.getByRole('table', { name: '进程列表', exact: true }).waitFor();
  await delay(firstLaunchInitialRead.delayMs);
  firstLaunchInitialRead.dialogCountDuringDelay = await page.locator('dialog').count();
  firstLaunchInitialRead.promptMountsDuringDelay = await page.evaluate(() => window.__firstPromptMounts);
  check(firstLaunchInitialRead.dialogCountDuringDelay === 0 && firstLaunchInitialRead.promptMountsDuringDelay === 0, 'initial real GET delay creates no first-launch dialog or transient mount');
  check((await api('preferences')).preferences.taskManagerPromptDismissed === false && integrationPosts === 0, 'pending first-launch read neither saves the choice nor posts an IFEO change');
  await capture('first-launch-reading-no-dialog');
  releaseInitialRead();
  const first = page.getByRole('dialog', { name: '替换标准任务管理器？', exact: true });
  await first.getByRole('button', { name: '前往设置接管现有入口', exact: true }).waitFor();
  await page.unroute(integrationRoute, delayRealRead);
  firstLaunchInitialRead.promptMountsAfterRead = await page.evaluate(() => window.__firstPromptMounts);
  check(firstLaunchInitialRead.promptMountsAfterRead === 1, 'continuing the unmodified real conflict GET creates exactly one first-launch dialog');
  check(await first.getByRole('button', { name: '暂不替换', exact: true }).evaluate(el => el === document.activeElement), 'first-launch initial focus is on the defer action');
  check((await first.innerText()).includes('不恢复此前工具') && (await first.innerText()).includes(state.currentDebugger), 'first-launch conflict explains replacement and standard-only restoration with the actual command');
  await geometryCheck('first-launch-1380', first); await capture('first-launch-existing-entry');
  await first.getByRole('button', { name: '前往设置接管现有入口', exact: true }).click();
  await page.getByRole('heading', { name: '任务管理器入口', exact: true }).waitFor();
  const card = page.getByRole('heading', { name: '任务管理器入口', exact: true }).locator('..');
  await card.scrollIntoViewIfNeeded();
  const takeover = card.getByRole('button', { name: '接管现有入口…', exact: true });
  await until(async () => await takeover.isEnabled() === state.canTakeOver, 'settings action follows actual takeover capability');
  check((await api('preferences')).preferences.taskManagerPromptDismissed && integrationPosts === 0, 'first-launch settings navigation persists the choice without an integration POST');
  const persisted = JSON.parse(await readFile(path.join(temporary, 'preferences.json'), 'utf8'));
  check(persisted.ui.taskManagerPromptDismissed === true, 'first-launch choice is present in the real JSON.NET preference file');
  check((await card.innerText()).includes(state.currentDebugger), 'settings shows the current real alternate command');
  await capture('settings-existing-entry');
  await card.getByRole('button', { name: '查看接管现有入口的影响', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: '接管现有任务管理器入口？', exact: true });
  await dialog.waitFor();
  const text = await dialog.innerText();
  check(['Ctrl+Shift+Esc', '所有用户', '管理员', '关闭影响入口的过滤配置', '不恢复此前工具', state.currentDebugger, state.executablePath].every(value => text.includes(value)), 'takeover confirmation states actual source/target, filtering and standard-only restoration');
  check(await dialog.getByRole('button', { name: '暂不更改', exact: true }).evaluate(el => el === document.activeElement), 'takeover initial focus is on the neutral defer action');
  check(await dialog.getByRole('button', { name: '确认接管现有入口', exact: true }).isEnabled() === state.canTakeOver, 'confirmation follows the real native capability');
  for (let index = 0; index < 7; index++) { await page.keyboard.press('Tab'); check(await page.evaluate(() => Boolean(document.activeElement?.closest('dialog[open]'))), `takeover Tab ${index + 1} stays inside the confirmation`); }
  await geometryCheck('takeover-1380', dialog); await capture('takeover-confirmation');
  await page.keyboard.press('Escape');
  check(await dialog.count() === 0 && integrationPosts === 0, 'Escape cancels takeover without a system write request');
  for (const zoom of [false, true]) {
    probe(tree.map(row => `${row.pid}:${row.fileTime}`).join(','), 'resize', 960, 640);
    if (zoom) await api('window/zoom-in', {});
    await delay(400); await card.scrollIntoViewIfNeeded();
    await card.getByRole('button', { name: '查看接管现有入口的影响', exact: true }).focus(); await page.keyboard.press('Enter'); await dialog.waitFor();
    if (zoom) check(Math.abs((await api('desktop/window-state')).zoomFactor - 1.25) < .001, 'real Electron zoom factor is 125%');
    await geometryCheck(zoom ? 'takeover-960-at-125' : 'takeover-960', dialog);
    await capture(zoom ? 'takeover-confirmation-125' : 'takeover-confirmation-960');
    await dialog.getByRole('button', { name: '暂不更改', exact: true }).click();
  }
  check(integrationPosts === 0 && hash(registry()) === beforeHash && errors.length === 0, 'all UI interactions preserve the complete existing entry and raise no renderer error');
  tree = processTree(host.pid);
  await api('window/close', {});
  await until(async () => !alive(host.pid) && !await portOpen(port) && !await portOpen(cdp), 'dedicated desktop closes its listeners', 25000);
  check(tree.every(row => !ownedAlive(row)), 'all dedicated Host/Electron creation identities have exited');
} catch (error) { failure = { message: error.message, stack: error.stack }; await page?.screenshot({ path: path.join(output, 'failure.png') }).catch(() => {}); }
finally {
  releaseInitialRead?.();
  await browser?.close().catch(() => {});
  if (host && alive(host.pid)) { if (!tree.length) tree = processTree(host.pid); await stopOwnedHost(host).catch(error => { failure ??= { message: error.message }; }); }
  for (const row of tree.reverse()) if (ownedAlive(row)) { try { process.kill(row.pid); } catch {} }
  const globalRegistryUnchanged = hash(registry()) === beforeHash;
  if (!globalRegistryUnchanged) failure ??= { message: 'read-only acceptance observed a changed global registry' };
  await rm(temporary, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
  const startupDiagnostics = startupText.split(/\r?\n/).filter(line => /error|exception|failed|failure|not found|missing|unhandled/i.test(line) && !/token|secret|auth/i.test(line)).slice(-20);
  const report = { result: failure ? 'FAIL' : 'PASS', checkedAtUtc: new Date().toISOString(), checkoutAtRun, release, assertions: checks.length, checks, artifacts, screenshots, geometry, firstLaunchInitialRead, errors, integrationPosts, globalRegistryUnchanged, canWrite: state?.canWrite, startupExitCode: host?.exitCode, startupDiagnostics, failure, unexecuted: ['F09: current-directory enabled silent completion/restart/save-failure; actual IFEO points to the installed directory, so no global entry was changed to manufacture this state', 'T03: successful global takeover/type replacement and standard reset; the current user entry was preserved', 'T04: real UseFilter mutation and system taskmgr launch; the existing user entry was preserved', 'T05: real native write denial, concurrent registry mutation and partial write failure; no corresponding controlled condition was induced', 'T07: global Launcher and uninstall success chains; existing user process and entry were preserved'], source: 'production self-contained Electron.NET → same-origin HTTP → JSON.NET → CsWin32; only the initial GET was delayed and then continued without changing its response; no mocked business API or registry fixture' };
  await writeFile(path.join(output, 'results.json'), JSON.stringify(report, null, 2)); console.log(JSON.stringify(report, null, 2)); if (failure) process.exitCode = 1;
}
