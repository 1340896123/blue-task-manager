import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { readFile, access, mkdtemp, rm, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { stopOwnedHost } from '../../../tests/etw-support.mjs';
import { until, portOpen } from '../../../tests/e2e-support.mjs';
const root = path.resolve(import.meta.dirname, '../../..');
const directory = path.join(root, 'output/Takao-win-x64');
const temporary = await mkdtemp(path.join(tmpdir(), 'takao-legacy-entry-'));
const checks = [];
const check = (ok, text) => { assert.ok(ok, text); checks.push(text); };
const port = 5367, origin = `http://127.0.0.1:${port}`;
let host;
try {
  for (const name of ['Host', 'Launcher']) {
    const hash = async file => createHash('sha256').update(await readFile(path.join(directory, file))).digest('hex');
    check(await hash(`Takao.${name}.exe`) === await hash(`TaskManager.${name}.exe`), `legacy ${name} apphost is identical to the Takao executable`);
    check(await access(path.join(directory, `TaskManager.${name}.dll`)).then(() => false, () => true), `legacy ${name} DLL is absent from the new payload`);
  }
  check(!await portOpen(port), 'dedicated legacy alias port is free');
  host = spawn(path.join(directory, 'TaskManager.Host.exe'), ['--headless', `--port=${port}`], { cwd: directory, env: { ...process.env, TASKMANAGER_DATA_DIR: temporary }, windowsHide: true, stdio: 'ignore' });
  await until(async () => { try { return (await (await fetch(origin + '/api/health')).json()).ready; } catch { return false; } }, 'legacy apphost starts the production Takao assembly');
  const bootstrap = await (await fetch(origin + '/api/bootstrap', { headers: { Origin: origin } })).json();
  check(bootstrap.hostPid === host.pid && bootstrap.desktop === false, 'legacy Host alias loads the production Takao assembly with the exact test PID');
  await stopOwnedHost(host);
  check(!await portOpen(port), 'legacy alias exit releases its listener and exact owned ETW session');
  const report = { result: 'PASS', assertions: checks.length, checks, checkedAtUtc: new Date().toISOString() };
  await writeFile(path.join(import.meta.dirname, 'legacy-alias.json'), JSON.stringify(report, null, 2));
  console.log(JSON.stringify(report));
} finally {
  await stopOwnedHost(host);
  await rm(temporary, { recursive: true, force: true });
}
