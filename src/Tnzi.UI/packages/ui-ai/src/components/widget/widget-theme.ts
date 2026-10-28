/**
 * The theme a widget is handed: whether the page is light or dark, and the CSS variables a widget
 * styles itself with (the names a model is told about in the tool's description, in the MCP Apps
 * style: `--color-text-primary`, `--font-sans`, …). The values are read from this package's own
 * tokens (`--tnzi-ai-*`) where the widget is shown, so a retinted product reaches its widgets too;
 * a page without those tokens gets the defaults below.
 */

export type WidgetColorScheme = 'light' | 'dark';

export interface WidgetTheme {
  readonly scheme: WidgetColorScheme;
  /** CSS custom properties, by name (`--color-text-primary` → `#34322d`). */
  readonly variables: Readonly<Record<string, string>>;
}

/** Each widget variable: the package token it is read from, and its value when there is none. */
const SOURCES: Readonly<Record<string, readonly [token: string | null, light: string, dark: string]>> = {
  '--color-background-primary': ['--tnzi-ai-surface', '#ffffff', '#1c1c1b'],
  '--color-background-secondary': ['--tnzi-ai-bg', '#f8f8f7', '#141414'],
  '--color-text-primary': ['--tnzi-ai-text', '#34322d', '#ededec'],
  '--color-text-secondary': ['--tnzi-ai-text-secondary', 'rgba(52, 50, 45, 0.72)', 'rgba(237, 237, 236, 0.72)'],
  '--color-text-tertiary': ['--tnzi-ai-text-tertiary', 'rgba(52, 50, 45, 0.48)', 'rgba(237, 237, 236, 0.48)'],
  '--color-border-primary': ['--tnzi-ai-border-strong', 'rgba(0, 0, 0, 0.20)', 'rgba(255, 255, 255, 0.20)'],
  '--color-border-secondary': ['--tnzi-ai-border', 'rgba(0, 0, 0, 0.08)', 'rgba(255, 255, 255, 0.08)'],
  '--color-accent': ['--tnzi-ai-accent', '#c96442', '#d97757'],
  '--color-on-accent': ['--tnzi-ai-on-accent', '#ffffff', '#ffffff'],
  '--color-success': ['--tnzi-ai-success', '#16a34a', '#22c55e'],
  '--color-warning': ['--tnzi-ai-warning', '#d97706', '#f59e0b'],
  '--color-danger': ['--tnzi-ai-danger', '#dc2626', '#ef4444'],
  '--color-info': ['--tnzi-ai-info', '#2563eb', '#3b82f6'],
  '--font-sans': [
    '--tnzi-ai-font-body',
    "-apple-system, BlinkMacSystemFont, 'Segoe UI', Helvetica, Arial, sans-serif",
    "-apple-system, BlinkMacSystemFont, 'Segoe UI', Helvetica, Arial, sans-serif",
  ],
  '--font-mono': [
    '--tnzi-ai-font-mono',
    "ui-monospace, 'SF Mono', 'Cascadia Mono', Menlo, Consolas, monospace",
    "ui-monospace, 'SF Mono', 'Cascadia Mono', Menlo, Consolas, monospace",
  ],
  '--border-radius-sm': [null, '6px', '6px'],
  '--border-radius-md': [null, '10px', '10px'],
  '--border-radius-lg': [null, '14px', '14px'],
};

/** The variable names a widget can use. */
export const WIDGET_THEME_VARIABLES: readonly string[] = Object.freeze(Object.keys(SOURCES));

/** Dark mode is a `.dark` class (or `data-theme="dark"`) on an ancestor, as for the rest of the package. */
export function widgetColorScheme(element: Element | null | undefined): WidgetColorScheme {
  const target = element ?? (typeof document === 'undefined' ? null : document.documentElement);
  return target?.closest('.dark, [data-theme="dark"]') ? 'dark' : 'light';
}

/** The theme for a widget shown inside `element` (the page's root when omitted). */
export function readWidgetTheme(element?: Element | null): WidgetTheme {
  const target = element ?? (typeof document === 'undefined' ? null : document.documentElement);
  const scheme = widgetColorScheme(target);
  const style = target && typeof getComputedStyle === 'function' ? getComputedStyle(target) : null;
  const variables: Record<string, string> = {};
  for (const [name, [token, light, dark]] of Object.entries(SOURCES)) {
    const value = token && style ? style.getPropertyValue(token).trim() : '';
    variables[name] = value || (scheme === 'dark' ? dark : light);
  }
  return { scheme, variables };
}

/** Whether two themes would look the same to a widget. */
export function sameWidgetTheme(a: WidgetTheme, b: WidgetTheme): boolean {
  if (a.scheme !== b.scheme) return false;
  return WIDGET_THEME_VARIABLES.every((name) => a.variables[name] === b.variables[name]);
}

/**
 * Calls `onChange` when the page may have switched theme (a class, `data-theme` or inline style
 * changed on `<html>` or `<body>`). Returns the function that stops watching.
 */
export function watchWidgetTheme(onChange: () => void): () => void {
  if (typeof MutationObserver === 'undefined' || typeof document === 'undefined') return () => {};
  const observer = new MutationObserver(() => onChange());
  const options: MutationObserverInit = { attributes: true, attributeFilter: ['class', 'data-theme', 'style'] };
  observer.observe(document.documentElement, options);
  if (document.body) observer.observe(document.body, options);
  return () => observer.disconnect();
}
