// Phase I.7.2+: ui-admin now ships precompiled unocss atoms in its
// `dist/style.css`. Importing the virtual stylesheet here ensures the atoms
// referenced by ui-admin's own components (e.g. TLoginPage / PwdLogin) end
// up in the build output so consumers get pixel-perfect rendering without
// installing unocss themselves.
import 'virtual:uno.css'

// IMPORTANT: every folder barrel below is imported as `./<folder>/index`, NOT
// `./<folder>`. `vue-tsc` copies these specifiers verbatim into
// `dist/index.d.ts`, and vite's preserveModules build also emits a root-level
// chunk per subpath export (`dist/components.js`, `dist/headless.js`, ...).
// TypeScript resolves a bare `./components` as a FILE first and only then as a
// folder, so it would land on `dist/components.js` - a plain .js with no types
// under `allowJs: false` - and `export * from './components'` would contribute
// ZERO members. Consumers then get `TS2305: has no exported member 'TCrudPage'`
// on every component, headless hook, store and route helper, while the runtime
// import works fine (bundlers read dist/index.js, which is correct). Spelling
// `/index` skips the file probe and lands on `dist/<folder>/index.d.ts`.
// Verified with `tsc --traceResolution` from the in-tree Acme admin app.
export * from './components/index'
export * from './headless/index'
// i18n layer. `translatePageKey` & co. used to live under `pages/_shared/` even
// though ~45 components resolve labels through it, which made `./components`
// impossible to import without dragging in the page layer AND both locale
// dictionaries. The dictionaries are async chunks now (see i18n/messages.ts),
// so a consumer only downloads the language it renders.
export * from './i18n/index'
// North-American accounting presentation conventions as pure functions
// (parenthesised negatives, tabular figures, unambiguous dates). Public so
// consumer report builders, CSV writers and chart labels produce the same
// strings as the built-in finance pages.
export * from './utils/placeholders'
export * from './utils/finance-format'
export * from './pages/index'
export * from './stores/index'
// Real route table + auth/permission guards. (Replaced the legacy
// `./template` barrel, whose `defaultAdminRoutes` was an empty [] that
// shadowed the real 64-route table shipped here.)
export * from './router/index'
export * from './plugin/index'
export * from './presets/index'
// Workbench widget system: the WidgetDef protocol, TWidgetCard, the bundled
// built-in widgets (KPI strip, list, charts, timeline, quick actions +
// business tiles), and the default deck preset.
//
// These live under `components/widgets/` but are deliberately NOT part of the
// `components/index.ts` barrel: the business tiles call admin bridges, so
// folding them in would make `@tnzi/ui-admin/components` drag the service layer
// into any consumer that wanted one display component.
export * from './components/widgets/index'

// Bridge plumbing - envelope helpers (`ensureOk` / `unwrapResult`, re-exported
// from @tnzi/core) + the CRUD query/result adapters, surfaced at the package
// root so consumer bridges import the whole set from `@tnzi/ui-admin` instead
// of copying `services/_mappers` (which is otherwise not on the public surface).
// `CrudPageQuery` / `CrudPageResult` already reach the root via `./headless`.
export {
  ensureOk,
  unwrapResult,
  mapQueryToListRequest,
  mapResultToCrud,
  pagedResult,
  pageArray,
} from './services/_mappers'
export type { BridgeCrudContract } from './services/types'
// CRUD bridge factories - declare an endpoint base instead of hand-writing the
// per-resource unwrap/ensureOk plumbing (see services/defineCrudBridge.ts).
// Shared per-client file-URL resolver. Consuming apps need `reset` on logout so
// signed tokens do not survive an identity switch.
export { getFileUrlResolver, resetFileUrlResolver } from './services/file-url-resolver'
export { defineCrudBridge, defineChildBridge } from './services/defineCrudBridge'
export type { CrudBridge, CrudBridgeOptions, ChildBridge } from './services/defineCrudBridge'
