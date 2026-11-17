// @vitest-environment node
/**
 * Convention gate: a waiting card (approval, plan, questions) never pushes its answers out of view.
 *
 * ## The bug this locks out
 *
 * The cards sit in `TPendingApprovals`, the band above the composer, which is capped at 40dvh and
 * scrolls. The cards themselves had no cap and were plain blocks with the answers last: a permission
 * card with a long diff (its text box up to 220px), a plan (its box up to min(420px, 45dvh), taller
 * than the band on its own) or a card of five questions (no cap at all) outgrew the band, and what
 * the band cut off was the row of buttons. On a 1440x760 desktop the Change / Decline row lay under
 * the composer; scrolling the band meant scrolling a nested box while the pointer usually sat on the
 * diff, which scrolled the diff instead.
 *
 * The shape that fixes it, read here from the declared styles because happy-dom does no layout:
 * - the band publishes its height as `--t-pending-approvals-max-height` (from the token
 *   `--tnzi-ai-pending-approvals-max-height`) and does not squeeze its cards (each keeps its height,
 *   the band scrolls: in a flex column a card that scrolls would otherwise shrink to nothing);
 * - a card is a flex column capped at that height, whose parts keep their height, except the one long
 *   part (the text, the plan, the list of questions), which gives way down to a few lines and
 *   scrolls inside; the card itself scrolls only past even that.
 * Measured in Chromium against `TChatApp` (1440x760, 768x1024, 390x844; long diff, long plan, five
 * questions, all three at once): before, 19 of 21 cards had buttons outside the band or under the
 * composer; after, title, buttons and time left of every card inside the band and hit-testable.
 */
import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join, resolve } from 'node:path';

const pkgRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const read = (path: string) => readFileSync(join(pkgRoot, path), 'utf8');

function stylesOf(path: string): string {
  const text = read(path);
  const css = path.endsWith('.css') ? text : Array.from(text.matchAll(/<style[^>]*>([\s\S]*?)<\/style>/g), (m) => m[1]).join('\n');
  return css.replace(/\/\*[\s\S]*?\*\//g, '');
}

/** Every declaration of the rules whose selector list holds `selector`, as `property: value` lines. */
function declarations(css: string, selector: string): string[] {
  return Array.from(css.matchAll(/([^{}]+)\{([^{}]*)\}/g))
    .filter((m) => m[1]!.split(',').map((s) => s.trim().replace(/\s+/g, ' ')).includes(selector))
    .flatMap((m) => m[2]!.split(';').map((d) => d.trim().replace(/\s+/g, ' ')).filter(Boolean));
}

const APPROVALS = 'src/components/approvals';

describe('pending card heights', () => {
  const band = stylesOf(`${APPROVALS}/TPendingApprovals.vue`);
  const card = stylesOf(`${APPROVALS}/TApprovalCard.vue`);

  it('reads the declared rules (positive control)', () => {
    expect(declarations('.a, .b { color: red; margin: 0 }', '.b')).toEqual(['color: red', 'margin: 0']);
    expect(declarations(band, '.t-pending-approvals').length).toBeGreaterThan(3);
  });

  it('the band takes its height from the token and hands it to its cards', () => {
    expect(stylesOf('src/styles/tokens.css')).toMatch(/--tnzi-ai-pending-approvals-max-height\s*:\s*40dvh/);
    const rule = declarations(band, '.t-pending-approvals');
    expect(rule).toContain('--t-pending-approvals-max-height: var(--tnzi-ai-pending-approvals-max-height, 40dvh)');
    expect(rule).toContain('max-height: var(--t-pending-approvals-max-height)');
    expect(rule).toContain('overflow-y: auto');
  });

  it('the band does not squeeze its cards, its own or a slot\'s', () => {
    expect(declarations(band, '.t-pending-approvals > *')).toContain('flex-shrink: 0');
    expect(declarations(band, '.t-pending-approvals > :slotted(*)')).toContain('flex-shrink: 0');
  });

  it('a card is a column capped at the band\'s height, keeping its own height in a column of cards', () => {
    const root = declarations(card, '.t-approval-card');
    for (const d of ['display: flex', 'flex-direction: column', 'flex-shrink: 0', 'max-height: var(--t-pending-approvals-max-height, none)', 'overflow-y: auto']) {
      expect(root).toContain(d);
    }
    expect(declarations(card, '.t-approval-card > *')).toContain('flex-shrink: 0');
  });

  // The one long part of each kind of card gives way and scrolls; the title and answers do not.
  it.each([
    ['approval card, long text', `${APPROVALS}/TApprovalCard.vue`, '.t-approval-card > .t-approval-card__detail', '.t-approval-card__detail'],
    ['plan card, long plan', `${APPROVALS}/TPlanCard.vue`, '.t-plan-card__plan', '.t-plan-card__plan'],
    ['question card, many questions', `${APPROVALS}/TQuestionCard.vue`, '.t-question-card', '.t-question-card'],
  ])('%s: gives way down to a few lines and scrolls inside', (_kind, path, shrinking, scrolling) => {
    const css = stylesOf(path);
    const rule = declarations(css, shrinking);
    expect(rule).toContain('flex-shrink: 1');
    const min = rule.find((d) => d.startsWith('min-height:'));
    expect(min, `${shrinking} needs a min-height, or it shrinks to nothing`).toMatch(/min-height: [1-9]\d*px/);
    expect(declarations(css, scrolling).some((d) => /^overflow(-y)?: auto$/.test(d))).toBe(true);
  });
});
