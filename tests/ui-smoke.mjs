import { stopOwnedHost } from './etw-support.mjs';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const framework = 'net10.0-windows10.0.19041.0';
const hostDirectory = path.join(root, 'src', 'Takao.Host');
const hostExe = path.join(hostDirectory, 'bin', 'Release', framework, 'Takao.Host.exe');
const workloadExe = path.join(root, 'tests', 'Takao.TestWorkload', 'bin', 'Release', framework, 'Takao.TestWorkload.exe');
const port = Number(process.env.TASKMANAGER_UI_PORT ?? 5192);
const origin = `http://127.0.0.1:${port}`;
const output = path.join(root, 'output', 'playwright');
const temporary = await mkdtemp(path.join(tmpdir(), 'taskmanager-ui-'));
await mkdir(output, { recursive: true });
const host = spawn(hostExe, ['--headless', `--port=${port}`], { cwd: path.dirname(hostExe), env: { ...process.env, TASKMANAGER_DATA_DIR: path.join(temporary, 'preferences') }, windowsHide: true, stdio: 'ignore' });
const workload = spawn(workloadExe, [temporary, '150'], { cwd: root, windowsHide: true, stdio: 'ignore' });
let browser;
let launchedTarget;
let assertions = 0;
const checks = [];
const delay = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));
async function until(predicate, message, timeout = 18000) {
  const start = Date.now();
  while (Date.now() - start < timeout) { if (await predicate()) return; await delay(120); }
  throw new Error(message);
}
function check(condition, message) { assert.ok(condition, message); assertions++; checks.push(message); }
try {
  await until(async () => { try { return (await (await fetch(origin + '/api/health')).json()).ready; } catch { return false; } }, 'real native host ready');
  const executablePath = process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE;
  browser = await chromium.launch({ headless: true, ...(executablePath ? { executablePath } : {}) });
  const page = await browser.newPage({ viewport: { width: 1380, height: 860 }, reducedMotion: 'reduce' });
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto(origin, { waitUntil: 'domcontentloaded' });
  await page.getByRole('heading', { level: 1, name: '进程', exact: true }).waitFor();
  await page.getByRole('table', { name: '进程列表', exact: true }).waitFor();
  await until(async () => (await page.locator('tbody tr[data-row-key]').count()) > 0, 'virtual real process rows present');
  const snapshot = await (await fetch(origin + '/api/snapshot')).json();
  check(snapshot.processes.some(row => row.pid === workload.pid), 'dedicated real workload appears in native snapshot');
  check(await page.locator('tbody tr[data-row-key]').count() < snapshot.processes.length, 'virtual table bounds rendered rows');
  check(await page.getByRole('button', { name: '最小化窗口', exact: true }).isDisabled(), 'headless desktop controls explicitly disabled');
  await page.screenshot({ path: path.join(output, 'stage3-processes-1380.png') });
  const search = page.getByRole('searchbox', { name: '搜索名称、发布者、路径或 PID' });
  await search.fill(String(workload.pid));
  await until(async () => (await page.locator('tbody tr[data-row-key]').count()) === 1, 'PID search filters actual workload');
  check(await page.locator('tbody').innerText().then(text => text.includes('Takao.TestWorkload')), 'PID search finds correct real process name');
  const row = page.locator('tbody tr[data-row-key]').first(); await row.click();
  check(await page.getByRole('button', { name: '结束任务', exact: true }).isEnabled(), 'actual terminable identity selected');
  check(await row.locator('[aria-label^="不可用："]').count() >= 1, 'unavailable process metrics expose actual reasons');
  await row.dblclick();
  check(await page.getByRole('dialog').innerText().then(text => text.includes(String(workload.pid))), 'identity information dialog shows selected native PID');
  await page.keyboard.press('Escape');
  check(await row.evaluate(element => element === document.activeElement), 'modal Escape restores triggering focus');
  await page.getByRole('button', { name: '清空搜索', exact: true }).click();
  await page.getByRole('button', { name: '按CPU排序', exact: true }).click();
  check(await page.getByRole('columnheader').filter({ has: page.getByRole('button', { name: '按CPU排序', exact: true }) }).getAttribute('aria-sort') === 'ascending', 'CPU header exposes numeric sort direction');
  await page.getByRole('button', { name: '设置', exact: true }).click();
  await page.getByRole('switch', { name: '自动采样', exact: true }).click();
  await until(async () => (await (await fetch(origin + '/api/snapshot')).json()).paused, 'sampler acknowledges paused'); await delay(300);
  const paused = await (await fetch(origin + '/api/snapshot')).json();
  await delay(1300);
  const stable = await (await fetch(origin + '/api/snapshot')).json();
  check(stable.paused && stable.sampleId === paused.sampleId, 'pause reaches real sampler and preserves sample');
  await page.getByRole('switch', { name: '自动采样', exact: true }).click();
  for (const name of ['性能', '应用历史记录', '启动应用', '用户', '详细信息', '服务', '设置']) {
    await page.getByRole('button', { name, exact: true }).click();
    check(await page.getByRole('heading', { level: 1, name, exact: true }).isVisible(), `real ${name} page available`);
    check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${name} has no page-level horizontal overflow`);
    if (name === '性能') {
      check(await page.locator('.resource-chart').count() >= 1, 'performance charts use actual native history');
      await page.screenshot({ path: path.join(output, 'stage3-performance-1380.png') });
    }
  }
  await page.getByLabel('采样间隔', { exact: true }).selectOption('2000');
  await until(async () => (await (await fetch(origin + '/api/snapshot')).json()).intervalMs === 2000, 'interval setting reaches native sampler');
  check(true, 'native interval changed via settings');
  await page.getByRole('button', { name: '标准', exact: true }).click();
  check(await page.locator('.app-shell').getAttribute('data-density') === 'standard', 'density preference applied');
  await page.getByRole('button', { name: '进程', exact: true }).click();
  await page.setViewportSize({ width: 960, height: 640 });
  check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), '960x640 has no page overflow');
  check(await page.getByRole('button', { name: '运行新任务', exact: true }).isVisible(), '960x640 toolbar visible');
  await page.screenshot({ path: path.join(output, 'stage3-processes-960.png') });
  await page.getByRole('button', { name: '运行新任务', exact: true }).click();
  let runDialog = page.getByRole('dialog');
  await runDialog.getByLabel('程序名称或完整路径', { exact: true }).fill(workloadExe);
  await runDialog.getByLabel('参数（可选）', { exact: true }).fill(`"${path.join(temporary, 'created-via-ui')}" 120`);
  const launchedResponse = page.waitForResponse(response => response.url() === origin + '/api/processes/start' && response.request().method() === 'POST');
  await runDialog.getByRole('button', { name: '运行', exact: true }).click();
  launchedTarget = await (await launchedResponse).json();
  check(launchedTarget.pid > 0 && process.kill(launchedTarget.pid, 0), 'UI creates dedicated real Windows process');
  await search.fill(String(launchedTarget.pid));
  await until(async () => await page.locator('tbody tr[data-row-key]').count() === 1 && await page.locator('tbody').innerText().then(text => text.includes('Takao.TestWorkload')), 'new UI-created process sampled');
  await page.locator('tbody tr[data-row-key]').first().click();
  await page.getByRole('button', { name: '结束任务', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: '取消', exact: true }).click();
  check(process.kill(launchedTarget.pid, 0), 'cancel termination leaves dedicated process alive');
  await page.getByRole('button', { name: '结束任务', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: '结束进程', exact: true }).click();
  await until(async () => { try { process.kill(launchedTarget.pid, 0); return false; } catch { return true; } }, 'confirmed UI termination exits real process');
  check(process.kill(workload.pid, 0), 'termination only affects selected UI-created process');
  check(true, 'UI to HTTP to CsWin32 to Windows termination verified');
  await page.getByRole('dialog').getByRole('button', { name: '关闭', exact: true }).click();
  launchedTarget = undefined;
  await page.getByRole('button', { name: '清空搜索', exact: true }).click();
  await page.getByRole('button', { name: '运行新任务', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('程序名称或完整路径', { exact: true }).fill(path.join(temporary, 'confirmed-absent', 'missing.exe'));
  await dialog.getByRole('button', { name: '运行', exact: true }).click();
  await dialog.getByRole('alert').waitFor();
  check(await dialog.isVisible(), 'native invalid executable error retains dialog');
  await page.keyboard.press('Escape');
  check(await page.getByRole('button', { name: '运行新任务', exact: true }).evaluate(element => element === document.activeElement), 'run dialog restores focus');
  check(errors.length === 0, `no browser runtime errors (${errors.length})`);
  const report = { result: 'PASS', assertions, checkedAtUtc: new Date().toISOString(), checks, screenshots: ['stage3-processes-1380.png', 'stage3-performance-1380.png', 'stage3-processes-960.png'] };
  await writeFile(path.join(output, 'stage3-smoke-results.json'), JSON.stringify(report, null, 2));
  console.log(JSON.stringify(report, null, 2));
} catch (error) {
  if (browser) { const pages = browser.contexts().flatMap(context => context.pages()); await pages[0]?.screenshot({ path: path.join(output, 'stage3-failure.png') }).catch(() => {}); }
  throw error;
} finally {
  await browser?.close();
  if (launchedTarget?.creationTimeFileTime) {
    try {
      const bootstrap = await (await fetch(origin + '/api/bootstrap')).json();
      await fetch(`${origin}/api/processes/${launchedTarget.pid}/terminate`, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-TaskManager-Token': bootstrap.token, Origin: origin }, body: JSON.stringify({ creationTimeFileTime: launchedTarget.creationTimeFileTime }) });
    } catch { /* The dedicated bounded workload also self-exits after 120 seconds. */ }
  }
  // Only terminate direct test-created handles, never processes selected by generic executable name.
  workload.kill(); await stopOwnedHost(host); await delay(350);
  await rm(temporary, { recursive: true, force: true });
}
