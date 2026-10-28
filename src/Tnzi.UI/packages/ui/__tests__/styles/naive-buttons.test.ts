/**
 * Naive buttons keep their fill whatever reset the application loads.
 *
 * Naive inserts its styles at the start of `<head>`, so an application reset
 * at the same specificity (`[type='button'] { background-color: transparent }`
 * in Tailwind / UnoCSS preflights) wins on source order and turns a primary
 * button into white text on nothing. The restore lives in this package's
 * stylesheet, which `@tnzi/ui-admin` and `@tnzi/ui-ai` both import; it used to
 * live only in the admin package, so the chat package's buttons were exposed.
 *
 * What is held here: the stylesheet ships the restore, at a specificity above
 * a single class, for every state naive paints. Whether it renders is a
 * browser question.
 */
import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

const SRC = resolve(__dirname, '../../src')
const stylesIndex = readFileSync(resolve(SRC, 'styles/index.css'), 'utf8')
const restore = readFileSync(resolve(SRC, 'styles/naive-buttons.css'), 'utf8').replace(/\/\*[\s\S]*?\*\//g, '')

describe('naive button restore', () => {
  it('is part of the package stylesheet', () => {
    expect(stylesIndex).toMatch(/@import\s+['"]\.\/naive-buttons\.css['"]/)
  })

  it.each([
    ['rest', /\.n-button\.n-button\s*\{\s*background-color:\s*var\(--n-color\)/],
    ['hover', /\.n-button\.n-button:hover\s*\{\s*background-color:\s*var\(--n-color-hover\)/],
    ['pressed', /\.n-button\.n-button\.n-button--pressed\s*\{\s*background-color:\s*var\(--n-color-pressed\)/],
    ['focus', /\.n-button\.n-button:focus\s*\{\s*background-color:\s*var\(--n-color-focus\)/],
    ['disabled', /\.n-button\.n-button\.n-button--disabled\s*\{\s*background-color:\s*var\(--n-color-disabled\)/],
  ])('restores the %s fill with a doubled class', (_state, rule) => {
    expect(restore).toMatch(rule)
  })

  it('does not touch the border, which naive draws with a pseudo element', () => {
    expect(restore).not.toMatch(/\bborder\s*:/)
  })
})
