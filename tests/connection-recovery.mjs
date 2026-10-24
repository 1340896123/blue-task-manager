import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { chromium } from 'playwright';
import { alive, delay, ownedAlive, portOpen, processTree, until } from './e2e-support.mjs';
import { probe } from './native-ui-support.mjs';
import { networkSessions, stopOwnedHost } from './etw-support.mjs';

const root = path.resolve(import.meta.dirname, '..');
const release = path.resolve(process.env.TASKMANAGER_RECOVERY_RELEASE ?? path.join(root, 'output/connection-recovery/TaskManager-win-x64'));
const smoke = process.argv.includes('--smoke');
const output = path.resolve(process.env.TASKMANAGER_RECOVERY_OUTPUT ?? path.join(root, 'output/playwright', smoke ? 'connection-recovery-installed' : 'connection-recovery'));
const temporary = await mkdtemp(path.join(tmpdir(), 'taskmanager-window-recovery-'));
const port = 5331, cdp = 9331, origin = `http://127.0.0.1:${port}`;
const checks = [], screenshots = [], launches = [], timings = [], rendererErrors = [], nativeFacts = [];
let current, failure;
const check = (condition, message) => { assert.ok(condition, message); checks.push(message); console.log('PASS ' + message); };
const hash = value => createHash('sha256').update(value).digest('hex');

async function api(route, body) {
  const response = await fetch(`${origin}/api/${route}`, {
    method: body === undefined ? 'GET' : 'POST',
    headers: { Origin: origin, ...(body === undefined ? {} : { 'Content-Type': 'application/json', 'X-TaskManager-Token': current.token }) },
    body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(10000),
  });
  assert.ok(response.ok, `${route}: ${response.status}`); return response.json();
}
async function capture(name) {
  const session = await current.page.context().newCDPSession(current.page);
  try {
    const result = await session.send('Page.captureScreenshot', { format: 'png', fromSurface: true, captureBeyondViewport: false });
    await writeFile(path.join(output, name + '.png'), Buffer.from(result.data, 'base64')); screenshots.push(name + '.png');
  } finally { await session.detach(); }
}
async function open(name, desktop = true) {
  assert.ok(!await portOpen(port) && !await portOpen(cdp), 'dedicated test ports free');
  const directory = path.join(temporary, name); await mkdir(directory);
  await writeFile(path.join(directory, 'preferences.json'), JSON.stringify({ version: 1, intervalMs: 1000, ui: { version: 1, density: 'compact', sidebarCollapsed: false, processView: 'flat', searchQuery: '', taskManagerPromptDismissed: true, pages: {} } }));
  const host = spawn(path.join(release, 'TaskManager.Host.exe'), [...(desktop ? [] : ['--headless']), `--port=${port}`], {
    cwd: release, env: { ...process.env, TASKMANAGER_DATA_DIR: directory, TASKMANAGER_CDP_PORT: String(cdp) }, windowsHide: true, stdio: 'ignore',
  });
  current = { name, host, desktop, directory, tree: [], requests: [], token: null, browser: null, page: null };
  await until(async () => { const response = await fetch(origin + '/api/health', { signal: AbortSignal.timeout(1500) }); return response.ok && (await response.json()).ready; }, name + ': real Host ready', 45000);
  const health = await api('health'); current.token = (await api('bootstrap')).token;
  check(health.nativeSource.includes('CsWin32') && health.json === 'Newtonsoft.Json', name + ': real Windows and JSON.NET source');
  if (desktop) {
    await until(() => portOpen(cdp), name + ': Electron CDP ready', 45000);
    current.browser = await chromium.connectOverCDP(`http://127.0.0.1:${cdp}`);
    await until(() => current.browser.contexts().flatMap(c => c.pages()).some(p => p.url().startsWith(origin)), name + ': real Electron page', 45000);
    current.page = current.browser.contexts().flatMap(c => c.pages()).find(p => p.url().startsWith(origin));
    await until(async () => (await api('bootstrap')).desktop, name + ': registered BrowserWindow');
  } else {
    current.browser = await chromium.launch({ headless: true }); current.page = await current.browser.newPage({ viewport: { width: 1380, height: 860 } }); await current.page.goto(origin);
  }
  current.tree = processTree(host.pid); launches.push({ name, hostPid: host.pid, desktop, identities: current.tree.map(p => ({ pid: p.pid, fileTime: p.fileTime })) });
  const identity = probe('process-elevation', host.pid); current.etwPrefix = `BlueDomain-Network-${host.pid}-${identity.creationTimeFileTime}-`;
  check(identity.elevated, name + ': actual elevated Host token');
  current.page.on('pageerror', error => rendererErrors.push({ scenario: name, message: error.message }));
  current.page.on('request', request => { if (request.url().includes('/api/window/')) current.requests.push(request.url().split('/api/')[1]); });
  current.page.setDefaultTimeout(12000); await current.page.emulateMedia({ reducedMotion: 'reduce' });
  await current.page.getByRole('table', { name: '进程列表', exact: true }).waitFor();
  check((await api('preferences')).preferences.taskManagerPromptDismissed === true, name + ': valid isolated preference skips the first-launch prompt');
  return current.page;
}
function windowState() { return probe(current.tree.map(p => `${p.pid}:${p.fileTime}`).join(',')); }
async function windowButtons(label) {
  const page = current.page;
  await page.getByRole('button', { name: '最小化窗口', exact: true }).click();
  await delay(100);
  const minimized = { label, toast: await page.locator('.toast').allTextContents() };
  try { minimized.native = windowState(); } catch (error) { minimized.nativeError = error.message; }
  nativeFacts.push(minimized); await writeFile(path.join(output, 'native-facts.json'), JSON.stringify(nativeFacts, null, 2));
  if (minimized.toast.length) throw new Error(label + ': window action reported ' + minimized.toast.join('; '));
  await until(() => windowState().state === 2, label + ': native minimized');
  // A minimized surface cannot receive a physical click. Dispatch its real button
  // handler through CDP, then independently inspect the resulting Win32 state.
  await page.getByRole('button', { name: '最大化或还原窗口', exact: true }).evaluate(element => element.click());
  await until(() => windowState().state === 3, label + ': native maximized');
  await page.getByRole('button', { name: '最大化或还原窗口', exact: true }).click();
  await until(() => windowState().state === 1, label + ': native restored');
  check(current.requests.length === 0, label + ': all three state transitions avoid HTTP window endpoints');
}
async function verifyExited(app) {
  await until(() => app.host.exitCode !== null || app.host.signalCode !== null, app.name + ': exact Host exits', 20000);
  await until(async () => !await portOpen(port) && !await portOpen(cdp), app.name + ': HTTP/CDP listeners released');
  check(app.tree.every(entry => !ownedAlive(entry)), app.name + ': all exact Host/Electron creation identities exited');
  check(!(await networkSessions()).some(name => name.startsWith(app.etwPrefix)), app.name + ': exact Host ETW sessions released');
}
async function closeByButton(label, maxMs = 3000) {
  const app = current, started = Date.now(), closed = app.page.waitForEvent('close', { timeout: 10000 });
  await app.page.getByRole('button', { name: '关闭窗口', exact: true }).click().catch(error => { if (!app.page.isClosed()) throw error; });
  await closed; const elapsedMs = Date.now() - started; timings.push({ scenario: label, elapsedMs });
  check(elapsedMs < maxMs, `${label}: window closes within ${maxMs}ms (actual ${elapsedMs}ms)`);
  check(app.requests.length === 0, label + ': close sends no HTTP window command');
  await verifyExited(app); await app.browser.close().catch(() => {}); current = null;
}
async function cleanup() {
  if (!current) return; const app = current;
  if (!app.page?.isClosed() && app.desktop) await app.page.evaluate(() => window.taskManagerWindow?.close()).catch(() => {});
  if (app.desktop) await until(() => app.host.exitCode !== null || app.host.signalCode !== null, 'cleanup graceful Host close', 10000).catch(() => {});
  if (alive(app.host.pid)) await stopOwnedHost(app.host);
  await app.browser?.close().catch(() => {});
  // Forced cleanup is limited to the exact creation identities captured above.
  for (const entry of [...app.tree].reverse()) if (ownedAlive(entry)) { try { process.kill(entry.pid); } catch {} }
  await until(async () => !await portOpen(port) && !await portOpen(cdp), 'dedicated recovery cleanup'); current = null;
}

await mkdir(output, { recursive: true });
const webIndex = await readFile(path.join(release, 'wwwroot/index.html'));
const artifactNames = ['TaskManager.Host.dll', '.electron/custom_main.js', '.electron/window-preload.js', 'wwwroot/index.html', ...[...webIndex.toString().matchAll(/(?:src|href)="([^"]+\.(?:js|css))"/g)].map(match => 'wwwroot/' + match[1].replace(/^\//, ''))];
const artifacts = await Promise.all(artifactNames.map(async relative => { const data = await readFile(path.join(release, relative)); return { path: relative, bytes: data.length, sha256: hash(data) }; }));
await writeFile(path.join(output, 'artifacts.json'), JSON.stringify({ release, artifacts, note: 'Production binaries include the pre-existing network working-tree baseline; hashes identify tested files.' }, null, 2));
try {
  let page = await open('normal');
  const first = await api('snapshot'); await delay(1800); const second = await api('snapshot');
  check(second.sampleId > first.sampleId && second.processes.some(p => p.pid === current.host.pid), 'normal: genuine samples advance and include this Host PID');
  check(await page.evaluate(() => Object.keys(window.taskManagerWindow).sort().join(',') === 'close,maximize,minimize' && typeof window.require === 'undefined'), 'normal: fixed three-method bridge and no renderer Node require');
  await page.evaluate(() => { const frame = document.createElement('iframe'); frame.name = 'recovery-security-child'; frame.src = 'about:blank'; document.body.append(frame); });
  await until(() => page.frames().some(frame => frame.name() === 'recovery-security-child'), 'same-origin child frame exists');
  check(await page.frame({ name: 'recovery-security-child' }).evaluate(() => typeof window.taskManagerWindow === 'undefined'), 'normal: child frame receives no window bridge');
  await page.evaluate(() => document.querySelector('iframe[name="recovery-security-child"]').remove());
  await windowButtons('normal'); await capture('normal-electron'); await closeByButton('normal');

  if (!smoke) {
  page = await open('http-disconnected');
  // Generate the reported notification through a genuinely failed HTTP action.
  await page.route('**/api/refresh', route => route.abort('failed'));
  await page.keyboard.press('F5'); await page.locator('.toast.error').waitFor();
  check((await page.locator('.toast').innerText()).includes('无法连接本机服务'), 'transport: genuine request failure displays connection explanation');
  await page.getByRole('button', { name: '关闭通知', exact: true }).click(); await page.locator('.toast').waitFor({ state: 'detached' });
  check(true, 'notification: mouse dismissal needs no Host request');
  await page.keyboard.press('F5'); await page.locator('.toast.error').waitFor();
  await page.getByRole('button', { name: '关闭通知', exact: true }).focus(); await page.keyboard.press('Enter'); await page.locator('.toast').waitFor({ state: 'detached' });
  check(true, 'notification: keyboard dismissal needs no Host request');
  await page.unroute('**/api/refresh'); await api('settings', { intervalMs: 1000, paused: true });
  await page.locator('.connection-banner').filter({ hasText: '采集已暂停' }).waitFor(); await delay(200);
  const retainedKey = await page.locator('tbody tr[data-row-key]').first().getAttribute('data-row-key');
  const retained = await page.locator('tbody tr[data-row-key]').first().innerText();
  await page.route('**/api/**', route => route.abort('failed'));
  await page.locator('.connection-banner.error').getByText('连接中断 · 保留上次数据', { exact: true }).waitFor();
  check(await page.locator('tbody tr[data-row-key]').first().getAttribute('data-row-key') === retainedKey && await page.locator('tbody tr[data-row-key]').first().innerText() === retained, 'http-disconnected: retains last genuine process identity and values');
  await windowButtons('http-disconnected'); await capture('http-disconnected-electron'); await closeByButton('http-disconnected');

  page = await open('host-terminated');
  await stopOwnedHost(current.host); check(!await portOpen(port), 'host-terminated: dedicated real Host and owned ETW sessions stopped');
  if (!page.isClosed()) {
    await page.locator('.connection-banner.error').waitFor();
    check(current.tree.some(entry => entry.pid !== current.host.pid && ownedAlive(entry)), 'host-terminated: real Electron remains alive after Host termination');
    await capture('host-terminated-electron'); await closeByButton('host-terminated');
  } else { check(true, 'host-terminated: framework itself closes Electron without leaving an unusable window'); await verifyExited(current); await current.browser.close().catch(() => {}); current = null; }

  page = await open('bootstrap-unavailable');
  await page.route('**/api/bootstrap', route => route.abort('failed')); await page.reload();
  await page.getByText('暂时无法连接本机服务', { exact: true }).waitFor();
  check(await page.getByRole('button', { name: '关闭窗口', exact: true }).isEnabled(), 'bootstrap-unavailable: fresh renderer has enabled native close before bootstrap succeeds');
  await windowButtons('bootstrap-unavailable'); await capture('bootstrap-unavailable-electron'); await closeByButton('bootstrap-unavailable');

  page = await open('preference-save-stalled'); let pendingSave = false;
  await page.route('**/api/preferences', route => { if (route.request().method() === 'POST') { pendingSave = true; return; } return route.continue(); });
  await page.getByRole('searchbox').fill(String(current.host.pid));
  await until(() => pendingSave, 'real preference POST remains stalled without response');
  await closeByButton('preference-save-stalled');

  page = await open('browser', false);
  check(await page.evaluate(() => typeof window.taskManagerWindow === 'undefined') && await page.getByRole('button', { name: '关闭窗口', exact: true }).isDisabled(), 'ordinary browser: bridge absent and desktop controls disabled');
  await cleanup();

  const ts = await import(pathToFileURL(path.join(root, 'src/TaskManager.Web/node_modules/typescript/lib/typescript.js')).href);
  const source = await readFile(path.join(root, 'src/TaskManager.Web/src/api.ts'), 'utf8');
  const compiled = ts.default.transpileModule(source, { compilerOptions: { target: ts.default.ScriptTarget.ES2022, module: ts.default.ModuleKind.ES2022 } }).outputText;
  const moduleFile = path.join(temporary, 'api-classification.mjs'); await writeFile(moduleFile, compiled);
  const module = await import(pathToFileURL(moduleFile).href);
  check(module.errorMessage(new TypeError('client field is not iterable')) === 'client field is not iterable', 'classification: client TypeError keeps its actual cause instead of claiming disconnection');
  }
  check(rendererErrors.length === 0, 'all scenarios: no renderer JavaScript exception');
} catch (error) {
  failure = { name: error.name, message: error.message, stack: error.stack };
  if (current?.page && !current.page.isClosed()) await capture('failure').catch(() => {});
} finally {
  await cleanup().catch(error => { failure ??= { message: 'cleanup: ' + error.message }; });
  await rm(temporary, { recursive: true, force: true });
  const report = { result: failure ? 'FAIL' : 'PASS', checkedAtUtc: new Date().toISOString(), checks, assertions: checks.length, timings, launches, screenshots, rendererErrors, nativeFacts, failure, limitations: ['Origin/unknown IPC command rejection inspected in production source; raw untrusted IPC injection was not attempted.', 'Physical secure-desktop UAC interaction and installation are outside this isolated lifecycle test.'] };
  await writeFile(path.join(output, 'results.json'), JSON.stringify(report, null, 2)); console.log(JSON.stringify(report, null, 2));
}
if (failure) process.exitCode = 1;
