import { stopOwnedHost } from './etw-support.mjs';
import { clickCommand, revealControl, openProcessFilter, resourceTiles, resourceTile } from './layout-ui-support.mjs';
import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';
import { delay, until, processTree, ownedAlive, portOpen } from './e2e-support.mjs';

// Real production HTTP/JSON.NET/CsWin32 only. Run this test from an elevated shell;
// it deliberately preserves the production administrator entry requirement.
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const framework = 'net10.0-windows10.0.19041.0';
const desktop = process.env.TASKMANAGER_GRID_DESKTOP === '1';
const mode = desktop ? 'desktop' : 'browser';
const hostExe = desktop
  ? path.join(process.env.TASKMANAGER_GRID_RELEASE_DIRECTORY ?? path.join(root, 'output/grid-acceptance/desktop'), 'Takao.Host.exe')
  : path.join(root, 'src/Takao.Host/bin/Release', framework, 'Takao.Host.exe');
const workloadExe = path.join(root, 'tests/Takao.TestWorkload/bin/Release', framework, 'Takao.TestWorkload.exe');
const port = Number(process.env.TASKMANAGER_GRID_PORT ?? 5230), cdp = Number(process.env.TASKMANAGER_GRID_CDP ?? 9250);
const origin = `http://127.0.0.1:${port}`;
const temporary = await mkdtemp(path.join(tmpdir(), 'taskmanager-grid-'));
const preferenceDirectory = path.join(temporary, 'preferences');
const output = path.join(root, 'output/playwright');
await mkdir(output, { recursive: true }); await mkdir(preferenceDirectory);
if (await portOpen(port) || desktop && await portOpen(cdp)) throw new Error('Grid test ports already in use');
const processKeys = ['name', 'pid', 'state', 'parentPid', 'sessionId', 'threadCount', 'handleCount', 'priority', 'cpuPercent', 'workingSetBytes', 'privateBytes', 'io', 'networkBytesPerSecond', 'gpuPercent', 'gpuEngine', 'userName', 'publisher', 'path'];
const legacyPage = { query: '', sortKey: 'name', sortDirection: 'ascending', visibleColumns: processKeys, filters: null, scrollTop: 0, selectedIdentity: null, resourceType: null, resourceId: null };
const legacy = { version: 1, density: 'compact', sidebarCollapsed: false, processView: 'flat', pages: { processes: legacyPage, details: legacyPage } };
await writeFile(path.join(preferenceDirectory, 'preferences.json'), JSON.stringify({ version: 1, intervalMs: 1000, ui: legacy }));
let host, browser, page, token, workload, ownedProcesses = [], snapshot, target;
let phase = 'startup', preferenceWrites = 0;
const checks = [], screenshots = [], errors = [], cspErrors = [], coverage = [], limitations = [];
const startedAtUtc = new Date().toISOString();
const check = (condition, message) => { assert.ok(condition, message); checks.push(message); };
const get = async route => { const response = await fetch(`${origin}/api/${route}`); assert.ok(response.ok, `GET ${route}: ${response.status}`); return response.json(); };
const send = (route, body, raw = false) => fetch(`${origin}/api/${route}`, { method: 'POST', headers: { 'Content-Type': 'application/json', Origin: origin, 'X-TaskManager-Token': token }, body: raw ? body : JSON.stringify(body) });
const post = async (route, body) => { const response = await send(route, body); assert.ok(response.ok, `POST ${route}: ${response.status}`); return response.json(); };
const collator = new Intl.Collator('zh-CN', { numeric: true });
const serviceStates = { SERVICE_RUNNING: '正在运行', SERVICE_STOPPED: '已停止', SERVICE_PAUSED: '已暂停', SERVICE_START_PENDING: '正在启动', SERVICE_STOP_PENDING: '正在停止', SERVICE_CONTINUE_PENDING: '正在继续', SERVICE_PAUSE_PENDING: '正在暂停' };
const sessionStates = { WTSActive: '活动', WTSConnected: '已连接', WTSDisconnected: '已断开', WTSListen: '侦听', WTSIdle: '空闲' };
const tcpStates = { MIB_TCP_STATE_LISTEN: '正在侦听', MIB_TCP_STATE_ESTAB: '已建立', MIB_TCP_STATE_CLOSED: '已关闭' };
const priorities = ['空闲', '低于正常', '正常', '高于正常', '高', '实时'];
const asBigInt = value => value == null ? null : BigInt(value);
const endpointKey = row => [row.protocol, row.addressFamily, row.localAddress, row.localPort, row.remoteAddress, row.remotePort].join('|');

function compare(a, b, direction) {
  if (a == null || b == null) return a == null ? b == null ? 0 : 1 : -1;
  if (Array.isArray(a)) { for (let i = 0; i < a.length; i++) { const result = compare(a[i], b[i], direction); if (result) return result; } return 0; }
  const order = typeof a === 'string' ? collator.compare(a, b) : a < b ? -1 : a > b ? 1 : 0;
  return direction === 'ascending' ? order : -order;
}
function rawProcess(row, key) {
  if (key === 'io') return row.ioReadBytesPerSecond == null || row.ioWriteBytesPerSecond == null ? null : row.ioReadBytesPerSecond + row.ioWriteBytesPerSecond;
  if (key === 'priority') return row.priority == null ? null : priorities.indexOf(row.priority);
  if (key === 'state') return row.state === 'restricted' ? '访问受限' : '运行中';
  return row[key];
}
async function start() {
  host = spawn(hostExe, [...(desktop ? [] : ['--headless']), `--port=${port}`], { cwd: path.dirname(hostExe), env: { ...process.env, TASKMANAGER_DATA_DIR: preferenceDirectory, ...(desktop ? { TASKMANAGER_CDP_PORT: String(cdp) } : {}) }, stdio: 'ignore', windowsHide: true });
  await until(async () => (await get('health')).ready, 'real administrator host ready', 45000);
  if (desktop) await until(async () => (await get('bootstrap')).desktop === true, 'actual Electron window registered', 45000);
  const bootstrap = await get('bootstrap'); token = bootstrap.token;
  check(bootstrap.desktop === desktop, `${mode}: actual host mode matches test`);
  if (desktop) { await until(() => portOpen(cdp), 'real Electron CDP ready', 45000); browser = await chromium.connectOverCDP(`http://127.0.0.1:${cdp}`); await until(() => browser.contexts()[0]?.pages().some(p => p.url().startsWith(origin)), 'real Electron page ready'); page = browser.contexts()[0].pages().find(p => p.url().startsWith(origin)); }
  else { browser = await chromium.launch({ headless: true }); const context = await browser.newContext({ viewport: { width: 1380, height: 860 }, reducedMotion: 'reduce' }); page = await context.newPage(); await page.goto(origin); }
  await page.emulateMedia({ reducedMotion: 'reduce' }); page.setDefaultTimeout(10000);
  page.on('pageerror', error => errors.push(error.message));
  page.on('console', message => { if (/content.security.policy|violates.*directive|refused to load/i.test(message.text())) cspErrors.push(message.text()); });
  page.on('request', request => { if (request.url() === `${origin}/api/preferences` && request.method() === 'POST') preferenceWrites++; });
  await page.getByRole('table', { name: '进程列表', exact: true }).waitFor();
}
async function closeHost() {
  if (!host) return;
  const tree = processTree(host.pid);
  if (desktop && page && !page.isClosed()) { await page.getByRole('button', { name: '关闭窗口', exact: true }).click(); await until(() => host.exitCode !== null, 'actual Electron close exits owned host', 15000); }
  else { if (browser) await browser.close(); await stopOwnedHost(host); await until(() => host.exitCode !== null || host.signalCode !== null, 'headless host exits'); }
  browser = null; page = null;
  await until(async () => !await portOpen(port) && (!desktop || !await portOpen(cdp)), 'owned application ports released');
  await until(() => tree.every(p => !ownedAlive(p)), 'owned application process identities exited', 15000);
  check(true, `${mode}: close releases exact application process identities and ports`);
  host = null;
}
async function nav(name, label) {
  await page.getByRole('button', { name, exact: true }).click();
  await page.getByRole('searchbox').fill('');
  const table = page.locator(`table[aria-label="${label}"]`); await table.waitFor(); return table;
}
const header = (table, key) => table.locator(`th[data-column-key="${key}"]`);
const separator = (table, key) => header(table, key).getByRole('separator');
async function geometry(table) {
  return table.evaluate(element => ({ columns: [...element.querySelectorAll('th')].map(th => ({ key: th.dataset.columnKey, width: th.getBoundingClientRect().width, left: th.getBoundingClientRect().left })), cells: [...(element.querySelector('tr[data-row-key]')?.querySelectorAll('td') ?? [])].map(td => ({ width: td.getBoundingClientRect().width, left: td.getBoundingClientRect().left })), width: element.getBoundingClientRect().width }));
}
function aligned(value, context) { check(value.cells.length === value.columns.length && value.cells.every((cell, index) => Math.abs(cell.width - value.columns[index].width) < 1.1 && Math.abs(cell.left - value.columns[index].left) < 1.1), `${context}: headers, colgroup and cells align in CSS pixels`); }
async function startDrag(table, key, delta) {
  const handle = separator(table, key); await handle.scrollIntoViewIfNeeded();
  await handle.evaluate(el => el.addEventListener('pointerdown', event => { window.__gridPointer = { id: event.pointerId, element: el }; }, { once: true }));
  const box = await handle.boundingBox(); await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2); await page.mouse.down(); await page.mouse.move(box.x + box.width / 2 + delta, box.y + box.height / 2 + 16, { steps: 4 });
  check(await page.locator('body').evaluate(el => el.classList.contains('resizing-columns')), `${key}: real pointer drag active`);
}
async function drag(table, key, delta, label) {
  const before = await geometry(table), sort = await table.locator('th[aria-sort]:not([aria-sort="none"])').getAttribute('data-column-key');
  const selected = await table.locator('tr[aria-selected="true"]').evaluateAll(rows => rows[0]?.dataset.rowKey ?? null);
  await startDrag(table, key, delta); await page.mouse.up(); const after = await geometry(table);
  const index = before.columns.findIndex(c => c.key === key);
  check(Math.abs(after.columns[index].width - before.columns[index].width - delta) < 1.1, `${label} ${key}: drag changes requested column by ${delta} CSS pixels`);
  check(before.columns.every((column, i) => i === index || Math.abs(column.width - after.columns[i].width) < 1.1), `${label} ${key}: all other column widths remain independent`);
  check(Math.abs(after.width - before.width - delta) < 1.1, `${label} ${key}: table total width follows the resized column`);
  check(await table.locator('th[aria-sort]:not([aria-sort="none"])').getAttribute('data-column-key') === sort && await table.locator('tr[aria-selected="true"]').evaluateAll(rows => rows[0]?.dataset.rowKey ?? null) === selected, `${label} ${key}: drag never sorts or selects a row`);
  check(!await page.locator('body').evaluate(el => el.classList.contains('resizing-columns')), `${label} ${key}: release clears pointer cursor state`); aligned(after, `${label} ${key}`);
}
async function renderedKeys(table) { return table.locator('tbody tr[data-row-key]').evaluateAll(rows => rows.map(row => row.dataset.rowKey)); }
async function reveal(table, identity) {
  const row = table.locator(`tr[data-row-key="${identity}"]`), scroll = table.locator('..');
  await scroll.evaluate(el => { el.scrollTop = 0; });
  await until(async () => { if (await row.count()) return true; await scroll.evaluate(el => { el.scrollTop += el.clientHeight * .8; }); await delay(20); return false; }, 'reveal owned identity in actual virtual tree');
  return row;
}
async function verifySort(table, key, direction, records, value, label) {
  const map = new Map(records.map(record => [record.key, record.row]));
  const scroll = table.locator('..');
  const assess = async where => {
    const keys = await renderedKeys(table);
    check(keys.length > 0 && keys.every(id => map.has(id)), `${label} ${key} ${direction} ${where}: rendered identities belong to actual source`);
    const values = keys.map(id => value(map.get(id), key));
    check(values.every((current, index) => index === 0 || compare(values[index - 1], current, direction) <= 0), `${label} ${key} ${direction} ${where}: actual raw values are ordered with unknown last`);
    return values;
  };
  const all = records.map(record => value(record.row, key));
  const first = await assess('top');
  if (all.some(v => v != null)) check(first[0] != null, `${label} ${key} ${direction}: known values precede unknown values`);
  if (all.some(v => v == null)) {
    await scroll.evaluate(el => { el.scrollTop = el.scrollHeight; }); await delay(25);
    const last = await assess('bottom'); check(last.at(-1) == null, `${label} ${key} ${direction}: unknown values occupy the actual end`);
    await scroll.evaluate(el => { el.scrollTop = 0; }); await delay(20);
  }
}
async function exerciseTable(label, table, records, value) {
  phase = `grid ${label}`;
  console.log(`Checking real grid: ${label} (${records.length} rows)`);
  const keys = await table.locator('th').evaluateAll(nodes => nodes.map(th => th.dataset.columnKey));
  check(await table.getByRole('separator').count() === keys.length && await table.locator('button.column-width-button').count() === keys.length, `${label}: every visible column exposes keyboard and pointer width controls`);
  for (const key of keys) {
    const th = header(table, key), sortButton = th.locator('.sort-button');
    const allUnknown = records.every(record => value(record.row, key) == null); let previousOrder;
    check(await sortButton.count() === 1, `${label} ${key}: real sort button exists`);
    for (let attempt = 0; attempt < 2; attempt++) {
      const previous = await th.getAttribute('aria-sort'); const direction = previous === 'ascending' ? 'descending' : 'ascending';
      await sortButton.click(); check(await th.getAttribute('aria-sort') === direction, `${label} ${key}: click exposes ${direction} aria-sort`);
      await verifySort(table, key, direction, records, value, label);
      if (allUnknown) { const order = await renderedKeys(table); if (previousOrder) check(JSON.stringify(order) === JSON.stringify(previousOrder), `${label} ${key}: both directions keep all-unknown row order stable`); previousOrder = order; }
    }
  }
  for (const key of ['Enter', 'Space']) { const th = header(table, keys[0]), before = await th.getAttribute('aria-sort'); await th.locator('.sort-button').press(key); check(await th.getAttribute('aria-sort') === (before === 'ascending' ? 'descending' : 'ascending'), `${label}: ${key} sorts through native header button`); }
  for (const key of [...new Set([keys[0], keys[Math.floor(keys.length / 2)], keys.at(-1)])]) { await drag(table, key, 40, label); await drag(table, key, -24, label); }
  check(await table.locator('tr[data-row-key]').count() <= 80, `${label}: virtual row DOM remains bounded`);
  coverage.push({ grid: label, columns: keys, realRows: records.length });
}
async function screenshot(name) { const file = `grid-${mode}-${name}.png`; await page.screenshot({ path: path.join(output, file) }); screenshots.push(`output/playwright/${file}`); }

try {
  await start(); phase = 'legacy and validation';
  const old = await get('preferences'); check(old.warning == null && !old.preferences.pages.processes.columnWidths, 'G01: legacy version 1 config without columnWidths loads normally');
  const valid = structuredClone(old.preferences); valid.pages.processes.columnWidths = { name: 310, pid: 96 };
  await post('preferences', valid); check((await get('preferences')).preferences.pages.processes.columnWidths.name === 310, 'G01: legal widths round trip through JSON.NET and disk');
  const invalid = [ ['unknown key', { typo: 120 }], ['cross-page key', { command: 120 }], ['below minimum', { name: 63 }], ['above maximum', { name: 1601 }], ['nonfinite NaN', { name: 'NaN' }], ['nonfinite infinity', { name: 'Infinity' }], ['prototype key', JSON.parse('{"__proto__":120}')], ['over 40 entries', Object.fromEntries(Array.from({ length: 41 }, (_, i) => [`invalid${i}`, 120]))] ];
  for (const [label, widths] of invalid) { const rejected = structuredClone(valid); rejected.pages.processes.columnWidths = widths; const response = await send('preferences', rejected); check(response.status === 400, `G01: JSON.NET rejects ${label}`); check(JSON.stringify((await get('preferences')).preferences.pages) === JSON.stringify(valid.pages), `G01: rejected ${label} leaves saved pages unchanged`); }
  await post('preferences', old.preferences); await page.reload(); await page.getByRole('table', { name: '进程列表', exact: true }).waitFor();

  phase = 'owned workload';
  workload = spawn(workloadExe, [path.join(temporary, 'workload'), '180', 'tree'], { stdio: 'ignore', windowsHide: true });
  await until(async () => (await get('snapshot')).processes.some(p => p.pid === workload.pid), 'owned real workload sampled');
  target = (await get('snapshot')).processes.find(p => p.pid === workload.pid);
  let children;
  await until(async () => { children = (await readFile(path.join(temporary, 'workload/children.txt'), 'utf8')).trim().split(/\r?\n/).map(line => line.split(':')); return children.length === 2; }, 'real test child identity file ready');
  ownedProcesses = processTree(workload.pid);
  // CIM CreationDate rounds FILETIME to microseconds. Match the exact fixture
  // FILETIME against native telemetry, and independently verify parent/image via CIM.
  await until(async () => { const current = await get('snapshot'); return children.every(([pid, fileTime]) => current.processes.some(p => p.pid === Number(pid) && p.creationTimeFileTime === fileTime)); }, 'exact owned child native identities sampled');
  check(children.every(([pid]) => ownedProcesses.some(p => p.pid === Number(pid) && p.imagePath?.toLowerCase() === workloadExe.toLowerCase())), 'owned child images and ancestry match independent Windows process query');
  await page.getByRole('searchbox').fill(String(workload.pid));
  let table = page.getByRole('table', { name: '进程列表', exact: true }); await table.locator(`tr[data-row-key="${target.identity}"]`).click();
  await page.getByRole('searchbox').fill('');
  await delay(800); phase = 'live pointer';
  const widthBefore = Number(await separator(table, 'name').getAttribute('aria-valuenow'));
  const writeStart = preferenceWrites, sampleStart = (await get('snapshot')).sampleId;
  await startDrag(table, 'name', 48);
  const dragBox = await separator(table, 'name').boundingBox(); await page.mouse.move(dragBox.x + dragBox.width / 2, dragBox.y + dragBox.height + 100);
  await until(async () => (await get('snapshot')).sampleId >= sampleStart + 2, 'two real native samples during held pointer', 12000);
  check(await separator(table, 'name').getAttribute('aria-valuenow') === String(widthBefore + 48) && await page.locator('.column-resizer.is-resizing').count() === 1, 'G03: real sampling preserves active gesture and width');
  check(preferenceWrites === writeStart, 'G05: pointermove and real refresh do not save preferences');
  await page.mouse.up(); await until(async () => (await get('preferences')).preferences.pages.processes.columnWidths?.name === widthBefore + 48, 'completed pointer persists');
  check(preferenceWrites > writeStart, 'G05: completed pointer saves through existing debounce');
  await separator(table, 'name').press('Home');
  for (const kind of ['escape', 'pointercancel', 'lostcapture', 'blur']) {
    const before = Number(await separator(table, 'name').getAttribute('aria-valuenow'));
    await startDrag(table, 'name', 35);
    if (kind === 'escape') await page.keyboard.press('Escape');
    else await page.evaluate(kind => { const { element, id } = window.__gridPointer; if (kind === 'pointercancel') element.dispatchEvent(new PointerEvent('pointercancel', { pointerId: id, bubbles: true })); else if (kind === 'lostcapture') element.releasePointerCapture(id); else window.dispatchEvent(new Event('blur')); }, kind);
    await page.mouse.up(); await delay(25);
    check(Number(await separator(table, 'name').getAttribute('aria-valuenow')) === before && !await page.locator('body').evaluate(el => el.classList.contains('resizing-columns')), `G03: ${kind} restores width and removes active cursor`);
    check(await page.evaluate(() => !window.__gridPointer.element.hasPointerCapture(window.__gridPointer.id)), `G03: ${kind} releases real pointer capture`);
  }
  await startDrag(table, 'name', 27); await page.getByRole('button', { name: '服务', exact: true }).focus(); await page.keyboard.press('Enter'); await page.mouse.up();
  check(!await page.locator('body').evaluate(el => el.classList.contains('resizing-columns')), 'G03: page unmount removes active gesture');
  table = await nav('进程', '进程列表');
  const handle = separator(table, 'pid'), originalPidWidth = Number(await handle.getAttribute('aria-valuenow'));
  await handle.focus(); await handle.press('ArrowRight'); check(Number(await handle.getAttribute('aria-valuenow')) === originalPidWidth + 16, 'G06: keyboard right increases width by semantic step');
  await handle.press('Shift+ArrowLeft'); check(Number(await handle.getAttribute('aria-valuenow')) === Math.max(64, originalPidWidth - 48), 'G06: Shift left uses large semantic step and bounds');
  await handle.press('Home'); check(Number(await handle.getAttribute('aria-valuenow')) === originalPidWidth, 'G06: keyboard Home restores default');
  await header(table, 'pid').locator('.column-width-button').click(); let editor = page.getByRole('dialog', { name: '设置PID列宽', exact: true });
  await editor.getByRole('spinbutton').fill('63'); check(await editor.getByRole('button', { name: '应用宽度', exact: true }).isDisabled(), 'G06: click alternative prevents out of range value');
  await editor.getByRole('spinbutton').fill('111'); await editor.getByRole('button', { name: '应用宽度', exact: true }).click(); check(await handle.getAttribute('aria-valuenow') === '111', 'G06: explicit input applies requested width');
  await handle.press('Enter'); editor = page.getByRole('dialog', { name: '设置PID列宽', exact: true }); await editor.getByRole('spinbutton').fill('1601'); check(await editor.getByRole('button', { name: '应用宽度', exact: true }).isDisabled(), 'G06: click alternative rejects width above maximum'); await editor.getByRole('spinbutton').fill('1600'); await editor.getByRole('button', { name: '应用宽度', exact: true }).click(); await handle.press('ArrowRight'); check(await handle.getAttribute('aria-valuenow') === '1600', 'G03: keyboard growth stops at finite maximum');
  await handle.dblclick(); check(Number(await handle.getAttribute('aria-valuenow')) === originalPidWidth, 'G06: double click restores default without sorting');
  await handle.press('ArrowLeft'); await handle.press('ArrowLeft'); check(await handle.getAttribute('aria-valuenow') === '64', 'G03: repeated keyboard shrink stops at finite minimum'); await handle.press('Home');
  await post('settings', { intervalMs: 1000, paused: true }); await page.reload(); await page.getByRole('table', { name: '进程列表', exact: true }).waitFor(); snapshot = await get('snapshot');
  check(snapshot.paused, 'G04: raw comparison snapshot is real and deliberately stable');
  const processes = snapshot.processes.map(row => ({ key: row.identity, row }));
  await exerciseTable('进程', page.getByRole('table', { name: '进程列表', exact: true }), processes, rawProcess);
  table = await nav('详细信息', '进程详细信息'); await exerciseTable('详细信息', table, processes, rawProcess);
  table = await nav('应用历史记录', '本次会话应用历史记录');
  const liveIds = new Set(snapshot.processes.map(p => p.identity));
  await exerciseTable('应用历史记录', table, snapshot.history.map(row => ({ key: row.identity, row })), (row, key) => key === 'cpu' ? asBigInt(row.observedCpuTime100ns) : key === 'observedIoReadBytes' || key === 'observedIoWriteBytes' ? asBigInt(row[key]) : key === 'last' ? Date.parse(row.lastObservedAtUtc) : key === 'state' ? liveIds.has(row.identity) ? '运行中' : '已退出' : row[key]);
  table = await nav('启动应用', '真实启动应用');
  if (snapshot.startupItems.length) await exerciseTable('启动应用', table, snapshot.startupItems.map(row => ({ key: row.id, row })), (row, key) => key === 'location' ? row.locationPath : row[key]); else limitations.push('Windows returned no startup rows; header coverage only.');
  table = await nav('用户', 'Windows 登录会话');
  await exerciseTable('用户', table, snapshot.users.map(row => ({ key: String(row.sessionId), row })), (row, key) => key === 'name' ? row.userName || null : key === 'session' ? row.sessionId : key === 'station' ? row.stationName : key === 'state' ? sessionStates[row.state] ?? row.state : key === 'count' ? row.processCount : key === 'cpu' ? row.cpuPercent : row.workingSetBytes);
  table = await nav('服务', 'Windows 服务');
  await exerciseTable('服务', table, snapshot.services.map(row => ({ key: row.name, row })), (row, key) => key === 'display' ? row.displayName : key === 'state' ? serviceStates[row.state] ?? row.state : key === 'pid' ? row.pid || null : row.name);
  await screenshot('services-1380');

  phase = 'tree'; table = await nav('进程', '进程列表'); await clickCommand(page, '进程树');
  await page.getByRole('searchbox').fill('Takao.TestWorkload'); table = page.getByRole('treegrid', { name: '进程列表', exact: true });
  const childIds = children.map(child => child.join(':'));
  const rootRow = table.locator(`tr[data-row-key="${target.identity}"]`); await rootRow.click();
  for (const direction of ['ascending', 'descending']) {
    const sort = header(table, 'pid'); if (await sort.getAttribute('aria-sort') !== direction) await sort.locator('.sort-button').click();
    const visible = await table.locator('tbody tr[data-row-key]').evaluateAll(rows => rows.map(row => ({ key: row.dataset.rowKey, level: Number(row.getAttribute('aria-level')) })));
    const rootIndex = visible.findIndex(row => row.key === target.identity); const children = visible.filter(row => childIds.includes(row.key));
    check(children.length === 2 && children.every(row => row.level === visible[rootIndex].level + 1 && visible.findIndex(r => r.key === row.key) > rootIndex), `G04: ${direction} keeps owned children below actual parent`);
    check(direction === 'ascending' ? Number(children[0].key.split(':')[0]) < Number(children[1].key.split(':')[0]) : Number(children[0].key.split(':')[0]) > Number(children[1].key.split(':')[0]), `G04: ${direction} sorts real siblings only`);
    check(await rootRow.getAttribute('aria-selected') === 'true', `G04: ${direction} retains selected process identity`);
  }
  await page.getByRole('searchbox').fill(''); await reveal(table, target.identity);
  await rootRow.locator('.tree-toggle').click(); await header(table, 'name').locator('.sort-button').click(); await reveal(table, target.identity);
  check(await rootRow.getAttribute('aria-expanded') === 'false', 'G04: sort retains actual collapsed parent state');
  await rootRow.locator('.tree-toggle').click(); await rootRow.dblclick();
  const detail = page.getByRole('dialog', { name: '进程信息', exact: true }); check((await detail.locator('.detail-identity').innerText()).includes(`PID ${workload.pid}`), 'G04: sorted selected row opens the same fixed identity');
  await page.getByRole('tab', { name: 'TCP / UDP', exact: true }).click(); table = page.getByRole('table', { name: '进程TCP UDP连接', exact: true });
  await until(async () => await table.locator('tr[data-row-key]').count() >= 4, 'real IPv4/IPv6 TCP/UDP endpoints loaded');
  const connections = await get(`processes/${workload.pid}/connections?creationTimeFileTime=${target.creationTimeFileTime}`);
  const endpoints = Object.fromEntries((await readFile(path.join(temporary, 'workload/endpoints.txt'), 'utf8')).trim().split(/\r?\n/).map(line => line.split('=')));
  check(['TCP4', 'UDP4'].every(key => connections.connections.some(row => row.protocol === key.slice(0, -1) && row.localPort === Number(endpoints[key]))), 'G07: connection table uses exact test-owned native socket ports');
  await exerciseTable('TCP/UDP', table, connections.connections.map(row => ({ key: endpointKey(row), row })), (row, key) => key === 'protocol' ? [row.protocol, row.addressFamily] : key === 'local' ? [row.localAddress, row.localPort] : key === 'remote' ? row.remoteAddress == null ? null : [row.remoteAddress, row.remotePort] : row.state == null ? null : tcpStates[row.state] ?? row.state);
  await header(table, 'local').locator('.column-width-button').click(); editor = page.getByRole('dialog', { name: '设置本地端点列宽', exact: true });
  for (let i = 0; i < 9; i++) { await page.keyboard.press('Tab'); check(await editor.evaluate(el => el.contains(document.activeElement)), 'G06: nested column width modal traps Tab focus'); }
  await page.keyboard.press('Escape'); check(await detail.count() === 1 && await page.getByRole('dialog').count() === 1 && await header(table, 'local').locator('.column-width-button').evaluate(el => el === document.activeElement), 'G06: nested Escape closes only width editor and restores trigger focus');
  const connectionWidth = await separator(table, 'local').getAttribute('aria-valuenow'); const connectionSort = await table.locator('th[aria-sort]:not([aria-sort="none"])').getAttribute('data-column-key');
  await page.getByRole('tab', { name: '概览', exact: true }).click(); await page.getByRole('tab', { name: 'TCP / UDP', exact: true }).click();
  await detail.getByRole('button', { name: '刷新详情', exact: true }).click(); await delay(250);
  check(await separator(table, 'local').getAttribute('aria-valuenow') === connectionWidth && await table.locator('th[aria-sort]:not([aria-sort="none"])').getAttribute('data-column-key') === connectionSort, 'G05: connection tab switch and native refresh preserve width and sorting');
  await screenshot('connections'); await page.keyboard.press('Escape');

  phase = 'persistence'; table = await nav('进程', '进程列表'); await clickCommand(page, '平铺');
  const processWidth = await separator(table, 'name').getAttribute('aria-valuenow');
  await separator(table, 'gpuEngine').press('ArrowRight'); const engineWidth = await separator(table, 'gpuEngine').getAttribute('aria-valuenow');
  const columnsMenu = page.locator('details.control-popover').filter({ has: page.locator(':scope > summary', { hasText: '显示列' }) }); await revealControl(columnsMenu.locator(':scope > summary'));await columnsMenu.locator(':scope > summary').click();
  await columnsMenu.getByLabel('GPU 引擎', { exact: true }).uncheck(); await columnsMenu.getByLabel('GPU 引擎', { exact: true }).check(); await columnsMenu.locator('summary').click();
  check(await separator(table, 'gpuEngine').getAttribute('aria-valuenow') === engineWidth, 'G05: hidden and reshown column preserves its custom width');
  table = await nav('详细信息', '进程详细信息'); await separator(table, 'name').press('ArrowRight'); const detailWidth = await separator(table, 'name').getAttribute('aria-valuenow');
  table = await nav('进程', '进程列表'); check(await separator(table, 'name').getAttribute('aria-valuenow') === processWidth && processWidth !== detailWidth, 'G05: process and details widths are isolated across navigation');
  await page.getByRole('searchbox').fill('');
  for (const size of [{ width: 960, height: 640 }, { width: 1380, height: 860 }]) {
    await page.setViewportSize(size);
    for (const [name, label] of [['进程', '进程列表'], ['详细信息', '进程详细信息'], ['应用历史记录', '本次会话应用历史记录'], ['启动应用', '真实启动应用'], ['用户', 'Windows 登录会话'], ['服务', 'Windows 服务']]) { table = await nav(name, label); check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth && document.documentElement.scrollHeight <= innerHeight), `G06: ${name} has no document overflow at ${size.width}x${size.height}`); aligned(await geometry(table), `G06 ${name} ${size.width}`); }
    await screenshot(`layout-${size.width}`);
  }
  if (desktop) { const session = await page.context().newCDPSession(page); await session.send('Emulation.clearDeviceMetricsOverride'); await session.detach(); const actualWidth = await page.evaluate(() => innerWidth); await post('window/zoom-in', {}); await until(async () => await page.evaluate(() => innerWidth) < actualWidth, 'actual Electron zoom changes CSS viewport'); check((await get('desktop/window-state')).zoomFactor === 1.25, 'G06: actual Electron zoom factor reads back 125 percent'); }
  else await page.setViewportSize({ width: 1104, height: 688 });
  for (const [name, label] of [['进程', '进程列表'], ['详细信息', '进程详细信息'], ['应用历史记录', '本次会话应用历史记录'], ['启动应用', '真实启动应用'], ['用户', 'Windows 登录会话'], ['服务', 'Windows 服务']]) { table = await nav(name, label); check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `G06: ${name} 125 percent equivalent viewport remains bounded`); aligned(await geometry(table), `G06 ${name} 125 percent`); }
  for (const name of ['性能', '设置']) { await page.getByRole('button', { name, exact: true }).click(); check(await page.locator('table.data-table').count() === 0 && await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `G02: ${name} retains its non-grid layout`); }
  await screenshot('zoom-125');
  if (desktop) await post('window/zoom-reset', {});
  await page.setViewportSize({ width: 1380, height: 860 });
  table = await nav('进程', '进程列表'); await delay(900);
  const persistedWidths = (await get('preferences')).preferences.pages.processes.columnWidths;
  await closeHost(); await start(); table = page.getByRole('table', { name: '进程列表', exact: true });
  check(await separator(table, 'name').getAttribute('aria-valuenow') === processWidth && JSON.stringify((await get('preferences')).preferences.pages.processes.columnWidths) === JSON.stringify(persistedWidths), 'G05: actual host restart reloads persisted page widths');
  await clickCommand(page, '重置视图');
  await until(async () => await separator(table, 'name').getAttribute('aria-valuenow') === '272' && (await get('preferences')).preferences.pages.processes.columnWidths == null, 'page reset renders and persists default widths');
  check(true, 'G05: page reset restores and persists default column widths');
  const memoryTitle = header(table, 'workingSetBytes').locator('.column-title'); await memoryTitle.scrollIntoViewIfNeeded();
  check(await memoryTitle.evaluate(el => el.scrollWidth <= el.clientWidth && getComputedStyle(el).textOverflow === 'ellipsis'), 'G06: default memory column title fits completely with tail ellipsis available'); await screenshot('default-readable-titles');
  await page.getByRole('button', { name: '设置', exact: true }).click(); await page.getByRole('searchbox').fill('');
  await page.locator('.setting-buttons').getByRole('button', { name: '恢复默认界面偏好…', exact: true }).click(); const reset = page.getByRole('dialog', { name: '恢复默认界面偏好？', exact: true }); await reset.getByRole('button', { name: '恢复默认界面偏好', exact: true }).click();
  await until(async () => Object.keys((await get('preferences')).preferences.pages ?? {}).length === 0, 'global reset clears every page width'); check(true, 'G05: global reset clears all persisted column widths');
  check(errors.length === 0, 'G07: no browser runtime errors'); check(cspErrors.length === 0, 'G07: no CSP violations');
  await closeHost();
} catch (error) {
  if (page && !page.isClosed()) await screenshot('failure').catch(() => {});
  await writeFile(path.join(output, `grid-${mode}-results.json`), JSON.stringify({ passed: false, phase, startedAtUtc, completedAtUtc: new Date().toISOString(), assertions: checks.length, checks, coverage, screenshots, limitations, errors, cspErrors, failure: { name: error.name, message: error.message } }, null, 2));
  throw error;
} finally {
  if (host) {
    const tree = processTree(host.pid);
    if (desktop) { await post('window/close', {}).catch(() => {}); await until(() => host.exitCode !== null, 'failure cleanup close', 4000).catch(() => {}); }
    if (browser) await browser.close().catch(() => {});
    if (host.exitCode === null) await stopOwnedHost(host);
    for (const entry of tree.reverse()) if (ownedAlive(entry)) spawnSync('powershell', ['-NoProfile', '-Command', `Stop-Process -Id ${entry.pid} -Force`], { windowsHide: true, stdio: 'ignore' });
  }
  // Cleanup is restricted to PID + creation time identities of this test's workload.
  for (const entry of ownedProcesses.reverse()) if (ownedAlive(entry)) spawnSync('powershell', ['-NoProfile', '-Command', `Stop-Process -Id ${entry.pid} -Force`], { windowsHide: true, stdio: 'ignore' });
  if (workload && workload.exitCode === null) workload.kill();
  await rm(temporary, { recursive: true, force: true, maxRetries: 4, retryDelay: 150 });
}
await writeFile(path.join(output, `grid-${mode}-results.json`), JSON.stringify({ passed: true, mode, startedAtUtc, completedAtUtc: new Date().toISOString(), assertions: checks.length, checks, coverage, screenshots, limitations, errors, cspErrors, cleanup: { applicationPortClosed: !await portOpen(port), cdpPortClosed: !desktop || !await portOpen(cdp), ownedWorkloadIdentitiesExited: ownedProcesses.every(p => !ownedAlive(p)) } }, null, 2));
console.log(JSON.stringify({ passed: true, mode, assertions: checks.length, grids: coverage.length, results: `output/playwright/grid-${mode}-results.json` }));
