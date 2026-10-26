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

const root = path.resolve(import.meta.dirname, '..');
const desktop = process.env.TASKMANAGER_CHART_MODE === 'desktop', mode = desktop ? 'desktop' : 'browser';
const release = path.resolve(process.env.TASKMANAGER_CHART_RELEASE_DIRECTORY ?? path.join(root, 'src/TaskManager.Host/bin/Release/net10.0-windows10.0.19041.0'));
const output = path.join(root, 'output/playwright/chart-palette');
const port = 5343, cdp = 9343, origin = `http://127.0.0.1:${port}`;
const checks = [], screenshots = [], readings = [], errors = [], writes = [], notExecuted = [];
const check = (condition, message) => { assert.ok(condition, message); checks.push(message); };
const hash = value => createHash('sha256').update(value).digest('hex');
await mkdir(output, { recursive: true });
check(probe('process-elevation', process.pid).elevated, 'controller has a real elevated Windows token');
check(!await portOpen(port) && !await portOpen(cdp), 'dedicated chart acceptance listeners are free');
const temporary = await mkdtemp(path.join(tmpdir(), 'taskmanager-chart-palette-'));
const artifacts = [], index = await readFile(path.join(release, 'wwwroot/index.html'), 'utf8');
for (const file of ['TaskManager.Host.exe', 'TaskManager.Host.dll', 'wwwroot/index.html', ...[...index.matchAll(/(?:src|href)="([^\"]+\.(?:js|css))"/g)].map(match => 'wwwroot/' + match[1].replace(/^\//, ''))]) {
  const bytes = await readFile(path.join(release, file)); artifacts.push({ file, sha256: hash(bytes), bytes: bytes.length });
}
const host = spawn(path.join(release, 'TaskManager.Host.exe'), [...(desktop ? [] : ['--headless']), `--port=${port}`], {
  cwd: release, windowsHide: true, stdio: 'ignore', env: { ...process.env, TASKMANAGER_DATA_DIR: temporary, TASKMANAGER_CDP_PORT: String(cdp) }
});
let browser, page, token, failure, first, snapshot, tree = [], cleanup = false;
async function api(route, body) {
  const response = await fetch(`${origin}/api/${route}`, { method: body === undefined ? 'GET' : 'POST', headers: { Origin: origin, ...(body === undefined ? {} : { 'Content-Type': 'application/json', 'X-TaskManager-Token': token }) }, body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(15000) });
  assert.ok(response.ok, `${route}: ${response.status}`); return response.json();
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
    const window = windows('蓝域').find(window => tree.some(entry => entry.pid === window.pid));
    check(Boolean(window), 'native screenshot belongs to the test-owned Electron process tree');
    const nativeFile = `${mode}-${name}-native.png`; nativeUi(window, 'capture', path.join(output, nativeFile)); screenshots.push(nativeFile);
  }
}
const tile = text => page.locator('.resource-tile').filter({ has: page.getByText(text, { exact: true }) });
async function select(target) { await target.focus(); await target.press('Enter'); await page.locator('.resource-content').evaluate(node => node.scrollTop = 0); }
async function inspect(label, { dual = false, logical = false, miniOnly = false } = {}) {
  const value = await page.evaluate(({ logical, miniOnly }) => {
    const charts = [...document.querySelectorAll(miniOnly ? '.resource-list .resource-chart' : '.resource-content .resource-chart')];
    const luminance = color => {
      const components = color.match(/[\d.]+/g).slice(0, 3).map(Number).map(value => { const part = value / 255; return part <= .04045 ? part / 12.92 : ((part + .055) / 1.055) ** 2.4; });
      return .2126 * components[0] + .7152 * components[1] + .0722 * components[2];
    };
    const contrast = color => 1.05 / (luminance(color) + .05);
    const labels = [...document.querySelectorAll('.chart-scale,.chart-axis,.chart-legend,.logical-cpu-chart header,.chart-wait')];
    const rootStyle = getComputedStyle(document.documentElement);
    return {
      viewport: [innerWidth, innerHeight], overflow: document.documentElement.scrollWidth > innerWidth + 1,
      reducedMotion: matchMedia('(prefers-reduced-motion: reduce)').matches,
      heat: rootStyle.getPropertyValue('--color-heat-0').trim(), selection: rootStyle.getPropertyValue('--color-selection').trim(),
      labels: labels.map(node => ({ text: node.textContent, color: getComputedStyle(node).color, contrast: contrast(getComputedStyle(node).color) })),
      legend: [...document.querySelectorAll('.resource-content .chart-legend i')].map(node => ({ color: getComputedStyle(node).borderTopColor, style: getComputedStyle(node).borderTopStyle })),
      charts: charts.map(chart => ({
        background: getComputedStyle(chart).backgroundColor, border: getComputedStyle(chart).borderTopColor,
        aria: chart.getAttribute('aria-label'), mini: Boolean(chart.closest('.mini-chart')),
        grid: [...chart.querySelectorAll('.chart-grid')].map(node => getComputedStyle(node).stroke),
        series: [...chart.querySelectorAll(':scope > g')].map((group, index) => ({ index,
          paths: [...group.querySelectorAll('path')].map(node => ({ stroke: getComputedStyle(node).stroke, contrast: contrast(getComputedStyle(node).stroke), dash: getComputedStyle(node).strokeDasharray, opacity: getComputedStyle(node).opacity })),
          points: [...group.querySelectorAll('circle')].map(node => ({ color: getComputedStyle(node).fill, contrast: contrast(getComputedStyle(node).fill) }))
        }))
      })),
      logicalCount: logical ? document.querySelectorAll('.logical-cpu-chart').length : null,
      logicalIds: logical ? [...document.querySelectorAll('.logical-cpu-chart')].map(node => node.dataset.cpuId) : []
    };
  }, { logical, miniOnly });
  readings.push({ label, ...value });
  check(value.charts.length > 0 && value.charts.every(chart => chart.background === 'rgb(255, 255, 255)' && chart.border === 'rgb(229, 229, 229)'), `${label}: every rendered plot has a white surface and neutral boundary`);
  check(value.charts.every(chart => chart.grid.every(color => color === 'rgb(236, 239, 242)')), `${label}: plot grids use neutral light gray`);
  const series = value.charts.flatMap(chart => chart.series);
  check(series.every(group => group.paths.every(line => line.stroke === (group.index ? 'rgb(88, 99, 111)' : 'rgb(0, 103, 192)') && line.contrast >= 3 && line.opacity === '1') && group.points.every(point => point.color === (group.index ? 'rgb(88, 99, 111)' : 'rgb(0, 103, 192)') && point.contrast >= 3)), `${label}: visible data lines and points use opaque readable blue/gray tokens`);
  check(series.every(group => group.paths.every(line => group.index ? line.dash !== 'none' : line.dash === 'none')), `${label}: primary solid and secondary dashed styles remain distinct`);
  check(value.labels.every(label => label.contrast >= 4.5), `${label}: axes, legends and state labels retain readable contrast`);
  check(!value.overflow && value.reducedMotion && value.heat.toLowerCase() === '#f5f9ff' && value.selection.toLowerCase() === '#e8eef5', `${label}: reduced-motion, layout and existing heat/selection roles remain intact`);
  if (dual) check(value.charts.every(chart => chart.series.length === 2) && value.legend.length === 2 && value.legend[0].color === 'rgb(0, 103, 192)' && value.legend[1].color === 'rgb(88, 99, 111)' && value.legend[1].style === 'dashed', `${label}: both throughput series and matching named legends remain available`);
  if (logical) check(value.logicalCount > 0 && value.logicalCount <= 48 && value.logicalIds.every(id => snapshot.logicalProcessors.some(cpu => cpu.id === id)), `${label}: CPU grid retains real bounded group/index identities`);
  return value;
}
async function viewport(width, height) {
  if (desktop) { probe(tree.map(entry => `${entry.pid}:${entry.fileTime}`).join(','), 'resize', width, height); await delay(350); }
  else await page.setViewportSize({ width, height });
}
try {
  await until(async () => (await api('health')).ready, 'current Release Host starts', 45000);
  const health = await api('health'), bootstrap = await api('bootstrap'); token = bootstrap.token;
  first = await api('snapshot');
  check(probe('process-elevation', host.pid).elevated && /CsWin32/.test(health.nativeSource) && /Newtonsoft|JSON.NET/.test(health.json), 'production Host retains the elevated CsWin32/JSON.NET chain');
  await until(async () => { snapshot = await api('snapshot'); return snapshot.telemetry?.sampleId >= 4 && snapshot.performanceHistory.length >= 6 && snapshot.logicalProcessors.length; }, 'real resource histories and CPU identities warm up', 45000);
  check(snapshot.performanceHistory.length <= 120 && snapshot.performanceHistory.every(point => point.capturedAtUtc), 'history retains its bounded real source timestamps');
  if (desktop) {
    await until(() => portOpen(cdp), 'official Electron CDP starts', 45000); browser = await chromium.connectOverCDP(`http://127.0.0.1:${cdp}`);
    await until(() => browser.contexts().flatMap(context => context.pages()).some(page => page.url().startsWith(origin)), 'official Electron page', 45000);
    page = browser.contexts().flatMap(context => context.pages()).find(page => page.url().startsWith(origin));
  } else { browser = await chromium.launch({ headless: true }); page = await browser.newPage({ viewport: { width: 1380, height: 860 }, reducedMotion: 'reduce' }); await page.goto(origin); }
  page.setDefaultTimeout(10000); page.on('pageerror', error => errors.push(error.message));
  page.on('request', request => { if (request.method() === 'POST' && /task-manager-integration|services\/.*\/control/.test(request.url())) writes.push(request.url()); });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  if (desktop) { await until(async () => (await api('bootstrap')).desktop, 'actual Electron BrowserWindow registration'); await delay(500); const defer = page.getByRole('button', { name: '暂不替换', exact: true }); if (await defer.isVisible()) await defer.click(); }
  tree = processTree(host.pid); check((await api('bootstrap')).desktop === desktop, `${mode}: actual bootstrap mode`);
  await page.getByRole('button', { name: '性能', exact: true }).click(); await viewport(1380, 860);
  await select(tile('CPU')); await inspect('1380-cpu'); await inspect('all-mini', { miniOnly: true }); await capture('1380-cpu');
  check(await tile('CPU').evaluate(node => document.activeElement === node && getComputedStyle(node).outlineStyle !== 'none'), 'keyboard-selected resource keeps a visible focus outline');
  await page.getByLabel('CPU 图表模式', { exact: true }).selectOption('logicalProcessors');
  await inspect('1380-logical-cpu', { logical: true }); await capture('1380-logical-cpu');
  await page.getByLabel('CPU 图表模式', { exact: true }).selectOption('total');
  await select(tile('内存')); await inspect('1380-memory'); await capture('1380-memory');
  const disks = snapshot.physicalDisks.filter(disk => !disk.isTotal);
  const picturedDisk = [...disks].sort((a,b) => (b.readBytesPerSecond ?? 0) + (b.writeBytesPerSecond ?? 0) - (a.readBytesPerSecond ?? 0) - (a.writeBytesPerSecond ?? 0))[0];
  for (const disk of disks) { await select(tile(`磁盘 ${disk.name}`)); await inspect(`1380-disk-${disk.id}`, { dual: true }); check(await page.locator('.resource-content .resource-chart').count() === 1 && !/活动时间|活動率|活动率/.test(await page.locator('.resource-content').textContent()), `disk ${disk.id}: D08 keeps one throughput plot without activity UI`); if (disk.id === picturedDisk.id) await capture('1380-disk'); }
  if (!disks.length) notExecuted.push('disk success palette: Windows returned no physical disk instances');
  const picturedNetwork = snapshot.networkAdapters.find(adapter => adapter.status === 'IfOperStatusUp') ?? snapshot.networkAdapters[0];
  for (const adapter of snapshot.networkAdapters) { await select(tile(adapter.alias)); await inspect(`1380-network-${adapter.id}`, { dual: true }); if (adapter.id === picturedNetwork.id) await capture('1380-network'); }
  if (!snapshot.networkAdapters.length) notExecuted.push('network success palette: Windows returned no hardware adapters');
  const gpuTiles = page.locator('.resource-tile').filter({ has: page.getByText('GPU', { exact: true }) });
  for (let index = 0; index < snapshot.gpuAdapters.length; index++) { await select(gpuTiles.nth(index)); await inspect(`1380-gpu-${index}`); if (!index) await capture('1380-gpu'); }
  if (!snapshot.gpuAdapters.length) notExecuted.push('GPU success palette: Windows returned no GPU instances');
  const observed = readings.flatMap(reading => reading.charts).flatMap(chart => chart.series);
  check(observed.some(group => group.index === 0 && group.paths.length) && observed.some(group => group.index === 1 && group.paths.length), 'real primary and secondary data paths were both observed, beyond empty-state styling');
  await viewport(960, 640); await select(tile('CPU')); await inspect('960-cpu'); await capture('960-cpu');
  await page.getByLabel('CPU 图表模式', { exact: true }).selectOption('logicalProcessors'); await inspect('960-logical-cpu', { logical: true }); await capture('960-logical-cpu'); await page.getByLabel('CPU 图表模式', { exact: true }).selectOption('total');
  if (picturedDisk) { await select(tile(`磁盘 ${picturedDisk.name}`)); await inspect('960-disk', { dual: true }); await capture('960-disk'); }
  if (desktop) {
    await viewport(1380, 860); await page.keyboard.press('Control+='); await until(async () => (await api('desktop/window-state')).zoomFactor === 1.25, 'real Electron zoom readback reaches 125%');
    await select(tile('CPU')); await inspect('125-cpu'); await capture('125-cpu', true);
    await page.getByLabel('CPU 图表模式', { exact: true }).selectOption('logicalProcessors'); await inspect('125-logical-cpu', { logical: true }); await capture('125-logical-cpu'); await page.getByLabel('CPU 图表模式', { exact: true }).selectOption('total');
    if (picturedDisk) { await select(tile(`磁盘 ${picturedDisk.name}`)); await inspect('125-disk', { dual: true }); await capture('125-disk'); }
    await page.keyboard.press('Control+0');
  }
  await api('settings', { intervalMs: 1000, paused: true }); await delay(400); const paused = await api('snapshot'); await delay(1400); const stillPaused = await api('snapshot');
  check(paused.sampleId === stillPaused.sampleId && JSON.stringify(paused.performanceHistory) === JSON.stringify(stillPaused.performanceHistory), 'pause retains the real last history without fabricating plot points');
  await api('settings', { intervalMs: 1000, paused: false }); await until(async () => (await api('snapshot')).sampleId > paused.sampleId, 'real sampling resumes');
  check(writes.length === 0 && errors.length === 0, 'palette browsing has no IFEO/service writes or UI runtime errors');
} catch (error) { failure = { message: error.message, stack: error.stack }; }
finally {
  const identities = processTree(host.pid);
  if (desktop && page && !page.isClosed()) { await api('window/close', {}).catch(() => {}); await until(() => host.exitCode !== null, 'owned Electron Host closes', 20000).catch(() => {}); }
  await browser?.close().catch(() => {}); if (host.exitCode === null) await stopOwnedHost(host);
  await until(() => host.exitCode !== null || host.signalCode !== null, 'owned Host exits').catch(() => {});
  cleanup = !await portOpen(port) && !await portOpen(cdp) && identities.every(entry => !ownedAlive(entry));
  if (!cleanup && !failure) failure = { message: 'test-owned identities or listeners remain' };
  await rm(temporary, { recursive: true, force: true, maxRetries: 5, retryDelay: 100 });
  const report = { result: failure ? 'FAIL' : 'PASS', mode, checkedAtUtc: new Date().toISOString(), checkoutAtRun: spawnSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8', windowsHide: true }).stdout.trim(), assertions: checks.length, checks, artifacts, firstSourceState: first ? { sampleId: first.sampleId, telemetrySampleId: first.telemetry?.sampleId, unavailable: first.telemetry?.unavailable } : null, resourceCounts: snapshot ? { disks: snapshot.physicalDisks.length, network: snapshot.networkAdapters.length, gpu: snapshot.gpuAdapters.length, logicalCpu: snapshot.logicalProcessors.length } : null, readings, screenshots, errors, writes, cleanup, failure, notExecuted, source: 'production Release UI → HTTP → JSON.NET → CsWin32 → actual Windows resources; no business mocks' };
  await writeFile(path.join(output, `${mode}-results.json`), JSON.stringify(report, null, 2) + '\n'); console.log(JSON.stringify({ ...report, readings: report.readings.map(({label, charts, labels, ...rest}) => ({label, charts: charts.length, labels: labels.length, ...rest})) }, null, 2)); if (failure) process.exitCode = 1;
}
