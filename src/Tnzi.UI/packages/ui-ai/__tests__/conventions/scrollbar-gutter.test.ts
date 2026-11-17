// @vitest-environment node
/**
 * Convention gate: the shell's main scrollers keep their scrollbar's room
 * whether or not their content overflows.
 *
 * A classic (non-overlay) scrollbar takes its width out of the scroller's
 * content box only while the content overflows. Moving between a long page and
 * a short one in the page area, or a conversation growing past the window,
 * therefore slid everything inside sideways by the bar's width (the page's
 * toolbar, the centred reading column, the composer). `scrollbar-gutter:
 * stable` reserves the room in both states; where scrollbars overlay the
 * content (iOS, macOS by default) it reserves nothing.
 *
 * - the page area keeps one gutter on the bar's side: its pages own their own
 *   layout and many run edge to edge, so a matching gutter on the other edge
 *   would be an empty strip;
 * - the conversation and the landing page keep a gutter on both edges: their
 *   column is centred, and both edges keep it centred on the same axis, so the
 *   composer does not move when the landing turns into a conversation.
 *
 * Read from the declared styles because happy-dom does no layout.
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
const LANDING = css('src/components/chat/TLandingPage.vue');

describe('scrollbar gutter on the shell scrollers', () => {
  it('the page area reserves the bar on its own side', () => {
    expect(decl(rule(CHAT_APP, '.t-chat-app__view'), 'scrollbar-gutter')).toBe('stable');
  });

  it('a page that fills the area and scrolls itself (useChatViewFill) gives up the area\'s scrolling and its gutter', () => {
    const fill = rule(CHAT_APP, '.t-chat-app__view--fill');
    expect(decl(fill, 'overflow')).toBe('hidden');
    expect(decl(fill, 'scrollbar-gutter')).toBe('auto');
  });

  it('the conversation and the landing reserve it on both edges', () => {
    expect(decl(rule(CHAT_APP, '.t-chat-app__thread'), 'scrollbar-gutter')).toBe('stable both-edges');
    expect(decl(rule(LANDING, '.t-landing'), 'scrollbar-gutter')).toBe('stable both-edges');
  });
});
