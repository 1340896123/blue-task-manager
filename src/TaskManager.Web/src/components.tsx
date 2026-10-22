import { useEffect, useId, useRef, type CSSProperties, type ReactNode, type KeyboardEvent, type MouseEvent } from 'react';
import { useVirtualizer } from '@tanstack/react-virtual';
import { ArrowDown, ArrowUp, CircleHelp, X } from 'lucide-react';

export function MissingValue({ reason }: { reason: string }) {
  return <span className="missing-value" tabIndex={0} aria-label={`不可用：${reason}`} data-reason={reason} title={reason}>—</span>;
}
export function Value({ value, reason }: { value: ReactNode; reason?: string }) {
  return reason ? <MissingValue reason={reason} /> : <>{value}</>;
}
export function Empty({ title = '没有匹配的结果', description = '试试其他名称、发布者或 PID，或清空搜索。' }: { title?: string; description?: string }) {
  return <div className="empty-state"><CircleHelp size={28} aria-hidden="true"/><h2>{title}</h2><p>{description}</p></div>;
}
export type Column<T> = {
  key: string; label: string; width: number; numeric?: boolean; sortable?: boolean;
  metric?: ReactNode; title?: string; render: (row: T) => ReactNode; cellStyle?: (row: T) => CSSProperties;
};
export type Sort = { key: string; direction: 'ascending' | 'descending' };
export const cssNumber = (token: string, fallback: number) => Number.parseFloat(getComputedStyle(document.documentElement).getPropertyValue(token)) || fallback;
export function DataTable<T>({ rows, columns, rowKey, label, rowHeight, sort, onSort, selected, onSelect, emptyDescription, onActivate, onContextMenu, onRowKey, initialScrollTop = 0, onScrollTop, revealKey, getHierarchy }: {
  rows: T[]; columns: Column<T>[]; rowKey: (row: T) => string; label: string; rowHeight: number;
  sort?: Sort; onSort?: (key: string) => void; selected?: string | null; onSelect?: (row: T) => void; emptyDescription?: string;
  onActivate?: (row: T) => void; onContextMenu?: (row: T, event: MouseEvent | KeyboardEvent) => void; onRowKey?: (row: T, event: KeyboardEvent) => void;
  initialScrollTop?: number; onScrollTop?: (top: number) => void; revealKey?: string | null;
  getHierarchy?: (row: T) => { level: number; expanded?: boolean };
}) {
  const scroll = useRef<HTMLDivElement>(null);
  const headerHeight = cssNumber('--height-table-header', 58);
  const restored = useRef(false);
  const virtual = useVirtualizer({ count: rows.length, getScrollElement: () => scroll.current, estimateSize: () => rowHeight, overscan: 8, scrollMargin: headerHeight, getItemKey: index => rowKey(rows[index]) });
  const items = virtual.getVirtualItems();
  const first = items[0]; const last = items[items.length - 1];
  const width = columns.reduce((total, column) => total + column.width, 0);
  useEffect(() => { if (!restored.current) { scroll.current?.scrollTo({ top: initialScrollTop }); restored.current = true; } }, [initialScrollTop]);
  useEffect(() => { if (!revealKey) return; const index = rows.findIndex(row => rowKey(row) === revealKey); if (index >= 0) virtual.scrollToIndex(index, { align: 'center' }); }, [revealKey, rows.length]);
  return <div ref={scroll} className="table-scroll" data-testid="table-scroll" tabIndex={0} aria-label={`${label}可滚动数据区`} onScroll={event => onScrollTop?.(event.currentTarget.scrollTop)}>
    <table className="data-table" role={getHierarchy ? 'treegrid' : undefined} aria-label={label} aria-rowcount={rows.length + 1} style={{ minWidth: width }}>
      <colgroup>{columns.map(column => <col key={column.key} style={{ width: `${column.width / width * 100}%` }}/>)}</colgroup>
      <thead><tr>{columns.map(column => <th key={column.key} scope="col" className={column.numeric ? 'numeric' : ''} title={column.title} aria-sort={column.sortable ? sort?.key === column.key ? sort.direction : 'none' : undefined}>
        {column.sortable ? <button className="sort-button" onClick={() => onSort?.(column.key)} aria-label={`按${column.label}排序`}>
          {column.metric !== undefined && <span className="column-metric">{column.metric}</span>}
          <span className="column-label">{sort?.key === column.key && (sort.direction === 'ascending' ? <ArrowUp size={12} aria-hidden="true"/> : <ArrowDown size={12} aria-hidden="true"/>)}{column.label}</span>
        </button> : <><span className="column-metric">{column.metric ?? '\u00a0'}</span><span className="column-label">{column.label}</span></>}
      </th>)}</tr></thead>
      <tbody>
        {first && first.start > headerHeight && <tr aria-hidden="true"><td colSpan={columns.length} style={{ height: first.start - headerHeight, padding: 0, border: 0 }}/></tr>}
        {items.map(item => { const row = rows[item.index]; const key = rowKey(row); const hierarchy = getHierarchy?.(row); return <tr key={key} data-row-key={key} aria-rowindex={item.index + 2} aria-level={hierarchy?.level} aria-expanded={hierarchy?.expanded} aria-selected={onSelect ? key === selected : undefined} tabIndex={onSelect ? (key === selected || (!selected && item.index === 0) ? 0 : -1) : undefined} className={key === selected ? 'selected' : ''} style={{ height: rowHeight }} onClick={() => onSelect?.(row)} onDoubleClick={() => onActivate?.(row)} onContextMenu={event => { if (onContextMenu) { event.preventDefault(); onSelect?.(row); onContextMenu(row, event); } }} onKeyDown={event => {
          if (!onSelect) return;
          onRowKey?.(row, event); if (event.defaultPrevented) return;
          if (event.key === 'Enter') { event.preventDefault(); onSelect(row); onActivate?.(row); }
          if ((event.key === 'F10' && event.shiftKey) || event.key === 'ContextMenu') { event.preventDefault(); onContextMenu?.(row, event); }
          if (['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) {
            event.preventDefault(); const nextIndex = event.key === 'Home' ? 0 : event.key === 'End' ? rows.length - 1 : Math.max(0, Math.min(rows.length - 1, item.index + (event.key === 'ArrowDown' ? 1 : -1)));
            virtual.scrollToIndex(nextIndex); onSelect(rows[nextIndex]);
            requestAnimationFrame(() => scroll.current?.querySelector<HTMLElement>(`tr[data-row-key="${CSS.escape(rowKey(rows[nextIndex]))}"]`)?.focus());
          }
        }}>{columns.map(column => <td key={column.key} className={column.numeric ? 'numeric' : ''} style={column.cellStyle?.(row)}>{column.render(row)}</td>)}</tr>; })}
        {last && <tr aria-hidden="true"><td colSpan={columns.length} style={{ height: Math.max(0, virtual.getTotalSize() - (last.end - headerHeight)), padding: 0, border: 0 }}/></tr>}
      </tbody>
    </table>
    {rows.length === 0 && <Empty description={emptyDescription}/>}
  </div>;
}
export function Modal({ title, description, children, onClose, busy = false, wide = false }: { title: string; description?: string; children: ReactNode; onClose: () => void; busy?: boolean; wide?: boolean }) {
  const dialog = useRef<HTMLDialogElement>(null); const titleId = useId(); const descriptionId = useId();
  useEffect(() => {
    const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const node = dialog.current!; node.showModal();
    return () => { node.close(); previous?.focus(); };
  }, []);
  return <dialog ref={dialog} className={`modal ${wide ? 'modal-wide' : ''}`} aria-labelledby={titleId} aria-describedby={description ? descriptionId : undefined} onCancel={event => { event.preventDefault(); if (!busy) onClose(); }} onKeyDown={event => {
    if (event.key !== 'Tab') return;
    const focusable = Array.from(dialog.current!.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(:disabled), select:not(:disabled), [tabindex="0"]'));
    const first = focusable[0]; const last = focusable[focusable.length - 1];
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus(); }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); }
  }}>
    <button className="icon-button modal-close" aria-label="关闭对话框" onClick={onClose} disabled={busy}><X size={18} aria-hidden="true"/></button>
    <h2 id={titleId}>{title}</h2>{description && <p className="modal-description" id={descriptionId}>{description}</p>}{children}
  </dialog>;
}
