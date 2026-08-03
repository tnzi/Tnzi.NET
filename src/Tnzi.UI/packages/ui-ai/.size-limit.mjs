/**
 * Size budgets for `@tnzi/ui-ai`.
 *
 * ## Why `shiki` is externalised from every budget
 *
 * size-limit bundles the whole reachable graph and **follows `import()`**, and
 * `shiki` alone is ~1.67 MB gzipped. But nothing here imports it eagerly:
 * `useStreamMarkdown` and `useCodeHighlight` both reach it through
 * `await import('shiki')`, so a page that never renders a highlighted code
 * block never downloads it.
 *
 * Leaving it in produced numbers nobody could act on, in both directions:
 *
 *   headless      1.73 MB against a 70 kB limit   - actually 59.9 kB
 *   embed         1.74 MB against an 80 kB limit  - actually 67.0 kB
 *   components    1.91 MB against a  2 MB limit   - actually 238.4 kB
 *   chat          1.74 MB against a 1.8 MB limit  - actually 74.0 kB
 *
 * The first two read as blown by 1.66 MB and were in fact comfortably inside
 * their (correctly chosen) limits. The other two read GREEN while sitting 8x
 * and 24x under a limit that had been inflated to accommodate the same phantom
 * weight - so `components` could have grown from 238 kB to 1.9 MB without
 * tripping anything. **A false green is worse than a false red**: the red at
 * least gets looked at.
 *
 * Externalising `shiki` is not slack. It is the difference between measuring
 * "everything this barrel can eventually pull" and "what the browser fetches to
 * render it".
 *
 * ## What is NOT externalised
 *
 * `markdown-it` stays in, because `useStreamMarkdown` imports it eagerly - it is
 * ~52.8 kB of the headless barrel's 59.9 kB and a consumer really does pay it
 * up front. Making it lazy would turn a synchronous composable asynchronous (a
 * breaking change) to save 52.8 kB that is already inside budget, so it is a
 * known future option rather than a defect.
 *
 * ## What these budgets cannot catch
 *
 * Turning a lazy `import()` into a static import would not move any number here:
 * externalised either way, and esbuild inlines `import()` when splitting is off
 * so the totals are identical too. Guard laziness with a convention test, not a
 * budget. (See `@tnzi/ui-admin`'s publicApi.test.ts for the pattern.)
 *
 * Measured 2026-07-31. Every limit is a ratchet: tighten as weight comes off,
 * and never raise one to turn a red run green without saying why in the same
 * commit.
 */

/** Lazily fetched by the markdown/code-highlight composables, never eagerly. */
const LAZY_SYNTAX_HIGHLIGHTER = ['shiki']

const budget = (name, path, limit) => ({
  name,
  path,
  ignore: LAZY_SYNTAX_HIGHLIGHTER,
  limit,
  gzip: true,
})

export default [
  // Ratcheted down on 2026-08-02 after the @vue-flow leak was closed: these
  // read 254.6 / 238.4 kB while `components/index.ts` still re-exported the
  // workflow SFCs, and 192.78 / 182.84 kB once it stopped. Leaving the old
  // limits in place would have handed the ~60 kB straight back - a budget with
  // 85 kB of slack cannot notice the dependency creeping in again.
  budget('index (whole package barrel)', 'dist/index.js', '205 kB'),
  budget('components barrel', 'dist/components.js', '195 kB'),
  // Reached from ui-admin's WorkflowEditor, which lazy-loads it; pulls @vue-flow.
  // Previously had no budget at all.
  budget('workflow barrel (@vue-flow canvas)', 'dist/workflow.js', '90 kB'),
  budget('chat barrel (TChatApp + markdown pipeline)', 'dist/chat.js', '85 kB'),
  // The embeddable widgets. Their whole value proposition is dropping onto a
  // third-party page, so this is the budget that matters most to defend.
  budget('embed barrel (floating / sidebar / inline chat)', 'dist/embed.js', '80 kB'),
  budget('headless barrel (incl. eager markdown-it ~52.8 kB)', 'dist/headless.js', '70 kB'),
  // The `shell` barrel is gone as of 2026-08-02. Its seven components (sidebar,
  // nav, rail, thread list, command palette, settings dialog, landing page) are
  // region frames, and `components/layout` + `components/overlay` are already
  // the structural domains for exactly that - the two rules ("frames a region
  // of an app shell" vs "frames a screen") could not be told apart, which is
  // how TSettingRow/TSettingGroup ended up in one and TSettingsDialog in the
  // other. They now sit in `components/*` and are covered by that budget.
  // Nothing outside this package imported `@tnzi/ui-ai/shell`.
  // The pre-auth surface. This is the one entry a visitor loads before they
  // have an account, on whatever connection they happen to be on, so it gets
  // its own budget rather than hiding inside the root barrel. It must stay far
  // below `chat`: nothing about signing in should pay for the markdown
  // pipeline or the conversation tree.
  budget('auth barrel (sign-in / sign-up page)', 'dist/auth.js', '12 kB'),
  // DTO -> view-model mapping. Pure functions; if this ever approaches the
  // budget something with a component in it has been added to the barrel.
  budget('adapters barrel', 'dist/adapters.js', '3 kB'),
  // Application assembly. Lazy-loads the home component by contract, so this
  // should stay near the auth page's weight, not the chat tree's.
  budget('plugin barrel (defineChatApp)', 'dist/plugin.js', '14 kB'),
  // The engine defaults to `en` for both `createAiI18n()`'s parameter and
  // `useAiI18n()`'s fallback, so the English catalogue rides along (measured
  // 2.5 kB against the 4.51 kB `locales` barrel). That fallback is pre-existing
  // behaviour carried over from when engine and dictionaries shared one file;
  // splitting them buys the *import graph* separation, not a smaller engine.
  budget('i18n engine (bundles `en` as the fallback catalogue)', 'dist/i18n.js', '3 kB'),
  budget('locales barrel', 'dist/locales.js', '6 kB'),
  // Tiny today. Budgeted anyway because a `utils` barrel is the easiest place
  // for something heavy to be dropped without anyone noticing.
  budget('utils barrel', 'dist/utils.js', '3 kB'),
  budget('theme barrel', 'dist/theme.js', '3 kB'),
]
