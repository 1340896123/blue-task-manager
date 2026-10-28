import { stopOwnedHost } from './etw-support.mjs';
import { clickCommand, revealControl, openProcessFilter, resourceTiles, resourceTile } from './layout-ui-support.mjs';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';
import { delay, until, processTree, ownedAlive, portOpen } from './e2e-support.mjs';

// No request interception or synthetic snapshots: both modes use the production
// Host, JSON.NET endpoints and CsWin32 collectors. Only dedicated tasks are ended.
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const framework = 'net10.0-windows10.0.19041.0';
const desktop = process.env.TASKMANAGER_SHARED_SEARCH_DESKTOP === '1';
const release = path.resolve(process.env.TASKMANAGER_RELEASE_DIRECTORY ?? path.join(root, 'src/Takao.Host/bin/Release', framework));
const executable = path.join(release, 'Takao.Host.exe');
const workload = path.join(root, 'tests/Takao.TestWorkload/bin/Release', framework, 'Takao.TestWorkload.exe');
const port = Number(process.env.TASKMANAGER_SHARED_SEARCH_PORT ?? 5290), cdp = Number(process.env.TASKMANAGER_SHARED_SEARCH_CDP ?? 9290);
const origin = `http://127.0.0.1:${port}`, mode = desktop ? 'desktop' : 'browser';
const temporary = await mkdtemp(path.join(tmpdir(), 'taskmanager-shared-search-'));
const dataDirectory = path.join(temporary, 'preferences'), preferenceFile = path.join(dataDirectory, 'preferences.json');
const output = path.join(root, 'output/playwright');
await mkdir(output, { recursive: true });
const checks = [], skipped = [], errors = [], forbiddenWrites = [], screenshots = [], owned = [];
let host, browser, page, token, integrationGets = 0;
const hostExited = () => host && (host.exitCode !== null || host.signalCode !== null);
const check = (condition, message) => { assert.ok(condition, message); checks.push(message); };
const get = async route => (await fetch(`${origin}/api/${route}`)).json();
async function request(route, body, headers = {}) {
  return fetch(`${origin}/api/${route}`, { method: 'POST', headers: { 'Content-Type': 'application/json', Origin: origin, 'X-TaskManager-Token': token, ...headers }, body: JSON.stringify(body) });
}
async function post(route, body) { const response = await request(route, body); assert.ok(response.ok, `${route}: HTTP ${response.status}`); return response.json(); }
const search = () => page.getByRole('searchbox');
async function navigate(name) { await page.getByRole('button', { name, exact: true }).click(); }
async function clear() {
  if (await search().inputValue()) await page.getByRole('button', { name: '清空搜索', exact: true }).click();
  check(await search().inputValue() === '', 'clear removes the shared query');
  check(await search().evaluate(node => node === document.activeElement), 'clear returns keyboard focus to the shared input');
}
async function capture(name) { const target = path.join(output, `shared-search-${mode}-${name}.png`); await page.screenshot({ path: target }); screenshots.push(target); }
async function start() {
  assert.equal(await portOpen(port), false, 'dedicated HTTP port is free');
  if (desktop) assert.equal(await portOpen(cdp), false, 'dedicated CDP port is free');
  host = spawn(executable, [...(desktop ? [] : ['--headless']), `--port=${port}`], { cwd: tmpdir(), env: { ...process.env, TASKMANAGER_DATA_DIR: dataDirectory, ...(desktop ? { TASKMANAGER_CDP_PORT: String(cdp) } : {}) }, stdio: 'ignore', windowsHide: true });
  host.on('error', error => errors.push(error.message));
  await until(async () => (await get('health')).ready, 'production Host ready', 45000);
  const health = await get('health');
  check(health.platform === 'Windows' && /CsWin32/.test(health.nativeSource) && /Newtonsoft|JSON.NET/.test(health.json), 'health confirms Windows, CsWin32 and JSON.NET');
  const bootstrap = await get('bootstrap'); token = bootstrap.token;
  await until(async () => (await get('snapshot')).slowDataCapturedAtUtc != null, 'real slow Windows sources sampled', 45000);
  if (desktop) {
    await until(() => portOpen(cdp), 'actual Electron CDP ready', 45000);
    browser = await chromium.connectOverCDP(`http://127.0.0.1:${cdp}`);
    await until(() => browser.contexts().flatMap(context => context.pages()).some(candidate => candidate.url().startsWith(origin)), 'official Electron.NET BrowserWindow exists', 130000);
    page = browser.contexts().flatMap(context => context.pages()).find(candidate => candidate.url().startsWith(origin));
  } else {
    browser = await chromium.launch({ headless: true });
    page = await browser.newPage({ viewport: { width: 1380, height: 860 }, reducedMotion: 'reduce' });
    await page.goto(origin);
  }
  page.on('pageerror', error => errors.push(error.message));
  page.on('request', req => {
    if (req.url().endsWith('/api/desktop/task-manager-integration')) {
      if (req.method() === 'GET') integrationGets++; else forbiddenWrites.push(req.url());
    }
    if (/\/api\/services\/.*\/control$/.test(req.url())) forbiddenWrites.push(req.url());
  });
  await search().waitFor();
  await until(async () => !await search().isDisabled(), 'shared preferences loaded');
  if (desktop) await until(async () => (await get('bootstrap')).desktop, 'official desktop window initialization completes', 30000);
  check((await get('bootstrap')).desktop === desktop, 'the actual production Host mode matches the requested test mode');
  return bootstrap;
}
async function stop() {
  if (!host || hostExited()) { await browser?.close().catch(() => {}); return; }
  const identities = processTree(host.pid);
  if (desktop) {
    await post('window/close', {});
    await until(hostExited, 'Electron closes its own Host', 30000);
  } else { await browser?.close(); browser = null; await stopOwnedHost(host); await until(hostExited, 'headless test Host exits'); }
  await until(async () => !await portOpen(port) && (!desktop || !await portOpen(cdp)), 'own HTTP/CDP ports close');
  check(identities.every(identity => !ownedAlive(identity)), 'closing leaves no processes belonging to this test Host');
  await browser?.close().catch(() => {}); browser = null;
}
async function waitSaved(query) { await until(async () => JSON.parse(await readFile(preferenceFile, 'utf8')).ui.searchQuery === query, 'JSON.NET saves current shared query'); }
async function locateReplayGuard(query) {
  await search().fill(query); await navigate('性能'); await navigate('详细信息');
  check(await search().inputValue() === query, 'remounting process workspace never replays the consumed old PID location');
  await page.locator('.empty-state').waitFor();
}
try {
  const bootstrap = await start();
  const absent = `absent-shared-search-${Date.now()}`;
  await search().fill(absent);
  const names = ['进程', '性能', '应用历史记录', '启动应用', '用户', '详细信息', '服务', '设置'];
  for (const name of names) {
    await navigate(name);
    check(await search().inputValue() === absent, `${name} keeps the exact shared query on entry`);
    await page.locator('.empty-state').waitFor();
    check(await page.locator('tbody tr[data-row-key]').count() === 0, `${name} applies the query immediately without stale rows`);
    check(await page.locator('.resource-chart:visible,.settings-section:visible').count() === 0, `${name} has no unrelated graph or settings card`);
    await page.keyboard.press('Control+f');
    check(await search().evaluate(node => node === document.activeElement), `Ctrl+F focuses the shared input on ${name}`);
  }
  await capture('all-pages-empty');
  await clear();
  const beforeIntegration = integrationGets;
  for (const [query, expected, excluded] of [
    ['更新速度', '更新速度', '自动采样'], ['自动采样', '自动采样', '更新速度'],
    ['冻结列表显示', '冻结列表显示', '表格密度'], ['表格密度', '表格密度', '自动采样'],
    ['默认进程视图', '默认进程视图', '表格密度'], ['折叠侧栏', '折叠侧栏', '自动采样'],
    ['始终置顶', '始终置顶', '界面缩放'], ['界面缩放', '界面缩放', '始终置顶'],
    ['界面偏好', '界面偏好', '自动采样'], ['Microsoft.Windows.CsWin32', '原生接口', '自动采样'],
    ['进程详情', '进程详情', '更新速度'], ['任务栏右键中的任务管理器', '任务管理器入口', '表格密度'],
  ]) {
    await search().fill(query);
    check(await page.getByText(expected, { exact: true }).first().isVisible(), `settings query ${query} reveals its real entry`);
    check(!await page.getByText(excluded, { exact: true }).first().isVisible(), `settings query ${query} hides unrelated rows`);
  }
  check(integrationGets === beforeIntegration, 'filtering settings never remounts or re-fetches task-manager integration');
  await capture('settings-filter');
  await clear(); await navigate('性能');
  for (const label of ['CPU', '内存', '网络', '磁盘', 'GPU']) {
    await search().fill(label);
    check(await resourceTiles(page).first().isVisible(), `performance category ${label} is searchable`);
    check(await page.locator('.performance-heading h2').isVisible(), `performance category ${label} preserves real chart or warm-up state`);
  }
  await clear();
  await resourceTile(page,'内存').click();
  const snapshot = await get('snapshot');
  const realResources = [
    ...snapshot.networkAdapters.slice(0, 1).flatMap(a => [[a.alias, '网络'], [a.id, '网络']]),
    ...snapshot.physicalDisks.filter(d=>!d.isTotal).slice(0, 1).map(d => [d.name, '磁盘']),
    ...snapshot.gpuAdapters.slice(0, 1).flatMap(g => [[g.description ?? g.adapterLuid, 'GPU'], [g.adapterLuid, 'GPU']]),
  ];
  for (const [query, category] of realResources) {
    await search().fill(query);
    await until(async()=>await page.locator('.resource-tile[aria-pressed="true"]').count()===1,'matching actual resource becomes selected');
    check(await page.locator('.resource-tile[aria-pressed="true"]').count() === 1, `actual resource identifier selects matching ${category} category`);
    check(await page.locator('.resource-tile[aria-pressed="true"]').isVisible()&&(await page.locator('.resource-tile[aria-pressed="true"]').getAttribute('title')).toLocaleLowerCase().includes(query.toLocaleLowerCase()), 'actual resource name/identity selects the corresponding real device');
    await clear();
    check(await resourceTile(page,'内存').getAttribute('aria-pressed') === 'true', 'clearing device search restores the saved memory resource preference');
  }
  if (!realResources.length) skipped.push('No real adapters/disks/GPUs were enumerated; device-specific searches not executed.');
  await capture('performance-restored');
  for (const [name, rows, queryOf, keyOf] of [
    ['服务', snapshot.services, value => value.name, value => value.name],
    ['启动应用', snapshot.startupItems, value => value.name, value => value.id],
    ['用户', snapshot.users, value => String(value.sessionId), value => String(value.sessionId)],
  ]) {
    await navigate(name);
    if (!rows.length) { skipped.push(`${name}: real Windows snapshot empty; positive match not executed.`); continue; }
    const query = queryOf(rows[0]); await search().fill(query);
    await page.locator('tr[data-row-key]').filter({ has: page.locator('td') }).first().waitFor();
    check(await page.locator('tr[data-row-key]').evaluateAll((elements, expected) => elements.some(element => element.dataset.rowKey === expected), keyOf(rows[0])), `${name} immediately matches an actual snapshot entry`);
  }
  await clear(); await navigate('进程');
  await page.getByRole('button', { name: '运行新任务', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('程序名称或完整路径', { exact: true }).fill(workload);
  await dialog.getByLabel('参数（可选）', { exact: true }).fill(`"${path.join(temporary, 'created')}" 600`);
  const response = page.waitForResponse(r => r.url() === `${origin}/api/processes/start` && r.request().method() === 'POST');
  await dialog.getByRole('button', { name: '运行', exact: true }).click();
  const target = await (await response).json(); owned.push(target);
  await until(async () => (await get('snapshot')).processes.some(p => p.identity === target.identity), 'new task sampled under its actual Windows identity');
  check(await search().inputValue() === String(target.pid), 'new task replaces shared query with its actual CreateProcess PID');
  // CIM creation timestamps have microsecond precision; compare at the source's
  // precision. Production actions below still receive the exact native FILETIME.
  check(processTree(target.pid).some(p => p.pid === target.pid && BigInt(p.fileTime) / 10n === BigInt(target.creationTimeFileTime) / 10n), 'independent Windows query verifies the dedicated test process identity');
  await locateReplayGuard(`${absent}-new-task`);
  await search().fill(String(target.pid));
  for (const name of ['进程', '详细信息', '应用历史记录']) {
    await navigate(name); await page.locator(`tr[data-row-key="${target.identity}"]`).waitFor();
    check(await search().inputValue() === String(target.pid), `${name} automatically matches the same real PID`);
    check(await page.locator('tbody tr[data-row-key]').count() === 1, `${name} uses exact numeric PID matching`);
  }
  await page.locator(`tr[data-row-key="${target.identity}"]`).click();
  await clickCommand(page, '定位仍运行的进程');
  await page.locator(`tr[data-row-key="${target.identity}"]`).waitFor();
  check(await search().inputValue() === String(target.pid), 'history navigation applies the verified real process PID globally');
  await locateReplayGuard(`${absent}-history`);
  check(await page.getByRole('button', { name: '结束任务', exact: true }).isDisabled(), 'filtered-out old selection cannot execute a destructive action');
  const conflict = await request(`processes/${target.pid}/terminate`, { creationTimeFileTime: String(BigInt(target.creationTimeFileTime) + 1n) });
  check(conflict.status === 409, 'mismatched creation identity is rejected without terminating the test process');
  const noToken = await fetch(`${origin}/api/processes/${target.pid}/terminate`, { method: 'POST', headers: { 'Content-Type': 'application/json', Origin: origin }, body: JSON.stringify({ creationTimeFileTime: target.creationTimeFileTime }) });
  check(noToken.status === 403, 'search/navigation retains native action token protection');
  await search().fill(String(target.pid)); await navigate('进程');
  await page.locator(`tr[data-row-key="${target.identity}"]`).dblclick();
  await until(async () => (await dialog.innerText()).includes('原生字段：'), 'actual process detail read');
  check((await dialog.innerText()).includes(String(target.pid)), 'details present the actual searched process');
  await page.keyboard.press('Escape');
  await capture('real-pid');
  const prefs = (await get('preferences')).preferences;
  for (const invalid of ['x'.repeat(257), 'invalid\0query']) {
    const rejected = await request('preferences', { ...prefs, searchQuery: invalid });
    check(rejected.status === 400, 'JSON.NET preferences reject an oversized or NUL shared query');
    check((await get('preferences')).preferences.searchQuery === prefs.searchQuery, 'invalid query leaves saved preferences unchanged');
  }
  await search().fill('shared-persisted-query'); await waitSaved('shared-persisted-query');
  const saved = JSON.parse(await readFile(preferenceFile, 'utf8'));
  check(saved.ui.searchQuery === 'shared-persisted-query' && !JSON.stringify(saved).includes(token), 'JSON.NET persists the shared field without action token');
  await post(`processes/${target.pid}/terminate`, { creationTimeFileTime: target.creationTimeFileTime });
  owned.length = 0;
  await stop(); await start();
  check(await search().inputValue() === 'shared-persisted-query', 'new shared query restores after an actual Host restart');
  await stop();
  const legacy = JSON.parse(await readFile(preferenceFile, 'utf8'));
  delete legacy.ui.searchQuery;
  legacy.ui.pages.processes = { ...legacy.ui.pages.processes, query: 'legacy-process-query' };
  await writeFile(preferenceFile, JSON.stringify(legacy));
  await start();
  check(await search().inputValue() === 'legacy-process-query', 'old version-one processes.query migrates after Host restart');
  await clear(); await waitSaved(''); await stop(); await start();
  check(await search().inputValue() === '', 'explicit shared empty survives restart and never revives the old process query');
  await navigate('性能'); check(await search().inputValue() === '', 'the restored explicit empty remains common on other pages');
  await navigate('设置'); await search().fill('界面偏好');
  await page.locator('.setting-buttons').getByRole('button', { name: '恢复默认界面偏好…', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: '恢复默认界面偏好', exact: true }).click();
  check(await search().inputValue() === '', 'restoring default interface preferences clears the shared query');
  for (const size of [{ width: 960, height: 640 }, { width: 1380, height: 860 }]) {
    await page.setViewportSize(size);
    for (const name of ['进程', '性能', '设置']) { await navigate(name); check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${name} shared search has no document overflow at ${size.width}`); }
  }
  await capture('settings-clear');
  check(forbiddenWrites.length === 0, 'searching does not change IFEO or control any Windows service');
  check(errors.length === 0, 'shared searching has no browser runtime errors');
  await stop();
  const result = { passed: true, mode, assertions: checks.length, checks, skipped, screenshots, errors, forbiddenWrites };
  await writeFile(path.join(output, `shared-search-${mode}-results.json`), JSON.stringify(result, null, 2));
  console.log(JSON.stringify(result, null, 2));
} catch (error) {
  if (page && !page.isClosed()) await capture('failure').catch(() => {});
  console.error(JSON.stringify({ passed: false, mode, assertions: checks.length, lastChecks: checks.slice(-8), error: error.message, screenshots }, null, 2));
  throw error;
} finally {
  for (const target of owned) if (token && host && !hostExited() && target.creationTimeFileTime) await request(`processes/${target.pid}/terminate`, { creationTimeFileTime: target.creationTimeFileTime }).catch(() => {});
  await stop().catch(async () => { if (host && !hostExited()) await stopOwnedHost(host); });
  await rm(temporary, { recursive: true, force: true });
}
