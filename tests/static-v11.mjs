import { readdir, readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import assert from 'node:assert/strict';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
async function files(dir,extension){const entries=await readdir(dir,{withFileTypes:true});return(await Promise.all(entries.filter(e=>!['obj','bin','node_modules','wwwroot'].includes(e.name)).map(e=>e.isDirectory()?files(path.join(dir,e.name),extension):e.name.endsWith(extension)?[path.join(dir,e.name)]:[]))).flat();}
const checks=[];
for(const project of ['Takao.Host','Takao.Launcher'])for(const file of await files(path.join(root,'src',project),'.cs')){const text=await readFile(file,'utf8');assert.ok(!/System\.Text\.Json|\[DllImport|\[LibraryImport|FileVersionInfo\.GetVersionInfo|WindowsIdentity\.GetCurrent|RegistryKey\./.test(text),file);}
checks.push('application C# JSON uses JSON.NET; native bindings are CsWin32, including metadata/account/registry; no handwritten native imports');
const style=await readFile(path.join(root,'src/Takao.Web/src/styles.css'),'utf8');const outsideTokens=style.replace(/:root\s*\{[\s\S]*?\}/g,'');assert.ok(!/#[0-9a-f]{3,8}\b|\brgba?\(|\bhsla?\(|font-size\s*:\s*\d|box-shadow\s*:\s*(?!var\()[0-9]/i.test(outsideTokens));
for(const extension of ['.tsx','.ts'])for(const file of await files(path.join(root,'src/Takao.Web/src'),extension)){const text=await readFile(file,'utf8');assert.ok(!/#[0-9a-f]{3,8}\b|\brgba?\(|\bhsla?\(|Math\.random\(|route\.fulfill|mockData/i.test(text),file);}
checks.push('component colors/font sizes/shadows resolve semantic tokens; no random/mock business data or mocked API routes');
assert.ok(style.includes('--height-table-header: 58px')&&style.includes('--height-table-header-plain: 36px')&&style.includes('--height-row-compact: 32px')&&style.includes('--height-row-standard: 40px'));
checks.push('metric header58/plain header36/compact32/standard40 originate from design tokens; actual DOM checked by UI suites');
console.log(JSON.stringify({result:'PASS',assertions:checks.length,checks},null,2));
