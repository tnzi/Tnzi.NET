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
 *   shell            192.6 kB  ← paid up front, always
 *   + one locale      53.2 kB (en) or 64.0 kB (zh-cn)  ← parallel, one only
 *   + route chunks   on navigation, per page
 *   + desktop         62.4 kB  ← only in `layoutMode: 'desktop'`
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

/**
 * The `desktop` layout's window manager: `AdminShellRoot` loads it with
 * `import()` and only when `layoutMode === 'desktop'`, so an application that
 * never turns the mode on downloads none of it.
 *
 * Externalised for the same reason route components are - and it was NOT, until
 * 2026-08-22. The shell and route-table figures had been quietly carrying the
 * whole window manager since the mode landed, which is exactly the "measuring
 * something nobody downloads" mistake this file was written to end. It also
 * made those budgets move whenever desktop chrome changed, sending someone to
 * raise a limit over weight that is not on the critical path.
 */
const LAZY_DESKTOP_SHELL = ['../components/desktop/*']

export default [
  {
    // Everything statically reachable from the package root - the admin shell,
    // stores, plugin wiring, component library, headless layer, widgets and the
    // route TABLE (its component `import()`s excluded). This is the figure that
    // actually describes "what does adding @tnzi/ui-admin cost me up front".
    name: 'shell - static graph (route components + locale packs load lazily)',
    path: 'dist/index.js',
    ignore: [...LAZY_ROUTE_COMPONENTS, ...LAZY_LOCALE_PACKS, ...LAZY_DESKTOP_SHELL],
    // Raised 196 -> 200 on 2026-08-22. The ratchet had 10 B of headroom left,
    // which stops being a budget and becomes a tripwire that fails CI for
    // whatever change happens to land next rather than for the one that grew
    // the shell. The growth is accounted for: the desktop vibrancy setter in
    // the theme store plus the theme drawer's system-override note.
    limit: '200 kB',
    gzip: true,
  },
  {
    // One of these, not both. They were static imports until 0.2.72+, which is
    // how ~107 kB gzip of dictionary ended up mandatory for every consumer
    // regardless of the language it rendered.
    name: 'locale pack - en (fetched only when the active locale is en)',
    path: 'dist/locales/en.js',
    limit: '57 kB',
    gzip: true,
  },
  {
    // Ratcheted 65 -> 67 kB on 2026-08-25 for the container-style setting's
    // strings. The pair had been sitting ~200 B under the line for several
    // releases, which is a tripwire rather than a budget: it fires for
    // whichever change happens to land next, not for the one that filled it.
    // A dictionary grows with the product; the number to watch is the step,
    // and a step this size is one setting's worth of copy.
    name: 'locale pack - zh-cn (fetched only when the active locale is zh-cn)',
    path: 'dist/locales/zh-cn.js',
    limit: '67 kB',
    gzip: true,
  },
  {
    // The route table itself: its records, their meta, guards and icons. Grows
    // when routes are added, not when pages get heavier.
    //
    // 82 -> 85 kB (2026-08-22): `AdminShellRoot` and `TAdminShell` now reach the
    // desktop store - that is what lets the User Center entry and a global-search
    // hit open a window instead of pushing a router whose outlet the desktop
    // replaced - and the store pulls in the window sizing policy with it. The
    // last 264 B are the desktop surface setters on the theme store, which the
    // shell has always imported.
    name: 'route table - records + guards (page components excluded)',
    path: 'dist/router.js',
    ignore: [...LAZY_ROUTE_COMPONENTS, ...LAZY_DESKTOP_SHELL],
    // Raised 85 -> 88 on 2026-08-22, measured at 85.00 kB - i.e. exactly on the
    // line, which is a tripwire rather than a budget. The growth is the desktop
    // store's panel support (panel-aware grouping, window size hints), which
    // the route table reaches through the shell.
    limit: '88 kB',
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
    /**
     * The desktop layout, as its own download. Fetched on the first render in
     * `layoutMode: 'desktop'` and never otherwise.
     *
     * Everything it shares with the shell is externalised, so the figure tracks
     * the desktop's own weight: leaving naive-ui and @iconify in produced 110 kB
     * dominated by dependencies, a number that barely moves when desktop code
     * changes - a ratchet that cannot catch what it is watching.
     *
     * Measured 2026-08-22 at 62.35 kB across the nine components plus their two
     * helper modules.
     */
    name: 'desktop layout chunk (fetched only in layoutMode: desktop)',
    path: 'dist/components/desktop/*.js',
    ignore: [
      // Every specifier this chunk imports that the shell has already paid for.
      // Verified against the built chunk's own import list, not guessed - the
      // first pass missed `vue-router`, `@vueuse/core` and `@iconify/vue` and
      // read 110 kB, most of it somebody else's weight.
      'vue',
      'vue-router',
      'naive-ui',
      '@vueuse/core',
      '@iconify/vue',
      '@tnzi/ui',
      '@tnzi/core',
      '../../stores/*',
      '../../headless/*',
      '../../i18n/*',
      '../../utils/*',
      '../../router/*',
      '../../_virtual/*',
    ],
    // Raised 66 -> 70 on 2026-08-22. Accounted for: chat as a window-manager
    // panel (registry + embedded mode), the frosted-glass material, per-window
    // tints, and the hover-suppression state.
    limit: '70 kB',
    gzip: true,
  },
  {
    // Coarse backstop, NOT a download size: every chunk summed, both locales
    // included. Nobody ever fetches this much. It exists to catch
    // across-the-board growth - a heavy new dependency, or pages fattening in
    // aggregate - that the per-unit budgets above would each absorb.
    name: 'total reachable graph (all chunks + both locales; not a download)',
    path: 'dist/index.js',
    // 610 since the bank-deposit page landed (2026-08-28): a new finance page
    // plus its two locale blocks, measured at 600.76 kB. Was 600 since the
    // desktop layout (2026-08-22). The window manager is lazily loaded and so
    // costs the `shell` budget nothing, but this number counts `import()` too -
    // esbuild inlines dynamic imports with splitting off - so genuinely-new
    // code always shows up here even when no user downloads it up front. That
    // is the point of keeping it: it is the only budget nothing can hide from.
    limit: '610 kB',
    gzip: true,
  },
]
