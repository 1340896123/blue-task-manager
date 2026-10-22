import { spawn } from 'node:child_process';
import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const suites = ['static-v11', 'native-api', 'native-metadata-v11', 'native-core-v11', 'native-management-v11', 'native-telemetry-v11', 'gpu-identity-v11', 'process-ui-v11', 'pages-ui-v11', 'ui-smoke', 'recovery-e2e'];
const results = [];
for (const suite of suites) {
  const started = performance.now();
  const child = spawn(process.execPath, [path.join(root, 'tests', suite + '.mjs')], { cwd: root, env: process.env, windowsHide: true });
  let stdout = '', stderr = ''; child.stdout.on('data', data => stdout += data); child.stderr.on('data', data => stderr += data);
  const code = await new Promise(resolve => child.on('exit', resolve));
  if (code !== 0) { console.error(stdout, stderr); throw new Error(suite + ' failed: ' + code); }
  const report = JSON.parse(stdout);
  results.push({ suite, result: report.result ?? (report.passed === true ? 'PASS' : 'UNKNOWN'), assertions: report.assertions ?? report.checks?.length, durationMs: performance.now() - started });
  console.log(suite + ': PASS ' + results.at(-1).assertions);
}
await mkdir(path.join(root, 'output/playwright'), { recursive: true });
await writeFile(path.join(root, 'output/playwright/regression-v11.json'), JSON.stringify({ result: 'PASS', checkedAtUtc: new Date().toISOString(), suites: results }, null, 2));
