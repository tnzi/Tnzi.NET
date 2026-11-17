// @vitest-environment node
/**
 * Convention gate: `@tnzi/ui` uses no component library.
 *
 * This package is the common layer under `@tnzi/ui-admin` (naive-ui),
 * `@tnzi/ui-ai` (its own controls) and the apps built on them. A product that
 * renders with one library must not have to install another because the
 * common layer reached for it once; that is how every app on `@tnzi/ui-ai`
 * used to need naive-ui. So, over EVERY file under `src/` (not only what an
 * entry reaches today, so a file nobody imports yet cannot slip through):
 *
 * - no import of a component library, `import type` and `import()` included
 *   (an emitted `.d.ts` keeps a type import, and a consumer's type check then
 *   needs the package installed);
 * - no component-library tag in a template (`<n-button>`, `<NButton>`,
 *   `<van-cell>`);
 * - no style rule that targets a library's classes or variables (`.n-*`,
 *   `--n-*`, `.van-*`, `--van-*`), in a stylesheet or an SFC `<style>`;
 * - no component library anywhere in `package.json`, devDependencies included.
 *
 * The built output is checked too (`dist/**`): the bundle and the declarations
 * name no component library, and `dist/style.css` holds no rule for one.
 * Those checks need the package built first, as the CI order guarantees.
 */
import { describe, expect, it } from 'vitest'
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs'
import { dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const pkgDir = resolve(dirname(fileURLToPath(import.meta.url)), '../..')

const LIBRARIES = [
  'naive-ui',
  'vant',
  'element-plus',
  'ant-design-vue',
  'vuetify',
  'primevue',
  'quasar',
  '@arco-design/web-vue',
  'tdesign-vue-next',
]

/** Class and custom-property prefixes the libraries above style with. */
// Kept to prefixes no other code uses: `.v-` is also Vue's own transition
// class, so Vuetify's is not listed.
const LIBRARY_STYLE = /(?:^|[^\w-])\.(?:n|van|el|ant|arco)-[a-z]|--(?:n|van|el)-[a-z]/

function files(dir: string, pattern: RegExp, out: string[] = []): string[] {
  if (!existsSync(dir)) return out
  for (const name of readdirSync(dir)) {
    const full = join(dir, name)
    if (statSync(full).isDirectory()) files(full, pattern, out)
    else if (pattern.test(name)) out.push(full)
  }
  return out
}

const rel = (file: string) => relative(pkgDir, file).replace(/\\/g, '/')

function isLibrary(specifier: string): boolean {
  return LIBRARIES.some((lib) => specifier === lib || specifier.startsWith(`${lib}/`))
}

function stripComments(source: string): string {
  return source.replace(/\/\*[\s\S]*?\*\//g, ' ').replace(/<!--[\s\S]*?-->/g, ' ').replace(/(^|[^:'"`\\])\/\/[^\n]*/g, '$1')
}

/** The style rules of a stylesheet or of an SFC's `<style>` blocks, comments out. */
function styleRules(file: string): string {
  const text = readFileSync(file, 'utf8')
  const css = file.endsWith('.vue') ? [...text.matchAll(/<style\b[^>]*>([\s\S]*?)<\/style>/g)].map((m) => m[1]).join('\n') : text
  return css.replace(/\/\*[\s\S]*?\*\//g, ' ')
}

/** A selector or custom property of a component library, with its line. */
function libraryRules(css: string): string[] {
  return css
    .split('\n')
    .filter((line) => LIBRARY_STYLE.test(line))
    .map((line) => line.trim())
}

describe('@tnzi/ui uses no component library', () => {
  const sources = files(join(pkgDir, 'src'), /\.(ts|tsx|vue|mts)$/)

  it('reads the sources (guards the scan itself)', () => {
    expect(sources.length).toBeGreaterThan(50)
    expect(sources.some((f) => f.endsWith('components/display/TAvatar.vue'))).toBe(true)
  })

  it('no source file imports one (type-only and dynamic imports included)', () => {
    const found: string[] = []
    for (const file of sources) {
      const code = stripComments(readFileSync(file, 'utf8'))
      const specifiers = [
        ...[...code.matchAll(/\bfrom\s*['"]([^'"]+)['"]/g)].map((m) => m[1]!),
        ...[...code.matchAll(/\bimport\s*\(?\s*['"]([^'"]+)['"]/g)].map((m) => m[1]!),
      ]
      for (const specifier of specifiers.filter(isLibrary)) found.push(`${rel(file)} -> ${specifier}`)
    }
    expect(found).toEqual([])
  })

  it('no template uses a component-library tag', () => {
    const found: string[] = []
    for (const file of sources.filter((f) => f.endsWith('.vue'))) {
      const template = stripComments(readFileSync(file, 'utf8').match(/<template[\s\S]*<\/template>/)?.[0] ?? '')
      for (const m of template.matchAll(/<((?:n|van|el|a)-[a-z-]+|N[A-Z][A-Za-z]+|Van[A-Z][A-Za-z]+)[\s/>]/g)) {
        found.push(`${rel(file)}: <${m[1]}>`)
      }
    }
    expect(found).toEqual([])
  })

  it('no stylesheet or SFC style targets a component library', () => {
    const styled = [...files(join(pkgDir, 'src'), /\.css$/), ...sources.filter((f) => f.endsWith('.vue'))]
    expect(styled.some((f) => f.endsWith('styles/variables.css'))).toBe(true)
    const found = styled.flatMap((file) => libraryRules(styleRules(file)).map((rule) => `${rel(file)}: ${rule}`))
    expect(found).toEqual([])
  })

  it('no UnoCSS shortcut reads a component library variable', () => {
    const config = readFileSync(join(pkgDir, 'uno.config.ts'), 'utf8')
    expect(libraryRules(config.replace(/\/\*[\s\S]*?\*\//g, ' '))).toEqual([])
  })

  it('package.json declares none, devDependencies included', () => {
    const manifest = JSON.parse(readFileSync(join(pkgDir, 'package.json'), 'utf8'))
    const found = ['dependencies', 'peerDependencies', 'optionalDependencies', 'devDependencies'].flatMap((field) =>
      Object.keys(manifest[field] ?? {}).filter(isLibrary).map((name) => `${field}: ${name}`),
    )
    expect(found).toEqual([])
  })

  describe('built output (needs `pnpm build` first)', () => {
    const dist = join(pkgDir, 'dist')

    it('the bundle and its declarations name no component library', () => {
      const built = files(dist, /\.(js|d\.ts)$/).filter((f) => !f.includes(`${join('dist', 'node_modules')}`))
      expect(built.length).toBeGreaterThan(20)
      const found = built.filter((file) => LIBRARIES.some((lib) => readFileSync(file, 'utf8').includes(`'${lib}`) || readFileSync(file, 'utf8').includes(`"${lib}`)))
      expect(found.map(rel)).toEqual([])
    })

    it('dist/style.css holds no rule for a component library', () => {
      const css = readFileSync(join(dist, 'style.css'), 'utf8').replace(/\/\*[\s\S]*?\*\//g, ' ')
      expect(css.length).toBeGreaterThan(1000)
      // The minified sheet is one long line: split on rule ends to name them.
      const found = css.split('}').filter((rule) => LIBRARY_STYLE.test(rule)).map((rule) => rule.trim().slice(0, 120))
      expect(found).toEqual([])
    })
  })
})
