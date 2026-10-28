import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import path from 'node:path';

const probe = path.resolve(import.meta.dirname, 'Takao.WindowProbe/bin/Release/net10.0-windows10.0.19041.0/Takao.WindowProbe.exe');
export async function networkSessions() {
  for (let attempt=0;attempt<4;attempt++) {
    const result = spawnSync('logman.exe', ['query', '-ets'], { encoding: 'utf8', windowsHide: true, timeout: 15000 });
    if (result.status===0) return [...new Set(result.stdout.match(/Takao-Network-\d+-\d+-[a-f0-9]{32}/g) ?? [])];
    // ETW provider removal during enumeration can yield ERROR_WMI_GUID_NOT_FOUND.
    // Retry this exact Windows race, without treating a failed query as an empty list.
    if ((result.status>>>0)!==0x80071068 || attempt===3) assert.equal(result.status,0,'ETW session enumeration failed; ownership cleanup cannot be verified');
    await new Promise(resolve=>setTimeout(resolve,100*(attempt+1)));
  }
  throw new Error('ETW session enumeration exhausted its bounded retry');
}
// Native suites intentionally terminate their own Host to exercise recovery.
// Windows system loggers survive TerminateProcess; capture exact ownership before it dies.
export async function stopOwnedHost(host) {
  if (!host || host.exitCode !== null || host.signalCode !== null) return;
  const identity = spawnSync(probe, ['process-elevation', String(host.pid)], { encoding: 'utf8', windowsHide: true, timeout: 15000 });
  assert.equal(identity.status, 0, 'test Host identity remains verifiable before termination');
  const { creationTimeFileTime } = JSON.parse(identity.stdout);
  assert.match(creationTimeFileTime, /^\d+$/);
  const prefix = `Takao-Network-${host.pid}-${creationTimeFileTime}-`;
  const owned = (await networkSessions()).filter(name => name.startsWith(prefix));
  const exited = new Promise(resolve => host.once('exit', resolve));
  host.kill();
  await exited;
  // A failed consumer can recreate a session between the capture and termination.
  // The verified creation FILETIME keeps these additional names tied to this exact Host.
  for (const name of await networkSessions()) if (name.startsWith(prefix) && !owned.includes(name)) owned.push(name);
  for (const name of owned) {
    if (!(await networkSessions()).includes(name)) continue;
    const stopped = spawnSync('logman.exe', ['stop', name, '-ets'], { encoding: 'utf8', windowsHide: true, timeout: 15000 });
    assert.equal(stopped.status, 0, 'stop only the captured test Host ETW session');
  }
  assert.ok(!(await networkSessions()).some(name => owned.includes(name)), 'all exact test-owned ETW sessions released');
}
