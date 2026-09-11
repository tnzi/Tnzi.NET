import { describe, it, expect } from 'vitest'
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join } from 'node:path'

/**
 * Several `useCrudPage` shells mounted on ONE route must each name their own
 * `detailUrl` key.
 *
 * Every shell delegates to a `useDetail` that mirrors its open editor into the
 * URL under `?detail=edit:<id>` by default and reconciles that key on every
 * route change. Two shells on the same route therefore fight over one key:
 * shell A writes `?detail=edit:<id>`, shell B cannot resolve that id in its
 * own rows and deletes the key, and A's editor closes the moment it opens -
 * with no error anywhere. 2026-09-04: the feature-flags page shipped this
 * way; the same audit found Taxes (3 shells) and payroll Setup (4 shells)
 * co-mounted with `displayDirective: 'show'`.
 *
 * Unit tests cannot see it (they mount one tab, or stub the router), so the
 * rule is a source scan: any SFC with two or more `useCrudPage(` calls must
 * give every call a `detailUrl:` and the values must be distinct. Shells that
 * live in sibling SFCs under one TTabsPage host (the feature-flags shape) are
 * outside this scan's reach; ui-admin CLAUDE.md rule 10 covers them.
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

/** Slice each `useCrudPage(...)` options literal by brace balance. */
function crudOptionBlocks(src: string): string[] {
  const blocks: string[] = []
  const re = /useCrudPage(?:<[^>]*>)?\(\s*\{/g
  let m: RegExpExecArray | null
  while ((m = re.exec(src))) {
    let depth = 1
    let i = m.index + m[0].length
    while (i < src.length && depth > 0) {
      const ch = src[i]
      if (ch === '{') depth++
      else if (ch === '}') depth--
      i++
    }
    blocks.push(src.slice(m.index, i))
  }
  return blocks
}

describe('co-mounted CRUD shells', () => {
  const files = walk(pagesDir)
  const multi = files
    .map((f) => ({ f, blocks: crudOptionBlocks(readFileSync(f, 'utf8')) }))
    .filter((x) => x.blocks.length >= 2)

  it('scans a realistic page tree and finds the known multi-shell pages', () => {
    expect(files.length).toBeGreaterThan(100)
    // Taxes.vue and payroll/Setup.vue are the known cases; if this drops to 0 the
    // extractor broke, which is a different failure than "all clean".
    expect(multi.length).toBeGreaterThanOrEqual(2)
  })

  it('every shell on a shared route names its own distinct detailUrl', () => {
    const offenders: string[] = []
    for (const { f, blocks } of multi) {
      const keys = blocks.map((b) => /\bdetailUrl:\s*(['"])([^'"]+)\1/.exec(b)?.[2] ?? null)
      const rel = f.slice(pagesDir.length + 1).replace(/\\/g, '/')
      keys.forEach((k, i) => {
        if (k === null) offenders.push(`${rel}: shell #${i + 1} has no detailUrl`)
      })
      const named = keys.filter((k): k is string => k !== null)
      if (new Set(named).size !== named.length) offenders.push(`${rel}: duplicate detailUrl keys ${JSON.stringify(named)}`)
    }
    expect(
      offenders,
      'Two useCrudPage shells on one route share the default `detail` key; the second one closes the first ' +
        'one\'s editor on open. Give each a distinct `detailUrl`. Offenders:\n' + offenders.join('\n'),
    ).toEqual([])
  })
})
