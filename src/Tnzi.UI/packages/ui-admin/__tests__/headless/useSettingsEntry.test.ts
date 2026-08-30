import { describe, it, expect, beforeEach } from 'vitest'
import { defineComponent, h } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter, type Router, type RouteRecordRaw } from 'vue-router'
import { useSettingsEntry, type SettingsEntry } from '../../src/headless/useSettingsEntry'
import { useAdminAuthStore } from '../../src/stores/useAdminAuthStore'
import { useAdminThemeStore } from '../../src/stores/useAdminThemeStore'
import { useAdminDesktopStore } from '../../src/stores/useAdminDesktopStore'
import { useAdminRouteStore } from '../../src/stores/useAdminRouteStore'

const Blank = defineComponent({ render: () => h('div') })

function makeRouter(settingsMeta: Record<string, unknown> | null): Router {
  const routes: RouteRecordRaw[] = [{ path: '/', name: 'home', component: Blank }]
  if (settingsMeta) {
    routes.push({ path: '/settings', name: 'settings', component: Blank, meta: settingsMeta })
  }
  return createRouter({ history: createMemoryHistory(), routes })
}

/** Mount just enough of a component to hold the composable. */
function setup(router: Router): SettingsEntry {
  let api!: SettingsEntry
  mount(
    defineComponent({
      setup() {
        api = useSettingsEntry()
        return () => h('div')
      },
    }),
    { global: { plugins: [router] } },
  )
  return api
}

describe('useSettingsEntry', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('hides the entry when the route does not exist', async () => {
    const router = makeRouter(null)
    await router.push('/')
    expect(setup(router).available.value).toBe(false)
  })

  it('shows it for a route that asks for nothing', async () => {
    const router = makeRouter({ title: 'Settings' })
    await router.push('/')
    expect(setup(router).available.value).toBe(true)
  })

  it('hides it when the module behind it is not loaded', async () => {
    const router = makeRouter({ title: 'Settings' })
    await router.push('/')
    const routeStore = useAdminRouteStore()
    // The gear must obey module availability, or it lands on a 403/404.
    routeStore.setAvailableModules(new Set<string>())
    routeStore.setAuthRoutes([
      { name: 'settings', path: '/settings', meta: { title: 'Settings', moduleGate: true } },
    ])
    expect(setup(router).available.value).toBe(false)
  })

  it('honours a plain permission on the route', async () => {
    const router = makeRouter({ title: 'Settings', permission: 'system.parameter.view' })
    await router.push('/')
    const auth = useAdminAuthStore()
    auth.setUserInfo({ id: '1', username: 'u', permissions: ['something.else'] })
    expect(setup(router).available.value).toBe(false)

    auth.setUserInfo({ id: '1', username: 'u', permissions: ['system.parameter.view'] })
    expect(setup(router).available.value).toBe(true)
  })

  it('opens a WINDOW under the desktop layout instead of navigating', async () => {
    const router = makeRouter({ title: 'Settings' })
    await router.push('/')
    useAdminThemeStore().layoutMode = 'desktop'
    const desktop = useAdminDesktopStore()

    setup(router).open()

    // A push here navigates a router whose outlet the window manager replaced:
    // the address bar moves and nothing appears.
    expect(desktop.windows).toHaveLength(1)
    expect(desktop.windows[0].stack[0].name).toBe('settings')
    expect(router.currentRoute.value.name).toBe('home')
  })

  it('navigates normally in every other layout', async () => {
    const router = makeRouter({ title: 'Settings' })
    await router.push('/')
    useAdminThemeStore().layoutMode = 'vertical'
    const desktop = useAdminDesktopStore()

    setup(router).open()
    // `open` fires the push without awaiting it, so let the navigation settle.
    await flushPromises()

    expect(desktop.windows).toHaveLength(0)
    expect(router.currentRoute.value.name).toBe('settings')
  })

  it('offers the built-in-menus toggle to super admins only', async () => {
    const router = makeRouter({ title: 'Settings' })
    await router.push('/')
    const auth = useAdminAuthStore()

    auth.setSuperUser(false)
    expect(setup(router).canToggleBuiltIn.value).toBe(false)

    auth.setSuperUser(true)
    const api = setup(router)
    expect(api.canToggleBuiltIn.value).toBe(true)

    const before = api.builtInEnabled.value
    api.toggleBuiltIn()
    expect(api.builtInEnabled.value).toBe(!before)
  })
})
