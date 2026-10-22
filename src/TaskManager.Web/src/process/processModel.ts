import type { Process, PageFilters } from '../types';
import type { Sort } from '../components';
import { matches } from '../format';
export type ProcessRow = { process: Process; depth: number; context: boolean; parent: string | null; hasChildren: boolean };
export const processIo = (p: Process) => p.ioReadBytesPerSecond === null || p.ioWriteBytesPerSecond === null ? null : p.ioReadBytesPerSecond + p.ioWriteBytesPerSecond;
export function compareProcess(sort: Sort) {
  const value = (p: Process) => sort.key === 'io' ? processIo(p) : p[sort.key as keyof Process];
  return (a: Process, b: Process) => {
    const x = value(a); const y = value(b);
    if (x == null) return y == null ? a.pid - b.pid : 1;
    if (y == null) return -1;
    const n = typeof x === 'string' && typeof y === 'string' ? x.localeCompare(y, 'zh-CN', { numeric: true }) : x < y ? -1 : x > y ? 1 : 0;
    return (sort.direction === 'ascending' ? n : -n) || a.pid - b.pid;
  };
}
export function processRows(processes: Process[], query: string, filters: PageFilters | null, currentUser: string | undefined, sort: Sort, tree: boolean, collapsed: Set<string>) {
  const byPid = new Map(processes.map(p => [p.pid, p]));
  const parents = new Map<string, string>(); const children = new Map<string, Process[]>();
  for (const p of processes) {
    const parent = byPid.get(p.parentPid);
    if (!parent || parent === p || !parent.creationTimeFileTime || !p.creationTimeFileTime || BigInt(parent.creationTimeFileTime) > BigInt(p.creationTimeFileTime)) continue;
    // Defensive cycle detection; a malformed/native racing relationship is shown as a root.
    const seen = new Set([p.identity]); let cursor: string | undefined = parent.identity;
    while (cursor && !seen.has(cursor)) { seen.add(cursor); cursor = parents.get(cursor); }
    if (cursor) continue;
    parents.set(p.identity, parent.identity);
    const list = children.get(parent.identity) ?? []; list.push(p); children.set(parent.identity, list);
  }
  const q = query.trim(); const numeric = /^\d+$/.test(q);
  const direct = new Set(processes.filter(p => (!numeric ? matches(q, p.name, p.pid, p.path, p.publisher, p.userName) : p.pid === Number(q)) && (!filters?.currentUser || (currentUser && p.userName?.toLocaleLowerCase() === currentUser.toLocaleLowerCase())) && (filters?.sessionId == null || p.sessionId === filters.sessionId) && (!filters?.state || filters.state === 'all' || p.state === filters.state)).map(p => p.identity));
  const compare = compareProcess(sort);
  if (!tree) return { rows: processes.filter(p => direct.has(p.identity)).sort(compare).map(process => ({ process, depth: 0, context: false, parent: parents.get(process.identity) ?? null, hasChildren: false })), parents, matched: direct.size };
  const included = new Set(direct);
  for (const id of direct) { let parent = parents.get(id); const seen = new Set<string>(); while (parent && !seen.has(parent)) { included.add(parent); seen.add(parent); parent = parents.get(parent); } }
  const rows: ProcessRow[] = []; const visited = new Set<string>(); const searching = Boolean(q || filters?.currentUser || filters?.sessionId != null || (filters?.state && filters.state !== 'all'));
  const visit = (p: Process, depth: number) => {
    if (visited.has(p.identity) || !included.has(p.identity)) return; visited.add(p.identity);
    const nodes = (children.get(p.identity) ?? []).filter(c => included.has(c.identity)).sort(compare);
    rows.push({ process: p, depth, context: !direct.has(p.identity), parent: parents.get(p.identity) ?? null, hasChildren: nodes.length > 0 });
    if (!collapsed.has(p.identity) || searching) for (const c of nodes) visit(c, depth + 1);
  };
  for (const p of processes.filter(p => !parents.has(p.identity)).sort(compare)) visit(p, 0);
  return { rows, parents, matched: direct.size };
}
const csvCell = (value: unknown) => {
  let text = value == null ? '' : String(value);
  if (/^[\s\uFEFF]*[=+\-@]/.test(text)) text = `'${text}`;
  return `"${text.replaceAll('"', '""')}"`;
};
export function exportProcesses(rows: ProcessRow[], capturedAtUtc: string, tree: boolean) {
  const headers = ['采样时刻 UTC', '树上下文行', '树层级', '名称', 'PID', '父PID', '创建FILETIME', '状态', 'CPU (%)', '工作集 (bytes)', '专用提交 (bytes)', 'I/O读取 (bytes/sec)', 'I/O写入 (bytes/sec)', 'GPU (%)', 'GPU采样时刻 UTC', 'GPU引擎', '用户', '路径', '字段不可用原因'];
  const records = rows.map(({ process: p, depth, context }) => [capturedAtUtc, tree ? context ? '祖先上下文' : '匹配' : '', tree ? depth : '', p.name, p.pid, p.parentPid, p.creationTimeFileTime, p.state, p.cpuPercent, p.workingSetBytes, p.privateBytes, p.ioReadBytesPerSecond, p.ioWriteBytesPerSecond, p.gpuPercent, p.gpuCapturedAtUtc, p.gpuEngine, p.userName, p.path, Object.entries(p.unavailable).map(([k, v]) => `${k}: ${v.message}`).join('; ')]);
  const blob = new Blob(['\uFEFF', [headers, ...records].map(record => record.map(csvCell).join(',')).join('\r\n'), '\r\n'], { type: 'text/csv;charset=utf-8' });
  const url = URL.createObjectURL(blob); const a = document.createElement('a'); a.href = url; a.download = `processes-${capturedAtUtc.replaceAll(':', '-')}.csv`; a.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
}
