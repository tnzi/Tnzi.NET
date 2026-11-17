import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

/**
 * Below 768px a TCrudPage table becomes TDataCardList, and the card list is
 * the ONLY scroll container of a page-mode list on a phone. TTableRenderer puts
 * its own classes on the card list's root, so a rule it writes for
 * `.t-table-renderer--cards` outranks the card list's `overflow-y: auto` (two
 * classes against one). A shorthand `overflow: hidden` there once made every
 * phone list unscrollable: 20 rows, the first 2.5 cards reachable, wheel and
 * touch dead, and no unit test noticed because happy-dom applies no SFC CSS.
 *
 * This reads the two style blocks and resolves what the card list's root gets
 * on the vertical axis, in the order the cascade would: the card list's own
 * rule first, then any TTableRenderer rule that targets the cards element.
 */

const SRC = resolve(__dirname, '../../../src') + '/'

function styleBlock(file: string): string {
  const source = readFileSync(SRC + file, 'utf8')
  const match = source.match(/<style[^>]*>([\s\S]*?)<\/style>/)
  if (!match) throw new Error(`no <style> in ${file}`)
  return match[1]!.replace(/\/\*[\s\S]*?\*\//g, '')
}

interface Rule { selector: string; decls: Record<string, string> }

function rules(css: string): Rule[] {
  const out: Rule[] = []
  for (const m of css.matchAll(/([^{}@]+)\{([^{}]*)\}/g)) {
    const decls: Record<string, string> = {}
    for (const part of m[2]!.split(';')) {
      const i = part.indexOf(':')
      if (i > 0) decls[part.slice(0, i).trim()] = part.slice(i + 1).trim()
    }
    out.push({ selector: m[1]!.trim(), decls })
  }
  return out
}

/** The vertical overflow a rule sets, if any (`overflow-y`, else the shorthand's second / only value). */
function overflowY(decls: Record<string, string>): string | undefined {
  if (decls['overflow-y']) return decls['overflow-y']
  const shorthand = decls.overflow?.split(/\s+/)
  return shorthand ? (shorthand[1] ?? shorthand[0]) : undefined
}

describe('phone card list scrolls in page mode', () => {
  it('TDataCardList scrolls its own root vertically', () => {
    const root = rules(styleBlock('components/data/TDataCardList.vue')).find((r) => r.selector === '.t-data-cards')
    expect(root).toBeDefined()
    expect(overflowY(root!.decls)).toBe('auto')
  })

  it('no TTableRenderer rule on the cards element takes the vertical scroll away', () => {
    const cardsRules = rules(styleBlock('components/crud/renderers/TTableRenderer.vue'))
      .filter((r) => r.selector.split(',').some((s) => s.includes('t-table-renderer--cards')))
    expect(cardsRules.length).toBeGreaterThan(0)

    let resolved = 'auto' // the card list's own value, overridden by any later, more specific rule
    for (const rule of cardsRules) resolved = overflowY(rule.decls) ?? resolved
    expect(['auto', 'scroll']).toContain(resolved)
  })
})

/**
 * Nothing in a card may push the card wider than its list. Two shapes did:
 * TItemCard's meta line (`nowrap` with items that would not shrink) and a phone
 * card's value cell holding a control that cannot shrink (a segmented button
 * group): both overflowed at 375.
 */
describe('cards never overflow their container', () => {
  it('TItemCard meta wraps and its items shrink to the line', () => {
    const css = rules(styleBlock('components/data/TItemCard.vue'))
    const meta = css.find((r) => r.selector === '.t-item-card__meta')!
    expect(meta.decls['flex-wrap']).toBe('wrap')
    const item = css.find((r) => r.selector === '.t-item-card__meta > span')!
    expect(item.decls['min-width']).toBe('0')
    expect(item.decls['max-width']).toBe('100%')
    expect(item.decls['flex-shrink'] ?? item.decls.flex).not.toBe('0')
    const text = css.find((r) => r.selector === '.t-item-card__meta-text')!
    expect(text.decls['text-overflow']).toBe('ellipsis')
  })

  it('a phone card field lets a value too wide to sit beside its label take its own line', () => {
    const css = rules(styleBlock('components/data/TDataCardList.vue'))
    expect(css.find((r) => r.selector === '.t-data-cards__field')!.decls['flex-wrap']).toBe('wrap')
    const value = css.find((r) => r.selector === '.t-data-cards__value')!
    // `min-width: 0` would let a control that cannot shrink overflow instead of wrapping.
    expect(value.decls['min-width']).toBeUndefined()
    expect(value.decls['max-width']).toBe('100%')
    expect(value.decls.width).toBeUndefined()
  })
})
