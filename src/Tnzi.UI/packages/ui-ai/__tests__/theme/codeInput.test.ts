// @vitest-environment node
/**
 * `.t-input--code` (`controls.css`): a one-time code is 16px monospace with
 * tabular digits, and it comes after `input.t-input--md` at the same
 * specificity, so `t-input t-input--md t-input--code` is 36px tall with the
 * code's 16px digits rather than the larger field's 14px text.
 */
import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const css = readFileSync(resolve(dirname(fileURLToPath(import.meta.url)), '../../src/components/controls/controls.css'), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '')

describe('.t-input--code', () => {
  it('is 16px monospace with tabular digits', () => {
    const body = /input\.t-input--code\s*\{([^}]*)\}/.exec(css)?.[1] ?? ''
    expect(body).toMatch(/font-family:\s*var\(--tnzi-ai-font-mono\)/)
    expect(body).toMatch(/font-size:\s*16px/)
    expect(body).toMatch(/font-variant-numeric:\s*tabular-nums/)
  })

  it('comes after the 36px field rule, so its text size wins', () => {
    expect(css.indexOf('input.t-input--code')).toBeGreaterThan(css.indexOf('input.t-input--md'))
  })
})

describe('.t-select--bare', () => {
  it('has no box of its own and sits as wide as its value', () => {
    const body = /\.t-select\.t-select--bare\s*\{([^}]*)\}/.exec(css)?.[1] ?? ''
    expect(body).toMatch(/border:\s*none/)
    expect(body).toMatch(/background-color:\s*transparent/)
    expect(body).toMatch(/width:\s*auto/)
  })

  it('leaves focus to the inline field row it sits in', () => {
    expect(css).toMatch(/\.t-inline-field \.t-select\.t-select--bare:focus-visible\s*\{\s*outline:\s*none;/)
  })
})
