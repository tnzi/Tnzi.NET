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

// Re-measured 2026-10-12 on the final tree of the audit round (`pnpm build`,
// then `pnpm size`), against the round's start (ee8187699, 2026-10-03) built
// the same way. Every limit stays; the closest is `chat`, 1.38 kB under.
//
//   index        199.85 -> 201.56 kB    auth        5.95 -> 6.07 kB
//   components   186.19 -> 187.05 kB    adapters    0.45 -> 0.45 kB
//   workflow      77.84 ->  77.77 kB    plugin      6.51 -> 6.56 kB
//   chat          86.13 ->  86.62 kB    i18n        3.78 -> 3.56 kB
//   embed         65.13 ->  66.16 kB    locales     7.12 -> 6.68 kB
//   headless      61.92 ->  62.73 kB    utils       0.91 -> 0.92 kB
//                                       theme       1.05 -> 1.38 kB
//
// Read off a per-module breakdown of the two builds. The dictionaries came
// down: the `admin` and `persona` sections nothing read were removed, which
// outweighs the composer, thread, artifact and embed labels that moved into
// them from English literals. The `i18n` / `locales` limits stay where the
// 2026-09-27 note put them; that left 0.22 / 0.38 kB of headroom, and the
// current 0.44 / 0.82 kB is what the next batch of labels will need. Weight
// went on in the composer and prompt input (attachment refusals, IME and
// streaming Enter handling), the markdown pipeline, the theme snapshot the
// shell and `createTnziChat` share, the embed widgets' naive theme and file
// icons, the workflow canvas's status-card nodes and animated edges, and the
// auth page's captcha widget.
//
// 2026-10-19, measured against HEAD (5bac70bdf) built the same way:
//
//   index        204.82 -> 207.65 kB    i18n        3.84 -> 4.20 kB
//   components   186.96 -> 189.30 kB    locales     7.25 -> 8.01 kB
//   chat          85.86 ->  88.07 kB    headless   59.54 -> 60.43 kB
//
// Four limits go up (index 205 -> 208, chat 88 -> 89, i18n 4 -> 4.5, locales
// 7.5 -> 8.5), which before this change already had 0.18 / 2.14 / 0.16 /
// 0.25 kB left. Most of the
// growth is text: voice input used to have one hard-coded English sentence and
// silently dropped every other failure, and now every way the recogniser stops
// has a sentence saying what happened and what to do, in both catalogues (the
// English one also rides in every barrel as the i18n fallback). The rest is the
// thread-opening state (`TThreadLoading`, the highlighted row), the optional
// answer byline, the microphone permission probe, the settings dialog's narrow
// sheet, and the markdown code block's copy button and the loader speaking the
// interface language (they were English literals; the button's labels now
// travel through markdown-it's render env and follow a language switch).
// `chat` crossed 88 kB by 74 B with that last item and goes to 89; it has
// 0.93 kB left, so the next addition to the shell argues for its weight or
// takes some off.
//
// 2026-10-20, measured against HEAD (e55147c60) built the same way:
//
//   index        207.93 -> 208.02 kB    auth       10.81 -> 10.93 kB
//   components   189.62 -> 189.69 kB    plugin     11.50 -> 11.65 kB
//   chat          88.33 ->  88.41 kB
//
// `index` goes 208 -> 209 kB: it had 70 B left (the agent mark took the rest)
// and the brand wordmark adds 90 B. That is the option on the three surfaces
// that show the brand name (the shell's sidebar header, the guest welcome bar,
// the sign-in page) and its pass-through from `defineChatApp`: one more
// template branch each, drawing the logotype with the name kept as hidden text.
// `chat` now has 0.59 kB left.
//
// 2026-10-21, measured on the change; the "before" figures are the 2026-10-20
// readings above plus the wordmark's ~90 B, not a fresh build of the parent:
//
//   index        ~208.1 -> 211.48 kB   components  ~189.8 -> 193.14 kB
//   chat          ~88.5 ->  92.20 kB   locales       (n/a) ->   8.22 kB
//
// `index` goes 209 -> 212 kB and `chat` 89 -> 93 kB. The growth is tool calls
// read as steps instead of payloads: the step shell and its three body parts
// (`TToolStep`, `TToolFields`, `TToolLinkList`, `TToolOutput`, exported so a
// product's own tool renderers are drawn the same way), the reading of a
// call's input and result (named lines, JSON results as pages and lines,
// `tool-views.ts`) and the views of the Claude Code CLI's `WebSearch` and
// `WebFetch` with the parser for the CLI's result text (`web-tools.ts`). The
// generic card they replace carried none of that, and every answer that uses a
// tool renders through it, so it rides in `chat`. Making the two web views
// lazy would not show here (esbuild inlines `import()` with splitting off).
//
// Then, the same day: tool calls made back to back read as one line.
//
//   index        211.48 -> 213.86 kB   components  193.14 -> 195.46 kB
//   chat          92.20 ->  94.60 kB   locales       8.22 ->   8.43 kB
//
// `index` 212 -> 215, `components` 195 -> 197, `chat` 93 -> 96 kB. The weight
// is `TToolRun` (the line, the box of rows, the outlet that keeps what a step
// handed over in view), the summary by kind and the step words with their
// defaults (`tool-steps.ts`), grouping the reply into text and runs, the
// registration entries (`describe`, `alwaysVisible`) and the words of the two
// CLI web tools, plus the run's summary words in both catalogues. `chat` and
// `index` have about 1.4 kB left, `components` 1.5 kB. `i18n` (4.43 / 4.5) and
// `locales` (8.43 / 8.5) are now nearly full: the next batch of labels moves
// them.
//
// 2026-10-21, usage limits and the model picker, measured with this batch's
// ui-ai sources swapped back to HEAD and built the same way:
//
//   index        213.87 -> 218.16 kB   i18n         4.43 ->  4.73 kB
//   components   195.48 -> 198.79 kB   locales      8.43 ->  9.03 kB
//   chat          94.61 ->  97.91 kB   headless    60.58 -> 62.22 kB
//
// `index` 215 -> 219, `components` 197 -> 200, `chat` 96 -> 99, `i18n`
// 4.5 -> 5, `locales` 8.5 -> 9.5 kB. The weight is the usage limits the user
// sees: the settings page as one bar per window with its reset (instead of
// token counts), the notice that says a limit was reached and when it resets
// (above the composer, and on a reply a limit stopped), the reading of a
// refused turn in `useChatThreads` (its rows come off the screen and the text
// goes back), the shared reset and countdown wording (`usage-limits.ts`), and
// the composer's model picker with `useChatModels`; plus their words in both
// catalogues (English rides in every barrel as the fallback).
//
// 2026-10-21, the sidebar's recent conversations, measured on the change
// against the usage-limits readings above:
//
//   index        218.16 -> 226.15 kB   i18n         4.73 ->  5.01 kB
//   components   198.79 -> 205.53 kB   locales      9.03 ->  9.57 kB
//   chat          97.91 -> 104.85 kB   headless    62.22 -> 64.03 kB
//
// `index` 219 -> 227, `components` 200 -> 207, `chat` 99 -> 106, `i18n`
// 5 -> 5.5, `locales` 9.5 -> 10 kB. The weight is the conversation list as a
// working list rather than a column of titles: the menu it is driven through
// (`TActionMenu`: on `<body>` so a row near the sidebar's edge can open one,
// arrow-key navigation, submenus in place), the list's header (fold, View all,
// search, the filter / group / sort menu), row rename / archive / delete, date
// grouping and the full Recents page (`TRecentsPage`), the preferences and the
// list query (`recents.ts`), and the controller in `useChatThreads` that keeps
// the sidebar and the page in step; plus their words in both catalogues. The
// Recents page rides in `chat` because the shell renders it for its `recents`
// view; a lazy import would not show here (esbuild inlines `import()` with
// splitting off), though a consumer's bundler would split it.
//
// Then, the same day: the phone layout (compact landing and composers, the
// drawer that is never the rail, `preventPageZoom`) fit inside those limits;
// the editable top-bar title (`TThreadTitle`: in-place rename, its menu, the
// inline delete confirmation) and the Recents page's inline search did not.
//
//   index        226.15 -> 228.09 kB   chat        104.85 -> 106.14 kB
//
// `index` 227 -> 229 and `chat` 106 -> 107 kB. The title rides in `chat`
// because the shell renders it for every open conversation.
//
// 2026-10-22, the sent turn brought to the top with the reply growing beneath
// it (`useTurnAnchor`: a reserve under the message, measured by a
// ResizeObserver, one scroll per send), the "scroll to the latest" button, the
// phone shell pinned over the visual viewport (`usePinnedViewport`), the touch
// size tokens and the editor / title field styles:
//
//   index        228.09 -> 229.53 kB   chat        106.14 -> 107.58 kB
//   components   208.2 kB (over its 207 kB limit)
//
// `index` 229 -> 230, `components` 207 -> 209, `chat` 107 -> 108 kB.
//
// Then the "scroll to the latest" button measured against the end of the last
// message instead of the end of the scroller (`useLatestInView`: a
// ResizeObserver on the scroller, its content and the last message, so a tool
// step collapsed with nothing scrolled takes the button down):
//
//   index        229.69 -> 230.41 kB   chat        107.72 -> 108.47 kB
//   components   208.35 -> 209.17 kB
//
// `index` 230 -> 231, `components` 209 -> 210, `chat` 108 -> 109 kB.
//
// Then message markdown restyled after claude.ai's rendering: GFM task lists
// as disabled checkboxes (a markdown-it core rule, no plugin dependency), the
// code block's icon-only copy button (two inline SVGs in the rendered HTML) and
// the rewritten prose styles:
//
//   index        230.41 -> 231.09 kB   chat        108.47 -> 109.17 kB
//
// `index` 231 -> 232, `chat` 109 -> 110 kB.
//
// 2026-10-22, inline widgets (`show_widget`): the frame, the host bridge and
// the in-frame bootstrap are their own entry, `@tnzi/ui-ai/widget` (about
// 7 kB that the root and `components` barrels would otherwise carry for every
// product, widgets or not), budgeted below. What still reaches the other
// barrels is the widget words in both catalogues (English rides in every
// barrel as the fallback), which put three of them just over:
//
//   components   210.02 kB   auth   12.00 kB (+1 B)   locales   10.03 kB
//
// `components` 210 -> 211, `auth` 12 -> 12.5, `locales` 10 -> 10.5 kB.
// 2026-10-23: the shell gained the right-side panel frame (`TAsidePanel`, resize handle with
// keyboard support, narrow-screen layer) and its per-conversation state (`useAsidePanel`):
//
//   index        232 -> 233.68 kB   components   211 -> 212.04 kB   chat   110 -> 111.4 kB
//
// `index` 232 -> 235, `components` 211 -> 214, `chat` 110 -> 113 kB.
// 2026-10-25: `useChatThreads` no longer fails a turn whose stream broke for a network
// reason: it picks a run up after the last event seen or reads the thread back, again when
// the page is shown, back online or focused, and Retry asks the server before re-running:
//
//   index        233.67 -> 235.14 kB   chat   111.4 kB (within)
//
// `index` 235 -> 237 kB.
export default [
  // Ratcheted down on 2026-08-02 after the @vue-flow leak was closed: these
  // read 254.6 / 238.4 kB while `components/index.ts` still re-exported the
  // workflow SFCs, and 192.78 / 182.84 kB once it stopped. Leaving the old
  // limits in place would have handed the ~60 kB straight back - a budget with
  // 85 kB of slack cannot notice the dependency creeping in again.
  budget('index (whole package barrel)', 'dist/index.js', '237 kB'),
  budget('components barrel', 'dist/components.js', '214 kB'),
  // Reached from ui-admin's WorkflowEditor, which lazy-loads it; pulls @vue-flow.
  // Previously had no budget at all.
  budget('workflow barrel (@vue-flow canvas)', 'dist/workflow.js', '90 kB'),
  // Inline widgets: the frame (sandboxed runner iframe, lazy mount), the host
  // bridge and the bootstrap written into each widget's document, plus the
  // tool step shell they fall back to. Measured 19.48 kB on 2026-10-22.
  budget('widget barrel (inline show_widget frames)', 'dist/widget.js', '21 kB'),
  // 85 -> 88 kB on 2026-08-05: the shell gained three wired settings pages
  // (Account / Security / Personalization, which call the framework's own
  // user-facing routes) plus the naive theme provider that makes every control
  // inside this package follow the product's palette. Both are capability the
  // barrel did not have, not creep - but the slack is small, so a further
  // increase should be argued rather than nudged.
  budget('chat barrel (TChatApp + markdown pipeline)', 'dist/chat.js', '113 kB'),
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
  budget('auth barrel (sign-in / sign-up page)', 'dist/auth.js', '12.5 kB'),
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
  // 3 -> 4 kB and 6 -> 7.5 kB (2026-09-27): measured 3.78 / 7.12 kB after the
  // four settings panels (account, security, personalization, usage) moved
  // their user-facing text into the catalogues. Before that those strings
  // were English literals in the SFCs, shipped in every locale; the growth is
  // the text moving here, not new weight.
  budget('i18n engine (bundles `en` as the fallback catalogue)', 'dist/i18n.js', '5.5 kB'),
  budget('locales barrel', 'dist/locales.js', '10.5 kB'),
  // Tiny today. Budgeted anyway because a `utils` barrel is the easiest place
  // for something heavy to be dropped without anyone noticing.
  budget('utils barrel', 'dist/utils.js', '3 kB'),
  budget('theme barrel', 'dist/theme.js', '3 kB'),
]
