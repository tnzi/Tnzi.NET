import { describe, it, expect, beforeEach, vi } from 'vitest'
import { createApp, h, defineComponent } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import type { RouteRecordRaw, Router } from 'vue-router'
import { defineAdminApp } from '../../src/plugin/defineAdminApp'
import { useAdminRouteStore } from '../../src/stores/useAdminRouteStore'
import { useAdminAuthStore } from '../../src/stores/useAdminAuthStore'
import { useAdminTabStore } from '../../src/stores/useAdminTabStore'
import { useAdminAppStore } from '../../src/stores/useAdminAppStore'
import { ADMIN_LOGIN_CONFIG_KEY, type AdminLoginConfig } from '../../src/plugin/login-config'

const dummyClient = {
  get: async () => ({ success: true, code: 200, data: null }),
  post: async () => ({ success: true, code: 200, data: null }),
  addUnauthorizedListener: () => () => {},
} as never

function findAdminRoute(routes: RouteRecordRaw[]): RouteRecordRaw | undefined {
  return routes.find((r) => r.path === '/admin')
}

function namesOfChildren(route: RouteRecordRaw | undefined): string[] {
  return (route?.children ?? [])
    .map((c) => (typeof c.name === 'string' ? c.name : ''))
    .filter(Boolean)
}

describe('defineAdminApp', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('returns the full default route tree by default', () => {
    const { routes } = defineAdminApp({ client: dummyClient })
    const admin = findAdminRoute(routes)
    expect(admin).toBeTruthy()
    const childNames = namesOfChildren(admin)
    // Sanity: at minimum identity/authorization/system/storage are in the preset
    expect(childNames).toContain('identity')
    expect(childNames).toContain('authorization')
    expect(childNames).toContain('system')
  })

  it('applies the GLOBAL admin theme at bootstrap - anonymous, before login', async () => {
    // The super-admin-configured global theme must reach the login page and the
    // top-level exception pages (403/404/500), which render OUTSIDE the
    // authenticated shell. So install() kicks off `GET /appearance/admin-theme`
    // at bootstrap (the endpoint is anonymous), not just once signed in -
    // otherwise the theme snapped back to the built-in palette on every refresh
    // of those pages.
    const get = vi.fn(async () => ({
      success: true,
      code: 200,
      data: { theme: null, updatedAt: null },
    }))
    const client = {
      get,
      post: async () => ({ success: true, code: 200, data: null }),
      addUnauthorizedListener: () => () => {},
    } as never
    const app = createApp({ render: () => h('div') })
    const pinia = createPinia()
    app.use(pinia)
    setActivePinia(pinia)
    const router = {
      beforeEach: vi.fn(),
      afterEach: vi.fn(),
      onError: vi.fn(),
    } as unknown as Router
    defineAdminApp({ client }).install(app, pinia, router)
    // The load is fire-and-forget; flush the microtask/macrotask queue.
    await new Promise((resolve) => setTimeout(resolve, 0))
    // Themes became scoped per front-end product on 2026-08-02; the admin
    // console reads its own scope rather than the old single endpoint.
    expect(
      get.mock.calls.some((c) => String(c[0]).includes('appearance/theme/admin')),
    ).toBe(true)
  })

  it('preserves meta.moduleGate through the RouteRecordRaw → store round-trip (regression)', () => {
    // toAdminRouteRecords() re-maps meta field-by-field; if it forgets to carry
    // `moduleGate` (as it once forgot `permission`), the store sees `undefined`
    // and NEVER gates the node - the sidebar keeps showing menus for modules the
    // backend never loaded, even for super-admins. Unit tests that seed the store
    // directly bypass this conversion, so this MUST install through the real path.
    const app = createApp({ render: () => h('div') })
    const pinia = createPinia()
    app.use(pinia)
    setActivePinia(pinia)
    const router = {
      beforeEach: vi.fn(),
      afterEach: vi.fn(),
      onError: vi.fn(),
    } as unknown as Router
    defineAdminApp({ client: dummyClient }).install(app, pinia, router)
    const store = useAdminRouteStore()

    // The marker survives the conversion (was `undefined` before the fix).
    const identity = store.allRoutes.find((r) => r.name === 'identity')
    expect(identity?.meta?.moduleGate).toBe(true)

    // …and actually drives gating end-to-end: with a known loaded-module signal
    // that omits identity, the identity subtree becomes unreachable and drops
    // from the menu, while a loaded module (authorization) stays. Super-user
    // bypasses the permission filter (which no longer fails open when logged
    // out), so this isolates the module gate - which holds for super-admins too.
    useAdminAuthStore().setSuperUser(true)
    store.setAvailableModules(new Set(['authorization']))
    expect(store.unavailableRouteNames.has('identity')).toBe(true)
    const menuKeys = store.menus.map((m) => m.key)
    expect(menuKeys).not.toContain('identity')
    expect(menuKeys).toContain('authorization')

    // STRING-valued gate variant: the Settings Center route carries
    // `moduleGate: 'system'` (its backend lives in the System module). The
    // marker must survive the same walk, and a signal without 'system' must
    // make the settings route unreachable (drives the sidebar gear too).
    const settings = store.allRoutes.find((r) => r.name === 'settings')
    expect(settings?.meta?.moduleGate).toBe('system')
    expect(store.unavailableRouteNames.has('settings')).toBe(true) // 'authorization'-only signal
    store.setAvailableModules(new Set(['authorization', 'system']))
    expect(store.unavailableRouteNames.has('settings')).toBe(false)
  })

  it('raises moduleSignalPending during the availability probe and settles it with the signal', async () => {
    // Side-effectful surfaces (TChatHost, module-tagged dashboard widgets)
    // defer on this flag so they never fire requests at a module the incoming
    // signal is about to rule out. It must be raised SYNCHRONOUSLY by
    // install() (before any component mounts) and settle regardless of
    // probe outcome.
    const app = createApp({ render: () => h('div') })
    const pinia = createPinia()
    app.use(pinia)
    setActivePinia(pinia)
    const router = {
      beforeEach: vi.fn(),
      afterEach: vi.fn(),
      onError: vi.fn(),
    } as unknown as Router
    let releaseProbe!: () => void
    const gate = new Promise<void>((resolve) => {
      releaseProbe = resolve
    })
    const client = {
      get: vi.fn(async (url: string) => {
        if (url === '/admin/shell/modules') {
          await gate
          return {
            success: true,
            code: 200,
            data: { modules: [{ name: 'Identity', isEnabled: true }] },
          }
        }
        return { success: true, code: 200, data: null }
      }),
      post: async () => ({ success: true, code: 200, data: null }),
      addUnauthorizedListener: () => () => {},
    } as never

    defineAdminApp({ client }).install(app, pinia, router)
    const store = useAdminRouteStore()
    expect(store.moduleSignalPending).toBe(true)

    releaseProbe()
    await vi.waitFor(() => expect(store.moduleSignalPending).toBe(false))
    expect(store.availableModules?.has('identity')).toBe(true)
  })

  it('settles moduleSignalPending even when the probe fails (fail-open, no signal)', async () => {
    const app = createApp({ render: () => h('div') })
    const pinia = createPinia()
    app.use(pinia)
    setActivePinia(pinia)
    const router = {
      beforeEach: vi.fn(),
      afterEach: vi.fn(),
      onError: vi.fn(),
    } as unknown as Router
    const client = {
      get: async () => {
        throw new Error('network down')
      },
      post: async () => ({ success: true, code: 200, data: null }),
      addUnauthorizedListener: () => () => {},
    } as never

    defineAdminApp({ client }).install(app, pinia, router)
    const store = useAdminRouteStore()
    await vi.waitFor(() => expect(store.moduleSignalPending).toBe(false))
    expect(store.availableModules).toBeNull()
  })

  it('never raises moduleSignalPending when moduleGating is disabled', () => {
    const app = createApp({ render: () => h('div') })
    const pinia = createPinia()
    app.use(pinia)
    setActivePinia(pinia)
    const router = {
      beforeEach: vi.fn(),
      afterEach: vi.fn(),
      onError: vi.fn(),
    } as unknown as Router
    defineAdminApp({ client: dummyClient, moduleGating: false }).install(app, pinia, router)
    expect(useAdminRouteStore().moduleSignalPending).toBe(false)
  })

  it('hideModules removes the named module subtree from /admin', () => {
    const { routes } = defineAdminApp({
      client: dummyClient,
      hideModules: ['payment', 'audit'],
    })
    const admin = findAdminRoute(routes)
    const childNames = namesOfChildren(admin)
    expect(childNames).not.toContain('payment')
    expect(childNames).not.toContain('audit')
    expect(childNames).toContain('identity')
  })

  it('showOnlyModules keeps only the whitelisted modules', () => {
    const { routes } = defineAdminApp({
      client: dummyClient,
      showOnlyModules: ['identity'],
    })
    const admin = findAdminRoute(routes)
    const childNames = namesOfChildren(admin)
    expect(childNames).toEqual(['identity'])
  })

  it('overridePages swaps the component for a named route deep in the tree', () => {
    const customPage = defineComponent({
      name: 'CustomUserPage',
      render: () => h('div', 'custom'),
    })
    const { routes } = defineAdminApp({
      client: dummyClient,
      overridePages: { 'identity.users': customPage },
    })
    const admin = findAdminRoute(routes)
    const identity = admin?.children?.find(
      (c) => typeof c.name === 'string' && c.name === 'identity',
    )
    const users = identity?.children?.find(
      (c) => typeof c.name === 'string' && c.name === 'identity.users',
    )
    expect(users).toBeTruthy()
    expect(users!.component).toBe(customPage)
  })

  it('addModules appends consumer-supplied routes under /admin', () => {
    const customRoute: RouteRecordRaw = {
      path: 'reports',
      name: 'reports',
      component: defineComponent({ render: () => h('div', 'reports') }),
      meta: { title: 'Reports' },
    }
    const { routes } = defineAdminApp({
      client: dummyClient,
      addModules: [customRoute],
    })
    const admin = findAdminRoute(routes)
    const childNames = namesOfChildren(admin)
    expect(childNames).toContain('reports')
  })

  it('loginComponent replaces the default /login route component', () => {
    const customLogin = defineComponent({
      name: 'CustomLogin',
      render: () => h('div', 'login'),
    })
    const { routes } = defineAdminApp({
      client: dummyClient,
      loginComponent: customLogin,
    })
    // Phase I.7.1: the default login route path is now
    // `/login/:module(pwd-login|...)?`. Match by name to stay path-shape neutral.
    const login = routes.find((r) => r.name === 'login')
    expect(login).toBeTruthy()
    expect(login!.component).toBe(customLogin)
  })

  it('forbiddenComponent replaces the placeholder /403 component', () => {
    const customForbidden = defineComponent({
      name: 'CustomForbidden',
      render: () => h('div', 'forbidden'),
    })
    const { routes } = defineAdminApp({
      client: dummyClient,
      forbiddenComponent: customForbidden,
    })
    // Find by NAME - the returned table is basePath-prefixed ('/admin/403').
    const forbidden = routes.find((r) => r.name === 'forbidden')
    expect(forbidden!.component).toBe(customForbidden)
  })

  it('install seeds the admin route store from the filtered routes', () => {
    const app = createApp({ render: () => h('div') })
    const pinia = createPinia()
    app.use(pinia)
    setActivePinia(pinia)

    const { install } = defineAdminApp({
      client: dummyClient,
      hideModules: ['payment'],
    })
    install(app, pinia)

    const routeStore = useAdminRouteStore()
    expect(routeStore.authRoutes.length).toBeGreaterThan(0)
    const names = routeStore.authRoutes.map((r) => r.name)
    expect(names).toContain('identity')
    expect(names).not.toContain('payment')
  })

  it('install returns a TnziUiAdminInstance with an uninstall hook', () => {
    const app = createApp({ render: () => h('div') })
    const pinia = createPinia()
    app.use(pinia)
    setActivePinia(pinia)

    const { install } = defineAdminApp({ client: dummyClient })
    const instance = install(app, pinia)
    expect(typeof instance.uninstall).toBe('function')
    expect(() => instance.uninstall()).not.toThrow()
  })

  it('installs the permission guard by default; auth guard only when auth.enabled', () => {
    const beforeEachCount = (auth?: {
      enabled?: boolean
      permissionGuard?: boolean
    }): number => {
      const app = createApp({ render: () => h('div') })
      const pinia = createPinia()
      app.use(pinia)
      setActivePinia(pinia)
      const router = { beforeEach: vi.fn(), afterEach: vi.fn(), onError: vi.fn() } as unknown as Router
      defineAdminApp({ client: dummyClient, ...(auth ? { auth } : {}) }).install(app, pinia, router)
      return (router.beforeEach as ReturnType<typeof vi.fn>).mock.calls.length
    }
    // Delta-based so the assertion is robust to however many beforeEach hooks
    // useRouteProgress installs.
    // Permission guard is on by DEFAULT (mirrors the always-on sidebar filter),
    // so opting it out drops exactly one beforeEach vs. the default.
    expect(beforeEachCount() - beforeEachCount({ permissionGuard: false })).toBe(1)
    // Enabling auth adds exactly one MORE beforeEach (the auth guard) on top of
    // the always-present permission guard.
    expect(beforeEachCount({ enabled: true }) - beforeEachCount()).toBe(1)
  })

  it('auth guard redirects by route NAME (deployment-prefix agnostic)', async () => {
    const app = createApp({ render: () => h('div') })
    const pinia = createPinia()
    app.use(pinia)
    setActivePinia(pinia)
    const guards: Array<(to: unknown, from: unknown, next: (arg?: unknown) => void) => Promise<void> | void> = []
    const router = {
      beforeEach: vi.fn((g) => guards.push(g)),
      afterEach: vi.fn(),
      onError: vi.fn(),
    } as unknown as Router
    defineAdminApp({ client: dummyClient, auth: { enabled: true } }).install(app, pinia, router)
    // Fresh store = not logged in → the auth guard must redirect via the
    // NAMED login route, so the target resolves correctly under any
    // basePath / router history base instead of a hardcoded '/login'. The guard
    // is async now (it first gives `resolveSession` a chance to restore), so
    // await each guard before asserting the redirect landed.
    const redirects: unknown[] = []
    const to = { meta: {}, name: 'x', path: '/admin/x', fullPath: '/admin/x', query: {}, params: {} }
    for (const g of guards) await g(to, {}, (arg?: unknown) => { if (arg) redirects.push(arg) })
    expect(redirects).toContainEqual({ name: 'login' })
  })

  it('loadPermissions prunes persisted tabs the signed-in user cannot open', async () => {
    const { loadPermissions } = defineAdminApp({ client: dummyClient })
    // A small route table so deniedRouteNames can resolve permissions (install()
    // does this from the real preset; seed it directly here).
    const routeStore = useAdminRouteStore()
    routeStore.setAuthRoutes([
      { name: 'identity.users', path: '/admin/identity/users', meta: { title: 'Users', permission: 'user.view' } },
      { name: 'system.diagnostics', path: '/admin/system/diagnostics', meta: { title: 'Diag', permission: 'system.diagnostics.view' } },
    ])
    // A prior (higher-privilege) session left BOTH tabs open (persisted).
    const tabStore = useAdminTabStore()
    tabStore.addTab({ name: 'identity.users', path: '/admin/identity/users', fullPath: '/admin/identity/users', query: {}, params: {}, meta: { title: 'Users' } })
    tabStore.addTab({ name: 'system.diagnostics', path: '/admin/system/diagnostics', fullPath: '/admin/system/diagnostics', query: {}, params: {}, meta: { title: 'Diag' } })
    expect(tabStore.tabs.map((t) => t.id)).toEqual(['identity.users', 'system.diagnostics'])

    // Business admin: granted user.view, LACKS system.diagnostics.view. Passing
    // `permissions` skips the backend round-trip; `roles` without a super role
    // keeps isSuperUser false so the deny set is enforced.
    await loadPermissions({ id: 'u1', roles: ['Admin'], permissions: ['user.view'] })

    // The unauthorized Diagnostics tab is dropped; the authorized one survives.
    expect(tabStore.tabs.map((t) => t.id)).toEqual(['identity.users'])
    expect(useAdminAuthStore().isSuperUser).toBe(false)
  })

  it('loadPermissions leaves the store untouched when it resolves neither identity nor permissions', async () => {
    // dummyClient answers every request with data:null - the exact shape of
    // an expired-token background refresh (profile 401, access-profile 401).
    const { loadPermissions } = defineAdminApp({ client: dummyClient })
    const auth = useAdminAuthStore()
    expect(auth.userInfo).toBeNull()

    const result = await loadPermissions()

    expect(result).toEqual([])
    // A poisoned write here ({ id: '', permissions: [] }) would flip userInfo
    // non-null with ZERO permissions: the permission guard stops failing open
    // and the next navigation bounces to /403 even while a real login is
    // completing in parallel (the "logout → sign in as another admin → 403"
    // regression).
    expect(auth.userInfo).toBeNull()
  })

  it('loadPermissions treats a failure envelope WITHOUT a data field as unresolved too', async () => {
    // The real backend 401 envelope may omit `data` entirely - the bridge's
    // unwrap then resolves to UNDEFINED (not null, no throw). The failed-
    // resolution guard must catch that shape as well; matching only `null`
    // was exactly the browser-reproduced poisoning bug.
    const noDataClient = {
      get: async () => ({ success: false, code: 401 }),
      post: async () => ({ success: false, code: 401 }),
      addUnauthorizedListener: () => () => {},
    } as never
    const { loadPermissions } = defineAdminApp({ client: noDataClient })
    const auth = useAdminAuthStore()

    await loadPermissions()

    expect(auth.userInfo).toBeNull()
  })

  it('a failed background refresh does not clobber a previously valid session', async () => {
    const { loadPermissions } = defineAdminApp({ client: dummyClient })
    const auth = useAdminAuthStore()
    auth.setUserInfo({ id: 'u1', username: 'admin', displayName: 'admin', roles: [], permissions: ['user.view'] })
    auth.setSuperUser(true)

    await loadPermissions()

    expect(auth.userInfo?.permissions).toEqual(['user.view'])
    expect(auth.isSuperUser).toBe(true)
  })

  it('mirrors the LIVE client token into the admin store when no token is passed (self-fetch mode → isLogin)', async () => {
    // The auth guard's resolveSession + the post-login after() both call
    // loadPermissions() with no token - the session already lives on the client.
    // loadPermissions must mirror that token so `isLogin` flips true, else a
    // just-authenticated user bounces back to login. getAccessToken is a
    // `this`-method (reads this._token) - locks in the method-call access too.
    const client = {
      _token: 'live-token',
      get: async () => ({ success: true, code: 200, data: null }),
      post: async () => ({ success: true, code: 200, data: null }),
      addUnauthorizedListener: () => () => {},
      getAccessToken(this: { _token: string | null }) {
        return this._token
      },
    } as never
    const { loadPermissions } = defineAdminApp({ client })
    const auth = useAdminAuthStore()
    expect(auth.isLogin).toBe(false)
    // Identity resolved (id + permissions supplied) but NO token passed.
    await loadPermissions({ id: 'u1', username: 'john', roles: ['Admin'], permissions: ['user.view'] })
    expect(auth.isLogin).toBe(true) // token mirrored from the live client session
    expect(auth.userInfo?.id).toBe('u1')
  })

  it('does not set isLogin when neither a passed token nor a live client token exists', async () => {
    const client = {
      get: async () => ({ success: true, code: 200, data: null }),
      post: async () => ({ success: true, code: 200, data: null }),
      addUnauthorizedListener: () => () => {},
      getAccessToken() {
        return null
      },
    } as never
    const { loadPermissions } = defineAdminApp({ client })
    const auth = useAdminAuthStore()
    await loadPermissions({ id: 'u1', username: 'john', permissions: ['user.view'] })
    expect(auth.isLogin).toBe(false) // no token anywhere → not signed in
    expect(auth.userInfo).not.toBeNull() // identity still populated (name/avatar/myId)
  })

  it('stamps meta.builtIn on preset module groups but not on addModules routes (regression)', () => {
    // Same class of bug as the forgotten `permission`/`moduleGate` copies:
    // if the stamp (or its walk copy) is dropped, the built-in-menus toggle
    // silently never filters anything. Assert through the REAL install path.
    const consumerRoute: RouteRecordRaw = {
      name: 'blog',
      path: 'blog',
      meta: { title: 'Blog' },
      children: [
        { name: 'blog.posts', path: 'posts', component: async () => ({}), meta: { title: 'Posts', permission: 'acme.blog.post.view' } },
      ],
    }
    const app = createApp({ render: () => h('div') })
    const pinia = createPinia()
    app.use(pinia)
    setActivePinia(pinia)
    const router = {
      beforeEach: vi.fn(),
      afterEach: vi.fn(),
      onError: vi.fn(),
    } as unknown as Router
    defineAdminApp({ client: dummyClient, addModules: [consumerRoute] }).install(app, pinia, router)
    const store = useAdminRouteStore()

    expect(store.allRoutes.find((r) => r.name === 'identity')?.meta?.builtIn).toBe(true)
    expect(store.allRoutes.find((r) => r.name === 'blog')?.meta?.builtIn).toBeUndefined()

    // …and it drives the toggle end-to-end: super admin + toggle OFF hides
    // built-in groups, keeps the consumer module and the neutral dashboard.
    useAdminAuthStore().setSuperUser(true)
    useAdminAuthStore().setUserInfo({ id: 'u1', username: 'u', roles: [], permissions: [] })
    const appStore = useAdminAppStore()
    appStore.setShowBuiltInMenus(false)
    const menuKeys = store.menus.map((m) => m.key)
    expect(menuKeys).not.toContain('identity')
    expect(menuKeys).toContain('blog')
    expect(menuKeys).toContain('dashboard')
    // install() wired pinia-plugin-persistedstate, so the OFF above landed in
    // localStorage - reset it or later tests hydrate the polluted value.
    appStore.setShowBuiltInMenus(true)
  })

  describe('session-expired redirect', () => {
    interface SessionSetup {
      trigger: (() => void) | undefined
      replace: ReturnType<typeof vi.fn>
      addListener: ReturnType<typeof vi.fn>
    }

    function setup(options?: {
      auth?: { enabled?: boolean; loginPath?: string; sessionExpiredRedirect?: boolean }
      currentRoute?: { name?: string; path: string; fullPath: string }
      withRouter?: boolean
    }): SessionSetup {
      const app = createApp({ render: () => h('div') })
      const pinia = createPinia()
      app.use(pinia)
      setActivePinia(pinia)

      let trigger: (() => void) | undefined
      const addListener = vi.fn((fn: () => void) => {
        trigger = fn
        return () => {}
      })
      const client = {
        get: async () => ({ success: true, code: 200, data: null }),
        post: async () => ({ success: true, code: 200, data: null }),
        addUnauthorizedListener: addListener,
      } as never

      const replace = vi.fn()
      const current = options?.currentRoute ?? {
        name: 'identity.users',
        path: '/admin/identity/users',
        fullPath: '/admin/identity/users?page=2',
      }
      const router = {
        beforeEach: vi.fn(),
        afterEach: vi.fn(),
        onError: vi.fn(),
        replace,
        currentRoute: { value: current },
      } as unknown as Router

      const { install } = defineAdminApp({
        client,
        ...(options?.auth ? { auth: options.auth } : {}),
      })
      install(app, pinia, options?.withRouter === false ? undefined : router)
      return { trigger, replace, addListener }
    }

    it('subscribes to the client unauthorized signal by default (router provided)', () => {
      const { addListener } = setup()
      expect(addListener).toHaveBeenCalledTimes(1)
    })

    it('does not subscribe when sessionExpiredRedirect is false', () => {
      const { addListener } = setup({ auth: { sessionExpiredRedirect: false } })
      expect(addListener).not.toHaveBeenCalled()
    })

    it('does not subscribe without a router', () => {
      const { addListener } = setup({ withRouter: false })
      expect(addListener).not.toHaveBeenCalled()
    })

    it('clears the admin auth store and redirects to /login with a next deep-link', () => {
      const { trigger, replace } = setup()
      const authStore = useAdminAuthStore()
      authStore.setToken('stale-token')
      authStore.setUserInfo({ id: '1', username: 'john', roles: [], permissions: [] })

      trigger!()

      expect(authStore.isLogin).toBe(false)
      expect(authStore.userInfo).toBeNull()
      // By NAME (not a hardcoded path) so the redirect follows any
      // basePath / history-base deployment prefix.
      expect(replace).toHaveBeenCalledWith({
        name: 'login',
        query: { next: '/admin/identity/users?page=2' },
      })
    })

    it('does not redirect when already on the login route', () => {
      const { trigger, replace } = setup({
        currentRoute: { name: 'login', path: '/login/pwd-login', fullPath: '/login/pwd-login' },
      })
      trigger!()
      expect(replace).not.toHaveBeenCalled()
    })

    it('honors a custom auth.loginPath', () => {
      const { trigger, replace } = setup({ auth: { loginPath: '/signin' } })
      trigger!()
      expect(replace).toHaveBeenCalledWith(
        expect.objectContaining({ path: '/signin' }),
      )
    })
  })

  describe('auth guard session restore (auth.enabled + restore)', () => {
    function setupGuard(restore?: () => Promise<void> | void, token: string | null = null) {
      const app = createApp({ render: () => h('div') })
      const pinia = createPinia()
      app.use(pinia)
      setActivePinia(pinia)

      const guards: unknown[] = []
      // getAccessToken is a `this`-dependent METHOD (mirrors the real HttpClient
      // `return this.accessToken`), so resolveSession extracting it into a bare
      // variable and calling it unbound would throw here - locks in the fix.
      const client = {
        _token: token,
        get: async () => ({ success: true, code: 200, data: null }),
        post: async () => ({ success: true, code: 200, data: null }),
        getAccessToken(this: { _token: string | null }) {
          return this._token
        },
      } as never
      const router = {
        beforeEach: vi.fn((g: unknown) => {
          guards.push(g)
        }),
        afterEach: vi.fn(),
        onError: vi.fn(),
        replace: vi.fn(),
        currentRoute: {
          value: { name: 'identity.users', path: '/admin/identity/users', fullPath: '/admin/identity/users' },
        },
      } as unknown as Router

      const { install } = defineAdminApp({ client, auth: { enabled: true, restore } })
      install(app, pinia, router)
      return { guards }
    }

    const signedOutRoute = {
      meta: { requiresAuth: true },
      name: 'x',
      path: '/admin/x',
      fullPath: '/admin/x',
      query: {},
    }

    it('threads the restore hook into a registered auth guard (called once, signed out)', async () => {
      const restore = vi.fn(async () => {})
      const { guards } = setupGuard(restore)
      // Invoke every registered guard: only the auth guard's resolveSession runs
      // the restore hook, so the count is order-independent.
      for (const g of guards) await (g as (...a: unknown[]) => unknown)(signedOutRoute, {}, vi.fn())
      expect(restore).toHaveBeenCalledTimes(1)
    })

    it('does not run restore when the client already carries a token (core session live)', async () => {
      const restore = vi.fn(async () => {})
      // Client token present → resolveSession is token-first → no restore needed.
      const { guards } = setupGuard(restore, 'live-token')
      const authStore = useAdminAuthStore()
      authStore.setToken('t')
      authStore.setUserInfo({ id: '1', username: 'u', roles: [], permissions: [] })
      for (const g of guards) await (g as (...a: unknown[]) => unknown)(signedOutRoute, {}, vi.fn())
      expect(restore).not.toHaveBeenCalled()
    })
  })

  describe('deliberate sign-out (wrapped login.user.onLogout)', () => {
    function installWithLogout(onLogout: () => void | Promise<void>) {
      const app = createApp({ render: () => h('div') })
      const pinia = createPinia()
      app.use(pinia)
      setActivePinia(pinia)
      const router = { beforeEach: vi.fn(), afterEach: vi.fn(), onError: vi.fn() } as unknown as Router
      defineAdminApp({
        client: dummyClient,
        login: { user: { userName: 'admin', onLogout } },
      }).install(app, pinia, router)
      const wrapped = app._context.provides[
        ADMIN_LOGIN_CONFIG_KEY as unknown as string | symbol
      ] as AdminLoginConfig
      return { wrapped }
    }

    it('runs the consumer callback FIRST (store still populated), then clears the store', async () => {
      // Order matters: the store MUST still be populated while the consumer's
      // logout (backend sign-out + redirect) runs - clearing it first nulled
      // `userInfo` while the shell was mounted and flashed the full fail-open
      // menu for the 1-2s the backend call took before the redirect.
      let stillLoggedInDuringConsumer: boolean | null = null
      // install() switches the active pinia, so populate the store AFTER it.
      const { wrapped } = installWithLogout(() => {
        stillLoggedInDuringConsumer = auth.isLogin && auth.userInfo !== null
      })
      const auth = useAdminAuthStore()
      auth.setToken('tok')
      auth.setUserInfo({ id: 'u1', username: 'admin', roles: [], permissions: ['user.view'] })
      auth.setSuperUser(true)

      await wrapped.user!.onLogout!()

      expect(stillLoggedInDuringConsumer).toBe(true) // consumer saw a live session
      expect(auth.isLogin).toBe(false) // …and the store is cleared afterwards
      expect(auth.userInfo).toBeNull()
      expect(auth.isSuperUser).toBe(false) // no cross-session super-user leak
    })

    it('clears the store even when the consumer callback throws (finally)', async () => {
      const { wrapped } = installWithLogout(() => {
        throw new Error('backend logout failed')
      })
      const auth = useAdminAuthStore()
      auth.setToken('tok')
      auth.setUserInfo({ id: 'u1', username: 'admin', roles: [], permissions: [] })
      auth.setSuperUser(true)

      await expect(wrapped.user!.onLogout!()).rejects.toThrow('backend logout failed')
      // The store is still cleared - a failed backend sign-out must not leave a
      // stale super-user / permission set to leak into the next sign-in.
      expect(auth.isLogin).toBe(false)
      expect(auth.userInfo).toBeNull()
      expect(auth.isSuperUser).toBe(false)
    })
  })

  describe('runtime (default auth orchestration)', () => {
    function makeRuntime() {
      const auth = {
        accessToken: null as string | null,
        login: vi.fn(async () => {}),
        logout: vi.fn(async () => {}),
        restoreAuth: vi.fn(async () => {}),
        applyTokenSession: vi.fn(async (t: { accessToken: string }) => {
          auth.accessToken = t.accessToken
        }),
      }
      const ok = { succeeded: true, success: true, code: 200, data: {} }
      const tokens = { accessToken: 'a', refreshToken: 'r', expiresIn: 1 }
      const authApi = {
        loginWithRefreshToken: vi.fn(async () => ({ ...ok, data: { ...tokens } })),
        verifyTwoFactor: vi.fn(async () => ({ ...ok, data: { ...tokens } })),
        sendTwoFactorCode: vi.fn(async () => ok),
        sendCodeLoginCode: vi.fn(async () => ok),
        sendPasswordRecoveryCode: vi.fn(async () => ok),
        sendQuickRegisterCode: vi.fn(async () => ok),
        getCaptchaJson: vi.fn(async () => ({ ...ok, data: { captchaId: 'cid', imageBase64: 'IMG', expirationSeconds: 300 } })),
        codeLogin: vi.fn(async () => ({ ...ok, data: { ...tokens } })),
        resetPasswordByCode: vi.fn(async () => ok),
        quickRegister: vi.fn(async () => ({ ...ok, data: { userId: 'u', userName: 'n', requirePasswordSetup: false } })),
        setPassword: vi.fn(async () => ok),
      }
      const http = {
        get: async () => ({ success: true, code: 200, data: null }),
        post: async () => ({ success: true, code: 200, data: null }),
        addUnauthorizedListener: () => () => {},
        getAccessToken: () => auth.accessToken,
      }
      return { http, auth, authApi }
    }

    function installWithRuntime(runtime: ReturnType<typeof makeRuntime>, login?: AdminLoginConfig) {
      const app = createApp({ render: () => h('div') })
      const pinia = createPinia()
      app.use(pinia)
      setActivePinia(pinia)
      const router = {
        beforeEach: vi.fn(),
        afterEach: vi.fn(),
        onError: vi.fn(),
        replace: vi.fn(),
        currentRoute: { value: { name: 'login', path: '/admin/login', fullPath: '/admin/login', query: {} } },
      } as unknown as Router
      defineAdminApp({ runtime: runtime as never, login }).install(app, pinia, router)
      const cfg = app._context.provides[
        ADMIN_LOGIN_CONFIG_KEY as unknown as string | symbol
      ] as AdminLoginConfig
      return { cfg, router }
    }

    it('auto-generates the five standard login callbacks from the runtime', () => {
      const { cfg } = installWithRuntime(makeRuntime())
      const cbs = cfg.callbacks!
      expect(typeof cbs.pwdLogin).toBe('function')
      expect(typeof cbs.codeLogin).toBe('function')
      expect(typeof cbs.sendCode).toBe('function')
      expect(typeof cbs.resetPwd).toBe('function')
      expect(typeof cbs.register).toBe('function')
    })

    it('a consumer callback overrides the framework default for its slot', () => {
      const customSendCode = vi.fn(async () => {})
      // sendCode is not wrapped by after() (only pwd/code are), so the override
      // is the exact function - proving the consumer wins its slot.
      const { cfg } = installWithRuntime(makeRuntime(), { callbacks: { sendCode: customSendCode } })
      expect(cfg.callbacks!.sendCode).toBe(customSendCode)
    })

    it('provides a default onLogout that calls auth.logout() + redirects to login by name', async () => {
      const runtime = makeRuntime()
      const { cfg, router } = installWithRuntime(runtime)
      expect(typeof cfg.user?.onLogout).toBe('function')
      await cfg.user!.onLogout!()
      expect(runtime.auth.logout).toHaveBeenCalledTimes(1)
      expect((router as unknown as { replace: ReturnType<typeof vi.fn> }).replace).toHaveBeenCalledWith({
        name: 'login',
      })
    })

    it('drives pwdLogin through loginWithRefreshToken + applyTokenSession on success', async () => {
      const runtime = makeRuntime()
      const { cfg } = installWithRuntime(runtime)
      await cfg.callbacks!.pwdLogin!({ userName: 'admin', password: 'pw', remember: false }, {
        setTwoFactorRequired: vi.fn(),
        clearTwoFactor: vi.fn(),
        setCaptchaRequired: vi.fn(),
        clearCaptcha: vi.fn(),
      })
      expect(runtime.authApi.loginWithRefreshToken).toHaveBeenCalledWith({ userName: 'admin', password: 'pw' })
      expect(runtime.auth.applyTokenSession).toHaveBeenCalled()
    })

    it('pwdLogin on a 2FA challenge sets the challenge (no session) instead of failing', async () => {
      const runtime = makeRuntime()
      runtime.authApi.loginWithRefreshToken = vi.fn(async () => ({
        succeeded: false,
        success: false,
        code: 403,
        errorCode: '2FA_REQUIRED',
        errorDetails: { tempToken: 'tt', supportedTypes: ['Totp', 'Email'] },
      })) as never
      const { cfg } = installWithRuntime(runtime)
      const setTwoFactorRequired = vi.fn()
      await cfg.callbacks!.pwdLogin!({ userName: 'admin', password: 'pw', remember: false }, {
        setTwoFactorRequired,
        clearTwoFactor: vi.fn(),
        setCaptchaRequired: vi.fn(),
        clearCaptcha: vi.fn(),
      })
      // Challenge handed to the shell: preferred (first) = TOTP, all enabled
      // methods carried so the UI can offer a switcher.
      expect(setTwoFactorRequired).toHaveBeenCalledWith({
        challengeId: 'tt',
        userName: 'admin',
        method: 'totp',
        methods: ['totp', 'email'],
      })
      // No session established, no premature code send for TOTP.
      expect(runtime.auth.applyTokenSession).not.toHaveBeenCalled()
      expect(runtime.authApi.sendTwoFactorCode).not.toHaveBeenCalled()
    })

    it('verifyTwoFactor verifies the code + establishes the session', async () => {
      const runtime = makeRuntime()
      runtime.authApi.loginWithRefreshToken = vi.fn(async () => ({
        succeeded: false,
        success: false,
        code: 403,
        errorCode: '2FA_REQUIRED',
        errorDetails: { tempToken: 'tt', supportedTypes: ['Totp'] },
      })) as never
      const { cfg } = installWithRuntime(runtime)
      // Establish the pending challenge (remembers tempToken + type).
      await cfg.callbacks!.pwdLogin!({ userName: 'admin', password: 'pw', remember: false }, {
        setTwoFactorRequired: vi.fn(),
        clearTwoFactor: vi.fn(),
        setCaptchaRequired: vi.fn(),
        clearCaptcha: vi.fn(),
      })
      await cfg.callbacks!.verifyTwoFactor!({ challengeId: 'tt', code: '123456' })
      // TwoFactorType is a string enum matching the wire (PascalCase).
      expect(runtime.authApi.verifyTwoFactor).toHaveBeenCalledWith({ tempToken: 'tt', code: '123456', type: 'Totp' })
      expect(runtime.auth.applyTokenSession).toHaveBeenCalled()
    })

    it('verifyTwoFactor honours a switched method (email) over the challenge default', async () => {
      const runtime = makeRuntime()
      runtime.authApi.loginWithRefreshToken = vi.fn(async () => ({
        succeeded: false,
        success: false,
        code: 403,
        errorCode: '2FA_REQUIRED',
        errorDetails: { tempToken: 'tt', supportedTypes: ['Totp', 'Email'] },
      })) as never
      const { cfg } = installWithRuntime(runtime)
      await cfg.callbacks!.pwdLogin!({ userName: 'admin', password: 'pw', remember: false }, {
        setTwoFactorRequired: vi.fn(),
        clearTwoFactor: vi.fn(),
        setCaptchaRequired: vi.fn(),
        clearCaptcha: vi.fn(),
      })
      // User switched to email in the UI → verify with the email type (2), not the
      // default TOTP.
      await cfg.callbacks!.verifyTwoFactor!({ challengeId: 'tt', code: '111111', method: 'email' })
      expect(runtime.authApi.verifyTwoFactor).toHaveBeenCalledWith({ tempToken: 'tt', code: '111111', type: 'Email' })
    })

    it('sends a code up front for an SMS/email-preferred 2FA challenge', async () => {
      const runtime = makeRuntime()
      runtime.authApi.loginWithRefreshToken = vi.fn(async () => ({
        succeeded: false,
        success: false,
        code: 403,
        errorCode: '2FA_REQUIRED',
        errorDetails: { tempToken: 'tt', supportedTypes: ['Email'] },
      })) as never
      const { cfg } = installWithRuntime(runtime)
      const setTwoFactorRequired = vi.fn()
      await cfg.callbacks!.pwdLogin!({ userName: 'admin', password: 'pw', remember: false }, {
        setTwoFactorRequired,
        clearTwoFactor: vi.fn(),
        setCaptchaRequired: vi.fn(),
        clearCaptcha: vi.fn(),
      })
      expect(setTwoFactorRequired).toHaveBeenCalledWith({
        challengeId: 'tt',
        userName: 'admin',
        method: 'email',
        methods: ['email'],
      })
      // Email 2FA → the code is delivered so the user has something to enter.
      expect(runtime.authApi.sendTwoFactorCode).toHaveBeenCalledWith({ tempToken: 'tt', type: 'Email' })
    })

    it('pwdLogin reveals the captcha (no session) when the backend demands one', async () => {
      const runtime = makeRuntime()
      runtime.authApi.loginWithRefreshToken = vi.fn(async () => ({
        succeeded: false,
        success: false,
        code: 400,
        errorCode: 'IDENTITY_CAPTCHA_REQUIRED',
        errorDetails: { captchaId: 'cid', imageBase64: 'IMG', expirationSeconds: 300 },
      })) as never
      const { cfg } = installWithRuntime(runtime)
      const setCaptchaRequired = vi.fn()
      await cfg.callbacks!.pwdLogin!({ userName: 'admin', password: 'pw', remember: false }, {
        setTwoFactorRequired: vi.fn(),
        clearTwoFactor: vi.fn(),
        setCaptchaRequired,
        clearCaptcha: vi.fn(),
      })
      // The fresh captcha from errorDetails is handed to the shell; no session.
      expect(setCaptchaRequired).toHaveBeenCalledWith({ captchaId: 'cid', imageBase64: 'IMG', expirationSeconds: 300 })
      expect(runtime.auth.applyTokenSession).not.toHaveBeenCalled()
    })

    it('pwdLogin forwards the captcha id/code it was given', async () => {
      const runtime = makeRuntime()
      const { cfg } = installWithRuntime(runtime)
      await cfg.callbacks!.pwdLogin!(
        { userName: 'admin', password: 'pw', remember: false, captchaId: 'cid', captchaCode: 'abcd' },
        { setTwoFactorRequired: vi.fn(), clearTwoFactor: vi.fn(), setCaptchaRequired: vi.fn(), clearCaptcha: vi.fn() },
      )
      expect(runtime.authApi.loginWithRefreshToken).toHaveBeenCalledWith({
        userName: 'admin',
        password: 'pw',
        captchaId: 'cid',
        captchaCode: 'abcd',
      })
    })

    it('getCaptcha maps getCaptchaJson to the login-captcha shape', async () => {
      const runtime = makeRuntime()
      const { cfg } = installWithRuntime(runtime)
      const c = await cfg.callbacks!.getCaptcha!('register')
      expect(runtime.authApi.getCaptchaJson).toHaveBeenCalledWith('register')
      expect(c).toEqual({ captchaId: 'cid', imageBase64: 'IMG', expirationSeconds: 300 })
    })

    it('sendCode forwards the captcha to the quick-register send-code (register purpose)', async () => {
      const runtime = makeRuntime()
      const { cfg } = installWithRuntime(runtime)
      await cfg.callbacks!.sendCode!({
        account: 'a@b.com',
        type: 'email',
        purpose: 'register',
        captchaId: 'cid',
        captchaCode: 'abcd',
      })
      expect(runtime.authApi.sendQuickRegisterCode).toHaveBeenCalledWith(
        expect.objectContaining({ captchaId: 'cid', captchaCode: 'abcd' }),
      )
    })

    it('enables the auth guard by default and threads runtime.auth.restoreAuth as the restore hook', async () => {
      const runtime = makeRuntime()
      const guards: unknown[] = []
      const app = createApp({ render: () => h('div') })
      const pinia = createPinia()
      app.use(pinia)
      setActivePinia(pinia)
      const router = {
        beforeEach: vi.fn((g: unknown) => guards.push(g)),
        afterEach: vi.fn(),
        onError: vi.fn(),
        replace: vi.fn(),
        currentRoute: { value: { name: 'x', path: '/admin/x', fullPath: '/admin/x' } },
      } as unknown as Router
      defineAdminApp({ runtime: runtime as never }).install(app, pinia, router)
      const signedOut = { meta: { requiresAuth: true }, name: 'x', path: '/admin/x', fullPath: '/admin/x', query: {} }
      for (const g of guards) await (g as (...a: unknown[]) => unknown)(signedOut, {}, vi.fn())
      expect(runtime.auth.restoreAuth).toHaveBeenCalled()
    })

    it('uses runtime.http as the client when `client` is omitted', () => {
      // No throw = the client was resolved from runtime.http.
      expect(() => defineAdminApp({ runtime: makeRuntime() as never })).not.toThrow()
    })

    it('throws when neither client nor runtime is supplied', () => {
      expect(() => defineAdminApp({} as never)).toThrow(/requires either .client. .* or .runtime./)
    })
  })

  describe('locales option', () => {
    it('registers host-app i18n overrides into the admin-app store at install time', () => {
      const app = createApp({ render: () => h('div') })
      const pinia = createPinia()
      app.use(pinia)
      setActivePinia(pinia)
      defineAdminApp({
        client: dummyClient,
        locales: { en: { tnzi: { admin: { modules: { foo: { title: 'Foo' } } } } } },
      }).install(app, pinia)
      const store = useAdminAppStore()
      const en = store.messageOverrides.en as Record<string, unknown>
      expect(en.tnzi).toBeTruthy()
    })
  })

  it('hideRoutes marks the matching sub-menu meta.hideInMenu without touching others', () => {
    const { routes } = defineAdminApp({
      client: dummyClient,
      hideRoutes: ['identity.tenants'],
    })
    const admin = findAdminRoute(routes)
    const identity = admin?.children?.find(
      (c) => typeof c.name === 'string' && c.name === 'identity',
    )
    expect(identity).toBeTruthy()
    const tenants = identity?.children?.find(
      (c) => typeof c.name === 'string' && c.name === 'identity.tenants',
    )
    const users = identity?.children?.find(
      (c) => typeof c.name === 'string' && c.name === 'identity.users',
    )
    expect(tenants).toBeTruthy()
    expect(users).toBeTruthy()
    expect((tenants!.meta as Record<string, unknown>).hideInMenu).toBe(true)
    // Sibling sub-menus are untouched.
    expect((users!.meta as Record<string, unknown>).hideInMenu).toBeFalsy()
  })

  it('hideRoutes drops the matched entry from the rendered menu tree', () => {
    const app = createApp({ render: () => h('div') })
    const pinia = createPinia()
    app.use(pinia)
    setActivePinia(pinia)

    const { install } = defineAdminApp({
      client: dummyClient,
      hideRoutes: ['identity.tenants'],
    })
    install(app, pinia)

    // Super-user renders the full menu (the permission filter no longer fails
    // open when logged out), isolating the hideRoutes filter under test.
    useAdminAuthStore().setSuperUser(true)
    const routeStore = useAdminRouteStore()
    const identityMenu = routeStore.menus.find((m) => m.key === 'identity')
    expect(identityMenu).toBeTruthy()
    const childKeys = (identityMenu!.children ?? []).map((c) => c.key)
    expect(childKeys).not.toContain('identity.tenants')
    // Sibling entries still render - we only hide the targeted route.
    expect(childKeys).toContain('identity.users')
  })

  it('hideRoutes is case-sensitive on exact route.name match', () => {
    const { routes } = defineAdminApp({
      client: dummyClient,
      // Wrong case - should NOT hide anything.
      hideRoutes: ['Identity.Tenants'],
    })
    const admin = findAdminRoute(routes)
    const identity = admin?.children?.find(
      (c) => typeof c.name === 'string' && c.name === 'identity',
    )
    const tenants = identity?.children?.find(
      (c) => typeof c.name === 'string' && c.name === 'identity.tenants',
    )
    expect(tenants).toBeTruthy()
    expect((tenants!.meta as Record<string, unknown>).hideInMenu).toBeFalsy()
  })

  it('hideRoutes works alongside hideModules without conflict', () => {
    const { routes } = defineAdminApp({
      client: dummyClient,
      hideModules: ['payment'],
      hideRoutes: ['identity.tenants', 'system.signalr'],
    })
    const admin = findAdminRoute(routes)
    const childNames = namesOfChildren(admin)
    // hideModules still strips the module entirely.
    expect(childNames).not.toContain('payment')
    // hideRoutes targets survive at the route table level.
    expect(childNames).toContain('identity')
    expect(childNames).toContain('system')
    const identity = admin?.children?.find((c) => c.name === 'identity')
    const system = admin?.children?.find((c) => c.name === 'system')
    const tenants = identity?.children?.find((c) => c.name === 'identity.tenants')
    const signalr = system?.children?.find((c) => c.name === 'system.signalr')
    expect((tenants!.meta as Record<string, unknown>).hideInMenu).toBe(true)
    expect((signalr!.meta as Record<string, unknown>).hideInMenu).toBe(true)
  })

  it('framework built-in routes ship with default meta.order', () => {
    const { routes } = defineAdminApp({ client: dummyClient })
    const admin = findAdminRoute(routes)
    const childByName = (name: string) =>
      admin?.children?.find((c) => typeof c.name === 'string' && c.name === name)
    const metaOrder = (name: string): number | undefined => {
      const route = childByName(name)
      return (route?.meta as Record<string, unknown> | undefined)?.order as number | undefined
    }
    expect(metaOrder('dashboard')).toBe(0)
    expect(metaOrder('identity')).toBe(100)
    expect(metaOrder('authorization')).toBe(110)
    expect(metaOrder('system')).toBe(120)
    expect(metaOrder('audit')).toBe(130)
    expect(metaOrder('chat')).toBe(140)
    expect(metaOrder('ai')).toBe(150)
    expect(metaOrder('storage')).toBe(160)
    expect(metaOrder('notification')).toBe(170)
    expect(metaOrder('payment')).toBe(180)
    expect(metaOrder('template')).toBe(190)
  })

  it('routeOrders overrides the default order of a framework route', () => {
    const { routes } = defineAdminApp({
      client: dummyClient,
      routeOrders: { dashboard: 5 },
    })
    const admin = findAdminRoute(routes)
    const dashboard = admin?.children?.find(
      (c) => typeof c.name === 'string' && c.name === 'dashboard',
    )
    expect(dashboard).toBeTruthy()
    expect((dashboard!.meta as Record<string, unknown>).order).toBe(5)
  })

  it('routeOrders does not affect routes not listed in it', () => {
    const { routes } = defineAdminApp({
      client: dummyClient,
      routeOrders: { dashboard: 5 },
    })
    const admin = findAdminRoute(routes)
    const identity = admin?.children?.find(
      (c) => typeof c.name === 'string' && c.name === 'identity',
    )
    expect(identity).toBeTruthy()
    // identity stays at its framework default (100).
    expect((identity!.meta as Record<string, unknown>).order).toBe(100)
  })

  it('routeOrders works alongside hideRoutes and hideModules', () => {
    const { routes } = defineAdminApp({
      client: dummyClient,
      hideModules: ['payment'],
      hideRoutes: ['identity.tenants'],
      routeOrders: { dashboard: 5, authorization: 95 },
    })
    const admin = findAdminRoute(routes)
    const childNames = namesOfChildren(admin)
    // hideModules still strips payment.
    expect(childNames).not.toContain('payment')
    // hideRoutes still flips meta.hideInMenu on the target.
    const identity = admin?.children?.find((c) => c.name === 'identity')
    const tenants = identity?.children?.find((c) => c.name === 'identity.tenants')
    expect((tenants!.meta as Record<string, unknown>).hideInMenu).toBe(true)
    // routeOrders applies its overrides to the same tree.
    const dashboard = admin?.children?.find((c) => c.name === 'dashboard')
    const authorization = admin?.children?.find((c) => c.name === 'authorization')
    expect((dashboard!.meta as Record<string, unknown>).order).toBe(5)
    expect((authorization!.meta as Record<string, unknown>).order).toBe(95)
    // Untouched framework default remains intact.
    expect((identity!.meta as Record<string, unknown>).order).toBe(100)
  })

  it('normalizes module names case-insensitively for hideModules', () => {
    const { routes: hideLower } = defineAdminApp({
      client: dummyClient,
      hideModules: ['payment'],
    })
    const { routes: hideUpper } = defineAdminApp({
      client: dummyClient,
      hideModules: ['PAYMENT'],
    })
    expect(namesOfChildren(findAdminRoute(hideLower))).toEqual(
      namesOfChildren(findAdminRoute(hideUpper)),
    )
  })

  describe('basePath', () => {
    function findByName(routes: RouteRecordRaw[], name: string): RouteRecordRaw | undefined {
      return routes.find((r) => r.name === name)
    }

    it('defaults to /admin and prefixes EVERY route (login/403/404/500 included)', () => {
      const { routes } = defineAdminApp({ client: dummyClient })
      const adminRoot = findByName(routes, 'admin-root')
      const login = findByName(routes, 'login')
      const forbidden = findByName(routes, 'forbidden')
      expect(adminRoot?.path).toBe('/admin')
      // Since 0.2.71 the default basePath prefixes login/403 too, so the
      // whole app lives under ONE prefix. Under an IIS sub-application
      // mounted at /admin, auth redirects previously escaped to the
      // domain-root '/login' and 404'd.
      expect(login?.path).toBe(
        '/admin/login/:module(pwd-login|code-login|register|reset-pwd|bind-wechat|two-factor)?',
      )
      expect(forbidden?.path).toBe('/admin/403')
      // The 404 / 500 exception routes are top-level too and share the prefix.
      expect(findByName(routes, 'not-found')?.path).toBe('/admin/404')
      expect(findByName(routes, 'server-error')?.path).toBe('/admin/500')
    })

    it('basePath="/console" prefixes admin-root and login', () => {
      const { routes } = defineAdminApp({ client: dummyClient, basePath: '/console' })
      const adminRoot = findByName(routes, 'admin-root')
      const login = findByName(routes, 'login')
      const forbidden = findByName(routes, 'forbidden')
      expect(adminRoot?.path).toBe('/console')
      expect(typeof login?.path).toBe('string')
      expect(login?.path as string).toContain('/console/login')
      // The original path-param shape is preserved after the prefix.
      expect((login?.path as string).startsWith('/console/login/:module(')).toBe(true)
      expect(forbidden?.path).toBe('/console/403')
    })

    it('basePath="/" deploys at domain root without producing //login', () => {
      const { routes } = defineAdminApp({ client: dummyClient, basePath: '/' })
      const adminRoot = findByName(routes, 'admin-root')
      const login = findByName(routes, 'login')
      const forbidden = findByName(routes, 'forbidden')
      expect(adminRoot?.path).toBe('/')
      // Login at root must keep its original shape - no leading "//".
      expect(login?.path).toBe(
        '/login/:module(pwd-login|code-login|register|reset-pwd|bind-wechat|two-factor)?',
      )
      expect((login?.path as string).startsWith('//')).toBe(false)
      expect(forbidden?.path).toBe('/403')
    })

    it('basePath normalizes input variants to /admin', () => {
      const variants = ['admin', '/admin', '/admin/', ' admin ', '']
      const reference = defineAdminApp({ client: dummyClient }).routes
      const refAdmin = findByName(reference, 'admin-root')?.path
      const refLogin = findByName(reference, 'login')?.path
      const refForbidden = findByName(reference, 'forbidden')?.path
      for (const v of variants) {
        const { routes } = defineAdminApp({ client: dummyClient, basePath: v })
        expect(findByName(routes, 'admin-root')?.path).toBe(refAdmin)
        expect(findByName(routes, 'login')?.path).toBe(refLogin)
        expect(findByName(routes, 'forbidden')?.path).toBe(refForbidden)
      }
    })

    it('basePath normalizes "/console/" → "/console"', () => {
      const a = defineAdminApp({ client: dummyClient, basePath: '/console/' }).routes
      const b = defineAdminApp({ client: dummyClient, basePath: 'console' }).routes
      expect(findByName(a, 'admin-root')?.path).toBe('/console')
      expect(findByName(b, 'admin-root')?.path).toBe('/console')
      expect(findByName(a, 'login')?.path).toBe(findByName(b, 'login')?.path)
    })

    it('basePath works alongside hideRoutes / hideModules / routeOrders', () => {
      const { routes } = defineAdminApp({
        client: dummyClient,
        basePath: '/console',
        hideModules: ['payment'],
        hideRoutes: ['identity.tenants'],
        routeOrders: { dashboard: 5, authorization: 95 },
      })
      const adminRoot = findByName(routes, 'admin-root')
      expect(adminRoot?.path).toBe('/console')
      const childNames = namesOfChildren(adminRoot)
      // hideModules still strips payment under the rewritten root.
      expect(childNames).not.toContain('payment')
      expect(childNames).toContain('identity')
      // hideRoutes flag still flips on a deep child.
      const identity = adminRoot?.children?.find((c) => c.name === 'identity')
      const tenants = identity?.children?.find((c) => c.name === 'identity.tenants')
      expect((tenants!.meta as Record<string, unknown>).hideInMenu).toBe(true)
      // routeOrders overrides land on the prefixed tree.
      const dashboard = adminRoot?.children?.find((c) => c.name === 'dashboard')
      const authorization = adminRoot?.children?.find((c) => c.name === 'authorization')
      expect((dashboard!.meta as Record<string, unknown>).order).toBe(5)
      expect((authorization!.meta as Record<string, unknown>).order).toBe(95)
    })

    it('install seeds the route store with paths under basePath', () => {
      const app = createApp({ render: () => h('div') })
      const pinia = createPinia()
      app.use(pinia)
      setActivePinia(pinia)

      const { install } = defineAdminApp({
        client: dummyClient,
        basePath: '/console',
      })
      install(app, pinia)

      const routeStore = useAdminRouteStore()
      const identity = routeStore.authRoutes.find((r) => r.name === 'identity')
      expect(identity).toBeTruthy()
      // Top-level menu entry sits under /console.
      expect(identity!.path).toBe('/console/identity')
      const users = identity!.children?.find((c) => c.name === 'identity.users')
      expect(users?.path).toBe('/console/identity/users')
    })

    it('install seeds the route store with no prefix when basePath="/"', () => {
      const app = createApp({ render: () => h('div') })
      const pinia = createPinia()
      app.use(pinia)
      setActivePinia(pinia)

      const { install } = defineAdminApp({
        client: dummyClient,
        basePath: '/',
      })
      install(app, pinia)

      const routeStore = useAdminRouteStore()
      const identity = routeStore.authRoutes.find((r) => r.name === 'identity')
      expect(identity?.path).toBe('/identity')
      const users = identity!.children?.find((c) => c.name === 'identity.users')
      expect(users?.path).toBe('/identity/users')
    })
  })
})
