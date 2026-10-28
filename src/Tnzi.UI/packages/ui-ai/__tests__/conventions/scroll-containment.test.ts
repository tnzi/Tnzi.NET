// @vitest-environment node
/**
 * Convention gate: every scroll container is a containing block.
 *
 * ## The bug this locks out
 *
 * An absolutely positioned element takes its place from the nearest positioned
 * ancestor, and only that ancestor's overflow (or an overflow between the two)
 * clips it. A scroller without `position` is neither: an `sr-only` label
 * (`position: absolute`) deep in a scrolled conversation was laid out at its
 * spot in the content, but belonged to the page. Opening a tool run put a few of
 * them about 500px below the window; the document grew a scrollbar, and
 * scrolling it moved the whole app shell up and left an empty band under it.
 * The scroller (`.t-chat-app__thread`, `overflow-y: auto`) clipped nothing,
 * because it was not their containing block.
 *
 * Nothing in a unit test sees this: happy-dom does no layout, and the elements
 * render correctly. So this reads the declared styles: a rule that makes an
 * element scroll must also make it a containing block (a non-static `position`,
 * or `contain: layout | paint | content | strict`), and so must a utility class
 * list that scrolls (`overflow-y-auto` with `relative`). Then whatever is added
 * inside a scroller later (another visually hidden label, a tooltip without a
 * positioned parent) stays inside it.
 */
import { describe, it, expect } from 'vitest';
import { readFileSync, readdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join, relative, resolve } from 'node:path';

const pkgRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const srcRoot = join(pkgRoot, 'src');

function walk(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) =>
    entry.isDirectory() ? walk(join(dir, entry.name)) : [join(dir, entry.name)],
  );
}

const SCROLLS = /overflow(?:-[xy])?\s*:\s*[^;]*\b(?:auto|scroll)\b/;
const CONTAINS = /(?:^|;)\s*(?:position\s*:\s*(?:relative|absolute|fixed|sticky)\b|contain\s*:\s*[^;]*\b(?:layout|paint|content|strict)\b)/;
const SCROLL_CLASS = /(?:^|\s)(?:[\w-]+:)*overflow(?:-[xy])?-(?:auto|scroll)(?=\s|$)/;
const POSITION_CLASS = /(?:^|\s)(?:relative|absolute|fixed|sticky)(?=\s|$)/;

/** Selectors whose rules scroll, without any rule for the same selector making it a containing block. */
export function uncontainedScrollers(css: string): string[] {
  const clean = css.replace(/\/\*[\s\S]*?\*\//g, '');
  const rules = Array.from(clean.matchAll(/([^{}]+)\{([^{}]*)\}/g), (m) => ({
    selector: m[1]!.trim().replace(/\s+/g, ' '),
    body: m[2]!,
  }));
  const offenders = new Set<string>();
  for (const rule of rules) {
    if (!SCROLLS.test(rule.body)) continue;
    const contained = rules.some((other) => other.selector === rule.selector && CONTAINS.test(other.body));
    if (!contained) offenders.add(rule.selector);
  }
  return [...offenders];
}

/** Static class lists in a template that scroll without a position utility. */
export function uncontainedScrollClasses(template: string): string[] {
  return Array.from(template.matchAll(/\bclass="([^"]*)"/g), (m) => m[1]!)
    .filter((list) => SCROLL_CLASS.test(list) && !POSITION_CLASS.test(list));
}

const sources = walk(srcRoot)
  .filter((file) => file.endsWith('.vue') || file.endsWith('.css'))
  .map((file) => ({ path: relative(pkgRoot, file).replace(/\\/g, '/'), text: readFileSync(file, 'utf8') }));

function stylesOf(text: string, path: string): string {
  if (path.endsWith('.css')) return text;
  return Array.from(text.matchAll(/<style[^>]*>([\s\S]*?)<\/style>/g), (m) => m[1]).join('\n');
}

function templateOf(text: string, path: string): string {
  if (!path.endsWith('.vue')) return '';
  const match = /<template>([\s\S]*)<\/template>/.exec(text);
  return match ? match[1]!.replace(/<!--[\s\S]*?-->/g, '') : '';
}

describe('scroll containment', () => {
  it('has sources to check, and finds the shell scroller among them', () => {
    expect(sources.filter((s) => s.path.endsWith('.vue')).length).toBeGreaterThan(50);
    const shell = sources.find((s) => s.path === 'src/components/chat/TChatApp.vue');
    expect(stylesOf(shell!.text, shell!.path)).toMatch(/\.t-chat-app__thread\s*\{[^}]*overflow-y\s*:\s*auto/);
  });

  // Positive and negative controls: the rule catches the shapes it bans and
  // nothing next to them.
  it.each([
    ['.a { overflow-y: auto; }', ['.a']],
    ['.a { overflow: hidden auto }', ['.a']],
    ['.a :deep(pre) { overflow-x: scroll; }', ['.a :deep(pre)']],
    ['@media (max-width: 600px) { .a { overflow: auto } }', ['.a']],
    ['.a { overflow: auto; position: static }', ['.a']],
  ])('catches %s', (css, expected) => {
    expect(uncontainedScrollers(css)).toEqual(expected);
  });

  it.each([
    '.a { overflow: hidden; }',
    '.a { overflow-y: auto; position: relative; }',
    '.a { overflow-y: auto } .a { position: sticky; }',
    '.a { overflow: auto; contain: layout paint; }',
    '.a { text-overflow: ellipsis; overflow-wrap: anywhere }',
  ])('leaves %s alone', (css) => {
    expect(uncontainedScrollers(css)).toEqual([]);
  });

  it('reads utility classes the same way', () => {
    expect(uncontainedScrollClasses('<div class="flex-1 overflow-y-auto p-4">')).toEqual(['flex-1 overflow-y-auto p-4']);
    expect(uncontainedScrollClasses('<div class="relative flex-1 overflow-y-auto">')).toEqual([]);
    expect(uncontainedScrollClasses('<div class="overflow-hidden truncate">')).toEqual([]);
  });

  it('every scroller in the package is a containing block for what it scrolls', () => {
    const offenders = sources.flatMap(({ path, text }) => [
      ...uncontainedScrollers(stylesOf(text, path)).map((selector) => `${path}: ${selector}`),
      ...uncontainedScrollClasses(templateOf(text, path)).map((list) => `${path}: class="${list}"`),
    ]);
    expect(offenders).toEqual([]);
  });
});
