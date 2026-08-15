/**
 * Package public-surface guards.
 *
 * Background: `components/data/index.ts` exported `TItemCard` / `TEntityCard`
 * while `components/index.ts` hand-picked only a few names out of that folder,
 * so those components were unreachable from the package root even though the
 * content-page standard told consumers to import them from `@tnzi/ui-admin`.
 * `dist/index.js` did not contain them and consumer pages rendered blank.
 *
 * Human review cannot hold this line: the drift lives between three files that
 * are each individually correct-looking. These tests close it from both ends -
 * structurally (a folder barrel is the folder's public surface, so all of it
 * must reach the root) and contractually (whatever the docs name must import).
 */
import { describe, it, expect } from 'vitest'
import fs from 'node:fs'
import path from 'node:path'
import * as pkg from '../src'

const COMPONENTS_DIR = path.resolve(__dirname, '../src/components')
const DOC_PATH = path.resolve(
  __dirname,
  '../../../../../docs/coding-standards/ui-content-page.md',
)

/** Names the doc mentions that are deliberately NOT part of this package root. */
const NOT_PACKAGE_ROOT_EXPORTS: Record<string, string> = {
  // Owned by @tnzi/ui - the doc says so explicitly at the end of section 0.
  TDescriptions: '@tnzi/ui',
  TSchemaForm: '@tnzi/ui',
  // The ⓘ + popover primitive TDetailSection renders for `hintMode="popover"`.
  // Named in the doc to explain what the mode produces, not as an admin import.
  THint: '@tnzi/ui',
  // Horizontal bar-rank widget for "top-N by X" drilldowns. The doc names it in
  // section 5 and spells the package out inline - `TMetricBars`（`@tnzi/ui`）.
  TMetricBars: '@tnzi/ui',
  // Admin shell internals assembled by defineAdminApp; named in the doc only to
  // describe framework behaviour, never as something a consumer imports.
  TAdminContent: 'shell internal',
  TAdminSidebar: 'shell internal',
  TGlobalSearch: 'shell internal',
  // Internal section wrapper of the built-in User Center page.
  TUserCenterSection: 'page internal',
}

/** Pull the exported binding names out of a barrel file (no `export *` here). */
function barrelExportNames(file: string): string[] {
  const src = fs.readFileSync(file, 'utf8')
  const names: string[] = []
  const re = /export\s+(?:type\s+)?\{([^}]*)\}\s*from\s*['"][^'"]+['"]/g
  let m: RegExpExecArray | null
  while ((m = re.exec(src))) {
    for (let part of m[1].split(',')) {
      part = part.trim().replace(/^type\s+/, '')
      if (!part) continue
      const aliased = part.match(/\bas\s+([A-Za-z0-9_$]+)$/)
      names.push(aliased ? aliased[1] : part.split(/\s+/)[0])
    }
  }
  return names
}

/**
 * Type-only exports vanish at runtime, so `in pkg` cannot see them. We check
 * value bindings at runtime and leave types to `vue-tsc` (the barrel would not
 * compile if a re-exported type did not exist).
 */
function isTypeOnlyExport(barrelFile: string, name: string): boolean {
  const src = fs.readFileSync(barrelFile, 'utf8')
  const localName = (part: string): string => {
    const aliased = part.match(/\bas\s+([A-Za-z0-9_$]+)$/)
    return aliased ? aliased[1] : part.replace(/^type\s+/, '').split(/\s+/)[0]
  }
  // `export type { A, B as C } from '...'`
  for (const block of src.matchAll(/export\s+type\s+\{([^}]*)\}/g)) {
    for (const part of block[1].split(',')) {
      if (part.trim() && localName(part.trim()) === name) return true
    }
  }
  // `export { A, type B, type C as D } from '...'`
  for (const block of src.matchAll(/export\s+\{([^}]*)\}/g)) {
    for (const part of block[1].split(',')) {
      const trimmed = part.trim()
      if (trimmed.startsWith('type ') && localName(trimmed) === name) return true
    }
  }
  return false
}

describe('package public surface', () => {
  describe('every component folder barrel reaches the package root', () => {
    const folders = fs
      .readdirSync(COMPONENTS_DIR, { withFileTypes: true })
      .filter((d) => d.isDirectory())
      .map((d) => d.name)
      .filter((name) => fs.existsSync(path.join(COMPONENTS_DIR, name, 'index.ts')))

    it('finds the component folder barrels (guards against a stale path)', () => {
      // A wrong path would make every assertion below vacuously pass, which is
      // exactly the silent-false-negative failure mode this suite exists for.
      expect(folders.length).toBeGreaterThan(0)
      expect(folders).toContain('data')
      expect(folders).toContain('forms')
    })

    it.each(folders)('components/%s', (folder) => {
      const barrel = path.join(COMPONENTS_DIR, folder, 'index.ts')
      const missing = barrelExportNames(barrel)
        .filter((name) => !isTypeOnlyExport(barrel, name))
        .filter((name) => !(name in pkg))

      expect(
        missing,
        `components/${folder}/index.ts exports [${missing.join(', ')}] but they do not reach ` +
          `the package root. Add \`export * from './${folder}'\` to components/index.ts ` +
          `(do not hand-pick names out of a folder that owns a barrel).`,
      ).toEqual([])
    })
  })

  describe('components named in the content-page standard are importable', () => {
    it('reads the standard (guards against a stale doc path)', () => {
      expect(
        fs.existsSync(DOC_PATH),
        `Cannot read ${DOC_PATH}. Fix the path - a missing file must fail loudly ` +
          `instead of silently skipping the contract check.`,
      ).toBe(true)
    })

    it('every T* component the doc names resolves from @tnzi/ui-admin', () => {
      const md = fs.readFileSync(DOC_PATH, 'utf8')
      const named = new Set<string>()
      // Only look inside inline-code spans so prose words are not mistaken for
      // component names.
      for (const span of md.matchAll(/`([^`\n]+)`/g)) {
        for (const t of span[1].matchAll(/\b(T[A-Z][A-Za-z0-9]+)\b/g)) named.add(t[1])
      }

      expect(named.size).toBeGreaterThan(20)
      expect(named).toContain('TItemCard')

      const missing = [...named]
        .filter((name) => !(name in NOT_PACKAGE_ROOT_EXPORTS))
        .filter((name) => !(name in pkg))
        .sort()

      expect(
        missing,
        `ui-content-page.md tells consumers to import [${missing.join(', ')}] from ` +
          `@tnzi/ui-admin, but they are not exported. Either export them or fix the doc - ` +
          `the two must agree.`,
      ).toEqual([])
    })

    it('the exemption list stays honest (an exempt name must really be absent)', () => {
      // If one of these later becomes a real root export, the exemption is
      // stale and hides the name from the contract check above.
      const wronglyExempt = Object.keys(NOT_PACKAGE_ROOT_EXPORTS).filter((n) => n in pkg)
      expect(
        wronglyExempt,
        `[${wronglyExempt.join(', ')}] are exported from the package root, so they must be ` +
          `removed from NOT_PACKAGE_ROOT_EXPORTS.`,
      ).toEqual([])
    })
  })

  describe('every headless module reaches the package root', () => {
    // The components side of this drift was gated in the 2026-07-29 repair; the
    // headless side was not, and 15 modules had silently fallen off the barrel -
    // including `useSectionRoute` / `useQueryScope`, which the content-page
    // standard tells consumers are available as low-level primitives. A folder
    // whose barrel is hand-maintained needs a gate on BOTH sides or the same
    // failure just moves next door.
    const HEADLESS_DIR = path.resolve(__dirname, '../src/headless')

    /** Modules deliberately kept off the public surface, with the reason. */
    const PRIVATE_HEADLESS: Record<string, string> = {}

    const modules = fs
      .readdirSync(HEADLESS_DIR)
      .filter((f) => f.endsWith('.ts') && f !== 'index.ts')
      .map((f) => f.replace(/\.ts$/, ''))

    it('finds the headless modules (guards against a stale path)', () => {
      expect(modules.length).toBeGreaterThan(0)
      expect(modules).toContain('useCrudPage')
    })

    it('every module is re-exported by headless/index.ts', () => {
      const barrel = fs.readFileSync(path.join(HEADLESS_DIR, 'index.ts'), 'utf8')
      const missing = modules
        .filter((name) => !(name in PRIVATE_HEADLESS))
        .filter((name) => !new RegExp(`from\\s*'\\./${name}'`).test(barrel))

      expect(
        missing,
        `headless/[${missing.join(', ')}].ts are not re-exported by headless/index.ts, ` +
          `so they are unreachable from '@tnzi/ui-admin' and '@tnzi/ui-admin/headless'. ` +
          `Add them to the barrel, or list them in PRIVATE_HEADLESS with a reason.`,
      ).toEqual([])
    })

    it('the private list stays honest (an entry must really exist)', () => {
      const stale = Object.keys(PRIVATE_HEADLESS).filter((n) => !modules.includes(n))
      expect(stale, `PRIVATE_HEADLESS names modules that no longer exist: ${stale.join(', ')}`)
        .toEqual([])
    })
  })

  describe('lower layers do not import upward into pages/', () => {
    // `components/`, `widgets/` and `headless/` sit BELOW `pages/`. When a
    // component imported its translator from `pages/_shared/translate`, the
    // `./components` subpath export could not be pulled in without the whole
    // page layer coming with it - and, through the translator, both locale
    // dictionaries. That is how ~119 kB gzip of i18n data ended up mandatory
    // for a consumer that imported one button.
    const LOWER = ['components', 'headless']

    function walk(dir: string, out: string[] = []): string[] {
      if (!fs.existsSync(dir)) return out
      for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
        const full = path.join(dir, e.name)
        if (e.isDirectory()) walk(full, out)
        else if (/\.(ts|vue)$/.test(e.name)) out.push(full)
      }
      return out
    }

    it('finds the lower-layer sources (guards against a stale path)', () => {
      const all = LOWER.flatMap((d) => walk(path.resolve(__dirname, '../src', d)))
      expect(all.length).toBeGreaterThan(100)
    })

    it('no component / widget / headless module imports from pages/', () => {
      const SRC = path.resolve(__dirname, '../src')
      const PAGES = path.join(SRC, 'pages')

      // Resolve each specifier against the importing file rather than pattern-
      // matching the string. `components/pages/` (route components inside the
      // component layer) and the top-level `pages/` layer both end in
      // `/pages/`, so a `(\.\./)+pages/` regex cannot tell them apart - it read
      // `components/widgets/… -> '../pages/TDashboardPage.vue'` as a layering
      // break when that target is a sibling inside the same layer.
      const offenders = LOWER.flatMap((d) => walk(path.join(SRC, d)))
        .filter((f) =>
          [...fs.readFileSync(f, 'utf8').matchAll(/from\s*'(\.\.?\/[^']*)'/g)].some((m) =>
            path.resolve(path.dirname(f), m[1]).startsWith(PAGES + path.sep),
          ),
        )
        .map((f) => path.relative(SRC, f).split(path.sep).join('/'))

      expect(
        offenders,
        `${offenders.join(', ')} import from pages/. Lower layers must not depend ` +
          `on the page layer - move the shared declaration down (see src/i18n/ ` +
          `and components/finance/document-row.ts) or import it from @tnzi/ui.`,
      ).toEqual([])
    })
  })

  describe('locale dictionaries stay off the static import graph', () => {
    // They are ~57 kB and ~62 kB gzipped - about half the package's whole entry
    // budget - and nothing ever reads more than one of them (lookup misses fall
    // through to `humanise`, never to another locale). A static `import { en }`
    // anywhere outside the registry puts both back in every consumer's bundle.
    it('only i18n/messages.ts imports locales/, and only dynamically', () => {
      const SRC = path.resolve(__dirname, '../src')
      function walk(dir: string, out: string[] = []): string[] {
        for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
          const full = path.join(dir, e.name)
          if (e.isDirectory()) {
            if (e.name !== 'locales') walk(full, out)
          } else if (/\.(ts|vue)$/.test(e.name)) out.push(full)
        }
        return out
      }

      const offenders = walk(SRC)
        .filter((f) => /^\s*import\s[^\n]*from\s*'[^']*locales\/(en|zh-cn)'/m.test(fs.readFileSync(f, 'utf8')))
        .map((f) => path.relative(SRC, f).split(path.sep).join('/'))

      expect(
        offenders,
        `${offenders.join(', ')} statically import a locale pack. Read dictionaries ` +
          `through i18n/messages.ts (getLocaleMessages / loadLocaleMessages) so ` +
          `bundlers can split them and fetch only the active language.`,
      ).toEqual([])
    })
  })

  describe('route components are loaded lazily', () => {
    // The 122 built-in pages are the bulk of the package's weight; they are only
    // affordable because `routes.ts` reaches them through `() => import(...)`,
    // so a consumer downloads a page when it navigates there and never
    // otherwise. A static import would quietly fold that page into the entry
    // chunk of every app that mounts the framework.
    //
    // No size budget can catch this. size-limit inlines `import()` when
    // splitting is off, so the total-graph figure is byte-identical either way
    // (measured: 546.21 kB before and after the locale packs went dynamic), and
    // the per-unit budgets externalise `../pages/*` regardless of how it is
    // imported. The invariant only survives if it is asserted here.
    const ROUTES = path.resolve(__dirname, '../src/router/routes.ts')

    /**
     * Deliberately empty: the table currently reaches EVERY page lazily,
     * including the exception views. Add a name here only with a reason it must
     * be eager, so the exception is an argued decision rather than a drift.
     */
    const ALLOWED_STATIC: string[] = []

    it('reads the route table (guards against a stale path)', () => {
      expect(fs.existsSync(ROUTES)).toBe(true)
      expect(fs.readFileSync(ROUTES, 'utf8')).toContain('component:')
    })

    it('imports no page component statically', () => {
      const src = fs.readFileSync(ROUTES, 'utf8')
      const offenders: string[] = []

      for (const m of src.matchAll(/^\s*import\s+(?:\{([^}]*)\}|(\w+))\s+from\s*'([^']*pages\/[^']*)'/gm)) {
        const names = (m[1] ?? m[2] ?? '')
          .split(',')
          .map((n) => n.trim().replace(/^type\s+/, '').split(/\s+as\s+/).pop()!.trim())
          .filter(Boolean)
        // A type-only import contributes no runtime edge.
        if (/^\s*import\s+type\s/.test(m[0])) continue
        for (const name of names) {
          if (!ALLOWED_STATIC.includes(name)) offenders.push(`${name} (${m[3]})`)
        }
      }

      expect(
        offenders,
        `routes.ts statically imports [${offenders.join(', ')}]. Route components must be ` +
          `reached with \`component: () => import('...')\` so each page stays its own chunk; ` +
          `a static import folds it into every consumer's entry bundle. Add a genuinely ` +
          `must-be-eager component to ALLOWED_STATIC with the reason.`,
      ).toEqual([])
    })

    it('every page is reached through a lazy component factory', () => {
      // The positive half of the assertion above. Without it, deleting the
      // route table's `import()` calls entirely would still pass: no static
      // page imports, because no page imports at all.
      const src = fs.readFileSync(ROUTES, 'utf8')
      const lazy = [...src.matchAll(/\(\)\s*=>\s*import\('\.\.\/pages\//g)].length
      expect(lazy).toBeGreaterThan(100)
    })
  })

  describe('root barrel specifiers survive the dist layout', () => {
    // vite's preserveModules build emits a root-level chunk per subpath export
    // (dist/components.js, dist/headless.js, ...) NEXT TO the folder that
    // vue-tsc emits the .d.ts into (dist/components/index.d.ts). vue-tsc copies
    // the specifier from src/index.ts verbatim, and TypeScript resolves a bare
    // './components' as a FILE before trying it as a folder - so it lands on
    // the type-less .js chunk and `export *` contributes nothing. The runtime
    // stays fine (bundlers read dist/index.js), which is why this hid for so
    // long: only a consumer's `tsc` sees it, as TS2305 on every component.
    //
    // Spelling `./components/index` skips the file probe. This test keeps it
    // that way; it cannot be caught by importing '../src' because source
    // resolution has no .js chunks to collide with.
    it('imports every folder barrel via an explicit /index specifier', () => {
      const rootIndex = path.resolve(__dirname, '../src/index.ts')
      const src = fs.readFileSync(rootIndex, 'utf8')

      const offenders: string[] = []
      // Anchored to line start so prose examples inside comments are ignored.
      for (const m of src.matchAll(/^export\s+\*\s+from\s*['"](\.\/[^'"]+)['"]/gm)) {
        const spec = m[1]
        if (spec.endsWith('/index')) continue
        // A specifier is only at risk when a same-named folder exists.
        const asFolder = path.resolve(__dirname, '../src', spec.replace(/^\.\//, ''))
        if (fs.existsSync(asFolder) && fs.statSync(asFolder).isDirectory()) {
          offenders.push(spec)
        }
      }

      expect(
        offenders,
        `src/index.ts re-exports [${offenders.join(', ')}] without the trailing '/index'. ` +
          `vue-tsc copies the specifier into dist/index.d.ts, where TypeScript resolves it ` +
          `to the type-less vite chunk (dist/<name>.js) instead of dist/<name>/index.d.ts, ` +
          `and consumers get TS2305 on every member. Write './<name>/index'.`,
      ).toEqual([])
    })
  })

  describe('components restored in the 2026-07-29 barrel repair', () => {
    // Explicit list so the guard survives a rewrite of the doc above.
    it.each([
      // components/data - the folder that triggered the bug report
      'TItemCard',
      'TEntityCard',
      'TChunkFileUpload',
      'TDataCardList',
      'TResponsiveTable',
      'TReportTable',
      'TKpiCard',
      'TKpiRow',
      'TEmpty',
      'TAttachmentPanel',
      'TCommentThread',
      // components/forms - the whole folder was unreachable
      'TPermissionMatrix',
      'TMenuTree',
      'TDictSelector',
      'TRoleSelector',
      'TUserSelector',
      'TTenantSelector',
      'createSelectorComponent',
      // components/display + components/crud
      'TChartPanel',
      'TCrudSearchAdvanced',
    ])('%s is exported from the package root', (name) => {
      expect(name in pkg).toBe(true)
    })

    it('keeps the deprecated TStatCard alias resolving to TKpiCard', () => {
      // @tnzi/ui no longer ships a TStatCard, so re-exporting the alias
      // wholesale reintroduces no cross-package clash.
      expect('TStatCard' in pkg).toBe(true)
      expect(pkg.TStatCard).toBe(pkg.TKpiCard)
    })
  })
})
