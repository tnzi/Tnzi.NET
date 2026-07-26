// Phase I.7.2+: ui-admin now ships precompiled unocss atoms in its
// `dist/style.css`. Importing the virtual stylesheet here ensures the atoms
// referenced by ui-admin's own components (e.g. TLoginPage / PwdLogin) end
// up in the build output so consumers get pixel-perfect rendering without
// installing unocss themselves.
import 'virtual:uno.css'

export * from './components'
export * from './headless'
// North-American accounting presentation conventions as pure functions
// (parenthesised negatives, tabular figures, unambiguous dates). Public so
// consumer report builders, CSV writers and chart labels produce the same
// strings as the built-in finance pages.
export * from './utils/placeholders'
export * from './utils/finance-format'
export * from './pages'
export * from './stores'
// Real route table + auth/permission guards. (Replaced the legacy
// `./template` barrel, whose `defaultAdminRoutes` was an empty [] that
// shadowed the real 64-route table shipped here.)
export * from './router'
export * from './plugin'
export * from './presets'
// Phase J (0.2.71+): Workbench widget system. Exposes the WidgetDef
// protocol, TWorkbenchLayout / TWidgetCard, useWidget(Data), the bundled
// built-in widgets (KPI strip, list, charts, timeline, quick actions +
// business tiles), and the default deck preset.
export * from './widgets'

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
export { defineCrudBridge, defineChildBridge } from './services/defineCrudBridge'
export type { CrudBridge, CrudBridgeOptions, ChildBridge } from './services/defineCrudBridge'
