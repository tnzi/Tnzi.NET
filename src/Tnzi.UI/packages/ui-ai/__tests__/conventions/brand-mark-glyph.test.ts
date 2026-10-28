// @vitest-environment node
/**
 * Convention gate: the glyph in the brand tile takes its size from
 * `--tnzi-ai-brand-mark-glyph-size` in both places the tile is drawn (the
 * sidebar header and the collapsed rail). A product whose mark should fill the
 * tile (an app-icon face) sets the token once; a size written into one of the
 * two again would make the mark change size when the sidebar collapses.
 */
import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const pkgRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const css = (rel: string) =>
  (/<style[^>]*>([\s\S]*?)<\/style>/.exec(readFileSync(resolve(pkgRoot, rel), 'utf8'))?.[1] ?? '').replace(/\/\*[\s\S]*?\*\//g, '');

function rule(source: string, selector: string): string {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const match = new RegExp(`(?:^|\\})\\s*${escaped}\\s*\\{([^}]*)\\}`).exec(source);
  if (!match) throw new Error(`no rule "${selector}"`);
  return match[1]!;
}

describe('brand mark glyph size', () => {
  it.each([
    ['src/components/chat/TChatApp.vue', '.t-chat-app__brand-mark > svg'],
    ['src/components/layout/TChatRail.vue', '.t-chat-rail__brand-logo svg'],
  ])('%s sizes the glyph from the token', (file, selector) => {
    const body = rule(css(file), selector);
    expect(body).toMatch(/width:\s*var\(--tnzi-ai-brand-mark-glyph-size\b/);
    expect(body).toMatch(/height:\s*var\(--tnzi-ai-brand-mark-glyph-size\b/);
  });

  it('the token is declared with its default', () => {
    const sheet = readFileSync(resolve(pkgRoot, 'src/styles/index.css'), 'utf8');
    expect(sheet).toMatch(/--tnzi-ai-brand-mark-glyph-size:\s*18px/);
  });
});
