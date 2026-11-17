// @vitest-environment node
/**
 * Convention gate: the package stylesheet restyles a page only when the page is
 * an app built on ui-ai.
 *
 * A few rules belong to the whole page of such an app (the body face and
 * canvas, the keyboard focus line, the colour scheme). They are written under
 * the marker `:root[data-tnzi-ai-app]` (headless/app-root.ts), which
 * `defineChatApp`, `TChatApp` and `markAiApp()` put on `<html>`. Written bare,
 * they would reach every page that imports the stylesheet: a host page that
 * embeds a ui-ai widget, or an admin app that lazy-loads one ui-ai component.
 */
import { describe, expect, it } from 'vitest'
import { readdirSync, readFileSync } from 'node:fs'
import { dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const pkgRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const srcRoot = join(pkgRoot, 'src')

function cssFiles(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) =>
    entry.isDirectory() ? cssFiles(join(dir, entry.name)) : entry.name.endsWith('.css') ? [join(dir, entry.name)] : [],
  )
}

/** Selectors of every rule in the plain stylesheets, comments removed and at-rule wrappers ignored. */
const selectors = cssFiles(srcRoot).flatMap((path) => {
  const css = readFileSync(path, 'utf8').replace(/\/\*[\s\S]*?\*\//g, '')
  return [...css.matchAll(/([^{}]+)\{[^{}]*\}/g)]
    .flatMap((m) => m[1]!.replace(/^[\s\S]*@[^{]*\{/, '').split(','))
    .map((s) => ({ file: relative(pkgRoot, path).replace(/\\/g, '/'), selector: s.trim().replace(/\s+/g, ' ') }))
    .filter((s) => s.selector && !s.selector.startsWith('@') && !/^(?:from|to|\d+%)$/.test(s.selector))
})

/** A selector that reaches the page itself rather than something ui-ai renders. */
function isPageWide(selector: string): boolean {
  const bare = selector.replace(/^:where\(([^)]*)\)/, '$1')
  return /^(?:html|body|\*|:focus|:focus-visible)\b/.test(bare) || (/^:root\b/.test(bare) && /\s/.test(bare))
}

describe('page-wide rules', () => {
  it('finds the stylesheets', () => {
    expect(selectors.length).toBeGreaterThan(50)
  })

  it('every rule that reaches the page is under the app marker', () => {
    const bare = selectors
      .filter(({ selector }) => isPageWide(selector))
      .filter(({ selector }) => !selector.replace(/^:where\(([^)]*)\)/, '$1').startsWith(':root[data-tnzi-ai-app]'))
      .map(({ file, selector }) => `${file}: ${selector}`)
    expect(bare).toEqual([])
  })

  it('the app has its body face, canvas and focus line', () => {
    const page = readFileSync(join(srcRoot, 'styles/page.css'), 'utf8')
    expect(page).toMatch(/:root\[data-tnzi-ai-app\] body \{[^}]*font-family: var\(--tnzi-ai-font-body\)/)
    expect(page).toMatch(/:root\[data-tnzi-ai-app\] body \{[^}]*background: var\(--tnzi-ai-bg\)/)
    expect(page).toMatch(/:where\(:root\[data-tnzi-ai-app\]\) :focus-visible \{[^}]*outline: var\(--tnzi-ai-focus-outline\)/)
  })
})
