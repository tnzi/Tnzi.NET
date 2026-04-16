import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { defineComponent, h, nextTick } from 'vue'
import { mount } from '@vue/test-utils'
import { useAdminMenu } from '../../src/headless/useAdminMenu'
import { usePermissionGuard } from '../../src/headless/usePermissionGuard'
import {
  provideMixMenuContext,
  injectMixMenuContext,
  useMixMenuContext,
} from '../../src/headless/useMixMenuContext'
import {
  provideTabContext,
  injectTabContext,
  useTabContext,
} from '../../src/headless/useTabContext'
import { useAdminRouteStore, type AdminRouteRecord } from '../../src/stores/useAdminRouteStore'
import { useAdminAuthStore } from '../../src/stores/useAdminAuthStore'
import { useAdminTabStore } from '../../src/stores/useAdminTabStore'

function seedIdentityRoutes(): AdminRouteRecord[] {
  return [
    {
      name: 'identity',
      path: '/identity',
      meta: { title: 'Identity', order: 1 },
      children: [
        {
          name: 'identity.users',
          path: '/identity/users',
          meta: { title: 'Users', order: 1 },
        },
      ],
    },
  ]
}

describe('useAdminMenu', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('returns active menu and parent chain by key', () => {
    useAdminRouteStore().setConstantRoutes(seedIdentityRoutes())
    const { activeKey, activeMenu, parentChain } = useAdminMenu()
    activeKey.value = 'identity.users'
    expect(activeMenu.value?.key).toBe('identity.users')
    expect(parentChain.value.map((p) => p.key)).toEqual(['identity', 'identity.users'])
  })

  it('returns empty chain when key not found', () => {
    const { activeKey, activeMenu, parentChain } = useAdminMenu()
    activeKey.value = 'nonexistent'
    expect(activeMenu.value).toBeNull()
    expect(parentChain.value).toEqual([])
  })
})

describe('usePermissionGuard', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('can(perm) returns true when permission present', () => {
    const auth = useAdminAuthStore()
    auth.setUserInfo({
      id: '1',
      username: 'x',
      roles: [],
      permissions: ['user.view', 'user.edit'],
    })
    const { can } = usePermissionGuard()
    expect(can('user.view')).toBe(true)
    expect(can('user.delete')).toBe(false)
  })

  it('canAny returns true if any match', () => {
    const auth = useAdminAuthStore()
    auth.setUserInfo({ id: '1', username: 'x', roles: [], permissions: ['user.view'] })
    const { canAny } = usePermissionGuard()
    expect(canAny(['user.view', 'user.edit'])).toBe(true)
    expect(canAny(['user.delete'])).toBe(false)
  })

  it('canAll returns true only if all match', () => {
    const auth = useAdminAuthStore()
    auth.setUserInfo({
      id: '1',
      username: 'x',
      roles: [],
      permissions: ['user.view', 'user.edit'],
    })
    const { canAll } = usePermissionGuard()
    expect(canAll(['user.view', 'user.edit'])).toBe(true)
    expect(canAll(['user.view', 'user.delete'])).toBe(false)
  })

  it('superUser flag bypasses all checks', () => {
    const auth = useAdminAuthStore()
    auth.setUserInfo({ id: '1', username: 'x', roles: [], permissions: [] })
    auth.isSuperUser = true
    const { can, canAny, canAll } = usePermissionGuard()
    expect(can('anything')).toBe(true)
    expect(canAny(['nope'])).toBe(true)
    expect(canAll(['nope', 'nada'])).toBe(true)
  })
})

describe('useMixMenuContext', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('injects context provided by ancestor', async () => {
    let injected: ReturnType<typeof useMixMenuContext> | null = null
    const Child = defineComponent({
      setup() {
        injected = useMixMenuContext()
        return () => h('div')
      },
    })
    const Parent = defineComponent({
      setup() {
        provideMixMenuContext()
        return () => h(Child)
      },
    })
    const wrapper = mount(Parent)
    await nextTick()
    expect(injected).not.toBeNull()
    expect(injected!.activeFirstLevelKey).toBeDefined()
    expect(Array.isArray(injected!.secondLevelMenus.value)).toBe(true)
    wrapper.unmount()
  })

  it('throws when injected without provider', () => {
    const Child = defineComponent({
      setup() {
        injectMixMenuContext()
        return () => h('div')
      },
    })
    expect(() => mount(Child)).toThrow(/provideMixMenuContext/)
  })
})

describe('useTabContext', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('provides goHome and closeCurrent helpers', async () => {
    let injected: ReturnType<typeof useTabContext> | null = null
    const Child = defineComponent({
      setup() {
        injected = useTabContext()
        return () => h('div')
      },
    })
    const Parent = defineComponent({
      setup() {
        provideTabContext()
        return () => h(Child)
      },
    })
    const wrapper = mount(Parent)
    await nextTick()
    expect(injected).not.toBeNull()
    expect(typeof injected!.goHome).toBe('function')
    expect(typeof injected!.closeCurrent).toBe('function')
    wrapper.unmount()
  })
})

// Suppress unused import warning for injectTabContext — used for symmetry with injectMixMenuContext
void injectTabContext
