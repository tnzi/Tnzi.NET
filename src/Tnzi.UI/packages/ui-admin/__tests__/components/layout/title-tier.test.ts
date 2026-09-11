// @vitest-environment node
/**
 * Convention gate: the content-page title has exactly TWO tiers, and each of
 * them is stated once, in tokens.
 *
 * ## The property this locks
 *
 * `TPageHeader` (page identity) and `TDetailSection` (one region inside it) are
 * a deliberate two-tier hierarchy - a `side`/`tabs` detail page renders both at
 * once, the header naming the record above a panel naming the part of it you
 * are looking at. What was NOT deliberate is stating each tier as bare literals
 * in two scoped stylesheets, which is how they came to disagree: the page tier
 * had a 767px step and the section tier had none, so on a phone they both
 * landed on 16px and the hierarchy silently disappeared.
 *
 * So: neither title rule may hard-code its own size/weight/tracking, and both
 * tiers must step at the same breakpoint.
 *
 * ## Why it is asserted against the CSS source
 *
 * happy-dom applies no stylesheet from an SFC and does no layout, so a mounted
 * test cannot see a font-size at all. Comparing the declared values is the part
 * a unit test can actually see. The DOM half - that the section modifier lands
 * on the element, and that a header inside a detail panel picks the tier up
 * without a prop at the call site - is asserted by mounting, in
 * TPageHeader.test.ts and TDetailLayout.test.ts.
 */
import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, resolve } from 'node:path'

const pkgRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../../..')
const read = (rel: string) => readFileSync(resolve(pkgRoot, rel), 'utf8')

/* Comments are stripped before any parsing: every rule below carries an
   explanation, and a comment between two declarations otherwise hides the one
   after it from a "preceded by ; or start of block" match. */
const stripComments = (css: string) => css.replace(/\/\*[\s\S]*?\*\//g, '')

/** Only the `<style>` block is CSS; the rule matcher anchors on `}` or block start. */
function styleBlock(sfc: string): string {
  const match = /<style[^>]*>([\s\S]*?)<\/style>/.exec(sfc)
  if (!match) throw new Error('no <style> block')
  return stripComments(match[1])
}

/** Body of an at-rule, by brace matching - a regex cannot handle the nesting. */
function atRuleBody(css: string, prelude: string): string {
  const start = css.indexOf(prelude)
  if (start < 0) throw new Error(`no at-rule ${prelude}`)
  const open = css.indexOf('{', start)
  let depth = 0
  for (let i = open; i < css.length; i++) {
    if (css[i] === '{') depth++
    else if (css[i] === '}' && --depth === 0) return css.slice(open + 1, i)
  }
  throw new Error(`unbalanced braces after ${prelude}`)
}

/**
 * Body of the rule whose selector is exactly `selector`.
 *
 * Anchored on the end of the previous block (or the start of the sheet) so a
 * short selector matches its own rule rather than the tail of a compound one.
 */
function ruleBody(css: string, selector: string): string {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  const match = new RegExp(`(?:^|\\})\\s*${escaped}\\s*\\{([^}]*)\\}`).exec(css)
  if (!match) throw new Error(`no rule for selector ${selector}`)
  return match[1]
}

/** Value of `prop` inside a rule body, or undefined when it is not declared. */
function decl(body: string, prop: string): string | undefined {
  const match = new RegExp(`(?:^|;)\\s*${prop}\\s*:\\s*([^;]+)`).exec(body)
  return match?.[1].trim()
}

const tokens = stripComments(read('src/styles/variables.css'))
const headerCss = styleBlock(read('src/components/layout/TPageHeader.vue'))
const sectionCss = styleBlock(read('src/components/detail/TDetailSection.vue'))

const PHONE = '@media (max-width: 767px)'
const TIERS = ['page', 'section'] as const
const FACETS = ['size', 'weight', 'tracking'] as const

/** Declared px value of one tier's size within the given slice of the sheet. */
function tierSize(css: string, tier: string): number {
  const value = decl(ruleBody(css, ':root'), `--tnzi-admin-title-${tier}-size`)
  if (!value) throw new Error(`no --tnzi-admin-title-${tier}-size`)
  return parseFloat(value)
}

describe('content-page title tiers', () => {
  it('has rules to check', () => {
    // Guards against a parser change turning every assertion below into a
    // vacuous pass over an empty stylesheet.
    expect(headerCss).toContain('.t-page-header__title')
    expect(sectionCss).toContain('.t-detail-section__title')
    expect(tokens).toContain('--tnzi-admin-title-page-size')
    expect(tokens.length).toBeGreaterThan(500)
  })

  // --- each tier is stated once, in tokens ---

  it.each(TIERS)('declares every %s-tier typography token', (tier) => {
    const root = ruleBody(tokens, ':root')
    for (const facet of FACETS) {
      expect(decl(root, `--tnzi-admin-title-${tier}-${facet}`)).toBeTruthy()
    }
  })

  it('sizes the page-header title from the tier tokens, never a literal', () => {
    const title = ruleBody(headerCss, '.t-page-header__title')
    // Indirected through a per-component local so the tier can be swapped by
    // rebinding three values instead of restating the whole rule - but the
    // value must still trace back to a token.
    expect(decl(title, 'font-size')).toBe('var(--t-page-header-title-size)')
    expect(decl(title, 'font-weight')).toBe('var(--t-page-header-title-weight)')
    expect(decl(title, 'letter-spacing')).toBe('var(--t-page-header-title-tracking)')

    const base = ruleBody(headerCss, '.t-page-header')
    const section = ruleBody(headerCss, '.t-page-header--section')
    for (const facet of FACETS) {
      expect(decl(base, `--t-page-header-title-${facet}`)).toBe(`var(--tnzi-admin-title-page-${facet})`)
      expect(decl(section, `--t-page-header-title-${facet}`)).toBe(`var(--tnzi-admin-title-section-${facet})`)
    }
  })

  it('sizes the detail-section title from the SAME section tokens', () => {
    const title = ruleBody(sectionCss, '.t-detail-section__title')
    expect(decl(title, 'font-size')).toBe('var(--tnzi-admin-title-section-size)')
    expect(decl(title, 'font-weight')).toBe('var(--tnzi-admin-title-section-weight)')
    expect(decl(title, 'letter-spacing')).toBe('var(--tnzi-admin-title-section-tracking)')
  })

  // --- the tier survives the narrow screen ---

  it('steps BOTH tiers at the same breakpoint', () => {
    const phone = atRuleBody(tokens, PHONE)
    // A step on one tier only is what made them meet at 16px on a phone.
    expect(tierSize(phone, 'page')).toBeGreaterThan(0)
    expect(tierSize(phone, 'section')).toBeGreaterThan(0)
  })

  it('keeps the page tier above the section tier at every width', () => {
    const phone = atRuleBody(tokens, PHONE)
    const wide = tokens.replace(phone, '')
    expect(tierSize(wide, 'page')).toBeGreaterThan(tierSize(wide, 'section'))
    expect(tierSize(phone, 'page')).toBeGreaterThan(tierSize(phone, 'section'))
    // And the step really is a step down, not a decorative restatement.
    expect(tierSize(phone, 'page')).toBeLessThan(tierSize(wide, 'page'))
    expect(tierSize(phone, 'section')).toBeLessThan(tierSize(wide, 'section'))
  })

  it('leaves no hard-coded phone size override on either title', () => {
    // The step belongs with the tokens; a second one in a component would
    // apply to only one tier again.
    const headerPhone = atRuleBody(headerCss, PHONE)
    expect(headerPhone).not.toMatch(/\.t-page-header__title\s*\{[^}]*font-size/)
    // (The section's own 640px block stacks its bar - it must not also resize
    // the title, or the two tiers get a second, unsynchronised breakpoint.)
    expect(atRuleBody(sectionCss, '@media (max-width: 640px)')).not.toContain('.t-detail-section__title')
    expect(sectionCss).not.toMatch(/\.t-detail-section__title\s*\{[^}]*font-size:\s*\d/)
  })
})
