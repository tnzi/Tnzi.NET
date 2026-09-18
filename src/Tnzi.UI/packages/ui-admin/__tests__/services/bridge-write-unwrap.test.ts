import { describe, it, expect } from 'vitest'
import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'

/**
 * Bridge writes must not swallow a failure envelope.
 *
 * `HttpClient` resolves a business refusal (HTTP 400 + `{ succeeded: false }`)
 * instead of rejecting, and `unwrapResult` alone turns that refusal into
 * `null`. `useCrudPage.submit` only hands *thrown* errors to the error path,
 * so a bridge that returns `null` from `create` gets a green "saved" toast, a
 * closed form and a refreshed list without the row - the user's input is gone
 * and nothing says why. 2026-09-04 audit (363e0c98): 155 such call sites across
 * 16 bridges, every one shaped `unwrap(await api.create(data)) as Dto`; the fix
 * that same day in `feature-bridge.ts` (ensureOk before unwrap) had not been
 * swept across the other bridges. (This header said "27 writes in five bridges"
 * from the day it was written until 2026-09-12; the commit message carried the
 * real count. A gate's own description is part of what it guards.)
 *
 * The rule: any call whose method name starts with a write verb goes through
 * `unwrapOk` (ensureOk + unwrapResult) or an explicit `ensureOk(...)`. Reads
 * may keep bare `unwrap`. Page tests cannot catch this - they `vi.mock` the
 * whole bridge - so the check is a source scan.
 *
 * ★ 2026-09-12: the first version of this scan required `await` to follow the
 * opening paren directly (`unwrap(await api.create(`), so the other shape the
 * bridges use - `unwrap<T>((await api.create(x)) as never)`, one wrapping paren
 * for the cast - sailed through it: 0 hits repo-wide while finance-bridge.ts held
 * 24 real write-path violations, the whole of the 09-04 sweep's blind spot. The
 * regex now tolerates that paren and newlines, the verb list has `end`, and the
 * positive-control test below feeds known-bad fixtures through the regex so a
 * future edit that quietly returns the gate to 0 hits reads as a failure, not
 * as "all clean".
 */

const WRITE_VERBS = [
  'create', 'update', 'delete', 'remove', 'assign', 'unassign', 'set', 'enable', 'disable', 'toggle',
  'reset', 'confirm', 'revoke', 'resend', 'send', 'save', 'mark', 'approve', 'reject', 'cancel', 'restore',
  'move', 'reorder', 'import', 'sync', 'clear', 'batch', 'grant', 'retry', 'activate', 'deactivate',
  'publish', 'unpublish', 'archive', 'unarchive', 'close', 'reopen', 'start', 'stop', 'pause', 'resume',
  'trigger', 'upsert', 'apply', 'release', 'complete', 'generate', 'seed', 'register', 'unregister', 'bind',
  'unbind', 'attach', 'detach', 'invite', 'accept', 'decline', 'lock', 'unlock', 'change', 'rotate', 'verify',
  'submit', 'post', 'void', 'pay', 'refund', 'issue', 'redeem', 'add', 'put', 'patch', 'kill', 'spawn', 'run',
  'execute', 'process', 'upload', 'rename', 'replace', 'getOrCreate', 'reprint', 'print', 'acknowledge',
  'claim', 'dismiss', 'adjust', 'transfer', 'merge', 'split', 'convert', 'duplicate', 'clone', 'copy',
  'resolve', 'reconcile', 'match', 'unmatch', 'exclude', 'include', 'record', 'capture', 'extract', 'calculate', 'ensure',
  'end',
  // 2026-09-12 second pass: verbs the 09-04 sweep never listed, so their sites were
  // converted only where a neighbour happened to share a listed verb (stopAbTest was
  // swept, configureAbTest two lines up was not). Every one of these is a POST that
  // changes server state.
  'reverse', 'spoil', 'refresh', 'pull', 'rebuild', 'reindex', 'rollback', 'configure', 'broadcast',
  'clean', 'cleanup', 'initiate',
  // 2026-09-12 third pass (independent review): two more POSTs that read like queries.
  // `bankFeed.suggest` runs the matching engine and WRITES suggested matches (auto-confirms
  // when a draft reconciliation is open); `cliRuntimes.probe` inserts/updates runtime rows
  // and answers 501 when the module is disabled. Both sat two lines from a converted
  // neighbour and survived the second pass purely because the verb was unlisted.
  'suggest', 'probe',
  // 2026-09-12 fourth pass: exports and downloads. Not writes, but every one of
  // them hands its result straight to a file (`downloadBlob`, `new Blob([JSON.
  // stringify(data)])`), so a refusal resolved to `undefined` is either a dead
  // button or a downloaded file whose body is the string `undefined` plus a
  // success toast. The backend's refusal message (row cap, "narrow the filter",
  // 403) has no other way to reach the user.
  'export', 'download',
]

// `unwrap(await api.create(` / `unwrap<T>(await api.runs.post(` /
// `unwrap<T>((await api.create(x)) as never)` / the same split across lines.
//
// ★ It matches the REAL names (`unwrapUnchecked`, and the deprecated `unwrapResult`)
// as well as the `unwrap` alias the bridges happen to import them under. Matching only
// the alias made this gate depend on an import style nothing enforces: writing
// `unwrapUnchecked(await api.create(x))` sailed straight through it. `unwrapOk` and
// `unwrapData` are deliberately NOT matched - both assert before returning.
//
// The verb must end at a camelCase boundary (`create(`, `createDraft(`, `runDue(`):
// a bare prefix match reported `attachmentCounts` and `runs` - both reads - as
// `attach` / `run`. Neither an allow-list nor "convert the reads too" fixes that;
// the match was simply wrong.
//
// ★ The receiver chain tolerates `!` and `?.` (`skillCategoryApi!.create(`) and the
// method tolerates an explicit type argument (`client.post<TDto>(`): both shapes are
// real, and both hid violations from the first two versions of this regex.
const BARE_UNWRAP_ON_WRITE = new RegExp(
  String.raw`\bunwrap(?:Unchecked|Result)?(?:<[^()]*?>)?\(\s*\(?\s*await\s+[A-Za-z_][A-Za-z0-9_.!?]*\.(?:${WRITE_VERBS.join('|')})(?:[A-Z0-9_][A-Za-z0-9_]*)?(?:<[^()]*?>)?\(`,
  'g',
)

// A `<Blob>` unwrap is an offender whatever the method is called (`calibration`,
// `templateSpecimen`, `print`): the only thing a page does with a Blob is write
// it to a file, and `HttpClient.download` resolves its failures.
const BARE_UNWRAP_OF_BLOB = /\bunwrap(?:Unchecked|Result)?<Blob>\(/g

/** Every offending call in `source`, reported as `line  text` (1-based line of the `unwrap`). */
function findBareWriteUnwraps(source: string): string[] {
  const lines = source.split('\n')
  const hits: string[] = []
  const seen = new Set<number>()
  for (const pattern of [BARE_UNWRAP_ON_WRITE, BARE_UNWRAP_OF_BLOB]) {
    for (const match of source.matchAll(pattern)) {
      if (seen.has(match.index)) continue
      seen.add(match.index)
      const line = source.slice(0, match.index).split('\n').length
      hits.push(`${line}  ${lines[line - 1].trim()}`)
    }
  }
  return hits
}

const servicesDir = join(__dirname, '..', '..', 'src', 'services')
const bridgesDir = join(servicesDir, 'bridges')

// The scan covers the built-in bridges AND the consumer-facing factories that live one
// level up (`defineCrudBridge.ts`). The factories are the file a consumer app is told to
// use for every plain REST resource, so a bare unwrap there is one defect multiplied by
// every consumer page - and until 2026-09-12 the scan never opened that file.
function scannedFiles(): Array<{ label: string; path: string }> {
  const bridges = readdirSync(bridgesDir)
    .filter((f) => f.endsWith('.ts') && f !== 'index.ts')
    .map((f) => ({ label: `bridges/${f}`, path: join(bridgesDir, f) }))
  const factories = readdirSync(servicesDir)
    .filter((f) => f.endsWith('.ts') && !f.endsWith('.d.ts'))
    .map((f) => ({ label: f, path: join(servicesDir, f) }))
  return [...bridges, ...factories]
}

describe('bridge write results', () => {
  const files = scannedFiles()

  it('scans a realistic number of bridge files (guards the scan itself)', () => {
    // Below this the directory moved or the filter broke; that is a different failure than "all clean".
    expect(files.length).toBeGreaterThan(20)
    expect(files.map((f) => f.label)).toContain('defineCrudBridge.ts')
  })

  // Positive control: the regex must see every shape the bridges actually write, or a
  // 0-hit run is indistinguishable from "all clean" (exactly how 24 sites survived 09-04).
  it('the regex catches every bare-unwrap shape the bridges use', () => {
    const bad = [
      'create: async (data) => unwrap(await api.create(data)) as Dto,',
      'create: async (data) => unwrap<TDto>((await api.create(data)) as never),',
      'end: async (id) => unwrapUnchecked<Dto>((await api.end(id)) as never),',
      'post: async (id) => unwrapResult<Dto>(await api.runs.post(id)),',
      'runDue: async (asOf) => unwrap<Dto>(\n  (await api.runDue(asOf)) as never,\n),',
      'convert: async (id, data) => unwrap<Record<string, number> | null>((await api.convert(id, data)) as never),',
      // Second-pass shapes (2026-09-12): a non-null-asserted receiver, an explicit type
      // argument on the method, and the verbs the first list never had.
      'create: async (data) => unwrap<SkillCategoryDto>(await skillCategoryApi!.create(data)),',
      'return unwrapResult<TDto>(await client.post<TDto>(base, createBody(data)))',
      'broadcast: async (dto) => unwrap<number>(await broadcastApi.broadcast(dto)),',
      'rollbackToVersion: async (id, version) =>\n  unwrap<AgentDto>(await agentApi.rollbackToVersion(String(id), version)),',
      'cleanExpired: async (m) => unwrap(await sessionApi?.cleanExpired(m)) as number,',
      'reverse: async (id, data) => unwrap<JournalEntryDto>(await journalApi.reverse(id, data)),',
      // Third pass: the two query-looking POSTs the review found at HEAD.
      'suggest: async (accountId) => unwrap<BankSuggestResultDto>(await api.suggest(accountId)),',
      'return unwrap<CliRuntimeProbeResultDto>(await runtimeApi.probe())',
      // Fourth pass: exports and downloads. `HttpClient.download` resolves a failed
      // envelope with `data: undefined`, so a bare `unwrap<Blob>` hands the page
      // `undefined` and the page either does nothing (TListShell) or throws a
      // TypeError out of createObjectURL. Any `<Blob>` unwrap is an offender
      // regardless of the method name, and so is any export*/download* call.
      'export: async (q) => unwrap<Blob>(await userApi.exportCsv(mapQuery(q) as unknown as UserListQueryDto)),',
      'download: async (id: string) => unwrapUnchecked<Blob>(await api.download(id)),',
      'calibration: async (bankAccountId: string) => unwrap<Blob>(await api.calibration(bankAccountId)),',
      'templateSpecimen: async (name) => unwrap<Blob>(await api.getTemplateSpecimen(name, options)),',
      'exportPersonalData: async () => unwrap(await profileApi.exportPersonalData()) as PersonalDataExportDto,',
      'exportJson: async (id) => unwrap<ThreadExportDto>(await threadApi.exportJson(String(id))),',
    ]
    for (const fixture of bad) expect(findBareWriteUnwraps(fixture), fixture).toHaveLength(1)
  })

  it('the regex ignores asserting helpers and reads', () => {
    const good = [
      'create: async (data) => unwrapOk<TDto>((await api.create(data)) as never),',
      'create: async (data) => unwrapOk(await api.create(data)) as Dto,',
      'print: async (data: CorePrintChecksDto) => unwrapOk<Blob>(await api.print(data)),',
      // `exportable` / `downloads` are reads: the camelCase boundary applies to the new verbs too.
      'exportable: async () => unwrap<Dto>(await api.exportable()),',
      'downloads: async (q) => unwrap<Paged | null>(await api.downloads(q)),',
      'create: async (data) => unwrapData(await api.create(data)),',
      'getById: async (id) => unwrap<TDto | null>((await api.get(id)) as never),',
      // Prefix of a verb is not the verb: `attachmentCounts` / `runs` / `settings` are reads.
      'counts: async () => unwrap<Record<string, number> | null>((await api.attachmentCounts(t, ids)) as never) ?? {},',
      'runs: async (q) => unwrap<Paged | null>((await api.runs(q)) as never),',
      'settings: async () => unwrap<Dto>(await api.settings()),',
      // `dunning` is `GET .../dunning` (candidates list) and `refreshToken`-style reads
      // are not `refresh(`: the camelCase boundary still applies to the new verbs.
      'dunning: async (t, asOf) => unwrap<DunningCandidateDto[] | null>((await api.dunning(t, asOf)) as never) ?? [],',
      'refreshable: async () => unwrap<Dto>(await api.refreshable()),',
      // `suggestions` (a GET list) is a read; the camelCase boundary must keep the new
      // `suggest` verb from matching its prefix.
      'suggestions: async (id) => unwrap<SuggestionDto[] | null>(await api.suggestions(id)) ?? [],',
      'fetch: async (q) => unwrapResult<PagedEnvelope<TDto>>(await client.get<PagedEnvelope<TDto>>(base)),',
    ]
    for (const fixture of good) expect(findBareWriteUnwraps(fixture), fixture).toEqual([])
  })

  it('never unwraps a write-verb call without asserting success first', () => {
    const offenders: string[] = []
    for (const file of files) {
      const source = readFileSync(file.path, 'utf8')
      for (const hit of findBareWriteUnwraps(source)) offenders.push(`${file.label}:${hit}`)
    }
    expect(
      offenders,
      'A write result went through bare `unwrap`. Use `unwrapOk(...)` (ensureOk + unwrapResult) so a ' +
        'business refusal throws instead of being announced as "saved". Offenders:\n' +
        offenders.join('\n'),
    ).toEqual([])
  })

  it('the helper the rule points at is exported from _mappers', () => {
    const mappers = readFileSync(join(servicesDir, '_mappers.ts'), 'utf8')
    expect(mappers).toMatch(/export \{[^}]*\bunwrapOk\b[^}]*\}/)
  })
})
