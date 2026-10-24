import { clickCommand, revealControl, openProcessFilter, resourceTiles, resourceTile } from './layout-ui-support.mjs';
import { spawn, spawnSync } from 'node:child_process';
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';
import { assertions, check, delay, until, alive, processTree, ownedAlive, portOpen } from './e2e-support.mjs';

// Keep only bounded, redacted startup facts. Electron.NET forwards its socket
// authentication token to stdout, so raw process output must never be persisted.
const DIAGNOSTIC_LINE_LIMIT = 80, DIAGNOSTIC_LINE_LENGTH = 1800;
function redactDiagnostic(value) {
  return String(value)
    .replace(/\u001b\[[0-9;]*[A-Za-z]/g, '')
    .replace(/((?:electronAuthToken|authToken|X-TaskManager-Token|authorization|token|secret)["']?\s*[=:]\s*)(?:"[^"]*"|'[^']*'|[^\s,;&]+)/gi, '$1[REDACTED]')
    .replace(/(Electron Socket:[^\r\n]*?\busing\s+)\S+/gi, '$1[REDACTED]')
    .replace(/\b(?:https?|wss?):\/\/[^\s<>"']+/gi, value => {
      try { const url = new URL(value); return `${url.protocol}//${url.host}${url.pathname}${url.search || url.hash ? '?[REDACTED]' : ''}`; }
      catch { return '[REDACTED URL]'; }
    })
    .replace(/\b[a-f0-9]{32,}\b/gi, '[REDACTED HEX]');
}
function diagnosticError(error) { return { name: String(error?.name ?? 'Error'), message: redactDiagnostic(error?.message ?? error).slice(0, 3000) }; }
function captureStartupStream(stream, source, lines) {
  let pending = '', dropping = false;
  const append = value => { lines.push({ source, line: redactDiagnostic(value).slice(0, DIAGNOSTIC_LINE_LENGTH) }); if (lines.length > DIAGNOSTIC_LINE_LIMIT) lines.shift(); };
  stream.setEncoding('utf8');
  stream.on('data', chunk => {
    // Redact only complete lines so a token split across chunks cannot escape.
    const pieces = (pending + chunk).split(/\r?\n/); pending = pieces.pop();
    for (const line of pieces) {
      if (dropping || line.length > 16384) append('[oversized startup line omitted]'); else append(line);
      dropping = false;
    }
    if (pending.length > 16384) { pending = ''; dropping = true; }
  });
  stream.on('end', () => { if (dropping) append('[oversized startup line omitted]'); else if (pending) append(pending); });
  stream.on('error', error => append(`startup stream error: ${error.message}`));
}
async function boundedDiagnostic(promise, label, timeout = 2500) {
  let timer;
  try { return await Promise.race([promise, new Promise((_, reject) => { timer = setTimeout(() => reject(new Error(`${label} timed out`)), timeout); })]); }
  finally { clearTimeout(timer); }
}

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const directory = path.resolve(process.env.TASKMANAGER_RELEASE_DIRECTORY ?? path.join(root, 'output/TaskManager-win-x64'));
const executable = path.join(directory, 'TaskManager.Host.exe');
const workloadExe = path.join(root, 'tests/TaskManager.TestWorkload/bin/Release/net10.0-windows10.0.19041.0/TaskManager.TestWorkload.exe');
const probeExe = path.join(root, 'tests/TaskManager.WindowProbe/bin/Release/net10.0-windows10.0.19041.0/TaskManager.WindowProbe.exe');
const output = path.join(root, 'output/playwright'), temporary = await mkdtemp(path.join(tmpdir(), 'taskmanager-desktop-'));
const port = Number(process.env.TASKMANAGER_DESKTOP_PORT ?? 5193), cdp = Number(process.env.TASKMANAGER_CDP_PORT ?? 9224);
const origin = `http://127.0.0.1:${port}`;
await mkdir(output, { recursive: true });
if (await portOpen(port) || await portOpen(cdp)) throw new Error('Desktop test ports already in use');
const startup = { startedAtUtc: new Date().toISOString(), phase: 'spawn', environment: { platform: process.platform, architecture: process.arch, node: process.version, ci: process.env.CI === 'true', githubActions: process.env.GITHUB_ACTIONS === 'true' }, facts: [], logs: [] };
const host = spawn(executable, [`--port=${port}`], { cwd: tmpdir(), env: { ...process.env, TASKMANAGER_DATA_DIR: path.join(temporary, 'preferences'), TASKMANAGER_CDP_PORT: String(cdp) }, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
captureStartupStream(host.stdout, 'stdout', startup.logs); captureStartupStream(host.stderr, 'stderr', startup.logs);
host.on('error', error => { startup.spawnError = diagnosticError(error); });
const independent = spawn(workloadExe, [temporary, '600'], { cwd: root, windowsHide: true, stdio: 'ignore' });
let browser, target, tree = [], page, diagnosticSession;
const screenshots = [];
function applicationPage() {
  return browser?.contexts().flatMap(context => context.pages()).find(candidate => { try { return new URL(candidate.url()).origin === origin; } catch { return false; } });
}
async function collectStartupFacts(phase) {
  const fact = { phase, elapsedMs: Date.now() - Date.parse(startup.startedAtUtc), host: { pid: host.pid, exitCode: host.exitCode, signalCode: host.signalCode }, browserConnected: browser?.isConnected() ?? false, contexts: browser?.contexts().slice(0, 4).map(context => ({ pages: context.pages().slice(0, 10).map(candidate => ({ url: redactDiagnostic(candidate.url()).slice(0, 512), closed: candidate.isClosed() })) })) ?? [] };
  try {
    const response = await fetch(origin + '/api/health', { signal: AbortSignal.timeout(2500) });
    const health = await response.json();
    fact.health = { status: response.status, ready: health.ready, processId: health.processId, platform: health.platform, nativeSource: health.nativeSource, json: health.json };
  } catch (error) { fact.healthError = diagnosticError(error); }
  if (browser?.isConnected()) {
    try {
      diagnosticSession ??= await boundedDiagnostic(browser.newBrowserCDPSession(), 'CDP diagnostic session');
      const version = await boundedDiagnostic(diagnosticSession.send('Browser.getVersion'), 'Browser.getVersion');
      fact.cdpVersion = { protocolVersion: version.protocolVersion, product: version.product, revision: version.revision, jsVersion: version.jsVersion };
      const targets = await boundedDiagnostic(diagnosticSession.send('Target.getTargets'), 'Target.getTargets');
      fact.targets = targets.targetInfos.slice(0, 20).map(info => ({ targetId: info.targetId, type: info.type, attached: info.attached, url: redactDiagnostic(info.url).slice(0, 512) }));
    } catch (error) { fact.cdpError = diagnosticError(error); }
  }
  startup.facts.push(fact); if (startup.facts.length > 12) startup.facts.shift();
}
async function waitForApplicationPage() {
  // Electron.NET's actual socket-bridge startup allows 120 seconds. Health and
  // CDP may be ready before that bridge calls CreateWindowAsync on a cold runner.
  // Wait for an existing real app target; never create or navigate a substitute.
  const deadline = Date.now() + 130000; let nextFactsAt = 0;
  while (Date.now() < deadline) {
    const existing = applicationPage(); if (existing) return existing;
    if (startup.spawnError || host.exitCode !== null || !browser.isConnected()) throw new Error('Electron host exited or CDP disconnected before the real application page appeared');
    if (Date.now() >= nextFactsAt) { await collectStartupFacts('waiting-for-application-page'); nextFactsAt = Date.now() + 15000; }
    await delay(150);
  }
  throw new Error('official Electron.NET creates BrowserWindow: no existing page at the application origin within 130 seconds');
}
async function saveStartupDiagnostics(error) {
  await collectStartupFacts(startup.phase);
  // Reuse identities already collected for acceptance; don't add an unbounded
  // synchronous CIM query to the failure-diagnostic path.
  startup.ownedProcesses = tree.slice(0, 24).map(entry => ({ pid: entry.pid, created: entry.created, image: path.basename(entry.imagePath ?? '') }));
  startup.failure = diagnosticError(error);
  const diagnosticPath = path.join(output, 'final-desktop-startup.json');
  await writeFile(diagnosticPath, JSON.stringify(startup, null, 2));
  console.error(JSON.stringify({ startupDiagnostics: diagnosticPath, phase: startup.phase, failure: startup.failure, facts: startup.facts.slice(-2), logs: startup.logs.slice(-16) }, null, 2));
}
async function capture(name, nativeSurface = false) {
  if (nativeSurface) {
    const session = await page.context().newCDPSession(page);
    const result = await session.send('Page.captureScreenshot', { format: 'png', fromSurface: true, captureBeyondViewport: false });
    await writeFile(path.join(output, name), Buffer.from(result.data, 'base64')); await session.detach();
  } else await page.screenshot({ path: path.join(output, name) });
  screenshots.push(name);
}
try {
  startup.phase = 'waiting-for-host-and-cdp';
  await until(async () => (await (await fetch(origin + '/api/health', { signal: AbortSignal.timeout(2500) })).json()).ready && await portOpen(cdp), 'real Release Electron host becomes ready', 45000);
  startup.phase = 'connecting-to-cdp';
  browser = await chromium.connectOverCDP(`http://127.0.0.1:${cdp}`, { timeout: 15000 });
  startup.phase = 'waiting-for-application-page';
  page = await waitForApplicationPage();
  await collectStartupFacts('real-application-page-discovered');
  startup.phase = 'real-desktop-acceptance';
  const errors = []; page.on('pageerror', error => errors.push(error.message));
  await page.getByRole('table', { name: '进程列表', exact: true }).waitFor();
  const bootstrap = await (await fetch(origin + '/api/bootstrap')).json();
  check(bootstrap.desktop === true, 'A18 production Release bootstrap reports actual Electron BrowserWindow');
  check(await page.getByRole('button', { name: '最小化窗口', exact: true }).isEnabled(), 'A18 actual desktop controls enabled');
  check(await page.evaluate(() => typeof require === 'undefined' && typeof process === 'undefined'), 'A18 renderer has no Node globals (contextIsolation, sandbox and nodeIntegration boundary)');
  check(await page.evaluate(() => location.origin) === origin, 'A18 production UI loads exact loopback origin');
  tree = processTree(host.pid);
  check(tree.length >= 3, 'A18 independent Windows query confirms .NET host and Electron descendants');
  const native = (...args) => {
    const result = spawnSync(probeExe, [tree.map(entry => `${entry.pid}:${entry.fileTime}`).join(','), ...args.map(String)], { encoding: 'utf8', windowsHide: true });
    if (result.status !== 0) throw new Error('Independent native window probe failed: ' + result.stderr);
    return JSON.parse(result.stdout);
  };
  const bounds = () => native();
  const resize = async (width, height) => {
    native('resize', width, height);
    await delay(450);
  };
  for (const [width, height] of [[1380, 860], [960, 640], [1600, 1000]]) {
    await resize(width, height);
    check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `A17 real Electron ${width}x${height} no page horizontal overflow`);
    check(await page.getByRole('button', { name: '运行新任务', exact: true }).isVisible(), `A17 ${width}x${height} action toolbar visible`);
    await capture(`final-desktop-${width}.png`);
  }
  await resize(1380, 860);
  // Electron persists per-origin Chromium zoom between launches. Reset through
  // the actual keyboard action before taking the device-scale baseline.
  await page.keyboard.press('Control+0'); await delay(400);
  const oldDpr = await page.evaluate(() => devicePixelRatio);
  await page.keyboard.press('Control+=');
  await until(async () => Math.abs((await page.evaluate(() => devicePixelRatio)) / oldDpr - 1.25) < .02, 'actual Electron zoom reaches 125%');
  await delay(500);
  check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'A17 Electron SetZoomFactor 125% has no page overflow');
  // innerWidth/innerHeight are rounded integers; layout rects retain fractions.
  check(await page.getByRole('button', { name: '关闭窗口', exact: true }).evaluate(element => { const rect = element.getBoundingClientRect(); return rect.right <= innerWidth + 1 && rect.top >= 0; }), 'A17 window controls stay inside actual 125% layout viewport');
  check(await page.getByRole('button', { name: '运行新任务', exact: true }).evaluate(element => { const rect = element.getBoundingClientRect(); return rect.right <= innerWidth + 1 && rect.bottom <= innerHeight + 1; }), 'A17 primary action stays inside actual 125% layout viewport');
  await capture('final-desktop-125-percent.png', true);
  await page.keyboard.press('Control+0');
  await until(async () => Math.abs(await page.evaluate(() => devicePixelRatio) - oldDpr) < .02, 'zoom reset');
  await page.emulateMedia({ reducedMotion: 'reduce' });
  check(await page.getByRole('button', { name: '运行新任务', exact: true }).evaluate(element => getComputedStyle(element).transitionDuration.split(',').every(value => parseFloat(value) === 0)), 'A17 reduced-motion removes nonessential CSS transitions');
  await page.keyboard.press('Control+f');
  const search = page.getByRole('searchbox', { name: '搜索名称、发布者、路径或 PID' });
  check(await search.evaluate(element => element === document.activeElement), 'A16 Ctrl+F focuses accessible global search');
  await search.fill(String(independent.pid));
  await until(async () => await page.locator('tbody tr[data-row-key]').count() === 1, 'real PID search');
  check(await page.locator('tbody').innerText().then(text => text.includes('TaskManager.TestWorkload')), 'A18 exact PID search finds independently spawned real test process');
  await search.fill('');
  await clickCommand(page, '冻结显示');
  await page.getByRole('button', { name: '继续显示最新数据', exact: true }).waitFor();
  await delay(2200);
  await page.getByRole('button', { name: '按内存排序', exact: true }).click();
  check(await page.getByRole('columnheader').filter({ has: page.getByRole('button', { name: '按内存排序', exact: true }) }).getAttribute('aria-sort') === 'ascending', 'A06 memory header exposes ascending numeric sort');
  const memoryIndex = (await page.getByRole('columnheader').allTextContents()).findIndex(text=>text.includes('内存'));
  const displayed = await page.locator('tbody tr[data-row-key]').evaluateAll((rows,index)=>rows.map(row=>row.cells[index]?.innerText),memoryIndex);
  const values = displayed.map(text=>{const match=/([\d.]+)\s*(B|KB|MB|GB|TB)/.exec(text??'');return match?Number(match[1])*1024**['B','KB','MB','GB','TB'].indexOf(match[2]):null;}).filter(value=>value!==null);
  check(values.length > 3 && values.every((value, index) => index === 0 || value >= values[index - 1]), 'A06 visible real working-set values sort numerically ascending');
  await clickCommand(page, '继续显示');
  await page.locator('.frozen-banner').waitFor({state:'detached'});
  for (const name of ['性能', '应用历史记录', '启动应用', '用户', '详细信息', '服务', '设置', '进程']) {
    await page.getByRole('button', { name, exact: true }).click();
    check(await page.getByRole('heading', { name, exact: true, level: 1 }).isVisible(), `A18 real desktop navigation ${name}`);
  }
  await page.getByRole('button', { name: '运行新任务', exact: true }).click();
  const dialog = page.getByRole('dialog');
  const focusables = dialog.locator('button:not(:disabled), input:not(:disabled), select:not(:disabled), [tabindex="0"]');
  await focusables.last().focus(); await page.keyboard.press('Tab');
  check(await focusables.first().evaluate(element => document.activeElement === element), 'A16 dialog Tab wraps last to first');
  await page.keyboard.press('Shift+Tab');
  check(await focusables.last().evaluate(element => document.activeElement === element), 'A16 dialog Shift+Tab wraps first to last');
  await page.keyboard.press('Escape');
  check(await page.getByRole('button', { name: '运行新任务', exact: true }).evaluate(element => document.activeElement === element), 'A16 dialog Escape restores trigger focus');
  await page.getByRole('button', { name: '运行新任务', exact: true }).click();
  await dialog.getByLabel('程序名称或完整路径', { exact: true }).fill(workloadExe);
  await dialog.getByLabel('参数（可选）', { exact: true }).fill(`"${path.join(temporary, 'ui-created')}" 120`);
  const response = page.waitForResponse(response => response.url().endsWith('/api/processes/start') && response.request().method() === 'POST');
  await dialog.getByRole('button', { name: '运行', exact: true }).click();
  target = await (await response).json();
  check(target.pid > 0 && alive(target.pid), 'A09/A18 desktop UI CreateProcess produces independently live Windows PID');
  check(processTree(host.pid).some(entry => entry.pid === target.pid), 'A09 independent Windows process-tree query confirms UI-created target');
  await search.fill(String(target.pid));
  await until(async () => await page.locator('tbody tr[data-row-key]').count() === 1 && await page.locator('tbody').innerText().then(text => text.includes('TaskManager.TestWorkload')), 'UI-created target enters actual collector snapshot');
  await page.locator('tbody tr[data-row-key]').first().click();
  await page.getByRole('button', { name: '结束任务', exact: true }).click();
  await dialog.getByRole('button', { name: '取消', exact: true }).click();
  check(alive(target.pid), 'A10 desktop cancel leaves target alive');
  await page.getByRole('button', { name: '结束任务', exact: true }).click();
  await capture('final-desktop-terminate-confirm.png');
  await dialog.getByRole('button', { name: '结束进程', exact: true }).click();
  await until(() => !alive(target.pid), 'desktop confirmed termination exits exact target');
  await until(async () => await page.locator('tbody tr[data-row-key]').count() === 0, 'terminated identity removed from real UI');
  check(!alive(target.pid) && alive(independent.pid), 'A10/A18 only selected UI-created target exits, independent workload survives');
  await page.getByRole('dialog').getByRole('button', { name: '关闭', exact: true }).click();
  target = undefined;
  await search.fill('');
    await page.evaluate(() => { const a = document.createElement('a'); a.href = 'https://example.com/'; a.textContent = 'test external navigation'; a.id = 'e2e-external'; Object.assign(a.style, {position: 'fixed', top:'100px',left:'300px',zIndex:'99999'}); document.body.append(a); });
  await page.locator('#e2e-external').click({ noWaitAfter: true }); await delay(300);
  check(await page.evaluate(() => location.origin) === origin, 'A18 synchronous main-process guard blocks external navigation');
  // Electron correctly prevents navigation, while CDP leaves a pending
  // navigation signal. A real same-origin reload resets the automation state.
  await page.goto(origin);
  await page.getByRole('table', { name: '进程列表', exact: true }).waitFor();
  const pagesBefore = page.context().pages().length;
  await page.evaluate(() => window.open('https://example.com/')); await delay(300);
  check(page.context().pages().length === pagesBefore, 'A18 external new window is denied');
  await page.getByRole('button', { name: '最大化或还原窗口', exact: true }).click();
  await until(() => bounds().state === 3, 'UI maximizes actual native BrowserWindow');
  check(true, 'A18 maximize changes actual native window state');
  await page.getByRole('button', { name: '最大化或还原窗口', exact: true }).click();
  await until(() => bounds().state === 1, 'UI restores window');
  check(true, 'A18 maximize toggles back to normal');
  await page.getByRole('button', { name: '最小化窗口', exact: true }).click();
  await until(() => bounds().state === 2, 'UI minimizes actual window');
  check(true, 'A18 minimize changes actual native window state');
  native('restore');
  await delay(350); await capture('final-desktop-complete.png');
  check(errors.length === 0, 'A18 zero renderer runtime errors throughout actual desktop flow');
  tree = processTree(host.pid);
  await page.getByRole('button', { name: '关闭窗口', exact: true }).click();
  await until(() => host.exitCode !== null, 'native window close exits exact .NET host', 15000);
  await until(async () => !await portOpen(port) && !await portOpen(cdp), 'desktop close releases HTTP and CDP ports');
  check(tree.every(entry => !ownedAlive(entry)), 'A18 all exact host/Electron PID+creation identities exited after native close');
  check(alive(independent.pid), 'A18 desktop shutdown preserves unrelated test-owned workload');
  const report = { result: 'PASS', assertions: assertions.length, checkedAtUtc: new Date().toISOString(), hostPid: host.pid, checks: assertions, screenshots, source: 'Release .NET -> official Electron.NET -> React -> HTTP -> CsWin32 -> Windows; no mocks' };
  await writeFile(path.join(output, 'final-desktop-results.json'), JSON.stringify(report, null, 2));
  console.log(JSON.stringify(report, null, 2));
} catch (error) {
  await saveStartupDiagnostics(error).catch(diagnosticFailure => console.error('Could not save startup diagnostics:', diagnosticError(diagnosticFailure)));
  await page?.screenshot({ path: path.join(output, 'final-desktop-failure.png'), timeout: 5000 }).catch(() => {});
  const safeError = new Error(redactDiagnostic(error?.message ?? error)); safeError.stack = redactDiagnostic(error?.stack ?? safeError.stack); throw safeError;
}
finally {
  if (target && alive(target.pid)) { try { process.kill(target.pid); } catch {} }
  if (host.pid && host.exitCode === null) {
    // Exact test-owned identities only; these are never selected by executable name.
    for (const entry of processTree(host.pid).reverse()) { if (ownedAlive(entry)) { try { process.kill(entry.pid); } catch {} } }
    host.kill();
  }
  independent.kill(); await browser?.close().catch(() => {});
  await delay(500); await rm(temporary, { recursive: true, force: true });
}
