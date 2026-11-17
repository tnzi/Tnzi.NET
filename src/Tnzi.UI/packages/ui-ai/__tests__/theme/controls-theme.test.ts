import { afterEach, describe, expect, it, vi } from 'vitest';
import { buildCssVars, defaultThemeSettings } from '@tnzi/ui/theme';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import {
  applyControlsTheme,
  controlsAccentVars,
  CONTROLS_ACCENT_VARS,
} from '../../src/theme/controls-theme';

/* Brand colours across the wheel and the lightness range: dark ones that
   keep white text, light ones that need the ink, and ones that read as text
   on one mode's surfaces only. */
const ACCENTS = [
  '#0d9488', '#ffd217', '#fdd835', '#2563eb', '#7c3aed', '#e11d48', '#f97316',
  '#16a34a', '#84cc16', '#06b6d4', '#000000', '#ffffff', '#808080', '#1a1a1a',
  '#f5f5f4', '#ff00ff', '#00ffff', '#a16207', '#fb7185', '#38bdf8', '#334155',
];

const html = () => document.documentElement;

afterEach(() => {
  applyControlsTheme({ mode: 'light', accent: null });
  html().classList.remove('dark');
  html().style.removeProperty('color-scheme');
});

describe('controlsAccentVars', () => {
  it('computes what the @tnzi/ui theme context computes for the same accent', () => {
    for (const accent of ACCENTS) {
      const theirs = buildCssVars({ ...defaultThemeSettings.colors, primary: accent }, 'light');
      const ours = controlsAccentVars(accent);
      for (const name of CONTROLS_ACCENT_VARS) {
        expect(ours[name], `${accent} ${name}`).toBe(theirs[name]);
      }
    }
  });

  it('writes the variables the controls stylesheet gives defaults for', () => {
    const defaults = readFileSync(resolve(__dirname, '../../src/styles/tnzi-defaults.css'), 'utf8');
    const declared = [...defaults.matchAll(/^\s*(--tnzi-[a-z-]+):\s*([^;]+);/gm)].map((m) => [m[1], m[2]!.trim()]);
    expect(declared.map(([name]) => name)).toEqual([...CONTROLS_ACCENT_VARS]);
    // The defaults are the default accent's own values.
    expect(Object.fromEntries(declared)).toEqual(controlsAccentVars('#0d9488'));
  });

  it('takes the short hex form', () => {
    expect(controlsAccentVars('#fd2')['--tnzi-primary']).toBe('#ffdd22');
  });

  it('refuses what is not a hex colour', () => {
    for (const bad of ['', 'teal', 'rgb(0 0 0)', '#12345', '#ggg']) {
      expect(() => controlsAccentVars(bad), bad).toThrow(/hex colour/);
    }
  });
});

describe('applyControlsTheme', () => {
  it('sets and clears the dark class, with the colour scheme', () => {
    applyControlsTheme({ mode: 'dark' });
    expect(html().classList.contains('dark')).toBe(true);
    expect(html().style.colorScheme).toBe('dark');
    applyControlsTheme({ mode: 'light' });
    expect(html().classList.contains('dark')).toBe(false);
    expect(html().style.colorScheme).toBe('light');
  });

  it('writes the accent inline and removes it with null', () => {
    applyControlsTheme({ accent: '#ffd217' });
    expect(html().style.getPropertyValue('--tnzi-primary')).toBe('#ffd217');
    expect(html().style.getPropertyValue('--tnzi-on-primary')).toBe('#141414');
    applyControlsTheme({ accent: null });
    for (const name of CONTROLS_ACCENT_VARS) expect(html().style.getPropertyValue(name)).toBe('');
  });

  it('leaves what it is not given as it is', () => {
    applyControlsTheme({ mode: 'dark', accent: '#2563eb' });
    applyControlsTheme({});
    expect(html().classList.contains('dark')).toBe(true);
    expect(html().style.getPropertyValue('--tnzi-primary')).toBe('#2563eb');
  });

  it('changes nothing when the accent is not a colour', () => {
    applyControlsTheme({ mode: 'light', accent: '#2563eb' });
    expect(() => applyControlsTheme({ mode: 'dark', accent: 'blue' })).toThrow();
    expect(html().classList.contains('dark')).toBe(false);
    expect(html().style.getPropertyValue('--tnzi-primary')).toBe('#2563eb');
  });

  it('applies to another element when given one', () => {
    const panel = document.createElement('div');
    applyControlsTheme({ mode: 'dark', accent: '#7c3aed' }, panel);
    expect(panel.classList.contains('dark')).toBe(true);
    expect(panel.style.getPropertyValue('--tnzi-primary')).toBe('#7c3aed');
    expect(html().classList.contains('dark')).toBe(false);
  });

  it('follows the system in auto until another mode is set', () => {
    const listeners = new Set<() => void>();
    const query = {
      matches: true,
      addEventListener: (_: string, fn: () => void) => listeners.add(fn),
      removeEventListener: (_: string, fn: () => void) => listeners.delete(fn),
    };
    vi.stubGlobal('matchMedia', vi.fn(() => query));
    try {
      applyControlsTheme({ mode: 'auto' });
      expect(html().classList.contains('dark')).toBe(true);
      query.matches = false;
      listeners.forEach((fn) => fn());
      expect(html().classList.contains('dark')).toBe(false);
      // A second `auto` replaces the first listener rather than adding one.
      applyControlsTheme({ mode: 'auto' });
      expect(listeners.size).toBe(1);
      applyControlsTheme({ mode: 'dark' });
      expect(listeners.size).toBe(0);
      query.matches = false;
      expect(html().classList.contains('dark')).toBe(true);
    } finally {
      vi.unstubAllGlobals();
    }
  });
});
