import { networkSessions } from './etw-support.mjs';
import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { chromium } from 'playwright';
import { delay, until, portOpen, processTree, ownedAlive } from './e2e-support.mjs';
import { probe, windows, nativeUi } from './native-ui-support.mjs';
import { clickCommand, openProcessFilter } from './layout-ui-support.mjs';

// Real production HTTP / Electron / Windows only. Wrappers below observe native
// fetch and Blob lifetimes; they never intercept or replace an API response.
const root = path.resolve(import.meta.dirname, '..');
const desktop = process.env.TASKMANAGER_ICONS_DESKTOP === '1', mode = desktop ? 'desktop' : 'browser';
const release = path.resolve(process.env.TASKMANAGER_RELEASE_DIRECTORY ?? path.join(root, 'output/Takao-win-x64'));
const hostExe = path.join(release, 'Takao.Host.exe');
const workloadExe = path.join(root, 'tests/Takao.TestWorkload/bin/Release/net10.0-windows10.0.19041.0/Takao.TestWorkload.exe');
const output = path.join(root, 'output/playwright/application-icons-adapters');
const temporary = await mkdtemp(path.join(tmpdir(), 'taskmanager-icons-adapters-'));
const fixtureName = `TaskManagerIconAcceptance-${path.basename(temporary)}`;
const port = Number(process.env.TASKMANAGER_ICONS_PORT ?? 5371), cdp = Number(process.env.TASKMANAGER_ICONS_CDP_PORT ?? 9371), historyPort = port + 1, origin = `http://127.0.0.1:${port}`;
const checks = [], screenshots = [], geometry = [], errors = [], cspErrors = [], apiFacts = [], networkFacts = {}, coverage = {};
const check = (ok, message) => { assert.ok(ok, message); checks.push(message); };
const ps = command => {
  const result = spawnSync('powershell.exe', ['-NoProfile', '-Command', command], { encoding: 'utf8', windowsHide: true, timeout: 30000 });
  if (result.status !== 0) throw new Error(`Test fixture PowerShell failed: ${result.stderr}`);
  return result.stdout.trim();
};
const quote = value => `'${String(value).replaceAll("'", "''")}'`;
await mkdir(output, { recursive: true });
assert.ok(!await portOpen(port) && !await portOpen(cdp) && !await portOpen(historyPort), 'dedicated icon acceptance ports free');
// Unique Startup shortcut points at the test Host. It is only enumerated; no
// startup item is launched, enabled, disabled, or changed by the production app.
const shortcut = ps(`$p=Join-Path ([Environment]::GetFolderPath('Startup')) ${quote(fixtureName + '.lnk')}; if(Test-Path -LiteralPath $p){throw 'Fixture already exists'}; $w=New-Object -ComObject WScript.Shell; $s=$w.CreateShortcut($p); $s.TargetPath=${quote(hostExe)}; $s.Arguments='--headless --port=5379'; $s.IconLocation=${quote(hostExe + ',0')}; $s.Save(); Write-Output $p`);
const host = spawn(hostExe, [...(desktop ? [] : ['--headless']), `--port=${port}`], { cwd: release, env: { ...process.env, TASKMANAGER_DATA_DIR: path.join(temporary, 'preferences'), TASKMANAGER_CDP_PORT: String(cdp) }, windowsHide: true, stdio: 'ignore' });
const workload = spawn(workloadExe, [path.join(temporary, 'workload'), '180', 'tree'], { cwd: root, windowsHide: true, stdio: 'ignore' });
let browser, page, token, tree = [], workloadTree = [], historyHost, historyTree = [], failure;
const get = async route => { const r = await fetch(`${origin}/api/${route}`, { signal: AbortSignal.timeout(12000) }); assert.ok(r.ok, `${route}: ${r.status}`); return r.json(); };
const post = async (route, body) => { const r = await fetch(`${origin}/api/${route}`, { method: 'POST', headers: { Origin: origin, 'Content-Type': 'application/json', 'X-TaskManager-Token': token }, body: JSON.stringify(body), signal: AbortSignal.timeout(20000) }); assert.ok(r.ok, `${route}: ${r.status}`); return r.json(); };
async function stopIconHost(child) {
  if (!child || child.exitCode !== null || child.signalCode !== null) return;
  let identity;
  try { identity = probe('process-elevation', child.pid); }
  catch (error) { await until(() => child.exitCode !== null || child.signalCode !== null, 'test Host already exited', 5000); return; }
  const prefix = `Takao-Network-${child.pid}-${identity.creationTimeFileTime}-`;
  // Window close can exit the Host during native queries. Poll the child state so a
  // missed exit event cannot leave this suite awaiting forever.
  child.kill();
  await until(() => child.exitCode !== null || child.signalCode !== null, 'test-owned Host exited', 15000);
  for (const name of (await networkSessions()).filter(name => name.startsWith(prefix))) {
    const stopped = spawnSync('logman.exe', ['stop', name, '-ets'], { encoding: 'utf8', windowsHide: true, timeout: 15000 });
    assert.equal(stopped.status, 0, 'stop only the exact icon test Host ETW session');
  }
  assert.ok(!(await networkSessions()).some(name => name.startsWith(prefix)), 'exact icon test Host ETW sessions released');
}
async function icon(kind, id, expected = 200) {
  const r = await fetch(`${origin}/api/application-icon?kind=${encodeURIComponent(kind)}&id=${encodeURIComponent(id)}`);
  check(r.status === expected, `${kind} application-icon returns ${expected}`);
  const bytes = Buffer.from(await r.arrayBuffer());
  if (expected === 200) check(r.headers.get('content-type')?.startsWith('image/png') && bytes.subarray(0, 8).equals(Buffer.from([137,80,78,71,13,10,26,10])) && bytes.readUInt32BE(16) > 0, `${kind} icon is a nonempty real PNG`);
  apiFacts.push({ kind, status: r.status, ...(expected === 200 ? { width: bytes.readUInt32BE(16), height: bytes.readUInt32BE(20), bytes: bytes.length } : {}) });
  return bytes;
}
async function pngFacts(bytes) {
  return page.evaluate(async data => {
    const blob = new Blob([Uint8Array.from(atob(data), c => c.charCodeAt(0))], { type: 'image/png' });
    const bitmap = await createImageBitmap(blob), canvas = document.createElement('canvas'); canvas.width = bitmap.width; canvas.height = bitmap.height;
    const c = canvas.getContext('2d'); c.drawImage(bitmap, 0, 0); const pixels = c.getImageData(0,0,canvas.width,canvas.height).data; bitmap.close();
    let visibleOnWhite=0;for(let i=0;i<pixels.length;i+=4)if((255-Math.min(pixels[i],pixels[i+1],pixels[i+2]))*pixels[i+3]/255>=48)visibleOnWhite++;
    return { width: canvas.width, height: canvas.height, nontransparent: [...pixels].filter((_, i) => i % 4 === 3 && pixels[i] > 0).length, visibleOnWhite, pixels: Array.from(pixels) };
  }, bytes.toString('base64'));
}
function pixelDifference(left, right) {
  const difference={opaque:0,alpha:0};
  for(let i=0;i<left.pixels.length;i+=4){
    if(left.pixels[i+3]!==right.pixels[i+3])difference.alpha++;
    if(left.pixels[i+3]===255&&right.pixels[i+3]===255&&left.pixels.slice(i,i+3).some((value,j)=>value!==right.pixels[i+j]))difference.opaque++;
  }
  return difference;
}
async function decoded(locator, label) {
  await until(async () => await locator.locator('.process-icon img').evaluateAll(images => images.length > 0 && images.every(img => img.complete && img.naturalWidth > 0)), label, 20000);
  check(true, label);
}
const nav = async name => { await page.getByRole('button', { name, exact: true }).click(); await page.getByRole('heading', { level: 1, name, exact: true }).waitFor(); };
async function capture(label) {
  const file = `${mode}-${label}.png`;
  if (desktop) { const session = await page.context().newCDPSession(page); const shot = await session.send('Page.captureScreenshot', { format:'png', fromSurface:true, captureBeyondViewport:false }); await session.detach(); await writeFile(path.join(output,file),Buffer.from(shot.data,'base64')); }
  else await page.screenshot({ path: path.join(output, file) });
  screenshots.push(file);
}
async function viewport(width, height) {
  if (desktop) { probe(tree.map(p => `${p.pid}:${p.fileTime}`).join(','), 'resize', width, height); await delay(300); }
  else await page.setViewportSize({ width, height });
}
try {
  await until(async () => (await get('health')).ready, 'real elevated production Host ready', 45000);
  token = (await get('bootstrap')).token;
  await until(async () => { const s = await get('snapshot'); return s.slowDataCapturedAtUtc && s.processes.some(p => p.pid === workload.pid) && s.startupItems.some(p => p.name.includes(fixtureName)); }, 'real process, SCM, WTS and startup fixture sampled', 45000);
  if (desktop) {
    await until(() => portOpen(cdp), 'production Electron CDP ready', 45000); browser = await chromium.connectOverCDP(`http://127.0.0.1:${cdp}`);
    await until(() => browser.contexts().flatMap(c => c.pages()).some(p => p.url().startsWith(origin)), 'production BrowserWindow page', 60000);
    page = browser.contexts().flatMap(c => c.pages()).find(p => p.url().startsWith(origin));
  } else { browser = await chromium.launch({ headless: true }); page = await browser.newPage({ viewport: { width:1380,height:860 }, reducedMotion:'reduce' }); }
  await page.addInitScript(() => {
    const facts = window.__iconAcceptance = { requests:0, inflight:0, maximumInflight:0, aborted:0, created:0, revoked:0, live:[], maximumLive:0 };
    const originalFetch = window.fetch;
    window.fetch = async function(...args) { const watched = String(args[0]).includes('/application-icon?'); if (!watched) return originalFetch.apply(this,args); facts.requests++; facts.inflight++; facts.maximumInflight=Math.max(facts.maximumInflight,facts.inflight); try { return await originalFetch.apply(this,args); } catch(e) { if (e.name==='AbortError') facts.aborted++; throw e; } finally { facts.inflight--; } };
    const create=URL.createObjectURL.bind(URL), revoke=URL.revokeObjectURL.bind(URL);
    URL.createObjectURL = value => { const url=create(value); facts.created++; facts.live.push(url); facts.maximumLive=Math.max(facts.maximumLive,facts.live.length); return url; };
    URL.revokeObjectURL = value => { facts.revoked++; facts.live=facts.live.filter(url=>url!==value); return revoke(value); };
    addEventListener('pagehide',()=>sessionStorage.setItem('icon-acceptance-unload',JSON.stringify({created:facts.created,revoked:facts.revoked,remaining:facts.live.length})));
  });
  page.on('pageerror', error => errors.push(error.message)); page.on('console', message => { if (/content.security.policy|violates.*directive|refused to load.*blob:/i.test(message.text())) cspErrors.push(message.text()); });
  const writes=[]; page.on('request', request => { if(request.method()==='POST' && /\/services\/.*\/control$|\/desktop\/task-manager-integration$/.test(request.url())) writes.push(request.url().split('/api/')[1]); });
  await page.goto(origin); await page.getByRole('table', { name:'进程列表',exact:true }).waitFor();
  if(desktop){const firstLaunch=page.getByRole('dialog',{name:'替换标准任务管理器？',exact:true});await firstLaunch.waitFor();await firstLaunch.getByRole('button',{name:'暂不替换',exact:true}).click();await firstLaunch.waitFor({state:'detached'});check(true,'defer first-launch replacement in isolated preferences without changing the system entry');}
  tree = processTree(host.pid); workloadTree = processTree(workload.pid);
  let snapshot = await get('snapshot'); const own = snapshot.processes.find(p => p.pid === host.pid), target = snapshot.processes.find(p => p.pid === workload.pid);
  check(Boolean(own?.path && target?.identity), 'test-owned exact Windows identities and real executable paths available');
  const actualIcon = await icon('process', own.identity); await writeFile(path.join(output,`${mode}-process-api.png`),actualIcon); probe('icon-exe', hostExe, path.join(output, `${mode}-independent`));
  const shell = await pngFacts(actualIcon), independent = await pngFacts(await readFile(path.join(output, `${mode}-independent/product-exe-small.png`)));
  const productColors = pixels => {let blue=0,white=0;for(let i=0;i<pixels.length;i+=4){if(pixels[i+3]>220&&pixels[i]<15&&Math.abs(pixels[i+1]-103)<15&&Math.abs(pixels[i+2]-192)<15)blue++;if(pixels[i+3]>220&&pixels[i]>240&&pixels[i+1]>240&&pixels[i+2]>240)white++;}return{blue,white};};
  const shellColors=productColors(shell.pixels),resourceColors=productColors(independent.pixels);const difference={pixels:0,opaque:0,alpha:0};for(let i=0;i<shell.pixels.length;i+=4){if(shell.pixels.slice(i,i+4).some((n,j)=>n!==independent.pixels[i+j])){difference.pixels++;if(shell.pixels[i+3]===255&&independent.pixels[i+3]===255)difference.opaque++;if(shell.pixels[i+3]!==independent.pixels[i+3])difference.alpha++;}}coverage.productIcon={shell:{width:shell.width,height:shell.height,...shellColors},resource:{width:independent.width,height:independent.height,...resourceColors},difference};
  check(shellColors.blue>10&&shellColors.white>0&&resourceColors.blue>10&&resourceColors.white>0,'Host Shell PNG and independent ExtractIconEx resource both contain actual blue/white product pixels');
  check(shell.width===independent.width&&shell.height===independent.height&&difference.opaque===0&&difference.alpha===0,'independent product resource and Shell icon match opaque pixels and exact alpha silhouette');
  const search = page.getByRole('searchbox'); await search.fill(String(own.pid)); let row = page.locator(`tr[data-row-key="${own.identity}"]`); await row.waitFor(); await decoded(row,'process list displays decoded real executable icon');
  await row.dblclick(); let dialog = page.getByRole('dialog'); await decoded(dialog.locator('.detail-identity'),'process overview displays decoded real executable icon'); await capture('process-detail'); await page.keyboard.press('Escape');
  await nav('详细信息'); row = page.locator(`tr[data-row-key="${own.identity}"]`); await row.waitFor(); await decoded(row,'details list displays decoded real executable icon');
  await nav('用户'); await search.fill(String(own.sessionId)); const userRow=page.locator(`tr[data-row-key="${own.sessionId}"]`); await userRow.waitFor(); check(await userRow.locator('.lucide-user-round').count()===1 && await userRow.locator('.process-icon img').count()===0,'Windows account row keeps UserRound account semantics'); await userRow.dblclick();
  await page.getByRole('table',{name:'进程列表',exact:true}).waitFor(); await search.fill(String(own.pid)); row=page.locator(`tr[data-row-key="${own.identity}"]`); await row.waitFor(); await decoded(row,'user session process navigation displays exact executable icon');
  await openProcessFilter(page); await page.getByRole('button',{name:'清除筛选',exact:true}).click(); await page.keyboard.press('Escape');
  await search.fill(String(target.pid)); row=page.locator(`tr[data-row-key="${target.identity}"]`); await row.click();const previewResponse=page.waitForResponse(r=>r.url().endsWith(`/api/processes/${target.pid}/tree-preview`)&&r.request().method()==='POST'); await clickCommand(page,'结束进程树…',{menu:true});const preview=await(await previewResponse).json(); dialog=page.getByRole('dialog'); await dialog.getByRole('list',{name:'结束树完整成员'}).waitFor();
  const expectedWorkload=[target.identity,...(await readFile(path.join(temporary,'workload/children.txt'),'utf8')).trim().split(/\r?\n/)];check(expectedWorkload.every(identity=>preview.members.some(p=>p.identity===identity)),'real preview includes exact creation identities of root and both explicitly created workload children');await decoded(dialog.locator('.tree-preview'),'end-tree preview displays decoded real member PNGs'); check(await dialog.locator('.tree-preview [role="listitem"]').count()===preview.members.length,'UI tree preview preserves complete actual native member set'); await capture('tree-preview'); await dialog.getByRole('button',{name:'取消',exact:true}).click();
  // Start a second real Host, observe it, stop it, THEN request its history icon
  // for the first time. The browser is still on processes, never history.
  historyHost=spawn(hostExe,['--headless',`--port=${historyPort}`],{cwd:release,env:{...process.env,TASKMANAGER_DATA_DIR:path.join(temporary,'history')},windowsHide:true,stdio:'ignore'});
  let past; await until(async()=>{past=(await get('snapshot')).processes.find(p=>p.pid===historyHost.pid&&p.path);return Boolean(past);},'second real Host path observed'); historyTree=processTree(historyHost.pid); await stopIconHost(historyHost); await until(async()=>!(await get('snapshot')).processes.some(p=>p.identity===past.identity),'dedicated history identity exited');
  const historical=await icon('history',past.identity); const historicalPixels=await pngFacts(historical); check(JSON.stringify(historicalPixels.pixels)===JSON.stringify(shell.pixels),'first historical icon request after exit retains exact executable resource pixels');
  await nav('应用历史记录'); await search.fill(String(past.pid)); row=page.locator(`tr[data-row-key="${past.identity}"]`); await row.waitFor(); await decoded(row,'exited history list displays real retained PNG'); await capture('exited-history');
  await icon('history',`${past.pid}:1`,204); await icon('process','2147483647:1',204); await icon('service',fixtureName,204); await icon('startup',fixtureName,204); await icon('invalid',own.identity,400);
  await nav('启动应用'); await search.fill(fixtureName); snapshot=await get('snapshot'); const startup=snapshot.startupItems.find(p=>p.name.includes(fixtureName)); await icon('startup',startup.id); row=page.locator('tbody tr[data-row-key]').first(); await decoded(row,'real Startup .lnk displays executable PNG'); await row.dblclick(); await decoded(page.getByRole('dialog').locator('.detail-identity'),'real Startup detail displays executable PNG'); await capture('startup-detail'); await page.keyboard.press('Escape');
  const services=(await get('snapshot')).services; let running, stopped;
  for (const service of services.filter(s=>s.pid>0).slice(0,50)) { const r=await fetch(`${origin}/api/application-icon?kind=service&id=${encodeURIComponent(service.name)}`); if(r.status===200){running=service;break;} }
  for (const service of services.filter(s=>!s.pid).slice(0,50)) { const r=await fetch(`${origin}/api/application-icon?kind=service&id=${encodeURIComponent(service.name)}`); if(r.status===200){stopped=service;break;} }
  check(Boolean(running && stopped),'real running and stopped SCM services expose readable binary icon resources');
  for(const [state,service] of [['running',running],['stopped',stopped]]) { await icon('service',service.name); await nav('服务'); await search.fill(service.name); row=page.locator(`tr[data-row-key="${service.name}"]`); await decoded(row,`${state} service list displays real binary PNG`); await row.dblclick(); await decoded(page.getByRole('dialog').locator('.detail-identity'),`${state} service detail displays real binary PNG`); await capture(`${state}-service-detail`); await page.keyboard.press('Escape'); }
  snapshot=await get('snapshot'); const serviceProcess=snapshot.processes.find(p=>p.pid===running.pid&&p.creationTimeFileTime);
  check(Boolean(serviceProcess),'running SCM service has readable exact process identity'); await nav('进程'); await search.fill(String(serviceProcess.pid)); row=page.locator(`tr[data-row-key="${serviceProcess.identity}"]`); await row.dblclick(); await page.getByRole('tab',{name:'关联服务',exact:true}).click(); await decoded(page.getByRole('dialog').locator('.related-list'),'process associated service buttons display real service PNGs'); await capture('associated-services'); await page.keyboard.press('Escape');
  const packaged=(await get('snapshot')).processes.filter(p=>p.path&&/\\WindowsApps\\/i.test(p.path)); coverage.packagedApp={observed:packaged.length,verified:0};
  if(packaged.length){for(const candidate of packaged.slice(0,20)){const r=await fetch(`${origin}/api/application-icon?kind=process&id=${encodeURIComponent(candidate.identity)}`);if(r.status!==200)continue;const packagedIcon=await pngFacts(Buffer.from(await r.arrayBuffer()));if([16,32].includes(packagedIcon.width)&&packagedIcon.height===packagedIcon.width&&packagedIcon.visibleOnWhite>32){coverage.packagedApp.verified=1;coverage.packagedApp.nontransparentPixels=packagedIcon.nontransparent;coverage.packagedApp.visibleOnWhite=packagedIcon.visibleOnWhite;break;}}check(coverage.packagedApp.verified===1,'real WindowsApps process returns a native PNG visible on the white application surface');}
  // Match the actual executable resource shown by Windows Properties rather
  // than merely accepting a visible package tile from AppsFolder.
  const codex=packaged.filter(p=>/\\WindowsApps\\OpenAI\.Codex_[^\\]+\\app\\ChatGPT\.exe$/i.test(p.path));
  coverage.codexApp={observed:codex.length,verified:0};
  if(codex.length){
    const resourceDirectory=path.join(output,`${mode}-codex-independent`);probe('icon-exe',codex[0].path,resourceDirectory);
    const resource=await pngFacts(await readFile(path.join(resourceDirectory,'product-exe-small.png')));
    check(resource.width===16&&resource.height===16&&resource.nontransparent<256&&resource.visibleOnWhite>32,'independent Codex EXE resource has a visible glyph and transparent background like Windows Properties');
    coverage.codexApp.resource={width:resource.width,height:resource.height,nontransparent:resource.nontransparent,visibleOnWhite:resource.visibleOnWhite};
    let minimumVisibleOnWhite=Infinity;
    for(const candidate of codex){const bytes=await icon('process',candidate.identity);const facts=await pngFacts(bytes),difference=pixelDifference(facts,resource);check(facts.width===resource.width&&facts.height===resource.height&&difference.opaque===0&&difference.alpha===0,'reported Codex process icon matches independent EXE opaque pixels and alpha silhouette');minimumVisibleOnWhite=Math.min(minimumVisibleOnWhite,facts.visibleOnWhite);coverage.codexApp.verified++;if(coverage.codexApp.verified===1){coverage.codexApp.difference=difference;await writeFile(path.join(output,`${mode}-codex-api.png`),bytes);}}
    coverage.codexApp.minimumVisibleOnWhite=minimumVisibleOnWhite;
    await nav('进程');await search.fill(String(codex[0].pid));row=page.locator(`tr[data-row-key="${codex[0].identity}"]`);await decoded(row,'reported Codex process row displays its real decoded application PNG');
    const visible=await row.locator('.process-icon img').evaluate(img=>{const canvas=document.createElement('canvas');canvas.width=canvas.height=16;const c=canvas.getContext('2d');c.fillStyle=getComputedStyle(img.parentElement).backgroundColor;c.fillRect(0,0,16,16);c.drawImage(img,0,0,16,16);const pixels=c.getImageData(0,0,16,16).data;let count=0;for(let i=0;i<pixels.length;i+=4)if(255-Math.min(pixels[i],pixels[i+1],pixels[i+2])>=48)count++;return count;});
    check(visible>8,'reported Codex row icon is visibly distinct from its actual white frame');coverage.codexApp.renderedVisiblePixels=visible;await capture('codex-process');
  }
  // A resource-less EXE can still get a nonempty generic icon from SHGetFileInfo.
  // Preserve the package application image for this real Windows case.
  const resourceCounts=new Map(),withoutResources=packaged.filter(candidate=>{if(!resourceCounts.has(candidate.path))resourceCounts.set(candidate.path,probe('icon-resource-count',candidate.path).count);return resourceCounts.get(candidate.path)===0;});
  coverage.packagedFallback={observed:withoutResources.length,verified:0};
  for(const candidate of withoutResources){const response=await fetch(`${origin}/api/application-icon?kind=process&id=${encodeURIComponent(candidate.identity)}`);if(response.status!==200)continue;const bytes=Buffer.from(await response.arrayBuffer()),facts=await pngFacts(bytes);if(facts.width===32&&facts.height===32&&facts.visibleOnWhite>32){coverage.packagedFallback={observed:withoutResources.length,verified:1,name:candidate.name,width:facts.width,height:facts.height,visibleOnWhite:facts.visibleOnWhite};await writeFile(path.join(output,`${mode}-packaged-fallback-api.png`),bytes);break;}}
  if(withoutResources.length)check(coverage.packagedFallback.verified===1,'real packaged process without EXE icon resources keeps its visible 32px AppsFolder image');
  // Full independent interface set, with flags from Windows rather than names.
  const native=probe('network-counters'), hardware=native.filter(a=>a.hardware&&!a.filter&&![24,131].includes(a.type)&&a.status!=='IfOperStatusNotPresent'); snapshot=await get('snapshot');
  check(JSON.stringify(snapshot.networkAdapters.map(a=>a.id).sort())===JSON.stringify(hardware.map(a=>a.id).sort()),'networkAdapters equals independent complete present Windows hardware/nonfilter/nonloopback/nontunnel set');
  const ids=new Set(hardware.map(a=>a.id)),up=snapshot.networkAdapters.filter(a=>a.status==='IfOperStatusUp');
  check(snapshot.networkAdapters.every(a=>a.isHardware&&ids.has(a.id)),'all returned network adapters are independently verified hardware');
  const disconnected=snapshot.networkAdapters.filter(a=>a.status!=='IfOperStatusUp'); check(disconnected.every(a=>a.receiveBytesPerSecond===null&&a.sendBytesPerSecond===null&&a.unavailable.receiveBytesPerSecond&&a.unavailable.sendBytesPerSecond),'disconnected hardware remains present with null rates and explicit reasons');
  for(const [sumKey,partKey] of [['networkReceiveBytesPerSecond','receiveBytesPerSecond'],['networkSendBytesPerSecond','sendBytesPerSecond']]){const allKnown=up.length&&up.every(a=>a[partKey]!==null);check(allKnown?Math.abs(snapshot.system[sumKey]-up.reduce((s,a)=>s+a[partKey],0))<0.000001:snapshot.system[sumKey]===null,'system network aggregate equals Up hardware parts or honest null');}
  check(snapshot.performanceHistory.every(p=>p.networkAdapters.every(a=>ids.has(a.id))),'real network performance history contains no excluded virtual IDs');
  Object.assign(networkFacts,{nativeCount:native.length,hardwareCount:hardware.length,upCount:up.length,disconnectedCount:disconnected.length,notPresentExcluded:native.filter(a=>a.hardware&&!a.filter&&![24,131].includes(a.type)&&a.status==='IfOperStatusNotPresent').length,excludedCount:native.length-hardware.length,historyPoints:snapshot.performanceHistory.length});
  const virtual=native.find(a=>!ids.has(a.id)); if(virtual){await delay(900);const {window:_window,...prefs}=(await get('preferences')).preferences; await post('preferences',{...prefs,searchQuery:'',pages:{...prefs.pages,performance:{query:'',sortKey:'name',sortDirection:'ascending',visibleColumns:null,columnWidths:null,filters:null,scrollTop:0,selectedIdentity:null,...prefs.pages?.performance,resourceType:'network',resourceId:virtual.id}}}); await page.reload(); await nav('性能'); await until(async()=>await page.locator('.resource-tile[aria-pressed="true"]').count()===1,'old virtual network preference falls back'); check(!(await page.locator('.resource-tile[aria-pressed="true"]').getAttribute('title')).includes(virtual.id),'old virtual network preference selects available real resource');}
  // Every application-bearing page plus performance/user: dimensions and
  // fixed icon/frame tokens at both required native window sizes.
  for(const size of [[1380,860],[960,640]]){await viewport(...size);for(const name of ['进程','详细信息','应用历史记录','启动应用','服务','用户','性能']){await nav(name);await page.getByRole('searchbox').fill('');await delay(300);if(!['用户','性能'].includes(name))await decoded(page.locator('.data-table'),`${name} ${size[0]} screenshot waits for actual decoded PNG`);if(name==='性能'&&hardware.length)await page.locator('.resource-tile').filter({has:page.getByText((hardware.find(a=>a.status==='IfOperStatusUp')??hardware[0]).alias,{exact:true})}).click();const facts=await page.evaluate(()=>({viewport:[innerWidth,innerHeight],dpr:devicePixelRatio,overflow:document.documentElement.scrollWidth>innerWidth,icons:[...document.querySelectorAll('.data-table .process-icon')].map(el=>{const r=el.getBoundingClientRect();const child=el.firstElementChild?.getBoundingClientRect();return{frame:[r.width,r.height],image:child?[child.width,child.height]:null};}),rows:[...document.querySelectorAll('tr[data-row-key]')].map(el=>el.getBoundingClientRect().height)})); geometry.push({size:size[0],name,...facts});check(!facts.overflow,`${name} ${size[0]} has no document overflow`);check(facts.icons.every(i=>Math.abs(i.frame[0]-21)<.2&&Math.abs(i.frame[1]-21)<.2&&i.image?.every(n=>Math.abs(n-16)<.2)),`${name} ${size[0]} preserves 21px frame and 16px application/account glyph`);check(facts.rows.every(h=>Math.abs(h-32)<.2),`${name} ${size[0]} keeps 32px compact row height`);await capture(`${size[0]}-${name}`);}}
  if(desktop){await viewport(1380,860);await post('window/zoom-reset',{});const base=await page.evaluate(()=>devicePixelRatio);await post('window/zoom-in',{});await until(async()=>(await get('desktop/window-state')).zoomFactor===1.25&&Math.abs((await page.evaluate(()=>devicePixelRatio))/base-1.25)<.02,'actual Electron 125% zoom');for(const name of ['进程','详细信息','应用历史记录','启动应用','服务','用户','性能']){await nav(name);await delay(250);if(!['用户','性能'].includes(name))await decoded(page.locator('.data-table'),`${name} 125% screenshot waits for actual decoded PNG`);check(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),`${name} actual Electron 125% has no document overflow`);await capture(`125-${name}`);}const window=windows('Takao').find(w=>tree.some(p=>p.pid===w.pid));nativeUi(window,'capture',path.join(output,'desktop-125-native.png'));screenshots.push('desktop-125-native.png');await post('window/zoom-reset',{});}
  await nav('进程');await page.getByRole('searchbox').fill(String(own.pid));await decoded(page.locator(`tr[data-row-key="${own.identity}"]`),'same visible identity still decodes after all navigation');const before=await page.evaluate(()=>window.__iconAcceptance.requests);await delay(3500);const after=await page.evaluate(()=>window.__iconAcceptance.requests);check(after-before<=1,'real per-second snapshots do not repeatedly refetch the unchanged visible icon');
  await page.getByRole('searchbox').fill('');for(const name of ['进程','应用历史记录','服务','详细信息']){await nav(name);for(const fraction of [1,.5,0]){await page.locator('.table-scroll').evaluate((el,f)=>el.scrollTop=f*(el.scrollHeight-el.clientHeight),fraction);await delay(180);}}
  await nav('设置');const stats=await page.evaluate(()=>{const s=window.__iconAcceptance;return{...s,live:s.live.length};});check(stats.maximumInflight<=2,'real icon fetch concurrency stays at two across rapid scroll/page changes');check(stats.maximumLive<=512&&stats.live<=512,'observable live Blob URLs stay within 512 entries');coverage.blob=stats;
  await page.reload();const unload=await page.evaluate(()=>JSON.parse(sessionStorage.getItem('icon-acceptance-unload')??'null'));check(unload?.created>0&&unload.remaining===0&&unload.revoked>=unload.created,'window unload revokes every observed Blob URL');coverage.unload=unload;
  await post('history/reset',{});await icon('history',past.identity,204);check(writes.length===0,'read-only suite sends no service-control or IFEO writes');check(errors.length===0,'no browser runtime errors');check(cspErrors.length===0,'no CSP violations');coverage.readOnlyWrites=writes.length;
} catch(error) { failure={message:error.message,stack:error.stack}; await page?.screenshot({path:path.join(output,`${mode}-failure.png`)}).catch(()=>{}); }
finally {
  tree=[...new Map([...tree,...processTree(host.pid)].map(p=>[`${p.pid}:${p.fileTime}`,p])).values()];if(!workloadTree.length)workloadTree=processTree(workload.pid);
  if(desktop&&page&&!page.isClosed())await post('window/close',{}).catch(()=>{});
  await browser?.close().catch(()=>{});
  for(const child of [historyHost,host])try{await stopIconHost(child);}catch(error){failure??={message:`test Host cleanup: ${error.message}`};}
  if(workload.exitCode===null)workload.kill();
  await delay(700);
  for(const entry of [...historyTree,...workloadTree,...tree].reverse())if(ownedAlive(entry)){try{process.kill(entry.pid);}catch{}}
  await until(async()=>!await portOpen(port)&&!await portOpen(cdp)&&!await portOpen(historyPort),'test-owned listeners released').catch(error=>{failure??={message:error.message};});
  const owned=[...historyTree,...workloadTree,...tree];const remaining=owned.filter(ownedAlive).length;coverage.cleanup={observedOwnedIdentities:owned.length,remainingOwnedIdentities:remaining,listenersReleased:!await portOpen(port)&&!await portOpen(cdp)&&!await portOpen(historyPort)};if(remaining||!coverage.cleanup.listenersReleased)failure??={message:'test-owned process identities or listeners remain'};
  // Remove exactly the unique .lnk this run created, after verifying its target.
  ps(`$p=${quote(shortcut)}; if(Test-Path -LiteralPath $p){$w=New-Object -ComObject WScript.Shell;$s=$w.CreateShortcut($p);if($s.TargetPath -ne ${quote(hostExe)}){throw 'Startup fixture target changed'};Remove-Item -LiteralPath $p -Force}`);
  await rm(temporary,{recursive:true,force:true});
  const report={result:failure?'FAIL':'PASS',mode,assertions:checks.length,checks,networkFacts,coverage,apiFacts,geometry,screenshots,errors,cspErrors,failure,source:'real production Release UI → HTTP → JSON.NET → CsWin32; independent Windows probe; no mocked data'};
  await writeFile(path.join(output,`${mode}-results.json`),JSON.stringify(report,null,2));console.log(JSON.stringify({result:report.result,mode,assertions:checks.length,networkFacts,coverage,failure},null,2));if(failure)process.exitCode=1;
}
