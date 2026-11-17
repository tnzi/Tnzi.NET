// @vitest-environment node
/**
 * Convention gate: no entry of the package reaches naive-ui.
 *
 * The package draws everything with its own controls and overlays
 * (`components/controls`, `components/overlay`), so an app built on it does
 * not install naive-ui. One import of it, however many files away from an
 * entry, would quietly make it required again, and `import type` counts too:
 * the emitted `.d.ts` keeps it, and a consumer's type check then needs the
 * package installed. This walks the import graph from every build entry in
 * `vite.config.ts`, the way `controls-entry` does, and names the path to any
 * import of naive-ui, any naive component used in a template, and any import
 * of `@tnzi/ui` other than its subpaths. The walk stops at `@tnzi/ui`: that
 * package's own gate (`no-component-library`) holds it free of naive.
 *
 * The stylesheet is held too: the built `dist/style.css` (which inlines
 * `@tnzi/ui/style.css`) carries no `.n-*` rule and no `--n-*` variable, so an
 * app on this package ships no naive styling either.
 *
 * An entry that genuinely needs naive-ui would be named in `EXEMPT` with its
 * reason, and naive-ui would then be an optional peer used by that entry
 * alone. There is none: every entry is held, and a new entry is held by
 * default.
 */
import { describe, expect, it } from 'vitest'
import { existsSync, readFileSync, statSync } from 'node:fs'
import { dirname, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const pkgRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const uiStylesRoot = resolve(pkgRoot, '../ui/src/styles')

/** Entries that may reach naive-ui, by entry name, with the reason. None today. */
const EXEMPT: Record<string, string> = {}

/** The conversation's main path: these may never be exempted. */
const MAIN_PATH = [
  'index', 'chat', 'auth', 'plugin', 'headless', 'embed', 'theme', 'i18n',
  'locales', 'adapters', 'utils', 'controls', 'components',
]

/**
 * `@tnzi/ui` is reached only through its subpaths, never the root, which also
 * installs its global stylesheet and the plugin's side effects.
 */
const UI_SUBPATHS: Record<string, ((name: string) => boolean) | undefined> = {
  '@tnzi/ui/headless': () => true,
  '@tnzi/ui/theme': () => true,
  '@tnzi/ui/components': () => true,
}

function buildEntries(): Record<string, string> {
  const config = readFileSync(resolve(pkgRoot, 'vite.config.ts'), 'utf8')
  const entries: Record<string, string> = {}
  for (const m of config.matchAll(/(?:^|\n)\s*'?([\w/]+)'?:\s*resolve\(import\.meta\.dirname,\s*'([^']+)'\)/g)) {
    entries[m[1]!] = m[2]!
  }
  return entries
}

interface ImportRecord {
  specifier: string
  names: string[]
}

/** Every import in a file, type-only and dynamic ones included. */
function importsOf(source: string): ImportRecord[] {
  const records: ImportRecord[] = []
  for (const m of source.matchAll(/(?:^|\n)\s*(?:import|export)\b([^'"]*?)\bfrom\s*['"]([^'"]+)['"]/g)) {
    const clause = m[1]!
    const braces = clause.match(/\{([^}]*)\}/)
    const names = braces
      ? braces[1]!.split(',').map((n) => n.trim().replace(/^type\s+/, '').split(/\s+as\s+/)[0]!).filter(Boolean)
      : []
    records.push({ specifier: m[2]!, names })
  }
  for (const m of source.matchAll(/(?:^|\n)\s*import\s*['"]([^'"]+)['"]/g)) records.push({ specifier: m[1]!, names: [] })
  for (const m of source.matchAll(/\bimport\(\s*['"]([^'"]+)['"]\s*\)/g)) records.push({ specifier: m[1]!, names: [] })
  return records
}

/** Naive components written straight into a template (`<n-button>`, `<NButton>`). */
function naiveTags(source: string): string[] {
  const template = source.match(/<template[\s\S]*<\/template>/)?.[0] ?? ''
  return [...template.matchAll(/<(n-[a-z-]+|N[A-Z][A-Za-z]+)[\s/>]/g)].map((m) => m[1]!)
}

function resolveSpecifier(fromFile: string, specifier: string): string | null {
  const base = resolve(dirname(fromFile), specifier)
  for (const candidate of [base, `${base}.ts`, `${base}.vue`, resolve(base, 'index.ts')]) {
    if (existsSync(candidate) && statSync(candidate).isFile()) return candidate
  }
  return null
}

function violations(entry: string): string[] {
  const found: string[] = []
  const seen = new Set<string>()
  const queue: Array<{ file: string; trail: string[] }> = [{ file: resolve(pkgRoot, entry), trail: [entry] }]
  while (queue.length) {
    const { file, trail } = queue.shift()!
    if (seen.has(file)) continue
    seen.add(file)
    if (!/\.(ts|vue)$/.test(file)) continue
    const source = readFileSync(file, 'utf8')
    const at = trail.join(' -> ')
    for (const tag of naiveTags(source)) found.push(`${at}: <${tag}>`)
    for (const { specifier, names } of importsOf(source)) {
      if (specifier === 'naive-ui' || specifier.startsWith('naive-ui/')) {
        found.push(`${at} -> ${specifier}`)
      } else if (specifier === '@tnzi/ui' || specifier.startsWith('@tnzi/ui/')) {
        const allows = UI_SUBPATHS[specifier]
        if (!allows) found.push(`${at} -> ${specifier}`)
        else for (const name of names.filter((n) => !allows(n))) found.push(`${at} -> ${specifier} { ${name} }`)
      } else if (specifier.startsWith('.')) {
        const next = resolveSpecifier(file, specifier)
        if (next) queue.push({ file: next, trail: [...trail, relative(pkgRoot, next).replace(/\\/g, '/')] })
      }
    }
  }
  return found
}

/** The rules of a stylesheet and of every file it `@import`s relatively. */
function stylesheetChain(file: string, seen = new Set<string>()): Array<{ file: string; css: string }> {
  if (seen.has(file)) return []
  seen.add(file)
  const css = readFileSync(file, 'utf8')
  const nested = [...css.matchAll(/@import\s+['"](\.[^'"]+)['"]/g)].flatMap((m) =>
    stylesheetChain(resolve(dirname(file), m[1]!), seen),
  )
  return [{ file, css: css.replace(/\/\*[\s\S]*?\*\//g, '') }, ...nested]
}

describe('no entry reaches naive-ui', () => {
  const entries = buildEntries()

  it('reads every build entry', () => {
    for (const name of MAIN_PATH) expect(entries[name], name).toBeTruthy()
    expect(Object.keys(entries)).toEqual(expect.arrayContaining(['workflow', 'widget']))
  })

  it('exempts only real entries, never one of the main path, each with a reason', () => {
    for (const [name, reason] of Object.entries(EXEMPT)) {
      expect(entries[name], name).toBeTruthy()
      expect(MAIN_PATH).not.toContain(name)
      expect(reason.trim().length).toBeGreaterThan(20)
    }
  })

  it.each(Object.entries(buildEntries()).filter(([name]) => !(name in EXEMPT)))(
    '%s imports no naive-ui and only the neutral parts of @tnzi/ui',
    (_name, file) => {
      expect(violations(file)).toEqual([])
    },
  )

  it('declares naive-ui only as an optional peer, and only while an entry needs it', () => {
    const manifest = JSON.parse(readFileSync(resolve(pkgRoot, 'package.json'), 'utf8'))
    const declared = ['dependencies', 'devDependencies', 'peerDependencies'].filter((field) => manifest[field]?.['naive-ui'])
    if (Object.keys(EXEMPT).length === 0) {
      expect(declared).toEqual([])
    } else {
      expect(manifest.dependencies?.['naive-ui']).toBeUndefined()
      expect(manifest.peerDependenciesMeta?.['naive-ui']?.optional).toBe(true)
    }
  })

  it('declares @tnzi/ui as a required peer', () => {
    const manifest = JSON.parse(readFileSync(resolve(pkgRoot, 'package.json'), 'utf8'))
    expect(manifest.peerDependencies?.['@tnzi/ui']).toBeTruthy()
    expect(manifest.peerDependenciesMeta?.['@tnzi/ui']?.optional).not.toBe(true)
  })

  it('ships no naive rule in its built stylesheet (needs `pnpm build` first)', () => {
    const css = readFileSync(resolve(pkgRoot, 'dist/style.css'), 'utf8').replace(/\/\*[\s\S]*?\*\//g, ' ')
    expect(css.length).toBeGreaterThan(1000)
    // The minified sheet is one long line: split on rule ends to name them.
    const naive = css
      .split('}')
      .filter((rule) => /(?:^|[^\w-])\.n-[a-z]|--n-[a-z]/.test(rule))
      .map((rule) => rule.trim().slice(0, 120))
    expect(naive).toEqual([])
  })

  it('takes no naive rules in through the @tnzi/ui stylesheet', () => {
    const own = readFileSync(resolve(pkgRoot, 'src/styles/index.css'), 'utf8')
    expect(own).toMatch(/@import\s+['"]@tnzi\/ui\/style\.css['"]/)
    const chain = stylesheetChain(resolve(uiStylesRoot, 'index.css'))
    expect(chain.length).toBeGreaterThan(1)
    const naive = chain.filter(({ css }) => /\.n-[a-z]|--n-[a-z]/.test(css)).map(({ file }) => relative(uiStylesRoot, file))
    expect(naive).toEqual([])
  })
})
