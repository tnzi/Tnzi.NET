import { describe, it, expect, beforeEach } from 'vitest';
import {
  applyAiTheme,
  applyThemeVars,
  resetAiTheme,
} from '../../src/theme/tokens';

/**
 * The regression these tests lock in: the token API used to write `--ai-*`
 * variables, which nothing in the package reads (every component reads
 * `--tnzi-ai-*`), so the whole API was a no-op.
 */
describe('applyAiTheme', () => {
  let target: HTMLElement;

  beforeEach(() => {
    target = document.createElement('div');
  });

  it('writes the --tnzi-ai-* variables the package actually reads', () => {
    applyAiTheme({ accent: '#7c3aed', codeBg: '#101014' }, target);

    expect(target.style.getPropertyValue('--tnzi-ai-accent')).toBe('#7c3aed');
    expect(target.style.getPropertyValue('--tnzi-ai-code-bg')).toBe('#101014');
  });

  it('never writes the legacy --ai-* names', () => {
    applyAiTheme({ accent: '#7c3aed' }, target);
    expect(target.style.getPropertyValue('--ai-accent')).toBe('');
  });

  it('maps conversation tokens onto their real variable names', () => {
    applyAiTheme(
      { userBubble: 'rgb(1 2 3)', assistantBubble: 'rgb(4 5 6)', streamingCursor: '#fff' },
      target,
    );

    expect(target.style.getPropertyValue('--tnzi-ai-chat-user-bg')).toBe('rgb(1 2 3)');
    expect(target.style.getPropertyValue('--tnzi-ai-chat-assistant-bg')).toBe('rgb(4 5 6)');
    expect(target.style.getPropertyValue('--tnzi-ai-streaming-cursor')).toBe('#fff');
  });

  it('only writes the keys it is given, so calls compose', () => {
    applyAiTheme({ accent: '#111111' }, target);
    applyAiTheme({ danger: '#222222' }, target);

    expect(target.style.getPropertyValue('--tnzi-ai-accent')).toBe('#111111');
    expect(target.style.getPropertyValue('--tnzi-ai-danger')).toBe('#222222');
    expect(target.style.getPropertyValue('--tnzi-ai-surface')).toBe('');
  });

  it('ignores null and undefined values', () => {
    applyAiTheme({ accent: undefined }, target);
    expect(target.style.getPropertyValue('--tnzi-ai-accent')).toBe('');
  });
});

describe('applyThemeVars', () => {
  it('accepts raw names with or without the leading dashes', () => {
    const target = document.createElement('div');
    applyThemeVars({ 'tnzi-ai-modal-radius': '12px', '--tnzi-ai-backdrop-blur': '8px' }, target);

    expect(target.style.getPropertyValue('--tnzi-ai-modal-radius')).toBe('12px');
    expect(target.style.getPropertyValue('--tnzi-ai-backdrop-blur')).toBe('8px');
  });
});

describe('resetAiTheme', () => {
  it('removes every --tnzi-ai-* override', () => {
    const target = document.createElement('div');
    applyAiTheme({ accent: '#7c3aed', codeBg: '#101014' }, target);

    resetAiTheme(target);

    expect(target.style.getPropertyValue('--tnzi-ai-accent')).toBe('');
    expect(target.style.getPropertyValue('--tnzi-ai-code-bg')).toBe('');
  });

  it('leaves variables owned by other packages alone', () => {
    const target = document.createElement('div');
    target.style.setProperty('--tnzi-primary', '#0d9488');
    applyAiTheme({ accent: '#7c3aed' }, target);

    resetAiTheme(target);

    expect(target.style.getPropertyValue('--tnzi-primary')).toBe('#0d9488');
    expect(target.style.getPropertyValue('--tnzi-ai-accent')).toBe('');
  });
});
