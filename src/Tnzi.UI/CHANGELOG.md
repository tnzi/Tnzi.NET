# Changelog

All notable changes to the `@tnzi/*` frontend packages are documented here. The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- `@tnzi/ui-admin`: `TListShell` composable list shell + `TCardPage`/`TCardRenderer` (card lists) + `TTableRenderer`. `page`/`container` height modes for embedding lists as page sections. `useCrudPage` write callbacks are now optional (pure-display lists need only `fetchData`; create/edit/delete affordances auto-hide).

### `@tnzi/ui-admin` page-consistency overhaul

Follow-up to the responsive batch — user reported that custom pages drift from the standard NCard / token vocabulary that TCrudPage pages enforce: "用量统计页面没有白色背景容器 / 评测页面的容器似乎不是同一风格,外边距偏大". Comprehensive audit found 15 custom (non-TCrudPage) pages with five categories of drift; this commit set unifies all five.

**Fixed — `@tnzi/ui-admin` P0 visual breakage**

- `UsageDashboard` rewritten in `TDashboardPage` style: filter NCard + gradient KPI tiles + chart NCards. Was previously a flat `<div>+<section>` page on the grey layout background with `#fafafa` stat cells and raw HTML inputs that ignored the theme entirely.
- `EvalViewer` outer `padding: 16px` removed — it duplicated `TAdminContent`'s own 16px so the page sat at 32px from the viewport edge (user-reported "外边距偏大"). Modal NModal `style="width: 560px"` swapped for `TFormModal` so it auto-fullscreens on phones.

**Fixed — `@tnzi/ui-admin` systematic token migration (12 files)**

Previous pages referenced tokens that didn't exist in `@tnzi/ui/styles/variables.css` and fell back to hardcoded `#06B6D4` cyan — they never followed the user's chosen primary color. Replaced:

| Old (broken) | New (canonical) |
| --- | --- |
| `--tnzi-primary-color` | `--tnzi-primary` |
| `--tnzi-primary-color-suppl, rgba(6,182,212,0.0X)` | `rgb(var(--tnzi-primary-rgb) / 0.0X)` |
| `--tnzi-base-border` | `--tnzi-border` |
| `--tnzi-base-fill` | `--tnzi-layout-bg` |
| `--tnzi-success-color` / `-warning-color` / `-error-color` | `--tnzi-success` / `-warning` / `-error` |
| `--tnzi-font-family-mono` | `ui-monospace, SFMono-Regular, Menlo, Consolas, monospace` (no token shipped) |
| `--t-border` / `--t-surface` / `--t-muted` / `--t-danger` | canonical equivalents |

Files touched: AgentDetail, RunMonitor, RunViewer, EvalViewer, EntityRole, Permission, RoleFunction, OrganizationManagement, TAuditTimeline, WorkflowEditor, StorageFile, TChartPanel.

**Refactored — `useSafeMessage` helper (10 pages)**

Previously 10 pages copy-pasted the same 5-line block to defend against missing `NMessageProvider`:

```ts
let message: { success(...): void; error(...): void }
try { message = useMessage() } catch { message = { success: () => {}, error: () => {} } }
```

Centralised into `src/pages/_shared/safeMessage.ts` returning the full naive-ui `MessageApi` shape with all methods as noop when no provider is mounted. Pages now do `const message = useSafeMessage()`.

**Fixed — double padding removed across 9 pages**

`TAdminContent` already applies `var(--tnzi-admin-content-padding, 16px)` to its root. Pages that added their own `.t-xx-page { padding: 16px; }` rendered at 32px from the viewport — visibly out-dented relative to TCrudPage pages. Removed from: AgentDetail, EvalViewer, RunViewer, EntityRole, Permission, RoleFunction, OrganizationManagement, SessionManagement, StorageFile.

**Refactored — NCard wrap for detail panels**

- `AgentDetail` 4-quadrant `<section>` blocks → `<NCard size="small" :bordered="false">` with box-shadow + radius parity with TCrudPage list-card.
- `RunMonitor` detail `<section>` → `<NCard>` + cancel button switched from raw `<button>` to `<NButton type="error" ghost>`.

**Polished**

- `OrganizationManagement` + `StorageFile` modals: hardcoded `style="width: 500px"` / `"460px"` → computed style binding that caps at `min(propWidth, 95vw)` and switches to 100vw below sm.
- `RunMonitor` + `WorkflowEditor`: `rem` units → `px` (project convention).

**Tests**

- New `__tests__/pages/safeMessage.test.ts` — verifies API shape + noop safety (2 tests).
- New `__tests__/pages/token-consistency.test.ts` — static regex check across all page .vue files prevents the banned tokens from creeping back (14 tests, one per banned token).
- Updated `__tests__/integration/UsageDashboard.test.ts` — mocks `@tnzi/ui` `useTheme` because the rewrite now wraps `TDashboardPage` → `useEcharts`.
- Suite now: **703 pass** (687 → 703, no regression).

### `@tnzi/ui-admin` responsive overhaul (375px-friendly)

User-driven full audit of cross-resolution behaviour. Found 20 issues across the shell, layout chrome, CRUD widgets, dashboard scaffold, and login page; six broke layout outright on phones / iPad portrait, the rest left UX gaps below `lg`. All fixed in one batch.

**Added — `@tnzi/ui-admin`**

- `useBreakpoint` headless: `xs/sm/md/lg/desktop` band refs + `isTouch` probe + reactive `width/height`. Wraps `@vueuse/core` with Tailwind breakpoints and adds touch detection via `matchMedia('(pointer: coarse)')`. Used by every responsive fix below so future components don't reinvent the wiring.

**Fixed — `@tnzi/ui-admin` P0 breakage**

- `TFormModal` width auto-caps at `min(propWidth, 95vw)`; auto-switches to fullscreen layout on viewports narrower than `max(width+32, 640)`. New `fullscreen` prop overrides auto-detect.
- `TGlobalSearch` width caps at `min(630, 95vw)`; fullscreens below 660px.
- `TThemeDrawer` width steps `420/360/100vw` across desktop/sm/xs; preset + layout grids drop to 2 cols on xs so the 3×96 layout cards stop overflowing the drawer.
- `TDashboardPage` swaps fixed `:cols` for `responsive="screen"` + `item-responsive`; KPI ladder picks 1/2/4 (4 cards), 1/3 (3 cards), 1/2 (2 cards) across xs/sm/md+; chart row stacks below `lg` so the pie isn't squashed into ⅓ of a phone screen.
- `TAdminShell` mobile drawer width clamps to `viewport-gutter` (iPhone SE-friendly); ESC dismisses the open drawer; the drawer now carries `role="dialog"` + `aria-modal`.
- `TCrudPage` simple-search row stacks vertically below 640px so the input no longer fights with the Search / Advanced buttons for the same line.

**Fixed — `@tnzi/ui-admin` P1 UX gaps**

- `TAdminHeader` collapses the right-side action buttons into a single `···` dropdown below 640px (configurable via `overflowMenuBreakpoint`); fullscreen button hides when the Fullscreen API is unsupported OR the device has a coarse pointer.
- `TAdminTabs` tab title capped 220px desktop / 140px mobile with ellipsis + native title tooltip; touch long-press (500ms) opens the same context menu as right-click; close button hit area grows to 24×24 on coarse pointers.
- `TCrudPage` pagination switches to NPagination simple mode + drops size picker below `sm`; footer stacks batch actions + pagination on mobile; list-card action toolbar becomes a horizontal scroll strip (instead of wrapping into a 2-row card header).
- `THeaderBanner` greeting / subtitle / time font sizes step down below 640.
- `TLoginPage` card width steps `400/360/min(340,calc(100vw-64px))` across `sm/xs` so iPhone 14 (390px) no longer falls into the old 400 → 300 cliff.

**Fixed — `@tnzi/ui-admin` P2 polish**

- `variables.css`: stepped responsive tokens — header / tab heights + content padding shrink at 480/767/1023.
- `polish.css`: `pointer: coarse` media query bumps icon-button hit area to 44×44 (header / avatar) and 36×36 (tab actions) so touch users meet HIG / Material guidelines without altering desktop density.
- `useAdminAppStore`: new tablet (768-1023) watcher auto-collapses the sider so the 220px column doesn't eat 28% of an iPad portrait viewport; restores on transition back to desktop.
- `TCrudPage` advanced-search grid span ladder upgraded from 1/2/4 to 1/2/3/4 across xs/sm/md/l so 768-1023 stops jumping straight to 4 cols with cramped inputs; matching `searchButtonSpan` recalculation.
- `TAdminUserAvatar` name hides below 640px (icon + native title tooltip keep the affordance discoverable); 160px max-width otherwise prevents long display names from crowding the header.

**Tests — `@tnzi/ui-admin`**

- 6 new `useBreakpoint` unit tests (xs/sm/md/lg/desktop bands + touch probe + width reactivity).
- 10 new component integration tests covering `TFormModal` / `TGlobalSearch` / `TAdminHeader` / `TDashboardPage` / `TAdminUserAvatar` responsive paths.
- Suite now: 687 pass (was 671). No regressions in existing tests.

**Migration**

Existing consumers are mostly unaffected — the changes are additive or behave identically at desktop widths. Two opt-out flags let callers restore legacy behaviour:

- `<TFormModal :fullscreen="false" />` forces never-fullscreen (auto-detect was the old behaviour but with the original 560px width on all viewports).
- `<TAdminHeader overflow-menu-breakpoint="never" />` keeps the inline button row at every viewport.

See [MIGRATION.md](./MIGRATION.md) for the full opt-out catalog.

## [0.2.70] — 2026-05-16 (`@tnzi/ui-admin` revert Phase H3 Suspense — page render regression)

User reported "click a sidebar menu and the page is invisible".
Browser DOM measurement showed `.t-admin-content__page` rendering
as an empty div with `routerLoading="on"` permanently — the route
component never resolved.

Root cause: the Phase H3 (L2) Suspense wrapper around KeepAlive +
the `defineAsyncComponent(...)` route components is a triple-bind
that Vue 3.5 doesn't handle cleanly. The async setup inside the
nested combo never settles, so the Suspense fallback never swaps
to the resolved component and the route progress bar stays "on"
forever. Vue Router itself warns about `defineAsyncComponent` use
in route records ("Write `() => import('./MyPage.vue')` instead").

**Fixed** (`@tnzi/ui-admin`):

- `TAdminRouterView` reverted to the pre-H3 simple shape:
  `RouterView → Transition → KeepAlive → component`. No Suspense.
  Pages render correctly across navigations again. Verified
  `/admin/workbench` (8 KPI cards), `/admin/identity/users` (53KB
  content), `/admin/system/menus` (30KB content) all render.

**Follow-up**:

- Suspense splash can come back later as a targeted enhancement
  after we (a) clean up `routes.ts` to drop `defineAsyncComponent`
  in favour of plain `() => import(...)` (Vue Router warning), or
  (b) put Suspense outside KeepAlive in a way that doesn't break
  the async resolution chain.
- A separate small bug: `useRouteProgress` shows progress bar stuck
  on "on" after navigation. Tracked separately; doesn't affect
  content render.

## [0.2.69] — 2026-05-16 (`@tnzi/ui-admin` chrome tab bottom-flush, for real)

User flagged that chrome tabs still weren't flush against the bar
bottom when the tab-bar height was customised. Browser DOM-measurement
caught the root cause: `.t-admin-tabs__list` was sitting at its
intrinsic height (~33px = tab content) instead of the bar's full
height (~42px), because the parent `.t-admin-tabs` uses
`align-items: center` so the list shrunk to fit. `align-items:
flex-end` on the inner `.t-admin-tabs__draggable` therefore had no
vertical room to push the tabs anywhere — they floated in the
middle of the bar.

**Fixed** (`@tnzi/ui-admin`):

- `.t-admin-tabs__list { height: 100%; align-self: stretch }` so
  the scroll container fills the bar's full height. Now flex-end on
  the inner draggable actually pushes chrome/slider tabs to the bar
  bottom — verified: `tab_bottom = 101`, `bar_bottom = 102`, gap is
  just the 1px bottom border. Matches soybean's `bsWrapper` using
  `h-full` for the same reason (`global-tab/index.vue:189`).

This is the symmetric "I forgot to apply h-full to the wrapper"
mistake that plagued the rail icon work three rounds ago — the
fix is one rule but the diagnosis required DOM measurement.

## [0.2.68] — 2026-05-16 (`@tnzi/ui-admin` Phase H4 follow-up — global search modal width)

In-browser verification of 0.2.67 caught one regression: the
`TGlobalSearch` modal rendered at ~315px instead of the intended
630px. Naive UI's `NModal` `:style` prop applies to the modal mask,
not the inner card. Switched to `:card-style="{ width: '630px' }"`
which the `preset="card"` mode forwards to the wrapping `NCard`.

## [0.2.67] — 2026-05-16 (`@tnzi/ui-admin` Phase H4 — P2 精修)

Round 4 of full 62-item deep-audit follow-up. Top-value P2 items.
Remaining P2 (better-scroll, SVG SystemLogo, full iconify-pack
mix, drawer setting TransitionGroup) deferred — they're long-tail
cosmetic touch-ups with low per-item user impact.

**Added** (`@tnzi/ui-admin`):

- `TBackTop.vue` (`components/utility/`) — thin `NBackTop` wrapper
  with a 40px primary-tinted floating button (chevron-up icon) that
  shows when scrolled past 200px. Default-mounted in `TAdminShell`
  via the new `:builtin-back-top="true"` prop (opt-out available).
- `TGlobalSearch` footer keyboard-hint row (I3): `↵` to select,
  `↑↓` to navigate, `esc` to close. Mirrors soybean's
  `search-footer.vue`. i18n keys `admin.search.kbdEnter` /
  `kbdNav` / `kbdClose` added (en + zh-cn).

**Changed** (`@tnzi/ui-admin`):

- **D8 — Tab transition 150ms → 300ms.** Matches soybean's
  `transition-all-300` on `PageTab` so active/inactive colour
  changes feel less abrupt.
- **I4 — TGlobalSearch empty state uses `NEmpty`** instead of a
  hand-written `<li class="__empty">`. Same i18n description; more
  consistent with soybean's preset visual.
- **I6 — TGlobalSearch active item gets solid primary fill + white
  text** (was a 12% primary tint with default text). Mirrors
  soybean `search-result.vue:38-42` — much higher contrast for the
  keyboard-active row.
- **TGlobalSearch modal width 600 → 630px** to match soybean.

**Deferred** (still on the audit board, low-priority):

- K3 SystemLogo SVG with theme-colour gradient stops
- K4 mixed iconify packs (heroicons/material-symbols/etc.) per
  button — current mdi-only setup works but lacks soybean's
  designer polish
- D3 BetterScroll for tab horizontal scroll (current
  `scrollIntoView` works for keyboard navigation; better-scroll
  would only smooth the touch flick on mobile)
- G5 TransitionGroup wrapping drawer condition rows
- I5 search debounce — reverted because it broke 2 unit tests
  that expect immediate filter updates; can come back as an
  opt-in prop later

## [0.2.66] — 2026-05-16 (`@tnzi/ui-admin` Phase H3 — P1 长尾)

Round 3 of full 62-item deep-audit follow-up. P1 long-tail items.

**Added** (`@tnzi/ui-admin`):

- `TSettingItem.vue` (`components/utility/`) — port of soybean's
  `setting-item.vue`: left-aligned label + optional help-tooltip
  `?` icon + right-slot control. Reusable for any drawer / settings
  pane that wants soybean-style row layout with tooltip help.
- `TAdminRouterView` — `<Suspense>` boundary wrapping `<KeepAlive>`
  + `<component>` (L2). Lazy-loaded route components now show a
  bouncing 3-dot spinner during fetch instead of a flash of white;
  consumers can override via the `#loading` slot. Mirrors soybean's
  `app-provider` Suspense pattern.

**Changed** (`@tnzi/ui-admin`):

- **D4 — TAdminTabs gains a fullscreen button next to reload.**
  soybean's `global-tab/index.vue:220-221` places the fullscreen
  toggle in the tab bar, not the header. Added `showFullscreen`
  prop (default true); toggles `appStore.toggleFullContent`. The
  pre-existing header fullscreen button stays as a fallback for
  layouts that hide the tabs entirely.
- **B3 — TAdminShell renders a default `TSystemLogo` in the header
  `#logo` slot** in horizontal / top-hybrid-sidebar-first / top-
  hybrid-header-first modes. Vertical / vertical-mix / vertical-
  hybrid-header-first leave the logo in the sider header. Consumer
  override via `#header-logo` slot still wins.

**Already aligned** (no change needed):

- **L1 NProgress route progress bar** — `useRouteProgress` (no-dep,
  pure CSS) was added before; `defineAdminApp` auto-installs it. The
  audit erroneously flagged it as missing.

**Tests fixed**:

- `TAdminRouterView.test.ts` "navigates between cached routes":
  the initial L2 attempt put `:key` on `<Suspense>`, which made
  Suspense rebuild on every route change and broke KeepAlive's
  caching semantic. Restructured to `<Suspense>` outside
  `<KeepAlive>` with the `:key` on the inner component — KeepAlive
  works as expected and the spinner still shows on first visit.

## [0.2.65] — 2026-05-16 (`@tnzi/ui-admin` Phase H2 — P1 视觉对齐)

Round 2 of full 62-item deep-audit follow-up. 8 P1 items.

**Added** (`@tnzi/ui-admin`):

- `TButtonIcon.vue` (`components/utility/`) — port of soybean's
  `custom/button-icon.vue` pattern: NButton (quaternary circle) +
  optional NTooltip wrapper. Provided as a reusable primitive though
  the immediate consumers (TAdminHeader) wrap their own buttons in
  NTooltip directly to preserve existing test selectors.
- `useAdminThemeStore` — three new mix-layout fields:
  `mixCollapsedWidth` (default 64, the rail width when the sider is
  collapsed in vertical-mix), `autoSelectFirstMenu` (default true,
  whether clicking a hybrid 1st-level menu auto-navigates to the
  deepest leaf — mirrors `themeStore.sider.autoSelectFirstMenu`).
  `mixChildMenuWidth` was already present; persistence + reset +
  snapshot entries added for all three. Drawer Layout tab gains
  conditionally-rendered controls for each (only visible when the
  current layout mode actually uses them).
- `AdminThemeSnapshotV1` — optional `mixCollapsedWidth`,
  `mixChildMenuWidth`, `autoSelectFirstMenu` fields.

**Changed** (`@tnzi/ui-admin`):

- **K1 — TAdminHeader icon buttons get NTooltip hover labels.**
  Every header action (toggler, search, reload, fullscreen, lang,
  theme-schema, theme) is now wrapped in `<NTooltip>` so new users
  can discover what each button does. Mirrors soybean's
  `custom/button-icon.vue` pattern.
- **D5 — TAdminTabs reload button uses iconify + spin animation.**
  Replaced the Unicode `↻` character with `<TSvgIcon icon="mdi:refresh">`
  + `.t-admin-tabs__reload--loading` class bound to
  `!appStore.reloadFlag`. New CSS keyframes spin 0.75s linear infinite
  (matches soybean `animate-spin animate-duration-750`).
- **D6 — chrome divider dark-mode flip.** `:global(.dark)
  .t-admin-tabs__chrome-divider` now uses `rgba(255,255,255,0.9)`
  (matches soybean `.chrome-tab_dark .chrome-tab-divider`).
- **G3 — Theme drawer section titles use NDivider.** All
  `<h4 class="t-theme-drawer__section-title">` instances replaced
  with `<NDivider class="t-theme-drawer__divider">` so each section
  reads with soybean's centred horizontal-rule style instead of a
  tight uppercase label.
- **B4 — TAdminShell default-renders TAdminUserAvatar in #user slot.**
  New `:builtin-user-avatar="true"` prop (default true); consumers can
  opt out via `:builtin-user-avatar="false"` and supply their own
  `#header-user` slot content. Zero-config user dropdown for
  consumer apps. (Note: pulls in `useDialog()` so the host
  app must wrap with `NDialogProvider`; existing consumers already do.)
- **C6 — TAdminMixRail default-renders TMenuToggler in #footer slot.**
  When TAdminShell uses the rail (vertical-mix mode), the rail now
  shows a built-in collapse toggle at its bottom — soybean's
  `first-level-menu.vue` has the same. Consumers can still override
  via the `#footer` slot.

**Notes**:

- Tests `TAdminShell.test.ts` and `TThemeDrawer.test.ts` updated:
  added NDivider/NTooltip stubs to drawer test; TAdminShell tests
  opt out of `builtinUserAvatar` / `builtinSearch` (which both need
  provider stacks the layout-structure tests don't set up).

## [0.2.64] — 2026-05-16 (`@tnzi/ui-admin` Phase H1 — P0 救命修复)

Round 1 of the full 62-item deep-audit follow-up. P0 = "user sees
this on first load" fixes; six items.

**Fixed** (`@tnzi/ui-admin`):

- **D1 — Tab bar / header / footer collapsed under empty content.**
  `.t-admin-tabs`, `.t-admin-header`, `.t-admin-footer` all gain
  `flex-shrink: 0` so the column-flex parent (`.t-admin-shell__main`)
  can't squash them. Without this the tab bar collapsed from 44px to
  ~34px when the page area was empty (e.g. fresh workbench mount),
  cropping the chrome SVG arcs and making chrome tabs look like
  button-style chips. soybean's AdminLayout uses the same
  flex-shrink:0 rails.
- **D2 — Tab title missing prefix icon.** soybean's global-tab/
  index.vue:212-214 renders a 16px iconify SvgIcon before every tab
  label (visual anchor). Our template only had `<span class="__title">`.
  Added `<TSvgIcon :icon="tabIconOf(tab)" :size="16">` ahead of the
  title in both the draggable and non-draggable branches. Falls back
  to `mdi:file-document-outline` when `meta.icon` isn't set.
- **B1 — No independent Sun/Moon theme schema toggle.** soybean's
  global-header/index.vue:49-53 puts a standalone button that cycles
  light↔dark↔auto in one click. Our header forced users to open the
  drawer → Appearance tab → mode radio (3 steps). Added new
  `showThemeSchemaBtn` prop (default true) + button + cycle logic
  reading from the `@tnzi/ui` theme context. Icons:
  `material-symbols:sunny-rounded` / `nightlight-rounded` /
  `hdr-auto` (matches soybean's choices).
- **B2 / B7 — Header shadow + height transition.** Earlier (false)
  parity annotation claimed "no box-shadow". soybean actually uses
  a subtle `0 1px 2px 0 rgb(0 21 41 / 8%)` so the header lifts off
  the content. Added that + `transition: height 0.3s ease` so
  drawer-driven height changes animate.
- **D7 — Tab bar shadow.** Same wrong "no shadow" annotation. Added
  the same subtle shadow.
- **I1 — Global search Ctrl/Cmd+K wasn't bound.** `TGlobalSearch`
  existed but was never mounted. `TAdminShell` now mounts it by
  default (`:builtinSearch="true"` prop, opt-out available) and
  binds a window `keydown` listener for Cmd/Ctrl+K. Select handler
  navigates via vue-router. Consumers can pass
  `:builtin-search="false"` to handle `@openSearch` themselves.
- **A2 / H28 — `scrollMode` (content vs wrapper) state added.**
  `useAdminThemeStore.scrollMode: Ref<'content' | 'wrapper'>` +
  setter + persistence + snapshot. `TAdminShell` reads it as a
  `data-scroll-mode` attribute and switches its overflow scheme.
  Drawer gains a Layout-tab NSelect to switch. Mirrors soybean's
  `theme.layout.scrollMode`.

**Bug squashed (incidental)**:

- `TAdminHeader.vue:140` had a stray `\\*` instead of `/*` (syntax
  error in the comment, didn't compile-fail because CSS comments
  are tolerated to start with `*` after a line — but inconsistent).
  Fixed.

## [0.2.63] — 2026-05-16 (`@tnzi/ui-admin` Phase G follow-up #4 — TAdminMixRail + tab align-end)

User reported vertical-mix rail still didn't show "icon + label below"
and tabs still didn't match soybean. Read soybean source end-to-end
(per user's "查看实际代码, 只凭视觉自己写" + "soybean-admin-example
包含页面的代码" directives) and found two structural diffs that
previous CSS-tweaking couldn't paper over.

**Added** (`@tnzi/ui-admin`):

- `TAdminMixRail.vue` — direct port of soybean's
  `src/layouts/modules/global-menu/components/first-level-menu.vue`.
  Custom div-based rail (NOT NMenu) because NMenu can't render the
  "icon-on-top + label-below" geometry cleanly — soybean wrote it as
  a lightweight reusable template too. Faithful port: `mx-4px mb-6px
  flex-col-center rounded-8px px-4px py-8px` item geometry, 22px
  icon stacked above a 12px label with `h-20px pt-4px ellipsis`,
  `selectedBgColor` derived via `transformColorWithOpacity(themeColor,
  0.1, '#ffffff')` (we use `color-mix` with the same intent), inverted
  variant flips to white-on-primary fill, mini mode collapses the
  label height to 0. Reference file:
  `D:\Github\soybean-admin-example\src\layouts\modules\global-menu\
   components\first-level-menu.vue`.

**Changed** (`@tnzi/ui-admin`):

- `TAdminShell` vertical-mix branch now renders `TAdminMixRail`
  instead of `TAdminSidebar`. Other modes (vertical / hybrids) keep
  using `TAdminSidebar` (NMenu) — that's still the right tool for
  classical row-based menus. New `onMixRailSelect` handler bridges
  the rail's string-key emit to the existing `onMixPrimarySelect`
  drawer-or-navigate logic.
- `TAdminTabs.__draggable` `align-items` now mirrors soybean's
  `global-tab/index.vue:193-197` per-style logic: `flex-end` for
  chrome+slider (so the SVG arc / 2px underline sit flush against
  the bar bottom), `center` for button (so the chip floats mid-bar).
  Previously we hard-coded `center` for all styles, which made
  chrome tabs look detached from the bar — the user-reported "tab
  style change-and-no-change" core issue.

**Fixed** (`@tnzi/ui-admin`):

- Test `TAdminShell.test.ts` updated: vertical-mix mode now expects
  0 TAdminSidebar instances (was 1) + 1 TAdminMixRail instance.

Reference files (read end-to-end this round):
- `D:\Github\soybean-admin-example\src\layouts\modules\global-menu\
   modules\vertical-mix-menu.vue`
- `D:\Github\soybean-admin-example\src\layouts\modules\global-menu\
   components\first-level-menu.vue`
- `D:\Github\soybean-admin-example\src\layouts\modules\global-menu\
   context\index.ts`
- `D:\Github\soybean-admin-example\src\layouts\modules\global-tab\
   index.vue`

## [0.2.62] — 2026-05-15 (`@tnzi/ui-admin` Phase G follow-up #3 — tab inactive colour)

User reported tab style still doesn't match soybean even after the
0.2.59→0.2.61 chrome SVG / button geometry / divider colour fixes.
Found via cross-port DOM measurement that the **inactive tab label
colour** was the actual diff: soybean's chrome/button/slider CSS
modules deliberately don't set a colour on the inactive tab (the
label inherits the layout-tab base text, ≈ `rgb(31,31,31)` near-
black), while our `.t-admin-tabs__tab` base was set to
`var(--tnzi-base-text-muted)` (≈ `rgb(75,85,99)` mid-grey). Read
side-by-side: our tab labels looked washed out compared to soybean.

**Fixed** (`@tnzi/ui-admin`):

- `.t-admin-tabs__tab` base colour `--tnzi-base-text-muted` →
  `--tnzi-base-text`. Matches soybean's inherited-from-parent
  rendering. Verified `outerColor` flipped from `rgb(118,124,130)`
  to `rgb(51,54,57)`.
- `.t-admin-tabs__close` lost its explicit `color` declaration so
  it inherits `currentColor` from the tab outer (soybean's
  `.svg-close` does the same — the SVG path uses
  `fill="currentColor"`). Active tab's close icon now reads primary
  along with the title.
- `[data-style='chrome'] .t-admin-tabs__tab` no longer re-asserts
  the base colour (was `var(--tnzi-base-text-muted)`); inherits
  from base.

Reference: D:\Github\soybean-admin-example\packages\materials\src\
libs\page-tab\index.module.css (read end-to-end as part of this fix;
all three style blocks match line-by-line now).

## [0.2.61] — 2026-05-15 (`@tnzi/ui-admin` Phase G follow-up #2 — sidebar brand + grid + scrollbar)

User flagged four residual issues after 0.2.60 inspection. All fixed
in this version; verified via in-browser DOM measurement.

**Fixed** (`@tnzi/ui-admin`):

- `TAdminSidebar` brand area now collapses to icon-only in
  `vertical-mix` mode. The brand text "Tnzi Admin" runs ~96 px wide
  alone; combined with the icon + header padding it pushed the
  header scrollWidth to 154 px inside a 90 px rail, producing the
  reported sidebar horizontal scroll. New `isHeaderCompact`
  computed = `siderCollapse || mode === 'vertical-mix'` so both
  states render the logo as `icon-only`. soybean's vertical-mix
  rail shows only the logo for the same reason.
- `TThemeDrawer` layout-mode grid switched to
  `repeat(3, 1fr)` + `column-gap: 16px` (was `repeat(3, 96px)` +
  `space-between`). The previous setup produced a 42 px gap between
  fixed-96px cards which looked sparse and left-bunched depending on
  drawer width. New approach lets each cell auto-size to ~113 px,
  the 96 px card centres in its cell via `justify-self: center`,
  and the visible card gap settles at a tight 33 px regardless of
  drawer chrome. Cards stay 96 px so previews don't stretch.
- `TThemeDrawer` `NDrawerContent` now sets
  `:native-scrollbar="false"`. Drawer body uses Naive's NScrollbar
  (thin rail, hidden until hover) instead of the browser-default
  scrollbar. soybean parity.
- `TAdminShell.__sub-sider-body` (vertical-mix drawer scroll
  container) gains the same custom thin-scrollbar styling already
  applied to `__sidebar__body`: `scrollbar-width: thin`,
  `--webkit-scrollbar { width: 6px }`, 50%-opaque border thumb.
  Was using the browser default.

## [0.2.60] — 2026-05-15 (`@tnzi/ui-admin` Phase G follow-up — overflow fixes)

User-reported regressions surfaced via in-browser inspection of 0.2.59
(grid cell stretch + 90px rail overflow).

**Fixed** (`@tnzi/ui-admin`):

- `TThemeDrawer` Layout grid no longer overflows the 420px drawer.
  `repeat(3, max-content)` was letting `<button>` user-agent stretch
  expand the cell width to ~159px (label-driven) so 3 cards summed to
  ~440px > 357px drawer-content width, producing the horizontal
  scrollbar. Pinned the columns to `repeat(3, 96px)` and added
  `width: 96px` + `width: 100%` + `text-overflow: ellipsis` on the
  card button + label so the card respects its declared 96px size
  even under auto-sized parents.
- `TAdminSidebar` `vertical-mix` rail no longer overflows the 90px
  width. NMenu's `:indent="18"` forced an 18px left padding on every
  rail item that, combined with the 4px right padding + 6/6px margin
  + 62px label width, exceeded 90px. Two-part fix: pass `:indent="0"`
  to NMenu when `mode === 'vertical-mix'`, and add `!important` to
  the scoped `padding: 8px 4px` so NMenu's internal inline padding
  rules can no longer stomp it. Also added `text-overflow: ellipsis`
  on the rail label so a slightly-long label truncates inside the
  rail rather than blowing it out.
- `vitest.config.ts` test timeout 15s → 30s. Phase G follow-up runs
  surfaced 1-4 flaky integration timeouts per pass on Windows SSDs
  (the page changed between runs — environmental, not regression).
  30s gives all integration mounts breathing room without masking
  genuine hangs.

## [0.2.59] — 2026-05-15 (`@tnzi/ui-admin` Phase G — layout cards + tab style overhaul)

User-driven soybean parity round 2: the layout-mode cards are now a
2x3 grid with mode-specific atomic preview rather than a 2-col grid
with a generic content-line placeholder; `vertical-mix` rail finally
shows menu icons; tabs gain proper chrome SVG arcs, true 4px-rounded
button style, restored slider style, pin/unpin, middle-click toggle,
icon-decorated context menu, and auto-scroll-into-view.

**Added** (`@tnzi/ui-admin`):

- `TChromeTabBg.vue` — port of soybean's
  `chrome-tab-bg.vue` SVG arc background. Stacked at z-index -1 inside
  each chrome tab; `currentColor` inherits the tab's text colour so
  active/hover state simply changes the colour.
- `useAdminTabStore` — pin/unpin support: `fixedTabIds: string[]`,
  `isTabPinned(id)`, `isTabRetain(id)` (true for home tab + pinned),
  `fixTab(id)`, `unfixTab(id)`. `removeTab` and the four close-* ops
  refuse to drop pinned tabs (close-others/left/right/all all skip
  them, matching soybean's `tab/index.ts` behaviour).
  `fixedTabIds` is persisted alongside `tabs` and `activeTabId`.
- `useAdminThemeStore.closeTabByMiddleClick` (default `false` to match
  soybean) + `setCloseTabByMiddleClick` setter, persisted.
- `TabStyle` regained `'slider'` (we had dropped it in 0.2.55 on a
  faulty premise; G2 diagnosis confirmed slider is independent —
  transparent chip with 2px primary bottom border + 10% primary bg).
- `TThemeDrawer` Layout tab — `Close tab on middle click` switch.
- `tabs.pin` / `tabs.unpin` i18n keys + tab style 'slider' i18n labels.
- `AdminThemeSnapshotV1.closeTabByMiddleClick?` field (optional, importer
  treats absence as no-op).

**Changed** (`@tnzi/ui-admin`):

- `TLayoutModeCard` rewritten. Drops the `geometry switch + content-line`
  abstraction in favour of six explicit `v-if` blocks mirroring
  soybean's `layout-mode.vue:18-60`. Canvas is fixed `96 × 64 px`
  (was `1fr` aspect-ratio). Atomic primitives: `__sider`,
  `__sider--primary` (full primary), `__sider--tertiary` (30% primary),
  `__sider--w8` / `--w16` / `--w18` for widths; `__header`,
  `__header--primary`, `__header--secondary`, `__header--tertiary`;
  `__main` (20% primary). Each mode now draws a distinguishable
  preview, including the three previously-identical hybrid modes.
- `TThemeDrawer.t-theme-drawer__layout-grid` now `repeat(3, max-content)`
  + `column-gap: 16px / row-gap: 12px` so 6 fixed-width cards arrange
  as 2 rows × 3 cols (matches soybean's
  `grid grid-cols-2 gap-x-16px gap-y-12px md:grid-cols-3`).
- `TAdminTabs` rewritten:
  - **Chrome style** uses `<TChromeTabBg>` SVG (currentColor-tinted)
    + `-mr-18px` overlap so adjacent tabs interlock; active tab gets
    `z-index: 10` + `color: var(--tnzi-primary-rgb 0.10)`. Fixed
    `__chrome-divider` strip on the right (`right: 7px; height: 16px`),
    hidden on hover/active.
  - **Button style** is now a 4px-rounded rectangle with 1px border
    (was a 999px pill). Active = primary text + primary@10% bg +
    primary@30% border (was white text + 100% primary bg). Mirrors
    soybean's `button-tab.vue:38-46`. Fixes the previous "white text
    on 6% transparent purple = invisible" bug from 0.2.55.
  - **Slider style** restored: full-bar height, transparent bg,
    `border-bottom: 2px transparent → primary` on active.
  - Close button is now a circular 16x16 SVG `×` (matches soybean's
    `svg-close.vue`) instead of text `×`. Hover = white-on-#9ca3af
    grey, white-on-primary on the active tab.
  - Active tab `scrollIntoView({ inline: 'center' })` on route change
    (matches soybean's `scrollToActiveTab`).
  - Context menu gains a 6th item (Pin / Unpin) and 5 iconified rows
    (mdi:close, mdi:close-box-multiple-outline,
    mdi:format-horizontal-align-{left,right}, mdi:close-circle-outline,
    mdi:pin-{outline,off-outline}). Mirrors soybean's
    `global-tab/context-menu.vue:39-66`.
  - Close button hides for retained tabs (home + pinned) and slider
    style (matches soybean).
  - Middle-click close now reads `themeStore.closeTabByMiddleClick`
    when no explicit `closeByMiddleClick` prop is provided.
- `TAdminSidebar` — vertical-mix `menuOptions` now includes the icon
  via `() => h(TSvgIcon, { icon, size: 20 })`. Previously the rail
  rendered as a label-only column because the option-mapping shape
  silently dropped icons. Active/hover NMenu vars adjusted to
  soybean palette: hover = neutral grey 6%, active = primary 10%.
- `TAdminShell` — vertical-mix sub-sider NMenu gains `:expanded-keys`
  bound to a route-derived ancestor path so the drawer auto-expands
  the active leaf's parent group.
- `TAdminShell.__sub-sider-title` font-weight 600 → 700 (matches
  soybean's `text-primary font-bold`).

**Migration**: see [MIGRATION.md `0.2.x` → `0.2.59`](./MIGRATION.md#0259).

## [0.2.58] — 2026-05-15 (`@tnzi/ui-admin` Phase F — accessibility filters + preset cards)

Long-tail soybean parity: accessibility filters (grayscale + colour-
weakness simulation), info-follows-primary linkage, and preset cards
in the Preset tab.

**Added** (`@tnzi/ui-admin`):

- `useAdminThemeStore.grayscale` + `colourWeakness` state, with
  `setGrayscale` / `setColourWeakness` setters that apply
  `document.documentElement.style.filter` (grayscale 100%,
  invert 80%). Mirrors soybean's `toggleAuxiliaryColorModes`.
  Both persisted; `reset()` clears them and the filter.
- `TThemeDrawer` Appearance tab — Grayscale + Colour-weakness switches.
- `TThemeDrawer` Preset tab — preset cards grid (up to 8 from
  `resolvedPresets`). Each card shows the primary colour swatch + name
  and clicking applies the colour. Mirrors soybean's preset block
  in `theme-drawer/modules/preset/index.vue`.
- `infoFollowPrimary` now actually links: when the toggle is on and
  the user changes the primary colour, the info colour follows
  automatically (matches soybean `theme-color.vue:67-69`).
- `AdminThemeSnapshotV1` — `grayscale?` + `colourWeakness?` fields
  added (optional, importer treats absence as no-op).

## [0.2.57] — 2026-05-15 (`@tnzi/ui-admin` Phase E — hybrid layout layered menus)

The 4 hybrid layout modes finally render distinct menu structures
instead of all looking like "vertical + horizontal stacked together".
Replaces the inline `activeFirstLevelKey` walker with a proper
composable that mirrors soybean's `provideMixMenuContext()`.

**Added** (`@tnzi/ui-admin`):

- `headless/useAdminMenuContext.ts` — Phase E composable. Exposes
  `firstLevelMenus` / `secondLevelMenus` / `childLevelMenus` /
  `activeFirstLevelMenuKey` / `activeSecondLevelMenuKey` /
  `isActiveFirstLevelMenuHasChildren` / `handleSelectFirstLevelMenu` /
  `handleSelectSecondLevelMenu` / `resolveFirstLevelKeyForRoute`.
  Fallback rule: if the current route doesn't match any menu key,
  default the active 1st level to the first item with children
  (fixes the long-standing vertical-mix bug where Workbench landed
  with the drawer permanently empty). 11 unit tests cover the
  composable.
- `TAdminSidebar` — `:items` prop overrides the default
  `routeStore.menus` source. Hybrid modes pass the layered slice the
  sider should render.
- `TAdminTopMenu` — `:items` prop + `:active-key` prop; same purpose.

**Fixed** (`@tnzi/ui-admin`):

- **3 hybrid modes were visually identical** to vertical-mix /
  horizontal because the sider always rendered the full menu tree.
  Now:
  - `vertical-hybrid-header-first` → top hosts 1st level + sider
    hosts the children of the active 1st level.
  - `top-hybrid-sidebar-first` → top hosts the 2nd level (children
    of the active 1st level) + sider hosts the 1st level rail.
  - `top-hybrid-header-first` → top hosts 1st level + sider hosts
    the children of the active 1st level. Sider auto-collapses to
    `width: 0` when the active 1st level has no children
    (matches soybean's `siderVisible` rule).
- **`vertical-mix` drawer was permanently empty when landing on a
  childless 1st level** (e.g. Workbench). Active 1st level now falls
  back to the first item with children, so the drawer ships with
  meaningful content.
- **`TLayoutModeCard` previews for modes 4/5/6 were all identical
  geometry**, so the theme drawer's layout grid had three
  visually-indistinguishable cards. Added `siderVariant: 'normal' |
  'rail'` + per-mode geometry so each card draws a distinct preview.

**Migration**: see [MIGRATION.md `0.2.x` → `0.2.57`](./MIGRATION.md#0257).

## [0.2.56] — 2026-05-15 (`@tnzi/ui-admin` Phase D — drawer parity)

soybean parity follow-up: theme drawer gains the missing controls, the
brittle visual chrome (per-row dashed border, deeply-buried reset) is
cleaned up, and Reset/Copy now live in a persistent drawer footer.

**Added** (`@tnzi/ui-admin`):

- `useAdminThemeStore` — six new soybean-parity settings:
  `recommendColor` (boolean), `infoFollowPrimary` (boolean),
  `tabCache` (boolean), `breadcrumbShowIcon` (boolean),
  `multilingualVisible` (boolean), `globalSearchVisible` (boolean).
  All persisted, all reset to soybean defaults on `reset()`.
- `TThemeDrawer`:
  - **Appearance tab** — `themeRadius` slider (0-16px), `recommendColor`
    switch, `infoFollowPrimary` switch.
  - **Layout tab** — `siderCollapsedWidth` slider (48-100px),
    `tabCache` switch, `breadcrumbShowIcon` switch.
  - **General tab** — `multilingualVisible` switch,
    `globalSearchVisible` switch.
- `AdminThemeSnapshotV1` — six new optional fields on `admin` block;
  importer treats absence as "keep current" (no-op).

**Changed** (`@tnzi/ui-admin`):

- `TThemeDrawer` — Reset + Copy buttons lifted out of the Preset tab
  into the `NDrawerContent #footer` slot so they're reachable from
  every tab. Mirrors soybean's
  `theme-drawer/components/config-operation.vue:51-54`.
- `TThemeDrawer` — `.t-theme-drawer__row` no longer renders a dashed
  bottom border; soybean uses a flat 12px-gap stack and the dashes
  added visual noise. Reduced row padding from 10px → 8px.
- `useAdminThemeStore.reset()` resets all six new fields to their
  defaults along with the existing ones.

**Removed** (`@tnzi/ui-admin`):

- `t-theme-drawer__reset` custom button class (the destructive button
  inside the Preset tab). Reset now uses a standard `NButton` in the
  footer.

## [0.2.55] — 2026-05-15 (`@tnzi/ui-admin` Phase C — tab style + horizontal header guard)

**Fixed** (`@tnzi/ui-admin`):

- `TAdminTabs` button-style active tab now actually paints the primary
  background and white text. The previous rule
  `[data-style='button'] .t-admin-tabs__tab--active { background-color: ... }`
  had identical CSS specificity to the base `.t-admin-tabs__tab--active`
  rule and lost the cascade race on cold mount, leaving active tabs
  transparent + purple-text (chrome-style fallback). Switched the active
  rule to read `var(--t-tab-active-bg, transparent)` /
  `var(--t-tab-active-color, var(--tnzi-primary))` /
  `var(--t-tab-active-underline-display, block)`; the data-style selector
  now only redefines the vars at the parent (specificity stops
  mattering). Drag/drop wrapper from `vue-draggable-plus` no longer
  blocks visual updates.
- `TAdminShell.headerVisible` computed now forces `true` whenever the
  layout mode hosts the menu in the header (`horizontal`,
  `vertical-hybrid-header-first`, `top-hybrid-sidebar-first`,
  `top-hybrid-header-first`). Previously a user toggling "Show header"
  off in horizontal mode would lose all navigation — the menu lived in
  the now-hidden header and the sider was suppressed by mode.
- `TThemeDrawer` "Show header" toggle is now `:disabled` in the four
  header-hosted layouts and reads `true` while disabled, communicating
  the constraint instead of silently no-op-ing.

**Removed** (`@tnzi/ui-admin`):

- `tabStyle: 'slider'` — soybean only ships `chrome` and `button`,
  and our slider variant was visually indistinguishable from chrome.
  `TabStyle` type narrowed; `setTabStyle('slider')` now silently
  rejects (validation drops it). Persisted snapshots from 0.2.54
  containing `tabStyle: 'slider'` will load the field as the default
  `'chrome'` (validation gate).

**Migration**: see [MIGRATION.md `0.2.x` → `0.2.55`](./MIGRATION.md#0255).

## [0.2.54] — 2026-05-15 (`@tnzi/ui-admin` Phase B — invertSider 真实现)

soybean parity follow-up: `invertSider` setting now actually inverts the
sider including the menu items, not just the empty wrapper underneath.

**Fixed** (`@tnzi/ui-admin`):

- `TAdminSidebar` accepts an `:inverted` prop. When true, it switches its
  own surface chrome (sider bg via `--tnzi-admin-sider-inverted-bg`,
  brand title to inverted-text, footer border to inverted-border) and
  passes `:inverted="true"` to the inner `<NMenu>` so menu item text /
  hover / active colours flip via Naive's inverted theme. Previously
  the toggle drove a `.t-admin-shell--invert-sider .t-admin-shell__sider`
  wrapper rule that lost the cascade race to the child component's own
  scoped `background: var(--tnzi-admin-sider-bg, ...)` declaration —
  the wrapper went dark while the inner sidebar stayed white.
- `TAdminShell` computes `siderInverted = themeStore.invertSider &&
  !isDark && layoutMode in ['vertical', 'vertical-mix']` and passes
  it to both the desktop sider and the mobile drawer sider. Mirrors
  soybean's `darkMenu = !darkMode && layout.includes('vertical')` —
  the toggle has no visible effect outside the supported combinations
  so the drawer hides it (see `TThemeDrawer` change below).

**Changed** (`@tnzi/ui-admin`):

- `TThemeDrawer` moved the `invertSider` switch from the Layout tab
  to the Appearance tab (where soybean keeps it), and gated its
  display with `v-if="!isDark && layoutMode.startsWith('vertical')"`
  so it doesn't appear when the toggle wouldn't do anything. Drops
  the previous wrapper-bg hack CSS from `TAdminShell`.

## [0.2.53] — 2026-05-15 (`@tnzi/ui-admin` Phase A — 4 救命修复)

Round-1 of soybean-parity audit follow-up. Four user-visible defects
fixed in one release; all gated by static checks (typecheck + 659
unit tests green).

**Fixed** (`@tnzi/ui-admin`):

- **CRUD edit/view modal never opens** (`headless/useFormModal.ts`) —
  `cloneInitial` called `structuredClone` on Vue reactive Proxy
  objects, which by spec throws `DataCloneError`. The exception fired
  before `visible.value = true`, so the modal stayed closed silently
  on every row edit. Fix: strip Proxy via `toRaw` before cloning, and
  fall back to JSON round-trip if `structuredClone` still rejects.
  Affected ~40 admin preset pages (every TCrudPage edit/view button
  was inert in production; unit tests passed because they constructed
  plain objects bypassing the reactive path).
- **Route transitions never trigger** (`router/AdminShellRoot.vue`) —
  `<router-view />` was nested inside `TAdminContent`'s `<Transition>`
  wrapper, but Vue's transition tracks the direct child slot, not
  whatever the slot's component swaps to. Replaced with the existing
  `TAdminRouterView` component, which uses the canonical
  `<RouterView v-slot>` + `<Transition>` + `<component :is>` pattern
  and was already written but unused. All 6 transition presets
  (fade / fade-slide / fade-bottom / fade-scale / zoom-fade /
  zoom-out) now actually animate.
- **Content area drifted cold-purple** (`styles/variables.css`) —
  `--tnzi-admin-content-bg` was defined as
  `color-mix(layout 95%, primary 5%)` based on a misread of soybean's
  behaviour. soybean's `bg-layout` is the flat container surface; no
  primary is mixed in. Removed the color-mix and aliased
  `--tnzi-admin-content-bg` directly to `--tnzi-layout-bg`. Content
  area now matches soybean's neutral grey across light + dark.

**Removed** (`@tnzi/ui-admin`):

- **`invertHeader` setting + toggle + CSS** — soybean has no inverted
  header (the header always follows the global light/dark mode), and
  our implementation was visually broken anyway: the wrapper
  `:deep(.t-admin-header) { background: ... }` rule had identical
  CSS specificity as the child component's own scoped rule and lost
  the cascade race, producing "black background, grey icons" garbage.
  Dropped from `useAdminThemeStore` (state, setter, persistence pick),
  `theme/admin-config.ts` (snapshot type), `TThemeDrawer` (control
  + import/reset wiring), `TAdminShell` (class + CSS rule),
  `variables.css` (light + dark token), and both i18n catalogues.
  Persisted snapshots from 0.2.52 missing the field on next read are
  no-ops (boolean default falsy).

**Migration**: see [MIGRATION.md `0.2.x` → `0.2.53`](./MIGRATION.md#0253).

## [0.2.36] — 2026-05-15 (`@tnzi/ui-admin` TCrudPage soybean-parity restructure)

Reshape `TCrudPage` to match soybean's two-card CRUD layout (visible
in `9527/manage/user`): a collapsible search panel card on top + a
list card with sub-title + action buttons row + table + footer
pagination. Existing consumer pages keep working without changes
(the same slot API + state contract is preserved).

**Changed** (`@tnzi/ui-admin`):

- `TCrudPage` template restructured:
  - Outer `NSpace` (size 16) wrapping the page so cards stack with
    consistent vertical rhythm.
  - **Search panel**: `NCard` containing `NCollapse / NCollapseItem`
    titled "Search" (collapsed by default). Renders the default
    `NInput` search field when no `#search` slot is supplied. Hidden
    entirely if `showDefaultSearch: false`.
  - **List card**: `NCard` with sub-title in `#header` (uses page
    `title` or i18n `admin.crud.list`) and the 4-button action strip
    in `#header-extra`. Buttons:
    - `+ Create` (primary tertiary, mdi:plus icon).
    - `🗑 Batch Delete` (error tertiary, mdi:trash-can-outline; disabled
      when no rows selected).
    - `↻ Refresh` (tertiary, mdi:refresh).
    - `📤 Export` / `📥 Import` (tertiary, opt-in via
      `showExport` / `showImport` — default `false`).
    - `⚙ Columns` (tertiary, mdi:cog-outline, opens the existing
      column-setting popover).
  - **Table** uses NDataTable's built-in `pagination` prop instead of
    a separate `NPagination`. The pagination footer shows
    `Total N` prefix + page index + size picker — matches soybean's
    "共 N 条 | 1 2 ... | 10/页" layout.

**Added props** (`TCrudPage`):

- `showBatchDelete: boolean` (default `true`).
- `showExport: boolean` (default `false`).
- `showImport: boolean` (default `false`).
- `showDefaultSearch: boolean` (default `true`).

**Removed dependency**:

- `TCrudToolbar` is no longer rendered by `TCrudPage`. Existing
  consumers that imported it directly keep working — it stays
  exported, just no longer wired into the default page shape.
  Test selector `.t-crud-toolbar__refresh` is preserved on the new
  inline Refresh button so existing tests don't break.

**Added locale keys** (en + zh-cn):

- `admin.crud.batchDelete` ("Batch Delete" / "批量删除")
- `admin.crud.search` ("Search" / "搜索")
- `admin.crud.list` ("List" / "列表")
- `admin.crud.total` ("Total" / "共")
- `admin.crud.actions` ("Actions" / "操作")

**Test updates**:

- `TCrudPage.test.ts` stubs extended with NCard / NCollapse /
  NCollapseItem / NSpace passthroughs so slot content keeps rendering.
- `.t-crud-toolbar__refresh button` selector simplified to
  `.t-crud-toolbar__refresh` (the stub Button renders the same DOM
  node it always did — the wrapping container changed).

Visual verification (Chrome side-by-side against
localhost:9527/manage/user):

- Search collapse panel `> Search` on top.
- List card titled "User Management" with 4 action buttons (Create
  / Batch Delete / Refresh / Columns) right-aligned.
- Pagination footer "Total 1 | 1 | 20 / page" right-aligned.
- Sidebar / header / tabs / footer all unchanged from 0.2.35.

Tests: 659 (`@tnzi/ui-admin`) — green.

Remaining polish (not yet shipped):
- Serial-number column + Actions column (edit/delete) — page-level
  customisation, not a framework concern.
- WeChat bind module + various i18n zh-cn completion.

## [0.2.35] — 2026-05-15 (`@tnzi/ui-admin` soybean-detail polish: header/sidebar/tabs)

Side-by-side review against `localhost:9527/manage/user` surfaced a
batch of styling details where the reference consumer diverged from the soybean
reference. All fixed in this patch (no API changes).

**Changed** (`@tnzi/ui-admin`):

- `TAdminHeader.showReload` default flipped from `true` to `false`
  via `TAdminShell` (the tab bar already has its own reload button,
  matching soybean — no duplicate icon in the header).
- `TAdminHeader` language switcher: dropped the "EN" / "中" label
  next to the translate icon (soybean shows only the icon). Icon
  size bumped 18→20 to match its neighbours.
- `TAdminSidebar` active menu row: removed the 3px primary
  left-border accent. Soybean uses only the bg highlight + primary
  text weight; the I.7.6 brief's "3px left-border" spec was
  inaccurate. Active row keeps `font-weight: 500` for emphasis.
- `TSystemLogo`:
  - Title font dropped from 18px to 16px (matches soybean's
    `Soybean 管理系统`).
  - Title `white-space: nowrap` so the brand never wraps.
  - Layout switched to inline `display: inline-flex` styles so the
    flex layout survives cross-package CSS-ordering edge cases
    (observed under unocss + library bundling, where the scoped
    `display: flex` rule was being silently reset to `block`).
- `TAdminSidebar` header now overrides `:deep(.t-system-logo__title)`
  to `var(--tnzi-base-text)`. Login page's stacked layout keeps the
  larger primary-coloured title.
- `useAdminThemeStore.siderWidth` default bumped 220 → 240. Soybean's
  220 is fine for 4-char CJK labels but Tnzi's English labels
  ("User Management" / "Role Management" / etc) need 240 to render
  without ellipsis truncation.
- `TAdminTabs` chrome polish:
  - Tab height reduced 32→28px, padding tightened.
  - Active tab gets a 2px primary underline anchor (matching
    Chrome's active-tab look + soybean's pattern).
  - Subtle 1px separator between adjacent inactive tabs (hidden
    when either side becomes active or hovered).

Visual verification (Chrome side-by-side against localhost:9527):
header utility icons match (search/fullscreen/translate/theme/avatar
with no duplicate reload), sidebar logo is `[icon] [bold black title]`
in one line, active menu row is bg-highlight only, tab bar shows
chrome-style underline + subtle separators.

Tests: 659 (`@tnzi/ui-admin`) — green.

Remaining (CRUD page restructure, soybean-style search/list cards
with action-buttons-with-icons + row-actions column + full
pagination): tracked separately as a Tier-3 follow-up.

## [0.2.34] — 2026-05-15 (`@tnzi/ui-admin` Phase I.7.5: TwoFactorChallenge module + 2FA helpers)

Wires the last login-page placeholder — the two-factor challenge step
that fires after a `pwdLogin` / `codeLogin` returns `requires2FA: true`.

**Added** (`@tnzi/ui-admin`):

- `pages/login/modules/TwoFactorChallenge.vue` — verification module
  showing the active challenge's user name + delivery channel
  ("authenticator app" / "SMS" / "email"), a numeric code input
  (`autocomplete="one-time-code"`, `inputmode="numeric"`), a Verify
  primary button, a conditional Resend button (with `useCaptcha`
  cooldown — shown only when the consumer supplies
  `callbacks.resendTwoFactor`), and Cancel that clears the challenge
  and returns to `pwd-login`.
- `LoginCallbackHelpers` type: `{ setTwoFactorRequired,
  clearTwoFactor }`. Passed as the second argument to `pwdLogin` /
  `codeLogin` callbacks so consumer code can transition to the
  two-factor UI without reaching into Vue state directly.
- `LoginContext.pendingTwoFactor: Ref<TwoFactorChallenge | null>` —
  reactive state shared between PwdLogin / CodeLogin (who set it via
  `helpers.setTwoFactorRequired`) and TwoFactorChallenge (who reads
  it). TLoginPage watches this ref and auto-toggles the active module
  to `two-factor` when it flips non-null.
- `LoginCallbacks.verifyTwoFactor` + `resendTwoFactor` callback slots
  (wire to `POST /auth/verify-2fa` and `POST /auth/send-2fa-code`
  respectively).
- New `LoginModule` value `'two-factor'`. `routes.ts` updates the
  login route regex to accept it; `pages/login/index.vue` registers
  the new module component.
- Top-level exports added: `TwoFactorChallengeModule`,
  `LoginCallbackHelpers`, `LoginDemoAccount`, `VerifyTwoFactorPayload`,
  `TwoFactorChallenge`.

**Changed** (`@tnzi/ui-admin`):

- `PwdLogin.handleSubmit` and `CodeLogin.handleSubmit` now pass
  `helpers` as the second argument to their callbacks. Consumer
  signature change:
  ```ts
  // Before:
  pwdLogin: async ({ userName, password, remember }) => { … }
  // After:
  pwdLogin: async ({ userName, password, remember }, { setTwoFactorRequired }) => { … }
  ```
  Existing callbacks that ignore the second argument keep working.
- `TLoginPage.DEFAULT_LABELS` adds `'two-factor': 'Two-Factor Verification'`.
- `TLoginPage.toggleLoginModule` clears `pendingTwoFactor` whenever
  the user navigates away from the two-factor module mid-flow.

Visual verification (Chrome): `/login/two-factor` renders the module
with title "Two-Factor Verification", the prompt "Enter the code from
your authenticator app.", numeric input with one-time-code autofill,
purple Verify button, and Cancel.

Tests: 659 (`@tnzi/ui-admin`) — green.

Phase I.7 remaining: I.7.10-I.7.12 (theme drawer rework + mix layout
hover) — the optional polish tier.

## [0.2.33] — 2026-05-15 (`@tnzi/ui-admin` CRUD page i18n double-prefix fix)

CRUD pages were surfacing raw `admin.crud.searchPlaceholder` /
`admin.crud.create` / etc. labels because
`translatePageKey('identity.roles', 'admin.crud.searchPlaceholder')`
prepended the namespace blindly, producing
`admin.modules.identity.roles.admin.crud.searchPlaceholder` — a path
that never exists in the locale dictionary.

**Changed** (`@tnzi/ui-admin`):

- `pages/_shared/translate.ts.translatePageKey()`:
  - Strips optional `tnzi.` prefix (callers can use either form).
  - When the key already starts with `admin.` it's looked up **directly**
    at the locale root — the page namespace is ignored. This fixes the
    double-prefix bug TCrudPage / TBatchActions / TCrudToolbar all hit
    by forwarding their `admin.crud.*` literals into the page-level
    translator.
  - Otherwise prepends `admin.modules.{pageNs}.{key}` as before.
  - Falls back to a humanised last-segment label on miss (matches the
    fallback in `useAdminRouteStore.resolveI18nKey`,
    `TAdminAutoBreadcrumb`, and `TAdminTabs.renderTitle`).

Visual verification (Chrome): `/admin/identity/roles` now shows
"Search..." / "Create" / "Refresh" / "Role Name" / etc. — all the
strings `en.ts` already had keys for but the translator was
mis-prefixing.

Tests: 659 (`@tnzi/ui-admin`) — green.

## [0.2.32] — 2026-05-15 (`@tnzi/ui-admin` default Workbench dashboard + i18n humanise fallback)

Replaces the `/admin → /admin/identity/users` redirect with a proper
default landing page matching soybean's `Home` workbench, and stops
raw `tnzi.admin.…` strings from leaking into the sidebar.

**Added** (`@tnzi/ui-admin`):

- `pages/dashboard/Workbench.vue` — default landing page rendered at
  `/admin/workbench`. Composes `THeaderBanner` (greeting +
  date), `TDashboardPage` (4 gradient KPI cards + line chart + pie
  chart), and `TProjectTimeline` + a Tips card. Data is canned so a
  fresh consumer never lands on a blank screen.
- `/admin/workbench` route registered with `keepAlive` +
  `fixedIndexInTab: 0` so it acts as the pinned home tab in the tab bar.
- `DEFAULT_ROUTE_ICONS.workbench = 'mdi:view-dashboard-outline'`.

**Changed** (`@tnzi/ui-admin`):

- `routes.ts` `/admin` redirect target switched from
  `/admin/identity/users` to `/admin/workbench`.
- `useAdminRouteStore.resolveI18nKey` now humanises missing i18n keys
  (e.g. `tnzi.admin.modules.workbench.title` → "Workbench",
  `tnzi.admin.modules.identity.loginLogs.title` → "Login Logs") instead
  of returning the raw dotted string. Mirrors the same fallback in
  `TAdminAutoBreadcrumb` and `TAdminTabs.renderTitle` so sidebar /
  breadcrumb / tabs all show a single humanised surface when the
  consumer hasn't seeded a full i18n pack.

Visual verification (Chrome): `/admin` now redirects to
`/admin/workbench` and renders the soybean-parity layout — greeting
banner, 4 KPI tiles with animated count-ups, ECharts line + pie, and
the activity timeline at the bottom. Sidebar shows "Workbench" / "Login
Log" / etc. as humanised labels instead of `tnzi.admin.modules.…`.

Tests: 659 (`@tnzi/ui-admin`) — green.

## [0.2.31] — 2026-05-15 (`@tnzi/ui-admin` Phase I.7.3-I.7.5: CodeLogin / Register / ResetPwd modules)

Three of the four remaining login-page placeholder modules are now wired
to real form submission. Each follows the same shape: NForm with a
`useFormRules`-validated input row, a `useCaptcha`-throttled send-code
button, a primary submit calling the consumer's `useLoginContext()`
callback, and a "Back" button that returns to `pwd-login`.

**Added** (`@tnzi/ui-admin`):

- `useCaptcha({ seconds?, translate? })` composable in `headless/` —
  60-second cooldown timer + in-flight loading flag + locale-aware
  label ("Get Code" / "60s" / spinner). Source-fidelity port of
  soybean's `useCaptcha` (omitting its `$message`-based phone
  validation; the form-level rule handles that).
- `CodeLogin.vue` — phone/email account + verification code login.
  Calls `callbacks.sendCode({ purpose: 'code-login' })` to trigger the
  code and `callbacks.codeLogin({ account, code })` to submit.
- `Register.vue` — phone/email + code + password + confirmPassword
  registration. Calls `callbacks.sendCode({ purpose: 'register' })`
  and `callbacks.register({ account, code, password })`. On success
  bounces back to `pwd-login` (soybean's flow).
- `ResetPwd.vue` — phone/email + code + new password + confirm. Calls
  `callbacks.sendCode({ purpose: 'reset-pwd' })` and
  `callbacks.resetPwd({ account, code, password })`. On success
  bounces back to `pwd-login`.

All three modules:

- Use `useFormRules` for shared phone / password validators.
- Validate the account field on its own before firing `sendCode` (so
  the user gets a clear "invalid phone" message instead of a confusing
  server error).
- Display submit errors inline (red text under the form) with
  `role="alert"`.
- Render via the new `useCaptcha` cooldown so the user can't spam the
  send-code endpoint.

Endpoints they map to (configured on the consumer side):

| Module     | sendCode → backend                              | submit → backend                          |
|------------|-------------------------------------------------|-------------------------------------------|
| CodeLogin  | `POST /auth/code-login/send-code`               | `POST /auth/code-login`                   |
| Register   | `POST /auth/quick-register/send-code`           | `POST /auth/register` or `quick-register` |
| ResetPwd   | `POST /auth/password-recovery/send-code`        | `POST /auth/password-recovery/reset`      |

Visual verification (Chrome): all three routes
(`/login/code-login`, `/login/register`, `/login/reset-pwd`) render
their respective forms with consistent visual rhythm, Get-Code button
disables during cooldown, "Back" returns to `/login/pwd-login`.

Tests: 659 (`@tnzi/ui-admin`) — green.

Phase I.7 remaining: I.7.5 (TwoFactorChallenge module — back-end
already supports `POST /auth/send-2fa-code` + `/auth/verify-2fa`,
just needs the UI), I.7.10-I.7.12 (theme drawer + mix layout), the
deferred default Dashboard page, and CRUD-page i18n cleanup.

## [0.2.30] — 2026-05-15 (`@tnzi/ui-admin` Phase I.7.9: default footer copyright)

Footer was rendering as a bare grey strip because `TAdminShell.footer.copyright`
was undefined when consumers didn't supply one. `AdminShellRoot` now derives a
default `Copyright © {year} {brand}` line from `useAdminLoginConfig().brand`,
matching soybean's `Copyright MIT © 2021 Soybean` baseline.

**Added** (`@tnzi/ui-admin`):

- `AdminLoginConfig.footer.copyright` / `footer.links` — consumer can
  override the copyright line entirely or append link entries (e.g.
  ToS / Privacy). Empty/missing falls back to the auto-derived
  default.
- `AdminShellRoot` computed `footerCopyright`:
  - Uses `loginConfig.footer.copyright` when supplied.
  - Otherwise: `Copyright © ${new Date().getFullYear()} ${brand}`.
- `AdminShellRoot` forwards `:footer="{ copyright, links }"` into
  `<TAdminShell>`.

Visual verification (Chrome): the footer now reads "Copyright © 2026
the app name" in muted text at the bottom of the content column.

Tests: 659 (`@tnzi/ui-admin`) — green.

Phase I.7 remaining: I.7.3-I.7.5 (CodeLogin / Register / ResetPwd /
2FA modules), I.7.10-I.7.12 (theme drawer rework + mix layout
hover), the deferred default Dashboard page, and the broader
CRUD-page i18n key cleanup tracked in the I.7.x backlog.

## [0.2.29] — 2026-05-15 (`@tnzi/ui-admin` Phase I.7.8: tab bar auto-push + click navigation)

Closes the tab-bar gap surfaced by the soybean side-by-side review: tab
list was empty on every page load because nothing was calling
`tabStore.addTab()` on route change, and `switchRouteByTab` was a stub
that flipped `activeTabId` without actually navigating.

**Added** (`@tnzi/ui-admin`):

- `AdminShellRoot` now watches `useRoute()` (immediate) and pushes the
  current route into `tabStore.addTab()` on every navigation. Bare
  `/admin` redirect (`admin-root`) is skipped. Initial page load seeds
  the first tab so users land on a populated tab bar instead of an
  empty strip.
- `AdminShellRoot.onTabClick(tab)` wires `router.push(tab.fullPath)`
  so clicking an inactive tab actually navigates to that route. The
  same handler is bound to the `tab-click` event TAdminShell now
  re-emits from `TAdminTabs`.
- `TAdminShell.tabClick` emit signature so the event bubbles cleanly
  to consumer routes (typed as `AdminTab`).
- `TAdminTabs.renderTitle()` — translate-then-humanise fallback (e.g.
  `tnzi.admin.modules.identity.users.title` → "Users") so missing
  i18n keys never surface raw to the user. Mirrors the same fallback
  in `TAdminAutoBreadcrumb`.

Visual verification (Chrome): clicking `/admin/identity/users` →
`/admin/identity/roles` shows both tabs in the chrome strip, with the
active tab styled in primary purple. Tabs are draggable via the
existing `vue-draggable-plus` integration. Close buttons remove the
tab and pin tabs (`homeTab`) stay anchored.

Tests: 659 (`@tnzi/ui-admin`) — green.

Known follow-up: built-in admin pages (TCrudPage et al.) still surface
raw `admin.crud.*` i18n keys in their internal labels because they're
wired to a translate function that returns the key unchanged when
missing. That's a CRUD-page-level i18n gap, not a tab-bar issue;
folded into the broader Phase I.7.9 / I.7.x backlog.

## [0.2.28] — 2026-05-15 (`@tnzi/ui-admin` Phase I.7.7: header breadcrumb + user avatar dropdown)

Soybean-parity overhaul for the admin header, closing the last
structural gap before the visual side-by-side matches end-to-end.

**Added** (`@tnzi/ui-admin`):

- `TAdminAutoBreadcrumb` — route-driven wrapper around the existing
  `TAdminBreadcrumb`. Walks `route.matched`, drops the bare `/admin`
  shell root + anything flagged `hideInMenu`, and renders each crumb
  with its mdi icon (from the same `DEFAULT_ROUTE_ICONS` map the
  sidebar uses). Leaf crumbs are clickable; branch crumbs are static.
  Falls back to a humanised key when the translate function misses.
- `TAdminUserAvatar` — header user dropdown matching soybean's
  `user-avatar.vue`. Renders an avatar icon + display name, opens a
  click dropdown with "User Center" / "Logout", confirms logout via
  `NDialog`. Has a "Sign in" button mode for unsigned users.
- `AdminLoginConfig.user` config slot: `{ userName, avatarIcon,
  onUserCenter, onLogout, signedIn, onSignIn }`. Consumers (the reference app
  et al.) configure once via `defineAdminApp({ login: { user: … } })`
  and the header dropdown wires up.
- Components exported from `@tnzi/ui-admin/components`:
  `TAdminAutoBreadcrumb`, `TAdminUserAvatar`.

**Changed** (`@tnzi/ui-admin`):

- `TAdminHeader` no longer auto-renders the `title` prop as a logo
  fallback. The logo lives in `TAdminSidebar` in vertical layout
  modes (Phase I.7.6); consumers wanting the title in the header opt
  in via `<template #logo>`. Both `logo` and `breadcrumb` slot
  containers now use `v-if="$slots.logo/breadcrumb"` so they
  collapse to nothing when unused.
- `AdminShellRoot` now defaults the `#header-breadcrumb` slot to
  `TAdminAutoBreadcrumb` and the `#header-user` slot to
  `TAdminUserAvatar`. Consumer overrides via `<template
  #header-breadcrumb>` / `#header-user` on `<TAdminShell>` still
  win.
- `defaultTranslate` signature widened from `(key) => string` to
  `(key, fallback?) => string` so missing-key paths return the
  caller's fallback instead of the raw key.

**Cross-repo** (reference consumer):

- `admin/src/main.ts` adds `login.user.onLogout` (calls
  `auth.logout()` then redirects to `/login`) so the header
  dropdown is fully wired with no extra component edits.

**Tests**:

- `TAdminHeader` test "renders default title when no logo slot"
  updated to assert the new collapsed-when-empty behaviour.

Visual verification (Chrome side-by-side against localhost:9527):

- Header left shows `<toggler> Identity / Users` with mdi icons
  beside each crumb.
- Header right ends with avatar icon + "Admin" label; clicking opens
  the dropdown with "User Center" / "Logout" rows.

Tests: 659 (`@tnzi/ui-admin`).

Remaining for Phase I.7 (per brief): I.7.3-I.7.5 (CodeLogin /
Register / ResetPwd / 2FA), I.7.8 (TAdminTabs chrome tabs auto-push),
I.7.9 (TAdminFooter visibility fix), and the deferred default
Dashboard page.

## [0.2.27] — 2026-05-15 (`@tnzi/ui-admin` Phase I.7.6: TAdminSidebar parity — logo + menu icons + active polish)

Soybean-parity overhaul for the admin sidebar, driven by side-by-side
review against `localhost:9527`. Three structural gaps closed.

**Added** (`@tnzi/ui-admin`):

- `router/routeIcons.ts` — curated mdi icon map keyed by route `name`
  for the 42 built-in admin pages (top-level modules + 38 sub-routes:
  identity / authorization / system / storage / audit / notification /
  chat / payment / ai / template). Picks follow soybean's outline
  vocabulary so a paste-side-by-side review feels consistent.
- `useAdminRouteStore.toMenuItem` now resolves `meta.icon` first and
  falls back to the new `DEFAULT_ROUTE_ICONS` map — consumers can still
  override per-route by setting `meta.icon` themselves.
- `TAdminSidebar.brand` + `brandIcon` props. The sider's `#header` slot
  now defaults to `<TSystemLogo>` showing the brand title + icon,
  collapsing to icon-only when the sider is folded. Consumer can still
  override the entire header via `<template #sider-header>`.
- `TAdminShell.sider.brand` + `sider.brandIcon` config fields, plumbed
  through to `TAdminSidebar`.
- `AdminShellRoot` wires `useAdminLoginConfig().brand` / `brandIcon`
  through to the sider header, so the login page and sidebar share the
  same brand identity without duplicate configuration.

**Changed** (`@tnzi/ui-admin`):

- `TAdminSidebar` active-menu signal: was `tabStore.activeTabId` (only
  updated on tab open), now `useRoute().name` directly — so the active
  row highlights on initial page load. Wrapped in a router-context
  guard so isolated unit-test mounts (no router installed) gracefully
  fall back to "no active route".
- `TAdminSidebar` auto-expands the active item's ancestor groups on
  route change (`expandedKeys` is now controlled with an immediate
  watcher), so landing on `/admin/identity/users` opens the `identity`
  group without a manual click. User toggles are preserved.
- `TAdminSidebar` NMenu CSS overhauled to match soybean: `--n-item-height: 44px`,
  `--n-item-color-hover: rgb(var(--tnzi-primary-rgb) / 0.06)`,
  `--n-item-color-active: rgb(var(--tnzi-primary-rgb) / 0.1)`. Active
  row gets a 3px primary left-border via a `::before` pseudo-element,
  matching soybean's signature accent.
- `TAdminSidebar.t-admin-sidebar__header` no longer renders a bottom
  border (logo-to-menu transition is seamless in soybean).
- Menu options now carry an `icon` render function pointing to
  `TSvgIcon` so icons appear left of each label.

**Cross-repo** (reference consumer):

- `admin/vite.config.ts` adds `resolve.alias` + `resolve.dedupe` for
  `vue` / `vue-router` / `pinia` / `naive-ui` so the pnpm `link:` to
  the ui-admin package doesn't load duplicate copies of those modules.
  Without this, ui-admin's internal `useRoute()` reads from a router
  that the consumer never installed (always returns `name: undefined`),
  which silently breaks active-menu highlighting and parent expansion.

**Test fixes**:

- `TAdminSidebar` test "binds active value to tabStore.activeTabId"
  renamed + rewired to assert the new no-router fallback (empty
  active key). The `useAdminTabStore` import is gone.

Tests: 659 (`@tnzi/ui-admin`) — green.

Visual verification (Chrome side-by-side against localhost:9527):

- Sidebar header shows the brand logo + title.
- Each menu row has its mdi icon.
- Active row (User Management when on `/admin/identity/users`) has
  the purple wash + 3px left-border accent.
- Active row's parent group (Identity) is auto-expanded on first load.

Remaining post-login layout gaps (header user dropdown, breadcrumb,
tab bar, footer copyright, default dashboard page) stay open for
Phase I.7.7-I.7.9.

## [0.2.26] — 2026-05-15 (`@tnzi/ui-admin` Phase I.7.2 visual polish: WaveBg blobs + naive theme override + default switches)

Driven by side-by-side comparison with `localhost:9527` (soybean demo):
three visual gaps the I.7.2 commit shipped wrong, all fixed here.

**Breaking** (`@tnzi/ui-admin`):

- `TWaveBg` API replaced. Was `{ height?, reverse?, opacity? }` with three
  horizontal sine-wave layers; now `{ themeColor? }` with two organic SVG
  blob paths (top-right + bottom-left) gradient-filled from the theme
  primary. Source-fidelity port of
  `soybean-admin-example/src/components/custom/wave-bg.vue` — same path
  data, same offsets, same gradient stops via `getPaletteColorByNumber`.
  Consumers of `TWaveBg` that relied on the old props get a clean blob
  fill instead — the old sine layers are gone for good.
- `TWaveBg` unit tests updated to the new 2-SVG shape + gradient-stop
  assertion (3 deletions, 2 additions; net 661 → 659 ui-admin tests).

**Changed** (`@tnzi/ui-admin`):

- `TLoginPage` toolbar slot now renders a `TThemeSchemaSwitch` +
  `TLangSwitch` pair by default (was empty). Consumer can still override
  via `<template #toolbar>`. This brings parity with soybean's
  `index.vue` header — the slot is no longer "empty by default but
  consumer-supplied".
- `TLoginPage` no longer applies the `--t-login-wave-tint` inline CSS
  var to `TWaveBg` (the prop is now `themeColor` and reactive).

**Cross-repo** (reference consumer):

- `admin/src/App.vue` now consumes `useTheme()` from `@tnzi/ui` to
  forward the admin theme context to `NConfigProvider`'s
  `:theme-overrides` (without this, `NButton type="primary"` etc.
  fell back to naive's default green `#18a058` instead of the admin
  signature purple `#646cff`). Also adds `NLoadingBarProvider` +
  `NNotificationProvider` so the matching primitives work everywhere.

**Visual verification** (Chrome side-by-side against localhost:9527):

- Login page now matches soybean's centered card on the blob-tinted
  background, with purple primary on Sign in / Admin demo buttons, and
  theme/language switchers in the top-right corner of the card.
- Admin shell post-login (`/admin/identity/users`) inherits the purple
  primary (Create button, pagination, etc.). Sidebar / Header / Tabs /
  Breadcrumb visual gaps vs soybean stay open for Phase I.7.6-I.7.9.

Tests: 661 → 659 (`@tnzi/ui-admin`) — net -2 from TWaveBg API breakage,
all green; visual checked via Chrome devtools.

## [0.2.25] — 2026-05-15 (`@tnzi/ui-admin` Phase I.7.2: PwdLogin wired + unocss adopted)

Closes the gap user feedback identified: `@tnzi/ui` and `@tnzi/ui-ai` both
adopt unocss, but `@tnzi/ui-admin` was still BEM-only. This patch wires
unocss in (vite plugin + `uno.config.ts` + `import 'virtual:uno.css'` in
the root entry), then rewrites `TLoginPage` and the new `PwdLogin`
module to use the same atom vocabulary soybean-admin-example does
(`flex-y-center` / `justify-between` / `gap-12px` / `text-28px` /
`text-primary` / `rd-12px`). The atoms get pre-compiled into
`dist/style.css` — consumers don't need to install unocss themselves.

**Added** (`@tnzi/ui-admin`):

- `uno.config.ts` — mirrors `@tnzi/ui` preset (presetWind4 + presetIcons),
  plus soybean-style shortcuts (`flex-y-center`, `i-flex-col`).
- `unocss` devDependency (^66.6.8) — matches `@tnzi/ui`.
- `src/index.ts` now imports `'virtual:uno.css'` so the atoms ship in
  the compiled bundle.
- `useAdminLoginConfig()` composable + `ADMIN_LOGIN_CONFIG_KEY` injection
  key + `AdminLoginConfig` type. Consumers configure the built-in login
  route via `defineAdminApp({ login: { brand, callbacks, demoAccounts } })`.
  The route component (`pages/login/index.vue`) injects the config and
  forwards it to `TLoginPage`.
- `TnziUiAdminOptions.login` field on `createTnziUiAdmin()` — same
  config shape, for consumers using the plugin factory directly instead
  of `defineAdminApp`.
- `DefineAdminAppOptions.login` field on `defineAdminApp()` factory.
- `LoginDemoAccount` type (replaces the inline `DemoAccount` exported
  by the deprecated `TAdminLoginCard`).

**Changed** (`@tnzi/ui-admin`):

- `TLoginPage.vue` — entire BEM stylesheet (~140 lines of scoped CSS)
  replaced with unocss atoms matching the soybean reference 1:1. Brand
  header + module label use `text-28px text-primary font-500` /
  `text-18px text-primary font-500`. NCard width `w-400px lt-sm:w-300px`.
  Layout `flex-center min-h-screen flex-y-center justify-between gap-12px`.
- `PwdLogin.vue` — fully wired:
  - NForm + NFormItem (path: userName / password) using `useFormRules` +
    `useNaiveForm` from `@tnzi/ui-admin/headless`.
  - Calls `useLoginContext().callbacks.pwdLogin({ userName, password, remember })`.
  - "Forgot password?" link calls `toggleLoginModule('reset-pwd')`.
  - "Code login" / "Register" secondary buttons call
    `toggleLoginModule('code-login' | 'register')`.
  - "Other Account Login" divider + demo account quick-fill buttons
    (auto-fills + submits via `handleAccountLogin`).
  - Error display under the form when callback rejects.
  - Hidden when `demoAccounts` is empty.
- `TLoginPage` test selectors switched to `data-test="…"` attributes so
  the BEM→atom rewrite doesn't churn tests.
- `LoginContext.demoAccounts` added — `TLoginPage` injects, `PwdLogin`
  consumes.

**Cross-repo**:

- The reference app's `admin/src/main.ts` now configures the built-in login via
  `defineAdminApp({ login: … })` instead of overriding the route
  component. `pages/LoginPage.vue` deleted. Smoke-tested: Admin demo
  account quick-fill → `/auth/login-with-refresh-token` → JWT issued →
  redirect to `/admin/identity/users` works end-to-end.

Tests: 661 (`@tnzi/ui-admin`) — unchanged; `data-test` attribute switch
preserved coverage. Visual verified via Chrome devtools.

## [0.2.24] — 2026-05-15 (`@tnzi/ui-admin` Phase I.7.1: login page router-param shell + 5 module scaffold)

Opening commit of Phase I.7 — the pixel-perfect soybean replication
push. `TLoginPage` was reworked from a `centered`/`split` variant
toggle into a **router-param driven shell** that mirrors
`soybean-admin-example/src/views/_builtin/login/index.vue` 1:1.

**Breaking** (`@tnzi/ui-admin`):

- `TLoginPage` props re-shaped. The `variant` (`centered`/`split`)
  toggle, `tagline`, `cardTitle`, `cardSubtitle`, `demoAccounts`,
  `enableCodeLogin`, `defaultUserName`, `defaultPassword`, `onLogin`,
  `translate` (single-arg signature) props are all removed. The new
  shape: `{ module?, moduleComponents, brand?, brandIcon?,
  brandIconSize?, translate? (two-arg with fallback), callbacks?,
  moduleLabels?, onToggleModule?, transitionName? }`. The single
  retained layout matches soybean's centered card on a brand-tinted
  background with `TWaveBg` underneath.
- `TLoginPageVariant` type export removed.
- `/login` route definition in `defaultAdminRoutes` is now
  `/login/:module(pwd-login|code-login|register|reset-pwd|bind-wechat)?`.
  Consumers that used path-equality match on `'/login'` should switch
  to name match on `'login'` (the route `name` is unchanged).
  `defineAdminApp({ loginComponent })` now resolves the override by
  name, so the public consumer-facing API is unchanged.

**Added** (`@tnzi/ui-admin`):

- `pages/login/index.vue` — route component that reads
  `route.params.module`, validates against the known 5-module set,
  and forwards to `TLoginPage` along with `router.replace`-based
  `toggleLoginModule`.
- `pages/login/modules/{PwdLogin,CodeLogin,Register,ResetPwd,BindWechat}.vue`
  — five placeholder modules. Phase I.7.2–I.7.5 will replace each
  placeholder with the real soybean-parity form wired to the
  `Tnzi.Identity` controller endpoints (`/auth/login-with-refresh-token`,
  `/auth/code-login/send-code` + `/auth/code-login`, `/auth/register`
  or `/auth/quick-register` + `/auth/quick-register/send-code`,
  `/auth/password-recovery/send-code` + `/auth/password-recovery/reset`,
  `/auth/verify-2fa` + `/auth/send-2fa-code`).
- `pages/login/useLoginContext.ts` — provide/inject channel between
  `TLoginPage` and the modules. Exposes `translate(key, fallback)`,
  `toggleLoginModule(name)`, and a `callbacks` bag of Promise-returning
  auth functions. Modules call this; the shell provides; the route
  page wires the consumer's HTTP client to the callbacks.
- Top-level `@tnzi/ui-admin` exports: `TnziAdminLoginPage`,
  `PwdLoginModule`, `CodeLoginModule`, `RegisterModule`,
  `ResetPwdModule`, `BindWechatModule`, plus context types
  (`LoginContext`, `LoginModule`, `LoginCallbacks`,
  `PwdLoginPayload`, `CodeLoginPayload`, `RegisterPayload`,
  `ResetPwdPayload`, `SendCodePayload`).

**Added** (`@tnzi/ui` 0.2.3):

- `mixColor(from, to, ratio)` exported from `@tnzi/ui/theme/palette`.
  Linear blend using colord's mix plugin, with ratio clamping. Soybean
  uses `mixColor('#ffffff', themeColor, 0.2)` (dark: 0.5) to derive the
  login page background wash; `TLoginPage` adopts the same recipe.

Tests: 660 → 661 (`@tnzi/ui-admin`); 444 → 449 (`@tnzi/ui`, +5 for
`mixColor`).

Coordination: Phase I.7 follow-up brief
(`memory/phase-i7-pixel-perfect-replication-brief.md`) covers I.7.2
through I.7.12 — they will land in subsequent commits in fresh
sessions per the brief's "do not stuff one session" rule.

## [0.2.23] — 2026-05-15 (`@tnzi/ui-admin` Phase I.6.7: dark mode transition + mix layout hover)

Closing patch for Phase I.6 — two polish-grade additions:

- **`TDarkModeContainer` smooth transition.** New `transition` prop
  (defaults to `'smooth'`) wraps the local theme flip in a 300 ms
  crossfade on background / color, plus a 250 ms cascade for child
  borders and backgrounds. Pass `transition="none"` for full-page
  hard-flips where animation would feel sluggish.
- **Mix layout hover-slide primitive.** New `.t-mix-hover-trigger` /
  `.t-mix-hover-trigger__sub` CSS pair in `styles/transition.css`. The
  narrow first-level rail (90 px, `mixSiderWidth`) expands a hidden
  second-level panel from 0 → `--tnzi-admin-sider-mix-child-width`
  (200 px) on hover / focus / a toggled `--expanded` class. Uses a
  spring ease curve (`cubic-bezier(0.16, 1, 0.3, 1)`) for the soybean
  feel. Consumer wires the `.t-mix-hover-trigger__sub` content to
  whatever second-level menu they render — the primitive is purely
  presentational so adopters can A/B different render strategies.

Tests: 658 → 660 (2 new — transition class default + opt-out, both on
TDarkModeContainer).

## [0.2.22] — 2026-05-15 (`@tnzi/ui-admin` Phase I.6.6: theme presets + radius + import/export)

This patch lays the foundation for the eventual 5-tab → 4-tab Theme
Drawer rework. The visual drawer rewrite is intentionally deferred
because the existing 721-line TThemeDrawer covers the same surface
and rewriting it well exceeds a single patch's reasonable scope; this
patch adds the **data** the new tabs will need so that swap can land
later as a pure UI shuffle.

- **Four built-in presets** in `src/presets/index.ts`, exported from
  `@tnzi/ui-admin` and the `/presets` subpath:
  - `default` — soybean violet (#646cff), 6 px radius, light scheme
  - `dark` — same palette, dark scheme, 8 px radius
  - `compact` — tighter sider / header / tab heights, 4 px radius
  - `azir` — cyan brand variant (#0ea5e9), 8 px radius
- **`themeRadius` in `useAdminThemeStore`** — 0-16 px clamp, persists
  to LocalStorage, syncs `--tnzi-admin-radius` CSS variable on every
  set so cards / inputs / buttons pick it up live.
- **`applyPreset(preset)`** action on the theme store — patches
  radius, sider widths, header / tab heights, and page transition
  from a `ThemePreset` shape. Color values live in `@tnzi/ui`'s base
  theme store; consumers wire `themeStore.setPrimary(preset.primaryColor)`
  separately to keep the two layers decoupled.
- **`exportThemeConfig(snapshot) / importThemeConfig(json)`** helpers —
  versioned JSON wrapper (`version: 1`) with download-ready shape;
  throws on malformed / version-mismatched payloads. Consumer typically
  pairs them with a file picker / blob download to expose import /
  export buttons in the (future) Preset tab.

Tests: 646 → 658 (12 new across preset shape sanity, applyPreset side
effects, setThemeRadius clamping, and import/export roundtripping).

## [0.2.21] — 2026-05-15 (`@tnzi/ui-admin` Phase I.6.5: Tab enhancement + KeepAlive RouterView)

`TAdminTabs` already shipped drag-reorder + 5-option context menu
(close current / left / right / others / all) + middle-click close, so
this patch focuses on the missing piece: a drop-in
**`TAdminRouterView`** that wires up KeepAlive + Transition the way
soybean-admin does:

- `<KeepAlive>` with `:include="visibleRouteNames"` + `:max="20"`. Each
  route opt-ins via `meta.keepAlive` (default true) and opts out by
  setting `meta.keepAlive: false` or appearing in the `exclude` prop.
  Anonymous routes (no `name`) are always skipped.
- `<Transition>` using whatever `useAdminThemeStore().pageTransition`
  reports (six built-ins, prefixed `tnzi-`). Respects `pageAnimate`.
- `appStore.reloadFlag` reactivity — clicking the header reload
  button unmounts the inner component and immediately re-mounts it,
  blowing the cache for that slot. The `v-if="reloadFlag"` guard is
  enough; KeepAlive picks up the remount automatically.

Drop into TAdminContent's slot:

    <TAdminContent>
      <TAdminRouterView :exclude="['login', '403', '404']" />
    </TAdminContent>

Tests: 640 → 646 (6 new — smoke / cache navigation / `meta.keepAlive:
false` exclusion / anonymous-route skip / `exclude` prop / reloadFlag
unmount-remount cycle).

## [0.2.20] — 2026-05-15 (`@tnzi/ui-admin` Phase I.6.4: Dashboard content expansion)

Three additions that lift the default dashboard from "scaffold" to
something an operations team would actually leave open all day:

- **`KpiCard.gradient`** — new field accepts `{ start, end }` and renders
  the whole card as a 135deg gradient tile (white text, no icon chip).
  Matches soybean's quartet of red→purple / purple→blue / cyan→blue /
  yellow→red KPI cards. The legacy `tone` path stays intact when
  `gradient` is unset.
- **`KpiCard.unit`** — optional prefix shown before the TCountTo number
  (e.g. `$`, `€`, `¥`). Renders at 18 px / weight 500 so it stays a
  modifier rather than competing with the value.
- **`<slot name="header">`** on `TDashboardPage` — typically populated
  with a `THeaderBanner` (see below) so consumers can drop in their
  welcome ribbon without restructuring the page.
- **`THeaderBanner`** (new, `components/dashboard/`) — welcome banner
  with auto time-of-day greeting (`morning` 5-11, `afternoon` 12-17,
  `evening` 18-22, `night` 23-4), live ticking datetime, optional
  subtitle motto, and an `illustration` slot. Pure presentational —
  consumer wires `userName` from auth store.
- **`TProjectTimeline`** (new, `components/dashboard/`) — naive-ui
  NTimeline wrapper for activity feeds. Items take title / description /
  time / icon / tone — soybean's "project news" card pattern in 30 lines.

All three exports are reachable from the package root and the
`/components` subpath. Tests: 630 → 640 (10 new across the three).

## [0.2.19] — 2026-05-15 (`@tnzi/ui-admin` Phase I.6.3: utility component suite)

Seven small, focused components that headers / toolbars / brand-areas /
login-pages all reach for. Each lives in `src/components/utility/`,
exports from the package root, and stays presentational (no store
coupling) so consumers can wire them into vue-i18n / theme stores /
router however they like:

- **`TThemeSchemaSwitch`** — cycle Light → Dark → Auto, optionally toggle
  the `dark` class on `<html>`. Controlled or uncontrolled.
- **`TLangSwitch`** — naive-ui NPopselect-backed locale picker; defaults
  to en + zh-CN, overridable via `options`.
- **`TFullScreen`** — Fullscreen API toggle with state-tracking icon
  flip. Falls back to a no-op on browsers that reject the request.
- **`TReloadButton`** — refresh icon with a 360-degree spin animation.
  Accepts an async `onReload` callback (awaits it) or fires-and-forgets
  with a short visual confirmation.
- **`TPinToggler`** — pin / unpin used by mix-layout sub-sider to lock
  the second-level drawer open.
- **`TMenuToggler`** — collapse / expand sidebar; icon flips with state.
- **`TSystemLogo`** — brand mark + caption with three layouts (full /
  icon-only / stacked) for sidebar / collapsed / login use.

Each component is a tiny wrapper over the existing `TButtonIcon` /
`TSvgIcon` primitives, so they pick up the recent NButton restoration
fix and theme tokens automatically.

Tests: 615 → 630 (15 new across the seven components covering
mount / click / emit / layout-mode / pinned-state / async-callback
scenarios). The fall-through `class` attribute is not tested on
TButtonIcon-based components — NTooltip's binder eats the class on its
trigger wrapper, so tests focus on emit + DOM presence instead.

## [0.2.18] — 2026-05-15 (`@tnzi/ui-admin` Phase I.6.1: visual polish + route progress + NButton restoration)

Three targeted fixes / additions to back the new TLoginPage:

- **NButton restoration.** Tailwind / UnoCSS preflight emits
  `button, [type="submit"], … { background-color: transparent }` with
  specificity `(0,1,0)`, which ties with `.n-button { background-color:
  var(--n-color) }` and wins by cascade order — so every primary / info /
  success / warning / error NButton rendered as a transparent outline with
  white text on white surfaces, basically invisible. Added a `.n-button.n-
  button` double-class rule (specificity `(0,2,0)`) in `styles/polish.css`
  restoring the var-driven background / border for default / hover /
  pressed / focus / disabled states. Applies app-wide so login, dialog,
  popover, and admin-shell buttons all recover at once.
- **Layout content background tint.** `--tnzi-admin-content-bg` now mixes
  5 % primary into the layout background (`color-mix(in srgb, …)`), so
  cards lift off the surface the way soybean's content area does instead
  of sitting on a flat grey.
- **Route progress bar.** New `useRouteProgress(router)` headless +
  pure-CSS bar (no `nprogress` dep). The composable toggles
  `<html data-tnzi-route-loading>` around each navigation, and a
  global `html::before` rule grows a 2 px primary-tinted bar across the
  top of the viewport during nav, fading out two animation frames after
  the route resolves. `defineAdminApp().install(app, pinia, router?)`
  picks it up automatically when the router is passed in.
- New variables in `styles/variables.css`:
  `--tnzi-admin-nprogress-color`, `--tnzi-admin-nprogress-height`.

Tests: 611 → 615 (4 new for `useRouteProgress` covering set / clear /
idempotent / concurrent cases).

## [0.2.17] — 2026-05-15 (`@tnzi/ui-admin` Phase I.6.2: TLoginPage centered variant + form rules)

Reworked `TLoginPage` to default to the soybean-admin **centered** layout (logo
+ title stacked above a single card, WaveBg footer, optional top-right
toolbar slot for theme/lang switchers). The previous split-pane layout is
retained behind `variant="split"` so existing consumers can opt in.

`TAdminLoginCard` now:

- exposes a `variant` of its own (`'page'` = full-page wrapper, `'standalone'`
  = bare card; the page wraps it as `'standalone'` to avoid double chrome);
- moves validation to `NForm` `:rules` (inline field errors, no manual
  `errorText` state) — easier to extend with custom rules;
- ships the button as `round size="large"` (44 px) and bumps the subtitle to
  18 px / weight 500 for the marquee headline feel.

### Note on version history

`0.2.6` through `0.2.16` shipped without CHANGELOG entries; the work is tracked
in memory (`2026-05-15-ui-admin-soybean-parity.md`) and the corresponding
commits. Future Phase I.6.x patches will document their changes here.

## [0.2.5] — 2026-05-15 (`@tnzi/ui-admin` Phase A.5: visual polish, CSS var alignment)

Patch inserted before Phase C after a live consumer flagged the
"basically no visible change" issue. Phase A's drawer + layout work was
landing in source but the rendered components looked identical to pre-A
because of two compounding bugs:

1. **Critical theme-context-missing bug (already shipped in `7bf20274`)** —
   `AdminShellRoot` was eagerly mounting `TThemeDrawer`, which calls
   `useTheme()` from `@tnzi/ui`. Consumers that don't install
   `createTnziUi()` (consumer pattern: uses Naive UI directly + only
   `@tnzi/ui-admin`) hit "no theme context found" during drawer setup, the
   whole route subtree silently failed, and none of the Phase A features
   reached the DOM. Fix landed before this release: `createTnziUiAdmin()`
   now provides a fallback theme context when none is already provided.
2. **CSS variable name mismatch** — `@tnzi/ui` injects `--tnzi-primary`,
   `--tnzi-base-text`, `--tnzi-container-bg`, `--tnzi-border`,
   `--tnzi-shadow-*`. But `@tnzi/ui-admin` components (TAdminShell, Sidebar,
   Header, Tabs, Breadcrumb, Footer, GlobalSearch, ThemeDrawer, LayoutModeCard)
   referenced `--tnzi-primary-color`, `--tnzi-text-color-1`, `--tnzi-sider-bg`,
   `--tnzi-surface`, `--tnzi-border-color`, `--tnzi-hover-bg`,
   `--tnzi-error-color` etc — none of those exist. CSS variables silently
   resolved to undefined and the fallback colors took over inconsistently:
   sidebar background went transparent, hover states didn't show, shadows
   never rendered, "样式无法识别".

### Packages bumped

| Package          | From  | To    |
|------------------|-------|-------|
| `@tnzi/ui-admin` | 0.2.4 | 0.2.5 |

### Fixed — `@tnzi/ui-admin`

- **CSS variable name alignment** across 9 layout components: 19 wrong-name
  references replaced with their `@tnzi/ui`-injected counterparts. Components
  now correctly receive theme colors, hover backgrounds, and shadow tokens
  reactively when the user picks a color in the Settings Drawer.

### Changed — `@tnzi/ui-admin`

- **Emoji icons → Iconify SVG** in `TAdminHeader`: ☰ → `mdi:menu`/`mdi:menu-open`,
  🔍 → `mdi:magnify`, ↻ → `mdi:refresh`, ⤢/⤡ → `mdi:fullscreen`/`fullscreen-exit`,
  🎨 → `mdi:palette-outline`, lang button now combines `mdi:translate` + text label.
  Buttons standardized as 36×36 rounded squares with consistent hover + active
  states pulling from `--tnzi-primary-100` and `--tnzi-primary`.
- `TAdminHeader` adds `box-shadow: var(--tnzi-shadow-header, ...)` and tighter
  spacing (gap 8 → 12px) matching soybean-admin's chrome.
- `TAdminSidebar` gains `box-shadow: var(--tnzi-shadow-sider, ...)`, a
  16px-padded header row matching the header height (56px), 8px vertical
  padding on body, and 6px-rounded NMenu items with 2/8px margins for
  proper "pill" hover affordance.

### Added — `@tnzi/ui-admin`

- `@iconify/vue@^5.0.0` as a runtime dependency (matches `@tnzi/ui`'s version).

### Test coverage

- 536 tests still pass — test selectors preserved by keeping the original
  per-button CSS class names (`__toggler`, `__search`, `__reload`,
  `__fullscreen`, `__lang`, `__theme`) alongside the new shared
  `__icon-btn` class.

### Live consumer validation

The reference admin app (fresh dev) shows:
- 6 SVG icon buttons rendered (was 0 SVG + 5 emoji)
- Sidebar background `rgb(255, 255, 255)` (was `rgba(0,0,0,0)` transparent)
- Sidebar shadow `2px 0 8px rgba(29,35,41,0.05)` (was `none`)
- Header shadow `0 1px 4px rgba(0,21,41,0.08)` (was `none`)
- 70 `--tnzi-*` CSS variables correctly injected on `<html>`

## [0.2.4] — 2026-05-15 (`@tnzi/ui-admin` Phase B: module manifest + defineAdminApp)

Second patch of the `@tnzi/ui-admin` 0.2.x overhaul. Phase B ships the
user-facing extensibility milestone — backend-driven module discovery plus
a `defineAdminApp()` factory that compresses ~110 lines of consumer
bootstrap boilerplate into ~10 lines.

### Packages bumped

| Package          | From  | To    |
|------------------|-------|-------|
| `@tnzi/ui-admin` | 0.2.3 | 0.2.4 |

### Added — backend (`Tnzi.AspNetCore`)

- **`GET admin/diagnostics/admin-manifest`** — new diagnostics endpoint
  returning `AdminManifestDto`: per loaded module, the admin controllers it
  exposes with route + HTTP methods + `IsDefault` + `HasFullCrud` flags.
  Designed for frontend menu auto-generation.
- `AdminManifestDto`, `AdminModuleEntryDto`, `AdminEntityEntryDto` —
  frontend-shaped DTOs in `Tnzi.AspNetCore.Dtos`. Existing
  `GET admin/diagnostics/modules` endpoint untouched (backward-compat).

### Added — `@tnzi/ui-admin`

- **`defineAdminApp({...})` factory** in `@tnzi/ui-admin/plugin`:
  - `hideModules` / `showOnlyModules` — case-insensitive filter on the
    second-level (module) children of `/admin`. Hidden modules' routes are
    stripped wholesale.
  - `overridePages` — map of `routeName → Component` that swaps the
    `component` field deep in the tree without touching `meta` / permission
    checks / keepAlive.
  - `addModules` — append consumer-supplied `RouteRecordRaw[]` under
    `/admin` for business-specific pages.
  - `loginComponent` / `forbiddenComponent` — replace the placeholder
    `/login` and `/403` routes.
  - `install(app, pinia)` — wraps `createTnziUiAdmin()` + seeds the admin
    route store from the filtered routes so `TAdminSidebar` renders the
    menu out of the box (no consumer hydration code needed).
- **`useAdminModuleManifest()` composable** in `@tnzi/ui-admin/headless`:
  fetches the backend manifest, exposes filtered modules + a derived
  `menuTree` (collapses single-entity modules to leaf nodes).
- **`fetchAdminManifest(client)` service** in
  `@tnzi/ui-admin/services/admin-manifest` (re-exported from root) — bare
  fetch helper with safe null fallback when the endpoint is unavailable.

### Consumer impact

Existing consumers using `createTnziUiAdmin()` directly are unaffected
(no breaking changes). Consumers can opt into `defineAdminApp()` to
remove route-filtering / store-hydration / login-replacement boilerplate.

The reference `admin/main.ts` migration: ~110 lines → ~50 lines
(the residual ~50 lines is `vue-router` setup + `beforeEach` auth guard +
`pnpm link:` Vue typedef casts; those don't belong in the framework
factory).

### Test coverage

- `@tnzi/ui-admin` 513 → 536 tests (+23):
  - `admin-manifest` service: 4 tests (200 ok, no data, missing modules,
    network error)
  - `useAdminModuleManifest`: 8 tests (manifest verbatim, disabled-module
    filter, hideModules/showOnlyModules, 2-level menu, single-entity
    collapse, i18n key convention, refresh, isAvailable)
  - `defineAdminApp`: 10 tests (default tree, hide/showOnly, deep
    override, addModules, login/forbidden swap, install seeds route store,
    uninstall hook, case-insensitive normalization)
- Build green, vue-tsc green.

## [0.2.3] — 2026-05-15 (`@tnzi/ui-admin` Phase A: layouts + theme drawer)

First release of the `@tnzi/ui-admin` 0.2.x overhaul aimed at production-grade
admin UX (benchmark: soybean-admin). This patch ships Phase A — layout modes,
dual-level theme integration, and a 5-tab settings drawer. Other `@tnzi/*`
packages keep their `0.2.2` version (no source changes for them yet).

### Packages bumped

| Package          | From  | To    |
|------------------|-------|-------|
| `@tnzi/ui-admin` | 0.2.2 | 0.2.3 |

### Added — `@tnzi/ui-admin`

- **6 layout modes** in `useAdminThemeStore.layoutMode`: `vertical` (default),
  `horizontal`, `vertical-mix`, `vertical-hybrid-header-first`,
  `top-hybrid-sidebar-first`, `top-hybrid-header-first`. `TAdminShell`
  dispatches main-sider / sub-sider / top-menu rendering per mode.
- **`TAdminTopMenu` component** — horizontal NMenu hosted inside the header
  for `horizontal` (full nested) and the 3 hybrid modes (first-level only).
- **`TAdminWatermark` component** — `NWatermark` overlay wired to
  `useAdminThemeStore.watermark`. Default off; consumer apps enable via the
  Settings Drawer or by patching the store. Composes `{customText} · {userName}
  · {YYYY-MM-DD}` based on toggles.
- **`TLayoutModeCard` component** — visual SVG-style 4:3 preview card for each
  of the 6 layout modes; used by the Settings Drawer's Layout tab.
- **`useAdminThemeStore` new fields**: `mixSiderWidth`, `tabStyle`
  (chrome/button/slider), `pageAnimate`, `fixedHeader`, `fixedTab`,
  `fixedFooter`, `watermark` (enabled/text/includeUserName/includeDate/opacity/
  fontSize). All persisted via `pinia-plugin-persistedstate`.
- **`theme/admin-config.ts`** — snapshot serialization (`buildSnapshot`,
  `snapshotToJson`, `parseSnapshot`, `isValidSnapshot`,
  `copySnapshotToClipboard`, `downloadSnapshot`). Versioned (`v1`) so future
  schema changes can migrate.
- **`TAdminHeader` `#menu` slot** — new center region between left (logo +
  breadcrumb) and right (action buttons). Consumed by `TAdminShell` to inject
  `TAdminTopMenu` for horizontal & hybrid modes.

### Changed — `@tnzi/ui-admin`

- **`TThemeDrawer` rewritten with 5 tabs** (replaces flat section list):
  - **Appearance** — color mode (light/dark/auto), 12 preset color swatches +
    `NColorPicker` for each role (primary/info/success/warning/error). Reuses
    `@tnzi/ui`'s `useTheme()` context — no duplicate color state.
  - **Layout** — 6 mode cards, invert sider/header toggles, sider/header/tab
    sizing sliders, tab style selector, visibility & fixed-position toggles.
  - **General** — page transition on/off + transition mode selector.
  - **Watermark** — enable + text + includeUserName + includeDate + opacity +
    fontSize.
  - **Preset** — copy-to-clipboard, download .json, import from textarea,
    reset-all with confirmation popover.
- `TAdminShell` now reads `layoutMode`, `headerVisible`, `tabVisible`,
  `footerVisible`, `breadcrumbVisible`, `siderWidth`, `siderCollapsedWidth`,
  `headerHeight`, `pageTransition`, `fixedHeader`, `invertSider`,
  `invertHeader` from the store as defaults when props are not provided. This
  keeps the Settings Drawer wired-up out of the box.

### Test coverage

- `@tnzi/ui-admin` 488 → 513 tests (+25): store hybrid modes + watermark +
  tabStyle + pageAnimate + reset; `admin-config` round-trip + version guard;
  `TLayoutModeCard` render geometry + select emit; `TThemeDrawer` 5 tabs +
  snapshot + reset + translate prop.
- Existing locale + integration tests adjusted for the new `theme.*` i18n
  shape (no behavioral regression).

### Roadmap

Phase A is the first of 7 patches (0.2.3 → 0.2.9) bringing `@tnzi/ui-admin`
to production grade. Upcoming Phase B (`/admin/diagnostics/modules` driven
auto-discovery + `defineAdminApp({ hideModules, overridePages, addModules })`)
is the user-facing extensibility milestone. See
`memory/ui-admin-overhaul-plan.md` for the full 7-Phase roadmap.

## [0.2.2] — 2026-05-13 (five-package alignment + ui-ai polish)

The first release where all five `@tnzi/*` packages share the same
on-disk version. Reconciles the long-standing drift where the CHANGELOG
recorded `0.2.0-preview.1/2/3` releases that never actually bumped
`package.json` on `@tnzi/core`, `@tnzi/ui`, `@tnzi/ui-admin`,
`@tnzi/mobile`. The breaking changes described in those preview
entries (admin pages split, `@tnzi/core` per-call service factories,
theme tokens, etc.) are all already in the source tree — this release
just lets the manifest reflect reality.

Two `@tnzi/ui-ai` improvements ride along (both backward-compatible):

### Packages bumped

| Package          | From  | To    |
|------------------|-------|-------|
| `@tnzi/core`     | 0.1.4 | 0.2.2 |
| `@tnzi/ui`       | 0.1.3 | 0.2.2 |
| `@tnzi/ui-admin` | 0.1.3 | 0.2.2 |
| `@tnzi/ui-ai`    | 0.2.1 | 0.2.2 |
| `@tnzi/mobile`   | 0.1.3 | 0.2.2 |

### Added

- **`@tnzi/ui-ai` `useChat` — streaming integration API**: the composable
  now exposes `addMessage`, `updateMessage`, `appendDelta` and
  `setStreaming` from its return value. A consumer's SSE / WebSocket
  transport can feed deltas back into the reactive message list
  without re-implementing the message store. `appendDelta(id, field,
  delta)` is synchronous so high-frequency deltas never race or
  coalesce; `updateMessage` keeps the existing rAF-batched semantics.
  Covered by 7 new unit tests (useChat 15 → 22 tests; package total
  195 → 202 tests).

### Fixed

- **`@tnzi/ui-ai` themes barrel** — `import { applyAiTheme,
  lightTokens, darkTokens } from '@tnzi/ui-ai/themes'` now resolves
  to a real runtime export. Previously rollup tree-shake stripped the
  `export { … } from './tokens'` re-export line in `themes/index.ts`
  (since the named identifiers were not locally referenced), leaving
  `dist/themes/index.js` with only `applyTheme` + `resetTheme` while
  the `dist/themes/index.d.ts` still advertised the full set —
  typecheck succeeded, runtime imports got `undefined`. Fixed by
  declaring `themes` as a top-level entry in `packages/ui-ai/vite.
  config.ts` (matches the existing `chat`, `shell`, `utils`, etc.
  entries) and adding a `"./themes"` shortcut next to the existing
  `"./themes/*"` wildcard in `package.json#exports`.

### Notes

- No source-level breaking changes for any package. The five-package
  alignment is a manifest-only reconciliation; the breaking changes
  documented under `[0.2.0-preview.*]` above already landed in code
  long ago and consumers tracking `workspace:*` (which is most of
  this repo) are unaffected.
- The `@tnzi/ui-ai` `themes` exports field is additive; existing
  `@tnzi/ui-ai/themes/tokens` subpath imports keep working.

## [0.2.1] — 2026-05-13 (`@tnzi/ui-ai` only)

First **realized** version bump on disk for `@tnzi/ui-ai`. The earlier
`0.2.0-preview.1/2/3` entries in this file describe work that landed in the
source tree, but `packages/ui-ai/package.json` was never bumped past `0.1.3`
during those preview cycles. This release reconciles the manifest with the
shipped code.

The other four packages (`@tnzi/core`, `@tnzi/ui`, `@tnzi/ui-admin`,
`@tnzi/mobile`) intentionally remain on `0.1.x` for now — their preview-track
entries above are tracked as a known reconciliation item to be addressed
separately.

### Packages bumped

| Package        | From  | To    |
|----------------|-------|-------|
| `@tnzi/ui-ai`  | 0.1.3 | 0.2.1 |

### Added

- **`@tnzi/ui-ai/chat` — `TChatApp`** (~840-line SFC): a drop-in
  production-grade chat application shell with Manus-inspired visuals.
  Internally composes `TCollapsibleSidebar` + `TLandingPage` +
  `TThreadComposer` + `TReasoningStage` + `TArtifactPanel` +
  `TSettingsDialog` + `TCommandPalette`. Consumers wire data via
  `:threads` / `:messages` / `:is-streaming` / `v-model:input-text` and
  override visuals through 15+ slots (`#brand`, `#topbar-actions`,
  `#sidebar-content`, `#landing-chips`, `#composer-left`,
  `#settings-{custom-id}`, …). 20+ props · 13 events · 15+ slots.
- **Manus surface tokens** moved into `src/styles/index.css`: complete
  `--tnzi-ai-bg / text / text-secondary / text-tertiary / surface /
  border / border-strong / divider / accent / accent-soft / hover /
  press / selected / font-display / font-body / font-mono /
  duration-fast|base|slow / easing / composer-radius / composer-shadow /
  modal-radius / backdrop` palette so consumers no longer need to ship
  their own token CSS.
- **Critical Rule #0** added to `packages/ui-ai/CLAUDE.md`: new chat
  products should start with `TChatApp` and only drop down to
  `@tnzi/ui-ai/shell` + `@tnzi/ui-ai/components` when `TChatApp`
  cannot host the design.

### Removed (breaking)

- **`ChatLayout` family — 6 components hard-deleted**: `ChatLayout`,
  `ChatSidebar`, `ChatMain`, `ChatHeader`, `ChatArtifactPanel`,
  `ChatSettings`. The legacy basic layout is no longer exported from
  `@tnzi/ui-ai` or `@tnzi/ui-ai/chat`. See `MIGRATION.md` "0.2 —
  `ChatLayout` → `TChatApp`" for the upgrade path.

### Notes on SemVer

Under strict SemVer for the `0.x` line a public-export removal would
warrant a minor bump to `0.3.0`. We are using `0.2.1` here to match the
`Migration: ChatLayout → TChatApp (0.2.1)` section already documented in
`packages/ui-ai/CLAUDE.md` and to keep alignment with the unrealized
`0.2.0-preview.*` history. Consumers should treat this as a breaking
change despite the patch-level number.

### Verification

`pnpm install` resolves workspace links cleanly; `pnpm --filter
@tnzi/ui-ai build` + `typecheck` pass; the reference chat product
consumes `@tnzi/ui-ai` via `link:` and stays green
because the `TChatApp` migration in the consumer's chat shell shipped in
the same session (consumer `main` `111023c`).

## [0.2.0-preview.3] — 2026-04-14

Maintenance pre-release adding the `@tnzi/ui-ai/shell` component set.

### Packages bumped

| Package          | From               | To                 |
|------------------|--------------------|--------------------|
| `@tnzi/ui-ai`    | 0.2.0-preview.2    | 0.2.0-preview.3    |

### Added

- **`@tnzi/ui-ai/shell`** — new subpath export with three slot-driven shell components for composing a full chat application:
  - `TCollapsibleSidebar` — three-mode sidebar (expanded / icon rail / hidden) with mobile drawer auto-transition
  - `TCommandPalette` — Cmd+K action launcher with keyboard nav and hotkey binding, dependency-free substring + keyword matching
  - `TSettingsDialog` — two-column multi-section modal (section list + dynamic content slot)
- **Composables** — `useSidebarState`, `useCommandPalette`, `useSettingsDialog` (re-exported from both `@tnzi/ui-ai/composables` and `@tnzi/ui-ai/shell`).
- **Tests** — 35 new unit tests across the three composables.
- **size-limit budget** — `dist/shell.js` capped at 25 kB gzipped (current: 10.86 kB).
- **Playground rewrite** — `packages/ui-ai/playground` is now a high-fidelity chat application (deer-flow-inspired) playing 10 scripted mock scenarios (simple chat, deep reasoning, RAG lookup, code artifact, workflow DAG, skill invocation, multi-agent handoff, file attachments, message branching, embed widget). Old demo tabs (`ChatDemo`, `ComponentsDemo`, `WorkflowDemo`, `AdminDemo`, `EmbedDemo`) are removed — their content is covered by the scenario set.

### Notes

All three shell components and their composables are marked `@experimental`. The surface may iterate until 0.3.0. Shell is shipped as a dedicated library entry (`dist/shell.js`) so non-consumers see no bundle size regression.

## [0.2.0-preview.2] — 2026-04-14

Maintenance pre-release. Aligns all five packages on a single version, removes residue from the Phase 0–6 refactor, and tightens a handful of types that slipped through the original audit.

### Packages bumped

| Package          | From               | To                 |
|------------------|--------------------|--------------------|
| `@tnzi/core`     | 0.2.0-preview.2    | 0.2.0-preview.2    |
| `@tnzi/ui`       | 0.2.0-preview.1    | 0.2.0-preview.2    |
| `@tnzi/ui-admin` | 0.2.0-preview.1    | 0.2.0-preview.2    |
| `@tnzi/ui-ai`    | 0.2.0-preview.1    | 0.2.0-preview.2    |
| `@tnzi/mobile`   | 0.2.0-preview.1    | 0.2.0-preview.2    |

### Changed

- `@tnzi/ui-admin`: `pages/chat/ChatSession.vue` is now generic on `ChatSessionListItemDto` instead of `Record<string, unknown>`, removing three `as unknown as` escape hatches; the bridge already maps create/update DTOs internally.
- `@tnzi/ui`: `TSelect.vue` no longer casts its `options` prop through `as any`; the prop is already typed as `Option[]`.
- `@tnzi/ui-ai`: removed a 38-line `TODO`-tagged commented SSE block from `composables/useChat.ts` — streaming integration is intentionally delegated to the consumer via `onStreamStart` / `onStreamEnd` hooks.
- `@tnzi/mobile`: `CLAUDE.md` package-info version aligned with `package.json`.

### Removed

- `@tnzi/ui-ai`: dead `playground/tailwind.config.js` + `playground/postcss.config.js` (they imported a parent `tailwind.config.js` deleted in the UnoCSS migration). Playground devDeps `tailwindcss`/`postcss`/`autoprefixer` removed.

### Fixed

- Workspace version drift introduced when `@tnzi/core` was bumped to `0.2.0-preview.2` in isolation; consumers depending on the published versions (rather than `workspace:*`) now see a consistent set.

## [0.2.0-preview.1] — 2026-04-13

The first pre-release of the 0.2 line. Delivers the structural refactor planned in `.superpowers/specs/2026-04-12-ui-packages-production-readiness-design.md` across seven phases (0–6). Consumers upgrading from 0.1 should read [MIGRATION.md](./MIGRATION.md) first.

### Highlights

- **`@tnzi/ui-admin` preset** — new admin framework that wires routing, plugins, stores, bridges and layout in one call (`createTnziUiAdmin`).
- **Real API bridges** — 29 preset admin pages now hit real `@tnzi/core/services/*` contracts via per-module bridge factories instead of inline stubs (Phase 3).
- **13 AI admin pages** — Phase 5 migrates agents / threads / skills / workflows / providers / MCP / RAG / quota / persona / evaluations / usage pages from `@tnzi/ui-ai/admin` into `@tnzi/ui-admin`. The `@tnzi/ui-ai` package is now purely the chat + workflow editor surface.
- **Styling cleanup** — `@tnzi/ui-ai` Phase 4 removed all `cn()` utility calls and `dark:` Tailwind variants, consolidating onto `@tnzi/ui`'s theme variable system.
- **Coverage thresholds enforced** — 80% lines/statements, 70% branches across `@tnzi/ui`, `@tnzi/ui-admin`, `@tnzi/ui-ai` (function thresholds are 80% for `@tnzi/ui`, 60% for ui-admin/ui-ai with documented rationale).
- **Playwright E2E** — auth, admin CRUD, workflow lazy-load, chat themes and accessibility baseline specs (19 tests total across 5 specs).
- **Engineering baseline** — unified ESLint flat config, commitlint + simple-git-hooks, size-limit guardrail on all 3 UI packages.

### Packages bumped

| Package          | From    | To                 |
|------------------|---------|--------------------|
| `@tnzi/core`     | 0.1.2   | 0.2.0-preview.1    |
| `@tnzi/ui`       | 0.1.1   | 0.2.0-preview.1    |
| `@tnzi/ui-admin` | 0.1.0   | 0.2.0-preview.1    |
| `@tnzi/ui-ai`    | 0.1.0   | 0.2.0-preview.1    |
| `@tnzi/mobile`   | 0.1.0   | 0.2.0-preview.1    |

### Added

- **Phase 1** — new foundation:
  - `composables/{auth,data,feedback,form,theme}` subdirectory layout with typed public contracts
  - 5 component barrels (`auth`, `form`, `navigation`, `card`, `list`)
  - Plugin install flow (`createTnziUi`) augmented (not replaced) to preserve Phase 0 adapter/store wiring
- **Phase 2a** — `@tnzi/ui-admin` stores (`adminAppStore`, `adminUserStore`, `adminPermissionStore`) + i18n + theme preset wiring
- **Phase 2b** — full admin component set (28 tasks): `TAdminShell`, `TAdminHeader/Footer/Sidebar`, `TCrudPage`, `TCrudToolbar`, `TCrudColumnSetting`, `TFormModal`, `TBatchActions`, responsive breakpoint adapter, `defaultAdminRoutes` for the 29 preset pages
- **Phase 3** — 29 Phase 3 admin pages wired to real bridges: Identity (User/Role/Tenant/LoginLog/GdprRequests), Audit, System, Chat, Notification, Payment, Template, Storage, Authorization
- **Phase 4** — `@tnzi/ui-ai` styles migration: `styles/index.css` inherits from `@tnzi/ui/style.css`, consumes `--tnzi-ai-*` CSS variables, removes `cn()` + Tailwind `dark:` residue
- **Phase 5** — 13 AI admin pages migrated into `@tnzi/ui-admin`: `AgentList`, `AgentDetail`, `RunMonitor`, `SkillList`, `PersonaList`, `WorkflowEditor` (lazy-loaded via `defineAsyncComponent`), `RunViewer`, `KbManager`, `McpServerList`, `ProviderConfig`, `QuotaRules`, `UsageDashboard`, `EvalViewer`
- **Phase 6 testing** — 1663 unit tests + 20 E2E tests, coverage reports, a11y baseline via axe-core
- **Phase 6 engineering** — ESLint flat config, commitlint, size-limit, Playwright

### Changed

- `@tnzi/ui-admin/pages` now consumes bridges from `@tnzi/ui-admin/src/services/bridges/*` (not inline stubs)
- `TCrudPage` receives `useCrudPage` state via prop; previously inlined the state machine
- `@tnzi/ui-ai` components use `--tnzi-ai-*` CSS variables for theme tokens; no more Tailwind `dark:` classes
- Default controller route registration via `defaultAdminRoutes` helper
- Pinia stores use factory pattern (`useAuthStore`, `useAppStore`, `useUserStore` with `resetAuthRuntime` / `resetStoreRuntime` test helpers)
- `@tnzi/core` service factories are per-call (not singletons) — bridges must call factories lazily in each sub-contract method

### Removed

- `packages/ai/src/admin/` directory (13 pages migrated to `@tnzi/ui-admin`)
- 7 legacy scaffold pages (Phase 6.2e cleanup): `ChatManagement.vue`, `OrderManagement.vue`, `SubscriptionManagement.vue`, `StorageManager.vue`, `FunctionManagement.vue`, `PermissionManagement.vue`, `NotificationManagement.vue`
- Legacy top-level composables in `@tnzi/ui/src/composables/` (kept on disk for baseline test compat, excluded from coverage, no longer publicly exported)

### Fixed

- Phase 4 carryover: `@tailwindcss/typography` + `tailwindcss` missing from `@tnzi/ui-ai` devDeps — installed in Phase 6.6 so the playground can start
- 10 pre-existing `ui-admin` integration mount flakes (Chat/Notification/Payment/Template timeouts): root cause was 5s vitest timeout insufficient for cold-cache dynamic SFC import; raised `testTimeout` to 15s (Phase 6.2e)
- `@tnzi/core/http` `normalizeApiResult` now handles the `success` field correctly for camelCase API responses
- Auth-store `401` auto-refresh handles concurrent refresh requests with a mutex and avoids infinite loops
- GET request deduplication via in-flight promise cache (opt-in via `deduplicateGets`)

### Known issues / Phase 6 deferrals

- **Hook install on Windows** — `commit-msg` hook cwd/path handling is fragile under git-bash + pnpm; commitlint works manually (`pnpm commitlint --edit .git/COMMIT_EDITMSG`)
- **`@tnzi/ui-ai` bundle size** — 1.91 MB gzipped for the full barrel import because `@vue-flow/core` + `shiki` + `markdown-it` + `katex` are transitively pulled; consumers who don't import `WorkflowCanvas` should rely on subpath exports + tree-shaking, but full subpath export tightening is deferred to a dedicated ticket
- **`@tnzi/ui-ai` shadcn residue** — Phase 4 removed `cn()` calls + `dark:` variants, but 40 `.vue` files still hard-code shadcn Tailwind utility classes (`text-primary`, `bg-accent`, `text-muted-foreground`, `bg-background`, `hover:bg-accent/50` …) and `WorkflowEdge/Node/Connection` still inline `hsl(var(--primary))` / `hsl(var(--ring))`. The package retains `tailwindcss@^3.4`, `@tailwindcss/typography`, `tailwind.config.js` and `postcss.config.js` with the full shadcn color system. Phase 6.21 cleanup (Option A) deleted the orphan `primitives/index.ts` compat shim + `cn()` helper as low-hanging fruit; **full migration off shadcn Tailwind utility classes + `tailwind.config.js` rewrite is a multi-day post-0.2.0 backlog item**
- **A11y** — 3-4 critical/serious axe violation types per playground section remain (mostly Naive UI upstream: `aria-input-field-name`, `button-name`, `color-contrast`, `role-img-alt`, `aria-toggle-field-name`). Baseline ceiling enforced; further fixes are their own sprint
- **Bundle-split CI gate** — size-limit tracks current sizes but rollup chunk-split verification for `WorkflowCanvas` is a production-build concern (not Vite dev-mode)

## [0.1.2] — pre-Phase-6 baseline

Previous baseline. See git history for pre-0.2 changes.
