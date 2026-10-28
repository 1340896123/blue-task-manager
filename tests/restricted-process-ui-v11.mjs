import { stopOwnedHost } from './etw-support.mjs';
import { clickCommand, revealControl, openProcessFilter, resourceTiles, resourceTile } from './layout-ui-support.mjs';
import { spawn, spawnSync } from 'node:child_process';
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';
import { assertions, check, delay, ownedAlive, portOpen, processTree, until } from './e2e-support.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const port = 5204, cdp = 9230, origin = `http://127.0.0.1:${port}`;
if (await portOpen(port) || await portOpen(cdp)) throw new Error('Restricted-process test ports are busy');
const temporary = await mkdtemp(path.join(tmpdir(), 'blue-restricted-ui-'));
const output = path.join(root, 'output/playwright');
await mkdir(output, { recursive: true });
const host = spawn(path.join(root, 'output/Takao-win-x64/Takao.Host.exe'), [`--port=${port}`], {
  cwd: tmpdir(), env: { ...process.env, TASKMANAGER_DATA_DIR: temporary, TASKMANAGER_CDP_PORT: String(cdp) }, windowsHide: true, stdio: 'ignore'
});
let browser, page, runtime = [];
try {
  await until(async () => (await (await fetch(origin + '/api/health')).json()).ready && await portOpen(cdp), 'real restricted-process desktop ready', 45000);
  browser = await chromium.connectOverCDP(`http://127.0.0.1:${cdp}`);
  await until(() => browser.contexts()[0]?.pages().length, 'actual Electron page exists');
  page = browser.contexts()[0].pages()[0];
  await page.getByRole('table', { name: '进程列表', exact: true }).waitFor();
  runtime = processTree(host.pid);
  const snapshot = await (await fetch(origin + '/api/snapshot')).json();
  const oracle = spawnSync('powershell', ['-NoProfile', '-Command', "[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false); @(Get-CimInstance Win32_Process -Filter \"Name = 'SGuard64.exe' OR Name = 'SGuardSvc64.exe'\" | Select-Object Name,ProcessId) | ConvertTo-Json -Compress"], { encoding: 'utf8', windowsHide: true });
  if (oracle.status !== 0) throw new Error('Independent process query failed');
  const decoded = JSON.parse(oracle.stdout || '[]'); const guards = Array.isArray(decoded) ? decoded : [decoded];
  check(guards.every(p => snapshot.processes.some(row => row.pid === p.ProcessId && row.name.toLowerCase() === p.Name.toLowerCase())), 'all independently observed real SGuard processes remain in the native snapshot');
  const target = snapshot.processes.find(p => p.name.toLowerCase() === 'sguard64.exe') ?? snapshot.processes.find(p => p.state === 'restricted');
  if (!target) throw new Error('No real restricted process is available for this check');
  const search = page.getByRole('searchbox'); const row = page.locator(`tr[data-row-key="${target.identity}"]`);
  await search.fill(target.name); await row.waitFor();
  check((await row.innerText()).includes(String(target.pid)), 'restricted process is visible by its real executable name');
  await search.fill(String(target.pid)); await row.waitFor();
  check((await row.innerText()).includes(target.name), 'restricted process is visible by its actual PID');
  await clickCommand(page, '进程树'); await row.waitFor();
  check(await row.isVisible(), 'unknown creation identity never removes a row from the real process tree');
  if (target.state === 'restricted') {
    check(await row.getByText('访问受限', { exact: true }).isVisible(), 'the row explains access restriction while preserving process existence');
    check(target.creationTimeFileTime !== null || target.unavailable.creationTimeFileTime?.nativeErrorCode != null, 'unreadable creation identity keeps the actual native error rather than fabricating a value');
  }
  await openProcessFilter(page);
  check(await page.getByRole('option', { name: '基本查询可用', exact: true }).count() === 1, 'information availability filter does not mislabel restricted running processes as stopped');
  await page.getByLabel('信息读取', { exact: true }).selectOption('restricted');
  if (target.state === 'restricted') await row.waitFor();
  await page.getByRole('button', { name: '清除筛选', exact: true }).click();
  await openProcessFilter(page);
  await search.fill('SGuard64.exe');
  if (guards.some(p => p.Name.toLowerCase() === 'sguard64.exe')) await page.locator('tbody tr[data-row-key]').first().waitFor();
  await page.screenshot({ path: path.join(output, 'guard-process-visible.png') });
  await search.fill('ACE-Guard Client');
  check(await page.getByText('请使用可执行文件名或PID搜索，或点击“重置视图”清除筛选；文件说明名称暂不参与搜索。', { exact: true }).isVisible(), 'unavailable file-description search explains how to find the executable');
  await page.getByRole('button', { name: '关闭窗口', exact: true }).click();
  await until(() => host.exitCode !== null, 'restricted-process desktop closes', 20000);
  await until(async () => !await portOpen(port) && !await portOpen(cdp), 'restricted-process ports released');
  check(runtime.every(p => !ownedAlive(p)), 'all exact test application identities exit without modifying the observed protected processes');
  const report = { result: 'PASS', checkedAtUtc: new Date().toISOString(), assertions: assertions.length, checks: assertions,
    observed: guards.map(p => ({ name: p.Name, pid: p.ProcessId })), scope: 'read-only observation of real Windows processes; no termination, priority, efficiency, or protected-process write requests', screenshot: 'guard-process-visible.png' };
  await writeFile(path.join(output, 'restricted-process-ui-v11-results.json'), JSON.stringify(report, null, 2));
  console.log(JSON.stringify(report, null, 2));
} finally {
  if (host.exitCode === null) { const remaining = processTree(host.pid); await stopOwnedHost(host); for (const p of remaining.reverse()) if (ownedAlive(p)) { try { process.kill(p.pid); } catch {} } }
  await browser?.close().catch(() => {}); await delay(300); await rm(temporary, { recursive: true, force: true });
}
