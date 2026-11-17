// @vitest-environment node
/**
 * Convention gate: `CONTROLS_ICON_NAMES` is every icon name written in the
 * files `@tnzi/ui-ai/controls` and `@tnzi/ui-ai/dialogs` reach.
 *
 * A page that uses only those entries bundles its icons from this list and then
 * turns remote icons off. A name the controls render that the list misses is a
 * blank square in that page, found by its users; one the list keeps after a
 * control stopped rendering it is weight for nothing. So the list is held to
 * the sources the way the package manifest (`src/icons/index.ts`) is.
 */
import { afterEach, describe, expect, it, vi } from 'vitest'
import { _api } from '@iconify/vue'
import { existsSync, readFileSync, statSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { ICON_PREFIXES } from '../../../../tools/icons/icon-manifest.mjs'
import { CONTROLS_ICON_NAMES, disableRemoteIcons } from '../../src/controls-icons'

const pkgRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const LIST = resolve(pkgRoot, 'src/controls-icons.ts')
const ICON_LITERAL = new RegExp(`(['"])((?:${ICON_PREFIXES.join('|')}):[a-z0-9][a-z0-9-]*)\\1`, 'g')

/** The files the controls and dialogs entries reach through runtime imports. */
function controlsGraph(): string[] {
  const files = new Set<string>()
  const queue = [resolve(pkgRoot, 'src/controls.ts'), resolve(pkgRoot, 'src/dialogs.ts')]
  while (queue.length) {
    const file = queue.shift()!
    if (files.has(file)) continue
    files.add(file)
    const source = readFileSync(file, 'utf8')
    const specifiers = [...source.matchAll(/(?:^|\n)\s*(?:import|export)(\s+type)?\s[^'"]*?from\s*['"](\.[^'"]+)['"]/g)]
      .filter((m) => !m[1])
      .map((m) => m[2]!)
    for (const specifier of specifiers) {
      const base = resolve(dirname(file), specifier)
      const next = [base, `${base}.ts`, `${base}.vue`, join(base, 'index.ts')].find((c) => existsSync(c) && statSync(c).isFile())
      if (next) queue.push(next)
    }
  }
  return [...files]
}

describe('CONTROLS_ICON_NAMES', () => {
  const written = new Set(
    controlsGraph()
      .filter((file) => file !== LIST)
      .flatMap((file) => [...readFileSync(file, 'utf8').matchAll(ICON_LITERAL)].map((m) => m[2]!)),
  )

  it('is every icon the controls and dialogs write, and only those', () => {
    expect(written.size).toBeGreaterThan(10)
    expect([...CONTROLS_ICON_NAMES]).toEqual([...written].sort())
  })

  it('is exported from the controls entry', () => {
    expect(readFileSync(resolve(pkgRoot, 'src/controls.ts'), 'utf8')).toMatch(/export \{ CONTROLS_ICON_NAMES, disableRemoteIcons \} from '\.\/controls-icons'/)
  })
})

// A page built from the controls alone turns remote icon fetching off without
// `@tnzi/ui-ai/icons`, which brings `@iconify/utils` and the whole manifest.
describe('disableRemoteIcons from the controls entry', () => {
  afterEach(() => vi.restoreAllMocks())

  it('replaces the default provider module with one that never queries the API', () => {
    const setModule = vi.spyOn(_api, 'setAPIModule')
    disableRemoteIcons()

    expect(setModule).toHaveBeenCalledTimes(1)
    const [provider, module] = setModule.mock.calls[0]!
    expect(provider).toBe('')
    expect(module.prepare('', 'lucide', ['house', 'x'])).toEqual([])
    const callback = vi.fn()
    module.send('https://api.iconify.design', { type: 'icons', provider: '', prefix: 'lucide', icons: ['house'] }, callback)
    expect(callback).not.toHaveBeenCalled()
  })

  it('reaches nothing from the icons entry or @iconify/utils', () => {
    for (const file of controlsGraph()) {
      const source = readFileSync(file, 'utf8')
      expect(source, file).not.toMatch(/from\s*['"]@iconify\/utils/)
      expect(source, file).not.toMatch(/from\s*['"][./]+icons(?:\/index)?['"]/)
    }
  })
})
