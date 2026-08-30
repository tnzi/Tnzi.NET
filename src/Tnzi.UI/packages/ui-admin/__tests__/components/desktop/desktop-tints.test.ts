import { describe, it, expect, beforeEach } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import {
  DESKTOP_MODULE_TINTS,
  DESKTOP_TINT_PALETTE,
  desktopTileStyle,
  resolveDesktopTint,
  rootModuleKey,
  shadeTint,
  useDesktopTint,
} from '../../../src/components/desktop/desktop-tints'
import { useAdminRouteStore } from '../../../src/stores/useAdminRouteStore'

describe('rootModuleKey', () => {
  it('takes the segment before the first dot', () => {
    expect(rootModuleKey('identity.users')).toBe('identity')
    expect(rootModuleKey('finance.group.sales')).toBe('finance')
  })

  it('returns an undotted name whole', () => {
    expect(rootModuleKey('dashboard')).toBe('dashboard')
    expect(rootModuleKey(undefined)).toBe('')
  })
})

describe('resolveDesktopTint', () => {
  it('gives every page of a framework module the same hand-picked hue', () => {
    expect(resolveDesktopTint('identity')).toBe(DESKTOP_MODULE_TINTS.identity)
    expect(resolveDesktopTint('identity.users')).toBe(DESKTOP_MODULE_TINTS.identity)
    expect(resolveDesktopTint('identity.users.detail')).toBe(DESKTOP_MODULE_TINTS.identity)
  })

  it('lets an explicit meta.color win over everything', () => {
    expect(resolveDesktopTint('identity.users', '#123456')).toBe('#123456')
  })

  it('never hands an unknown module a hue the framework already claims', () => {
    // A consumer module landing on identity's violet puts two unrelated blocks
    // of the desktop in one colour - the exact confusion the tinting removes.
    const claimed = new Set(Object.values(DESKTOP_MODULE_TINTS))
    for (const tint of DESKTOP_TINT_PALETTE) expect(claimed.has(tint)).toBe(false)
  })

  it('hashes an unknown module into the palette, stably', () => {
    const first = resolveDesktopTint('warehouse')
    expect(DESKTOP_TINT_PALETTE).toContain(first as (typeof DESKTOP_TINT_PALETTE)[number])
    // Stability is the whole point: two people comparing screenshots must see
    // the same colour for the same page.
    expect(resolveDesktopTint('warehouse')).toBe(first)
  })
})

describe('shadeTint', () => {
  it('darkens a hex colour toward the bottom of the gradient', () => {
    expect(shadeTint('#ffffff')).toBe('rgb(158, 158, 158)')
  })

  it('returns a non-hex colour untouched so the gradient degrades to a flat fill', () => {
    // A consumer `meta.color` may be any CSS colour; throwing here would take
    // the whole icon grid down.
    expect(shadeTint('var(--brand)')).toBe('var(--brand)')
    expect(shadeTint('rebeccapurple')).toBe('rebeccapurple')
  })
})

describe('desktopTileStyle', () => {
  it('scales to the requested size and keeps the lit-surface shadows', () => {
    const style = desktopTileStyle('#3b82f6', 44, 10)
    expect(style.width).toBe('44px')
    expect(style.borderRadius).toBe('10px')
    expect(style.background).toContain('linear-gradient(160deg, #3b82f6')
    expect(style.boxShadow).toContain('inset')
  })
})

describe('useDesktopTint', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('groups by the menu tree, not by the route name', () => {
    const routeStore = useAdminRouteStore()
    routeStore.setAuthRoutes([
      {
        name: 'shop',
        path: '/shop',
        meta: { title: 'Shop' },
        children: [
          { name: 'shop-orders', path: 'orders', meta: { title: 'Orders' } },
          { name: 'shop-products', path: 'products', meta: { title: 'Products' } },
        ],
      },
    ])

    const tintFor = useDesktopTint()
    // Sibling pages sharing no dotted prefix - the name-only rule would give
    // each its own colour.
    expect(tintFor('shop-orders')).toBe(tintFor('shop-products'))
    expect(tintFor('shop-orders')).toBe(tintFor('shop'))
  })

  it('falls back to the route name for routes the tree cannot answer for', () => {
    const routeStore = useAdminRouteStore()
    routeStore.setAuthRoutes([])

    const tintFor = useDesktopTint()
    // A detail page reached by id is `hideInMenu` and so absent from the tree,
    // but it still has to look like the module it belongs to.
    expect(tintFor('identity.users.detail')).toBe(DESKTOP_MODULE_TINTS.identity)
  })

  it('still honours an explicit override', () => {
    useAdminRouteStore().setAuthRoutes([])
    expect(useDesktopTint()('identity.users', '#abcdef')).toBe('#abcdef')
  })
})
