import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { mkdir } from 'node:fs/promises';

const root = path.resolve(import.meta.dirname, '..');
const output = path.join(root, 'output/playwright/network-throughput');
await mkdir(output, { recursive: true });
const native = path.join(root, 'tests/Takao.NetworkTests/bin/Release/net10.0-windows10.0.19041.0/Takao.NetworkTests.exe');
const result = spawnSync(native, [path.join(output, 'native.json')], { cwd: root, encoding: 'utf8', windowsHide: true, timeout: 180000 });
assert.equal(result.status, 0, result.stdout + result.stderr);
console.log(result.stdout);
