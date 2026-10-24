import { useEffect, useRef, useState } from 'react';
import { AppWindow } from 'lucide-react';
import { cachedIcon, iconKey, rejectIcon, subscribeIcon, type IconTarget } from './applicationIcons';

export default function ApplicationIcon({ kind, id, revision }: IconTarget) {
  const key = iconKey({ kind, id, revision });
  const element = useRef<HTMLSpanElement>(null);
  const [image, setImage] = useState({ key, url: cachedIcon(key) });
  useEffect(() => {
    if (!id) return;
    const subscribe = () => subscribeIcon({ kind, id, revision }, url => setImage({ key, url }));
    if (!element.current || typeof IntersectionObserver === 'undefined') return subscribe();
    let unsubscribe: (() => void) | undefined;
    const observer = new IntersectionObserver(changes => {
      if (changes.some(change => change.isIntersecting)) unsubscribe ??= subscribe();
      else { unsubscribe?.(); unsubscribe = undefined; }
    }, { rootMargin: '128px' });
    observer.observe(element.current);
    return () => { observer.disconnect(); unsubscribe?.(); };
  }, [kind, id, revision, key]);
  const url = image.key === key ? image.url : cachedIcon(key);
  return <span ref={element} className="process-icon" aria-hidden="true">{url ? <img src={url} alt="" draggable={false} onError={() => rejectIcon(key, url)}/> : <AppWindow/>}</span>;
}
