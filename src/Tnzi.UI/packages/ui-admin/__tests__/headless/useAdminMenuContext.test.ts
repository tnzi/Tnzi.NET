import { computed, ref, type ComputedRef } from 'vue'
import { describe, it, expect } from 'vitest'
import {
  useAdminMenuContext,
  type AdminMenuContext,
} from '../../src/headless/useAdminMenuContext'
import type { AdminMenuItem } from '../../src/stores/useAdminRouteStore'

function makeMenus(): AdminMenuItem[] {
  return [
    {
      key: 'workbench',
      label: 'Workbench',
      path: '/admin/workbench',
    },
    {
      key: 'identity',
      label: 'Identity',
      path: '/admin/identity',
      children: [
        { key: 'users', label: 'Users', path: '/admin/identity/users' },
        {
          key: 'roles',
          label: 'Roles',
          path: '/admin/identity/roles',
          children: [
            { key: 'roles-list', label: 'List', path: '/admin/identity/roles/list' },
            { key: 'roles-tree', label: 'Tree', path: '/admin/identity/roles/tree' },
          ],
        },
      ],
    },
    {
      key: 'system',
      label: 'System',
      path: '/admin/system',
      children: [{ key: 'menus', label: 'Menus', path: '/admin/system/menus' }],
    },
  ]
}

function makeContext(initialRoute = 'users'): {
  ctx: AdminMenuContext
  routeName: ReturnType<typeof ref<string>>
  menus: ComputedRef<AdminMenuItem[]>
} {
  const menusRef = ref(makeMenus())
  const menus = computed(() => menusRef.value)
  const routeName = ref<string>(initialRoute)
  const ctx = useAdminMenuContext({
    menus,
    routeName: computed(() => routeName.value ?? ''),
  })
  return { ctx, routeName, menus }
}

describe('useAdminMenuContext', () => {
  it('exposes 1st level menu list directly from input', () => {
    const { ctx } = makeContext()
    expect(ctx.firstLevelMenus.value.map((m) => m.key)).toEqual([
      'workbench',
      'identity',
      'system',
    ])
  })

  it('resolves activeFirstLevelMenuKey from a 2nd level route name', () => {
    const { ctx } = makeContext('users')
    expect(ctx.activeFirstLevelMenuKey.value).toBe('identity')
  })

  it('resolves activeFirstLevelMenuKey from a 3rd level route name', () => {
    const { ctx } = makeContext('roles-list')
    expect(ctx.activeFirstLevelMenuKey.value).toBe('identity')
  })

  it('keeps a 1st level leaf as active when the route matches it directly', () => {
    // workbench is a 1st level item with no children — UI components decide
    // (via isActiveFirstLevelMenuHasChildren) whether to show a sub-rail.
    const { ctx } = makeContext('workbench')
    expect(ctx.activeFirstLevelMenuKey.value).toBe('workbench')
    expect(ctx.isActiveFirstLevelMenuHasChildren.value).toBe(false)
  })

  it('falls back to first item with children when the route matches nothing', () => {
    // 'phantom-route' is not in any menu key — fall back to first
    // 1st level item with children = 'identity'.
    const { ctx } = makeContext('phantom-route')
    expect(ctx.activeFirstLevelMenuKey.value).toBe('identity')
  })

  it('exposes secondLevelMenus matching the active 1st level item', () => {
    const { ctx } = makeContext('users')
    expect(ctx.secondLevelMenus.value.map((m) => m.key)).toEqual(['users', 'roles'])
  })

  it('exposes childLevelMenus matching the active 2nd level item', () => {
    const { ctx } = makeContext('users')
    // Default 2nd level is the first child of identity = 'users' (no children).
    expect(ctx.childLevelMenus.value).toEqual([])
    ctx.handleSelectSecondLevelMenu('roles')
    expect(ctx.childLevelMenus.value.map((m) => m.key)).toEqual([
      'roles-list',
      'roles-tree',
    ])
  })

  it('isActiveFirstLevelMenuHasChildren reflects current selection', () => {
    const { ctx } = makeContext('users')
    expect(ctx.isActiveFirstLevelMenuHasChildren.value).toBe(true)
    ctx.handleSelectFirstLevelMenu('workbench')
    // Workbench has no children — but the watcher would have refilled it...
    // routeName is still 'users' so activeFirstLevel would re-sync. Force
    // a route change too.
    expect(ctx.activeFirstLevelMenuKey.value).toBe('workbench')
    expect(ctx.isActiveFirstLevelMenuHasChildren.value).toBe(false)
  })

  it('handleSelectFirstLevelMenu mutates active state', () => {
    const { ctx } = makeContext('users')
    ctx.handleSelectFirstLevelMenu('system')
    expect(ctx.activeFirstLevelMenuKey.value).toBe('system')
    expect(ctx.secondLevelMenus.value.map((m) => m.key)).toEqual(['menus'])
  })

  it('route change re-syncs activeFirstLevelMenuKey', async () => {
    const { ctx, routeName } = makeContext('users')
    expect(ctx.activeFirstLevelMenuKey.value).toBe('identity')
    routeName.value = 'menus'
    // Vue watchers are async — let microtasks drain.
    await Promise.resolve()
    expect(ctx.activeFirstLevelMenuKey.value).toBe('system')
  })

  it('autoSelectFirstWith=false leaves activeFirstLevelMenuKey at first item', () => {
    const menusRef = ref(makeMenus())
    const menus = computed(() => menusRef.value)
    const routeName = ref<string>('workbench')
    const ctx = useAdminMenuContext({
      menus,
      routeName: computed(() => routeName.value ?? ''),
      autoSelectFirstWith: false,
    })
    // workbench matches first item directly so result is 'workbench'.
    expect(ctx.activeFirstLevelMenuKey.value).toBe('workbench')
  })
})
