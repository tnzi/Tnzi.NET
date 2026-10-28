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
 * asks for (measured 2026-10-12):
 *
 *   shell            217.9 kB  ← paid up front, always
 *   + one locale      71.8 kB (en) or 85.5 kB (zh-cn)  ← parallel, one only
 *   + route chunks   on navigation, per page
 *   + desktop         67.1 kB  ← only in `layoutMode: 'desktop'`
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
 * The 546 kB figures were measured 2026-07-31. Treat every limit as a ratchet:
 * tighten it when weight comes off, and never raise one to turn a red run green
 * without saying why in the same commit.
 *
 * `peerDependencies` (`vue`, `naive-ui`, `@tnzi/core`, `@tnzi/ui`, ...) are
 * never counted: size-limit prepends them to every check's `ignore`. A change
 * inside `@tnzi/core` or `@tnzi/ui` moves these budgets only through code of
 * this package that changed with it.
 *
 * The 2026-10-12 notes below come from the audit round's merges, measured by
 * building the round's commits in a scratch extract with the same budget
 * definitions and reading a per-module breakdown of each bundle (esbuild's
 * metafile under size-limit's own options) to name what moved.
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

/**
 * Hostable panels the package root exports as async components (the AI usage-limit
 * panels): a consumer pays for one when a page renders it, not up front. The
 * built-in page that also uses them is a route component, already external.
 */
const LAZY_HOSTED_PANELS = ['./components/ai/*']

export default [
  {
    // Everything statically reachable from the package root - the admin shell,
    // stores, plugin wiring, component library, headless layer, widgets and the
    // route TABLE (its component `import()`s excluded). This is the figure that
    // actually describes "what does adding @tnzi/ui-admin cost me up front".
    name: 'shell - static graph (route components + locale packs load lazily)',
    path: 'dist/index.js',
    ignore: [...LAZY_ROUTE_COMPONENTS, ...LAZY_LOCALE_PACKS, ...LAZY_DESKTOP_SHELL, ...LAZY_HOSTED_PANELS],
    // Raised 196 -> 200 on 2026-08-22. The ratchet had 10 B of headroom left,
    // which stops being a budget and becomes a tripwire that fails CI for
    // whatever change happens to land next rather than for the one that grew
    // the shell. The growth is accounted for: the desktop vibrancy setter in
    // the theme store plus the theme drawer's system-override note.
    //
    // 200 -> 205 on 2026-09-02, measured at 202.26 kB. Attributed by building
    // BOTH ways rather than by guessing: reverting that day's five changed
    // source files to HEAD and rebuilding still measured 201.87 kB, i.e. the
    // budget was ALREADY red and the breadcrumb / page-header work accounts for
    // 390 B of it. The 1.87 kB of drift predates it and belongs to whatever
    // landed since 2026-08-22 without re-measuring - the recurring failure this
    // file keeps recording. (Both builds also carried unrelated uncommitted work
    // sitting in the same tree, so the drift figure is an upper bound on what is
    // actually on main.) Headroom is back to ~1.3%.
    //
    // 205 -> 210 on 2026-09-20, measured at 207.18 kB. Built both ways: with the
    // one barrel change of that day reverted (`UserSecuritySection` no longer
    // exported from `pages/index.ts`) the shell measures 204.62 kB, so the
    // section accounts for 2.56 kB and the tree was green before it. The cost
    // is deliberate: the sign-in security section is now hostable from a
    // consumer's own persona page, which puts it on the static graph like
    // `Users` and the login modules - the price of "import and mount" without a
    // second entry point. Headroom is back to ~1.3%.
    //
    // 210 -> 215 on 2026-09-20 (pt3), measured at 209.07 kB against 207.18 kB
    // at HEAD, both built the same way. The 1.89 kB is `TTwoFactorPanel`: the
    // two-factor method list shared by the User Center and the user detail
    // page, exported from the root so a consumer account page can host it, and
    // with it the core `StepUpPromptController` + `TStepUpModal` it mounts when
    // no host bridge is passed. Its authenticator-enrolment modal (naive's QR
    // encoder) is behind an `import()` and measured at 0.86 kB of that figure -
    // esbuild inlines it here, a browser fetches it on the first "Set up". Left
    // counted rather than externalised: an ignore entry for 0.86 kB is more
    // config than the weight is worth. 0.93 kB of headroom is a tripwire, not a
    // budget, hence the step.
    //
    // 215 -> 216 kB (2026-10-11). Measured 213.97 kB with this batch's ui-admin
    // sources swapped back to HEAD and 214.87 kB with them (+0.90 kB): the
    // lossless JSON re-indenter behind the `json` form field, the in-flight save
    // guard on the CRUD / detail Confirm buttons, and the finance period's
    // re-resolution of relative presets. 0.13 kB of headroom is a tripwire.
    //
    // 216 -> 217 kB (2026-10-11). Measured 215.76 kB with this batch's ui-admin
    // sources swapped back to HEAD and 216.19 kB with them (+0.43 kB): the
    // router view's page holders (KeepAlive matches component names, and a kept
    // page gets its own view of the route), and a detail opened by id that
    // stays closed and says why when the record cannot be loaded.
    //
    // 217 -> 218 kB (2026-10-12). Measured 216.63 kB before this batch and
    // 217.08 kB with it (+0.45 kB). The statically reachable part: the tab
    // store's bound navigator and the shell's router binding (closing a tab
    // takes the page on screen with it), the bridges' list reads asserting
    // success so a refusal is reported instead of an empty list, and the core
    // captcha client reading the ALTCHA challenge document raw.
    //
    // 218 -> 219 kB (2026-10-12). Measured 217.08 kB before this batch and
    // 218.04 kB with it (+0.96 kB). Its ui-admin part: chat writes that report
    // the server's refusal, latest-keyword guards on the remote selectors and
    // chat searches (with `useDebounced`), the broadcast link scheme check,
    // advanced search resolving the schema's label keys and `visible`, the
    // attachment panel's batch queue, accounting money in `TReportTable`, and
    // the activity timeline's locale-aware relative times.
    //
    // Re-measured 2026-10-12 at 217.86 kB on the round's final tree; the limit
    // stays. Merging the f43cf2c1c line took it from 218.04 to 217.63 kB
    // (-0.41 kB): the module manifest, the AI bridge members nothing called and
    // the component resolver came out, against the route gate shared by the
    // guard and quick actions, the hide/show-modules diagnostics, the KPI
    // strip's placeholders and the theme snapshot's colour check. The fixes
    // after it add 0.23 kB: the chat config probed again when the shell
    // switches users, and the sign-in modules submitting once per attempt.
    // 219 -> 220 kB (2026-10-26). Measured 219.01 kB with the billing shell
    // hooks removed and 219.22 kB with them: the super admin bypass scope
    // (`bypassesPermissionChecks` replacing the blanket `isSuperUser` checks)
    // and the core HTTP result listener account for most of it; the billing
    // standing hooks add about 0.2 kB. The billing UI itself is behind the
    // `@tnzi/ui-admin/billing` subpath and `import()`.
    // 2026-10-27: 219.21 -> 219.47 kB, inside the limit and accepted by the
    // product owner: the reusable footer switch on the form modal (a record
    // flag such as "Active" at the footer's left, `useCrudPage({ formFooterSwitch })`).
    // Measured by building with the round's ui-admin sources at HEAD and with them.
    limit: '220 kB',
    gzip: true,
  },
  {
    // One of these, not both. They were static imports until 0.2.72+, which is
    // how ~107 kB gzip of dictionary ended up mandatory for every consumer
    // regardless of the language it rendered.
    // 57 -> 68 kB (2026-08-31). Attributed, not guessed: 242 -> 247 kB of source
    // across six ordinary feature commits since the last ratchet, then +28 kB in
    // one - commit 310a6545, whose 210 added lines are 204 `*Desc` keys (the
    // settings-centre field descriptions). Descriptions are whole sentences, so
    // they cost ~5x a label per line.
    //
    // Headroom is deliberate. The note below this one says a limit sitting ~200 B
    // under the line is a tripwire, not a budget: it fires for whichever change
    // lands next rather than the one that filled it. ~4 kB absorbs several
    // ordinary features' worth of copy (six of them came to ~1.5 kB gzip here)
    // while still firing on another batch the size of that one.
    //
    // Worth knowing if this needs to come down: en's 204 descriptions duplicate
    // the English the backend already sends as the fallback. They exist because
    // zh-cn's type is derived from en's, so en has to be complete. Breaking that
    // coupling would return ~10 kB gzip to English consumers.
    // 68 -> 70 kB (2026-09-21), measured 67.66 kB: the passkey / security-key
    // strings (a fourth two-factor method: panel row, login-page prompt and
    // switcher entry, settings-centre field + description). 0.34 kB of headroom
    // had turned the budget into a tripwire again.
    // 70 -> 72 kB (2026-10-03), measured 70.06 kB: the strings for the
    // permission holders drawer, the file version history panel and the User
    // Center notification preferences (+1.17 kB over 68.89 kB). The headroom
    // keeps the next ordinary feature from tripping a budget it did not fill.
    // 72 -> 74 kB (2026-10-12), measured 71.79 kB on the audit round's final
    // tree against 70.27 kB at its start (+1.52 kB), nearly all of it in the
    // batches merged before the f43cf2c1c line (71.75 kB): strings that say
    // why an action was refused or a figure could not be read, the CSV user
    // import's per-row outcome, signing enum labels and sender values,
    // template preview and clone on shipped templates, workflow resume and
    // step renames, captured audit request details and corrected setting
    // descriptions. The f43cf2c1c line's agent provider hint, chat drawer and
    // workflow unsaved-edits strings add 0.04 kB. 0.21 kB of headroom is a
    // tripwire.
    name: 'locale pack - en (fetched only when the active locale is en)',
    path: 'dist/locales/en.js',
    limit: '74 kB',
    gzip: true,
  },
  {
    // Ratcheted 65 -> 67 kB on 2026-08-25 for the container-style setting's
    // strings. The pair had been sitting ~200 B under the line for several
    // releases, which is a tripwire rather than a budget: it fires for
    // whichever change happens to land next, not for the one that filled it.
    // A dictionary grows with the product; the number to watch is the step,
    // and a step this size is one setting's worth of copy.
    // 67 -> 81 kB (2026-08-31), same cause as en above: the 204 settings-centre
    // descriptions (+26 kB of source in commit 310a6545). zh-cn carries more
    // bytes per entry than en because CJK is 3 bytes per character in UTF-8.
    // 81 -> 84 kB (2026-09-21), measured 80.58 kB, same strings as en above.
    // 84 -> 86 kB (2026-10-11). Measured 84.85 kB with the skill edit and
    // workflow step-id messages, the signing request create form's sender-value
    // strings and the "finish archiving" action; each measured under 0.2 kB on
    // its own. The pack had reached the line, and 0.15 kB of headroom would
    // turn it red on whichever string lands next. That figure predates the
    // MCP exposed agents card's sentence (exposure made there lasts until the
    // API restarts), which was measured on its own line at 83.97 -> 84.08 kB
    // (+0.11 kB) and merged after it.
    // 86 -> 88 kB (2026-10-12), measured 85.53 kB on the audit round's final
    // tree against 83.69 kB at its start (+1.84 kB): the same strings as the
    // en note above, the MCP sentence among them. 0.47 kB of headroom would
    // trip on the next batch of that size, and the two packs move together.
    name: 'locale pack - zh-cn (fetched only when the active locale is zh-cn)',
    path: 'dist/locales/zh-cn.js',
    limit: '88 kB',
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
    // 88 -> 89 kB (2026-10-11). Measured 87.74 kB with this batch's ui-admin
    // sources swapped back to HEAD and 88.00 kB with them (87,999 bytes, one
    // byte under the line): the tab store's navigator and the shell binding it
    // to the router, so a tab close takes the page on screen with it.
    // 89 -> 90 kB (2026-10-12), measured 88.91 kB on the audit round's final
    // tree, 89 B under the line. In three steps from the 88.00 kB above:
    // 88.67 kB once the 2026-10-12 batches had merged (+0.67 kB, the chat
    // surfaces the shell mounts: the conversation info panel, the new-chat,
    // member-picker and search-history dialogs, the message bubble and the chat
    // store, which say why a write was refused, keep to the latest keyword and
    // link only to web addresses); 88.81 kB after the f43cf2c1c merge (+0.14
    // kB: the shared route gate, the theme snapshot's colour check, whole-list
    // keyword search and login modules that survive a reload, less the guard
    // code the route gate replaced); 88.91 kB with the chat config probed again
    // when the shell switches users (+0.10 kB).
    limit: '90 kB',
    gzip: true,
  },
  {
    // Subpath imports. These have no dynamic edges, so the raw figure is
    // already the download unit.
    name: 'components subpath (TCrudPage, TListShell, renderers + deps)',
    path: 'dist/components.js',
    // 100 -> 101 kB (2026-10-12). Measured 99.43 kB with this batch's ui-admin
    // sources swapped back to HEAD and 99.77 kB with them (+0.34 kB): advanced
    // search resolving the schema's label keys and `visible`, and the delete
    // paths settling a refusal handleDelete already reported. 0.23 kB of
    // headroom is a tripwire.
    // Re-measured 2026-10-12 at 100.27 kB on the audit round's final tree; the
    // limit stays. The f43cf2c1c merge added 0.45 kB (99.81 -> 100.26 kB): the
    // route gate the list shells' permission checks share, the CRUD list
    // retrying only transient failures and reloading after a refused batch
    // delete, whole-list keyword search matching the row's text, the detail
    // overlay refusing an id that would steer its request to another path, and
    // masked secret inputs in the schema form.
    // 101 -> 102 kB (2026-10-27), requested by the product owner. Measured
    // 100.91 kB with the round's ui-admin sources at HEAD and 101.18 kB with
    // them (+0.26 kB): the reusable footer switch on the form modal (label,
    // hint and switch at the footer's left, edit-only by default) and the list
    // shell handing `useCrudPage`'s `formFooterSwitch` to it. 86 B of headroom
    // before it made any framework opt-in on the modal red.
    limit: '102 kB',
    gzip: true,
  },
  {
    name: 'headless subpath (useCrudPage etc.)',
    path: 'dist/headless.js',
    // 36 -> 37 kB (2026-10-11). Measured 35.60 kB at HEAD and 35.91 kB with the
    // in-flight save guard (useFormModal / useCrudPage / useDetail) and the
    // finance period's rollover handling: 0.09 kB of headroom is a tripwire.
    // Re-measured 2026-10-12 at 36.21 kB on the audit round's final tree; the
    // limit stays. It read 36.39 kB before the f43cf2c1c merge, which took
    // 0.18 kB off: the module manifest came out, against the shared route gate,
    // the theme snapshot's colour check and the CRUD list's retry rule.
    limit: '37 kB',
    gzip: true,
  },
  {
    name: 'pages subpath (built-in page components + translate helpers)',
    path: 'dist/pages.js',
    // 60 -> 65 kB on 2026-09-20 (pt3), measured at 62.09 kB against 58.61 kB at
    // HEAD. `UserSecuritySection` (exported here since the day before) now
    // renders its two-factor half through `TTwoFactorPanel`, and the one
    // component carries both modes: the 3.48 kB is the panel plus the
    // self-service pieces the admin mode never opens - the step-up controller
    // and modal, and the authenticator-enrolment modal. One panel that reads
    // the same in the User Center and on the user detail page is the point;
    // splitting it per mode to save ~2 kB on a subpath would give the two
    // hosts two components to keep in step.
    // 65 -> 66 kB (2026-10-10), measured 64.99 kB with this batch's ui-admin
    // sources swapped back to HEAD and 65.21 kB with them: the user page and
    // list now send back the profile fields their forms do not show (the update
    // replaces the detail record), gate the roles / sessions / sign-in history
    // sections on their own grants, and stop claiming "none" after a refused load.
    // 0.01 kB of headroom had made the budget a tripwire.
    // 66 -> 67 kB (2026-10-11). Measured 65.93 kB with this review's ui-admin
    // sources swapped back to HEAD and 65.99 kB with them: the users list swaps
    // the full record into an edit restored from a list row and keeps the
    // stored nickname when the form never held one; feature usage counts its
    // stats and trend reads apart. 0.01 kB of headroom again.
    // 67 -> 68 kB (2026-10-11). Measured 67.06 kB once the audit fixes to the
    // users page and the list / detail shells it pulls in were combined: the
    // whole profile carried on save with one save per Confirm, advanced search
    // opening on the filters in effect, read-only readers able to view, and an
    // Edit whose record cannot be loaded staying closed. Fixes, not a new surface.
    // 68 -> 69 kB (2026-10-12), measured 67.56 kB on the audit round's final
    // tree. 67.25 kB once the 2026-10-12 batches had merged: on top of the
    // users page fixes above, the authenticator enrolment that confirms once
    // per Enter and the list shell pieces the built-in pages carry (advanced
    // search label keys, settled delete refusals), measured on their own line
    // at 66.81 -> 67.02 kB (+0.21 kB), and the CSV user import summary that
    // names the refused rows. The f43cf2c1c merge added 0.25 kB (the shared
    // route gate, the CRUD list's retry rule, login modules that survive a
    // reload, detail ids that cannot steer a request, masked secret inputs)
    // and the sign-in modules submitting once per attempt 0.06 kB. 0.44 kB of
    // headroom would trip on the next batch of this round's size.
    limit: '69 kB',
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
    // Re-measured 2026-10-12 at 67.11 kB, the same at the start and the end of
    // the audit round; the limit stays.
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
    // 610 -> 635 kB (2026-08-31). Both locale packs are counted here, so this
    // moved for the same reason they did; the rest is the published icon
    // manifest (`@tnzi/*/icons`, 3.19 kB gzip), which no consumer downloads
    // unless it opts into bundling icons - but this budget counts `import()`
    // too, which is exactly what it is for.
    // 635 -> 645 kB (2026-09-12). Measured 638.68 kB at the head of the
    // audit-backlog branch before the step-up UI landed (the day's fixes had
    // already crossed the line without anyone re-ratcheting), then 640.58 kB
    // with `TStepUpModal` + the core `StepUpPromptController` + its two locale
    // blocks. The re-authentication prompt is new capability, not creep: it
    // is what turns a `[RequireStepUp]` challenge from a dead-end toast into
    // a completed action.
    // 645 -> 652 kB (2026-09-20). Measured 647.73 kB with the user detail
    // page's sign-in security section (`UserSecuritySection`: another account's
    // two-factor methods and its sign-in IP allow-list), its bridge contract,
    // the core `useAdminUserSecurityApi` / `getPermissionPicture` clients and
    // the two locale blocks, on top of the opt-out list page that landed the
    // same day. Both are new admin surfaces for backend capability that had no
    // page; nothing existing grew.
    // 652 -> 665 kB (2026-09-27). Measured 661.9 kB after four admin surfaces
    // for backend capability that had no page: the workflow version history
    // drawer, human-input resume panel and run controls; agent grant switches
    // and the "used by agents" lookups on skills / knowledge / tools; the core
    // workflow and grant clients; and their locale blocks. All of it sits
    // behind route-level `import()` (shell stayed at 211.6 kB), which is why it
    // only shows up here.
    // 665 -> 675 kB (2026-10-03). Measured 663.01 kB before and 672.28 kB after
    // three admin surfaces for backend capability that had no page, each
    // measured on its own against the same base: the permission holders
    // drawer (+2.84 kB: the roles that grant a code, every holder and its path,
    // the explicit denies), the file manager's version history panel and the
    // Versions page's download / delete actions (+2.70 kB), and the User Center
    // "Notifications" section where recipients edit their own channel switches,
    // quiet hours in their own time zone and hourly caps (+3.93 kB; swapping
    // the time pickers for quarter-hour selects moved it by 0.1 kB, so the
    // weight is the feature, not a control choice). Each slice also adds its
    // core client and two locale blocks. All three sit behind route-level
    // `import()`, so the shell moved 0.27 kB (212.48 -> 212.75 kB).
    // 675 -> 677 kB (2026-10-10). Measured 673.77 kB with this batch's ui-admin
    // sources swapped back to HEAD and 675.29 kB with them (+1.52 kB): the
    // finance editors carrying the fields they do not show (manual exchange
    // rates, line tax overrides, journal line party / dimension tags, tax code
    // compound flags, a bank rule's party and its picker), cent rounding of
    // every client-derived settlement amount, and the identity fixes counted in
    // the pages budget above. Data-integrity fixes, not a new surface.
    // 677 -> 679 kB (2026-10-10). Measured 676.59 kB with this batch's ui-admin
    // sources swapped back to HEAD and 677.33 kB with them (+0.74 kB): date
    // fields sent as calendar dates (advanced search, the user, tenant and
    // recurring forms) with day bounds for the timestamp-filtered lists, the
    // per-document party filter keys, the recurring editor loading the full
    // template, and `createDefaults` seeding the create forms. Contract fixes,
    // not a new surface.
    // 679 -> 681 kB (2026-10-11). Measured 678.97 kB with this batch's ui-admin
    // sources swapped back to HEAD and 679.92 kB with them (+0.95 kB): last-wins
    // guards on the pay-run drawer, statements, the party work surface, the file
    // list, organization members, sessions, feature usage and the log viewer;
    // refusals said out loud on resend / disconnect / clear and the log reads;
    // actions offered only where their endpoint accepts them (journal reverse,
    // pay-run calculate / post, refund review, template preview / send-test,
    // invited users' lock); the revaluation and pending-actions guards. Fixes,
    // not a new surface.
    // 681 -> 683 kB (2026-10-11). Measured 680.73 kB with this review's ui-admin
    // sources swapped back to HEAD and 681.02 kB with them (+0.29 kB): last-wins
    // guards on the cheque and deposit queues, separate stats / trend counters on
    // feature usage, the users list's restored edit, and estimates / purchase
    // orders carrying the internal note and ship-to the editor has no field for.
    // 683 -> 685 kB (2026-10-11). Measured 681.02 kB with this batch's ui-admin
    // sources swapped back to HEAD and 682.99 kB with them (+1.97 kB): one save
    // per Confirm, lossless JSON formatting (editor and the three read-only
    // payload views), relative finance periods that follow today, the skill edit
    // form loading its body, and workflow step renames that keep the graph
    // configuration in step. Fixes, not a new surface; 0.01 kB of headroom again.
    // 685 -> 691 kB (2026-10-11). Measured 689.59 kB with: the signing request
    // create form's sender values and "finish archiving" (+1.19 kB), the
    // organization page's "Add members" picker with the catalogue / role-list
    // reads on the role, user-grant, permission and entity-role pages (+0.72 kB),
    // tab pages actually kept alive (page holders, the route each kept page sees,
    // hand-offs that also arrive as route changes), a detail opened by id that
    // stays closed when its record cannot be loaded, paged knowledge base
    // documents, queued agent resource writes, CLI reads that report refusals,
    // the AI create forms' defaults, and the template preview sending only
    // rendering inputs. Fixes, not a new surface.
    // 691 -> 694 kB (2026-10-12). Measured 689.59 kB before this batch and
    // 692.28 kB with it (+2.69 kB): the user import summary that names the
    // refused rows, tab closes that take the page with them, template preview
    // and clone on shipped templates, list reads that report refusals, thread
    // Markdown exported through the client, skill switches offered only where
    // the batch endpoints act, catalogue statistics that show placeholders when
    // they cannot be read, and the captcha challenge read. Fixes, not a new
    // surface.
    // 694 -> 695 kB (2026-10-12). Measured 692.28 kB before this batch and
    // 693.62 kB with it (+1.34 kB): one submit per Enter on the authenticator,
    // sign-in and share-link forms, chat refusals said out loud with the
    // server's column widths, latest-keyword guards on every remote search, the
    // broadcast link scheme check, advanced search label keys, settled delete
    // refusals, the attachment batch queue, accounting money in report tables,
    // the reconcile counter, and the locale-aware activity timeline. 0.38 kB of
    // headroom would fire on whichever change lands next.
    // 695 -> 696 kB (2026-10-12), measured 694.50 kB on the audit round's final
    // tree. Merging the f43cf2c1c line took it from 693.62 to 694.29 kB (+0.67
    // kB; that line measured its own batch at 688.40 -> 689.01 kB, +0.61 kB, in
    // a note the merge dropped): the CLI run drawer re-reading its run and the
    // run list's formatted columns, the run monitor reading a linked run older
    // than its page, the evaluation list's Load more and pick guards, the
    // workflow editor's unsaved-edits gate, MCP / channels / sandbox reads that
    // tell a refusal from an absent module, the chat drawer and presence
    // guards, masked secret inputs, the route gate shared by the guard and the
    // quick actions, and the hide/show-modules diagnostics; less the module
    // manifest, the AI bridge members nothing called, the component resolver,
    // and the chart placeholders and key catalog removed alongside. The fixes
    // after it add 0.21 kB: the chat config probed again when the shell
    // switches users, the sign-in modules submitting once per attempt, array
    // reads that reject a refusal, channel rules keyed by their position, and
    // surrogate-safe cuts in the detail badges and card excerpts. 0.50 kB of
    // headroom would fire on whichever change lands next.
    // 696 -> 704 kB (2026-10-21). Measured 695.09 kB with this batch's ui-admin
    // sources swapped back to HEAD and 701.90 kB with them (+6.81 kB): AI usage
    // limits replacing the token quotas - the two hostable panels (everyone's
    // defaults; one user's usage per window and own settings), the shared policy
    // form, the rewritten page (defaults / users / budget) and both locale
    // blocks. A new surface. The panels are root exports loaded with `import()`
    // (ignored by the shell budget above), so the shell moved 0.23 kB
    // (218.01 -> 218.24 kB); as static exports they had cost it 4.99 kB.
    // 704 -> 710 kB (2026-10-26). Measured 704.32 kB with the subscription
    // plans route removed and 707.03 kB with it (+2.71 kB): the plan catalogue
    // page (cards, create / edit form, deactivate / activate, drag reorder),
    // its bridge and config. Both locale blocks (about 1.1 kB gzip each) were in
    // both measurements, which is why the tree was already 0.32 kB over without
    // the page. A new surface for backend capability that had no page; the
    // route sits behind `import()`, so the shell did not move.
    // 710 -> 720 kB (2026-10-26). Measured 718.59 kB: the customer billing
    // surface (subscription, payment methods and billing history sections, the
    // Stripe card form, the suspended screen and its strings), all lazy chunks
    // that download only when billing renders. A new surface.
    // 720 -> 725 kB (2026-10-26). Measured 721.87 kB (+3.28 kB): starting a
    // subscription from the billing section - the plan list (about 1.25 kB),
    // the subscribe dialog (about 2.0 kB), their strings and the section's
    // empty / pending states. Lazy billing chunks again; the shell stayed at
    // 219.22 kB.
    // 725 -> 728 kB (2026-10-27). Measured 725.12 kB (no before-measurement
    // taken) with plan storage allowances and the storage meter: the plan
    // form's storage section and GB editor, the single-plan offer, the
    // section's usage row, the history's overage note, their strings, and the
    // core storage-unit helpers and storage-usage client. Lazy chunks again;
    // the shell stayed at 219.21 kB (219,213 B, as before).
    limit: '728 kB',
    gzip: true,
  },
]
