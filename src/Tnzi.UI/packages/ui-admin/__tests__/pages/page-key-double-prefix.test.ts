import { describe, it, expect } from 'vitest'
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join } from 'node:path'
import { translatePageKey } from '../../src/i18n/translate'

/**
 * A page-scoped key must be namespace-relative.
 *
 * `makePageTranslator('finance.recurring')` resolves a key that does not start
 * with `admin.` by prepending `admin.modules.finance.recurring.`. A key written
 * with the namespace already on it (`'finance.recurring.actions.end'`) is
 * therefore looked up at `admin.modules.finance.recurring.finance.recurring.actions.end`,
 * misses, and falls to `humanise` - which turns `endConfirm` into "End Confirm"
 * and `runNow` into "Run Now" in every locale. Nothing errors, English readers
 * see plausible words, and only the confirm text (or a non-English user) gives
 * it away. 2026-09-12: the six row actions on the Recurring page shipped this
 * way; every sibling page uses the short form.
 *
 * Two checks: the translator itself with a double-prefixed key (it now repairs
 * the slip, because consumer pages are outside any in-tree scan), and a source
 * scan for `label:` / `confirm:` / `title:` string literals that start with the
 * SFC's own page namespace (the built-in pages are the reference a consumer
 * copies, so they must show the short form).
 */

const pagesDir = join(__dirname, '..', '..', 'src', 'pages')

function walk(dir: string, out: string[] = []): string[] {
  for (const name of readdirSync(dir)) {
    const p = join(dir, name)
    if (statSync(p).isDirectory()) walk(p, out)
    else if (name.endsWith('.vue')) out.push(p)
  }
  return out
}

const NAMESPACE_RE = /makePageTranslator\(\s*(['"])([^'"]+)\1\s*\)/g

/** Every `<option>: '<ns>.…'` literal in `src` whose key repeats one of the SFC's own page namespaces. */
function doublePrefixedKeys(src: string): string[] {
  const namespaces = [...src.matchAll(NAMESPACE_RE)].map((m) => m[2]!)
  const hits: string[] = []
  for (const ns of namespaces) {
    const escaped = ns.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
    const re = new RegExp(String.raw`\b(label|confirm|title|placeholder|help|description):\s*(['"])(${escaped}\.[^'"]+)\2`, 'g')
    for (const m of src.matchAll(re)) {
      const line = src.slice(0, m.index).split('\n').length
      hits.push(`${line}  ${m[1]}: '${m[3]}' (namespace '${ns}' is already applied by the page translator)`)
    }
  }
  return hits
}

describe('page-scoped i18n keys are namespace-relative', () => {
  it('the short form resolves, and a key that repeats its own namespace is repaired rather than humanised', () => {
    // The global test setup preloads the bundled dictionaries (en is the default locale).
    expect(translatePageKey('finance.recurring', 'actions.endConfirm')).toContain('stops generating')
    expect(translatePageKey('finance.recurring', 'actions.runNow')).toBe('Run now')
    // The double-prefixed form used to miss and humanise to "End Confirm" / "Run Now".
    // The translator now resolves it once-prefixed (consumer pages are outside the
    // source scan below, so the runtime has to cope too).
    expect(translatePageKey('finance.recurring', 'finance.recurring.actions.endConfirm')).toContain('stops generating')
    expect(translatePageKey('finance.recurring', 'finance.recurring.actions.runNow')).toBe('Run now')
    // A genuinely absent key still humanises - the repair only fires on a hit.
    expect(translatePageKey('finance.recurring', 'finance.recurring.actions.noSuchKey')).toBe('No Such Key')
  })

  it('the scan sees the shape it is for', () => {
    const fixture =
      "const tp = makePageTranslator('finance.recurring')\n" +
      "const a = { key: 'end', label: 'finance.recurring.actions.end', confirm: 'finance.recurring.actions.endConfirm' }\n" +
      "const b = { key: 'run', label: 'actions.runNow' }"
    expect(doublePrefixedKeys(fixture)).toHaveLength(2)
    expect(doublePrefixedKeys("const tp = makePageTranslator('finance.bills')\nconst a = { label: 'actions.pay' }")).toEqual([])
  })

  it('no page passes a key that starts with its own namespace to the page translator', () => {
    const offenders: string[] = []
    for (const f of walk(pagesDir)) {
      const rel = f.slice(pagesDir.length + 1).replace(/\\/g, '/')
      for (const hit of doublePrefixedKeys(readFileSync(f, 'utf8'))) offenders.push(`${rel}:${hit}`)
    }
    expect(
      offenders,
      'A page-scoped key already carries the page namespace; the page translator prepends it again, the ' +
        'lookup misses and the label is humanised (wrong confirm text, English in every locale). Drop the ' +
        'namespace prefix. Offenders:\n' + offenders.join('\n'),
    ).toEqual([])
  })
})
