import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
export const probeExe = path.join(root, 'tests/TaskManager.WindowProbe/bin/Release/net10.0-windows10.0.19041.0/TaskManager.WindowProbe.exe');
export function probe(...args) {
  const result = spawnSync(probeExe, args.map(String), { encoding: 'utf8', windowsHide: true, timeout: 20000 });
  if (result.status !== 0) throw new Error('Independent CsWin32 probe: ' + (result.stderr || result.error));
  return JSON.parse(result.stdout);
}
export const windows = (filter, includeHidden = false) => probe('windows', filter, ...(includeHidden ? ['--include-hidden'] : []));
export const nativeUi = (window, command, ...args) => probe('native-ui', window.hwnd, window.pid, window.fileTime, command, ...args);
