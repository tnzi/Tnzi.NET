/**
 * `createAdminApp()` - full one-call bootstrap for a Tnzi admin app.
 *
 * `defineAdminApp()` returns `{ routes, install, … }` and leaves the consumer
 * to repeat the SAME ~15-line ceremony in every `main.ts`: assemble the root
 * redirect + 404 catch-all, `createRouter`, `createPinia`, `createApp`,
 * `app.use()` twice, `install()`, then `app.mount()`. `createAdminApp()`
 * folds all of that into one call so the consumer writes:
 *
 * ```ts
 * import { createTnziClient } from '@tnzi/core/state'
 * import { createAdminApp } from '@tnzi/ui-admin'
 * import App from './App.vue'
 *
 * createAdminApp({
 *   rootComponent: App as never,          // one cast at the Vue-typedef boundary
 *   runtime: createTnziClient({ baseUrl: '/api' }),
 *   historyBase: import.meta.env.BASE_URL,
 *   addModules: myRoutes,
 *   login: { brand: 'My App', … },
 *   locales: { en, 'zh-cn' },
 * }).mount('#app')
 * ```
 *
 * The framework owns router assembly (root redirect + catch-all), the
 * pinia / app wiring, `install()`, and i18n registration ordering. The
 * consumer still owns everything genuinely app-specific: the root component,
 * the deployment history base, extra public routes, branding, modules.
 */

import { createApp, type App, type Component } from 'vue'
import { createPinia, type Pinia } from 'pinia'
import {
  createRouter,
  createWebHistory,
  type Router,
  type RouteRecordRaw,
} from 'vue-router'
import {
  defineAdminApp,
  normalizeBasePath,
  type DefineAdminAppOptions,
  type DefineAdminAppResult,
} from './defineAdminApp'

export interface CreateAdminAppOptions extends DefineAdminAppOptions {
  /**
   * Root component to mount - usually your `App.vue`. Because the consumer's
   * SFC is typed against ITS `@vue/runtime-core` and this factory calls
   * `createApp` with the framework's, pass it with a single `as never` cast at
   * the workspace typedef boundary (runtime is a single Vue). This replaces
   * the three `as never` casts consumers used to write on app / pinia / router.
   */
  rootComponent: Component

  /**
   * vue-router history base. Pass `import.meta.env.BASE_URL` for
   * deployment-prefix independence (the recommended shape - Vite `base` is the
   * single source of truth for the deployment prefix). Omit for the domain
   * root (`createWebHistory()`).
   */
  historyBase?: string

  /**
   * Extra ROOT-level routes (siblings of `/admin`, not under it) - e.g. public
   * standalone pages with no admin shell / auth. The root redirect and the 404
   * catch-all are added automatically after these.
   */
  rootRoutes?: RouteRecordRaw[]

  /**
   * Redirect target for the automatically-added `/:pathMatch(.*)*` catch-all.
   * Defaults to the built-in `not-found` route (by name, so it follows any
   * basePath). Pass e.g. `{ name: 'dashboard' }` to bounce unknown URLs to the
   * shell instead of showing the 404 page.
   */
  notFoundRedirect?: RouteRecordRaw['redirect']
}

export interface AdminAppHandle extends DefineAdminAppResult {
  /** The created Vue app. */
  app: App
  /** The created pinia instance. */
  pinia: Pinia
  /** The created vue-router instance (framework routes + root redirect + catch-all). */
  router: Router
  /** The installed `@tnzi/ui-admin` plugin instance. */
  instance: ReturnType<DefineAdminAppResult['install']>
  /** The full assembled route table (framework + consumer + root redirect + catch-all). */
  routes: RouteRecordRaw[]
  /** Mount the app into the DOM. Returns the app for chaining. */
  mount(selector: string | Element): App
}

/**
 * Bootstrap a complete Tnzi admin app in one call. Assembles the router
 * (framework preset + your `addModules` + `rootRoutes` + auto root-redirect +
 * auto 404 catch-all), creates pinia + the Vue app, installs the plugin, and
 * returns a handle whose `.mount('#app')` finishes startup.
 */
export function createAdminApp(options: CreateAdminAppOptions): AdminAppHandle {
  const def = defineAdminApp(options)

  const basePath = normalizeBasePath(options.basePath)
  // Root redirect: send '/' to the admin root. Skipped for domain-root
  // deployment (basePath '/'), where the admin root IS '/'.
  const rootRedirect: RouteRecordRaw[] =
    basePath === '/' ? [] : [{ path: '/', redirect: basePath } as RouteRecordRaw]
  // Unknown URLs land on the built-in 404 page BY NAME (so it follows the
  // basePath), instead of silently bouncing to the dashboard.
  const catchAll: RouteRecordRaw = {
    path: '/:pathMatch(.*)*',
    redirect: options.notFoundRedirect ?? { name: 'not-found' },
  } as RouteRecordRaw

  const routes: RouteRecordRaw[] = [
    ...def.routes,
    ...(options.rootRoutes ?? []),
    ...rootRedirect,
    catchAll,
  ]

  const router = createRouter({
    history: createWebHistory(options.historyBase),
    routes,
  })
  const pinia = createPinia()
  const app = createApp(options.rootComponent)
  app.use(pinia)
  app.use(router)

  const instance = def.install(app, pinia, router)

  return {
    ...def,
    routes,
    app,
    pinia,
    router,
    instance,
    mount(selector: string | Element): App {
      app.mount(selector)
      return app
    },
  }
}
