import { stopOwnedHost } from './etw-support.mjs';
import { clickCommand, revealControl, openProcessFilter, resourceTiles, resourceTile } from './layout-ui-support.mjs';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';
import { processTree, ownedAlive, portOpen } from './e2e-support.mjs';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const framework = 'net10.0-windows10.0.19041.0';
const desktopMode = process.env.TASKMANAGER_PROCESS_UI_DESKTOP === '1'; const cdp = 9227;
const hostExe = desktopMode ? path.join(root, 'output/Takao-win-x64/Takao.Host.exe') : path.join(root, 'src/Takao.Host/bin/Release', framework, 'Takao.Host.exe');
const workloadExe = path.join(root, 'tests/Takao.TestWorkload/bin/Release', framework, 'Takao.TestWorkload.exe');
const port = Number(process.env.TASKMANAGER_PROCESS_UI_PORT ?? 5193); const origin = `http://127.0.0.1:${port}`;
const temporary = await mkdtemp(path.join(tmpdir(), 'taskmanager-process-ui-')); const output = path.join(root, 'output/playwright'); await mkdir(output, { recursive: true });
if (await portOpen(port) || desktopMode && await portOpen(cdp)) throw new Error('Process UI test ports already in use');
const host = spawn(hostExe, [...(desktopMode ? [] : ['--headless']), `--port=${port}`], { cwd: path.dirname(hostExe), env: { ...process.env, TASKMANAGER_DATA_DIR: path.join(temporary, 'preferences'), ...(desktopMode ? {TASKMANAGER_CDP_PORT:String(cdp)} : {}) }, stdio: 'ignore', windowsHide: true });
const workload = spawn(workloadExe, [path.join(temporary, 'tree'), '180', 'tree'], { cwd: root, stdio: 'ignore', windowsHide: true });
let browser; let token; let created; let ownRoot; let assertions = 0; const checks = []; const errors = []; const cspErrors = [];
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
async function until(predicate, message, timeout = 20000) { const start = Date.now(); while (Date.now() - start < timeout) { try { if (await predicate()) return; } catch {} await delay(120); } throw new Error(message); }
function check(condition, message) { assert.ok(condition, message); assertions++; checks.push(message); }
function csvRecords(text) {
  const rows = []; let row = [], cell = '', quoted = false;
  for (let i = 0; i < text.length; i++) {
    const ch = text[i];
    if (ch === '"') { if (quoted && text[i + 1] === '"') { cell += '"'; i++; } else quoted = !quoted; }
    else if (ch === ',' && !quoted) { row.push(cell); cell = ''; }
    else if (ch === '\n' && !quoted) { row.push(cell.endsWith('\r') ? cell.slice(0, -1) : cell); rows.push(row); row = []; cell = ''; }
    else cell += ch;
  }
  return rows;
}
const get = async route => (await fetch(`${origin}/api/${route}`)).json();
const post = async (route, body) => (await fetch(`${origin}/api/${route}`, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-TaskManager-Token': token }, body: JSON.stringify(body) })).json();
try {
  await until(async () => (await get('health')).ready, 'real host ready'); token = (await get('bootstrap')).token;
  await until(async () => (await get('snapshot')).processes.some(p => p.pid === workload.pid), 'owned workload sampled');
  ownRoot = (await get('snapshot')).processes.find(p => p.pid === workload.pid);
  const children = (await readFile(path.join(temporary, 'tree/children.txt'), 'utf8')).trim().split(/\r?\n/).map(line => line.split(':'));
  if (desktopMode) await until(() => portOpen(cdp), 'actual Electron CDP ready');
  browser = desktopMode ? await chromium.connectOverCDP(`http://127.0.0.1:${cdp}`) : await chromium.launch({ headless: true }); if(desktopMode) await until(()=>browser.contexts()[0]?.pages().length>0,'real Electron BrowserWindow page attached'); const context = desktopMode ? browser.contexts()[0] : await browser.newContext({ viewport: { width: 1380, height: 860 }, reducedMotion: 'reduce', permissions: ['clipboard-read', 'clipboard-write'] }); const page = desktopMode ? context.pages()[0] : await context.newPage();
  page.on('pageerror', error => errors.push(error.message));
  page.on('console', message => { if (/content.security.policy|violates.*directive|refused to load.*blob:/i.test(message.text())) cspErrors.push(message.text()); });
  const snapshotResponses = []; page.on('response', response => { if (response.url() === `${origin}/api/snapshot`) snapshotResponses.push(response.status()); });
  await page.goto(origin); await page.getByRole('table', { name: '进程列表', exact: true }).waitFor();
  check(await page.locator('tbody tr[data-row-key]').count() < (await get('snapshot')).processes.length, 'real table remains virtualized');
  check(await page.locator('.data-table thead').evaluate(element => Math.abs(element.getBoundingClientRect().height - Number.parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--height-table-header'))) < 1), 'actual table header matches virtualizer master token');
  const search = page.getByRole('searchbox'); await search.fill(String(workload.pid));
  const row = page.locator(`tr[data-row-key="${ownRoot.identity}"]`); await row.waitFor();
  await until(async () => await row.locator('img').evaluateAll(images => images.some(image => image.complete && image.naturalWidth > 0)), 'real Shell PNG blob decodes in browser');
  check(true, 'visible process real Shell icon decodes with positive native dimensions');
  check(await page.locator('tbody tr[data-row-key]').count() === 1, 'numeric PID query is exact');
  await row.click(); await clickCommand(page, '冻结显示');
  const before = await get('snapshot'); await delay(2300); check((await get('snapshot')).sampleId > before.sampleId, 'display freeze keeps native sampling and history advancing');
  check(await page.getByText('显示已冻结 ·', { exact: false }).first().isVisible(), 'freeze has visible timestamp and live sampling explanation');
  await clickCommand(page, '继续显示');
  await page.getByRole('button', { name: '服务', exact: true }).click(); await search.fill('Windows'); await page.getByRole('button', { name: '进程', exact: true }).click();
  check(await search.inputValue() === 'Windows', 'all pages immediately share the latest search query');
  await clickCommand(page, '进程树'); await search.fill(children[0][0]);
  await until(async () => await page.locator('.context-tag').count() >= 1, 'tree query includes real ancestors');
  check(await page.locator('tbody tr[data-row-key]').count() >= 2, 'tree reveals actual ancestor context for child PID');
  const childIdentity = children[0].join(':'); const child = page.locator(`tr[data-row-key="${childIdentity}"]`); await child.dblclick();
  const dialog = page.getByRole('dialog'); await page.getByRole('tab', { name: 'TCP / UDP', exact: true }).click();
  await until(async () => (await dialog.innerText()).includes('不适用（UDP）'), 'real UDP endpoint semantics shown');
  check((await dialog.innerText()).includes('IPv6'), 'real IPv6 endpoint family shown');
  await page.getByRole('tab', { name: '概览', exact: true }).click();
  await until(async () => (await dialog.innerText()).includes('原生字段：'), 'fresh process details read');
  check((await dialog.innerText()).includes('专用提交'), 'private bytes labeled committed private memory');
  await page.keyboard.press('Escape'); await child.click(); await child.press('Shift+F10');
  const menu = page.getByRole('menu'); await menu.waitFor(); check((await menu.innerText()).includes('Windows 属性'), 'keyboard context menu exposes real file actions');
  await page.keyboard.press('End'); check(await page.getByRole('menuitem', { name: '结束进程树…', exact: true }).evaluate(el => el === document.activeElement), 'menu End moves to final action');
  await page.keyboard.press('Escape'); check(await page.getByRole('menu').count() === 0, 'menu Escape closes only menu');
  await page.locator('.command-menu > summary').click(); await clickCommand(page, '设置优先级', {menu:true});
  await page.getByLabel('目标优先级').selectOption('belowNormal'); await page.getByRole('button', { name: '确认变更', exact: true }).click();
  await until(async () => (await dialog.innerText()).includes('Windows 实际读回优先级：低于正常'), 'priority write read back');
  const detail = await get(`processes/${children[0][0]}/details?creationTimeFileTime=${children[0][1]}`);
  check(detail.process.priority === '低于正常', 'fresh native details independently confirm priority'); await dialog.getByRole('button', { name: '关闭', exact: true }).click();
  await child.click(); await page.locator('.command-menu > summary').click(); await clickCommand(page, '效率模式', {menu:true});
  await page.getByRole('button', { name: '启用效率模式', exact: true }).click(); await until(async () => (await dialog.innerText()).includes('效率模式已启用'), 'real efficiency enabled');
  check((await dialog.innerText()).includes('关闭本应用不会自动恢复'), 'efficiency impact explicitly explains session restore boundary'); await dialog.getByRole('button', { name: '关闭', exact: true }).click();
  await child.click(); await page.locator('.command-menu > summary').click(); await clickCommand(page, '效率模式', {menu:true}); await page.getByRole('button', { name: '恢复原状态', exact: true }).click();
  await until(async () => (await dialog.innerText()).includes('原优先级与电源节流状态已恢复'), 'real efficiency restored'); check(true, 'efficiency enable/restore UI uses actual Windows state'); await dialog.getByRole('button', { name: '关闭', exact: true }).click();
  await search.fill(String(workload.pid)); await row.click(); await page.locator('.command-menu > summary').click(); await clickCommand(page, '结束进程树…', {menu:true});
  await until(async () => await page.locator('.tree-preview [role="listitem"]').count() >= 3, 'actual tree preview has children');
  check((await dialog.innerText()).includes('完整成员'), 'tree includes explicit exact membership explanation'); await dialog.getByRole('button', { name: '取消', exact: true }).click();
  await search.fill(''); await clickCommand(page, '冻结显示');
  const expectedCsvRows = Number(await page.getByRole('treegrid').getAttribute('aria-rowcount')) - 1;
  await until(async()=>{if(await row.count())return true;await page.locator('[data-testid="table-scroll"]').evaluate(element=>{element.scrollTop+=element.clientHeight*.8;});await delay(30);return false;},'scroll actual virtual tree to owned root for collapse');
  await row.locator('.tree-toggle').click();
  check(Number(await page.getByRole('treegrid').getAttribute('aria-rowcount')) - 1 < expectedCsvRows, 'actual owned tree collapses while the full filtered set remains known');
  const download = page.waitForEvent('download'); await clickCommand(page, '导出 CSV'); const csv = await readFile(await (await download).path());
  check(csv.subarray(0, 3).equals(Buffer.from([239, 187, 191])), 'CSV has UTF-8 BOM'); check(csv.toString('utf8').includes('工作集 (bytes)'), 'CSV includes explicit units');
  const exported = csvRecords(csv.toString('utf8'));
  check(exported.length === expectedCsvRows + 1 && exported.length > await page.locator('tbody tr[data-row-key]').count(), 'CSV exports every real filtered row beyond the virtual viewport');
  check(children.every(([pid]) => exported.slice(1).some(record => record[exported[0].findIndex(label => label.includes('PID'))] === pid)), 'CSV includes actual children hidden by collapsed tree nodes');
  await clickCommand(page, '继续显示'); await search.fill(String(workload.pid));
  await page.screenshot({ path: path.join(output, 'v11-process-tree.png') });
  await search.fill('no-such-task-unique-query'); check(await page.getByRole('button', { name: '结束任务', exact: true }).isDisabled(), 'filter excludes old selection from toolbar actions');
  await page.keyboard.press('Control+n'); await dialog.getByLabel('程序名称或完整路径', { exact: true }).fill(path.join(temporary, 'confirmed-absent', 'missing.exe')); await dialog.getByRole('button', { name: '运行', exact: true }).click(); await dialog.locator('.form-error').waitFor();
  check(await dialog.getByLabel('程序名称或完整路径', { exact: true }).inputValue() === path.join(temporary, 'confirmed-absent', 'missing.exe'), 'run failure preserves input and native error');
  await dialog.getByLabel('程序名称或完整路径', { exact: true }).fill(workloadExe); await dialog.getByLabel('参数（可选）', { exact: true }).fill(`"${path.join(temporary, 'created')}" 120`);
  const response = page.waitForResponse(r => r.url() === `${origin}/api/processes/start` && r.request().method() === 'POST'); await dialog.getByRole('button', { name: '运行', exact: true }).click(); created = await (await response).json();
  await until(async () => await page.locator(`tr[data-row-key="${created.identity}"]`).count() === 1, 'created actual identity located');
  check(await search.inputValue() === String(created.pid), 'successful run locates actual CreateProcess PID');
  const creationSample=(await get('snapshot')).sampleId; await until(async()=>{const s=await get('snapshot');return s.sampleId>creationSample&&s.processes.some(p=>p.identity===created.identity&&p.canTerminate);},'registered Run task stays actionable after subsequent real sampling'); check(true,'new Run identity remains terminable after a later native snapshot');
  await clickCommand(page, '平铺'); await page.setViewportSize({ width: 960, height: 640 });
  check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), '960px desktop has no document overflow');
  await page.screenshot({ path: path.join(output, 'v11-process-960.png') });
  await page.locator(`tr[data-row-key="${created.identity}"]`).click(); await page.getByRole('button', { name: '结束任务', exact: true }).click(); await dialog.getByRole('button', { name: '结束进程', exact: true }).click();
  await until(async () => (await dialog.innerText()).includes('已确认退出'), 'single termination confirmed by actual Windows readback'); check(true, 'single terminate UI displays actual completed result'); await dialog.getByRole('button', { name: '关闭', exact: true }).click();
  await search.fill(children[1][0]); await page.locator(`tr[data-row-key="${children[1].join(':')}"]`).dblclick(); await page.getByRole('tab', { name: '概览', exact: true }).click(); await until(async () => (await dialog.innerText()).includes('原生字段：'), 'exit fixture detail read');
  await post(`processes/${children[1][0]}/terminate`, { creationTimeFileTime: children[1][1] }); await until(async () => (await dialog.innerText()).includes('已保留最后成功信息'), 'exited detail retains data');
  check(await dialog.getByRole('button', { name: '设置优先级', exact: true }).isDisabled(), 'exited detail disables native writes while retaining last information'); await page.keyboard.press('Escape');
  await search.fill(String(workload.pid)); await row.click(); await page.locator('.command-menu > summary').click(); await clickCommand(page, '结束进程树…', {menu:true}); await page.getByRole('button', { name: '结束完整进程树', exact: true }).click();
  await until(async () => await dialog.locator('[aria-label="树结束逐项结果"]').count() > 0, 'tree native results displayed'); check(!(await dialog.locator('[aria-label="树结束逐项结果"]').innerText()).includes('失败'), 'tree UI retains successful per-member native results'); await dialog.getByRole('button', { name: '关闭', exact: true }).click();
  await search.fill(''); await page.getByRole('button', { name: '设置', exact: true }).click(); await page.getByRole('switch', { name: '自动采样', exact: true }).click(); await until(async () => (await get('snapshot')).paused, 'settings pauses true collector'); await delay(2300);
  check(snapshotResponses.includes(304), 'conditional snapshot reads receive successful empty 304');
  await page.getByRole('button', { name: '标准', exact: true }).click(); await delay(800); await page.reload(); await until(async () => (await page.locator('.app-shell').getAttribute('data-density')) === 'standard', 'JSON.NET preferences restore density after reload');
  check(true, 'persisted UI preference restored'); check(errors.length === 0, 'no browser runtime exceptions'); check(cspErrors.length === 0, 'no content security policy violations including native icon blobs');
  await writeFile(path.join(output, desktopMode ? 'process-desktop-v11-results.json' : 'process-ui-v11-results.json'), JSON.stringify({ runtime: desktopMode ? 'Release Electron.NET' : 'real Chromium', assertions, checks, errors, cspErrors, screenshotPaths: ['v11-process-tree.png', 'v11-process-960.png'] }, null, 2));
  console.log(JSON.stringify({ passed: true, assertions, checks }, null, 2));
} catch (error) { if (browser) await browser.contexts()[0]?.pages()[0]?.screenshot({ path: path.join(output, 'process-ui-v11-failure.png') }).catch(() => {}); throw error; }
finally {
  if (created?.creationTimeFileTime && token) await post(`processes/${created.pid}/terminate`, { creationTimeFileTime: created.creationTimeFileTime }).catch(() => {});
  if (ownRoot?.creationTimeFileTime && token) { const preview = await post(`processes/${ownRoot.pid}/tree-preview`, { creationTimeFileTime: ownRoot.creationTimeFileTime }).catch(() => null); if (preview?.canTerminate) await post(`processes/${ownRoot.pid}/terminate-tree`, { creationTimeFileTime: ownRoot.creationTimeFileTime, members: preview.members.map(m => ({ pid: m.pid, creationTimeFileTime: m.creationTimeFileTime })) }).catch(() => {}); }
  if(desktopMode && host.exitCode===null){const appTree=processTree(host.pid);await post('window/close',{}).catch(()=>{});await until(()=>host.exitCode!==null,'desktop process flow host close',20000);await until(async()=>!await portOpen(port)&&!await portOpen(cdp),'desktop process flow ports closed');if(!appTree.every(p=>!ownedAlive(p)))throw new Error('Desktop process flow app identity leaked');}
  await browser?.close(); if (host.exitCode === null) await stopOwnedHost(host); await delay(600); await rm(temporary, { recursive: true, force: true }).catch(() => {});
}
