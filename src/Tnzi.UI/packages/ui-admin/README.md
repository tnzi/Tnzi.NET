# @tnzi/ui-admin

Production-grade Vue 3 admin framework built on Naive UI + Pinia. Ships
**59 preset admin pages**, **6 layout modes**, **5-tab theme drawer**,
**backend-driven module auto-discovery**, **`v-permission` directive**,
**i18n-reactive form rules**, and **`TAdminAppRoot` one-shot provider
stack** out of the box.

Designed to consume in three lines of `main.ts` and one line of `App.vue`.

## Quick start

```bash
pnpm add @tnzi/ui-admin @tnzi/core @tnzi/ui naive-ui pinia vue-router
```

**`main.ts` — five lines past the imports:**

```ts
import { createApp } from 'vue'
import { createPinia } from 'pinia'
import { createRouter, createWebHistory } from 'vue-router'
import { defineAdminApp } from '@tnzi/ui-admin'
import '@tnzi/ui-admin/style.css'
import App from './App.vue'
import { httpClient } from './http'

const { routes, install } = defineAdminApp({
  client: httpClient,
  hideModules: ['Payment'],            // optional
  overridePages: { 'identity.users': MyCustomUserPage }, // optional
})
const router = createRouter({ history: createWebHistory(), routes })
const pinia = createPinia()
const app = createApp(App)
app.use(pinia); app.use(router)
install(app, pinia)
app.mount('#app')
```

**`App.vue` — one line (0.2.72+ via `TAdminAppRoot`):**

```vue
<script setup lang="ts">
import { TAdminAppRoot } from '@tnzi/ui-admin'
</script>

<template>
  <TAdminAppRoot />
</template>
```

`TAdminAppRoot` mounts the full Naive UI provider stack
(`NConfigProvider` + `NLoadingBarProvider` + `NMessageProvider` +
`NNotificationProvider` + `NDialogProvider`), wires it to the admin
theme context, and renders `<router-view />`. Pass `theme` /
`themeOverrides` props to override; pass a default slot (or
`router-view="false"`) to host non-router content.

Sign in and you get the full preset menu + 59 pages, a working 🎨 theme
drawer in the header, and `v-permission` ready to use.

## Customizing

### Deploying under a sub-path

By default the admin SPA mounts under `/admin` — `admin-root` lives at
`/admin`, the login shell at `/login/:module(...)?`, and `403` at `/403`.
That works out of the box when the app is the only thing on its origin.

When you need to host the admin SPA under a **different prefix** (for
example, an IIS sub-application at `/console`, a Kubernetes ingress
behind `/portal`, or directly at the domain root) pass `basePath`:

```ts
// Default — same behaviour as today
defineAdminApp({ client })

// Custom base path
defineAdminApp({ client, basePath: '/console' })
// → admin-root = '/console'
// → login     = '/console/login/:module(...)?'
// → 403       = '/console/403'

// Domain root deployment
defineAdminApp({ client, basePath: '/' })
// → admin-root = '/'
// → login     = '/login/:module(...)?'   (no leading //)
// → 403       = '/403'
```

`basePath` rewrites every **top-level** framework route so the router's
internal path matches the browser URL the consumer actually sees.
Routes inside `admin-root.children` use relative paths and are not
touched — they inherit the new parent automatically.

**Why does this exist?** Without it, sub-path deploys hit a router /
hosting mismatch: if the SPA is served from `/console/` but the router
expects `/admin/...` and `/login/...` at the top level, redirects from
the auth guard (e.g. `{ name: 'login' }`) compute `/login`, the host
hands that URL to whatever serves the domain root, and the admin SPA
loses control.

**Normalization** — accepts forgiving input:

| Input | Normalized |
|-------|------------|
| `undefined` / `null` / `''` | `/admin` (default) |
| `'admin'` / `'/admin'` / `'/admin/'` | `/admin` |
| `'console'` / `'/console'` / `'/console/'` | `/console` |
| `'/'` | `/` (special — domain root) |

**Relationship to Vite `base` and `createWebHistory()`** — three
distinct concerns, often confused:

| Concern | Where it lives | What it controls |
|---------|----------------|------------------|
| **Asset prefix** | `vite.config.ts` `base` | Where the built `index.html` looks for `/assets/*.js` and `/assets/*.css` |
| **History base** | `createWebHistory(base)` | What URL prefix vue-router strips off `window.location` before matching routes |
| **Router base path** (this option) | `defineAdminApp({ basePath })` | The prefix written into the route table's top-level paths |

The common deploy combos:

1. **Sub-application that already strips the prefix** (IIS sub-app,
   nginx `root` rewrite): the SPA sees the trailing path with no
   prefix. Use `vite base: '/admin/'`, `createWebHistory()` (default
   `'/'`), and `defineAdminApp({ basePath: '/admin' })`. The browser
   shows `/admin/identity/users`, the server hands `/identity/users`
   to vue-router, and `basePath` writes `/admin/identity/users` into
   the route table so the URLs line up.
2. **Reverse proxy that keeps the prefix**: use `vite base: '/admin/'`,
   `createWebHistory('/admin/')`, and `defineAdminApp({ basePath: '/admin' })`.
   vue-router strips `/admin/` before matching, then `basePath`-rewritten
   routes add it back when computing hrefs.
3. **Domain root**: leave Vite + history at their defaults and pass
   `basePath: '/'`.

### Hide built-in modules

```ts
defineAdminApp({ client, hideModules: ['Payment', 'AI'] })
```

Or invert: only show what you whitelist.

```ts
defineAdminApp({ client, showOnlyModules: ['Identity', 'System'] })
```

### Hide individual sub-menu entries

When you want to drop a single sub-menu (e.g. `identity.tenants`) but keep
the rest of its parent module visible, use `hideRoutes`. Accepts exact
vue-router route names (case-sensitive); the matched routes stay in the
route table — only `meta.hideInMenu` is flipped so the sidebar skips them.

```ts
defineAdminApp({
  client,
  hideRoutes: ['identity.tenants', 'identity.organizations', 'system.signalr'],
})
```

`hideRoutes` and `hideModules` are independent and may be combined —
`hideModules` strips a top-level module entirely (routes removed),
`hideRoutes` hides a single sub-menu (route stays reachable by deep link
or `router.push`).

### Override sidebar ordering

Every framework top-level module ships with a default `meta.order` so
the sidebar lays itself out predictably without any consumer mutation:

| Route name     | Default order |
| -------------- | ------------- |
| `workbench`    | `0`           |
| `identity`     | `100`         |
| `authorization`| `110`         |
| `system`       | `120`         |
| `audit`        | `130`         |
| `chat`         | `140`         |
| `ai`           | `150`         |
| `storage`      | `160`         |
| `notification` | `170`         |
| `payment`      | `180`         |
| `template`     | `190`         |

The step of 10 leaves room for consumer-injected entries:

- Slots `1..99` — between Workbench and the first framework module.
- Slots `200+` — after the last framework module.
- Use `routeOrders` to override any framework default.

```ts
defineAdminApp({
  client,
  routeOrders: {
    workbench: 5,          // shift Workbench from 0
    authorization: 95,      // pull ahead of identity (100)
  },
})
```

`routeOrders` is keyed by exact vue-router route `name` (case-sensitive)
and walks `/admin` children + grandchildren. It is independent of
`hideRoutes`, `hideModules`, `overridePages`, and `addModules` — combine
freely.

Consumer modules added via `addModules` should declare `meta.order`
themselves; pick a slot in `1..99` to land right after Workbench, or
`200+` to land after the last framework module.

### Override a built-in page

```ts
defineAdminApp({
  client,
  overridePages: {
    'identity.users': () => import('./pages/MyUserManagement.vue'),
  },
})
```

The route position, `meta`, permission checks, and keep-alive flag are
preserved — only the `component` is swapped.

### Add your own business pages

```ts
import type { RouteRecordRaw } from 'vue-router'

const myRoutes: RouteRecordRaw[] = [
  {
    path: 'reports',
    name: 'reports',
    component: () => import('./pages/ReportsPage.vue'),
    meta: { title: 'Reports', permission: 'reports.view' },
  },
]

defineAdminApp({ client, addModules: myRoutes })
```

Pages are mounted under `/admin/reports`.

### Login page

```ts
defineAdminApp({
  client,
  login: {
    brand: { title: 'My App', subtitle: 'Sign in to continue' },
    callbacks: {
      pwdLogin: async ({ userName, password }) => { /* ... */ },
      codeLogin: async ({ phone, code }) => { /* ... */ },
      register: async ({ ... }) => { /* ... */ },
      resetPassword: async ({ ... }) => { /* ... */ },
    },
    demoAccounts: [
      { label: 'Super', userName: 'super', password: 'super', description: 'Full access' },
      { label: 'Admin', userName: 'admin', password: 'admin', description: 'Admin role' },
    ],
    user: () => h(MyUserAvatarSlot),    // top-right slot in login toolbar
  },
})
```

5 modules are wired by `/login/:module(…)?` — `pwd-login`, `code-login`,
`register`, `reset-pwd`, `2fa-challenge`. Helpers
(`helpers.setTwoFactorRequired(...)`, `helpers.setCaptchaImage(...)`)
let pages drive UI state from auth callbacks.

### Workbench / dashboard widgets

```ts
import { defineAdminApp, type WidgetDef } from '@tnzi/ui-admin'

const widgets: WidgetDef[] = [
  { id: 'orders', component: () => import('./widgets/OrdersWidget.vue'), span: 12 },
  { id: 'kpis',   widgetType: 'kpi-cards', span: 24 },
]

defineAdminApp({
  client,
  workbench: { widgets, layout: 'draggable' },
})
```

Omit `workbench` to fall back to the bundled
`defaultWorkbenchWidgets()` (HeaderBanner + KPIs + business stats +
activity timeline + tips, 12 tiles total).

## Subpath imports

| Subpath | Exports |
|---|---|
| `@tnzi/ui-admin` | `defineAdminApp`, `createTnziUiAdmin`, `useAdminClient`, `vPermission`, `installDirectives`, `fetchAdminManifest`, `TAdminAppRoot` |
| `@tnzi/ui-admin/components` | `TAdminLoginCard`, `TIconPicker`, `TJsonEditor`, layout primitives (`TAdminAppRoot`, `TAdminAutoBreadcrumb`, `TAdminUserAvatar`, `TDarkModeContainer`, `TAdminRouterView`, `TSystemLogo`), display + utility re-exports from `@tnzi/ui` |
| `@tnzi/ui-admin/headless` | `useCrudPage`, `useFormRules`, `useNaiveForm`, `useAdminModuleManifest`, `useColumnSettings`, `useBatchActions`, `useFormModal`, `usePermissionGuard`, `useAdminMenuContext`, `useBreakpoint`, ... |
| `@tnzi/ui-admin/stores` | `useAdminThemeStore`, `useAdminAuthStore`, `useAdminAppStore`, `useAdminRouteStore`, `useAdminTabStore`, `useAdminPermissionStore` |
| `@tnzi/ui-admin/router` | `defaultAdminRoutes` (filtered/overridable via `defineAdminApp`) |
| `@tnzi/ui-admin/pages` | All 59 preset pages (also referenced by `defaultAdminRoutes`) |
| `@tnzi/ui-admin/widgets` | `WidgetDef`, `TWorkbenchLayout`, `TWidgetCard`, `useWidget`, `useWidgetData`, 14 built-in widgets, `defaultWorkbenchWidgets()` |

## Preset pages

The 59 pages auto-mount under `/admin/<module>/<feature>` and are
suppressed individually via `hideModules` / `overridePages`. Each
column / form schema lives in a sibling `*-config.ts` for thin .vue
files (~50 lines).

**identity** — `UserManagement`, `RoleManagement`, `TenantManagement`,
`OrganizationManagement`, `SessionManagement`, `LoginSecurity`,
`LoginLog`, `GdprRequests` (8)

**authorization** — `FunctionModule`, `EntityRole`, `RoleFunction`,
`Permission` (4)

**system** — `MenuManagement`, `ParameterManagement`,
`DictionaryManagement`, `FeatureManagement`, `ScheduledJob`,
`AccessLog`, `LogViewer`, `LocalizationMissing`, `Diagnostics`,
`HealthChecks`, `Performance`, `SignalRMonitor` (12)

**audit** — `AuditLog`, `AuditOperation` (2) — both render
`TAuditTimeline`

**storage** — `StorageFile`, `StorageChunk`, `StorageVersion` (3)

**template** — `TemplateManagement`, `TemplateLayout` (2)

**notification** — `NotificationMessage`, `NotificationSubscription`,
`NotificationTemplate` (3)

**payment** — `PaymentOrder`, `PaymentRefund`, `PaymentSubscription`,
`PaymentInvoice`, `PaymentPromotion`, `PaymentStatistics` (6)

**chat** — `ChatSession`, `ChatMessage` (2)

**ai** — `AgentList`, `AgentDetail`, `RunMonitor`, `ThreadList`,
`PersonaList`, `SkillList`, `WorkflowList`, `WorkflowEditor`,
`RunViewer`, `ProviderConfig`, `McpServerList`, `KbManager`,
`PermissionRules`, `QuotaRules`, `UsageDashboard`, `EvalViewer`,
`ChannelList`, `DeviceList`, `SandboxStatus`, `WorkspaceAgentList` (20)

**other** — `Workbench` (dashboard), `UserCenter` (account),
`login/index` (5-module auth shell) (3)

Total: 8 + 4 + 12 + 2 + 3 + 2 + 3 + 6 + 2 + 20 + 3 = 65 components,
of which 59 are routed pages (`TAuditTimeline` is shared by the 2
audit pages, `login/index` hosts 5 sub-modules under one route).

## v-permission directive

Auto-installed by `createTnziUiAdmin` / `defineAdminApp`.

```vue
<NButton v-permission="'user.delete'">Delete</NButton>
<NButton v-permission="['user.delete', 'user.update']">Edit & Delete</NButton>
<NButton v-permission.any="['admin', 'editor']">Either Role</NButton>
<NButton v-permission.hide="'feature.flag'">Hidden when no permission</NButton>
```

Super-admins (`isSuperUser: true` in the auth store) bypass every check.

## TCrudPage + useCrudPage

90% of the preset pages are thin shells around `TCrudPage` driven by
`useCrudPage`:

```vue
<script setup lang="ts">
import { TCrudPage } from '@tnzi/ui-admin/components'
import { useCrudPage } from '@tnzi/ui-admin/headless'
import { createIdentityBridge } from '@tnzi/ui-admin/services/bridges'

const bridge = createIdentityBridge({ client: useAdminClient() })
const crud = useCrudPage<UserDto>({
  pageId: 'identity.users',
  columns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (q) => bridge.users.fetch(q),
  createData: (d) => bridge.users.create(d),
  updateData: (id, d) => bridge.users.update(String(id), d),
  deleteData: (ids) => bridge.users.delete(ids.map(String)),
  // 0.2.72+ — fetch retry + error reporting
  retryFetch: 3,
  onError: (err, op) => { console.warn(`[users:${op}]`, err) },
})
crud.refresh().catch(() => undefined)
</script>

<template>
  <TCrudPage :state="crud" :all-columns="columns" :title="t('title')" :translate="t">
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer :schema="formSchema" :model="formData ?? {}" :readonly="mode === 'view'" />
    </template>
    <!-- 0.2.72+ — custom error UI (default is an NAlert with Retry) -->
    <template #error="{ error, retry, dismiss }">
      <MyErrorBanner :error="error" @retry="retry" @dismiss="dismiss" />
    </template>
  </TCrudPage>
</template>
```

`useCrudPage<T>` options (0.2.72+):
- `retryFetch` (default `3`) — number of retries with exponential
  backoff (300 → 600 → 1200ms); writes are never retried
- `retryDelayMs` (default `300`) — base delay for the exponential
  backoff
- `onError(err, op)` — called for every failed fetch/create/update/
  delete/export/import; return `false` to suppress the default
  `window.$message.error(err.message)` toast

`useCrudPage` return additions:
- `dismissError()` — clears `state.error` without re-fetching

## Form rules

```ts
import { useFormRules } from '@tnzi/ui-admin/headless'

const { rules } = useFormRules() // optional translate fn for i18n
const formRules = {
  userName: rules.userName,
  email: rules.email,
  password: rules.password({ min: 10, max: 32 }),
  confirmPassword: rules.matches(() => model.password),
}
```

Available: `required`, `text`, `userName`, `email`, `phone`, `password`,
`matches`, `url`, `json`, `integer`.

## Theme drawer

Auto-mounted by `AdminShellRoot`. Header 🎨 button toggles it.

Five tabs — **Appearance** (color mode + 12 preset colors + role
pickers), **Layout** (6 mode cards + sizing sliders + visibility
toggles), **General** (page transitions), **Watermark** (text +
opacity + auto-username + auto-date), **Preset** (copy / download /
import JSON config).

## Backend module manifest

The framework reads `GET admin/diagnostics/admin-manifest` to learn
which modules + admin controllers the backend actually exposes:

```ts
import { useAdminModuleManifest } from '@tnzi/ui-admin/headless'
import { useAdminClient } from '@tnzi/ui-admin'

const client = useAdminClient()
const { menuTree, modules, isAvailable } = useAdminModuleManifest({ client })
```

Use this to gate UI on backend feature availability ("only show the AI
menu when the AI module is loaded server-side").

## Troubleshooting

### Duplicate Vue / Pinia / Router instances under `pnpm link`

**Symptom**: `provide`/`inject` returns `undefined` even though the
provider is mounted; theme drawer doesn't react; tabs duplicate the
home route; Naive UI form rules fire on the wrong field.

**Root cause**: under `pnpm link` (typical when consuming this package
from a sibling workspace via `workspace:^`), the consumer's `node_modules`
holds one copy of `vue` / `vue-router` / `pinia` / `naive-ui` and the
linked `@tnzi/ui-admin` package holds its own. Two copies →
`Vue.provide` from one copy and `Vue.inject` from the other see
different `Symbol` keys.

**Fix** — add Vite `resolve.alias` (or `resolve.dedupe`) entries in
the consumer's `vite.config.ts`:

```ts
import { fileURLToPath } from 'node:url'

export default defineConfig({
  resolve: {
    dedupe: ['vue', 'vue-router', 'pinia', 'naive-ui'],
    alias: {
      vue: fileURLToPath(new URL('./node_modules/vue', import.meta.url)),
      'vue-router': fileURLToPath(new URL('./node_modules/vue-router', import.meta.url)),
      pinia: fileURLToPath(new URL('./node_modules/pinia', import.meta.url)),
      'naive-ui': fileURLToPath(new URL('./node_modules/naive-ui', import.meta.url)),
    },
  },
})
```

Once 0.2.71+, these four packages are declared as `peerDependencies`
(not `dependencies`) so they're resolved from the consumer instead of
the linked package — Vite's `dedupe` is enough in most cases.

### Stale dist after editing a source file in `@tnzi/ui` / `@tnzi/ui-admin`

**Symptom**: editing `src/components/X.vue` in `@tnzi/ui-admin` doesn't
take effect in the running dev server.

**Root cause**: when consumed via `pnpm link`, the resolver picks
`dist/index.js` (per `package.json#main`), not `src/`. The dist is
only rebuilt on `pnpm build`.

**Fix**:

```bash
pnpm --filter @tnzi/ui-admin build
# Vite cache may also need clearing in the consumer:
rm -rf path/to/consumer/node_modules/.vite
```

The bundled `/acme-up` skill (in the Tnzi monorepo) handles this
incrementally — rebuilds only `@tnzi/*` packages whose `src/` is newer
than `dist/` and clears the consumer's `.vite` cache before restarting.

### Blank page when wrapping `TAdminRouterView` in `<Suspense>` + `<KeepAlive>` with `defineAsyncComponent`

**Don't**. Vue 3.5 + Vue Router 4 have a long-standing incompatibility
between `Suspense` + `KeepAlive` + `defineAsyncComponent` — the inner
async component resolves to `undefined` and the route subtree silently
fails to render.

**Fix**: use `defineAsyncComponent` *inside* the route's component
factory (not as a sibling wrapped in Suspense/KeepAlive), or hoist the
async loader out to `routes[i].component` so vue-router's own
async-component handler resolves it:

```ts
// GOOD
{ path: 'workflows/editor', component: () => import('./pages/ai/workflows/WorkflowEditor.vue') }
```

```vue
<!-- BAD — silent blank page -->
<Suspense>
  <KeepAlive>
    <component :is="defineAsyncComponent(() => import('./HeavyPage.vue'))" />
  </KeepAlive>
</Suspense>
```

### `"useTheme: no theme context found"` error during component setup

**Root cause**: a component used `useTheme()` from `@tnzi/ui` but no
`@tnzi/ui` plugin was installed upstream.

**Fix**: `createTnziUiAdmin()` already installs a fallback theme
context — if you're seeing this it means the plugin wasn't installed
before the component mounted. Make sure `install(app, pinia)` runs
before `app.mount(...)`.

## Testing

```bash
pnpm test              # vitest run — 690+ tests
pnpm test:coverage     # vitest --coverage (80/70/60 lines/branches/fns)
pnpm typecheck         # vue-tsc --noEmit --skipLibCheck
```

## CHANGELOG

See [../../CHANGELOG.md](../../CHANGELOG.md) for the full
version-by-version history. Most recent: **0.2.72** (Batch A1 / B1
cleanup, B2 fetch retry + onError, B4 page → bridge enforcement,
A3 readme rewrite, 2026-05-21).

See [../../MIGRATION.md](../../MIGRATION.md) for the 0.2.2 → 0.2.72
overview covering the production overhaul phases.
