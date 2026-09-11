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
 * and nothing says why. 2026-09-04 audit: 27 such writes in five bridges, all
 * shaped `unwrap(await api.create(data)) as Dto`; the fix that same day in
 * `feature-bridge.ts` (ensureOk before unwrap) had not been swept across the
 * other bridges.
 *
 * The rule: any call whose method name starts with a write verb goes through
 * `unwrapOk` (ensureOk + unwrapResult) or an explicit `ensureOk(...)`. Reads
 * may keep bare `unwrap`. Page tests cannot catch this - they `vi.mock` the
 * whole bridge - so the check is a source scan.
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
]

// `unwrap(await api.create(` / `unwrap<T>(await api.runs.post(`.
//
// ★ It matches the REAL names (`unwrapUnchecked`, and the deprecated `unwrapResult`)
// as well as the `unwrap` alias the bridges happen to import them under. Matching only
// the alias made this gate depend on an import style nothing enforces: writing
// `unwrapUnchecked(await api.create(x))` sailed straight through it. `unwrapOk` and
// `unwrapData` are deliberately NOT matched - both assert before returning.
const BARE_UNWRAP_ON_WRITE = new RegExp(
  String.raw`\bunwrap(?:Unchecked|Result)?(?:<[^()]*?>)?\(\s*await\s+[A-Za-z_][A-Za-z0-9_.]*\.(?:${WRITE_VERBS.join('|')})[A-Za-z0-9_]*\(`,
)

const bridgesDir = join(__dirname, '..', '..', 'src', 'services', 'bridges')

describe('bridge write results', () => {
  const files = readdirSync(bridgesDir).filter((f) => f.endsWith('.ts') && f !== 'index.ts')

  it('scans a realistic number of bridge files (guards the scan itself)', () => {
    // Below this the directory moved or the filter broke; that is a different failure than "all clean".
    expect(files.length).toBeGreaterThan(20)
  })

  it('never unwraps a write-verb call without asserting success first', () => {
    const offenders: string[] = []
    for (const file of files) {
      const lines = readFileSync(join(bridgesDir, file), 'utf8').split('\n')
      lines.forEach((line, i) => {
        if (BARE_UNWRAP_ON_WRITE.test(line)) offenders.push(`${file}:${i + 1}  ${line.trim()}`)
      })
    }
    expect(
      offenders,
      'A write result went through bare `unwrap`. Use `unwrapOk(...)` (ensureOk + unwrapResult) so a ' +
        'business refusal throws instead of being announced as "saved". Offenders:\n' +
        offenders.join('\n'),
    ).toEqual([])
  })

  it('the helper the rule points at is exported from _mappers', () => {
    const mappers = readFileSync(join(bridgesDir, '..', '_mappers.ts'), 'utf8')
    expect(mappers).toMatch(/export \{[^}]*\bunwrapOk\b[^}]*\}/)
  })
})
