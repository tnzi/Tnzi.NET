// @vitest-environment node
/**
 * Convention gate: every scroll container ui-ai renders has the thin scrollbar,
 * wherever it ends up in the page, without styling the consumer's `*`.
 *
 * The thin style used to be scoped to `.t-chat-app` (the shell's subtree) and
 * an opt-in `.t-ai-scroll` nobody used. Anything rendered outside the shell -
 * the settings dialog and command palette teleported to `<body>`, and any
 * component used on its own - fell back to the browser's thick default bar.
 *
 * The rule now has three hooks (see `styles/index.css`), and this file checks
 * that each scroll container uses one:
 *
 * - a CSS rule that makes something scroll (`overflow: auto | scroll`) names an
 *   element whose static class list carries `t-ai-scroll`, or the component's
 *   template is (or sits in) a `t-ai-scroll-scope` (rendered markup whose
 *   scrolling `pre` the template cannot class: markdown, highlighted code);
 * - a utility-class scroller (`overflow-y-auto`, ...) carries `t-ai-scroll` in
 *   the same class list;
 * - whatever is teleported to `<body>` has `t-ai-scroll-scope` on its root, so
 *   the consumer's own content in its slots matches too;
 * - the stylesheet never styles a bare `*` or `::-webkit-scrollbar` globally.
 */
import { describe, it, expect } from 'vitest';
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join, relative, resolve } from 'node:path';

const pkgRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const srcRoot = resolve(pkgRoot, 'src');

function walk(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const full = join(dir, name);
    return statSync(full).isDirectory() ? walk(full) : full.endsWith('.vue') ? [full] : [];
  });
}

const SCROLL_DECL = /overflow(?:-x|-y)?\s*:\s*(?:auto|scroll)/;
const SCROLL_UTILITY = /\boverflow(?:-x|-y)?-(?:auto|scroll)\b/;

interface Sfc {
  file: string;
  template: string;
  css: string;
  classLists: string[];
}

function parse(file: string): Sfc {
  const source = readFileSync(file, 'utf8');
  const template = /<template>([\s\S]*)<\/template>/.exec(source)?.[1] ?? '';
  const css = (/<style[^>]*>([\s\S]*?)<\/style>/.exec(source)?.[1] ?? '').replace(/\/\*[\s\S]*?\*\//g, '');
  const classLists = [...template.matchAll(/(?<![:\w-])class="([^"]*)"/g)].map((m) => m[1]!);
  return { file: relative(pkgRoot, file).replace(/\\/g, '/'), template, css, classLists };
}

const sfcs = walk(srcRoot).map(parse);

/** The class a scroll rule styles: the first class of its last compound, `:deep()` unwrapped. */
function targetClass(selector: string): string | null {
  const unwrapped = selector.replace(/:deep\(([^)]*)\)/g, ' $1');
  const last = unwrapped.trim().split(/\s+/).at(-1) ?? '';
  return /\.([\w-]+)/.exec(last)?.[1] ?? null;
}

const isScope = (sfc: Sfc) => /\bt-ai-scroll-scope\b/.test(sfc.template) || /\bclass="t-chat-app\b/.test(sfc.template);
const hasClass = (list: string, name: string) => list.split(/\s+/).includes(name);

const scrollRules = sfcs.flatMap((sfc) =>
  [...sfc.css.matchAll(/([^{}]+)\{([^{}]*)\}/g)]
    .filter((m) => SCROLL_DECL.test(m[2]!))
    .map((m) => ({ sfc, selector: m[1]!.trim().replace(/\s+/g, ' ') })),
);

describe('thin scrollbars on every ui-ai scroll container', () => {
  it('finds the scroll containers it checks', () => {
    expect(scrollRules.length).toBeGreaterThan(10);
  });

  it('each CSS scroll container carries t-ai-scroll, or its component is a scope', () => {
    const missing = scrollRules
      .filter(({ sfc, selector }) => {
        if (isScope(sfc)) return false;
        const cls = targetClass(selector);
        if (!cls) return true;
        // A BEM modifier (`block--scroll`) is bound through `:class`; the element is the block's.
        const block = cls.replace(/--[\w-]+$/, '');
        return !sfc.classLists.some((list) => hasClass(list, block) && hasClass(list, 't-ai-scroll'));
      })
      .map(({ sfc, selector }) => `${sfc.file}: ${selector}`);
    expect(missing).toEqual([]);
  });

  it('each utility-class scroller carries t-ai-scroll', () => {
    const missing = sfcs.flatMap((sfc) =>
      sfc.classLists
        .filter((list) => SCROLL_UTILITY.test(list) && !hasClass(list, 't-ai-scroll'))
        .map((list) => `${sfc.file}: class="${list}"`),
    );
    expect(missing).toEqual([]);
  });

  it('everything teleported to <body> is a scope at its root', () => {
    const teleports = sfcs.flatMap((sfc) =>
      [...sfc.template.matchAll(/<Teleport to="body">\s*(?:<transition[^>]*>\s*)?<(\w+)([\s\S]*?)>/g)].map((m) => ({
        file: sfc.file,
        root: m[2]!,
      })),
    );
    expect(teleports.length).toBeGreaterThanOrEqual(3);
    const missing = teleports.filter((t) => !/\bt-ai-scroll-scope\b/.test(t.root)).map((t) => t.file);
    expect(missing).toEqual([]);
  });
});

describe('the scrollbar rule stays scoped', () => {
  const sheet = readFileSync(resolve(srcRoot, 'styles/index.css'), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '');
  const selectors = [...sheet.matchAll(/([^{}]+)\{([^{}]*)\}/g)]
    .filter((m) => /scrollbar/.test(m[1]!) || /scrollbar-(?:width|color)/.test(m[2]!))
    .flatMap((m) => m[1]!.split(',').map((s) => s.trim()));

  it('covers the shell, the scope and the per-element hook', () => {
    for (const hook of ['.t-chat-app *', '.t-ai-scroll-scope *', '.t-ai-scroll', '.t-ai-scroll-scope ::-webkit-scrollbar']) {
      expect(selectors).toContain(hook);
    }
  });

  it('never targets the consumer page globally', () => {
    const global = selectors.filter((s) => /^(?:\*|html|body|:root)\b/.test(s) || /^::-webkit-scrollbar/.test(s));
    expect(global).toEqual([]);
  });
});
