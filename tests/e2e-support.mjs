import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import net from 'node:net';
export const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
export const alive = pid => { try { process.kill(pid, 0); return true; } catch { return false; } };
export async function until(predicate, message, timeout = 20000) {
  const start = Date.now();
  while (Date.now() - start < timeout) { try { if (await predicate()) return; } catch {} await delay(150); }
  throw new Error(message);
}
export const assertions = [];
export function check(condition, message) { assert.ok(condition, message); assertions.push(message); }
export function processTree(rootPid) {
  // Only return descendants of the exact host PID created by this test; never select by name.
  const command = `$all = Get-CimInstance Win32_Process; $ids = [System.Collections.Generic.HashSet[int]]::new(); [void]$ids.Add(${rootPid}); do { $changed = $false; foreach ($p in $all) { if ($ids.Contains([int]$p.ParentProcessId) -and $ids.Add([int]$p.ProcessId)) { $changed = $true } } } while ($changed); @($all | Where-Object { $ids.Contains([int]$_.ProcessId) } | Select-Object @{n='pid';e={$_.ProcessId}},@{n='imagePath';e={$_.ExecutablePath}},@{n='created';e={$_.CreationDate.ToUniversalTime().ToString('O')}},@{n='fileTime';e={$_.CreationDate.ToFileTimeUtc().ToString()}}) | ConvertTo-Json -Compress`;
  const result = spawnSync('powershell', ['-NoProfile', '-Command', command], { encoding: 'utf8', windowsHide: true });
  if (result.status !== 0) throw new Error('Independent Windows process-tree query failed');
  const data = JSON.parse(result.stdout || '[]'); return Array.isArray(data) ? data : [data];
}
export function ownedAlive(entry) {
  const result = spawnSync('powershell', ['-NoProfile', '-Command', `$p=Get-CimInstance Win32_Process -Filter 'ProcessId = ${entry.pid}'; if ($p -and $p.CreationDate.ToUniversalTime().ToString('O') -eq '${entry.created}') { Write-Output 'alive' }`], { encoding: 'utf8', windowsHide: true });
  return result.stdout.trim() === 'alive';
}
export async function portOpen(port) {
  return new Promise(resolve => { const socket = net.connect(port, '127.0.0.1'); socket.once('connect', () => { socket.destroy(); resolve(true); }); socket.once('error', () => resolve(false)); socket.setTimeout(600, () => { socket.destroy(); resolve(false); }); });
}
