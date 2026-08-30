import { describe, it, expect, beforeEach } from 'vitest'
import { defineComponent, h, nextTick } from 'vue'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter, useRoute, useRouter, type Router } from 'vue-router'
import { useWindowRoute } from '../../src/headless/useWindowRoute'
import { useDesktopWindowId } from '../../src/headless/desktop-window-context'
import { useQueryScope } from '../../src/headless/query-scope'
import { useAdminDesktopStore } from '../../src/stores/useAdminDesktopStore'

const Blank = defineComponent({ render: () => h('div') })

function makeRouter(): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'home', component: Blank },
      { path: '/users', name: 'identity.users', component: Blank, meta: { title: 'Users' } },
      {
        path: '/users/:id',
        name: 'identity.users.detail',
        component: Blank,
        meta: { title: 'User Detail' },
      },
    ],
  })
}

/**
 * What each probe saw during setup. Captured into module scope rather than
 * `expose`d: the values under test are a reactive route object and a plain
 * router-shaped object, and reading them straight off setup is both simpler
 * and closer to what a real page does with them.
 */
interface PageCapture {
  route: ReturnType<typeof useRoute>
  router: ReturnType<typeof useRouter>
  detail: ReturnType<typeof useQueryScope>
  /** Read from the PAGE, not the shell: provide/inject flows downward, so a
   *  component cannot inject what its own setup provided. */
  seenWindowId: string | null
}
interface ShellCapture {
  ctx: ReturnType<typeof useWindowRoute>
}
let pages: PageCapture[] = []
let shells: ShellCapture[] = []

/** Reads the injected route/router - i.e. behaves like any page in this package. */
const PageProbe = defineComponent({
  setup() {
    const route = useRoute()
    const router = useRouter()
    // The single most common URL-state pattern in the package (useDetail /
    // TTabsPage both ride it), and the one that collides across windows.
    const detail = useQueryScope('detail')
    pages.push({ route, router, detail, seenWindowId: useDesktopWindowId() })
    return () => h('div', { 'data-name': String(route.name ?? '') })
  },
})

/** A window shell: installs the per-window route context, then renders a page. */
const WindowProbe = defineComponent({
  props: { windowId: { type: String, required: true } },
  setup(props) {
    const ctx = useWindowRoute(props.windowId)
    shells.push({ ctx })
    return () => h(PageProbe)
  },
})

/** Mount N window shells side by side under one real router, as the desktop does. */
function mountWindows(router: Router, ids: string[]) {
  const Host = defineComponent({
    setup() {
      return () => ids.map((id) => h(WindowProbe, { key: id, windowId: id, ref: id }))
    },
  })
  return mount(Host, { global: { plugins: [router] } })
}

describe('useWindowRoute', () => {
  let router: Router

  beforeEach(async () => {
    setActivePinia(createPinia())
    pages = []
    shells = []
    router = makeRouter()
    await router.push('/')
    await router.isReady()
  })

  it('hands the page this window location instead of the global route', async () => {
    const desktop = useAdminDesktopStore()
    const a = desktop.open({ name: 'identity.users', path: '/users', title: 'Users' })

    const wrapper = mountWindows(router, [a])
    await nextTick()

    expect(pages[0].route.name).toBe('identity.users')
    // The global router is still sitting on '/', which is exactly the point:
    // the page believes it is somewhere the address bar knows nothing about.
    expect(router.currentRoute.value.name).toBe('home')
  })

  it('two windows on the same route do not share URL state', async () => {
    const desktop = useAdminDesktopStore()
    const a = desktop.open({ name: 'identity.users', path: '/users', title: 'Users' })
    const b = desktop.open({ name: 'identity.users', path: '/users', title: 'Users' })

    const wrapper = mountWindows(router, [a, b])
    await nextTick()

    const [{ detail: scopeA }, { detail: scopeB }] = pages

    scopeA.set('edit:1')
    await nextTick()

    expect(desktop.locationOf(a)!.query).toEqual({ detail: 'edit:1' })
    // Without a per-window route this is the failure: B would read A's key.
    expect(desktop.locationOf(b)!.query).toEqual({})
    expect(scopeB.read()).toBeNull()

    scopeB.set('view:2')
    await nextTick()

    expect(desktop.locationOf(a)!.query).toEqual({ detail: 'edit:1' })
    expect(desktop.locationOf(b)!.query).toEqual({ detail: 'view:2' })

    // Read side, asserted separately: writes reach the store through the window
    // ROUTER, so the two assertions above stay green even if the window ROUTE
    // were never provided (mutation-verified - they did). What proves the route
    // is scoped is each page seeing its own query back.
    expect(pages[0].route.query).toEqual({ detail: 'edit:1' })
    expect(pages[1].route.query).toEqual({ detail: 'view:2' })
    // `value` is the reactive ref the scope hands back, hence the double hop.
    expect(scopeA.value.value).toBe('edit:1')
    expect(scopeB.value.value).toBe('view:2')

    // And each window is still on the page it was on. Asserting the query alone
    // let a real bug through: `useQueryScope` writes a PARTIAL location
    // (`{ query }`, no path), which vue-router anchors to the router's own
    // current route - the global one. The query survived, so the assertions
    // above passed, while the window silently jumped to whatever page the
    // address bar was showing.
    expect(desktop.locationOf(a)!.name).toBe('identity.users')
    expect(desktop.locationOf(b)!.name).toBe('identity.users')
  })

  it('keeps a query-only navigation on THIS window page, not the global one', async () => {
    const desktop = useAdminDesktopStore()
    // Global router sits somewhere else entirely - as it always does under the
    // desktop layout, where the address bar tracks nothing in particular.
    await router.push('/')
    const a = desktop.open({ name: 'identity.users', path: '/users', title: 'Users' })
    desktop.navigate(a, {
      name: 'identity.users.detail',
      path: '/users/7',
      title: 'Detail',
      params: { id: '7' },
    })

    mountWindows(router, [a])
    await nextTick()

    // Exactly what a detail page's `?section=` sync issues.
    await pages[0].router.replace({ query: { section: 'roles' } })
    await nextTick()

    const loc = desktop.locationOf(a)!
    expect(loc.name).toBe('identity.users.detail')
    expect(loc.params).toEqual({ id: '7' })
    expect(loc.query).toEqual({ section: 'roles' })
    expect(router.currentRoute.value.name).toBe('home')
  })

  it('router.push navigates inside the window, not the whole app', async () => {
    const desktop = useAdminDesktopStore()
    const a = desktop.open({ name: 'identity.users', path: '/users', title: 'Users' })

    const wrapper = mountWindows(router, [a])
    await nextTick()

    await pages[0].router.push({ name: 'identity.users.detail', params: { id: '7' } })
    await nextTick()

    expect(desktop.locationOf(a)).toMatchObject({
      name: 'identity.users.detail',
      params: { id: '7' },
    })
    // The SPA did not navigate - had it, the desktop itself would be gone.
    expect(router.currentRoute.value.name).toBe('home')
  })

  it('window router.back walks this window stack only', async () => {
    const desktop = useAdminDesktopStore()
    const a = desktop.open({ name: 'identity.users', path: '/users', title: 'Users' })
    const b = desktop.open({ name: 'identity.users', path: '/users', title: 'Users' })

    const wrapper = mountWindows(router, [a, b])
    await nextTick()

    const routerA = pages[0].router
    await routerA.push({ name: 'identity.users.detail', params: { id: '1' } })
    routerA.back()
    await nextTick()

    expect(desktop.locationOf(a)!.name).toBe('identity.users')
    expect(desktop.locationOf(b)!.name).toBe('identity.users')
  })

  it('exposes canGoBack from the window stack, not browser history', async () => {
    const desktop = useAdminDesktopStore()
    const a = desktop.open({ name: 'identity.users', path: '/users', title: 'Users' })

    const wrapper = mountWindows(router, [a])
    await nextTick()

    const { ctx } = shells[0]
    expect(ctx.canGoBack.value).toBe(false)

    desktop.navigate(a, { name: 'identity.users.detail', path: '/users/1', title: 'D' })
    await nextTick()
    expect(ctx.canGoBack.value).toBe(true)
  })

  it('reflects a navigation in the SAME tick, before any flush', async () => {
    const desktop = useAdminDesktopStore()
    const a = desktop.open({ name: 'identity.users', path: '/users', title: 'Users' })

    mountWindows(router, [a])
    await nextTick()

    const route = pages[0].route
    expect(route.params).toEqual({})

    desktop.navigate(a, {
      name: 'identity.users.detail',
      path: '/users/7',
      title: 'Detail',
      params: { id: '7' },
    })

    // NO `await` here on purpose. A page component for the new location mounts
    // in this same tick and reads `route.params.id` during its setup; if the
    // route only caught up on the next flush it would read the PREVIOUS
    // location's params and fetch nothing. Measured in the browser as a detail
    // page stuck on "Loading..." throwing on `.email`.
    expect(route.params).toEqual({ id: '7' })
    expect(route.name).toBe('identity.users.detail')
  })

  it('resolve delegates to the real router so RouterLink and back targets work', async () => {
    const desktop = useAdminDesktopStore()
    const a = desktop.open({ name: 'identity.users', path: '/users', title: 'Users' })

    const wrapper = mountWindows(router, [a])
    await nextTick()

    const resolved = pages[0].router.resolve({
      name: 'identity.users.detail',
      params: { id: '9' },
    })

    expect(resolved.path).toBe('/users/9')
    // `matched` is what RouterLink reads for its active-class logic; a synthetic
    // object cannot invent it, which is why resolve is delegated rather than faked.
    expect(resolved.matched.length).toBeGreaterThan(0)
  })

  it('marks the page subtree as being inside a window', async () => {
    const desktop = useAdminDesktopStore()
    const a = desktop.open({ name: 'identity.users', path: '/users', title: 'Users' })

    const wrapper = mountWindows(router, [a])
    await nextTick()

    expect(pages[0].seenWindowId).toBe(a)
  })

  it('renders nothing rather than throwing when the route no longer exists', async () => {
    const desktop = useAdminDesktopStore()
    // A window persisted against a page that was renamed or removed since.
    const ghost = desktop.open({ name: 'gone.away', path: '/gone', title: 'Gone' })

    expect(() => mountWindows(router, [ghost])).not.toThrow()
  })
})
