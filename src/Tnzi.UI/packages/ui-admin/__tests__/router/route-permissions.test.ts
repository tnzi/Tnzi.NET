import { describe, it, expect } from 'vitest'
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs'
import { join, resolve } from 'node:path'
import type { RouteRecordRaw } from 'vue-router'
import { defaultAdminRoutes } from '../../src/router/routes'
import { defaultQuickActions, defaultWorkbenchWidgets } from '../../src/components/widgets/presets'

/**
 * Module group codes (`<module>.view` on a `moduleGate: true` node) are menu gates
 * by convention: no backend controller enforces them. A leaf page therefore must
 * carry the code the endpoints it calls actually enforce (`chat.session.view`,
 * `audit.operation.view`, ...), never the group code of its parent. A leaf that
 * reuses the group code passes the route guard for a role that holds only the
 * group code, mounts, and then every request it makes answers 403 - the bare
 * reads unwrap the refusal to `null`, so the page renders as "nothing here yet"
 * with no error anywhere (2026-09-12 audit, chat.overview).
 */

interface Offender {
  name: string
  permission: string
}

function collectLeafReusingParentGate(records: RouteRecordRaw[], parent?: RouteRecordRaw): Offender[] {
  const out: Offender[] = []
  for (const record of records) {
    const meta = record.meta ?? {}
    const parentMeta = parent?.meta ?? {}
    const isLeaf = !record.children?.length
    if (
      isLeaf &&
      parentMeta.moduleGate === true &&
      typeof meta.permission === 'string' &&
      meta.permission === parentMeta.permission
    ) {
      out.push({ name: String(record.name), permission: meta.permission })
    }
    if (record.children?.length) out.push(...collectLeafReusingParentGate(record.children, record))
  }
  return out
}

describe('route permission codes', () => {
  it('no leaf route reuses its parent module-gate permission code', () => {
    const offenders = collectLeafReusingParentGate(defaultAdminRoutes)
    expect(
      offenders,
      'A leaf page is gated by its module group code, which no controller enforces. Give it the code ' +
        'of the endpoints it calls. Offenders:\n' +
        offenders.map((o) => `${o.name} -> ${o.permission}`).join('\n'),
    ).toEqual([])
  })

  /**
   * The parent-gate rule above only catches a leaf that copies its group code.
   * A leaf can also carry a code that is DECLARED (it appears in a permission
   * provider, so the role editor offers it) but enforced by no controller:
   * `audit.log.view` gated the Logs page while its only data path answered to
   * `audit.operation.view`, so a role granted "View Audit Logs" opened a page
   * whose every request 403'd, and a role holding the enforced code could not
   * reach the page at all. The check below reads the codes the backend
   * controllers actually enforce (`[ApiAuthorize(PermissionName = "...")]`)
   * and requires every leaf code, and every widget code, to be one of them.
   */
  describe('leaf codes are enforced by a backend controller', () => {
    // Codes the backend enforces outside the `[ApiAuthorize]` attribute model,
    // each with the reason it is allowed here.
    const ENFORCED_ELSEWHERE = new Set<string>([
      // The health page reads the ASP.NET health endpoints (`/health*`), which
      // are mapped outside the API prefix and carry no permission gate; the
      // code is a menu gate only, and holding it gets no 403 anywhere.
      'system.health.view',
    ])

    // packages/ui-admin/__tests__/router -> repo `src/` (the backend modules
    // live beside `Tnzi.UI`, in the public mirror as well).
    const backendSrc = resolve(__dirname, '..', '..', '..', '..', '..')

    function collectEnforced(dir: string, out: Set<string>): void {
      for (const entry of readdirSync(dir)) {
        if (entry === 'node_modules' || entry === 'bin' || entry === 'obj' || entry === 'Tnzi.UI') continue
        const full = join(dir, entry)
        if (statSync(full).isDirectory()) {
          collectEnforced(full, out)
          continue
        }
        if (!entry.endsWith('.cs')) continue
        const text = readFileSync(full, 'utf8')
        for (const m of text.matchAll(/ApiAuthorize\(PermissionName\s*=\s*"([^"]+)"/g)) out.add(m[1])
      }
    }

    function collectLeafCodes(records: RouteRecordRaw[], parent?: RouteRecordRaw): { name: string; permission: string }[] {
      const out: { name: string; permission: string }[] = []
      for (const record of records) {
        const isLeaf = !record.children?.length
        const permission = record.meta?.permission
        if (isLeaf && typeof permission === 'string' && record.meta?.moduleGate !== true) {
          out.push({ name: String(record.name), permission })
        }
        if (record.children?.length) out.push(...collectLeafCodes(record.children, record))
      }
      return out
    }

    const enforced = new Set<string>()
    if (existsSync(backendSrc)) collectEnforced(backendSrc, enforced)

    it('reads the backend controllers (guards the scan itself)', () => {
      expect(enforced.size).toBeGreaterThan(100)
      expect(enforced).toContain('audit.operation.view')
    })

    it('every leaf route code is enforced by a controller', () => {
      const offenders = collectLeafCodes(defaultAdminRoutes).filter(
        (l) => !enforced.has(l.permission) && !ENFORCED_ELSEWHERE.has(l.permission),
      )
      expect(
        offenders,
        'A leaf page is gated by a code no controller enforces: a role granted it reaches a page whose ' +
          'requests all 403, and a role holding the enforced code cannot reach the page. Use the code of ' +
          'the endpoints the page calls. Offenders:\n' +
          offenders.map((o) => `${o.name} -> ${o.permission}`).join('\n'),
      ).toEqual([])
    })

    it('every dashboard widget and quick action code is enforced by a controller', () => {
      const offenders: string[] = []
      for (const widget of defaultWorkbenchWidgets()) {
        const p = widget.permission
        if (p && !enforced.has(p) && !ENFORCED_ELSEWHERE.has(p)) offenders.push(`widget ${widget.id} -> ${p}`)
      }
      for (const action of defaultQuickActions()) {
        const p = action.permission
        if (p && !enforced.has(p) && !ENFORCED_ELSEWHERE.has(p)) offenders.push(`quick action ${action.key} -> ${p}`)
      }
      expect(offenders).toEqual([])
    })
  })

  it('the walk sees the module groups it is meant to police (guards the scan itself)', () => {
    const gated: string[] = []
    const walk = (records: RouteRecordRaw[]): void => {
      for (const r of records) {
        if (r.meta?.moduleGate === true) gated.push(String(r.name))
        if (r.children?.length) walk(r.children)
      }
    }
    walk(defaultAdminRoutes)
    expect(gated.length).toBeGreaterThan(5)
    expect(gated).toContain('chat')
  })
})
