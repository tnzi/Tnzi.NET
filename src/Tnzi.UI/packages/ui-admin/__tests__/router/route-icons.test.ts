import { describe, it, expect } from 'vitest'
import type { RouteRecordRaw } from 'vue-router'
import { defaultAdminRoutes } from '../../src/router/routes'
import { DEFAULT_ROUTE_ICONS } from '../../src/router/route-icons'

/**
 * Every route the sidebar lists has a glyph. The built-in route table sets no
 * `meta.icon`; the glyph comes from `DEFAULT_ROUTE_ICONS`, keyed by route
 * name, and a route added without a matching entry renders as a bare label
 * between iconed neighbours. Nothing else notices: the row still navigates,
 * the tests of the page still pass. Twice now a batch of new pages shipped
 * without entries (five storage pages, then two audit pages), so this is a
 * gate rather than a one-off reconciliation.
 */
function menuRoutesWithoutIcon(records: RouteRecordRaw[], out: string[] = []): string[] {
  for (const record of records) {
    const meta = record.meta ?? {}
    // Route-less redirects and hidden pages (detail routes reached by id) never appear in the menu.
    if (meta.hideInMenu || !record.name) continue
    const name = String(record.name)
    if (!meta.icon && !DEFAULT_ROUTE_ICONS[name]) out.push(name)
    if (record.children?.length) menuRoutesWithoutIcon(record.children, out)
  }
  return out
}

describe('route icons', () => {
  it('gives every menu-visible built-in route a glyph', () => {
    const admin = defaultAdminRoutes.find((r) => r.name === 'admin-root') ?? defaultAdminRoutes[0]
    const children = (admin?.children ?? []) as RouteRecordRaw[]
    expect(children.length).toBeGreaterThan(10)
    expect(menuRoutesWithoutIcon(children)).toEqual([])
  })
})
