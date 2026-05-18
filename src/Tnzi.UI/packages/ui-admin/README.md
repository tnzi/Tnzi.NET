# @tnzi/ui-admin

Production-grade Vue 3 admin framework on Naive UI + Pinia. Ships **42 preset
admin pages**, **6 layout modes**, **5-tab settings drawer**, **backend-driven
module auto-discovery**, **`v-permission` directive**, and **i18n-reactive
form rules** out of the box.

## Quick start

```bash
pnpm add @tnzi/ui-admin @tnzi/core naive-ui pinia vue-router
```

```ts
// admin/main.ts (~10 lines)
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

That's it — sign in and you get the full preset menu + 42 pages, a working
🎨 theme drawer in the header, and `v-permission` ready to use.

## Customizing

### Hide built-in modules

```ts
defineAdminApp({ client, hideModules: ['Payment', 'AI'] })
```

Or invert: only show what you whitelist.

```ts
defineAdminApp({ client, showOnlyModules: ['Identity', 'System'] })
```

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

### Custom login page

```ts
import MyLogin from './pages/MyLogin.vue'

defineAdminApp({ client, loginComponent: MyLogin })
```

Or use the bundled `TAdminLoginCard` for a soybean-style production
card with demo-account quick-fill + SMS-code tab:

```vue
<script setup lang="ts">
import { TAdminLoginCard } from '@tnzi/ui-admin/components'
const onLogin = async (payload) => { /* call your auth service */ }
</script>
<template>
  <TAdminLoginCard
    :on-login="onLogin"
    :demo-accounts="[
      { label: 'Super', userName: 'super', password: 'super', description: 'Full access' },
      { label: 'Admin', userName: 'admin', password: 'admin', description: 'Admin role' },
      { label: 'User', userName: 'user', password: 'user', description: 'Read-only' },
    ]"
  />
</template>
```

## Subpath imports

| Subpath | Exports |
|---|---|
| `@tnzi/ui-admin` | `defineAdminApp`, `createTnziUiAdmin`, `useAdminClient`, `vPermission`, `installDirectives`, `fetchAdminManifest` and types |
| `@tnzi/ui-admin/components` | `TAdminLoginCard`, `TIconPicker`, `TJsonEditor` |
| `@tnzi/ui-admin/headless` | `useCrudPage`, `useFormRules`, `useNaiveForm`, `useAdminModuleManifest`, `useColumnSettings`, `usePermissionGuard`, ... |
| `@tnzi/ui-admin/stores` | `useAdminThemeStore`, `useAdminAuthStore`, `useAdminAppStore`, `useAdminRouteStore`, `useAdminTabStore` |
| `@tnzi/ui-admin/router` | `defaultAdminRoutes` (filtered/overridable via `defineAdminApp`) |
| `@tnzi/ui-admin/pages` | All 42 preset pages (also referenced by `defaultAdminRoutes`) |

## v-permission directive

Auto-installed by `createTnziUiAdmin` / `defineAdminApp`.

```vue
<NButton v-permission="'user.delete'">Delete</NButton>
<NButton v-permission="['user.delete', 'user.update']">Edit & Delete</NButton>
<NButton v-permission.any="['admin', 'editor']">Either Role</NButton>
<NButton v-permission.hide="'feature.flag'">Hidden when no permission (visibility instead of display)</NButton>
```

Super-admins (`isSuperUser: true` in the auth store) bypass every check.

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

Five tabs — Appearance (color mode + 12 preset colors + role pickers),
Layout (6 mode cards + sizing sliders + visibility toggles), General (page
transitions), Watermark (text + opacity + auto-username + auto-date),
Preset (copy / download / import JSON config).

## Backend module manifest

The framework reads `GET admin/diagnostics/admin-manifest` to learn which
modules and admin controllers the backend actually exposes:

```ts
import { useAdminModuleManifest } from '@tnzi/ui-admin/headless'
import { useAdminClient } from '@tnzi/ui-admin'

const client = useAdminClient()
const { menuTree, modules, isAvailable } = useAdminModuleManifest({ client })
```

Use this to gate UI on backend feature availability ("only show the AI menu
when the AI module is loaded server-side").

## Testing

```bash
pnpm test              # vitest run — 560 tests
pnpm test:coverage     # vitest --coverage
pnpm typecheck         # vue-tsc --noEmit
```

## CHANGELOG

See [../../CHANGELOG.md](../../CHANGELOG.md) for the full version-by-version
history. Most recent: **0.2.9** (Phase F i18n auto-resolve, 2026-05-15).

See [../../MIGRATION.md](../../MIGRATION.md) for the 0.2.2 → 0.2.9 overview
covering the 7-Phase production overhaul.
