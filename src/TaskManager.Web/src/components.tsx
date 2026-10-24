import { useEffect, useId, useRef, useState, type CSSProperties, type ReactNode, type KeyboardEvent, type MouseEvent, type PointerEvent, type RefObject } from 'react';
import { useVirtualizer } from '@tanstack/react-virtual';
import { ArrowDown, ArrowUp, ChevronsLeftRight, CircleHelp, MoreHorizontal, X } from 'lucide-react';

export function PageHeader({ title, children }: { title: string; children?: ReactNode }) {
  return <div className="toolbar"><div className="page-title"><h1>{title}</h1></div><div className="toolbar-actions">{children}</div></div>;
}
/** Native disclosure keeps controls keyboard reachable, including when there is no row selection. */
export function CommandMenu({ label = '更多操作', children }: { label?: string; children: ReactNode }) {
  const disclosure = useRef<HTMLDetailsElement>(null);
  useEffect(() => {
    const close = (event: globalThis.PointerEvent) => { if (!disclosure.current?.contains(event.target as Node)) disclosure.current?.removeAttribute('open'); };
    document.addEventListener('pointerdown', close);
    return () => document.removeEventListener('pointerdown', close);
  }, []);
  return <details ref={disclosure} className="command-menu" onKeyDown={event => {
    if (event.key === 'Escape' && disclosure.current?.open) { event.preventDefault(); event.stopPropagation(); disclosure.current.open = false; disclosure.current.querySelector('summary')?.focus(); }
  }}><summary className="command-button" aria-label={label}><MoreHorizontal size={18} aria-hidden="true"/><span>更多</span></summary><div className="command-menu-content" aria-label={label} onClick={event => { if (event.target instanceof Element && event.target.closest('[data-menu-close]') && disclosure.current) { disclosure.current.open = false; disclosure.current.querySelector('summary')?.focus(); } }}>{children}</div></details>;
}

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
  key: string; label: string; width: number; minWidth?: number; numeric?: boolean; sortable?: boolean;
  metric?: ReactNode; title?: string; render: (row: T) => ReactNode; cellStyle?: (row: T) => CSSProperties;
};
export type Sort = { key: string; direction: 'ascending' | 'descending' };
export const cssNumber = (token: string, fallback: number) => Number.parseFloat(getComputedStyle(document.documentElement).getPropertyValue(token)) || fallback;
export function DataTable<T>({ rows, columns, rowKey, label, rowHeight, sort, onSort, selected, onSelect, emptyDescription, emptyTitle, onActivate, onContextMenu, onRowKey, initialScrollTop = 0, onScrollTop, revealKey, scrollResetKey, getHierarchy, columnWidths, onColumnWidthsChange, metricHeader = false }: {
  rows: T[]; columns: Column<T>[]; rowKey: (row: T) => string; label: string; rowHeight: number;
  sort?: Sort; onSort?: (key: string) => void; selected?: string | null; onSelect?: (row: T) => void; emptyDescription?: string; emptyTitle?: string;
  onActivate?: (row: T) => void; onContextMenu?: (row: T, event: MouseEvent | KeyboardEvent) => void; onRowKey?: (row: T, event: KeyboardEvent) => void;
  initialScrollTop?: number; onScrollTop?: (top: number) => void; revealKey?: string | null; scrollResetKey?: string;
  getHierarchy?: (row: T) => { level: number; expanded?: boolean };
  columnWidths?: Record<string, number> | null; onColumnWidthsChange?: (widths: Record<string, number>) => void;
  metricHeader?: boolean;
}) {
  const scroll = useRef<HTMLDivElement>(null);
  const [widths, setWidths] = useState<Record<string, number>>(columnWidths ?? {});
  const widthValues = useRef(widths);
  const saveWidths = useRef(onColumnWidthsChange); saveWidths.current = onColumnWidthsChange;
  const [resizing, setResizing] = useState<string | null>(null);
  const drag = useRef<{ key: string; pointer: number; startX: number; startWidth: number; before: Record<string, number>; element: HTMLDivElement } | null>(null);
  const [widthEditor, setWidthEditor] = useState<{ key: string; value: string } | null>(null);
  const widthMin = cssNumber('--width-column-min', 64), widthMax = cssNumber('--width-column-max', 1600);
  const minimum = (column: Column<T>) => Math.max(widthMin, column.minWidth ?? widthMin);
  const clamp = (column: Column<T>, value: number) => Math.round(Math.max(minimum(column), Math.min(widthMax, value)));
  const columnWidth = (column: Column<T>) => clamp(column, Number.isFinite(widths[column.key]) ? widths[column.key] : column.width);
  const publishWidths = (next: Record<string, number>, persist = false) => {
    widthValues.current = next; setWidths(next); if (persist) saveWidths.current?.(next);
  };
  const releaseDrag = (cancel: boolean) => {
    const active = drag.current; if (!active) return;
    drag.current = null; document.body.classList.remove('resizing-columns'); setResizing(null);
    if (active.element.hasPointerCapture(active.pointer)) active.element.releasePointerCapture(active.pointer);
    if (cancel) publishWidths(active.before);
    else if (widthValues.current !== active.before) saveWidths.current?.(widthValues.current);
  };
  // Live row refreshes leave the local gesture intact. Only actual preference changes reset it.
  const widthSignature = JSON.stringify(columnWidths ?? {});
  useEffect(() => { releaseDrag(true); publishWidths(columnWidths ?? {}); }, [widthSignature]);
  useEffect(() => {
    const cancel = () => releaseDrag(true); window.addEventListener('blur', cancel);
    return () => {
      window.removeEventListener('blur', cancel);
      const active = drag.current; drag.current = null; document.body.classList.remove('resizing-columns');
      if (active?.element.hasPointerCapture(active.pointer)) active.element.releasePointerCapture(active.pointer);
    };
  }, []);
  const setColumnWidth = (column: Column<T>, value: number | null) => {
    const next = { ...widthValues.current }; if (value === null) delete next[column.key]; else next[column.key] = clamp(column, value);
    publishWidths(next, true);
  };
  const startResize = (column: Column<T>, event: PointerEvent<HTMLDivElement>) => {
    if (event.button !== 0 || !event.isPrimary) return;
    event.preventDefault(); event.stopPropagation(); releaseDrag(true);
    event.currentTarget.focus(); event.currentTarget.setPointerCapture(event.pointerId);
    drag.current = { key: column.key, pointer: event.pointerId, startX: event.clientX, startWidth: columnWidth(column), before: widthValues.current, element: event.currentTarget };
    setResizing(column.key); document.body.classList.add('resizing-columns');
  };
  const editorColumn = columns.find(column => column.key === widthEditor?.key);
  const editorWidth = widthEditor ? Number(widthEditor.value) : NaN;
  const validEditor = editorColumn && widthEditor?.value.trim() !== '' && Number.isFinite(editorWidth) && editorWidth >= minimum(editorColumn) && editorWidth <= widthMax;
  const headerHeight = cssNumber(metricHeader ? '--height-table-header' : '--height-table-header-plain', metricHeader ? 58 : 36);
  const restored = useRef(false);
  const virtual = useVirtualizer({ count: rows.length, getScrollElement: () => scroll.current, estimateSize: () => rowHeight, overscan: 8, scrollMargin: headerHeight, getItemKey: index => rowKey(rows[index]) });
  const items = virtual.getVirtualItems();
  const first = items[0]; const last = items[items.length - 1];
  const width = columns.reduce((total, column) => total + columnWidth(column), 0);
  useEffect(() => { if (!restored.current) { scroll.current?.scrollTo({ top: initialScrollTop }); restored.current = true; } }, [initialScrollTop]);
  const priorReset = useRef(scrollResetKey);
  useEffect(() => { if (priorReset.current !== scrollResetKey) { priorReset.current = scrollResetKey; scroll.current?.scrollTo({ top: 0 }); } }, [scrollResetKey]);
  useEffect(() => { if (!revealKey) return; const index = rows.findIndex(row => rowKey(row) === revealKey); if (index >= 0) virtual.scrollToIndex(index, { align: 'center' }); }, [revealKey, rows.length]);
  return <><div ref={scroll} className={`table-scroll ${metricHeader ? 'metric-header' : 'plain-header'}`} data-testid="table-scroll" tabIndex={0} aria-label={`${label}可滚动数据区`} onScroll={event => onScrollTop?.(event.currentTarget.scrollTop)}>
    <table className="data-table" role={getHierarchy ? 'treegrid' : undefined} aria-label={label} aria-rowcount={rows.length + 1} style={{ width }}>
      <colgroup>{columns.map(column => <col key={column.key} style={{ width: columnWidth(column) }}/>)}</colgroup>
      <thead><tr>{columns.map(column => <th key={column.key} data-column-key={column.key} scope="col" className={column.numeric ? 'numeric' : ''} title={column.title} aria-sort={onSort && column.sortable !== false ? sort?.key === column.key ? sort.direction : 'none' : undefined}>
        {onSort && column.sortable !== false ? <button className="sort-button" onClick={() => { scroll.current?.scrollTo({ top: 0 }); onSort(column.key); }} aria-label={`按${column.label}排序`}>
          {metricHeader && <span className="column-metric">{column.metric ?? '\u00a0'}</span>}
          <span className="column-label">{sort?.key === column.key && (sort.direction === 'ascending' ? <ArrowUp size={12} aria-hidden="true"/> : <ArrowDown size={12} aria-hidden="true"/>)}<span className="column-title">{column.label}</span></span>
        </button> : <>{metricHeader && <span className="column-metric">{column.metric ?? '\u00a0'}</span>}<span className="column-label">{column.label}</span></>}
        <button type="button" className="column-width-button" aria-label={`设置${column.label}列宽`} title={`设置${column.label}列宽`} onClick={() => setWidthEditor({ key: column.key, value: String(columnWidth(column)) })}><ChevronsLeftRight size={12} aria-hidden="true"/></button>
        <div role="separator" aria-orientation="vertical" tabIndex={0} aria-label={`调整${column.label}列宽`} aria-valuemin={minimum(column)} aria-valuemax={widthMax} aria-valuenow={columnWidth(column)} aria-valuetext={`${columnWidth(column)}像素；左右键调整，Shift加速，Home恢复默认`} title="拖动调整列宽；双击恢复默认；左右键调整" className={`column-resizer ${resizing === column.key ? 'is-resizing' : ''}`} onPointerDown={event => startResize(column, event)} onPointerMove={event => {
          const active = drag.current; if (!active || active.pointer !== event.pointerId) return;
          event.preventDefault(); event.stopPropagation(); const nextWidth = clamp(column, active.startWidth + event.clientX - active.startX);
          if (nextWidth !== (widthValues.current[column.key] ?? column.width)) publishWidths({ ...widthValues.current, [column.key]: nextWidth });
        }} onPointerUp={event => { event.stopPropagation(); if (drag.current?.pointer === event.pointerId) releaseDrag(false); }} onPointerCancel={event => { if (drag.current?.pointer === event.pointerId) releaseDrag(true); }} onLostPointerCapture={event => { if (drag.current?.pointer === event.pointerId) releaseDrag(true); }} onClick={event => event.stopPropagation()} onDoubleClick={event => { event.preventDefault(); event.stopPropagation(); setColumnWidth(column, null); }} onKeyDown={event => {
          if (['ArrowLeft', 'ArrowRight', 'Home', 'Enter', ' '].includes(event.key)) {
            event.preventDefault(); event.stopPropagation();
            if (event.key === 'Home') setColumnWidth(column, null);
            else if (event.key === 'Enter' || event.key === ' ') setWidthEditor({ key: column.key, value: String(columnWidth(column)) });
            else setColumnWidth(column, columnWidth(column) + (event.key === 'ArrowRight' ? 1 : -1) * cssNumber(event.shiftKey ? '--step-column-resize-large' : '--step-column-resize', event.shiftKey ? 64 : 16));
          } else if (event.key === 'Escape' && drag.current) { event.preventDefault(); event.stopPropagation(); releaseDrag(true); }
        }}/>
      </th>)}</tr></thead>
      <tbody>
        {first && first.start > headerHeight && <tr aria-hidden="true"><td colSpan={columns.length} style={{ height: first.start - headerHeight, padding: 0, border: 0 }}/></tr>}
        {items.map(item => { const row = rows[item.index]; const key = rowKey(row); const hierarchy = getHierarchy?.(row); return <tr key={key} data-row-key={key} aria-rowindex={item.index + 2} aria-level={hierarchy?.level} aria-expanded={hierarchy?.expanded} aria-selected={onSelect ? key === selected : undefined} tabIndex={onSelect ? (key === selected || (!selected && item.index === 0) ? 0 : -1) : undefined} className={key === selected ? 'selected' : ''} style={{ height: rowHeight }} onClick={() => onSelect?.(row)} onDoubleClick={event => { if (!(event.target instanceof Element && event.target.closest('button,a,input,select,textarea,[role="button"]'))) onActivate?.(row); }} onContextMenu={event => { if (onContextMenu) { event.preventDefault(); onSelect?.(row); onContextMenu(row, event); } }} onKeyDown={event => {
          if (!onSelect || (event.target !== event.currentTarget && event.target instanceof Element && event.target.closest('button,a,input,select,textarea,[role="button"],[role="menuitem"]'))) return;
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
    {rows.length === 0 && <Empty title={emptyTitle} description={emptyDescription}/>}
  </div>{widthEditor && editorColumn && <Modal title={`设置${editorColumn.label}列宽`} description={`允许 ${minimum(editorColumn)}～${widthMax} 像素。也可拖动列头右边界，或聚焦调整柄后使用左右键。`} onClose={() => setWidthEditor(null)}><form onSubmit={event => { event.preventDefault(); if (validEditor) { setColumnWidth(editorColumn, editorWidth); setWidthEditor(null); } }}><label className="column-width-field">列宽（像素）<input autoFocus type="number" min={minimum(editorColumn)} max={widthMax} step="1" value={widthEditor.value} onChange={event => setWidthEditor({ ...widthEditor, value: event.target.value })}/></label><div className="modal-actions"><button type="button" className="command-button bordered" onClick={() => { setColumnWidth(editorColumn, null); setWidthEditor(null); }}>恢复默认宽度</button><button type="button" className="command-button" onClick={() => setWidthEditor(null)}>取消</button><button className="command-button primary" type="submit" disabled={!validEditor}>应用宽度</button></div></form></Modal>}</>;
}
export function Modal({ title, description, children, onClose, busy = false, wide = false, className = '', initialFocusRef }: { title: string; description?: string; children: ReactNode; onClose: () => void; busy?: boolean; wide?: boolean; className?: string; initialFocusRef?: RefObject<HTMLElement | null> }) {
  const dialog = useRef<HTMLDialogElement>(null); const titleId = useId(); const descriptionId = useId();
  useEffect(() => {
    const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const node = dialog.current!; node.showModal(); initialFocusRef?.current?.focus({ preventScroll: true });
    return () => { node.close(); previous?.focus(); };
  }, [initialFocusRef]);
  return <dialog ref={dialog} className={`modal ${wide ? 'modal-wide' : ''} ${className}`} aria-labelledby={titleId} aria-describedby={description ? descriptionId : undefined} onCancel={event => { event.preventDefault(); event.stopPropagation(); if (!busy) onClose(); }} onKeyDown={event => {
    if (event.target instanceof Element && event.target.closest('dialog') !== dialog.current) return;
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
