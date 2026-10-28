import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { spawn, spawnSync } from 'node:child_process';
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { chromium } from 'playwright';
import { delay, until, portOpen, processTree, ownedAlive } from './e2e-support.mjs';
import { stopOwnedHost } from './etw-support.mjs';
import { probe, windows, nativeUi } from './native-ui-support.mjs';
import { clickCommand } from './layout-ui-support.mjs';

const root = path.resolve(import.meta.dirname, '..');
const mode = process.env.TASKMANAGER_STARTUP_MODE ?? 'browser', desktop = mode === 'desktop';
assert.ok(['browser', 'desktop'].includes(mode));
const release = path.resolve(process.env.TASKMANAGER_STARTUP_RELEASE_DIRECTORY ?? path.join(root, 'src/TaskManager.Host/bin/Release/net10.0-windows10.0.19041.0'));
const output = path.join(root, 'output/playwright/startup-status');
const port = 5354, cdp = 9354, origin = `http://127.0.0.1:${port}`;
const checks = [], screenshots = [], errors = [], forbiddenWrites = [];
const check = (condition, message) => { assert.ok(condition, message); checks.push(message); };
const hash = value => createHash('sha256').update(value).digest('hex');
const label = enabled => enabled === true ? '已启用' : enabled === false ? '已禁用' : '无法确定';
await mkdir(output, { recursive: true });
check(probe('process-elevation', process.pid).elevated, 'controller uses a real elevated Windows token');
check(!await portOpen(port) && !await portOpen(cdp), 'dedicated acceptance ports are initially free');
const temporary = await mkdtemp(path.join(tmpdir(), 'takao-startup-status-'));
const manifest = path.join(temporary, 'fixtures.json');
function fixtures(action, { state, rows } = {}) {
  const result = spawnSync('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.join(root, 'tests/startup-status-fixtures.ps1'), '-Action', action, '-ManifestPath', manifest, ...(state ? ['-State', String(state)] : [])], {
    encoding: 'utf8', windowsHide: true, timeout: 45000, input: rows === undefined ? undefined : JSON.stringify(rows)
  });
  // PowerShell parse errors can echo their input; never put raw oracle stderr in artifacts.
  assert.equal(result.status, 0, `fixture ${action}: PowerShell exit ${result.status}; raw input/output omitted`);
  return JSON.parse(result.stdout);
}
const artifacts = [], index = await readFile(path.join(release, 'wwwroot/index.html'), 'utf8');
for (const file of ['TaskManager.Host.exe', 'TaskManager.Host.dll', 'wwwroot/index.html', ...[...index.matchAll(/(?:src|href)="([^\"]+\.(?:js|css))"/g)].map(match => 'wwwroot/' + match[1].replace(/^\//, ''))]) {
  const bytes = await readFile(path.join(release, file)); artifacts.push({ file, sha256: hash(bytes), bytes: bytes.length });
}
const revision = spawnSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8', windowsHide: true }).stdout.trim();
let fixture, host, browser, page, token, snapshot, tree = [], failure, cleanup = false, cleanupFacts, comparisons;
const cleanupErrors = [];
async function api(route, body) {
  const response = await fetch(`${origin}/api/${route}`, { method: body === undefined ? 'GET' : 'POST', headers: { Origin: origin, ...(body === undefined ? {} : { 'Content-Type': 'application/json', 'X-TaskManager-Token': token }) }, body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(15000) });
  assert.ok(response.ok, `${route}: ${response.status}`); return response.json();
}
const expected = { enabled: true, disabled: false, unknown1: null, unknown6: null, unknown7: null, badtype: null, short: null, long: null, missing: true, once: null, conflict: null, 'default-conflict': null, 'folder-enabled': true, 'folder-disabled': false, 'common-disabled': false };
const suffix = row => row.name.slice(fixture.prefix.length);
function rowFor(item) {
  return page.locator('tbody tr[data-row-key]').filter({ has: page.getByText(item.name, { exact: true }) }).filter({ has: page.getByText(item.source, { exact: true }) });
}
async function viewport(width, height) {
  if (desktop) {
    probe(tree.map(entry => `${entry.pid}:${entry.fileTime}`).join(','), 'resize', width, height);
    await until(async () => await page.evaluate(({ width, height }) => innerWidth === width && innerHeight === height, { width, height }), 'native Electron viewport resize');
  } else await page.setViewportSize({ width, height });
}
async function capture(name, native = false) {
  const file = `${mode}-${name}.png`, destination = path.join(output, file);
  if (desktop) {
    const session = await page.context().newCDPSession(page);
    const result = await session.send('Page.captureScreenshot', { format: 'png', fromSurface: true, captureBeyondViewport: false });
    await session.detach(); await writeFile(destination, Buffer.from(result.data, 'base64'));
  } else await page.screenshot({ path: destination });
  screenshots.push(file);
  if (native) {
    const window = windows('').find(window => tree.some(entry => entry.pid === window.pid) && !window.minimized && window.title);
    check(Boolean(window), 'native screenshot targets an exact test-owned Electron window');
    const nativeFile = `${mode}-${name}-native.png`; nativeUi(window, 'capture', path.join(output, nativeFile)); screenshots.push(nativeFile);
  }
}
async function inspectViewport(name) {
  const facts = await page.evaluate(() => ({ width: innerWidth, height: innerHeight, overflow: document.documentElement.scrollWidth > innerWidth + 1 || document.documentElement.scrollHeight > innerHeight + 1, reduced: matchMedia('(prefers-reduced-motion: reduce)').matches }));
  check(!facts.overflow && facts.reduced, `${name}: no document overflow and reduced motion remains active`);
}
try {
  fixture = fixtures('create');
  check(fixture.values > 20 && fixture.files === 3, 'real isolated Run/RunOnce/Run32 and user/common Startup files were created');
  host = spawn(path.join(release, 'TaskManager.Host.exe'), [...(desktop ? [] : ['--headless']), `--port=${port}`], { cwd: release, windowsHide: true, stdio: 'ignore', env: { ...process.env, TASKMANAGER_DATA_DIR: path.join(temporary, 'preferences'), TASKMANAGER_CDP_PORT: String(cdp) } });
  await until(async () => (await api('health')).ready, 'production host ready', 45000);
  const health = await api('health'); token = (await api('bootstrap')).token;
  check(probe('process-elevation', host.pid).elevated && /CsWin32/.test(health.nativeSource) && /Newtonsoft|JSON.NET/.test(health.json), 'production elevated Host uses CsWin32 and JSON.NET');
  await until(async () => { snapshot = await api('snapshot'); return snapshot.slowDataCapturedAtUtc && snapshot.startupItems.filter(row => row.name.startsWith(fixture.prefix)).length === 17; }, '17 real startup fixture source rows collected', 45000);
  const owned = snapshot.startupItems.filter(row => row.name.startsWith(fixture.prefix));
  const oracle = fixtures('inspect', { rows: snapshot.startupItems.map(({ id, name, source }) => ({ id, name, source })) });
  const oracleById = new Map(oracle.map(row => [row.id, row]));
  for (const row of snapshot.startupItems) assert.equal(row.enabled, oracleById.get(row.id)?.enabled, `independent actual Registry oracle for source ${hash(row.id).slice(0, 12)}`);
  const preexisting = snapshot.startupItems.filter(row => !row.name.startsWith(fixture.prefix));
  comparisons = { total: snapshot.startupItems.length, fixtures: owned.length, existing: preexisting.length, existingEnabled: preexisting.filter(row => row.enabled === true).length, existingDisabled: preexisting.filter(row => row.enabled === false).length, existingUnknown: preexisting.filter(row => row.enabled === null).length, ownStates: Object.fromEntries([...new Set(oracle.map(row => String(row.state)))].map(state => [state, oracle.filter(row => String(row.state) === state).length])) };
  check(preexisting.length > 0 && preexisting.some(row => row.enabled === false) && preexisting.some(row => row.enabled === true), 'existing user startup rows include real enabled and disabled records independently verified without modifying them');
  check(oracle.length === snapshot.startupItems.length, `all ${oracle.length} real source rows match independent managed Registry reads`);
  for (const [name, enabled] of Object.entries(expected)) {
    const rows = owned.filter(row => suffix(row) === name);
    check(rows.length === 1 && rows[0].enabled === enabled && rows[0].enabledReason.length > 10, `${name}: expected nullable state and real reason`);
  }
  const views = owned.filter(row => suffix(row) === 'views');
  check(views.length === 2 && new Set(views.map(row => row.id)).size === 2 && views.every(row => row.command === views[0].command), 'HKLM32 and HKLM64 equal-name/equal-command sources retain separate identities');
  check(views.find(row => row.source.includes('Registry64')).enabled === true && views.find(row => row.source.includes('Registry32')).enabled === false, 'native Shell Run and Run32 records map to their exact HKLM registry views');
  check(owned.filter(row => row.source.startsWith('HKCU')).every(row => !row.source.includes('Registry32')), 'shared HKCU Run views appear only once');
  check(owned.find(row => suffix(row) === 'once').enabledReason.includes('RunOnce') && owned.find(row => suffix(row) === 'missing').enabledReason.includes('默认启用'), 'RunOnce and missing approval have distinct explanations');
  check(owned.find(row => suffix(row) === 'folder-enabled').enabled === true, 'StartupFolder matches the file extension and ignores the opposite suffixless record');
  if (desktop) {
    await until(() => portOpen(cdp), 'official Electron CDP listener', 45000); browser = await chromium.connectOverCDP(`http://127.0.0.1:${cdp}`);
    await until(() => browser.contexts().flatMap(context => context.pages()).some(candidate => candidate.url().startsWith(origin)), 'existing official Electron page', 130000);
    page = browser.contexts().flatMap(context => context.pages()).find(candidate => candidate.url().startsWith(origin));
  } else { browser = await chromium.launch({ headless: true }); page = await browser.newPage({ viewport: { width: 1380, height: 860 }, reducedMotion: 'reduce' }); await page.goto(origin); }
  page.setDefaultTimeout(15000); page.on('pageerror', error => errors.push(error.message));
  page.on('request', request => { if (request.method() === 'POST' && /startup\/(?:enable|disable|control)|task-manager-integration|services\/.*\/control/.test(request.url())) forbiddenWrites.push(new URL(request.url()).pathname); });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.getByRole('table', { name: '进程列表', exact: true }).waitFor();
  if (desktop) { const defer = page.getByRole('button', { name: '暂不替换', exact: true }); if (await defer.isVisible()) await defer.click(); }
  tree = processTree(host.pid); check((await api('bootstrap')).desktop === desktop, `${mode}: actual bootstrap matches the runtime`);
  await page.getByRole('button', { name: '启动应用', exact: true }).click(); await viewport(1380, 860);
  const search = page.getByRole('searchbox'); await search.fill(fixture.prefix);
  await until(async () => await page.locator('tbody tr[data-row-key]').count() === owned.length, 'real fixture rows displayed');
  for (const item of owned) {
    const row = rowFor(item), status = row.locator('.running-state'); await row.waitFor();
    check(await status.textContent() === label(item.enabled) && await status.getAttribute('title') === item.enabledReason, `${suffix(item)} ${item.source.includes('Registry32') ? '32' : ''}: UI state and reason match the real snapshot`);
  }
  check(await page.getByRole('button', { name: 'Windows 启动应用设置', exact: true }).isEnabled(), 'official Windows startup management entry remains available');
  await inspectViewport('1380x860'); await capture('1380-states');
  const column = page.getByRole('columnheader', { name: /启用状态/ });
  for (const direction of ['ascending', 'descending']) {
    await column.getByRole('button', { name: '按启用状态排序', exact: true }).click();
    await until(async () => await column.getAttribute('aria-sort') === direction, `status sort ${direction}`);
    const ids = await page.locator('tbody tr[data-row-key]').evaluateAll(rows => rows.map(row => row.dataset.rowKey));
    const map = new Map(owned.map(row => [row.id, row.enabled]));
    const ranked = ids.map(id => map.get(id) === null ? 2 : direction === 'ascending' ? Number(map.get(id)) : 1 - Number(map.get(id)));
    check(ranked.length === owned.length && ranked.every((rank, i) => !i || rank >= ranked[i - 1]), `${direction}: known states sort in order and unknown values stay last`);
  }
  await page.keyboard.press('Control+f'); check(await search.evaluate(node => node === document.activeElement), 'Ctrl+F focuses the shared startup search');
  const enabled = owned.find(row => suffix(row) === 'enabled'); await search.fill(enabled.name);
  const enabledRow = rowFor(enabled); await enabledRow.focus(); await enabledRow.press('Enter');
  const dialog = page.getByRole('dialog', { name: '启动项详情', exact: true }); await dialog.waitFor();
  check((await dialog.innerText()).includes('已启用') && (await dialog.innerText()).includes(enabled.enabledReason), 'keyboard row activation opens real state and source detail');
  fixtures('mutate', { state: 3 }); const oldTime = snapshot.slowDataCapturedAtUtc; await api('refresh', {});
  await until(async () => { snapshot = await api('snapshot'); return snapshot.slowDataCapturedAtUtc !== oldTime && snapshot.startupItems.find(row => row.id === enabled.id)?.enabled === false; }, 'fresh snapshot observes external real fixture disable', 45000);
  await until(async () => (await dialog.innerText()).includes('已禁用') && !(await dialog.innerText()).includes('状态2'), 'open startup detail follows the refreshed real row');
  check(true, 'existing open detail updates after an external registry change and refresh');
  await capture('detail-disabled'); await page.keyboard.press('Escape'); await until(async () => await dialog.count() === 0, 'Escape closes startup detail');
  check(await enabledRow.locator('.running-state').textContent() === '已禁用', 'refreshed row also shows the real disabled state');
  fixtures('mutate', { state: 2 }); await clickCommand(page, '刷新数据');
  await until(async () => await enabledRow.locator('.running-state').textContent() === '已启用', 'visible refresh returns known fixture state to enabled', 45000);
  check(true, 'visible refresh command re-reads real Windows startup state');
  await search.fill(fixture.prefix); await viewport(960, 640); await inspectViewport('960x640'); await capture('960-states');
  if (desktop) {
    await viewport(1380, 860); await page.keyboard.press('Control+=');
    await until(async () => (await api('desktop/window-state')).zoomFactor === 1.25, 'real Electron zoom factor 125%');
    await inspectViewport('Electron 125%'); await capture('125-states', true);
  }
  check(errors.length === 0 && forbiddenWrites.length === 0, 'no renderer errors, startup writes, integration changes or service controls');
} catch (error) {
  failure = { name: error.name, message: error.message };
  if (page && !page.isClosed()) await capture('failure').catch(() => {});
} finally {
  try {
    if (host && host.exitCode === null) {
      if (desktop && token) { await api('window/close', {}); await until(() => host.exitCode !== null, 'official Electron close exits the owned host', 20000); }
      else await stopOwnedHost(host);
    }
  } catch (error) {
    cleanupErrors.push({ step: 'host-close', message: error.message.slice(0, 600) });
    try { if (host?.exitCode === null) await stopOwnedHost(host); }
    catch (fallback) { cleanupErrors.push({ step: 'owned-host-fallback', message: fallback.message.slice(0, 600) }); }
  }
  try { if (browser) await browser.close(); } catch { /* CDP has already closed with the owned Electron. */ }
  try {
    await until(async () => !await portOpen(port) && !await portOpen(cdp), 'test-owned listeners close', 20000);
    if (tree.length) await until(() => tree.every(entry => !ownedAlive(entry)), 'exact owned process identities exit', 20000);
  } catch (error) { cleanupErrors.push({ step: 'listeners-and-processes', message: error.message.slice(0, 600) }); }
  // Always attempt registry/file cleanup, even when a preceding shutdown check failed.
  try {
    cleanupFacts = fixtures('cleanup');
    check(cleanupFacts?.cleanup === true, 'all exact test-created startup values/files are cleaned without changing user items');
  } catch (error) { cleanupErrors.push({ step: 'startup-fixtures', message: error.message.slice(0, 600) }); }
  cleanup = cleanupErrors.length === 0 && cleanupFacts?.cleanup === true;
  // A failed cleanup retains its manifest so only these exact identities can be recovered.
  if (cleanup) await rm(temporary, { recursive: true, force: true });
  if (cleanupErrors.length) failure ??= { name: 'CleanupError', message: 'Acceptance cleanup did not fully succeed; see the bounded cleanupErrors and retained fixture manifest.' };
}
const report = { result: failure ? 'FAIL' : 'PASS', mode, checkedAtUtc: new Date().toISOString(), revision, testedProductionCommit: 'd7935d2', releaseDirectory: release, executable: 'TaskManager.Host.exe', platform: process.platform, node: process.version, assertions: checks.length, checks, comparisons, artifacts, screenshots, errors, forbiddenWrites, cleanup, cleanupFacts, cleanupErrors, ...(!cleanup ? { recoveryManifest: manifest } : {}), notExecuted: ['Actual user startup policy permissions failures were not forced; existing user policies were preserved.', 'States 6/7 are tested as unknown format values; their Windows Shell enabled/disabled semantics are not claimed.', 'No user startup command was executed and no startup-writing product endpoint was added.'], ...(failure ? { failure } : {}) };
await writeFile(path.join(output, `${mode}-results.json`), JSON.stringify(report, null, 2));
console.log(JSON.stringify({ result: report.result, assertions: report.assertions, mode, cleanup, comparisons, ...(failure ? { failure } : {}) }, null, 2));
if (failure) process.exitCode = 1;
