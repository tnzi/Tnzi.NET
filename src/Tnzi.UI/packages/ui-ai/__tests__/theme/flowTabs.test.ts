// @vitest-environment node
/**
 * The flowing tabs (`styles/page.css`): the default strip joins the content
 * under it (rounded at the top, curves at the foot opening downwards); the
 * `--bottom` variant joins the content above it, turned over. Read from the
 * stylesheet, since the join is drawn by pseudo-elements and gradients alone.
 */
import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const css = readFileSync(resolve(dirname(fileURLToPath(import.meta.url)), '../../src/styles/page.css'), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '')

const rules = [...css.matchAll(/([^{}]+)\{([^{}]*)\}/g)].map((m) => ({
  selector: m[1]!.replace(/\s+/g, ' ').trim(),
  body: m[2]!.replace(/\s+/g, ' ').trim(),
}))

/** The declarations of the rule written for exactly this selector. */
function body(selector: string): string {
  return rules.find((rule) => rule.selector === selector)?.body ?? ''
}

describe('flowing tabs', () => {
  it('on top: rounded at the top, the curves at the foot', () => {
    expect(body('.t-flow-tab')).toContain('border-radius: 8px 8px 0 0')
    expect(body('.t-flow-tab.is-on::before')).toContain('circle at 0 0')
    expect(body('.t-flow-tab.is-on::after')).toContain('circle at 100% 0')
  })

  it('at the bottom: rounded at the foot, the curves at the top opening upwards', () => {
    expect(body('.t-flow-tabs--bottom .t-flow-tab')).toContain('border-radius: 0 0 8px 8px')
    expect(body('.t-flow-tabs--bottom .t-flow-tab.is-on::before')).toContain('circle at 0 100%')
    expect(body('.t-flow-tabs--bottom .t-flow-tab.is-on::after')).toContain('circle at 100% 100%')
    expect(css).toMatch(/\.t-flow-tabs--bottom \.t-flow-tab\.is-on::before,\s*\.t-flow-tabs--bottom \.t-flow-tab\.is-on::after\s*\{\s*top: 0;\s*bottom: auto;/)
  })
})
