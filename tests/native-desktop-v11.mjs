import { stopOwnedHost } from './etw-support.mjs';
import { clickCommand } from './layout-ui-support.mjs';
import { spawn } from 'node:child_process';
import { mkdir, mkdtemp, cp, rename, readFile, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';
import { check, assertions, until, delay, processTree, ownedAlive, portOpen, alive } from './e2e-support.mjs';
import { probe, windows, nativeUi } from './native-ui-support.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const release = process.env.TASKMANAGER_NATIVE_DIRECTORY ?? path.join(root, 'output/TaskManager-win-x64');
const temporary = await mkdtemp(path.join(tmpdir(), 'blue-native-v11-'));
const fixture = path.join(temporary, 'owned,fixture'); await mkdir(fixture);
await cp(path.join(root, 'tests/TaskManager.TestWorkload/bin/Release/net10.0-windows10.0.19041.0'), fixture, { recursive: true });
const fileName = `=BlueNative-${path.basename(temporary)}.exe`, exe = path.join(fixture, fileName);
await rename(path.join(fixture, 'TaskManager.TestWorkload.exe'), exe);
const output = path.join(root, 'output/playwright'); await mkdir(output, { recursive: true });
const port = 5195, cdp = 9225, origin = `http://127.0.0.1:${port}`, preferences = path.join(temporary, 'preferences');
if (await portOpen(port) || await portOpen(cdp)) throw new Error('Native desktop ports busy');
const start = () => spawn(path.join(release, 'TaskManager.Host.exe'), [`--port=${port}`], { cwd: tmpdir(), env: { ...process.env, TASKMANAGER_DATA_DIR: preferences, TASKMANAGER_CDP_PORT: String(cdp) }, windowsHide: true, stdio: 'ignore' });
let host, browser, page, tree = [], created, ownedWindows = [];
const checks = [], images = [];
const record = (condition, text) => { check(condition, text); checks.push(text); console.log(text); };
const snap = async () => (await fetch(origin + '/api/snapshot')).json();
const main = () => probe(tree.map(p => `${p.pid}:${p.fileTime}`).join(','));
const capture = async name => { const session = await page.context().newCDPSession(page); const result = await session.send('Page.captureScreenshot', { format: 'png', fromSurface: true, captureBeyondViewport: false }); await session.detach(); await writeFile(path.join(output, name), Buffer.from(result.data, 'base64')); images.push(name); };
async function connect() {
  await until(async () => (await (await fetch(origin + '/api/health')).json()).ready && await portOpen(cdp), 'native release ready', 45000);
  browser = await chromium.connectOverCDP(`http://127.0.0.1:${cdp}`); await until(() => browser.contexts()[0]?.pages().length > 0, 'window created'); page = browser.contexts()[0].pages()[0];
  await page.getByRole('table', { name: '进程列表', exact: true }).waitFor(); tree = processTree(host.pid);
  await until(()=>windows('蓝域').some(window=>tree.some(entry=>entry.pid===window.pid)), 'actual native BrowserWindow becomes visible');
}
async function close() {
  const userTasks = new Set(created && alive(created.pid) ? processTree(created.pid).map(p => p.pid) : []); const exact = processTree(host.pid).filter(p => !userTasks.has(p.pid)); await page.getByRole('button', { name: '关闭窗口', exact: true }).click();
  await until(() => host.exitCode !== null, 'native window close ends host', 20000); await until(async () => !await portOpen(port) && !await portOpen(cdp), 'both ports released');
  record(exact.every(p => !ownedAlive(p)), 'native close removes every exact app PID/creation identity'); await browser.close().catch(() => {}); browser = undefined;
}
try {
  host = start(); await connect(); const errors = [], csp = []; page.on('pageerror', e => errors.push(e.message)); page.on('console', m => { if (/content.security.policy|violates.*directive/i.test(m.text())) csp.push(m.text()); });
  await page.keyboard.press('Control+0');
  await page.getByRole('button', { name: '运行新任务', exact: true }).click(); const dialog = page.getByRole('dialog'); const input = dialog.getByLabel('程序名称或完整路径', { exact: true });
  await input.fill(exe); await dialog.getByLabel('参数（可选）', { exact: true }).fill(`"${path.join(temporary, 'load')}" 600`);
  await dialog.getByRole('button', { name: '浏览…', exact: true }).click();
  let picker; await until(() => { picker = windows('选择要运行的程序').find(w => tree.some(p => p.pid === w.pid)); return Boolean(picker); }, 'real Windows exe dialog appears');
  const pickerInfo = nativeUi(picker, 'inspect');
  record(picker.className === '#32770' && pickerInfo.controls.some(c => c.className === 'Edit'), 'B11 actual Electron-owned common file dialog has native Edit controls');
  nativeUi(picker, 'capture', path.join(output, 'v11-native-picker.png')); images.push('v11-native-picker.png');
  await delay(10200);
  record(windows('选择要运行的程序').some(w => w.hwnd === picker.hwnd) && await input.isDisabled(), 'B11 native picker stays open beyond 9 seconds without client timeout');
  nativeUi(picker, 'cancel'); await until(async () => await input.isEnabled(), 'cancel completes actual dialog response');
  record(await input.inputValue() === exe, 'B11 actual native cancel preserves executable input');
  await input.fill('notepad.exe'); await dialog.getByRole('button', { name: '浏览…', exact: true }).click();
  await until(() => { picker = windows('选择要运行的程序').find(w => tree.some(p => p.pid === w.pid)); return Boolean(picker); }, 'second native picker');
  const info = nativeUi(picker, 'inspect'); const edit = info.controls.find(c => c.className === 'Edit' && c.id === 1148) ?? info.controls.find(c => c.className === 'Edit' && (c.id === 1001 || c.id === 1152));
  if (!edit) throw new Error('Native filename Edit was not observed: ' + JSON.stringify(info.controls));
  nativeUi(picker, 'choose', edit.hwnd, exe); await until(async () => await input.isEnabled(), 'real common-dialog accepts observed filename control');
  record(await input.inputValue() === exe, 'B11 native exe selection returns exact owned path including comma');
  const response = page.waitForResponse(r => r.url() === origin + '/api/processes/start' && r.request().method() === 'POST'); await dialog.getByRole('button', { name: '运行', exact: true }).click(); created = await (await response).json();
  record(created.pid > 0 && alive(created.pid), 'B11 chosen file creates independently live exact Windows process');
  const row = page.locator(`tr[data-row-key="${created.identity}"]`); await row.waitFor(); await row.click();
  const [csvDownload] = await Promise.all([page.waitForEvent('download'), clickCommand(page,'导出 CSV')]); const csv = await readFile(await csvDownload.path());
  record(csv.subarray(0,3).equals(Buffer.from([239,187,191])) && csv.toString('utf8').includes(`"'${fileName}"`) && csv.toString('utf8').includes(`"${exe}"`) && csv.toString('utf8').includes('工作集 (bytes)'), 'B07 actual Electron CSV preserves comma path and neutralizes real formula-prefixed executable name with BOM/units');
  const shellBefore = new Set(windows('class:CabinetWClass').map(w => w.hwnd));
  await page.locator('.command-menu > summary').click(); const locationResponse = page.waitForResponse(r => r.url().endsWith('/file-action')); await clickCommand(page, '打开文件位置', {menu:true}); console.log('file-action:', await (await locationResponse).text());
  let explorer, locationInfo;
  await until(() => {
    for (const w of windows('class:CabinetWClass').filter(w => !shellBefore.has(w.hwnd) || w.title.includes('owned,fixture'))) {
      const inspected = nativeUi(w, 'inspect'); 
      if (inspected.elements.some(e => e.selected === true && (e.name === fileName || e.name === path.parse(fileName).name))) { explorer = w; locationInfo = inspected; return true; }
    }
    return false;
  }, 'actual Explorer selects exact unique owned executable', 30000);
  if (!shellBefore.has(explorer.hwnd)) ownedWindows.push(explorer); record(locationInfo.elements.some(e => e.selected === true && e.name.includes(path.parse(fileName).name)), 'B06 standard Windows Explorer select command visibly selects unique test executable');
  nativeUi(explorer, 'capture', path.join(output, 'v11-native-file-location.png')); images.push('v11-native-file-location.png');
  nativeUi(windows('蓝域').find(w=>tree.some(p=>p.pid===w.pid)), 'foreground'); await page.bringToFront(); await row.click(); await page.locator('.command-menu > summary').click(); await clickCommand(page, 'Windows 属性', {menu:true});
  let properties, propInfo;
  await until(() => { properties = windows(path.parse(fileName).name).find(w => w.className === '#32770'); if (!properties) return false; propInfo = nativeUi(properties, 'inspect'); return propInfo.elements.some(e => (e.name ?? '').includes(fileName) || (e.value ?? '').includes(fileName)); }, 'actual Windows properties for exact test file', 30000);
  ownedWindows.push(properties); record(propInfo.elements.some(e => (e.name ?? '').includes(fileName) || (e.value ?? '').includes(fileName)), 'B06 actual Windows property sheet identifies owned filename');
  nativeUi(properties, 'capture', path.join(output, 'v11-native-properties.png')); images.push('v11-native-properties.png');
  nativeUi(properties, 'close'); ownedWindows = ownedWindows.filter(w => w.hwnd !== properties.hwnd); if (!shellBefore.has(explorer.hwnd)) nativeUi(explorer, 'close'); ownedWindows = ownedWindows.filter(w => w.hwnd !== explorer.hwnd); nativeUi(windows('蓝域').find(w=>tree.some(p=>p.pid===w.pid)), 'foreground'); await page.bringToFront();
  await page.getByRole('searchbox').fill('');await page.getByRole('button', { name: '设置', exact: true }).click(); await page.getByRole('switch', { name: '始终置顶', exact: true }).click();
  await until(() => main().topmost, 'independent native topmost style'); record(main().topmost, 'B10 actual WS_EX_TOPMOST matches enabled UI readback');
  probe(tree.map(p => `${p.pid}:${p.fileTime}`).join(','), 'resize', 960, 640); await delay(800); await page.keyboard.press('Control+='); await delay(500);
  const state = await (await fetch(origin + '/api/desktop/window-state')).json(); console.log('zoom/bounds', JSON.stringify({state,native:main(),viewport:await page.evaluate(()=>({w:innerWidth,h:innerHeight,dpr:devicePixelRatio}))})); record(state.zoomFactor === 1.25 && main().clientWidth === 960 && main().clientHeight === 640 && await page.evaluate(() => Math.abs(devicePixelRatio-1.25)<.02 && Math.abs(innerWidth-960/1.25)<=1), 'B17 native 960px width and actual Electron 125% zoom combined');
  record(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'B17 960 native window at125% has no document horizontal overflow'); await capture('v11-desktop-960-125-settings.png');
  await page.getByRole('button', { name: '进程', exact: true }).click();await page.getByRole('searchbox').fill(String(created.pid)); await row.waitFor(); await row.dblclick();
  record(await dialog.evaluate(el => { const r = el.getBoundingClientRect(); return r.left >= 0 && r.right <= innerWidth + 1 && r.bottom <= innerHeight + 1; }), 'B17 live details fit 960 window at125%'); await capture('v11-desktop-960-125-details.png'); await page.keyboard.press('Escape');
  await row.click(); await row.press('Shift+F10'); record(await page.getByRole('menu').evaluate(el => { const r = el.getBoundingClientRect(); return r.left >= 0 && r.right <= innerWidth + 1 && r.bottom <= innerHeight + 1; }), 'B17 keyboard menu fits combined minimum native window and125%'); await page.keyboard.press('Escape');
  // Observe actual hidden DOM from native minimize; HTTP request scheduling only.
  // Disconnect the interaction session so its default focus emulation is removed.
  await browser.close(); record(alive(host.pid) && await portOpen(cdp), 'CDP disconnect preserves actual Electron window'); browser = await chromium.connectOverCDP(`http://127.0.0.1:${cdp}`, {noDefaults:true}); page = browser.contexts()[0].pages()[0];
  const visibilitySession = await page.context().newCDPSession(page); await visibilitySession.send('Emulation.setFocusEmulationEnabled',{enabled:false});
  const polls = []; page.on('request', r => { if (r.url() === origin + '/api/snapshot') polls.push(Date.now()); });
  await page.getByRole('button', { name: '最小化窗口', exact: true }).focus(); await page.keyboard.press('Enter'); await delay(400); console.log('minimize check',JSON.stringify({native:main(),visibility:await page.evaluate(()=>({hidden:document.hidden,state:document.visibilityState}))})); await until(async () => await page.evaluate(() => document.hidden), 'native minimize sets document.hidden'); const hiddenAt = Date.now(); await delay(11200);
  const hiddenPolls = polls.filter(t => t > hiddenAt + 500); record(hiddenPolls.length <= 3 && hiddenPolls.slice(1).every((t,i) => t-hiddenPolls[i] >= 4800), 'B09 actual minimized Electron throttles snapshot polling to >=5s');
  probe(tree.map(p => `${p.pid}:${p.fileTime}`).join(','), 'restore'); await until(async () => !await page.evaluate(() => document.hidden), 'native restore visible'); await visibilitySession.detach();
  const resumeAt = Date.now(); await until(() => polls.some(t => t >= resumeAt), 'visible window immediately schedules latest snapshot', 2500); record(true, 'B09 visible restore immediately resumes real polling');
  await delay(1000); const saved = JSON.parse(await readFile(path.join(preferences, 'preferences.json'), 'utf8'));
  record(!/"token"|"processes"\s*:\s*\[|"paused"|"frozen"/.test(JSON.stringify(saved)), 'B10 persisted JSON.NET preferences contain no token/snapshots/pause/freeze');
  const expectedWindow = saved.ui.window; await close(); record(alive(created.pid), 'desktop close does not terminate independently running user task');
  host = start(); await connect(); const restored = main(); const restoredState = await (await fetch(origin + '/api/desktop/window-state')).json();
  console.log('restore dimensions',JSON.stringify({expectedWindow,restored,restoredState})); record(restored.topmost && restored.clientWidth === expectedWindow.width && restored.clientHeight === expectedWindow.height && restoredState.zoomFactor === expectedWindow.zoomFactor && await page.evaluate(()=>Math.abs(devicePixelRatio-1.25)<.02), 'B10 restart restores native size/topmost and actual zoom preferences');
  await page.keyboard.press('Control+0'); await page.getByRole('searchbox').fill('');await page.getByRole('button', { name: '设置', exact: true }).click(); await page.getByRole('switch', { name: '始终置顶', exact: true }).click();
  record(errors.length === 0 && csp.length === 0, 'native desktop workflow has zero renderer exceptions/CSP errors'); await close();
  const report = { result: 'PASS', checkedAtUtc: new Date().toISOString(), assertions: checks.length, checks, screenshots: images, nativeSources: 'CsWin32 common-dialog controls/UI Automation/PrintWindow/extended style/placement', cleanup: 'exact app identities and ports released; only new exact test Shell HWNDs closed; unique owned workload cleaned in finally' };
  await writeFile(path.join(output, 'native-desktop-v11-results.json'), JSON.stringify(report, null, 2)); console.log(JSON.stringify(report, null, 2));
} catch (error) { await page?.screenshot({ path: path.join(output, 'native-desktop-v11-failure.png') }).catch(() => {}); throw error; }
finally {
  for (const w of ownedWindows) { try { nativeUi(w, 'close'); } catch {} }
  if (created && alive(created.pid)) {
    try { const current = probe('process-resources', created.pid, created.creationTimeFileTime); if (current) process.kill(created.pid); } catch { }
  }
  if (host?.exitCode === null) { const remaining = processTree(host.pid); await stopOwnedHost(host); for (const p of remaining.reverse()) if (ownedAlive(p)) { try { process.kill(p.pid); } catch {} } }
  await browser?.close().catch(() => {}); await delay(500); await rm(temporary, { recursive: true, force: true });
}
