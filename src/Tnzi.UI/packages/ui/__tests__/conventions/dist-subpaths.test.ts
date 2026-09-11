import { describe, it, expect } from 'vitest'
import { existsSync, readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'

/**
 * Every `package.json` `exports` subpath must point at something the build
 * actually emits.
 *
 * Ported from `@tnzi/core`'s dist gate. The failure it exists for is silent:
 * an `exports` entry whose target is missing does not break this package's
 * own build or tests, it breaks the consumer's import at their build time,
 * with a message about a file that "should" be there. 2026-09-04 the theme
 * barrel was reachable in `dist/theme/` but had no `./theme` subpath, so a
 * sibling package kept a hand-copied `presetTnzi` instead of importing it.
 *
 * Runs against `dist/`, so it needs the package built first - the CI order
 * (build before test) already guarantees that.
 */

const pkgDir = join(__dirname, '..', '..')
const pkg = JSON.parse(readFileSync(join(pkgDir, 'package.json'), 'utf8')) as {
  exports: Record<string, unknown>
}

function targets(entry: unknown): string[] {
  if (typeof entry === 'string') return [entry]
  if (entry && typeof entry === 'object') return Object.values(entry as Record<string, unknown>).flatMap(targets)
  return []
}

describe('@tnzi/ui package exports', () => {
  const entries = Object.entries(pkg.exports)

  it('declares a realistic number of subpaths (guards the scan itself)', () => {
    expect(entries.length).toBeGreaterThan(8)
    expect(entries.map(([k]) => k)).toEqual(expect.arrayContaining(['./components', './adapters', './theme']))
  })

  it('every subpath target exists after a build', () => {
    const missing: string[] = []
    for (const [sub, entry] of entries) {
      for (const target of targets(entry)) {
        // Wildcard subpaths (`./adapters/*`, `./theme/presets/*`) are checked by
        // the directory the wildcard expands into.
        const probe = target.includes('*') ? dirname(target.replace(/\*.*$/, 'x')) : target
        if (!existsSync(join(pkgDir, probe))) missing.push(`${sub} -> ${target}`)
      }
    }
    expect(
      missing,
      'An `exports` entry points at a file the build does not emit. Add the barrel to ' +
        '`vite.config.ts` `build.lib.entry` (or fix the path). Missing:\n' + missing.join('\n'),
    ).toEqual([])
  })
})
