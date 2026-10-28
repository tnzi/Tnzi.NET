import { describe, it, expect } from 'vitest'
// The components barrel is what the package root re-exports for every `T*`
// component (the root itself pulls in `virtual:uno.css`, which only the build
// provides); `registerAllComponents` derives its list from the same barrel.
import * as components from '../../src/components/index'
import { TnziUiResolver } from '../../src/resolvers/index'
import { TNZI_UI_COMPONENT_NAMES } from '../../src/resolvers/component-names'

/** The `T*` components `import { X } from '@tnzi/ui'` can actually bind. */
function exportedComponentNames(): string[] {
  return Object.entries(components)
    .filter(([name, value]) => /^T[A-Z0-9]/.test(name) && value != null && (typeof value === 'object' || typeof value === 'function'))
    .map(([name]) => name)
    .sort()
}

describe('TnziUiResolver', () => {
  it('resolves every T* component @tnzi/ui exports', () => {
    const resolver = TnziUiResolver()
    const names = exportedComponentNames()
    expect(names.length).toBeGreaterThan(50)
    for (const name of names) {
      expect(resolver.resolve(name), name).toEqual({ name, from: '@tnzi/ui' })
    }
  })

  // unplugin-vue-components asks the resolvers in order and takes the first
  // answer, so claiming a name this package does not export rewrites another
  // package's component (or the app's own) into an import that fails the build.
  it('leaves T-prefixed names this package does not export to other resolvers', () => {
    const resolver = TnziUiResolver()
    for (const name of ['TCrudPage', 'TAdminShell', 'TChatApp', 'TLoginForm', 'TMyWidget', 'Teleport', 'Transition']) {
      expect(resolver.resolve(name), name).toBeUndefined()
    }
  })

  // The list is hand-kept (the resolver cannot import the `.vue` barrel), so a
  // component added to or removed from the barrel must be added to or removed
  // from the list as well.
  it('lists exactly the components the barrel exports', () => {
    expect([...TNZI_UI_COMPONENT_NAMES].sort()).toEqual(exportedComponentNames())
  })
})
