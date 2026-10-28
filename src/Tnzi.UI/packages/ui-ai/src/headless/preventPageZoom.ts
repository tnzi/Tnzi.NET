/**
 * Turn off the browser's page zoom (pinch, double-tap, zoom on focusing a
 * small-text field), so a chat app on a phone behaves like an app rather than
 * a page that drifts when a thumb lands wrong. Returns a function that puts
 * everything back.
 *
 * Takes four measures, because no single one covers every browser:
 *
 * - the viewport meta gets `maximum-scale=1, user-scalable=no` (one is created
 *   with `width=device-width, initial-scale=1` when the page has none). Chrome
 *   on Android honours it; it also stops iOS zooming into a field whose text is
 *   under 16px when it is focused;
 * - `touch-action: pan-x pan-y` on the root element: scrolling stays, pinch
 *   and double-tap zoom go, where the browser follows `touch-action`;
 * - `text-size-adjust: 100%`, so rotating a phone does not inflate the text;
 * - Safari's `gesturestart` / `gesturechange` / `gestureend` are cancelled: iOS
 *   Safari ignores both the meta and `touch-action` for pinch, and this is the
 *   only thing that stops it.
 *
 * Opt-in on purpose: page zoom is how some people read small text (WCAG 1.4.4),
 * and whether an app gives it up is the product's decision.
 *
 * Zooming inside a component is unaffected. Cancelling the default of a
 * gesture event does not stop it reaching listeners, so a viewer that handles
 * pinch itself (pointer or touch events, or Safari's gesture events) on an
 * element with `touch-action: none` still gets every event; only the browser's
 * own page zoom is blocked. A viewer that relies on the browser zooming the
 * page cannot work while this is on.
 */
export function preventPageZoom(): () => void {
  if (typeof document === 'undefined') return () => {};

  const root = document.documentElement;
  const rootStyle = root.style as CSSStyleDeclaration & { webkitTextSizeAdjust?: string };
  const previousStyle = {
    touchAction: rootStyle.touchAction,
    textSizeAdjust: rootStyle.getPropertyValue('text-size-adjust'),
    webkitTextSizeAdjust: rootStyle.getPropertyValue('-webkit-text-size-adjust'),
  };
  rootStyle.touchAction = 'pan-x pan-y';
  rootStyle.setProperty('text-size-adjust', '100%');
  rootStyle.setProperty('-webkit-text-size-adjust', '100%');

  let meta = document.querySelector<HTMLMetaElement>('meta[name="viewport"]');
  const created = meta === null;
  if (!meta) {
    meta = document.createElement('meta');
    meta.name = 'viewport';
    meta.content = 'width=device-width, initial-scale=1';
    document.head.appendChild(meta);
  }
  const previousContent = meta.content;
  meta.content = withViewportValues(previousContent, { 'maximum-scale': '1', 'user-scalable': 'no' });

  const cancel = (event: Event): void => event.preventDefault();
  const GESTURES = ['gesturestart', 'gesturechange', 'gestureend'] as const;
  for (const type of GESTURES) document.addEventListener(type, cancel, { passive: false });

  let restored = false;
  return () => {
    if (restored) return;
    restored = true;
    for (const type of GESTURES) document.removeEventListener(type, cancel);
    if (created) meta!.remove();
    else meta!.content = previousContent;
    rootStyle.touchAction = previousStyle.touchAction;
    restoreProperty(rootStyle, 'text-size-adjust', previousStyle.textSizeAdjust);
    restoreProperty(rootStyle, '-webkit-text-size-adjust', previousStyle.webkitTextSizeAdjust);
  };
}

function restoreProperty(style: CSSStyleDeclaration, name: string, value: string): void {
  if (value) style.setProperty(name, value);
  else style.removeProperty(name);
}

/** A viewport `content` with `values` set, keeping every other key and their order. */
export function withViewportValues(content: string, values: Readonly<Record<string, string>>): string {
  const entries = content
    .split(',')
    .map((part) => part.trim())
    .filter(Boolean)
    .map((part) => {
      const [key = '', ...rest] = part.split('=');
      return [key.trim().toLowerCase(), rest.join('=').trim()] as const;
    });
  const out = entries.map(([key, value]) => (key in values ? [key, values[key]!] : [key, value]));
  for (const [key, value] of Object.entries(values)) {
    if (!entries.some(([k]) => k === key)) out.push([key, value]);
  }
  return out.map(([key, value]) => (value ? `${key}=${value}` : key)).join(', ');
}
