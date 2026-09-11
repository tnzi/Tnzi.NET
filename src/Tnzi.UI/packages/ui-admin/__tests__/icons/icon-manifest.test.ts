/**
 * The published icon manifests, checked by **using** them.
 *
 * ## Why this test exists in this shape
 *
 * The manifests answer "which Iconify names does the framework render?", so a
 * consumer can `addCollection` them and run with no network. Two versions of
 * the documented recipe have now shipped broken, both silently:
 *
 * 1. It passed *qualified* names, and the icon map rather than the whole
 *    `IconifyJSON`, to `getIcons`. `getIcons` returns `null`, `addCollection`
 *    accepts `null`, the app renders, every icon is a blank square.
 * 2. It looped over `` import(`@iconify-json/${prefix}/icons.json`) ``. That
 *    builds cleanly, and Vite leaves the bare specifier verbatim because a
 *    variable makes it unanalysable - so the browser cannot resolve it at
 *    runtime. **A test that ran the recipe under vitest passed**, because Node
 *    resolves bare specifiers and browsers do not.
 *
 * Both are the exact failure the manifest exists to remove, reintroduced at the
 * copy-paste boundary, landing on whoever follows the docs most literally. So
 * this file asserts several different things, because no single one of them
 * would have caught both:
 *
 * - the recipe **survives a real Vite build** and the glyph data lands in the
 *   output (catches #2 - the only check that runs the target toolchain);
 * - after loading, `@iconify/vue` reports every manifest name as present
 *   (catches #1, and keeps "does this name exist upstream?" permanent and
 *   offline - it previously lived in a one-off script hitting the Iconify API,
 *   which found four names that resolve nowhere);
 * - the recipe text is identical across the generator, the published `.d.ts`
 *   files and the two docs pages (stops any one copy being fixed alone).
 */
import { afterAll, describe, expect, it } from 'vitest'
import { mkdirSync, rmSync, writeFileSync } from 'node:fs'
import { readFile } from 'node:fs/promises'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { build } from 'vite'
import { iconLoaded, type IconifyJSON } from '@iconify/vue'

// @ts-expect-error - plain .mjs tooling module, no declarations and none wanted
import { ICON_PACKAGES, renderBundlingRecipe } from '../../../../tools/icons/icon-manifest.mjs'

import { ALL_COLLECTIONS } from './bundle-recipe'
import * as coreManifest from '../../../core/src/icons'
import * as uiManifest from '../../../ui/src/icons'
import * as aiManifest from '../../../ui-ai/src/icons'
import * as adminManifest from '../../src/icons'

const here = dirname(fileURLToPath(import.meta.url))
const packageRoot = resolve(here, '..', '..')
const repoRoot = resolve(packageRoot, '..', '..', '..', '..')

interface BundleReport {
  readonly loaded: number
  readonly missingCollections: readonly string[]
  readonly unresolved: readonly string[]
}

/**
 * A manifest module as a *consumer* sees it.
 *
 * The helper is optional because `@tnzi/core` deliberately ships data only (it
 * is the headless package and must not gain an `@iconify/vue` dependency).
 * Spelling that here keeps the difference type-level rather than a runtime
 * surprise.
 */
interface ManifestModule {
  readonly tnziIconNames: readonly string[]
  readonly tnziIconsByPrefix: Readonly<Record<string, readonly string[]>>
  readonly bundleTnziIcons?: (collections: readonly IconifyJSON[]) => BundleReport
}

/** Narrowest-first; the order is asserted below, not assumed. */
const MANIFESTS: readonly { pkg: string; mod: ManifestModule }[] = [
  { pkg: 'core', mod: coreManifest },
  { pkg: 'ui', mod: uiManifest },
  { pkg: 'ui-ai', mod: aiManifest },
  { pkg: 'ui-admin', mod: adminManifest },
]

/**
 * Docs that quote the aggregate package's recipe. Each must carry it verbatim.
 *
 * ★ These paths reach outside the projected tree: `docs/` is not mirrored to the
 * public repo (see the publish-public skill), so this assertion cannot pass
 * there. That is a known and accepted coupling, not an oversight - the public
 * mirror does not run the frontend quality workflow, and weakening this to
 * "assert only if the file exists" would buy nothing while opening a real hole
 * (a deleted docs page would stop being noticed here).
 */
const DOC_PAGES = [
  'docs/frontend/getting-started.md',
  'docs/frontend/troubleshooting.md',
]

/**
 * Read a repo file for source-text assertions.
 *
 * Line endings are normalised. None of these assertions is about line endings, and
 * the repo has no `.gitattributes`, so a Windows checkout gets CRLF and every
 * `toContain` built from an LF-joined string fails there while passing on CI.
 * A test that is red on every developer machine and green on CI stops carrying
 * information: the real failures drown in it (this gate was hiding one).
 */
const read = async (relative: string) =>
  (await readFile(join(repoRoot, relative), 'utf8')).replace(/\r\n?/g, '\n')

/** The recipe as published for one package, straight from the generator. */
const recipeFor = (pkg: string, mod: ManifestModule): string =>
  renderBundlingRecipe(pkg, Object.keys(mod.tnziIconsByPrefix))

/** `mdi:home`'s path data - proof that real glyphs, not just modules, got bundled. */
const MDI_HOME_PATH = 'M10 20v-6h4v6h5v-8h3L12 3L2 12h3v8z'

describe('icon manifest - the documented recipe, built and executed', () => {
  /**
   * The check that would have caught the shipped defect: build the recipe with
   * the toolchain a consumer actually uses, and confirm the glyphs are *in the
   * output*.
   *
   * A bundle is the only place the dynamic-import bug is visible. It leaves no
   * trace in the source, no build error and no warning - just a bare specifier
   * in the output and a `TypeError` in the browser the first time the module
   * runs. Asserting on real glyph data (rather than on "the build succeeded")
   * is what makes that unmissable: a build that resolves nothing still succeeds.
   */
  it('survives a real Vite build with the icon data inlined', async () => {
    const scratch = join(packageRoot, 'node_modules', '.icon-recipe-build')
    mkdirSync(scratch, { recursive: true })
    const entry = join(scratch, 'entry.ts')

    // Verbatim, except for the one line a consumer writes differently: they
    // import from the published package, we import from source.
    const recipe = recipeFor('ui-admin', adminManifest)
    const manifestSource = JSON.stringify(join(packageRoot, 'src', 'icons', 'index.ts'))
    const rewritten = recipe.replace("'@tnzi/ui-admin/icons'", manifestSource)
    expect(rewritten, 'the recipe no longer imports from @tnzi/ui-admin/icons').not.toBe(recipe)
    writeFileSync(entry, `${rewritten}\n`, 'utf8')

    const result = await build({
      root: packageRoot,
      logLevel: 'error',
      build: {
        write: false,
        // Minified so the leak check below sees code, not prose: the generated
        // manifest's own `@example` quotes `from '@iconify-json/mdi'`, and an
        // unminified bundle keeps that comment verbatim. String *contents*
        // survive minification, so the glyph assertion is unaffected.
        minify: true,
        lib: { entry, formats: ['es'], fileName: 'recipe' },
      },
    })

    const bundles = Array.isArray(result) ? result : [result]
    const code = bundles
      .flatMap((bundle) => ('output' in bundle ? [...bundle.output] : []))
      .map((chunk) => (chunk.type === 'chunk' ? chunk.code : ''))
      .join('\n')

    expect(
      code,
      'the built recipe contains no glyph data, so nothing was actually bundled. The collection '
      + 'imports resolved to nothing - which is what a bare specifier built from a variable does: '
      + 'it builds clean and fails in the browser.',
    ).toContain(MDI_HOME_PATH)

    // A specifier that survived into the output is the signature of the bug.
    const leaked = [...code.matchAll(/(?:import\(|from\s*)['"]@iconify-json\/[^'"]*['"]/g)]
    expect(
      leaked.map((match) => match[0]),
      'an @iconify-json specifier survived into the bundle instead of being inlined.',
    ).toEqual([])
  }, 180_000)

  afterAll(() => {
    rmSync(join(packageRoot, 'node_modules', '.icon-recipe-build'), { recursive: true, force: true })
  })

  /**
   * After loading, is every name actually there?
   *
   * `iconLoaded` is the same predicate `<Icon>` uses to decide it must fetch, so
   * this answers the user-visible question rather than a structural one.
   *
   * Order matters and is asserted, not assumed: the manifests are cumulative and
   * share one global icon storage, so loading `ui-admin` (the superset) first
   * would make every earlier package pass on icons it never contributed.
   */
  it('leaves every manifest name resolvable by @iconify/vue', () => {
    expect(
      MANIFESTS.map((entry) => entry.pkg),
      'MANIFESTS must run narrowest-first, or a superset loaded earlier masks a missing name.',
    ).toEqual((ICON_PACKAGES as { name: string }[]).map((pkg) => pkg.name))

    for (const [index, { pkg, mod }] of MANIFESTS.entries()) {
      const declared = (ICON_PACKAGES as { bundler: boolean }[])[index].bundler
      expect(
        typeof mod.bundleTnziIcons === 'function',
        `@tnzi/${pkg}: ICON_PACKAGES says bundler=${declared}, but the generated manifest disagrees.`,
      ).toBe(declared)

      // `@tnzi/core` is headless and publishes no helper, so load it the way its
      // own @example tells a consumer to - with the helper of a package above.
      const load = mod.bundleTnziIcons ?? uiManifest.bundleTnziIcons
      const report = load(ALL_COLLECTIONS)

      expect(report.missingCollections, `@tnzi/${pkg}: a collection was not supplied.`).toEqual([])

      const own = new Set(mod.tnziIconNames)
      expect(
        report.unresolved.filter((name) => own.has(name)),
        `@tnzi/${pkg}: name(s) resolve to nothing in the real Iconify data. A consumer bundling `
        + 'this manifest renders a blank square for each - no error, no failed request.',
      ).toEqual([])

      expect(
        mod.tnziIconNames.filter((name) => !iconLoaded(name)),
        `@tnzi/${pkg}: names reported as loaded are absent from @iconify/vue's storage.`,
      ).toEqual([])
    }
  })

  /**
   * Guards the direction the executed test cannot: it would still pass if the
   * grouped export were dropped entirely and every name loaded some other way.
   */
  it('groups every name exactly once, under its own collection', () => {
    for (const { pkg, mod } of MANIFESTS) {
      const regrouped = Object.entries(mod.tnziIconsByPrefix)
        .flatMap(([prefix, bare]) => bare.map((name) => `${prefix}:${name}`))
        .sort()

      expect(regrouped, `@tnzi/${pkg}: the grouped export does not re-assemble into the flat list.`)
        .toEqual([...mod.tnziIconNames].sort())
    }
  })

  /**
   * Bare, not qualified. Stated separately from the re-assembly check above so
   * a failure names the actual mistake instead of showing a 561-line diff.
   */
  it('groups bare names, not qualified ones', () => {
    for (const { pkg, mod } of MANIFESTS) {
      const qualified = Object.entries(mod.tnziIconsByPrefix)
        .flatMap(([prefix, bare]) => bare.filter((name) => name.startsWith(`${prefix}:`)))

      expect(
        qualified,
        `@tnzi/${pkg}: grouped values are qualified names. getIcons() looks names up bare, so it `
        + 'would return null for every collection and every icon would render blank.',
      ).toEqual([])
    }
  })

  /**
   * `selectTnziIcons` is the answer to the size problem, so its defining
   * property gets its own assertion: the subsets carry this manifest's icons
   * and not the 8 MB collection they came from.
   */
  it('selects only the manifest icons, not whole collections', () => {
    const { subsets, loaded } = adminManifest.selectTnziIcons(ALL_COLLECTIONS)
    const selected = subsets.reduce(
      (total, subset) =>
        total + Object.keys(subset.icons ?? {}).length + Object.keys(subset.aliases ?? {}).length,
      0,
    )
    const whole = ALL_COLLECTIONS.reduce(
      (total, collection) => total + Object.keys(collection.icons ?? {}).length,
      0,
    )

    expect(loaded).toBe(adminManifest.tnziIconNames.length)
    expect(
      selected,
      'selectTnziIcons returned about as many icons as the collections hold, so a consumer using it '
      + 'for a build-time subset would ship the whole of every collection anyway.',
    ).toBeLessThan(whole / 10)
  })

  /**
   * A missing collection must be *reported*, not absorbed.
   *
   * This is the whole reason the helper returns anything: the previous recipe
   * had no way to say "you did not give me mdi", so it did the one thing this
   * mechanism exists to prevent - rendered blanks and said nothing.
   */
  it('reports a missing collection instead of silently skipping it', () => {
    const report = adminManifest.selectTnziIcons(
      ALL_COLLECTIONS.filter((collection) => collection.prefix !== 'mdi'),
    )

    expect(report.missingCollections).toContain('mdi')
    expect(report.unresolved.some((name) => name.startsWith('mdi:'))).toBe(true)
  })

  /**
   * The anti-drift pin. Without it, the generator could be fixed while the docs
   * and the published `.d.ts` keep handing consumers the broken version - which
   * is how both defects reached a consumer in the first place.
   */
  it('publishes the same recipe text everywhere it appears', async () => {
    for (const { pkg, mod } of MANIFESTS) {
      const manifest = await read(`src/Tnzi.UI/packages/${pkg}/src/icons/index.ts`)
      if (!mod.bundleTnziIcons) {
        expect(manifest, `@tnzi/${pkg} ships no helper, so it must not publish a recipe.`)
          .not.toContain('bundleTnziIcons([')
        continue
      }
      const jsdoc = recipeFor(pkg, mod)
        .split('\n')
        .map((line) => (line ? ` * ${line}` : ' *'))
        .join('\n')
      expect(manifest, `@tnzi/${pkg}'s manifest @example (and its published .d.ts) is out of date.`)
        .toContain(jsdoc)
    }

    const published = recipeFor('ui-admin', adminManifest)
    for (const page of DOC_PAGES) {
      expect(await read(page), `${page} does not quote the recipe verbatim.`).toContain(published)
    }
  })
})
