// Phase I.7.2+: ui-admin now ships precompiled unocss atoms in its
// `dist/style.css`. Importing the virtual stylesheet here ensures the atoms
// referenced by ui-admin's own components (e.g. TLoginPage / PwdLogin) end
// up in the build output so consumers get pixel-perfect rendering without
// installing unocss themselves.
import 'virtual:uno.css'

export * from './components'
export * from './headless'
export * from './pages'
export * from './stores'
// Real route table + auth/permission guards. (Replaced the legacy
// `./template` barrel, whose `defaultAdminRoutes` was an empty [] that
// shadowed the real 64-route table shipped here.)
export * from './router'
export * from './plugin'
export * from './presets'
// Phase J (0.2.71+): Workbench widget system. Exposes the WidgetDef
// protocol, TWorkbenchLayout / TWidgetCard, useWidget(Data), the 14
// built-in widgets, and the default deck preset.
export * from './widgets'
