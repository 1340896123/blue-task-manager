import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdir, mkdtemp, readFile, rename, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';
import { delay, portOpen, until } from './e2e-support.mjs';
import { nativeUi, probe, probeExe, windows } from './native-ui-support.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const directory = path.resolve(process.env.TASKMANAGER_RELEASE_DIRECTORY ?? path.join(root, 'output/TaskManager-win-x64'));
const prefix = process.env.TASKMANAGER_IFEO_EVIDENCE_PREFIX ?? 'portable';
assert.match(prefix, /^[a-z0-9-]+$/);
const output = path.join(root, 'output/playwright/ifeo-elevation', prefix);
const session = await mkdtemp(path.join(tmpdir(), 'taskmanager-ifeo-'));
const workerScript = path.join(root, 'tests/ifeo-elevation-worker.ps1');
const hostExe = path.join(directory, 'TaskManager.Host.exe'), launcherExe = path.join(directory, 'TaskManager.Launcher.exe');
const legacyCommand = `"${hostExe}" --task-manager-entry`, launcherCommand = `"${launcherExe}" --task-manager-entry`;
const systemTarget = path.join(process.env.SystemRoot, 'System32/taskmgr.exe');
const wow64Target = path.join(process.env.SystemRoot, 'SysWOW64/taskmgr.exe');
const settingsPort = 5261, settingsCdp = 9261, entryPort = 5188, forwardPort = 5263, forwardCdp = 9263;
const checks = [], launches = [], errors = [], evidence = [], tracked = [];
const check = (condition, message) => { assert.ok(condition, message); checks.push(message); console.log('PASS ' + message); };
const psQuote = value => "'" + String(value).replaceAll("'", "''") + "'";
const windowsQuote = value => '"' + String(value).replace(/(\\*)"/g, '$1$1\\"').replace(/(\\+)$/g, '$1$1') + '"';
const powershell = (args, environment) => {
  const result = spawnSync('powershell.exe', ['-NoProfile', ...args], { windowsHide: true, encoding: 'utf8', timeout: 30000, env: { ...process.env, ...environment } });
  assert.equal(result.status, 0, result.stderr || result.error?.message); return result.stdout.trim().replace(/^\uFEFF/, '');
};
const independentRegistry = () => powershell(['-ExecutionPolicy', 'Bypass', '-File', workerScript, '-ReadOnly']);
const hash = text => createHash('sha256').update(text).digest('hex');
let requestNumber = 0, workerStarted = false, browser, page, token, origin, hostIdentity, initial, finalState, resultError, preferencesRestored;
async function worker(operation, data = {}) {
  const id = String(++requestNumber).padStart(4, '0');
  const request = path.join(session, `request-${id}.json`), response = path.join(session, `response-${id}.json`);
  await writeFile(request + '.tmp', JSON.stringify({ operation, expectedState: independentRegistry(), ...data }));
  await rename(request + '.tmp', request);
  let value;
  await until(async () => { value = JSON.parse(await readFile(response, 'utf8')); return true; }, `elevated worker ${operation} response`, 45000);
  assert.ok(value.ok, value.message); return value;
}
async function fixture(debuggerValue, kind = 'String', extra = {}) {
  return (await worker('fixture', { debugger: debuggerValue, kind, ...extra })).state;
}
function mediumLaunch(executable, args = [], environment = {}, bare = false) {
  const command = [bare ? executable : windowsQuote(executable), ...args.map(windowsQuote)].join(' ');
  const value = spawnSync(probeExe, ['process-launch', bare ? '-' : executable, command], {
    encoding: 'utf8', windowsHide: true, timeout: 22000,
    env: { ...process.env, TASKMANAGER_PORT: String(forwardPort), TASKMANAGER_CDP_PORT: String(forwardCdp), TASKMANAGER_DATA_DIR: path.join(session, 'invalid-preferences'), ...environment }
  });
  assert.equal(value.status, 0, value.stderr || value.error?.message);
  const launched = { target: executable, arguments: args, ...JSON.parse(value.stdout) }; launches.push(launched); return launched;
}
async function api(route, body, expected = 200, headers = {}) {
  const response = await fetch(`${origin}/api/${route}`, {
    method: body === undefined ? 'GET' : 'POST',
    headers: { Origin: origin, ...(body === undefined ? {} : { 'Content-Type': 'application/json', 'X-TaskManager-Token': token }), ...headers },
    body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(10000)
  });
  const text = await response.text();
  const value = text ? JSON.parse(text) : null;
  assert.equal(response.status, expected, `${route}: ${text}`); return value;
}
async function attach(port, cdp) {
  origin = `http://127.0.0.1:${port}`;
  await until(async () => (await api('health')).ready && await portOpen(cdp), 'production Host/real Electron ready', 60000);
  let bootstrap;
  await until(async () => { bootstrap = await api('bootstrap'); return bootstrap.desktop; }, 'actual desktop bootstrap', 30000);
  token = bootstrap.token;
  browser = await chromium.connectOverCDP(`http://127.0.0.1:${cdp}`);
  await until(() => browser.contexts()[0]?.pages().length, 'actual Electron page');
  page = browser.contexts()[0].pages()[0]; page.on('pageerror', error => errors.push(error.message));
  await page.getByText('已连接 · 实时采样', { exact: true }).waitFor({ timeout: 30000 });
  return bootstrap;
}
async function screenshot(name) { await page.screenshot({ path: path.join(output, name) }); evidence.push(name); }
async function closeDesktop(port, cdp) {
  const tree = (await worker('tree')).data; tracked.push(...tree);
  await page.getByRole('button', { name: '关闭窗口', exact: true }).click();
  await until(async () => !await portOpen(port) && !await portOpen(cdp), 'test-owned desktop listeners close', 25000);
  await browser.close().catch(() => {}); browser = undefined; page = undefined; token = undefined;
}
await mkdir(output, { recursive: true });
try {
  check(probe('process-elevation', process.pid).elevated === false, 'controller is a real non-elevated Medium process');
  for (const port of [settingsPort, settingsCdp, entryPort, forwardPort, forwardCdp]) assert.ok(!await portOpen(port), `Acceptance port occupied: ${port}; existing app must remain untouched`);
  initial = independentRegistry();
  const initialObject = JSON.parse(initial);
  check(initialObject.values.length === 0 && initialObject.subkeys.length === 0, 'initial HKLM64 key is independently saved and contains no third-party configuration');
  await writeFile(path.join(output, 'registry-before.json'), initial + '\n');
  for (const [name, executable, level] of [['host', hostExe, 'requireAdministrator'], ['launcher', launcherExe, 'asInvoker']]) {
    const manifest = path.join(output, `${name}.manifest.xml`);
    const mt = spawnSync('C:/Program Files (x86)/Windows Kits/10/bin/10.0.26100.0/x64/mt.exe', [`-inputresource:${executable};#1`, `-out:${manifest}`], { encoding: 'utf8', windowsHide: true });
    assert.equal(mt.status, 0, mt.stderr);
    const text = await readFile(manifest, 'utf8');
    check(text.includes(`level="${level}"`) && text.includes('uiAccess="false"'), `independent mt.exe verifies ${name} ${level}/uiAccess=false`);
  }
  const direct = mediumLaunch(hostExe, ['--headless']);
  check(!direct.success && direct.nativeErrorCode === 740, 'Medium CreateProcess of requireAdministrator Host returns native Windows 740');
  for (const args of [[], ['--headless'], ['--task-manager-entry'], ['--uninstall-task-manager-integration']]) {
    const denied = spawnSync('dotnet.exe', [path.join(directory, 'TaskManager.Host.dll'), ...args], { encoding: 'utf8', windowsHide: true, timeout: 15000, env: { ...process.env, TASKMANAGER_PORT: String(forwardPort), TASKMANAGER_CDP_PORT: String(forwardCdp), TASKMANAGER_DATA_DIR: path.join(session, 'denied-preferences') } });
    check(denied.status !== 0 && /administratorRequired|740/.test(denied.stderr), `Medium DLL startup rejects before business setup (${args.join(' ') || 'normal'})`);
  }
  const invalidArguments = [[], ['--headless'], ['--task-manager-entry', 'C:\\untrusted\\taskmgr.exe'], ['--task-manager-entry', systemTarget, '--port=5263'], ['--task-manager-entry', systemTarget, '--headless'], ['--task-manager-entry', '--task-manager-entry'], ['--task-manager-entry', systemTarget, '--uninstall-task-manager-integration'], ['--task-manager-entry', systemTarget, '/contains space'], ['--task-manager-entry', systemTarget, ...Array(33).fill('/7')]];
  for (const args of invalidArguments) {
    const invalid = mediumLaunch(launcherExe, args);
    check(invalid.success && invalid.exited && invalid.exitCode === 87, `Launcher rejects untrusted entry with native exit 87: ${JSON.stringify(args)}`);
  }
  check(!await portOpen(forwardPort) && !await portOpen(forwardCdp), 'Medium denial and invalid Launcher inputs create no HTTP/CDP listeners');
  const workerArguments = ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', windowsQuote(workerScript), '-SessionDirectory', windowsQuote(session), '-ReleaseDirectory', windowsQuote(directory), '-ProbePath', windowsQuote(probeExe), '-ControllerPid', String(process.pid)];
  powershell(['-Command', `Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList @(${workerArguments.map(psQuote).join(',')}) | Out-Null`]);
  workerStarted = true;
  let ready;
  await until(async () => { ready = JSON.parse(await readFile(path.join(session, 'worker-ready.json'), 'utf8')); return ready.elevated; }, 'standard RunAs worker actually elevated', 45000);
  check(ready.initial === initial && probe('process-elevation', ready.pid).elevated, 'RunAs worker confirms actual TokenIsElevated and identical complete initial key');
  await fixture(legacyCommand);
  const old = mediumLaunch(systemTarget, ['/7']);
  check(!old.success && old.nativeErrorCode === 740, 'real Medium System32 taskmgr.exe reproduces native 740 through exact legacy IFEO Host command');
  hostIdentity = (await worker('start-host', { port: settingsPort, cdp: settingsCdp })).data;
  const settingsBootstrap = await attach(settingsPort, settingsCdp);
  check(settingsBootstrap.hostPid === hostIdentity.pid, 'repair UI belongs to the dedicated elevated test Host');
  check(await page.evaluate(() => typeof require === 'undefined' && typeof process === 'undefined'), 'production elevated Electron renderer has sandboxed no-Node globals over real CDP');
  let state = await api('desktop/task-manager-integration');
  check(state.status === 'migrationRequired' && state.ownsDebugger && state.canRepair && state.registrationCommand === launcherCommand, 'production CsWin32 read identifies exact legacy REG_SZ and exposes explicit repair');
  const legacyState = independentRegistry();
  await api('desktop/task-manager-integration', { enabled: true, expectedRevision: state.revision }, 409);
  await api('desktop/task-manager-integration', { enabled: true, repair: true, expectedRevision: '0'.repeat(64) }, 409);
  await api('desktop/task-manager-integration', { enabled: true, repair: true, expectedRevision: state.revision }, 403, { 'X-TaskManager-Token': '' });
  await api('desktop/task-manager-integration', { enabled: true, repair: true, expectedRevision: state.revision }, 403, { Origin: 'https://example.invalid' });
  check(independentRegistry() === legacyState, 'implicit migration, stale revision, missing token and foreign Origin all refuse with the full key unchanged');
  await page.getByRole('button', { name: '设置', exact: true }).click();
  const card = page.getByRole('heading', { name: '任务栏右键中的任务管理器', exact: true }).locator('..');
  await card.getByRole('button', { name: '修复接管…', exact: true }).waitFor();
  await card.scrollIntoViewIfNeeded(); await screenshot('legacy-repair-settings.png');
  await card.getByRole('button', { name: '修复接管…', exact: true }).click();
  const dialog = page.getByRole('dialog'); await dialog.waitFor();
  check((await dialog.innerText()).includes('只有确认后才修改'), 'real repair confirmation explains explicit migration before writing');
  await screenshot('legacy-repair-confirmation.png');
  await dialog.getByRole('button', { name: '暂不更改', exact: true }).click();
  check(independentRegistry() === legacyState, 'canceling repair confirmation leaves the old full registry state unchanged');
  await card.getByRole('button', { name: '修复接管…', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: '确认修复接管', exact: true }).click();
  await until(async () => (await api('desktop/task-manager-integration')).status === 'enabled', 'explicit production UI repairs legacy debugger');
  state = await api('desktop/task-manager-integration');
  check(JSON.parse(independentRegistry()).values.find(row => row.name === 'Debugger').data === launcherCommand && !state.migrationRequired, 'independent HKLM64 read confirms UI migrated to the exact fixed Launcher command');
  await card.scrollIntoViewIfNeeded(); await screenshot('repaired-settings.png');
  await api('desktop/task-manager-integration', { enabled: false, expectedRevision: state.revision });
  check(JSON.parse(independentRegistry()).values.length === 0, 'production cancel deletes only the exact new owned Debugger');
  await fixture(legacyCommand);
  state = await api('desktop/task-manager-integration');
  await api('desktop/task-manager-integration', { enabled: false, expectedRevision: state.revision });
  check(JSON.parse(independentRegistry()).values.length === 0, 'production cancel also recognizes the exact legacy owned Debugger');
  const variants = [
    { name: 'other directory', value: `"C:\\Other\\TaskManager.Launcher.exe" --task-manager-entry` },
    { name: 'additional argument', value: launcherCommand + ' /7' },
    { name: 'different command casing', value: launcherCommand.replace('TaskManager.Launcher.exe', 'taskmanager.launcher.exe') },
    { name: 'REG_EXPAND_SZ', value: launcherCommand, kind: 'ExpandString' },
    { name: 'REG_DWORD', value: 1, kind: 'DWord' },
    { name: 'embedded null', value: launcherCommand + '\0unexpected' }
  ];
  for (const variant of variants) {
    const before = await fixture(variant.value, variant.kind ?? 'String');
    state = await api('desktop/task-manager-integration');
    check(state.status === 'conflict' && !state.ownsDebugger && !state.canRepair, `production ownership rejects ${variant.name}`);
    for (const body of [{ enabled: true }, { enabled: true, repair: true }, { enabled: false }]) await api('desktop/task-manager-integration', { ...body, expectedRevision: state.revision }, 409);
    const maintenance = (await worker('maintenance')).data;
    check(maintenance.exitCode === 0 && independentRegistry() === before, `API and fixed uninstall maintenance preserve the complete ${variant.name} configuration`);
  }
  for (const command of [legacyCommand, launcherCommand]) {
    const before = await fixture(command, 'String', { special: true, useFilter: true });
    state = await api('desktop/task-manager-integration');
    check(state.status === 'conflict' && state.ownsDebugger && !state.canRepair, 'special UseFilter/default/binary/multi-string/nested-key config blocks enable/repair but preserves strict ownership');
    await api('desktop/task-manager-integration', { enabled: true, repair: true, expectedRevision: state.revision }, 409);
    const maintenance = (await worker('maintenance')).data;
    const expected = JSON.parse(before); expected.values = expected.values.filter(row => row.name !== 'Debugger').map(row => row.name === 'UseFilter' ? { ...row, kind: 'DWord', data: '0' } : row);
    check(maintenance.exitCode === 0, 'fixed production maintenance exits successfully for exact owned old/new command');
    assert.deepEqual(JSON.parse(independentRegistry()), expected);
    check(true, 'maintenance removes owned Debugger, disables entry filtering and retains unrelated value/type/nested children');
  }
  await fixture(null);
  state = await api('desktop/task-manager-integration');
  await api('desktop/task-manager-integration', { enabled: true, expectedRevision: state.revision });
  check(JSON.parse(independentRegistry()).values[0].data === launcherCommand, 'production enable registers the new fixed Launcher entry from a clean key');
  await closeDesktop(settingsPort, settingsCdp);
  await worker('prepare-real-entry');
  const firstLaunch = mediumLaunch(systemTarget, ['/7']);
  check(firstLaunch.success && firstLaunch.exited && firstLaunch.exitCode === 0, 'real Medium System32 taskmgr.exe creates the asInvoker Launcher and it exits after Shell runas');
  hostIdentity = (await worker('adopt-entry', { launcherPid: firstLaunch.pid })).data;
  origin = `http://127.0.0.1:${entryPort}`;
  await until(async () => (await api('health')).ready, 'real default-port IFEO Host ready', 60000);
  let bootstrap;
  await until(async () => { bootstrap = await api('bootstrap'); return bootstrap.desktop; }, 'real IFEO Electron window ready', 45000);
  token = bootstrap.token;
  check(bootstrap.hostPid === hostIdentity.pid, 'real default-port API belongs to the exact test Launcher child; runas environment isolation is respected');
  const tree = (await worker('tree')).data; tracked.push(...tree);
  const browserProcess = tree.find(row => row.imagePath?.toLowerCase() === path.join(directory, '.electron/node_modules/electron/dist/electron.exe').toLowerCase() && BigInt(row.fileTime) > BigInt(hostIdentity.fileTime) && !row.commandLine?.includes('--type='));
  check((await worker('elevation', { pid: bootstrap.hostPid })).data.elevated && browserProcess && (await worker('elevation', { pid: browserProcess.pid })).data.elevated, 'real IFEO chain produces elevated Host and official Electron browser tokens');
  const renderers = tree.filter(row => BigInt(row.fileTime) > BigInt(hostIdentity.fileTime) && row.commandLine?.includes('--type=renderer'));
  check(renderers.length > 0 && renderers.every(row => row.commandLine.includes('--enable-sandbox') && !row.commandLine.includes('--no-sandbox')), 'real IFEO renderer command line explicitly enables sandbox; production repair UI also has no Node globals');
  const sample = await api('snapshot');
  check(sample.processes.some(row => row.pid === bootstrap.hostPid), 'production Windows collector contains the exact actual test Host');
  await until(async () => (await api('snapshot')).sampleId > sample.sampleId, 'real native sample progresses');
  check(true, 'native telemetry advances through the production HTTP/JSON.NET/UI chain');
  const ids = new Set(tree.map(row => row.pid));
  const window = windows('蓝域').find(row => ids.has(row.pid));
  check(window && nativeUi(window, 'state').visible, 'independent CsWin32 HWND query finds the visible test-owned product window');
  await worker('native-ui', { ...window, command: 'foreground' }); await delay(300);
  await worker('native-ui', { ...window, command: 'capture-visible', argument: path.join(output, 'real-ifeo-first-window.png') }); evidence.push('real-ifeo-first-window.png');
  const workload = path.join(root, 'tests/TaskManager.TestWorkload/bin/Release/net10.0-windows10.0.19041.0/TaskManager.TestWorkload.exe');
  const target = await api('processes/start', { executable: workload, arguments: `${windowsQuote(path.join(session, 'workload'))} 60` });
  try {
    check((await worker('elevation', { pid: target.pid })).data.elevated, 'real dedicated new-task process inherits the elevated token');
    await api(`processes/${target.pid}/terminate`, { creationTimeFileTime: '1' }, 409);
    await api(`processes/${bootstrap.hostPid}/terminate`, { creationTimeFileTime: hostIdentity.fileTime }, 403);
    check(true, 'identity mismatch and own Host protection remain enforced on the real IFEO instance');
  } finally { await api(`processes/${target.pid}/terminate`, { creationTimeFileTime: target.creationTimeFileTime }); }
  for (const [targetPath, bare] of [[systemTarget, false], [wow64Target, false], ['taskmgr.exe', true]]) {
    await api('window/minimize', {});
    await until(() => nativeUi(window, 'state').minimized, 'test HWND is actually minimized');
    const forwarded = mediumLaunch(targetPath, ['/7'], { TASKMANAGER_PORT: String(forwardPort), TASKMANAGER_CDP_PORT: String(forwardCdp), TASKMANAGER_DATA_DIR: path.join(session, 'forward-preferences') }, bare);
    check(forwarded.success && forwarded.exited && forwarded.exitCode === 0, `real repeated ${bare ? 'bare taskmgr.exe' : targetPath} Launcher exits successfully`);
    await until(() => !nativeUi(window, 'state').minimized, 'production IFEO entry restores same HWND', 20000);
    check((await api('health')).processId === bootstrap.hostPid && windows('蓝域').filter(row => ids.has(row.pid)).length === 1, 'repeated system entry restores the same HWND and preserves the original Host PID');
    check(!await portOpen(forwardPort) && !await portOpen(forwardCdp), 'repeated entry opens neither another HTTP host nor another CDP listener');
  }
  await worker('native-ui', { ...window, command: 'foreground' }); await delay(300);
  await worker('native-ui', { ...window, command: 'capture-visible', argument: path.join(output, 'real-ifeo-restored-window.png') }); evidence.push('real-ifeo-restored-window.png');
  check(errors.length === 0, 'real repair and IFEO Electron pages emit no renderer errors');
  state = await api('desktop/task-manager-integration');
  await api('desktop/task-manager-integration', { enabled: false, expectedRevision: state.revision });
  await api('window/close', {});
  await until(async () => !await portOpen(entryPort), 'real IFEO desktop closes'); token = undefined;
  check(!await portOpen(settingsPort) && !await portOpen(settingsCdp) && !await portOpen(entryPort), 'repair and real IFEO desktops release all HTTP and CDP listeners');
} catch (error) {
  resultError = error;
  await page?.screenshot({ path: path.join(output, 'failure.png') }).catch(() => {});
} finally {
  if (page) { try { await page.getByRole('button', { name: '关闭窗口', exact: true }).click({ timeout: 5000 }); } catch {} }
  else if (token) { try { await api('window/close', {}); } catch {} }
  await browser?.close().catch(() => {});
  if (workerStarted) {
    try {
      await worker('finish');
      let finished;
      await until(async () => { finished = JSON.parse(await readFile(path.join(session, 'worker-finished.json'), 'utf8')); return true; }, 'elevated finally cleanup and restoration', 45000);
      assert.ok(finished.ok, finished.error); assert.ok(finished.allTrackedExited, 'all test-owned identities exit'); tracked.push(...finished.tracked); preferencesRestored = finished.preferencesRestored;
    } catch (error) { resultError ??= error; }
  }
  finalState = independentRegistry();
  try { assert.equal(finalState, initial, 'final independent complete HKLM64 snapshot must exactly equal initial state'); }
  catch (error) { resultError ??= error; }
  await writeFile(path.join(output, 'registry-after.json'), finalState + '\n');
  const git = spawnSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8' }).stdout.trim();
  const productVersion = powershell(['-Command', `(Get-Item -LiteralPath ${psQuote(hostExe)}).VersionInfo.ProductVersion`]);
  const result = {
    result: resultError ? 'FAIL' : 'PASS', checkedAtUtc: new Date().toISOString(), testWorkspaceCommit: git, releaseCodeCommit: productVersion.match(/\+([a-f0-9]{40})/)?.[1] ?? null, productVersion, directory,
    hostSha256: hash(await readFile(hostExe)), launcherSha256: hash(await readFile(launcherExe)),
    assertions: checks.length, checks, launches, errors, evidence, testSession: session,
    finalRegistryRestored: finalState === initial, preferencesRestored, registryBeforeSha256: hash(initial ?? ''), registryAfterSha256: hash(finalState),
    trackedIdentities: [...new Map(tracked.map(({ pid, fileTime, imagePath }) => [`${pid}:${fileTime}`, { pid, fileTime, imagePath }])).values()],
    notExecuted: ['real UAC cancellation', 'wrong administrator credentials', 'token-query failure injection', 'actual NSIS lifecycle (separate installer acceptance)'],
    ...(resultError ? { error: resultError.stack } : {})
  };
  await writeFile(path.join(output, 'results.json'), JSON.stringify(result, null, 2));
  console.log(JSON.stringify({ result: result.result, assertions: checks.length, directory, releaseCodeCommit: result.releaseCodeCommit, finalRegistryRestored: result.finalRegistryRestored, preferencesRestored, report: path.join(output, 'results.json'), ...(resultError ? { error: resultError.message } : {}) }, null, 2));
}
if (resultError) throw resultError;
