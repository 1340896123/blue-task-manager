import { useEffect, useLayoutEffect, useRef, useState } from 'react';
export type MenuItem = { label: string; action: () => void; disabled?: boolean; reason?: string; danger?: boolean };
export default function ContextMenu({ x, y, items, onClose, label }: { x: number; y: number; items: MenuItem[]; onClose: () => void; label: string }) {
  const menu = useRef<HTMLDivElement>(null); const [position, setPosition] = useState({ x, y });
  useLayoutEffect(() => { const box = menu.current!.getBoundingClientRect(); setPosition({ x: Math.max(0, Math.min(x, innerWidth - box.width)), y: Math.max(0, Math.min(y, innerHeight - box.height)) }); }, [x, y]);
  useEffect(() => {
    const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    menu.current?.querySelector<HTMLButtonElement>('button:not(:disabled)')?.focus();
    const click = (event: PointerEvent) => { if (!menu.current?.contains(event.target as Node)) onClose(); };
    const blur = () => onClose();
    document.addEventListener('pointerdown', click); window.addEventListener('blur', blur);
    return () => { document.removeEventListener('pointerdown', click); window.removeEventListener('blur', blur); if (previous?.isConnected) previous.focus({ preventScroll: true }); else document.querySelector<HTMLElement>('.table-scroll')?.focus({ preventScroll: true }); };
  }, []);
  return <div ref={menu} className="context-menu" role="menu" aria-label={label} style={{ left: position.x, top: position.y }} onKeyDown={event => {
    const buttons = [...menu.current!.querySelectorAll<HTMLButtonElement>('button:not(:disabled)')]; const current = buttons.indexOf(document.activeElement as HTMLButtonElement);
    if (['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) { event.preventDefault(); const index = event.key === 'Home' ? 0 : event.key === 'End' ? buttons.length - 1 : (current + (event.key === 'ArrowDown' ? 1 : -1) + buttons.length) % buttons.length; buttons[index]?.focus(); }
    if (event.key === 'Escape' || event.key === 'Tab') { event.preventDefault(); onClose(); }
  }}>{items.map(item => <button key={item.label} role="menuitem" className={item.danger ? 'danger-text' : ''} disabled={item.disabled} title={item.reason} onClick={() => { onClose(); item.action(); }}>{item.label}</button>)}</div>;
}
