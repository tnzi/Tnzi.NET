# Changelog

All notable changes to the `@tnzi/*` frontend packages are documented here. The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/).

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

The first pre-release of the 0.2 line. Delivers the structural refactor planned in `docs/superpowers/specs/2026-04-12-ui-packages-production-readiness-design.md` across seven phases (0–6). Consumers upgrading from 0.1 should read [MIGRATION.md](./MIGRATION.md) first.

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
