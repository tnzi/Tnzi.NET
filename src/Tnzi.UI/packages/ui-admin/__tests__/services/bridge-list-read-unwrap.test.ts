import { describe, it, expect } from 'vitest'
import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'

/**
 * Bridge list reads must not swallow a failure envelope.
 *
 * `HttpClient` resolves a refused or failed request (403, 404, 5xx, timeout)
 * as an envelope instead of rejecting, and the unchecked unwrap hands back its
 * `data` (undefined) or, for a body that is not an envelope, the failed result
 * itself. Behind the bridges' fallbacks (`toCrudResult(null)` -> empty page,
 * `paged?.items ?? []`, `Array.isArray(x) ? x : []`) that read as a real empty
 * list: "No data" on Users for a 403, an empty Settings or Scheduled Jobs list
 * for a 500, and `useCrudPage` never reached its error state or retry. The
 * bridges without a fallback dereferenced `result.items` and threw a TypeError,
 * which at least failed but lost the server's reason.
 *
 * The rule: a read that feeds a list page - a paged result (`PagedList<`,
 * `{ items`, `CrudPageResult<`, `PayrollPagedResult<`) or an array handed to
 * `pageArray` - goes through `unwrapOk` (or an explicit `ensureOk` first). Page
 * tests cannot catch this, they mock the whole bridge, so the check is a source
 * scan like the write-unwrap gate next to it.
 */

const UNCHECKED = String.raw`\bunwrap(?:Unchecked|Result)?`

/** An unchecked unwrap whose type argument is a paged shape. */
const PAGED_UNWRAP = new RegExp(
  String.raw`${UNCHECKED}<\s*(?:PagedList<|\{\s*items\b|CrudPageResult<|PayrollPagedResult<|FinancePagedResult<)`,
  'g',
)

/** How far above a `pageArray(` call the read that feeds it may sit. */
const PAGE_ARRAY_LOOKBACK_LINES = 6

function lineOf(source: string, index: number): number {
  return source.slice(0, index).split('\n').length
}

/** Index of the `)` closing the `(` at `open` (the end of `source` when unbalanced). */
function closingParenOf(source: string, open: number): number {
  let depth = 0
  for (let i = open; i < source.length; i++) {
    if (source[i] === '(') depth++
    else if (source[i] === ')' && --depth === 0) return i
  }
  return source.length - 1
}

/** Every offending read in `source`, reported as `line  text`. */
function findUncheckedListReads(source: string): string[] {
  const lines = source.split('\n')
  const hits = new Map<number, string>()
  for (const match of source.matchAll(PAGED_UNWRAP)) {
    const line = lineOf(source, match.index)
    hits.set(line, `${line}  ${lines[line - 1].trim()}`)
  }
  // An array read handed to `pageArray` a few lines later: the in-memory paging
  // of a whole-list endpoint (settings, scheduled jobs, function modules).
  const unchecked = new RegExp(`${UNCHECKED}(?:<[^()]*>)?\\(`)
  for (const match of source.matchAll(/\bpageArray\(/g)) {
    const at = lineOf(source, match.index)
    for (let line = Math.max(1, at - PAGE_ARRAY_LOOKBACK_LINES); line <= at; line++) {
      const text = lines[line - 1]
      if (unchecked.test(text) && !/\bensureOk\(/.test(lines[line - 2] ?? '')) {
        hits.set(line, `${line}  ${text.trim()}`)
      }
    }
    // ...or made inside the call itself, on the lines after `pageArray(`
    // (`pageArray(\n  unwrap<X[]>(await api.list()) ?? [],\n  query,\n)`).
    const end = closingParenOf(source, match.index + match[0].length - 1)
    const lastLine = lineOf(source, end)
    for (let line = at + 1; line <= lastLine; line++) {
      const text = lines[line - 1]
      if (unchecked.test(text)) hits.set(line, `${line}  ${text.trim()}`)
    }
  }
  return [...hits.entries()].sort(([a], [b]) => a - b).map(([, text]) => text)
}

const bridgesDir = join(__dirname, '..', '..', 'src', 'services', 'bridges')

function bridgeFiles(): Array<{ label: string; path: string }> {
  return readdirSync(bridgesDir)
    .filter((f) => f.endsWith('.ts') && f !== 'index.ts')
    .map((f) => ({ label: `bridges/${f}`, path: join(bridgesDir, f) }))
}

describe('bridge list reads', () => {
  const files = bridgeFiles()

  it('scans a realistic number of bridge files (guards the scan itself)', () => {
    expect(files.length).toBeGreaterThan(20)
  })

  it('the scan catches every unchecked list-read shape the bridges use (positive control)', () => {
    const bad = [
      'fetch: async (q) => toCrudResult(unwrap<PagedList<AgentDto>>(await agentApi.getList(mapQuery(q)))),',
      'const result = unwrap<{ items: PaymentDto[]; totalCount: number }>(\n  await pa.getList(params),\n)',
      'const result = unwrap<CrudPageResult<EnvelopeListDto>>(await a.getRequests(params))',
      'const result = unwrapUnchecked<PayrollPagedResult<EmployeeDto>>((await api.employees.getList(q)) as never)',
      'const raw = unwrap<SettingDto[]>(await settingApi.getList())\nconst source = Array.isArray(raw) ? raw : []\nreturn pageArray(source, query)',
      'const items = unwrapResult<ScheduledJobDto[]>(await jobs.getList()) ?? []\nreturn pageArray(items, query)',
      'fetch: async (query) =>\n  pageArray(\n    unwrap<CountryPackDto[]>(await api.countryPacks.getRegistered()) ?? [],\n    query,\n  ),',
    ]
    for (const fixture of bad) expect(findUncheckedListReads(fixture), fixture).toHaveLength(1)
  })

  it('the scan accepts asserting reads', () => {
    const good = [
      'fetch: async (q) => toCrudResult(unwrapOk<PagedList<AgentDto>>(await agentApi.getList(mapQuery(q)))),',
      'const result = unwrapOk<{ items: PaymentDto[]; totalCount: number }>(await pa.getList(params))',
      'const items = unwrapOk<ScheduledJobDto[]>(await jobs.getList()) ?? []\nreturn pageArray(items, query)',
      'const res = await valueApi.getAll(scope)\nensureOk(res)\nconst items = unwrap<FeatureValueDto[]>(res) ?? []\nreturn pageArray(items, query)',
      // A detail read is not a list read.
      'getById: async (id) => unwrap<AgentDto>(await agentApi.getById(String(id))),',
      // ...nor one that follows a finished `pageArray(...)` call.
      'fetch: async (q) => pageArray(unwrapOk<X[]>(await api.list()) ?? [], q),\ngetById: async (id) => unwrap<X>(await api.get(id)),',
    ]
    for (const fixture of good) expect(findUncheckedListReads(fixture), fixture).toEqual([])
  })

  it.each(bridgeFiles())('$label asserts success on every list read', ({ path }) => {
    const offenders = findUncheckedListReads(readFileSync(path, 'utf8').replace(/\r\n?/g, '\n'))
    expect(
      offenders,
      'A list read through the unchecked unwrap turns a 403 / 5xx into an empty list. Use unwrapOk. Offenders:\n' +
        offenders.join('\n'),
    ).toEqual([])
  })
})
