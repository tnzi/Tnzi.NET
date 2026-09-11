import { describe, it, expect } from 'vitest'
import { readFileSync, readdirSync, statSync } from 'fs'
import { join, sep } from 'path'

/**
 * Static gates for the surface tiers.
 *
 * Before the tiers existed, one card recipe - a 5% drop shadow at rest and an
 * 8% lift on hover - was copied by hand into thirteen components. The copies
 * had already drifted: some carried a border as well, some had neither, one
 * read a lighter token, and the two chat popovers spelled a token name
 * (`--tnzi-shadow-popover`) that was never defined anywhere, so they silently
 * rendered their own fallback and sat deeper than every other dropdown.
 *
 * The failure mode is what makes this worth a gate rather than a review note:
 * nothing breaks, no test goes red, and no single copy looks wrong on its own.
 * It only reads as wrong when two of them are on screen at the same time -
 * which is the one situation a component test never reproduces.
 */

/**
 * Both packages, not just this one. `@tnzi/ui` renders half the containers a
 * consumer sees (widget cards, stat tiles, the auth panel), so a gate that
 * stops at the package boundary leaves the card tier only half enforced - and
 * a rule that covers half of what it claims to cover reads as green while the
 * drift continues on the other side. `TWidgetCard` was exactly that: still
 * carrying the literal after every ui-admin copy had been migrated, and this
 * gate said nothing until its scan was widened.
 */
const ROOTS = [
  join(__dirname, '..', '..', 'src'),
  join(__dirname, '..', '..', '..', 'ui', 'src'),
]

/** The two recipes the card tier owns, in every spelling CSS accepts. */
const CARD_TIER_LITERALS: { pattern: RegExp; tier: string }[] = [
  {
    pattern: /box-shadow:\s*0 1px 2px(?: 0)? (?:rgb\(0 0 0 \/ 0?\.05\)|rgba\(0,\s*0,\s*0,\s*0?\.05\))/,
    tier: '--tnzi-surface-card-shadow',
  },
  {
    pattern: /box-shadow:\s*0 4px 12px(?: 0)? (?:rgb\(0 0 0 \/ 0?\.08\)|rgba\(0,\s*0,\s*0,\s*0?\.08\))/,
    tier: '--tnzi-surface-card-shadow-hover',
  },
]

/**
 * Deliberate exceptions, with the reason. A chat bubble is a speech element,
 * not a panel: it must keep its lift when the shell goes flat, or a wall of
 * messages collapses into one undifferentiated column of text.
 */
const ALLOWED = new Set(['@tnzi/ui-admin/components/chat/TMessageBubble.vue'])

/** Reads the card tier, so it IS one - see the marker gate below. */
const READS_CARD_TIER = /var\(--tnzi-surface-card-(?:shadow|border)/

/**
 * Files that read the tier without being a surface in it. Each needs a reason,
 * because the easy way to make this gate green is also the way to make it
 * useless.
 *
 *  - the two pickers DEPICT the tier rather than belong to it: a 96x64
 *    mock-up of a layout and the three swatches in the container-style
 *    chooser. Nothing nests inside a preview.
 *  - `polish.css` holds the rule for `.t-table-tabs`, but the class - and the
 *    marker next to it - are applied in `TTabsPage.vue`. The gate is
 *    file-scoped, so it cannot see across the two; the assertion right below
 *    checks that pairing instead of taking it on faith.
 */
const MARKER_EXEMPT = new Set([
  '@tnzi/ui-admin/components/layout/TLayoutModeCard.vue',
  '@tnzi/ui-admin/components/layout/TThemeDrawer.vue',
  '@tnzi/ui-admin/styles/polish.css',
])
/** The per-component hand-off the marker class replaced. */
const HAND_OFF = /--tnzi-surface-card-bg:\s*var\(--tnzi-surface-card-bg-nested\)/

/**
 * Comments are prose, not reads. Without this the gates fire on their own
 * documentation: a comment naming `var(--tnzi-surface-card-shadow-raised)` and
 * wrapping mid-name reads, to a regex, as a component consuming a token called
 * `--tnzi-surface-card-shadow-` that nobody ever defined.
 */
function code(file: string): string {
  return readFileSync(file, 'utf8')
    .replace(/\/\*[\s\S]*?\*\//g, ' ')
    .replace(/^\s*\/\/.*$/gm, ' ')
}

/**
 * Only the stylesheet half of a file. A `.vue` script section is full of
 * braces, so a rule-shaped regex run over the whole file happily reports
 * object literals as CSS selectors.
 */
function css(file: string): string {
  const src = code(file)
  if (file.endsWith('.css')) return src
  return [...src.matchAll(/<style[^>]*>([\s\S]*?)<\/style>/g)].map((m) => m[1]).join('\n')
}

function walk(dir: string, out: string[] = []): string[] {
  for (const name of readdirSync(dir)) {
    const full = join(dir, name)
    if (statSync(full).isDirectory()) walk(full, out)
    else if (name.endsWith('.vue') || name.endsWith('.css')) out.push(full)
  }
  return out
}

/**
 * `@tnzi/ui/components/layout/TWidgetCard.vue` - package-qualified, because
 * the two packages have the same folder shape and a bare `components/...`
 * path would let an allow-list entry silently cover the wrong file. Split on
 * the LAST `src`: the absolute path contains an earlier one
 * (`Tnzi.NET/src/Tnzi.UI/...`).
 */
function relative(file: string): string {
  const parts = file.split(sep)
  const at = parts.lastIndexOf('src')
  if (at === -1) return parts.join('/')
  return `@tnzi/${parts[at - 1]}/${parts.slice(at + 1).join('/')}`
}

describe('surface tiers', () => {
  const files = ROOTS.flatMap((root) => walk(root))

  it('finds style-bearing files to scan', () => {
    expect(files.length).toBeGreaterThan(100)
  })

  for (const { pattern, tier } of CARD_TIER_LITERALS) {
    it(`no component hardcodes the recipe owned by ${tier}`, () => {
      const offenders = files
        .filter((f) => pattern.test(readFileSync(f, 'utf8')))
        .map(relative)
        .filter((f) => !ALLOWED.has(f))
      expect(offenders, `Use var(${tier}) instead: ${offenders.join(', ')}`).toEqual([])
    })
  }

  /**
   * The nesting step is structural: `surfaces.css` keys it off the
   * `t-surface-card` class, so a surface that reads the tier tokens but forgets
   * the marker silently opts out - its own chrome looks right, and every card
   * nested inside it paints exactly the colour it paints.
   *
   * That is the bug this area started with, and it is invisible in isolation:
   * you only see it when a card is inside another card, which no component test
   * renders. It also cannot be caught while writing the component at fault -
   * the symptom appears in somebody else's component, later.
   */
  it('every surface that reads the card tier also carries the t-surface-card marker', () => {
    const offenders = files
      .filter((f) => {
        const src = code(f)
        return READS_CARD_TIER.test(src) && !src.includes('t-surface-card')
      })
      .map(relative)
      .filter((f) => !MARKER_EXEMPT.has(f))
    expect(
      offenders,
      `Reads the card tier but never marks itself as one, so nothing nested inside it steps: ${offenders.join(', ')}`,
    ).toEqual([])
  })

  it('the tab surface carries the marker where its class is applied', () => {
    // `.t-table-tabs` is styled in polish.css and applied in TTabsPage, so the
    // file-scoped marker gate above exempts the stylesheet. This is the half
    // that exemption is standing on: the two must appear together, or a tab
    // page is a card that no nested card knows it is sitting on.
    const src = readFileSync(join(__dirname, '..', '..', 'src', 'components', 'layout', 'TTabsPage.vue'), 'utf8')
    expect(src).toContain('t-table-tabs t-surface-card')
  })

  it('the nesting step is declared in exactly one place', () => {
    // Per-component hand-offs are what the marker replaced. Each one is a place
    // the next card component has to know about, and will not.
    const offenders = files
      .filter((f) => HAND_OFF.test(code(f)))
      .map(relative)
      .filter((f) => !f.endsWith('styles/surfaces.css'))
    expect(offenders, `The hand-off belongs in surfaces.css only: ${offenders.join(', ')}`).toEqual([])
  })

  it('every --tnzi-surface-* token a component reads is actually defined', () => {
    // The `--tnzi-shadow-popover` incident: a `var()` with a fallback cannot
    // fail loudly, so a token that never existed rendered as its fallback for
    // as long as nobody put the two popovers side by side.
    const declared = new Set<string>()
    for (const vars of [
      readFileSync(join(__dirname, '..', '..', '..', 'ui', 'src', 'styles', 'variables.css'), 'utf8'),
      readFileSync(join(__dirname, '..', '..', 'src', 'styles', 'variables.css'), 'utf8'),
    ]) {
      for (const m of vars.matchAll(/(--tnzi-surface-[a-z0-9-]+)\s*:/g)) declared.add(m[1])
    }

    const missing = new Map<string, string[]>()
    for (const file of files) {
      for (const m of code(file).matchAll(/var\(\s*(--tnzi-surface-[a-z0-9-]+)/g)) {
        if (!declared.has(m[1])) {
          missing.set(m[1], [...(missing.get(m[1]) ?? []), relative(file)])
        }
      }
    }
    expect(Object.fromEntries(missing)).toEqual({})
  })

  /**
   * `TWidgetCard` renders two different things under one class: the NCard,
   * and - when `bare` - a plain div that promises no chrome at all. Both carry
   * `t-widget-card`; only the card carries `t-surface-card`. So the marker is
   * the only thing in the DOM that distinguishes them, and any rule painting
   * card chrome has to be keyed to it.
   *
   * Both properties were once keyed to `.t-widget-card` alone, and the
   * component tried to buy them back with a subtractive
   * `.t-widget-card--bare { background: transparent; border: none;
   * box-shadow: none }`. It lost both races on specificity - to
   * `.t-widget-card:hover` in its own file, and to `:root .t-admin-content
   * .t-widget-card` in polish.css - so every bare widget sat on an opaque
   * panel and grew a card shadow under the pointer.
   *
   * The symptom is why this is a gate and not a review note. A bare widget
   * holding two cards did not look like a broken widget; it looked like two
   * cards with no gutter, because the gutter was there and painted card
   * colour. A consuming project spent three rounds on the card colour, the
   * border, the shadow, the gutter width and the canvas colour before anyone
   * asked what was painting the gap. Nothing about the word `bare` suggests
   * opaque, so nobody suspects it.
   *
   * Asserted on selectors rather than computed values on purpose: the values
   * were never wrong. Both rules painted exactly the tokens they were asked
   * to paint, onto one element too many - so a gate pinned to today's token
   * values would have been green throughout, and would go red on a consumer
   * re-theming the card colour, which is legal.
   */
  it('no rule paints widget-card chrome without the t-surface-card marker', () => {
    /** The card root itself - not `__header`, and not `--bare`. */
    const CARD_ROOT = /\.t-widget-card(?![\w-])/
    /** `border-radius` is geometry, not chrome, and deliberately not listed. */
    const CHROME =
      /(?:^|[;{\s])(?:background(?:-color|-image)?|border(?:-color|-style|-width)?|box-shadow)\s*:/
    const offenders: string[] = []
    for (const file of files) {
      for (const [, selector, body] of css(file).matchAll(/([^{}]*)\{([^{}]*)\}/g)) {
        if (!CARD_ROOT.test(selector) || selector.includes('t-surface-card')) continue
        if (!CHROME.test(body)) continue
        offenders.push(`${relative(file)}: ${selector.trim().replace(/\s+/g, ' ')}`)
      }
    }
    expect(
      offenders,
      `Chrome keyed to a selector a bare widget also matches, so \`bare\` still paints a card. Add .t-surface-card: ${offenders.join(', ')}`,
    ).toEqual([])
  })
})
