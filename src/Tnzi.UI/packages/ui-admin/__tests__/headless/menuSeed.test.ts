import { describe, it, expect } from 'vitest'
import { exportRouteMenuSeed } from '../../src/headless/menuSeed'
import type { AdminMenuItem } from '../../src/stores/useAdminRouteStore'
import { MenuType } from '@tnzi/core/services/system'

describe('exportRouteMenuSeed', () => {
  it('flattens a menu tree into seed rows keyed by menuKey (route name)', () => {
    const menus: AdminMenuItem[] = [
      {
        key: 'identity',
        label: 'Identity',
        path: '/identity',
        icon: 'mdi:account',
        meta: { title: 'Identity', order: 100 },
        children: [
          { key: 'identity.users', label: 'Users', path: '/identity/users', icon: 'mdi:account-multiple', meta: { title: 'Users', order: 1 } },
          { key: 'identity.roles', label: 'Roles', path: '/identity/roles', meta: { title: 'Roles', order: 2 } },
        ],
      },
      { key: 'dashboard', label: 'Dashboard', path: '/dashboard', meta: { title: 'Dashboard', order: 0 } },
    ]
    const seed = exportRouteMenuSeed(menus)
    // flattened: identity + 2 children + dashboard = 4
    expect(seed).toHaveLength(4)
    const byKey = Object.fromEntries(seed.map((s) => [s.menuKey, s]))
    expect(byKey['identity.users'].name).toBe('Users')
    expect(byKey['identity.users'].icon).toBe('mdi:account-multiple')
    expect(byKey['identity.users'].sortOrder).toBe(1)
    expect(byKey['identity'].menuKey).toBe('identity')
    expect(byKey['dashboard'].name).toBe('Dashboard')
    // directory (has children) vs leaf typing
    expect(byKey['identity'].type).toBe(MenuType.Directory)
    expect(byKey['identity.users'].type).toBe(MenuType.Menu)
    expect(byKey['dashboard'].type).toBe(MenuType.Menu)
  })

  it('returns empty for empty menus', () => {
    expect(exportRouteMenuSeed([])).toEqual([])
  })

  it('every seed row carries a menuKey + name (flat, no parent linkage)', () => {
    const menus: AdminMenuItem[] = [
      {
        key: 'a',
        label: 'A',
        path: '/a',
        meta: { title: 'A' },
        children: [{ key: 'a.b', label: 'B', path: '/a/b', meta: { title: 'B' } }],
      },
    ]
    const seed = exportRouteMenuSeed(menus)
    expect(seed).toHaveLength(2)
    for (const row of seed) {
      expect(row.menuKey).toBeTruthy()
      expect(row.name).toBeTruthy()
      // seed is flat — no parentId linkage (merge matches by menuKey only)
      expect(row.parentId).toBeUndefined()
    }
  })
})
