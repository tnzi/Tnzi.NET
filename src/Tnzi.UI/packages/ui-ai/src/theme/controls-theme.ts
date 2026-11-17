/**
 * Light / dark and the accent colour for a page built from
 * `@tnzi/ui-ai/controls` alone, without the `@tnzi/ui` theme context.
 *
 * The controls read `--tnzi-ai-*` tokens; the accent family of those tokens
 * reads five `--tnzi-*` variables that the `@tnzi/ui` theme context writes
 * whenever the brand colour changes:
 *
 *   --tnzi-primary, --tnzi-primary-rgb   the accent fill
 *   --tnzi-on-primary                    text / icons on that fill
 *   --tnzi-primary-text-on-light / -dark the accent as a readable text colour
 *
 * The controls stylesheet (`controls.css`) ships defaults for the five (teal),
 * and `applyControlsTheme` writes them for any other accent, computed the way
 * the theme context computes them (white on the fill while it reads at 3:1,
 * otherwise a near-black ink; the text colour moved along HSL lightness just
 * far enough to read at 4.5:1). The arithmetic is local and small on purpose:
 * this module may import nothing (gate: `conventions/controls-entry.test.ts`),
 * and `__tests__/theme/controls-theme.test.ts` holds it to the theme
 * context's own results.
 */

export type ControlsThemeMode = 'light' | 'dark' | 'auto';

export interface ControlsThemeOptions {
  /**
   * `light` / `dark` set or clear the `dark` class on the target; `auto`
   * follows the system's `prefers-color-scheme` and keeps following it until
   * the next call for the same target. Omitted leaves the mode as it is.
   */
  mode?: ControlsThemeMode;
  /**
   * The accent as a hex colour (`#0d9488`, `#fd2`). `null` removes an accent
   * set earlier, back to the stylesheet's default. Omitted leaves it as it is.
   */
  accent?: string | null;
}

/** The variables `applyControlsTheme` writes for an accent, in this order. */
export const CONTROLS_ACCENT_VARS = [
  '--tnzi-primary',
  '--tnzi-primary-rgb',
  '--tnzi-on-primary',
  '--tnzi-primary-text-on-light',
  '--tnzi-primary-text-on-dark',
] as const;

/* The same references and thresholds as the `@tnzi/ui` theme: accent text has
   to read on every surface from white down to the light sidebar grey, and on
   every dark one from black up to the dark container. */
const READABLE_ON = { light: '#ebebeb', dark: '#1c1c1c' } as const;
const READABLE_CONTRAST = 4.5;
const MIN_WHITE_ON_FILL = 3;
const TEXT_ON_DARK_FILL = '#ffffff';
const TEXT_ON_LIGHT_FILL = '#141414';
const DARK_QUERY = '(prefers-color-scheme: dark)';

interface Rgb { r: number; g: number; b: number }
interface Hsl { h: number; s: number; l: number }

const HEX = /^#([0-9a-f]{3}|[0-9a-f]{6})$/i;

function parseHex(color: string): Rgb {
  const match = HEX.exec(color.trim());
  if (!match) throw new Error(`applyControlsTheme: "${color}" is not a hex colour (#rgb or #rrggbb)`);
  const digits = match[1]!.length === 3 ? [...match[1]!].map((d) => d + d).join('') : match[1]!;
  return {
    r: parseInt(digits.slice(0, 2), 16),
    g: parseInt(digits.slice(2, 4), 16),
    b: parseInt(digits.slice(4, 6), 16),
  };
}

function toHex({ r, g, b }: Rgb): string {
  return `#${[r, g, b].map((c) => Math.round(Math.min(255, Math.max(0, c))).toString(16).padStart(2, '0')).join('')}`;
}

function linear(channel: number): number {
  const c = channel / 255;
  return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
}

function luminance({ r, g, b }: Rgb): number {
  return 0.2126 * linear(r) + 0.7152 * linear(g) + 0.0722 * linear(b);
}

/** WCAG 2.x contrast ratio, 1 to 21. */
export function contrastRatio(a: string, b: string): number {
  const la = luminance(parseHex(a));
  const lb = luminance(parseHex(b));
  return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05);
}

/* HSL with whole-number channels, as the theme context rounds them. */
function toHsl({ r, g, b }: Rgb): Hsl {
  const max = Math.max(r, g, b);
  const delta = max - Math.min(r, g, b);
  const sector = delta ? (max === r ? (g - b) / delta : max === g ? 2 + (b - r) / delta : 4 + (r - g) / delta) : 0;
  const h = 60 * (sector < 0 ? sector + 6 : sector);
  const sv = max ? (delta / max) * 100 : 0;
  const v = (max / 255) * 100;
  const k = ((200 - sv) * v) / 100;
  const s = k > 0 && k < 200 ? ((sv * v) / 100 / (k <= 100 ? k : 200 - k)) * 100 : 0;
  return { h: Math.round(h) % 360, s: Math.round(s), l: Math.round(k / 2) };
}

function fromHsl({ h, s, l }: Hsl): Rgb {
  const sat = (s * (l < 50 ? l : 100 - l)) / 100;
  const v = (l + sat) / 100;
  const sv = sat > 0 ? (2 * sat) / (l + sat) : 0;
  const sector = (h / 360) * 6;
  const i = Math.floor(sector);
  const f = sector - i;
  const p = v * (1 - sv);
  const q = v * (1 - f * sv);
  const t = v * (1 - (1 - f) * sv);
  const k = i % 6;
  return {
    r: 255 * [v, q, p, p, t, v][k]!,
    g: 255 * [t, v, v, q, p, p][k]!,
    b: 255 * [p, p, t, v, v, q][k]!,
  };
}

/** Text / icons on a fill of `fill`: white while it reads at 3:1, else a near-black ink. */
export function textOnFill(fill: string): string {
  const white = contrastRatio(TEXT_ON_DARK_FILL, fill);
  if (white >= MIN_WHITE_ON_FILL) return TEXT_ON_DARK_FILL;
  return contrastRatio(TEXT_ON_LIGHT_FILL, fill) > white ? TEXT_ON_LIGHT_FILL : TEXT_ON_DARK_FILL;
}

/**
 * `color` as a text colour on `background`: itself when it reads at 4.5:1,
 * otherwise the same hue made darker (light background) or lighter (dark
 * one) just far enough to read.
 */
export function readableOn(color: string, background: string): string {
  const hex = toHex(parseHex(color));
  if (contrastRatio(hex, background) >= READABLE_CONTRAST) return hex;
  const darken = contrastRatio('#000000', background) >= contrastRatio('#ffffff', background);
  const { h, s, l } = toHsl(parseHex(hex));
  const reads = (lightness: number): boolean =>
    contrastRatio(toHex(fromHsl({ h, s, l: lightness })), background) >= READABLE_CONTRAST;
  let fails = l;
  let passes = darken ? 0 : 100;
  if (!reads(passes)) return darken ? '#000000' : '#ffffff';
  for (let step = 0; step < 24; step += 1) {
    const mid = (fails + passes) / 2;
    if (reads(mid)) passes = mid;
    else fails = mid;
  }
  return toHex(fromHsl({ h, s, l: passes }));
}

/** The five accent variables for `accent`, without writing them anywhere. */
export function controlsAccentVars(accent: string): Record<(typeof CONTROLS_ACCENT_VARS)[number], string> {
  const rgb = parseHex(accent);
  const hex = toHex(rgb);
  return {
    '--tnzi-primary': hex,
    '--tnzi-primary-rgb': `${rgb.r} ${rgb.g} ${rgb.b}`,
    '--tnzi-on-primary': textOnFill(hex),
    '--tnzi-primary-text-on-light': readableOn(hex, READABLE_ON.light),
    '--tnzi-primary-text-on-dark': readableOn(hex, READABLE_ON.dark),
  };
}

const followers = new WeakMap<HTMLElement, () => void>();

function setDark(target: HTMLElement, dark: boolean): void {
  target.classList.toggle('dark', dark);
  target.style.colorScheme = dark ? 'dark' : 'light';
}

/**
 * Sets light / dark and the accent on `target` (default `<html>`), for a page
 * that uses `@tnzi/ui-ai/controls` and `controls.css` without the `@tnzi/ui`
 * theme context. An app that has that context sets its mode and colour there
 * instead: both writing the same variables would leave whichever ran last.
 *
 * @throws If `accent` is not a hex colour; nothing is changed then.
 */
export function applyControlsTheme(options: ControlsThemeOptions, target?: HTMLElement): void {
  const element = target ?? document.documentElement;
  const vars = typeof options.accent === 'string' ? controlsAccentVars(options.accent) : null;

  if (options.mode !== undefined) {
    followers.get(element)?.();
    followers.delete(element);
    if (options.mode === 'auto' && typeof matchMedia === 'function') {
      const query = matchMedia(DARK_QUERY);
      const follow = (): void => setDark(element, query.matches);
      follow();
      query.addEventListener('change', follow);
      followers.set(element, () => query.removeEventListener('change', follow));
    } else {
      setDark(element, options.mode === 'dark');
    }
  }

  if (vars) {
    for (const name of CONTROLS_ACCENT_VARS) element.style.setProperty(name, vars[name]);
  } else if (options.accent === null) {
    for (const name of CONTROLS_ACCENT_VARS) element.style.removeProperty(name);
  }
}
