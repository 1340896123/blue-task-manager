import { useEffect, useLayoutEffect, useRef, useState } from 'react';
export type MenuItem = { label: string; action: () => void; disabled?: boolean; reason?: string; danger?: boolean };
export default function ContextMenu({ x, y, items, onClose, label }: { x: number; y: number; items: MenuItem[]; onClose: () => void; label: string }) {
  const menu = useRef<HTMLDivElement>(null); const [position, setPosition] = useState({ x, y });
  const close = useRef(onClose); close.current = onClose;
  useLayoutEffect(() => { const box = menu.current!.getBoundingClientRect(); setPosition({ x: Math.max(0, Math.min(x, innerWidth - box.width)), y: Math.max(0, Math.min(y, innerHeight - box.height)) }); }, [x, y]);
  useEffect(() => {
    const element = menu.current!;
    const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    let restoreFocus = true;
    element.querySelector<HTMLButtonElement>('button:not(:disabled)')?.focus();
    const click = (event: PointerEvent) => { if (!element.contains(event.target as Node)) { restoreFocus = false; close.current(); } };
    const blur = () => { restoreFocus = false; close.current(); };
    const scroll = (event: Event) => { if (!element.contains(event.target as Node)) close.current(); };
    const resize = () => close.current();
    document.addEventListener('pointerdown', click); document.addEventListener('scroll', scroll, true);
    window.addEventListener('blur', blur); window.addEventListener('resize', resize); window.visualViewport?.addEventListener('resize', resize);
    return () => {
      document.removeEventListener('pointerdown', click); document.removeEventListener('scroll', scroll, true);
      window.removeEventListener('blur', blur); window.removeEventListener('resize', resize); window.visualViewport?.removeEventListener('resize', resize);
      if (restoreFocus && (document.activeElement === document.body || element.contains(document.activeElement))) {
        if (previous?.isConnected) previous.focus({ preventScroll: true });
        else document.querySelector<HTMLElement>('.table-scroll')?.focus({ preventScroll: true });
      }
    };
  }, []);
  return <div ref={menu} className="context-menu" role="menu" aria-label={label} style={{ left: position.x, top: position.y }} onKeyDown={event => {
    const buttons = [...menu.current!.querySelectorAll<HTMLButtonElement>('button:not(:disabled)')]; const current = buttons.indexOf(document.activeElement as HTMLButtonElement);
    if (['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) { event.preventDefault(); const index = event.key === 'Home' ? 0 : event.key === 'End' ? buttons.length - 1 : (current + (event.key === 'ArrowDown' ? 1 : -1) + buttons.length) % buttons.length; buttons[index]?.focus(); }
    if (event.key === 'Escape' || event.key === 'Tab') { event.preventDefault(); event.stopPropagation(); onClose(); }
  }}>{items.map(item => <button key={item.label} role="menuitem" className={item.danger ? 'danger-text' : ''} disabled={item.disabled} title={item.reason} onClick={() => { onClose(); item.action(); }}>{item.label}</button>)}</div>;
}
