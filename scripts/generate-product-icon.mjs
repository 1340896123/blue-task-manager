import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { copyFile, mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

// One authored vector is the only graphic source. Chromium renders SVG;
// Pillow creates standard Windows ICO frames from the rendered RGBA image.
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const assets = path.join(root, 'assets/branding');
const web = path.join(root, 'src/Takao.Web/public');
const svg = await readFile(path.join(assets, 'takao.svg'), 'utf8');
const tokens = await readFile(path.join(root, 'design-system/takao/MASTER.md'), 'utf8');
for (const [token, color] of [['--color-accent', '#0067C0'], ['--color-on-accent', '#FFFFFF']]) {
  assert.ok(tokens.includes(`| ${token} | ${color} |`), `${token} must match the reviewed MASTER token`);
  assert.ok(svg.includes(color));
}
await mkdir(web, { recursive: true });
const browser = await chromium.launch({ headless: true });
try {
  const page = await browser.newPage({ viewport: { width: 512, height: 512 }, deviceScaleFactor: 1 });
  await page.setContent('<html><body style="margin:0;background:transparent"></body></html>');
  await page.evaluate(async source => {
    const icon = document.createElement('img');
    icon.src = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(source);
    icon.width = 512; icon.height = 512; icon.style.display = 'block';
    document.body.appendChild(icon); await icon.decode();
  }, svg);
  await page.locator('img').screenshot({ path: path.join(assets, 'takao-512.png'), omitBackground: true });
} finally { await browser.close(); }

const python = process.env.TASKMANAGER_PYTHON ?? 'python';
const result = spawnSync(python, ['-c', `
from PIL import Image
from pathlib import Path
import sys
directory = Path(sys.argv[1])
source = Image.open(directory / 'takao-512.png').convert('RGBA')
assert source.size == (512, 512)
assert source.getpixel((0, 0))[3] == 0, 'Outer edge must remain transparent'
source.resize((256, 256), Image.Resampling.LANCZOS).save(directory / 'takao-256.png', optimize=True)
sizes = [(n, n) for n in (16, 24, 32, 48, 64, 128, 256)]
source.save(directory / 'takao.ico', format='ICO', sizes=sizes)
with Image.open(directory / 'takao.ico') as icon:
    assert icon.ico.sizes() == set(sizes), 'All required Windows icon frames must exist'
print('SVG -> 512/256 RGBA PNG -> ICO 16/24/32/48/64/128/256: PASS')
`, assets], { encoding: 'utf8', windowsHide: true });
if (result.status !== 0) throw new Error(result.stderr || 'Python/Pillow icon conversion failed.');
for (const [source, target] of [['takao.svg', 'icon.svg'], ['takao-512.png', 'icon-512.png'], ['takao-256.png', 'icon-256.png'], ['takao.ico', 'favicon.ico']]) {
  await copyFile(path.join(assets, source), path.join(web, target));
}
// Keep generated asset metadata text-only; no machine/runtime data or secrets.
await writeFile(path.join(assets, 'README.md'), '# Product icon assets\n\nSource: `takao.svg` (hand-authored vector).\n\nRegenerate PNG, ICO and web copies from the repository root with `node scripts/generate-product-icon.mjs`. Requires the locked Playwright browser and Python with Pillow.\n\nPalette: MASTER `--color-accent` and `--color-on-accent`; transparent outer edge. No third-party brand artwork is included.\n');
console.log(result.stdout.trim());
