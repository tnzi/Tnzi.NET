// @vitest-environment node
/**
 * Convention gate: `@tnzi/ui-ai/controls` reaches only `vue` and `@iconify/vue`
 * (plus `uqr`, a dependency of this package rather than a peer).
 *
 * The controls entry exists so that an app (or a desktop window) can build its
 * pages from the package's buttons, fields, dropdowns, switches and menus
 * without naive-ui, `@tnzi/ui` or `@tnzi/core`. One import of any of them,
 * however many files away (a headless helper that reaches for `useFocusTrap`,
 * a control that borrows a naive component), would quietly hand them back.
 * This walks the import graph from the entry, the way `vue-flow-isolation`
 * does, and names the path to any package it finds.
 */
import { describe, expect, it } from 'vitest'
import { existsSync, readFileSync, statSync } from 'node:fs'
import { dirname, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const pkgRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const ENTRY = 'src/controls.ts'
/* `uqr` is a dependency of this package (not a peer), so it is installed with
   it; TQrCode loads it with `import()` when it first draws a code. */
const ALLOWED = ['vue', '@iconify/vue', 'uqr']

/** Runtime specifiers only: `import type` / `export type` never reach a bundle. */
function specifiersOf(file: string): string[] {
  const source = readFileSync(file, 'utf8')
  const withFrom = [...source.matchAll(/(?:^|\n)(\s*(?:import|export)([^'"\n]*))from\s*['"]([^'"]+)['"]/g)]
    .filter(([, , clause]) => !/^\s+type\s/.test(clause!))
    .map((m) => m[3]!)
  const bare = [...source.matchAll(/(?:^|\n)\s*import\s*['"]([^'"]+)['"]/g)].map((m) => m[1]!)
  const dynamic = [...source.matchAll(/\bimport\(\s*['"]([^'"]+)['"]\s*\)/g)].map((m) => m[1]!)
  return [...withFrom, ...bare, ...dynamic]
}

function resolveSpecifier(fromFile: string, specifier: string): string | null {
  const base = resolve(dirname(fromFile), specifier)
  for (const candidate of [base, `${base}.ts`, `${base}.vue`, resolve(base, 'index.ts')]) {
    if (existsSync(candidate) && statSync(candidate).isFile()) return candidate
  }
  return null
}

function walk(entry: string): { files: Set<string>; packages: string[] } {
  const files = new Set<string>()
  const packages: string[] = []
  const queue: Array<{ file: string; trail: string[] }> = [{ file: resolve(pkgRoot, entry), trail: [entry] }]
  while (queue.length) {
    const { file, trail } = queue.shift()!
    if (files.has(file)) continue
    files.add(file)
    for (const specifier of specifiersOf(file)) {
      if (!specifier.startsWith('.')) {
        packages.push(`${trail.join(' -> ')} -> ${specifier}`)
        continue
      }
      const next = resolveSpecifier(file, specifier)
      if (next) queue.push({ file: next, trail: [...trail, relative(pkgRoot, next).replace(/\\/g, '/')] })
    }
  }
  return { files, packages }
}

describe('@tnzi/ui-ai/controls', () => {
  const graph = walk(ENTRY)

  it('is a build entry', () => {
    expect(readFileSync(resolve(pkgRoot, 'vite.config.ts'), 'utf8')).toContain(`'${ENTRY}'`)
    expect(JSON.parse(readFileSync(resolve(pkgRoot, 'package.json'), 'utf8')).exports['./controls']).toBeTruthy()
  })

  it('walks the controls it serves', () => {
    const names = [...graph.files].map((f) => relative(pkgRoot, f).replace(/\\/g, '/'))
    for (const file of ['src/components/controls/TSelect.vue', 'src/components/layout/TSettingRow.vue', 'src/components/layout/TSettingGroup.vue', 'src/theme/controls-theme.ts', 'src/components/overlay/TDropdownMenu.vue', 'src/headless/tooltip.ts', 'src/components/controls/TStateView.vue', 'src/components/controls/TToastHost.vue']) {
      expect(names).toContain(file)
    }
  })

  it('reaches no package but vue and @iconify/vue', () => {
    const outside = graph.packages.filter((line) => {
      const specifier = line.split(' -> ').at(-1)!
      return !ALLOWED.some((name) => specifier === name || specifier.startsWith(`${name}/`))
    })
    expect(outside).toEqual([])
  })
})

/*
 * The same promise held against what is published. The source walk above
 * cannot see what the build adds: a shared chunk, a helper Vite injects, an
 * import a plugin rewrites. This walks `dist/controls.js` and the modules it
 * loads. Needs `pnpm build` first, as the other built-output gates.
 */
describe('@tnzi/ui-ai/controls, as built', () => {
  const distRoot = resolve(pkgRoot, 'dist')

  function builtGraph(): { files: Set<string>; packages: string[] } {
    const files = new Set<string>()
    const packages: string[] = []
    const queue = [resolve(distRoot, 'controls.js')]
    while (queue.length) {
      const file = queue.shift()!
      if (files.has(file)) continue
      files.add(file)
      const source = readFileSync(file, 'utf8')
      const specifiers = [
        ...[...source.matchAll(/\bfrom\s*["']([^"']+)["']/g)].map((m) => m[1]!),
        ...[...source.matchAll(/\bimport\s*["']([^"']+)["']/g)].map((m) => m[1]!),
        ...[...source.matchAll(/\bimport\(\s*["']([^"']+)["']\s*\)/g)].map((m) => m[1]!),
      ]
      for (const specifier of specifiers) {
        if (specifier.startsWith('.')) queue.push(resolve(dirname(file), specifier))
        else packages.push(`${relative(distRoot, file).replace(/\\/g, '/')} -> ${specifier}`)
      }
    }
    return { files, packages }
  }

  it('loads no package but vue, @iconify/vue and uqr', () => {
    const { files, packages } = builtGraph()
    expect(files.size).toBeGreaterThan(30)
    const outside = packages.filter((line) => {
      const specifier = line.split(' -> ').at(-1)!
      return !ALLOWED.some((name) => specifier === name || specifier.startsWith(`${name}/`))
    })
    expect(outside).toEqual([])
  })
})

/*
 * `@tnzi/ui-ai/dialogs`: the dialogs for a page of controls. It reaches what the controls reach
 * plus the page's overlay stack (`@tnzi/ui/headless`, over `@tnzi/core`'s registry), and nothing
 * else: no naive-ui, no root barrel of either package.
 */
describe('@tnzi/ui-ai/dialogs', () => {
  const DIALOGS_ALLOWED = [...ALLOWED, '@tnzi/ui/headless', '@tnzi/core/headless', '@tnzi/core/utils']
  const graph = walk('src/dialogs.ts')

  it('is a build entry', () => {
    expect(readFileSync(resolve(pkgRoot, 'vite.config.ts'), 'utf8')).toContain(`'src/dialogs.ts'`)
    expect(JSON.parse(readFileSync(resolve(pkgRoot, 'package.json'), 'utf8')).exports['./dialogs']).toBeTruthy()
  })

  it('walks the dialog, the confirmation and the image viewer', () => {
    const names = [...graph.files].map((f) => relative(pkgRoot, f).replace(/\\/g, '/'))
    for (const file of ['src/components/overlay/TDialog.vue', 'src/components/overlay/TConfirmHost.vue', 'src/components/overlay/confirm.ts', 'src/components/overlay/TImageViewer.vue']) {
      expect(names).toContain(file)
    }
  })

  it('reaches no package but those of the controls and the overlay stack', () => {
    const outside = graph.packages.filter((line) => {
      const specifier = line.split(' -> ').at(-1)!
      return !DIALOGS_ALLOWED.some((name) => specifier === name || specifier.startsWith(`${name}/`))
    })
    expect(outside).toEqual([])
  })

  // The viewer's words come through the injection key, as the controls' do: the entry carries no catalogue.
  it('reaches no catalogue', () => {
    const names = [...graph.files].map((f) => relative(pkgRoot, f).replace(/\\/g, '/'))
    expect(names.filter((f) => f.startsWith('src/locales/') || f === 'src/i18n/index.ts')).toEqual([])
  })
})
