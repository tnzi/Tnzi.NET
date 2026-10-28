// @vitest-environment node
/**
 * Convention gate: the controls that sit in every answer carry no box at rest.
 *
 * The action buttons under a reply (copy, retry, ratings), the line of a tool step and
 * the line of a run of steps are read past on every turn. The first two used to be
 * boxed (a bordered circle on a surface fill; a filled card with a bordered name chip),
 * which made the thread feel heavier than the text it holds. None of them shows more
 * than a faint wash or a colour change on hover. Nothing renders
 * wrong if a box comes back, so no mount test would notice; this reads the declared
 * styles instead, the way `sidebar-row-geometry.test.ts` does.
 */
import { describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const pkgRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');

function styleBlock(rel: string): string {
  const sfc = readFileSync(resolve(pkgRoot, rel), 'utf8');
  const match = /<style[^>]*>([\s\S]*?)<\/style>/.exec(sfc);
  if (!match) throw new Error(`no <style> block in ${rel}`);
  return match[1].replace(/\/\*[\s\S]*?\*\//g, '');
}

/** Body of the rule whose selector is exactly `selector` (anchored on the previous `}`). */
function ruleBody(css: string, selector: string): string {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const match = new RegExp(`(?:^|\\})\\s*${escaped}\\s*\\{([^}]*)\\}`).exec(css);
  if (!match) throw new Error(`no rule with selector exactly "${selector}"`);
  return match[1];
}

function declared(body: string, property: string): string | null {
  const match = new RegExp(`(?:^|;)\\s*${property}\\s*:\\s*([^;]+)`).exec(body);
  return match ? match[1].trim() : null;
}

const NO_BORDER = ['none', '0'];
const NO_FILL = ['transparent', 'none'];

describe('quiet chrome', () => {
  it.each([
    ['src/components/chat/TThreadMessage.vue', '.t-thread-message__action'],
    ['src/components/reasoning/TToolStep.vue', '.t-tool-step__line'],
    ['src/components/reasoning/TToolRun.vue', '.t-tool-run__line'],
  ])('%s %s has no border and no fill at rest', (file, selector) => {
    const body = ruleBody(styleBlock(file), selector);
    expect(NO_BORDER).toContain(declared(body, 'border'));
    expect(NO_FILL).toContain(declared(body, 'background'));
  });

  it('pressed ratings are said by colour, not by a box', () => {
    const body = ruleBody(styleBlock('src/components/chat/TThreadMessage.vue'), '.t-thread-message__action.is-active');
    expect(declared(body, 'background')).toBeNull();
    expect(declared(body, 'border-color')).toBeNull();
    expect(declared(body, 'color')).toBe('var(--tnzi-ai-accent-text)');
  });

  it('keyboard focus stays visible on all of them', () => {
    expect(styleBlock('src/components/chat/TThreadMessage.vue')).toMatch(/\.t-thread-message__action:focus-visible\s*\{[^}]*outline:/);
    expect(styleBlock('src/components/reasoning/TToolStep.vue')).toMatch(/button\.t-tool-step__line:focus-visible\s*\{[^}]*outline:/);
    expect(styleBlock('src/components/reasoning/TToolRun.vue')).toMatch(/\.t-tool-run__line:focus-visible\s*\{[^}]*outline:/);
  });
});
