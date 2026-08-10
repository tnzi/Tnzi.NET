// @vitest-environment node
/**
 * Convention gate: at >= 768px the header is three columns - [back] [identity +
 * subtitle] [actions] - and the CENTRE column can never displace the actions.
 *
 * ## The property this locks
 *
 * Only the centre column is flexible, and it may shrink past its own content
 * (`min-width: 0`). The bar does not wrap, and the two side columns do not
 * shrink. Together those four declarations mean pressure from a long title, a
 * wide badge row or a long subtitle is absorbed INSIDE the centre column - as
 * wrapping or ellipsis - instead of pushing the actions onto a row of their own.
 * Drop any one of them and the guarantee is gone.
 *
 * ## Why it is asserted here and not by measuring
 *
 * happy-dom does no layout: `getBoundingClientRect()` returns zeros, so a test
 * that "measured" this would be a green lie. Comparing the declared values is
 * the part a unit test can actually see. The DOM half of the contract - that
 * `#extra` really does live in the same column as the identity - is asserted by
 * mounting, in TPageHeader.test.ts.
 *
 * ## History
 *
 * `#extra` used to be a full-width strip BELOW the whole bar, so it started at
 * the container's left edge while the title started after the back control. A
 * consuming app compensated with a hard-coded `padding-left: 36px`. A first fix
 * expressed that offset as CSS variables here; this structure removes the need
 * for an offset at all, and those variables are gone. Re-introducing an indent
 * on `.t-page-header__extra` would mean the columns had been broken again.
 */
import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, resolve } from 'node:path'

const pkgRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../../..')

/* Comments are stripped before any parsing: a `/* ... *\/` between two
   declarations otherwise hides the one after it from a "preceded by ; or start
   of block" match, and every rule below carries an explanation. */
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

const HEADER = 'src/components/layout/TPageHeader.vue'
const sfc = readFileSync(resolve(pkgRoot, HEADER), 'utf8')
const css = styleBlock(sfc)
const PHONE = atRuleBody(css, '@media (max-width: 767px)')
/* Everything outside the phone override - i.e. what >= 768px actually gets. */
const wide = css.replace(PHONE, '')

describe('TPageHeader three-column geometry', () => {
  it('has rules to check', () => {
    // Guards against a parser change silently turning every assertion below
    // into a vacuous pass over an empty stylesheet.
    expect(css).toContain('.t-page-header__main')
    expect(PHONE).toContain('.t-page-header__actions')
    expect(css.length).toBeGreaterThan(500)
  })

  // --- the containment guarantee: four declarations, all four required ---

  it('the bar does not wrap at wide widths', () => {
    // A wrapping bar is exactly how the actions used to end up on their own row.
    expect(decl(ruleBody(wide, '.t-page-header__bar'), 'flex-wrap')).toBe('nowrap')
  })

  it('the actions column never shrinks', () => {
    expect(decl(ruleBody(css, '.t-page-header__actions'), 'flex-shrink')).toBe('0')
  })

  it('the back column never shrinks', () => {
    expect(decl(ruleBody(css, '.t-page-header__back'), 'flex-shrink')).toBe('0')
  })

  it('the centre column is the only flexible one, and may shrink past its content', () => {
    const main = ruleBody(css, '.t-page-header__main')
    // `min-width: 0` defeats the automatic minimum size. Without it the column
    // refuses to go below min-content, the line overflows, and the actions move.
    expect(decl(main, 'min-width')).toBe('0')
    const flex = decl(main, 'flex')
    expect(flex).toBeDefined()
    // grow >= 1 and shrink >= 1 - it both fills the row and absorbs the pressure.
    const [grow, shrink] = (flex as string).split(/\s+/)
    expect(Number(grow)).toBeGreaterThanOrEqual(1)
    expect(Number(shrink)).toBeGreaterThanOrEqual(1)
  })

  // (How the pressure resolves inside the column - ellipsis at roomy widths,
  // wrapping on phones - is asserted by "flips the identity wrap" below. Either
  // way it stays inside the column: that is what the four rules above pin.)

  // --- the column structure itself ---

  it('stacks the identity and the subtitle in one column', () => {
    expect(decl(ruleBody(css, '.t-page-header__main'), 'flex-direction')).toBe('column')
  })

  it('centres the three columns against each other', () => {
    expect(decl(ruleBody(wide, '.t-page-header__bar'), 'align-items')).toBe('center')
  })

  it('gives the subtitle NO indent - it aligns by construction', () => {
    // The whole point of the restructure. An indent here would mean `#extra`
    // had been moved back out of the identity column.
    const extra = ruleBody(css, '.t-page-header__extra')
    expect(decl(extra, 'padding-left')).toBeUndefined()
    expect(decl(extra, 'padding-inline-start')).toBeUndefined()
    expect(decl(extra, 'margin-left')).toBeUndefined()
  })

  it('has no leftover indent machinery from the previous fix', () => {
    // The criterion was to remove these rather than leave them inert.
    expect(css).not.toContain('--tnzi-page-header-indent')
    expect(css).not.toContain('--tnzi-page-header-back-size')
    expect(css).not.toContain('--tnzi-page-header-left-gap')
    expect(sfc).not.toContain('t-page-header--has-back')
  })

  // --- narrow screens keep stacking ---

  it('lets the actions stack under the identity below 768px', () => {
    expect(PHONE).toMatch(/\.t-page-header__bar\s*\{[^}]*flex-wrap:\s*wrap/)
    expect(decl(ruleBody(PHONE, '.t-page-header:not(.t-page-header--inline-actions) .t-page-header__actions'), 'flex-basis')).toBe('100%')
  })

  // --- phone height budget: the header sits OUTSIDE the scroll container, so
  // every row is subtracted from the readable area outright. ---

  it('gives the centre column a ZERO basis on phones', () => {
    // With an `auto` basis the centre column's max-content width does not fit
    // beside the back control, the bar wraps, and the back arrow takes a whole
    // row to itself above the title - measured at 390px as 180 -> 220.
    const main = ruleBody(PHONE, '.t-page-header:not(.t-page-header--inline-actions) .t-page-header__main')
    expect(decl(main, 'flex-basis')).toBe('0')
  })

  it('top-aligns the side columns on phones', () => {
    // The identity block is routinely 2-3 rows tall here; centring puts the back
    // arrow beside the badges or the subtitle instead of beside the title.
    // Consuming apps were patching this themselves - the framework owns it now.
    expect(decl(ruleBody(PHONE, '.t-page-header__bar'), 'align-items')).toBe('flex-start')
  })

  it('flips the identity wrap between the two widths', () => {
    // Roomy: ellipsis is cheaper than a row. Phone: the name is worth the row.
    expect(decl(ruleBody(wide, '.t-page-header__left'), 'flex-wrap')).toBe('nowrap')
    expect(decl(ruleBody(PHONE, '.t-page-header__left'), 'flex-wrap')).toBe('wrap')
  })

  it('only ever wraps the bar inside the phone override', () => {
    // If `flex-wrap: wrap` leaked into the base rule the wide-screen guarantee
    // would be void while every other assertion here stayed green.
    expect(wide).not.toMatch(/\.t-page-header__bar\s*\{[^}]*flex-wrap:\s*wrap/)
  })
})
