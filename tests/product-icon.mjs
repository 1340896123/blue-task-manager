import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';
import { assertions, check, delay, until, alive, processTree, ownedAlive, portOpen } from './e2e-support.mjs';
import { nativeUi, probe, windows } from './native-ui-support.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const directory = path.join(root, 'output/TaskManager-win-x64');
const output = path.join(root, 'output/product-icon');
const temporary = await mkdtemp(path.join(tmpdir(), 'taskmanager-product-icon-'));
const port = 5198, cdp = 9230, origin = `http://127.0.0.1:${port}`;
await mkdir(output, { recursive: true });
if (await portOpen(port) || await portOpen(cdp)) throw new Error('Icon acceptance ports are occupied.');
const executable = path.join(directory, 'TaskManager.Host.exe');
const host = spawn(executable, [`--port=${port}`], { cwd: tmpdir(), env: { ...process.env, TASKMANAGER_DATA_DIR: path.join(temporary, 'preferences'), TASKMANAGER_CDP_PORT: String(cdp) }, windowsHide: true, stdio: 'ignore' });
let browser, tree = [], page;
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
try {
  const ico = await readFile(path.join(root, 'assets/branding/task-manager.ico'));
  const count = ico.readUInt16LE(4);
  const sizes = Array.from({ length: count }, (_, index) => ico[6 + index * 16] || 256);
  check(ico.readUInt16LE(0) === 0 && ico.readUInt16LE(2) === 1 && sizes.join(',') === '16,24,32,48,64,128,256', 'ICO contains all seven standard Windows image sizes');
  for (const relative of ['.electron/task-manager.ico', 'wwwroot/favicon.ico']) {
    check(hash(await readFile(path.join(directory, relative))) === hash(ico), `portable ${relative} matches canonical generated ICO`);
  }
  const exeIcons = probe('icon-exe', executable, output);
  for (const [size, icon] of Object.entries(exeIcons)) {
    check(icon.bluePixels > 10 && icon.whitePixels > 0 && icon.transparentCorner, `Host EXE embedded ${size} icon independently extracted with blue/white/transparent product pixels`);
  }
  await until(async () => (await (await fetch(origin + '/api/health')).json()).ready && await portOpen(cdp), 'real Electron product-icon host ready', 45000);
  browser = await chromium.connectOverCDP(`http://127.0.0.1:${cdp}`);
  await until(() => browser.contexts()[0]?.pages().length > 0, 'product icon BrowserWindow exists');
  page = browser.contexts()[0].pages()[0];
  await page.getByText('已连接 · 实时采样', { exact: true }).waitFor();
  check(await page.locator('img.brand-mark').evaluate(async image => { await image.decode(); return image.complete && image.naturalWidth === 512 && image.src.endsWith('/icon.svg'); }), 'React brand loads and decodes the canonical SVG');
  for (const [route, kind] of [['icon.svg', 'svg'], ['favicon.ico', 'icon'], ['icon-256.png', 'png'], ['icon-512.png', 'png']]) {
    const response = await fetch(`${origin}/${route}`);
    check(response.status === 200 && response.headers.get('content-type')?.includes(kind), `${route} serves HTTP 200 with image content type`);
  }
  check(await page.locator('link[rel="icon"]').evaluateAll(links => links.some(link => link.getAttribute('href') === '/favicon.ico') && links.some(link => link.getAttribute('href') === '/icon.svg')), 'document declares standard ICO fallback and SVG favicon');
  check(await page.evaluate(() => typeof require === 'undefined' && typeof process === 'undefined'), 'branding preserves isolated renderer without Node globals');
  tree = processTree(host.pid);
  const identities = new Set(tree.map(entry => entry.pid));
  const target = windows('蓝域').find(window => identities.has(window.pid));
  check(Boolean(target), 'independent Win32 query identifies the test-owned native product window');
  const nativeIcons = probe('icon-window', target.hwnd, target.pid, target.fileTime, output);
  for (const [size, icon] of Object.entries(nativeIcons)) {
    check(icon.bluePixels > 10 && icon.whitePixels > 0 && icon.transparentCorner, `WM_GETICON ${size} native window/taskbar icon is the blue/white product image`);
  }
  await page.screenshot({ path: path.join(output, 'product-desktop.png') });
  nativeUi(target, 'capture', path.join(output, 'product-native-window.png'));
  await page.getByRole('button', { name: '关闭窗口', exact: true }).click();
  await until(async () => !alive(host.pid) && !await portOpen(port) && !await portOpen(cdp), 'icon test closes real desktop and ports', 20000);
  check(tree.every(entry => !ownedAlive(entry)), 'closing branded desktop leaves no test-owned host or Electron descendants');
  await writeFile(path.join(output, 'results.json'), JSON.stringify({ result: 'PASS', checkedAtUtc: new Date().toISOString(), assertions: assertions.length, checks: assertions, exeIcons, nativeIcons, evidence: ['product-desktop.png', 'product-native-window.png', 'product-exe-large.png', 'product-exe-small.png', 'product-window-large.png', 'product-window-small.png'] }, null, 2));
  console.log(JSON.stringify({ result: 'PASS', assertions: assertions.length, checks: assertions }, null, 2));
} catch (error) {
  await page?.screenshot({ path: path.join(output, 'failure.png') }).catch(() => {});
  throw error;
} finally {
  await browser?.close().catch(() => {});
  if (alive(host.pid)) { if (!tree.length) tree = processTree(host.pid); host.kill(); }
  // Process identities are only descendants of this run's exact host; no name-based selection.
  for (const entry of tree.reverse()) if (ownedAlive(entry)) { try { process.kill(entry.pid); } catch {} }
  await delay(350);
  await rm(temporary, { recursive: true, force: true });
}
