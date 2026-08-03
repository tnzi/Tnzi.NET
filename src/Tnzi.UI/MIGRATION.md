# Migration guide — `@tnzi/*`

This guide collects breaking changes by version range. Read [CHANGELOG.md](./CHANGELOG.md) alongside.

## `@tnzi/*` unreleased — directory naming unified across all five packages

**Breaking: four published subpaths were renamed with no compatibility alias.**

One concept now has one name in every package, so a consumer no longer has to
relearn the layout per package. Nothing moved between packages and no behaviour
changed — only where the code sits and what the subpath is called.

| Old subpath | New subpath | Packages affected |
| --- | --- | --- |
| `@tnzi/ui/composables` | `@tnzi/ui/headless` | `@tnzi/ui` |
| `@tnzi/ui-ai/composables` | `@tnzi/ui-ai/headless` | `@tnzi/ui-ai` |
| `@tnzi/ui-ai/locale`, `/locale/*` | `@tnzi/ui-ai/locales`, `/locales/*` | `@tnzi/ui-ai` |
| `@tnzi/ui-ai/themes`, `/themes/*` | `@tnzi/ui-ai/theme`, `/theme/*` | `@tnzi/ui-ai` |
| `@tnzi/core/composables` | `@tnzi/core/headless` | `@tnzi/core` |
| `@tnzi/mobile/composables` | `@tnzi/mobile/headless` | `@tnzi/mobile` |

`@tnzi/ui-admin` is unaffected — its subpaths were already the target names.

### New subpath

`@tnzi/ui-ai/i18n` now holds the translation engine (`createAiI18n` /
`useAiI18n` / `formatAiMessage`), which used to live inside the locale barrel.
`@tnzi/ui-ai/locales` keeps the dictionaries (`en` / `zhCn`). Splitting them
means a component can reach the translator without pulling a language pack in
with it. Importing either name from the package root still works.

### How to migrate

A find/replace over the table above is the whole migration — the exported
members are unchanged in both name and behaviour:

```diff
- import { useSafeMessage } from '@tnzi/ui/composables'
+ import { useSafeMessage } from '@tnzi/ui/headless'

- import { useChatThreads } from '@tnzi/ui-ai/composables'
+ import { useChatThreads } from '@tnzi/ui-ai/headless'

- import { applyAiTheme } from '@tnzi/ui-ai/themes'
+ import { applyAiTheme } from '@tnzi/ui-ai/theme'

- import { createAiI18n, en } from '@tnzi/ui-ai/locale'
+ import { createAiI18n } from '@tnzi/ui-ai/i18n'
+ import { en } from '@tnzi/ui-ai/locales'
```

No alias was left behind deliberately: keeping both names alive is the thing
this change exists to end.

## `@tnzi/ui-admin` 0.2.70 → unreleased — page consistency overhaul

Custom (non-TCrudPage) pages were rewritten to use the same NCard chrome, theme tokens, and message-helper pattern that TCrudPage pages enforce. **Pure cleanup — no public API additions.** Two opt-in points exist if you extend custom pages:

### New shared helper

`useSafeMessage` is now the canonical way to consume `useMessage()` in a way that doesn't crash when no `NMessageProvider` ancestor is mounted:

```ts
import { useSafeMessage } from '@tnzi/ui/headless'

const message = useSafeMessage()
message.success('saved')  // works whether or not NMessageProvider is installed
```

The previous in-file try/catch pattern still works — it's just no longer recommended.

### Token migration (mandatory for custom pages)

If you authored a page that referenced any of the following tokens, it was silently falling back to hardcoded `#06B6D4` (cyan) and ignoring user theme color choices. Update to the canonical names:

| Old (broken) | New (canonical) |
| --- | --- |
| `--tnzi-primary-color` | `--tnzi-primary` |
| `--tnzi-primary-color-suppl` | `rgb(var(--tnzi-primary-rgb) / 0.XX)` |
| `--tnzi-base-border` | `--tnzi-border` |
| `--tnzi-base-fill` | `--tnzi-layout-bg` |
| `--tnzi-success-color` / `-warning-color` / `-error-color` | `--tnzi-success` / `-warning` / `-error` |
| `--tnzi-font-family-mono` | `ui-monospace, SFMono-Regular, Menlo, Consolas, monospace` |
| `--t-*` (border / surface / muted / danger) | canonical `--tnzi-*` equivalents |

`__tests__/pages/token-consistency.test.ts` enforces this with a static regex check so the banned tokens can't creep back into the codebase.

### Page padding contract

Custom pages **must NOT** apply `padding: 16px` (or any equivalent) to their outer wrapper. `TAdminContent` is the single source of truth for the page-edge gutter (`var(--tnzi-admin-content-padding, 16px)`). Custom pages that added their own padding rendered at 32px (double) and looked out-dented relative to TCrudPage pages — fixed across 9 pages in this batch.

```css
/* ✗ Don't */
.t-my-page { padding: 16px; }

/* ✓ Do — let TAdminContent own the gutter */
.t-my-page {
  /* layout / flex / grid only; no padding */
}
```

## `@tnzi/ui-admin` 0.2.70 → unreleased — responsive overhaul (375px-friendly)

The responsive batch is **additive at desktop widths** — applications that only run on `lg+` viewports see no behavioural change. Below `lg` the chrome rearranges (overflow menus, fullscreen modals, stacked footers) so consumers that previously worked around the layout breakage may want to remove their own overrides.

### No code changes required for these consumers

- Apps that mount only on desktops (≥ 1024px) — the responsive code paths never trigger.
- Apps that already rely on the auto-collapse mobile drawer.
- Apps that don't customise the dashboard / login / theme drawer widths.

### Opt-outs (when you want the old behaviour back)

| Behaviour | Old default | New default | Opt-out |
| --------- | ----------- | ----------- | ------- |
| `TFormModal` fullscreen on small viewports | Never (fixed 560px, would overflow) | Auto-fullscreen when viewport < `max(width+32, 640)` | `<TFormModal :fullscreen="false" />` |
| `TAdminHeader` overflow menu | Never (inline row, would clip) | `···` dropdown below 640px | `<TAdminHeader overflow-menu-breakpoint="never" />` |
| `TAdminHeader` fullscreen button | Always visible | Hidden on touch devices + when Fullscreen API unsupported | No direct opt-in — a non-functional button on touch is intentionally suppressed. |
| `TAdminShell` mobile drawer ESC dismiss | No-op | ESC closes the drawer | None — this is a pure UX addition. |
| Tablet sider auto-collapse | Stays open (220px eats 28% of iPad portrait) | Auto-collapses to icon rail when entering `tablet` band | Call `appStore.setSiderCollapse(false)` after mount if you really want it open. |
| `TDashboardPage` chart row | Fixed 2:1 split at all widths | Stacks vertically below `lg` | Build a custom layout instead of using the prop-driven scaffold. |
| `TCrudPage` advanced-search grid | 1/2/4 cols (jumped 2→4 at md) | 1/2/3/4 cols across xs/sm/md/l | None — the new ladder is strictly more granular. |
| `TCrudPage` mobile pagination | NPagination full with size picker | `simple` mode + no size picker below 640 | None — full controls would overflow phone widths. |
| Touch hit-area (44×44 / 36×36) | Inherited 36×36 desktop sizes | Auto-promotes on `(pointer: coarse)` | Override `.t-admin-header__icon-btn { min-width: 36px; min-height: 36px; }` in your own stylesheet to keep 36×36 on touch. |

### New optional APIs

```vue
<!-- Force-disable auto fullscreen (legacy 560px modal at every viewport): -->
<TFormModal :state="modal" title="Edit" :fullscreen="false" />

<!-- Choose a different overflow-menu breakpoint: -->
<TAdminHeader overflow-menu-breakpoint="sm" />     <!-- folds below 768 -->
<TAdminHeader overflow-menu-breakpoint="never" />  <!-- always inline -->
```

### New headless

`useBreakpoint()` is now exported from `@tnzi/ui-admin/headless`:

```ts
import { useBreakpoint } from '@tnzi/ui-admin'

const bp = useBreakpoint()
// bp.isXs / isSm / isMd / isLg / isDesktop / isTouch
// bp.width.value / bp.height.value
```

Use it in your own pages to mirror the same breakpoint scale the admin shell consumes.

## `@tnzi/ui-admin` 0.2.58 → 0.2.59 — Phase G layout cards + tab overhaul

### Breaking — `TabStyle` regained `'slider'`

```diff
- export type TabStyle = 'chrome' | 'button'
+ export type TabStyle = 'chrome' | 'button' | 'slider'
```

If you narrowed your own type alias to the 2-member union you'll need
to widen it. `setTabStyle('slider')` now works.

### Breaking — `TLayoutModeCard` internal class names

If you targeted `TLayoutModeCard`'s internal classes from CSS, the
class set changed. Old: `__top-menu`, `__sub-sider`, `__content-line`,
`__sider--rail`. New: `__sider--primary`, `__sider--tertiary`,
`__sider--w8/--w16/--w18`, `__header--primary`, `__header--secondary`,
`__header--tertiary`, `__main`. The card's outer `t-layout-card`
class + `t-layout-card--active` modifier are unchanged.

### Behavioural — `useAdminTabStore` close ops skip pinned tabs

`removeTab(id)` returns `null` (no-op) for pinned + home tabs.
`removeLeftTabs / removeRightTabs / removeOtherTabs / clearAllTabs`
all keep pinned tabs. If you have tests that asserted "close all
clears the array completely", they need to either also `unfixTab`
beforehand or assert against `tabStore.tabs.filter(t => !isTabPinned(t.id))`.

### Behavioural — middle-click close now reads from theme store

```ts
// Before: TAdminTabs.props.closeByMiddleClick defaulted true
<TAdminTabs />  // closes on middle click

// After: defaults to themeStore.closeTabByMiddleClick (default false)
<TAdminTabs />  // does NOT close on middle click unless user enables it
                // in Theme Drawer → Layout → Close tab on middle click
<TAdminTabs :close-by-middle-click="true" />  // explicit override still works
```

### New stable APIs

`useAdminTabStore`:
- `fixedTabIds: Ref<string[]>`
- `isTabPinned(id) / isTabRetain(id) / fixTab(id) / unfixTab(id)`

`useAdminThemeStore`:
- `closeTabByMiddleClick: Ref<boolean>` + `setCloseTabByMiddleClick`

`@tnzi/ui-admin/components` exports `TChromeTabBg.vue` (port of
soybean's chrome SVG arc background, useful if you're building
custom chrome-styled tabs).

`AdminThemeSnapshotV1.closeTabByMiddleClick?: boolean`.

## `@tnzi/ui-admin` 0.2.56 → 0.2.57 — Phase E hybrid layouts

### Non-breaking improvements

- The 4 hybrid layout modes finally render distinct menu structures.
  If you've been using only `vertical` or `horizontal` you won't
  notice anything.
- `vertical-mix` drawer no longer ships empty when the user lands
  on a 1st level leaf route.
- `TAdminSidebar` and `TAdminTopMenu` accept a new `:items` prop
  for callers that want to override the menu source. The default
  (omit it) reads from `useAdminRouteStore().menus`.

### New composable

`useAdminMenuContext({ menus, routeName, autoSelectFirstWith? })`
exposes layered menu slices + active-key state. If you build custom
hybrid layouts in your app, prefer this over walking the menu tree
manually:

```ts
import { useAdminMenuContext } from '@tnzi/ui-admin/headless'
const ctx = useAdminMenuContext({
  menus: computed(() => routeStore.menus),
  routeName: computed(() => useRoute().name as string),
})
// ctx.firstLevelMenus / secondLevelMenus / childLevelMenus
// ctx.activeFirstLevelMenuKey / activeSecondLevelMenuKey
// ctx.isActiveFirstLevelMenuHasChildren
// ctx.handleSelectFirstLevelMenu(key) / handleSelectSecondLevelMenu(key)
```

## `@tnzi/ui-admin` 0.2.54 → 0.2.55 — Phase C tab style + header guard

### Breaking — `TabStyle` narrowed to `'chrome' | 'button'`

```diff
- export type TabStyle = 'chrome' | 'button' | 'slider'
+ export type TabStyle = 'chrome' | 'button'
```

`useAdminThemeStore.setTabStyle('slider')` now silently rejects
(validation gate at `VALID_TAB_STYLES`); existing persisted state
containing `tabStyle: 'slider'` reads back as the default `'chrome'`.
The slider style was visually indistinguishable from chrome — drop
the option from any consumer setting UI.

### Non-breaking improvements

- Button-mode tabs actually paint primary bg + white text now.
- "Show header" toggle in the theme drawer self-disables in
  horizontal/hybrid layouts (where the header hosts the menu).
- The shell ignores a `headerVisible: false` value when the layout
  needs the header — no more "I hid the header and lost all
  navigation" foot-gun.

## `@tnzi/ui-admin` 0.2.52 → 0.2.53 — Phase A 救命修复

### Breaking — `invertHeader` removed from `useAdminThemeStore`

The setting + setter + persistence pick are gone:

```diff
- themeStore.invertHeader            // ❌ removed
- themeStore.toggleInvertHeader()    // ❌ removed
- localStorage 'tnzi-admin-theme' .invertHeader  // ❌ no longer persisted
```

If you reference these from consumer code, just delete the calls — the
header now always follows the global light/dark mode (matching
soybean). Snapshot JSON written by 0.2.52 that contains
`admin.invertHeader: false` will load fine in 0.2.53 (the field is
silently ignored). Snapshot JSON written by 0.2.52 with
`admin.invertHeader: true` will load with the header in light/dark
mode following the global theme — visually closer to what soybean ships
than the previous broken behaviour.

`AdminThemeSnapshotV1` type lost the `admin.invertHeader: boolean`
member. If you have a custom snapshot importer, drop the field.

### Non-breaking improvements (no consumer action needed)

- Every `<TCrudPage>` row's edit/view button now actually opens the
  modal (was silently broken since the page introduced reactive list
  state). No code change in your pages — the fix is internal to
  `useFormModal`.
- Route transitions now actually animate. If you previously worked
  around the missing animation by adding your own `<Transition>` on
  top of `<router-view>`, you can remove that wrapper.
- Content area background is no longer cold-purple. If you overrode
  `--tnzi-admin-content-bg` to compensate, you can remove the
  override.

## `@tnzi/ui-admin` 0.2.23 → 0.2.24 (Phase I.7.1: TLoginPage router-param rewrite)

**Breaking — `TLoginPage` prop shape and route path both changed.** The
component is now a router-param driven shell that mirrors
`soybean-admin-example/views/_builtin/login/index.vue`. The old
`centered` / `split` variant toggle was a design dead-end and has been
removed entirely; the new single layout is what soybean has always
shipped.

### What broke

- `<TLoginPage variant="centered|split" />` — the `variant` prop is
  gone. The component now has only one layout (soybean centered card on
  brand-tinted background with a `TWaveBg` underneath).
- `<TLoginPage cardTitle cardSubtitle tagline />` — these single-shot
  text props are gone. The page now renders 5 modules (pwd-login,
  code-login, register, reset-pwd, bind-wechat) each with its own
  title; override per-module titles via the `moduleLabels` prop.
- `<TLoginPage onLogin demoAccounts enableCodeLogin defaultUserName
  defaultPassword />` — gone. The shell no longer owns the form; each
  module owns its own form and pulls callbacks from
  `useLoginContext()`. Phase I.7.2+ will wire these.
- `<TLoginPage translate="…" />` — the function signature changed from
  `(key) => string` to `(key, fallback?) => string`. If your translate
  function ignores extra args it'll keep working; otherwise wrap it:
  `translate={(k, f) => myT(k) ?? f ?? k}`.
- `import { TLoginPageVariant }` — type export removed.
- `defaultAdminRoutes` — the `/login` entry now has the path
  `/login/:module(pwd-login|code-login|register|reset-pwd|bind-wechat)?`.
  Consumers matching by literal `'/login'` should match by `name ===
  'login'` instead.

### What you gain

- Built-in admin login page (no need to write your own route component
  for the common case). Consumers that pass `loginComponent` to
  `defineAdminApp()` are unaffected.
- 5 module slots — `moduleComponents` prop on `TLoginPage` is a
  `Record<LoginModule, Component>` so you can replace any module
  individually. The 5 built-in modules are exported from
  `@tnzi/ui-admin/pages/login/modules/*` (also barreled at top level).
- `useLoginContext()` composable — modules call `translate`,
  `toggleLoginModule(name)`, and the consumer-supplied auth
  `callbacks` (Promise-returning bag).

### Recipe — minimal upgrade

If you were previously rendering a single-card login with an `onLogin`
callback:

```ts
// Before (0.2.23)
<TLoginPage variant="centered" :on-login="login" :demo-accounts="[…]" />

// After (0.2.24) — wrap with the route component and pass callbacks
import { TnziAdminLoginPage } from '@tnzi/ui-admin'
// register `TnziAdminLoginPage` at `/login/:module(…)?` (defaultAdminRoutes does this)
// Then wire callbacks in your shell by overriding the route:
defineAdminApp({
  client,
  loginComponent: MyLoginShell, // see Phase I.7.2 wire-up
})
```

Phase I.7.2 (next commit) will ship a real wired-up reference example
for consumers, including how to pass `callbacks.pwdLogin` to the page.

## `@tnzi/ui-admin` 0.2.2 → 0.2.9 (7 Phase production overhaul)

Patch-grade bumps across `0.2.3` through `0.2.9` collectively reshape the
admin lib from "42-page scaffold" to "production-grade admin framework"
benchmarked against soybean-admin. None of the individual patches break
existing consumers, but the overall feature surface is materially larger:

### What's new — opt-in, no migration required

- **`defineAdminApp({ client, hideModules, showOnlyModules, overridePages,
  addModules, loginComponent, forbiddenComponent })` factory** (0.2.4) —
  shrinks consumer `admin/main.ts` boilerplate from ~110 lines to ~50.
- **Backend module manifest** at `GET admin/diagnostics/admin-manifest`
  (0.2.4) — frontend can call `useAdminModuleManifest()` to learn which
  admin entities the backend actually exposes.
- **`v-permission` directive** (0.2.6) — hide / remove DOM elements based
  on user permissions. Auto-installed by `createTnziUiAdmin`.
- **`TAdminLoginCard` component** (0.2.6) — soybean-style production login
  card with pwd / SMS-code tabs, demo-account quick-fill cards, and
  third-party login slot.
- **`useFormRules(translate?)` + `useNaiveForm()` composables** (0.2.7) —
  i18n-reactive Naive UI form validation rules (required / text / userName
  / email / phone / password / matches / url / json / integer) plus an
  ergonomic NForm wrapper.
- **`TIconPicker` + `TJsonEditor` components** (0.2.7) — searchable
  Iconify icon picker (80 curated MDI icons by default) and a textarea-
  based JSON editor with format/minify/validate. Both bundle-light by
  design.
- **6 layout modes** in `useAdminThemeStore.layoutMode` (0.2.3) — vertical
  / horizontal / vertical-mix / 3 hybrid variants. Driven by `TAdminShell`
  dispatch.
- **5-tab Theme Settings Drawer** (0.2.3) — Appearance / Layout / General
  / Watermark / Preset. Auto-mounted by `AdminShellRoot` and wired to the
  header 🎨 button.
- **4 new admin pages scaffolded** (0.2.8) — identity/organizations,
  identity/sessions, ai/threads, system/features.

### Behavioral changes worth knowing

- **CSS variable names aligned to `@tnzi/ui`** (0.2.5) — custom CSS
  referencing `--tnzi-primary-color` / `--tnzi-text-color-1` /
  `--tnzi-border-color` etc. should switch to `--tnzi-primary` /
  `--tnzi-base-text` / `--tnzi-border`. Old names never existed in
  `@tnzi/ui`'s actual injection set; they silently fell through to CSS
  fallbacks.
- **Header icons are now Iconify SVG** (0.2.5) — emoji buttons replaced
  with `mdi:*` icons. CSS class names preserved for backward-compat.
- **`createTnziUiAdmin` auto-provides a `@tnzi/ui` theme context** when
  the consumer hasn't installed `createTnziUi()`. Before this fix
  (`7bf20274`), `TThemeDrawer` would throw during setup and the whole
  `/admin` subtree would silently fail to render.
- **Menu titles auto-resolve dotted i18n keys** (0.2.9) — `meta.title`
  values starting with `admin.` / `tnzi.admin.` are now looked up in the
  bundled locale pack.

### Required deps changes

- **`@iconify/vue@^5.0.0`** added as a runtime dependency (0.2.5).

### Tests

488 → 560 tests (+72 across the 7 Phases). No regressions.

---

## `@tnzi/*` 0.1 → 0.2

This guide walks a consumer of `@tnzi/core`, `@tnzi/ui`, `@tnzi/ui-admin`, `@tnzi/ui-ai`, or `@tnzi/mobile` through the breaking changes introduced in the 0.2.0-preview.1 release. Read [CHANGELOG.md](./CHANGELOG.md) alongside this guide.

## Scope

0.2.0-preview.1 is the first major-version pre-release. The changes below are **breaking** — code that imports from 0.1.x will not work unchanged against 0.2.x. Patch-level changes (bug fixes, docs, tests) are listed in the CHANGELOG but don't require migration steps.

## Upgrade checklist

- [ ] Update `package.json` dependency ranges to `"@tnzi/core": "^0.2.0-preview.1"` (etc.)
- [ ] Run `pnpm install` (or the equivalent)
- [ ] Follow the sections below that apply to your consumer

---

## 1. `@tnzi/core` — service factories are now per-call

**Before (0.1):**

```ts
import { useProfileApi } from '@tnzi/core/services/identity'
const api = useProfileApi(client) // cached singleton
```

**After (0.2):** every call to a service factory returns a fresh API object. Bridges must call the factory inside each sub-contract method, not once at module scope.

```ts
// Bridge pattern
export function createIdentityBridge(client: HttpClient) {
  return {
    users: {
      async fetch(query: PagedQuery) {
        const api = useAdminUserApi(client)  // per-call
        return api.getPaged(query)
      },
      // ... other methods likewise
    },
  }
}
```

See `packages/ui-admin/src/services/bridges/identity-bridge.ts` for the canonical example.

---

## 2. `@tnzi/ui-admin` — new `createTnziUiAdmin` preset

**Before (0.1):** consumers manually wired routing, stores, plugins, and the admin shell.

**After (0.2):** use the preset factory.

```ts
// main.ts
import { createApp } from 'vue'
import { createTnziUiAdmin } from '@tnzi/ui-admin'
import { httpClient } from './api'
import App from './App.vue'

const app = createApp(App)
const admin = createTnziUiAdmin({
  client: httpClient,  // MUST inject — bridges need a client
  // optional: routes, plugins, locale, theme overrides
})
app.use(admin)
app.mount('#app')
```

This single call registers:

- `TAdminShell`, `TCrudPage`, `TFormModal`, `TListShell`, `TRowActions`（`TCrudToolbar` / `TBatchActions` 后来被内联进 `TListShell`，已不再单独存在）
- 预置页面 via `defaultAdminRoutes`（数量随版本增长，以 `src/router/routes.ts` 为准，当前 102 个懒加载路由组件）
- Pinia stores（`useAdminAppStore`、`useAdminAuthStore`、`useAdminRouteStore`、`useAdminTabStore`、`useAdminThemeStore`、`useAdminBreadcrumbStore`）
- i18n messages (en + zh-cn) under the `tnzi.admin.*` namespace
- Theme preset with `--tnzi-admin-*` CSS variables inheriting from `@tnzi/ui`'s palette

---

## 3. `@tnzi/ui-ai` — chat + workflow editor only

The `admin/` subdirectory has been removed. If you were importing `@tnzi/ui-ai/admin/{AgentManagement,SkillManagement,...}`, migrate to `@tnzi/ui-admin`:

```ts
// Before
import AgentManagement from '@tnzi/ui-ai/admin/AgentManagement.vue'

// After — 这些页面由 `defaultAdminRoutes` 自动挂载，无需手动 import。
// 只在需要替换/包装某一页时，用 `defineAdminApp` 的路由覆盖能力接管对应 route name。
```

The `@tnzi/ui-ai` public surface now consists of:

- `chat/` — **`TChatApp`** (drop-in Manus-style chat application shell; the recommended entry point as of 0.2.1)
- `components/{agent,artifact,chat,context,knowledge,reasoning,skill,streaming,workflow}` — fine-grained building blocks (62 SFCs)
- `shell/` — reusable chrome (`TCollapsibleSidebar`, `TCommandPalette`, `TSettingsDialog`, `TLandingPage`) for custom layouts when `TChatApp` is too prescriptive
- `composables/` — 13 headless composables (`useChat`, `useAgentExecution`, `useAutoScroll`, `useEmbedMode`, `useLocalSearch`, `useMessageBranch`, `useMessageGroup`, `useRagChat`, `useSidebarState`, `useSkillBrowser`, `useStreamMarkdown`, `useTokenCounter`, `useWorkflowVisualization`)
- `embed/` — embed widget
- `lib/` — `formatCompactNumber`, `cn` (deprecated — use `:class` bindings)

### 0.2.x — `ChatLayout` → `TChatApp` (breaking)

`@tnzi/ui-ai` 0.2.1 hard-removes the legacy `ChatLayout` family (`ChatLayout`,
`ChatSidebar`, `ChatMain`, `ChatHeader`, `ChatArtifactPanel`, `ChatSettings`).
All chat product code must move to `TChatApp`.

```diff
- import { ChatLayout } from '@tnzi/ui-ai'
+ import { TChatApp } from '@tnzi/ui-ai/chat'

  <ChatLayout
-   :threads="..."
-   :active-thread-id="..."
-   :messages="..."
-   :is-streaming="..."
-   :input-text="..."
-   :agent-name="..."
-   @new-chat
-   @select-thread
-   @send
-   @stop
- />
+ <TChatApp
+   brand-name="..."          <!-- new: sidebar brand wordmark -->
+   :threads="..."
+   :active-thread-id="..."
+   :messages="..."
+   :is-streaming="..."
+   v-model:input-text="..."  <!-- now two-way bound -->
+   agent-name="..."
+   agent-label="..."         <!-- new: small tag rendered after agent name -->
+   landing-greeting="..."    <!-- new: serif headline on empty state -->
+   :landing-chips="..."      <!-- new: suggestion chips -->
+   @new-chat
+   @select-thread
+   @send
+   @stop
+ />
```

**Slot mapping** (old → new):

| Old (`ChatLayout`) | New (`TChatApp`) |
|---|---|
| `#sidebar-header` | `#brand` |
| `#sidebar-footer` | `#sidebar-footer` |
| `#header-extra` | `#topbar-actions` |
| `#input-above` | `#thread-composer-left` / `#thread-composer-right` |
| _(no equivalent)_ | `#sidebar-content`, `#sidebar-nav`, `#rail` |
| _(no equivalent)_ | `#landing-plan`, `#landing-headline`, `#landing-subline`, `#landing-chips`, `#composer-left`, `#composer-right` |
| _(no equivalent)_ | `#settings-account`, `#settings-appearance`, `#settings-about`, `#settings-{customId}` |

**New capabilities** that consumers no longer need to build manually:

- Three-mode collapsible sidebar (expanded / icon-rail / hidden + mobile drawer)
- Settings dialog (Account / Appearance / About defaults; extensible via `settingsSections`)
- Command palette (Cmd+K, opt-in via `enable-command-palette`)
- Landing empty state with serif headline, suggestion chips, composer
- Auto theme application (`autoApplyTheme` defaults to `true` → calls `applyAiTheme()` on mount and theme change)
- Top-bar with workspace title + actions slot
- Stop button automatically appears in composer when `is-streaming=true`

If `TChatApp`'s shell is too prescriptive (embedded chat panel, customer-support
widget, kiosk mode, etc.), compose the region frames from
`@tnzi/ui-ai/components` (`TCollapsibleSidebar`, `TLandingPage`,
`TSettingsDialog`, `TCommandPalette`) together with the `components/chat/*`
thread primitives directly.

> These four used to live behind a `@tnzi/ui-ai/shell` subpath. That subpath was
> removed on 2026-08-02: its admission rule ("frames a region of an app shell")
> could not be told apart from `components/layout`'s ("frames a screen"), which
> is how `TSettingRow` / `TSettingGroup` ended up in one and `TSettingsDialog`
> in the other. Nothing outside the package imported it.

---

## 4. Theme tokens — CSS variables instead of Tailwind `dark:`

`@tnzi/ui-ai` no longer uses Tailwind's `dark:` variant or the `cn()` utility. Token access is via CSS variables that inherit from `@tnzi/ui`:

**Before (0.1):**

```vue
<div class="bg-white dark:bg-gray-900 text-black dark:text-white">...</div>
```

**After (0.2):**

```vue
<div class="bg-[--tnzi-ai-surface] text-[--tnzi-ai-text]">...</div>
```

> ⚠️ 早期版本的本节曾建议「用 Tailwind 的 theme extension 消费这些变量」。**该建议已作废**：
> Tailwind 于 2026-04 从整个 monorepo 移除，原子类引擎统一为 **UnoCSS**，且各包 `CLAUDE.md`
> 明令禁止重新引入 `tailwindcss` / `postcss.config.js`。

若消费方也用 UnoCSS，把变量映射进 `theme.colors` 即可（与 `@tnzi/ui-ai` 自身 `uno.config.ts` 同一写法）：

```ts
// uno.config.ts (consumer)
import { defineConfig, presetWind4 } from 'unocss'

export default defineConfig({
  presets: [presetWind4({ preflights: { reset: false } })],
  theme: {
    colors: {
      'tnzi-ai': {
        surface: 'var(--tnzi-ai-surface)',
        text: 'var(--tnzi-ai-text)',
        // ...
      },
    },
  },
})
```

---

## 5. Stores — factory pattern + reset helpers

**Before (0.1):**

```ts
import { useAuthStore } from '@tnzi/ui/stores/auth'
// Direct usage — singleton auth-state manager hidden inside
const auth = useAuthStore()
```

**After (0.2):** the store pattern is unchanged for consumers, but the internal manager is now a lazy singleton that can be reset for SSR / test isolation.

```ts
import { useAuthStore, resetAuthRuntime } from '@tnzi/ui/stores/auth'

// In tests:
beforeEach(() => {
  resetAuthRuntime()      // fresh manager per test
  setActivePinia(createPinia())
})
```

Similarly `resetStoreRuntime()` from `@tnzi/ui/stores` resets the shared HTTP client + storage adapter injected by `initStoreRuntime()`.

---

## 6. HTTP client — 401 auto-refresh, GET deduplication

The `HttpClient` in `@tnzi/core` gained two opt-in behaviors:

- **`refreshTokenFn`** — when set, 401 responses trigger a single refresh attempt (mutex-guarded) and the original request retries once. Configure it alongside `notifyUnauthorized` in your client construction.
- **`deduplicateGets: true`** (default) — concurrent GET requests to the same URL share a single in-flight promise (with shallow copy on return to prevent mutation). Disable via `createHttpClient({ deduplicateGets: false })` if your app depends on per-call fresh fetches.

No action required unless you were relying on duplicate GETs or stock 401 behavior.

---

## 7. DTO shapes — canonical-compact rule

Phase 5 applied the "canonical compact" rule: page configs follow real backend DTO shapes, not plan sketches. If your code consumed DTO fields that only existed in the plan (not the backend), TypeScript will now fail at build time. Check `packages/core/src/services/*/dtos.ts` for the canonical shape.

Common renames:

| Old (plan-only)      | New (backend) |
|----------------------|---------------|
| `AgentPersonaDto.avatarUrl`    | *(removed — not in DTO)* |
| `AgentPersonaDto.isEnabled`    | *(removed)* |
| `AgentPersonaDto.content`      | system-prompt body (unchanged) |

---

## 8. Test helpers — `resetAuthRuntime` / `resetStoreRuntime`

If you had tests mocking `@tnzi/core/state.AuthStateManager`, make sure each test resets the runtime:

```ts
import { resetAuthRuntime } from '@tnzi/ui/stores/auth'

beforeEach(() => {
  resetAuthRuntime()  // wipes module-level singleton
  setActivePinia(createPinia())
})
```

---

## 9. Coverage thresholds (if your CI mirrors ours)

Phase 6 coverage thresholds (per package `vitest.config.ts`):

| Package          | Lines | Statements | Branches | Functions |
|------------------|-------|------------|----------|-----------|
| `@tnzi/ui`       | 80%   | 80%        | 70%      | 80%       |
| `@tnzi/ui-admin` | 80%   | 80%        | 70%      | 60%       |
| `@tnzi/ui-ai`    | 80%   | 80%        | 70%      | 60%       |

The lowered ui-admin / ui-ai function thresholds reflect the fact that mount-based integration tests under happy-dom can't reach 80% without full user-flow simulation. At the time this was written those flows were covered by Playwright specs in `e2e/`; **that suite was deleted on 2026-08-01 along with the package playgrounds it ran against**, so those flows now have no automated coverage at all — see `docs/frontend/architecture.md` §4.2. The current ui-admin thresholds are also a ratchet rather than the 80/70 targets quoted here.

---

## 10. Rolling back

If 0.2.0-preview.1 blocks your release and you need to roll back:

```bash
pnpm add @tnzi/core@^0.1.2 @tnzi/ui@^0.1.1 @tnzi/ui-admin@^0.1.0 @tnzi/ui-ai@^0.1.0 @tnzi/mobile@^0.1.0
```

Then revert any code changes from sections 1–8 above.

---

## Questions

File an issue at `https://github.com/tnzi/tnzi.net/issues` or consult the design spec at `.superpowers/specs/2026-04-12-ui-packages-production-readiness-design.md` for architectural context.
