// Phase I.7.2+: ui-admin now ships precompiled unocss atoms in its
// `dist/style.css`. Importing the virtual stylesheet here ensures the atoms
// referenced by ui-admin's own components (e.g. TLoginPage / PwdLogin) end
// up in the build output so consumers get pixel-perfect rendering without
// installing unocss themselves.
import 'virtual:uno.css'

export * from './components'
export * from './headless'
export * from './pages'
export * from './template'
export * from './plugin'
export * from './presets'
