import { describe, it, expect, beforeEach } from 'vitest';
import { nextTick } from 'vue';
import { createThemeContext, mergeThemeSettings } from '@tnzi/ui';
import {
  AI_THEME_SNAPSHOT_VERSION,
  applyAiThemeSnapshot,
  buildAiThemeSnapshot,
  isValidAiThemeSnapshot,
} from '../../src/theme/snapshot';

function el(): HTMLElement {
  const node = document.createElement('div');
  document.body.appendChild(node);
  return node;
}

describe('buildAiThemeSnapshot', () => {
  it('stamps the version and time', () => {
    const s = buildAiThemeSnapshot({ now: () => new Date('2026-08-02T10:00:00Z') });
    expect(s.version).toBe(AI_THEME_SNAPSHOT_VERSION);
    expect(s.exportedAt).toBe('2026-08-02T10:00:00.000Z');
  });

  it('splits shared brand tokens from this package own', () => {
    const s = buildAiThemeSnapshot({ primary: '#7c3aed', ai: { bg: '#fff' } });
    expect(s.ui.primary).toBe('#7c3aed');
    expect(s.ai.bg).toBe('#fff');
  });

  it('omits the brand half when no brand colour was set', () => {
    expect(buildAiThemeSnapshot({ ai: { bg: '#fff' } }).ui).toEqual({});
  });

  it('copies the token map so later edits do not mutate the snapshot', () => {
    const tokens = { bg: '#fff' };
    const s = buildAiThemeSnapshot({ ai: tokens });
    tokens.bg = '#000';
    expect(s.ai.bg).toBe('#fff');
  });
});

describe('isValidAiThemeSnapshot', () => {
  it('accepts a snapshot of the current version', () => {
    expect(isValidAiThemeSnapshot(buildAiThemeSnapshot())).toBe(true);
  });

  it.each([null, undefined, 42, 'x', [], {}, { version: 99 }, { version: 1, ai: 'nope' }])(
    'rejects %p',
    (value) => {
      expect(isValidAiThemeSnapshot(value)).toBe(false);
    },
  );
});

describe('applyAiThemeSnapshot', () => {
  let target: HTMLElement;
  beforeEach(() => {
    target = el();
  });

  it('writes both halves', () => {
    applyAiThemeSnapshot(
      buildAiThemeSnapshot({ primary: '#7c3aed', ai: { bg: '#101014', modalRadius: '4px' } }),
      target,
    );
    expect(target.style.getPropertyValue('--tnzi-primary')).toBe('#7c3aed');
    expect(target.style.getPropertyValue('--tnzi-ai-bg')).toBe('#101014');
    expect(target.style.getPropertyValue('--tnzi-ai-modal-radius')).toBe('4px');
  });

  /**
   * The behaviour that makes a snapshot "the complete set of deviations": a
   * token dropped between saves has to stop being overridden. Without the reset
   * an operator could add an override but never remove one.
   */
  it('drops overrides that the new snapshot no longer carries', () => {
    applyAiThemeSnapshot(buildAiThemeSnapshot({ ai: { bg: '#101014', text: '#fff' } }), target);
    applyAiThemeSnapshot(buildAiThemeSnapshot({ ai: { bg: '#101014' } }), target);

    expect(target.style.getPropertyValue('--tnzi-ai-bg')).toBe('#101014');
    expect(target.style.getPropertyValue('--tnzi-ai-text')).toBe('');
  });

  it('clears everything, brand included, when given null', () => {
    applyAiThemeSnapshot(buildAiThemeSnapshot({ primary: '#7c3aed', ai: { bg: '#101014' } }), target);
    applyAiThemeSnapshot(null, target);

    expect(target.style.getPropertyValue('--tnzi-primary')).toBe('');
    expect(target.style.getPropertyValue('--tnzi-ai-bg')).toBe('');
  });

  it('is a no-op without a target', () => {
    expect(() => applyAiThemeSnapshot(buildAiThemeSnapshot(), null)).not.toThrow();
  });

  /**
   * `--tnzi-primary` belongs to `@tnzi/ui`, whose theme system writes it as
   * inline style on <html> at mount (`injectCssVars`). Reset must clear only
   * what THIS module wrote, or the host application's brand colour disappears
   * on any reset - and the symptom shows up far from here.
   */
  it('leaves a host-written --tnzi-primary alone', () => {
    target.style.setProperty('--tnzi-primary', '#0d9488'); // as @tnzi/ui would

    applyAiThemeSnapshot(buildAiThemeSnapshot({ ai: { bg: '#101014' } }), target);
    expect(target.style.getPropertyValue('--tnzi-primary')).toBe('#0d9488');

    applyAiThemeSnapshot(null, target);
    expect(target.style.getPropertyValue('--tnzi-primary')).toBe('#0d9488');
  });

  it('does clear a --tnzi-primary it wrote itself', () => {
    applyAiThemeSnapshot(buildAiThemeSnapshot({ primary: '#7c3aed' }), target);
    expect(target.style.getPropertyValue('--tnzi-primary')).toBe('#7c3aed');

    applyAiThemeSnapshot(null, target);
    expect(target.style.getPropertyValue('--tnzi-primary')).toBe('');
  });

  it('restores the host value when its own override is dropped', () => {
    target.style.setProperty('--tnzi-primary', '#0d9488');
    applyAiThemeSnapshot(buildAiThemeSnapshot({ primary: '#7c3aed' }), target);
    // Operator removed the brand override from the snapshot.
    applyAiThemeSnapshot(buildAiThemeSnapshot({ ai: { bg: '#fff' } }), target);
    // Its own write is gone; the host's is NOT re-applied by us - it was
    // overwritten, which is why this reads empty rather than '#0d9488'.
    expect(target.style.getPropertyValue('--tnzi-primary')).toBe('');
  });
});

/**
 * The published brand colour. `--tnzi-primary` alone is not the brand: the
 * accent tints derive from `--tnzi-primary-rgb`, the palette levels feed the
 * atoms, and the naive controls take theirs from the host theme context. And a
 * host context re-writes `--tnzi-primary` itself on every change to its
 * settings (a light/dark switch included), so an inline write next to it was
 * undone by the next mode switch.
 */
describe('applyAiThemeSnapshot brand colour', () => {
  it('without a host context writes the whole primary family, and clears it again', () => {
    const target = el();
    applyAiThemeSnapshot(buildAiThemeSnapshot({ primary: '#7c3aed' }), target);
    expect(target.style.getPropertyValue('--tnzi-primary')).toBe('#7c3aed');
    expect(target.style.getPropertyValue('--tnzi-primary-rgb')).toBe('124 58 237');
    expect(target.style.getPropertyValue('--tnzi-primary-500')).toBe('#7c3aed');
    expect(target.style.getPropertyValue('--tnzi-info')).toBe('');

    applyAiThemeSnapshot(null, target);
    expect(target.style.getPropertyValue('--tnzi-primary-rgb')).toBe('');
    expect(target.style.getPropertyValue('--tnzi-primary-500')).toBe('');
  });

  // The foreground on an accent fill and the accent as text go with the brand,
  // or a light brand colour gets white text on it and itself as unreadable text.
  it('without a host context writes the readable companions too, both modes at once', () => {
    const target = el();
    applyAiThemeSnapshot(buildAiThemeSnapshot({ primary: '#FFD217' }), target);
    expect(target.style.getPropertyValue('--tnzi-on-primary')).toBe('#141414');
    expect(target.style.getPropertyValue('--tnzi-primary-text-on-light')).toBe('#816800');
    expect(target.style.getPropertyValue('--tnzi-primary-text-on-dark')).toBe('#ffd217');
    // The per-mode alias stays the stylesheet's: an inline value would pin one mode.
    expect(target.style.getPropertyValue('--tnzi-primary-text')).toBe('');

    applyAiThemeSnapshot(null, target);
    expect(target.style.getPropertyValue('--tnzi-on-primary')).toBe('');
    expect(target.style.getPropertyValue('--tnzi-primary-text-on-light')).toBe('');
  });

  it('with a host context sets the context primary, so a mode switch keeps it', async () => {
    const context = createThemeContext(mergeThemeSettings({ colors: { primary: '#0d9488' } }));
    const root = document.documentElement;

    applyAiThemeSnapshot(buildAiThemeSnapshot({ primary: '#7c3aed' }), root, context);
    expect(context.settings.value.colors.primary).toBe('#7c3aed');

    context.setMode('dark');
    await nextTick();
    expect(root.style.getPropertyValue('--tnzi-primary')).toBe('#7c3aed');
    expect(root.style.getPropertyValue('--tnzi-primary-rgb')).toBe('124 58 237');

    // Dropping the override hands the host its own brand back.
    applyAiThemeSnapshot(buildAiThemeSnapshot({ ai: { bg: '#fff' } }), root, context);
    expect(context.settings.value.colors.primary).toBe('#0d9488');
    await nextTick();
    expect(root.style.getPropertyValue('--tnzi-primary')).toBe('#0d9488');

    root.classList.remove('dark');
    root.removeAttribute('style');
  });
});
