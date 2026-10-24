import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import path from 'node:path';

const probe = path.resolve(import.meta.dirname, 'TaskManager.WindowProbe/bin/Release/net10.0-windows10.0.19041.0/TaskManager.WindowProbe.exe');
function sessions() {
  const result = spawnSync('logman.exe', ['query', '-ets'], { encoding: 'utf8', windowsHide: true, timeout: 15000 });
  assert.equal(result.status, 0, 'test-owned ETW cleanup requires real query permission');
  return [...new Set(result.stdout.match(/BlueDomain-Network-\d+-\d+-[a-f0-9]{32}/g) ?? [])];
}
// Native suites intentionally terminate their own Host to exercise recovery.
// Windows system loggers survive TerminateProcess; capture exact ownership before it dies.
export async function stopOwnedHost(host) {
  if (!host || host.exitCode !== null || host.signalCode !== null) return;
  const identity = spawnSync(probe, ['process-elevation', String(host.pid)], { encoding: 'utf8', windowsHide: true, timeout: 15000 });
  assert.equal(identity.status, 0, 'test Host identity remains verifiable before termination');
  const { creationTimeFileTime } = JSON.parse(identity.stdout);
  assert.match(creationTimeFileTime, /^\d+$/);
  const prefix = `BlueDomain-Network-${host.pid}-${creationTimeFileTime}-`;
  const owned = sessions().filter(name => name.startsWith(prefix));
  const exited = new Promise(resolve => host.once('exit', resolve));
  host.kill();
  await exited;
  // A failed consumer can recreate a session between the capture and termination.
  // The verified creation FILETIME keeps these additional names tied to this exact Host.
  for (const name of sessions()) if (name.startsWith(prefix) && !owned.includes(name)) owned.push(name);
  for (const name of owned) {
    if (!sessions().includes(name)) continue;
    const stopped = spawnSync('logman.exe', ['stop', name, '-ets'], { encoding: 'utf8', windowsHide: true, timeout: 15000 });
    assert.equal(stopped.status, 0, 'stop only the captured test Host ETW session');
  }
  assert.ok(!sessions().some(name => owned.includes(name)), 'all exact test-owned ETW sessions released');
}
