/**
 * Size budgets for `@tnzi/ui-admin`.
 *
 * Authored as `.mjs` rather than `.json` so each budget can say what it
 * measures. That matters here because the previous config measured something
 * nobody downloads: it pointed size-limit at `dist/index.js` with no `ignore`,
 * and size-limit bundles the ENTIRE reachable graph - following `import()` as
 * well as static imports. So the "entry" figure folded in all 122 lazily-loaded
 * route components AND both locale dictionaries. It read 546 kB against a
 * "240 kB limit", had never once been green, and told you nothing about what a
 * browser fetches before first paint.
 *
 * The fix is not a bigger number - it is measuring per download unit. Anything
 * the framework loads with `import()` is externalised from the entry budget and
 * gets its own budget, so each figure below is one thing the browser actually
 * asks for:
 *
 *   shell            190.6 kB  ← paid up front, always
 *   + one locale      48.7 kB (en) or 58.5 kB (zh-cn)  ← parallel, one only
 *   + route chunks   on navigation, per page
 *
 * ## Why `ignore` and not code splitting
 *
 * size-limit's `entry` option (which would let us measure just the entry chunk
 * of a split build) requires `@size-limit/webpack`; this repo runs the esbuild
 * preset, where `ignore` maps to esbuild's `external`. Externalising the exact
 * specifiers the framework loads dynamically gets the same answer without a
 * second bundler in devDependencies.
 *
 * ## What these budgets CANNOT catch
 *
 * **No budget here can tell a lazy edge from a static one.** The externalised
 * ones exclude those modules either way, and the total-graph one counts them
 * either way - esbuild inlines `import()` when splitting is off, so the total
 * came to exactly 546.21 kB both before and after the locale packs were made
 * dynamic. Measured, not assumed.
 *
 * Laziness is therefore guarded where it can be - as convention tests in
 * `__tests__/publicApi.test.ts`: "locale dictionaries stay off the static
 * import graph" and "route components are loaded lazily".
 *
 * Numbers measured 2026-07-31. Treat every limit as a ratchet: tighten it when
 * weight comes off, and never raise one to turn a red run green without saying
 * why in the same commit.
 */

/** Route components: `routes.ts` loads all 122 with `import()`, one chunk each. */
const LAZY_ROUTE_COMPONENTS = ['../pages/*']

/** Locale dictionaries: `i18n/messages.ts` loads only the active one, lazily. */
const LAZY_LOCALE_PACKS = ['../locales/en.js', '../locales/zh-cn.js']

export default [
  {
    // Everything statically reachable from the package root - the admin shell,
    // stores, plugin wiring, component library, headless layer, widgets and the
    // route TABLE (its component `import()`s excluded). This is the figure that
    // actually describes "what does adding @tnzi/ui-admin cost me up front".
    name: 'shell - static graph (route components + locale packs load lazily)',
    path: 'dist/index.js',
    ignore: [...LAZY_ROUTE_COMPONENTS, ...LAZY_LOCALE_PACKS],
    limit: '210 kB',
    gzip: true,
  },
  {
    // One of these, not both. They were static imports until 0.2.72+, which is
    // how ~107 kB gzip of dictionary ended up mandatory for every consumer
    // regardless of the language it rendered.
    name: 'locale pack - en (fetched only when the active locale is en)',
    path: 'dist/locales/en.js',
    limit: '55 kB',
    gzip: true,
  },
  {
    name: 'locale pack - zh-cn (fetched only when the active locale is zh-cn)',
    path: 'dist/locales/zh-cn.js',
    limit: '65 kB',
    gzip: true,
  },
  {
    // The route table itself: 122 records, their meta, guards and icons. Grows
    // when routes are added, not when pages get heavier.
    name: 'route table - records + guards (page components excluded)',
    path: 'dist/router.js',
    ignore: LAZY_ROUTE_COMPONENTS,
    limit: '90 kB',
    gzip: true,
  },
  {
    // Subpath imports. These have no dynamic edges, so the raw figure is
    // already the download unit.
    name: 'components subpath (TCrudPage, TListShell, renderers + deps)',
    path: 'dist/components.js',
    limit: '100 kB',
    gzip: true,
  },
  {
    name: 'headless subpath (useCrudPage etc.)',
    path: 'dist/headless.js',
    limit: '36 kB',
    gzip: true,
  },
  {
    name: 'pages subpath (built-in page components + translate helpers)',
    path: 'dist/pages.js',
    limit: '60 kB',
    gzip: true,
  },
  {
    // Coarse backstop, NOT a download size: every chunk summed, both locales
    // included. Nobody ever fetches this much. It exists to catch
    // across-the-board growth - a heavy new dependency, or pages fattening in
    // aggregate - that the per-unit budgets above would each absorb.
    name: 'total reachable graph (all chunks + both locales; not a download)',
    path: 'dist/index.js',
    limit: '580 kB',
    gzip: true,
  },
]
