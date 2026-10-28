import { describe, it, expect, vi, beforeEach } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { createRouter, createMemoryHistory } from 'vue-router'
import { createPinia, setActivePinia } from 'pinia'
import { createTabNavigator } from '../../src/router/tab-navigation'
import { useAdminTabStore } from '../../src/stores/useAdminTabStore'

const Page = { template: '<div />' }

function makeRouter() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/admin',
        name: 'admin-root',
        component: Page,
        redirect: { name: 'dashboard' },
        children: [
          { path: 'dashboard', name: 'dashboard', component: Page },
          { path: 'users', name: 'users', component: Page },
          { path: 'roles', name: 'roles', component: Page },
        ],
      },
    ],
  })
}

const tab = (name: string) => ({ id: name, title: name, fullPath: `/admin/${name}` })

describe('createTabNavigator', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('showing a tab navigates to its page', async () => {
    const router = makeRouter()
    await router.push('/admin/users')
    const retrack = vi.fn()
    await createTabNavigator(router, retrack)(tab('roles'))
    expect(router.currentRoute.value.fullPath).toBe('/admin/roles')
    expect(retrack).not.toHaveBeenCalled()
  })

  it('with no tab left it goes to the landing page', async () => {
    const router = makeRouter()
    await router.push('/admin/users')
    await createTabNavigator(router, vi.fn())(null)
    expect(router.currentRoute.value.fullPath).toBe('/admin/dashboard')
  })

  // Close all on the dashboard: the landing page is already on screen, the
  // route does not change, so nothing else would put its tab back.
  it('★ the landing page already on screen gets its tab back', async () => {
    const router = makeRouter()
    await router.push('/admin/dashboard')
    const retrack = vi.fn()
    await createTabNavigator(router, retrack)(null)
    expect(router.currentRoute.value.fullPath).toBe('/admin/dashboard')
    expect(retrack).toHaveBeenCalledTimes(1)
  })

  it('a tab whose page is already on screen is tracked again, not pushed again', async () => {
    const router = makeRouter()
    await router.push('/admin/users')
    const push = vi.spyOn(router, 'push')
    const retrack = vi.fn()
    await createTabNavigator(router, retrack)(tab('users'))
    expect(push).not.toHaveBeenCalled()
    expect(retrack).toHaveBeenCalledTimes(1)
  })

  // End to end through the store: the x on the tab on screen takes the page
  // away with it. It used to move the highlight only.
  it('★ closing the tab on screen moves the route to the neighbour tab', async () => {
    const router = makeRouter()
    await router.push('/admin/users')
    const store = useAdminTabStore()
    store.addTab({ name: 'users', fullPath: '/admin/users', path: '/admin/users' })
    store.addTab({ name: 'roles', fullPath: '/admin/roles', path: '/admin/roles' })
    store.setActiveTab('users')
    const navigate = createTabNavigator(router, vi.fn())
    const pending: Promise<void>[] = []
    store.bindNavigation((t) => {
      pending.push(navigate(t))
    })
    store.removeTab('users')
    await Promise.all(pending)
    expect(router.currentRoute.value.fullPath).toBe('/admin/roles')
  })

  // The binding is the whole fix on the shell side: without it every close
  // goes back to moving only the highlight, and no other test would notice.
  it('the admin shell binds the tab store to the router', () => {
    const shellSource = readFileSync(resolve(__dirname, '../../src/router/AdminShellRoot.vue'), 'utf8')
    expect(shellSource).toMatch(/tabStore\.bindNavigation\(\s*createTabNavigator\(router,/)
  })
})
