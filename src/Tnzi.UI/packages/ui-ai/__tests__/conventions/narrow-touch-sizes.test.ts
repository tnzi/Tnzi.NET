// @vitest-environment node
/**
 * Convention gate: on a narrow screen the chat shell changes its layout, not
 * its scale, and the sidebar marks the open conversation by background only.
 * Read from the declared styles because happy-dom does no layout.
 *
 * - `TChatApp`'s narrow class sets touch sizes (44px rows and icon buttons,
 *   16px row and field text), and the rows and buttons that a phone taps read
 *   those tokens with their desktop value as the fallback. The phone layout
 *   used to keep the desktop's 32px rows and 13-14px text, which on a phone
 *   reads as the whole interface shrunk and misses the thumb.
 * - The open conversation in Recents has no coloured bar on its left edge.
 * - The thread scroller does not hand an overscroll on to the page.
 */
import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const pkgRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');

function css(rel: string): string {
  const sfc = readFileSync(resolve(pkgRoot, rel), 'utf8');
  return (/<style[^>]*>([\s\S]*?)<\/style>/.exec(sfc)?.[1] ?? '').replace(/\/\*[\s\S]*?\*\//g, '');
}

function rule(style: string, selector: string): string {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const match = new RegExp(`(?:^|\\})\\s*${escaped}\\s*\\{([^}]*)\\}`).exec(style);
  if (!match) throw new Error(`no rule "${selector}"`);
  return match[1]!;
}
const decl = (body: string, prop: string) => new RegExp(`(?:^|;)\\s*${prop}\\s*:\\s*([^;]+)`).exec(body)?.[1]?.trim();

const CHAT_APP = css('src/components/chat/TChatApp.vue');
const NAV = css('src/components/layout/TSidebarNav.vue');
const THREADS = css('src/components/chat/TThreadList.vue');

describe('narrow shell touch sizes', () => {
  it('the narrow class sets touch-size tokens no smaller than the desktop sizes', () => {
    const narrow = rule(CHAT_APP, '.t-chat-app--narrow');
    expect(decl(narrow, '--tnzi-ai-row-height')).toBe('44px');
    expect(decl(narrow, '--tnzi-ai-icon-button-size')).toBe('44px');
    expect(decl(narrow, '--tnzi-ai-row-font-size')).toBe('16px');
    expect(decl(narrow, '--tnzi-ai-field-font-size')).toBe('16px');
  });

  it('sidebar rows read the tokens, with the desktop size as fallback', () => {
    for (const [style, selector] of [[NAV, '.t-sidebar-nav__item'], [THREADS, '.t-thread-list__row']] as const) {
      const body = rule(style, selector);
      expect(decl(body, 'height')).toBe('var(--tnzi-ai-row-height, 32px)');
      expect(decl(body, 'font-size')).toBe('var(--tnzi-ai-row-font-size, 14px)');
    }
  });

  it('the top bar and sidebar header buttons read the icon-button token', () => {
    for (const selector of ['.t-chat-app__topbar-toggle', '.t-chat-app__brand-collapse']) {
      expect(decl(rule(CHAT_APP, selector), 'width')).toBe('var(--tnzi-ai-icon-button-size, 32px)');
    }
  });

  it('no rule shrinks text or scales the shell below the breakpoint', () => {
    for (const style of [CHAT_APP, NAV, THREADS]) {
      expect(style).not.toMatch(/\bzoom\s*:/);
      expect(style).not.toMatch(/transform\s*:\s*scale\(/);
    }
  });
});

describe('Recents active row', () => {
  it('is marked by its background alone, no edge bar', () => {
    expect(decl(rule(THREADS, '.t-thread-list__row.is-active'), 'background')).toBe('var(--tnzi-ai-accent-soft)');
    expect(THREADS).not.toContain('.t-thread-list__row.is-active::before');
  });
});

describe('thread scroller', () => {
  it('contains its overscroll', () => {
    expect(decl(rule(CHAT_APP, '.t-chat-app__thread'), 'overscroll-behavior')).toBe('contain');
  });
});

describe('thread top fade', () => {
  const fade = rule(CHAT_APP, '.t-chat-app__top-fade');

  it('is pinned to the top of the scroller and lets clicks through', () => {
    expect(decl(fade, 'position')).toBe('sticky');
    expect(decl(fade, 'top')).toBe('0');
    expect(decl(fade, 'pointer-events')).toBe('none');
    expect(decl(fade, 'background')).toContain('var(--tnzi-ai-bg)');
  });

  it('is hidden until something is scrolled up out of view', () => {
    expect(decl(fade, 'opacity')).toBe('0');
    expect(decl(rule(CHAT_APP, '.t-chat-app__top-fade.is-visible'), 'opacity')).toBe('1');
  });
});
