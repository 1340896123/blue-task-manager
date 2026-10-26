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
const desktop = process.env.TASKMANAGER_DISK_MODE === 'desktop', mode = desktop ? 'desktop' : 'browser';
const release = path.resolve(process.env.TASKMANAGER_DISK_RELEASE_DIRECTORY ?? path.join(root, 'src/TaskManager.Host/bin/Release/net10.0-windows10.0.19041.0'));
const output = path.join(root, 'output/playwright/disk-identity');
const port = 5342, cdp = 9342, origin = `http://127.0.0.1:${port}`;
const checks = [], screenshots = [], geometry = [], errors = [], writes = [];
const notExecuted = ['physical hot unplug/replug and device-number reuse', 'real access-denied/unsupported/RAID or virtual disk identity branches; production parser/cache boundaries are covered separately by TaskManager.DiskTests'];
const check = (condition, message) => { assert.ok(condition, message); checks.push(message); };
const hash = value => createHash('sha256').update(value).digest('hex');
await mkdir(output, { recursive: true });
check(probe('process-elevation', process.pid).elevated, 'acceptance controller has a real elevated Windows token');
check(!await portOpen(port) && !await portOpen(cdp), 'dedicated test listeners are free');
const temporary = await mkdtemp(path.join(tmpdir(), 'taskmanager-disk-identity-'));
const artifacts = [];
const index = await readFile(path.join(release, 'wwwroot/index.html'), 'utf8');
for (const file of ['TaskManager.Host.exe', 'TaskManager.Host.dll', 'wwwroot/index.html', ...[...index.matchAll(/(?:src|href)="([^\"]+\.(?:js|css))"/g)].map(match => 'wwwroot/' + match[1].replace(/^\//, ''))]) {
  const bytes = await readFile(path.join(release, file)); artifacts.push({ file, sha256: hash(bytes), bytes: bytes.length });
}
const host = spawn(path.join(release, 'TaskManager.Host.exe'), [...(desktop ? [] : ['--headless']), `--port=${port}`], {
  cwd: release, windowsHide: true, stdio: 'ignore', env: { ...process.env, TASKMANAGER_DATA_DIR: temporary, TASKMANAGER_CDP_PORT: String(cdp) }
});
let browser, page, token, failure, snapshot, independent, tree = [], cleanup = false;
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
    check(Boolean(window), 'native screenshot belongs to the exact test-owned Electron tree');
    const nativeFile = `${mode}-${name}-native.png`; nativeUi(window, 'capture', path.join(output, nativeFile)); screenshots.push(nativeFile);
  }
}
const tile = disk => page.locator('.resource-tile').filter({ has: page.getByText(`磁盘 ${disk.name}`, { exact: true }) });
const fact = label => page.locator('.resource-fact-grid > div').filter({ has: page.getByText(label, { exact: true }) }).locator('dd');
async function verifyLayout(label, disk) {
  await page.getByRole('searchbox').fill(''); await tile(disk).click();
  await page.locator('.resource-content').evaluate(node => node.scrollTop = 0);
  const value = await page.evaluate(() => {
    const heading = document.querySelector('.disk-heading'), model = heading.querySelector('span');
    const rect = element => { const box = element.getBoundingClientRect(); return { left: box.left, right: box.right, width: box.width, height: box.height }; };
    return { viewport: [innerWidth, innerHeight], overflow: document.documentElement.scrollWidth > innerWidth + 1, heading: rect(heading), model: rect(model), modelWhiteSpace: getComputedStyle(model).whiteSpace, modelSelectable: getComputedStyle(model).userSelect, reducedMotion: matchMedia('(prefers-reduced-motion: reduce)').matches, charts: document.querySelectorAll('.resource-content .resource-chart').length };
  });
  geometry.push({ label, ...value });
  check(!value.overflow && value.model.right <= value.viewport[0] + 1, `${label}: complete disk model fits without document horizontal overflow`);
  check(value.modelWhiteSpace === 'normal' && value.modelSelectable === 'text', `${label}: model supports wrapping and text selection`);
  check(value.charts === 2 && value.reducedMotion, `${label}: activity/read-write charts and reduced-motion remain available`);
  await capture(`${label}-heading`, desktop && label === '125');
  await fact('型号').scrollIntoViewIfNeeded();
  check((await fact('型号').innerText()) === disk.model && (await fact('类型').innerText()) === disk.mediaType && (await fact('总线类型').innerText()) === disk.busType, `${label}: readable facts match the selected actual disk`);
  await capture(`${label}-facts`);
}
try {
  await until(async () => (await api('health')).ready, 'current Release Host starts', 45000);
  const health = await api('health'), bootstrap = await api('bootstrap'); token = bootstrap.token;
  check(probe('process-elevation', host.pid).elevated, 'production Host retains its administrator requirement');
  check(/CsWin32/.test(health.nativeSource) && /Newtonsoft|JSON.NET/.test(health.json), 'production API reports CsWin32 and JSON.NET');
  await until(async () => { snapshot = await api('snapshot'); return snapshot.telemetry?.sampleId >= 3 && snapshot.physicalDisks.length && snapshot.physicalDisks.every(disk => disk.identityCapturedAtUtc && ['deviceNumber','model','mediaType','busType','activePercent','readBytesPerSecond','writeBytesPerSecond'].every(field => disk[field] !== null || disk.unavailable[field])); }, 'attempted disk identity and adjacent PDH samples with explicit availability', 45000);
  const storage = spawnSync('powershell.exe', ['-NoProfile', '-Command', String.raw`$ErrorActionPreference='Stop';[pscustomobject]@{physical=@(Get-PhysicalDisk|Select-Object DeviceId,FriendlyName,@{n='MediaType';e={$_.MediaType.ToString()}},@{n='BusType';e={$_.BusType.ToString()}});disks=@(Get-Disk|Select-Object Number,FriendlyName,BusType);win32=@(Get-CimInstance Win32_DiskDrive|Select-Object Index,Model);instances=@((Get-Counter '\PhysicalDisk(*)\Disk Read Bytes/sec').CounterSamples.InstanceName|Where-Object {$_ -ne '_total'})}|ConvertTo-Json -Compress -Depth 5`], { encoding: 'utf8', windowsHide: true, timeout: 30000 });
  assert.equal(storage.status, 0, storage.stderr); independent = JSON.parse(storage.stdout);
  check(snapshot.capabilities.diskIdentity.available, 'disk identity capability exposes its native source');
  for (const disk of snapshot.physicalDisks) {
    const expectedNumber = Number(disk.name.split(' ')[0]);
    const source = independent.physical.find(row => Number(row.DeviceId) === expectedNumber);
    check(!disk.isTotal && independent.instances.includes(disk.name.toLowerCase()) && (disk.deviceNumber === null ? Boolean(disk.unavailable.deviceNumber) : expectedNumber === disk.deviceNumber), `disk ${disk.id}: actual PDH identity has a verified native number or an explicit failure`);
    const modelMatches = disk.model === null ? Boolean(disk.unavailable.model) : Boolean(source?.FriendlyName?.includes(disk.model));
    const mediaMatches = disk.mediaType === null ? Boolean(disk.unavailable.mediaType) : Boolean(source && ['HDD','SSD'].includes(source.MediaType) && disk.mediaType === source.MediaType);
    const busMatches = disk.busType === null ? Boolean(disk.unavailable.busType) : Boolean(source && source.BusType !== 'Unknown' && disk.busType === source.BusType);
    check(modelMatches && mediaMatches && busMatches, `disk ${disk.id}: each model/media/bus field matches a definite independent Windows result or has an explicit reason`);
    check(disk.deviceNumber === null || independent.disks.some(row => row.Number === disk.deviceNumber) && independent.win32.some(row => row.Index === disk.deviceNumber), `disk ${disk.id}: available physical index is confirmed by Get-Disk and Win32_DiskDrive`);
    check(disk.identityCapturedAtUtc && ['model', 'mediaType', 'busType', 'deviceNumber'].every(field => disk[field] !== null || disk.unavailable[field]), `disk ${disk.id}: each identity field has a value or a reason`);
    check(['activePercent','readBytesPerSecond','writeBytesPerSecond'].every(field => disk[field] === null ? Boolean(disk.unavailable[field]) : Number.isFinite(disk[field]) && disk[field] >= 0) && (disk.activePercent === null || disk.activePercent <= 100), `disk ${disk.id}: live PDH metrics have valid values or explicit reasons`);
  }
  check(snapshot.physicalDisks.every(disk => disk.activePercent !== null) ? snapshot.system.diskActivePercent === Math.max(...snapshot.physicalDisks.map(disk => disk.activePercent)) : snapshot.system.diskActivePercent === null && snapshot.system.unavailable.diskActivePercent, 'B14: system activity is the busiest complete physical sample or explicitly unavailable');
  check(snapshot.physicalDisks.every(disk => disk.readBytesPerSecond !== null) ? snapshot.system.diskReadBytesPerSecond === snapshot.physicalDisks.reduce((sum, disk) => sum + disk.readBytesPerSecond, 0) : snapshot.system.diskReadBytesPerSecond === null && snapshot.system.unavailable.diskReadBytesPerSecond, 'B14: read rates aggregate each complete physical instance once or remain explicitly unavailable');
  check(snapshot.performanceHistory.every(point => point.physicalDisks.every(disk => !('model' in disk) && !('mediaType' in disk))), 'static identities are absent from bounded performance history');
  if (desktop) {
    await until(() => portOpen(cdp), 'official Electron CDP starts', 45000); browser = await chromium.connectOverCDP(`http://127.0.0.1:${cdp}`);
    await until(() => browser.contexts().flatMap(context => context.pages()).some(page => page.url().startsWith(origin)), 'official Electron page', 45000);
    page = browser.contexts().flatMap(context => context.pages()).find(page => page.url().startsWith(origin));
  } else { browser = await chromium.launch({ headless: true }); page = await browser.newPage({ viewport: { width: 1380, height: 860 }, reducedMotion: 'reduce' }); await page.goto(origin); }
  page.setDefaultTimeout(10000); page.on('pageerror', error => errors.push(error.message)); page.on('request', request => { if (request.method() === 'POST' && /task-manager-integration|services\/.*\/control/.test(request.url())) writes.push(request.url()); });
  await page.emulateMedia({ reducedMotion: 'reduce' }); await page.getByRole('button', { name: '性能', exact: true }).waitFor();
  if (desktop) { await until(async () => (await api('bootstrap')).desktop, 'actual Electron BrowserWindow registration'); await delay(500); const defer = page.getByRole('button', { name: '暂不替换', exact: true }); if (await defer.isVisible()) await defer.click(); }
  tree = processTree(host.pid); check((await api('bootstrap')).desktop === desktop, `${mode}: actual bootstrap mode`);
  await page.getByRole('button', { name: '性能', exact: true }).click();
  for (const disk of snapshot.physicalDisks) {
    await tile(disk).focus(); await tile(disk).press('Enter');
    check(await tile(disk).getAttribute('aria-pressed') === 'true', `disk ${disk.id}: keyboard selection targets the real resource`);
    check(disk.model === null ? await fact('型号').locator('.missing-value').getAttribute('aria-label') === `不可用：${disk.unavailable.model.message}` : (await page.locator('.disk-heading').innerText()).includes(disk.model), `disk ${disk.id}: model heading or accessible unavailable reason matches the actual field`);
  }
  const readable = snapshot.physicalDisks.filter(disk => disk.model && disk.mediaType && disk.busType);
  const selected = [...readable].sort((a,b) => b.model.length - a.model.length)[0];
  if (!selected) notExecuted.push('complete model/media/bus UI success sample: no actual readable disk on this host');
  for (const query of [...new Set([selected?.model, ...snapshot.physicalDisks.map(disk => disk.mediaType), ...snapshot.physicalDisks.map(disk => disk.busType)].filter(Boolean))]) {
    await page.getByRole('searchbox').fill(query); await delay(250);
    const expected = snapshot.physicalDisks.filter(disk => [disk.model,disk.mediaType,disk.busType].some(value => value?.toLowerCase().includes(query.toLowerCase())));
    check(expected.length > 0 && await page.locator('.resource-list .resource-tile').count() === expected.length, `search ${query}: only actual matching disk identities remain`);
    for (const disk of expected) check(await tile(disk).count() === 1, `search ${query}: disk ${disk.id} remains reachable`);
  }
  for (const [width,height] of selected ? [[1380,860],[960,640]] : []) {
    if (desktop) { probe(tree.map(entry => `${entry.pid}:${entry.fileTime}`).join(','), 'resize', width, height); await delay(350); }
    else await page.setViewportSize({ width,height });
    await verifyLayout(String(width), selected);
  }
  if (desktop && selected) {
    probe(tree.map(entry => `${entry.pid}:${entry.fileTime}`).join(','), 'resize', 1380, 860); await delay(350);
    await page.keyboard.press('Control+='); await until(async () => (await api('desktop/window-state')).zoomFactor === 1.25, 'actual Electron zoom readback is 125%');
    await verifyLayout('125', selected); await page.keyboard.press('Control+0');
  }
  const beforePause = await api('snapshot'); await api('settings', { intervalMs: 1000, paused: true }); await delay(500); const paused = await api('snapshot'); await delay(1500); const stillPaused = await api('snapshot');
  check(paused.sampleId === stillPaused.sampleId && JSON.stringify(paused.physicalDisks) === JSON.stringify(stillPaused.physicalDisks), 'pause preserves the actual last disk snapshot and identity');
  await api('settings', { intervalMs: 1000, paused: false }); await until(async () => (await api('snapshot')).sampleId > beforePause.sampleId, 'disk sampling resumes');
  check(writes.length === 0 && errors.length === 0, 'disk browsing causes no IFEO/service mutations or UI runtime errors');
} catch (error) { failure = { message: error.message, stack: error.stack }; }
finally {
  const identities = processTree(host.pid);
  if (desktop && page && !page.isClosed()) { await api('window/close', {}).catch(() => {}); await until(() => host.exitCode !== null, 'owned Electron host closes', 20000).catch(() => {}); }
  await browser?.close().catch(() => {}); if (host.exitCode === null) await stopOwnedHost(host);
  await until(() => host.exitCode !== null || host.signalCode !== null, 'owned Host exits').catch(() => {});
  cleanup = !await portOpen(port) && !await portOpen(cdp) && identities.every(entry => !ownedAlive(entry));
  if (!cleanup && !failure) failure = { message: 'test-owned identities or listeners remain' };
  await rm(temporary, { recursive: true, force: true, maxRetries: 5, retryDelay: 100 });
  const report = { result: failure ? 'FAIL' : 'PASS', mode, checkedAtUtc: new Date().toISOString(), checkoutAtRun: spawnSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8', windowsHide: true }).stdout.trim(), assertions: checks.length, checks, artifacts, disks: snapshot?.physicalDisks, independent, geometry, screenshots, errors, writes, cleanup, failure, source: 'production Release UI → HTTP → JSON.NET → CsWin32 → actual Windows devices; no business mocks', notExecuted };
  await writeFile(path.join(output, `${mode}-results.json`), JSON.stringify(report, null, 2) + '\n'); console.log(JSON.stringify(report, null, 2)); if (failure) process.exitCode = 1;
}
