/**
 * A list embedded in a tab does not draw a second card.
 *
 * The tab container (`.t-table-tabs`) is the card: it draws the card tier's
 * border + shadow pair and the 12px inset. The list's own card used to drop
 * only its shadow and padding, so under `surfaceStyle: 'outlined'` its border
 * stayed - a hairline 1px inside the tab border, with the toolbar's first
 * control and the refresh button pressed against it. The flattened card must
 * drop the WHOLE chrome (border, shadow, background) together with the inset,
 * at a specificity above the shell's own scoped card rules (`.x[data-v]`,
 * 0,2,0; its content padding `.x[data-v] .n-card-content`, 0,3,0).
 *
 * What is held here: the stylesheet says so. Where the toolbar actually lands
 * is a browser question (checked at 1280 / 375 under elevated and outlined).
 */
import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

const css = readFileSync(resolve(__dirname, '../../src/styles/polish.css'), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '')

/** Declarations of the first rule whose selector list contains `selector`, as a map. */
function declarationsOf(selector: string): Record<string, string> {
  const rules = [...css.matchAll(/([^{}]+)\{([^{}]*)\}/g)]
  const rule = rules.find(([, sel]) => sel.split(',').map((s) => s.trim()).includes(selector))
  expect(rule, `no rule for ${selector}`).toBeTruthy()
  return Object.fromEntries(
    rule![2]
      .split(';')
      .map((d) => d.trim())
      .filter(Boolean)
      .map((d) => [d.slice(0, d.indexOf(':')).trim(), d.slice(d.indexOf(':') + 1).trim()]),
  )
}

/** Class + attribute + pseudo-class count of a simple selector chain (the middle term of specificity). */
function classWeight(selector: string): number {
  return (selector.match(/\.[\w-]+|\[[^\]]+\]|:(?!:)[\w-]+/g) ?? []).length
}

describe('tab-embedded list card flattening', () => {
  const card = '.t-table-tabs .t-list-shell__list-card.n-card'
  const content = '.t-table-tabs .t-list-shell__list-card.n-card > .n-card__content'

  it('drops border, shadow and background together', () => {
    const d = declarationsOf(card)
    expect(d.border).toBe('none')
    expect(d['box-shadow']).toBe('none')
    expect(d.background).toBe('transparent')
  })

  it('drops the inner inset, so the tab container owns the only 12px', () => {
    expect(declarationsOf(content).padding).toBe('0')
    // The tab container carries that inset on the sides and bottom.
    expect(declarationsOf('.t-table-tabs').padding).toBe('0 12px 12px')
  })

  it("outranks the shell's own scoped card rules", () => {
    expect(classWeight(card)).toBeGreaterThan(classWeight('.t-list-shell__list-card[data-v-x]'))
    expect(classWeight(content)).toBeGreaterThan(classWeight('.t-list-shell__list-card[data-v-x] .n-card-content'))
  })
})
