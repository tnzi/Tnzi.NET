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
 *
 * ★ 2026-09-12, third shape: ONE `useCrudPage` (whose internal engine claims
 * `detail` by default) plus an explicit page-level `useDetail({ url: 'detail' })`
 * for a read-only drawer. Same clobber, different pair: a refreshed or shared
 * `?detail=view:<id>` for a record outside the loaded page is resolved by the
 * page engine (it has `loadData`) and then wiped by the crud engine (no
 * `loadDetailById`, id not in `items`), so the drawer closes or never opens.
 * Deposits / EftBatches / Receipts shipped this way; the first rule counts
 * `useCrudPage(` blocks and saw one, so it never looked. The second rule below
 * collects every deep-link key an SFC claims - from crud shells AND bare
 * `useDetail` calls - and requires them to be distinct.
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

/** Slice each `<call>(...)` options literal by brace balance. */
function optionBlocks(src: string, call: string): string[] {
  const blocks: string[] = []
  const re = new RegExp(String.raw`\b${call}(?:<[^>]*>)?\(\s*\{`, 'g')
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

const crudOptionBlocks = (src: string) => optionBlocks(src, 'useCrudPage')
const detailOptionBlocks = (src: string) => optionBlocks(src, 'useDetail')

/**
 * The URL query key one options block claims for its overlay, or null when it opts
 * out. `useCrudPage` defaults to `'detail'` (absent or `true`), `useDetail` defaults
 * to off (absent or `false`); `true` means `'detail'` on both; a string names the key.
 */
function claimedDetailKey(block: string, option: 'detailUrl' | 'url', defaultOn: boolean): string | null {
  const m = new RegExp(String.raw`\b${option}:\s*(?:(true|false)|(['"])([^'"]+)\2)`).exec(block)
  if (!m) return defaultOn ? 'detail' : null
  if (m[1] === 'true') return 'detail'
  if (m[1] === 'false') return null
  return m[3] ?? null
}

/** Every deep-link key an SFC claims, in source order, tagged with its engine. */
function claimedDetailKeys(src: string): Array<{ engine: string; key: string }> {
  const claims: Array<{ engine: string; key: string }> = []
  crudOptionBlocks(src).forEach((b, i) => {
    const key = claimedDetailKey(b, 'detailUrl', true)
    if (key) claims.push({ engine: `useCrudPage #${i + 1}`, key })
  })
  detailOptionBlocks(src).forEach((b, i) => {
    const key = claimedDetailKey(b, 'url', false)
    if (key) claims.push({ engine: `useDetail #${i + 1}`, key })
  })
  return claims
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
        "one's editor on open. Give each a distinct `detailUrl`. Offenders:\n" + offenders.join('\n'),
    ).toEqual([])
  })
})

describe('deep-link keys claimed by one SFC', () => {
  it('the key extractor reads every option shape', () => {
    expect(claimedDetailKey("useCrudPage<Row>({ pageId: 'x', fetchData })", 'detailUrl', true)).toBe('detail')
    expect(claimedDetailKey('useCrudPage<Row>({ detailUrl: false, fetchData })', 'detailUrl', true)).toBeNull()
    expect(claimedDetailKey("useCrudPage<Row>({ detailUrl: 'rates' })", 'detailUrl', true)).toBe('rates')
    expect(claimedDetailKey("useDetail<Dto>({ mode: 'drawer', loadData })", 'url', false)).toBeNull()
    expect(claimedDetailKey("useDetail<Dto>({ mode: 'drawer', url: true })", 'url', false)).toBe('detail')
    expect(claimedDetailKey("useDetail<Dto>({ mode: 'drawer', url: 'detail', loadData })", 'url', false)).toBe('detail')
    expect(claimedDetailKey("useDetail<Dto>({ url: 'void' })", 'url', false)).toBe('void')
    // Positive control: the third shape (one shell + one bare useDetail on `detail`).
    const fixture =
      "const crud = useCrudPage<Row>({ pageId: 'x', fetchData })\n" +
      "const detail = useDetail<Dto>({ mode: 'drawer', url: 'detail', loadData: (id) => api.get(id) })"
    expect(claimedDetailKeys(fixture).map((c) => c.key)).toEqual(['detail', 'detail'])
  })

  it('no SFC claims the same query key from two engines', () => {
    const offenders: string[] = []
    for (const f of walk(pagesDir)) {
      const claims = claimedDetailKeys(readFileSync(f, 'utf8'))
      const byKey = new Map<string, string[]>()
      for (const c of claims) byKey.set(c.key, [...(byKey.get(c.key) ?? []), c.engine])
      for (const [key, engines] of byKey) {
        if (engines.length > 1) {
          const rel = f.slice(pagesDir.length + 1).replace(/\\/g, '/')
          offenders.push(`${rel}: ?${key}= claimed by ${engines.join(' and ')}`)
        }
      }
    }
    expect(
      offenders,
      'Two overlay engines on one route reconcile the same URL key and clobber each other: a refreshed ' +
        '`?detail=view:<id>` is wiped by the engine that cannot resolve the id. Pass `detailUrl: false` to a ' +
        'useCrudPage whose overlay the page never opens, or give the bare useDetail its own `url`. Offenders:\n' +
        offenders.join('\n'),
    ).toEqual([])
  })
})
