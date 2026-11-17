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
// 2026-11-01: sidebar rows reveal a truncated title by scrolling it under the pointer
// (`vTitleReveal` / `createTitleReveal`, plus its edge-fade CSS), and the account button
// gained its hover / focus states:
//
//   index        236.43 -> 237.08 kB   components   213.12 -> 213.8 kB (within)   chat   112.98 kB (within)
//
// `index` 237 -> 238 kB.
// 2026-11-01 (pt2): nav icons play a one-shot hover motion (`vIconMotion` / `createIconMotion`,
// five keyframe sets in the stylesheet, `NavItem.iconMotion` and the shell's built-in motions):
//
//   index        237.08 -> 237.43 kB (within)   components   213.8 -> 214.14 kB   chat   112.98 -> 113.27 kB
//
// `components` 214 -> 215, `chat` 113 -> 114 kB.
// 2026-11-01 (pt3): the Account page gained the profile photo (pick, centre-crop on a canvas,
// upload through `useStorageApi`, save) and the "Delete account" group (typed confirmation,
// step-up beside the button), plus 18 strings in each catalogue. `useStorageApi` is a factory
// object, so the whole storage client comes along; it is still the only path to `files/upload`
// the frontend/backend contract gate can see, so it is not hand-written around:
//
//   index        237.43 -> 240.3 kB   components   214.14 -> 217.03 kB   chat   113.27 -> 116.01 kB   headless -> 71.09 kB
//   auth -> 13.17 kB   i18n -> 5.72 kB   locales -> 10.97 kB   (en catalogue: +18 keys)
//
// `index` 238 -> 241, `components` 215 -> 218, `chat` 114 -> 117, `headless` 70 -> 72, `auth` 13 -> 13.5,
// `i18n` 5.5 -> 6, `locales` 10.5 -> 11 kB.
// 2026-11-01 (pt4): `TDialog`, the package's general dialog (focus placement, Escape / backdrop
// switches, open-order stacking, sheet and full-screen layouts, the scroll hairlines), and the
// shared dialog chrome in `components/overlay/dialog.css`. The full view of an inline widget is
// now a `TDialog` instead of a hand-drawn overlay, so the widget entry carries it (A/B on that
// one file: 20.02 -> 21.6 kB); an app that also uses `TDialog` loads it once:
//
//   index        240.3 -> 241.85 kB   components   217.03 -> 218.62 kB   widget   20.02 -> 21.6 kB
//   chat   116.17 kB (within)
//
// `index` 241 -> 243, `components` 218 -> 220, `widget` 21 -> 22.5 kB.
// 2026-11-01 (pt5): the page toolbar (`TPageToolbar`, `TToolbarSearch`, `TSegmented`, the
// fold of its icon actions into a "more" menu, the global `page-toolbar.css`), one layering
// scale (`overlay/layers.ts`: the dialog stack by depth, used by the settings dialog and the
// palette too), `TPopoverMenu` placed `position: fixed` by script, and a `toolbar` section in
// both catalogues (English rides in every barrel as the fallback):
//
//   index        241.85 -> 245.45 kB   components   218.62 -> 222.07 kB   chat   116.17 -> 116.95 kB
//   widget       21.6 -> 22.07 kB      headless     71.25 -> 71.6 kB      plugin 14.53 kB   locales 11.04 kB
//
// `index` 243 -> 246, `components` 220 -> 223, `plugin` 14.5 -> 15, `locales` 11 -> 11.5 kB.
// `chat` is 0.05 kB under its 117: the next weight there has to be argued.
// 2026-11-01 (pt6): one focus style for form controls. The shell's naive theme provider
// (`useAiNaiveTheme`) now lays `AI_FOCUS_THEME_OVERRIDES` over the host's overrides, so every
// naive field, select, picker, checkbox and switch focuses with `--tnzi-ai-focus-*` instead of
// the raw brand colour (unreadable for a light brand); nine focus keys in `TOKEN_TO_VAR`:
//
//   chat   116.95 -> 117.34 kB   headless   71.6 -> 72.0 kB (at its limit)   index 245.83   components 222.51
//
// That is the weight argued for: `chat` 117 -> 118 kB.
// 2026-11-01 (pt7): what migrating real content pages onto the toolbar asked for: per-element
// attributes (`attrs` on actions, items and the primary; search / more attrs), printable folded
// ids (`<actionId>::<itemId>`), `maxlength` and the package's own clear button on the search,
// one action on a phone not folded, the chosen tab scrolled into view (`scrollLeftToReveal`), no
// keyboard ring on a pointer-opened menu, `bodyPaddingBottom`; one more catalogue string:
//
//   index        245.83 -> 246.74 kB   components   222.51 -> 223.31 kB   headless   72.0 -> 72.01 kB
//   chat         117.34 -> 117.43 kB (within)
//
// `index` 246 -> 247, `components` 223 -> 224, `headless` 72 -> 72.5 kB.
// 2026-11-01 (pt8): `TDialog` opens from an element and closes back into it (`origin`; FLIP of
// the panel, contents fading, backdrop in step; reduced motion fades; falls back to the rise when
// the origin is gone), `beforeClose(reason)` for every close it asks for, `transition="none"`,
// `panel` exposed (`overlay/dialog-motion.ts`, Web Animations). The widget entry carries TDialog:
//
//   index        246.74 -> 248.25 kB   components   223.31 -> 224.65 kB   widget   22.07 -> 23.52 kB
//
// `index` 247 -> 249, `components` 224 -> 225.5, `widget` 22.5 -> 24 kB.
// 2026-11-04: moving to another conversation no longer stops the turn on screen (`useChatThreads`
// keeps a stop and a leave apart, and holds a request with no run named yet until its first sign),
// and a run waiting on background tasks has its own row state (`status: 'waiting'`, the tasks, one
// catalogue string). `index`, `components` and `chat` were already over before this batch:
//
//   index        249.97 -> 250.8 kB   components   225.94 -> 226.16 kB   chat   118.69 -> 118.83 kB
//   headless     72.28 -> 72.84 kB    widget       23.99 -> 24.01 kB
//
// `index` 249 -> 251.5, `components` 225.5 -> 226.5, `chat` 118 -> 119, `headless` 72.5 -> 73,
// `widget` 24 -> 24.5 kB.
// 2026-11-04: chat scrolling (`useAutoScroll`) follows by the reader's input instead of by position
// (wheel / touch / keys / scrollbar / disclosures, nested scrollers, same-frame ResizeObserver
// pins, a limit at the sent message for `scrollMode: 'chat'`), and `TChatBox` detects a send.
// `useTurnAnchor` is still exported, so the root and headless barrels carry both models:
//
//   index        250.98 -> 252.28 kB   components   226.33 -> 227.19 kB   chat   118.98 -> 119.53 kB
//   headless     72.95 -> 74.01 kB
//
// `index` 251.5 -> 252.5, `components` 226.5 -> 227.5, `chat` 119 -> 120, `headless` 73 -> 74.5 kB.
// 2026-11-04 (composer): the composer's microphone, send and the shell's stop become one primary
// button (`composerPrimaryAction`, exported; `running` / `stop` on `TThreadComposer`; dictation
// ended on send and when a turn starts; its state styles). `TChatApp` drops its own stop button:
//
//   index        250.98 -> 251.36 kB   components   226.33 -> 226.65 kB   chat   118.98 -> 119.25 kB
//
// `components` 226.5 -> 227, `chat` 119 -> 119.5 kB.
// 2026-11-04 (rail): the collapsed rail labels every control with a tooltip (`vTooltip` /
// `createTooltip`: delay and skip window, keyboard-only focus via input modality, one fixed bubble
// on <body>, placement with flip) and gains a recents flyout (`TRailFlyout` with its hover / focus /
// pin rules in `createFlyoutController`, `TRailThreadList`), plus `TRailButton`, which the rail's nav
// items now use. Measured against the previous commit's build:
//
//   index        252.68 -> 256.46 kB   components   227.56 -> 230.86 kB   chat   119.94 -> 123.80 kB
//   headless     74.01 -> 75.61 kB
//
// `index` 253 -> 257, `components` 228 -> 231.5, `chat` 120.5 -> 124.5, `headless` 74.5 -> 76 kB.
// 2026-11-04 (sidebar auto-fit): the sidebar folds into the rail when the conversation has too
// little room (`useSidebarAutoFit`, `conversationRoom`, `decideSidebarFit`; `useSidebarState` gains
// `autoCollapsed` / `choices` / `applyAutoMode`; `TChatApp` measures its own width with a
// ResizeObserver). Measured against the rail commit's build:
//
//   index        256.46 -> 257.21 kB   components   230.86 -> 231.56 kB   chat   123.80 -> 124.57 kB
//   headless     75.61 -> 76.15 kB
//
// `index` 257 -> 257.5, `components` 231.5 -> 232, `chat` 124.5 -> 125, `headless` 76 -> 76.5 kB.
// 2026-11-04 (dialog keyboard): a sheet or full-screen `TDialog` keeps to the visual viewport
// (`useVisualViewportInsets`, exported, and the focused field revealed when the space changes), so
// it stays above the phone keyboard. `TDialog` rides in the root, components and widget entries.
// Measured against the previous commit's build:
//
//   index        257.29 -> 257.78 kB   components   231.58 -> 231.98 kB   widget   24.02 -> 24.37 kB
//   headless     76.15 -> 76.46 kB (within)
//
// `index` 257.5 -> 258, `components` 232 -> 232.5 kB (20 B of headroom left otherwise).
//
// 2026-11-05, phone shell (back closes layers, view scroll memory, touch press): measured before ->
// after: index 257.78 -> 260.81, components 231.98 -> 234.89, widget 24.37 -> 26.32, chat
// 124.68 -> 127.83, headless 76.46 -> 78.49 kB. `headless/back-layers` is ~2.1 kB of it (the
// history model, the held history calls, the address check), reached by every overlay that takes
// the phone's back (`TDialog`, which is why the widget barrel moves, the menus, the drawer, the
// side panel); `useScrollMemory` ~0.6 kB and the shell's wiring the rest. Limits raised to keep the
// previous headroom: index 258 -> 261.5, components 232.5 -> 235.5, widget 24.5 -> 26.5, chat
// 125 -> 128.5, headless 76.5 -> 79 kB. `plugin` only claims the first `popstate` listener (a
// 0.2 kB module) and stays inside its limit.
//
// 2026-11-05, no forward history on a narrow screen (the back-layers step that takes an entry out
// and pushes it back) and the composer's `sendIcon`: measured against the phone shell figures
// above, index 260.81 -> 262.12, components 234.89 -> 236.33, widget 26.32 -> 27.54, chat
// 127.83 -> 129.3, headless 78.49 -> 79.69 kB. All but ~0.06 kB of it is `headless/back-layers`
// (the replayed push and its guard), which every overlay taking the phone's back reaches; the send
// icon is one prop through TChatApp -> TLandingPage -> TThreadComposer and TGuestWelcome (index,
// chat +0.06, auth +0.02, plugin +0.03 kB). Limits raised to keep about the previous headroom:
// index 261.5 -> 263, components 235.5 -> 237.5, widget 26.5 -> 28, chat 128.5 -> 130, headless
// 79 -> 80.5 kB. `auth` (13.49 of 13.5) and `plugin` (14.95 of 15) stay inside their limits.
//
// 2026-11-06, account-menu submenus that fly out beside the menu with a mouse: measured against
// HEAD (d6d730ca4) built the same way, index 262.39 -> 264.73, components 236.53 -> 239.03, chat
// 129.58 -> 132.03 kB. It is `TUserMenu` alone: the hover intent and grace triangle
// (`headless/submenu-intent`, not exported from the headless barrel, which is unchanged), the
// fly-out's placement, its keyboard (open, move, back) and the second item list it renders.
// Limits raised to keep about the previous headroom: index 263 -> 265.5, components 237.5 -> 240,
// chat 130 -> 132.5 kB.
//
// 2026-11-07, a message's image opens full size in a viewer over the page instead of a new tab
// (a just-sent image is a data: URL, which browsers will not open as a page; an app embedding
// the UI hands new windows to a browser where the page's blob: URLs do not exist). Measured
// against HEAD (4f33ebdde) built the same way: index 264.73 -> 266.59, components 239.03 ->
// 239.36, widget 27.76 -> 28.18, chat 132.03 -> 135.96, embed 73.64 -> 80.93, auth 13.49 ->
// 13.51 kB. Almost all of it is `TDialog` (focus trap, motion, phone back layer, keyboard-aware
// sheet), which `TAttachmentImage` reaches through `defineAsyncComponent(() => import(...))`:
// size-limit inlines that import, a browser fetches it on the first click. So these numbers
// overstate what a page downloads up front; `conventions/lazy-attachment-viewer.test.ts`
// keeps the import lazy, since no budget here could notice it turning static. `auth` grew by
// the viewer's fallback label in the `en` catalogue. Limits: index 265.5 -> 267.5, widget
// 28 -> 28.5, chat 132.5 -> 136.5, embed 80 -> 81.5, auth 13.5 -> 13.6 kB.
//
// 2026-11-08, the page-wide marker (`headless/app-root.ts`, `markAiApp`): `defineChatApp` and
// `TChatApp` put `data-tnzi-ai-app` on <html> so the body face, focus line and thin scrollbars
// apply to the app's whole page and to no other. Measured over the previous limits: components
// +0.05, chat +0.02, plugin +0.15 kB, the counted mark and its release. Limits: components
// 240 -> 240.5, chat 136.5 -> 137, plugin 15 -> 15.5 kB.
//
// 2026-11-08, the controls domain (`components/controls`: TButton, TIconButton, TField, TSelect,
// TMultiSelect, TSwitch, TTag, TNotice, TSpinner) and TDropdownMenu / TMenuItem / TMenuSeparator,
// reached from both barrels: index 267.48 -> 271.98, components 240.05 -> 244.97 kB. New
// capability, nine components and three menu parts at about 0.4 kB each. Limits: index 267.5 ->
// 272.5, components 240.5 -> 245.5 kB. The new `controls` entry gets its own budget: it exists to
// stay small (only vue and @iconify/vue behind it): 11.75 kB measured, limit 12.5 kB.
//
// 2026-11-08, states and feedback in the controls domain: TStateView, TEmptyState, TEmptyArt (13
// line drawings, 2.4 kB of it), TTemplateRow, the self-drawn toast (toast.ts + TToastHost) and,
// in overlay, useConfirm + TConfirmHost; TChatApp mounts both hosts; TResourceEmpty is gone.
// Measured: index 271.98 -> 276.9, components 244.97 -> 250.05, controls 11.75 -> 16.66, chat
// 136.9 -> 138.9 kB (the two hosts; TDialog and TButton were already there). New capability, not
// creep. Limits: index 272.5 -> 277.5, components 245.5 -> 250.5, controls 12.5 -> 17.5, chat
// 137 -> 139.5 kB.
// 2026-11-08, the cards a conversation waits on its user for (permission requests, questions,
// plans): `TApprovalCard`, `TQuestionCard`, `TPlanCard` and the list `TPendingApprovals`, drawn by
// `TChatApp` above the composer; the queue (`approval-queue.ts`: sources, polling while a turn
// runs, pushed items, answers), the framework's source and default card words
// (`run-requests.ts`), `useChatThreads` feeding it from the stream, and an `approvals` section in
// both catalogues (English rides in every barrel as the fallback, which is all that moves
// `auth`, `plugin`, `widget`, `workflow` and `embed`). Measured against HEAD (e804321b1) built
// the same way: index 277.31 -> 285.28, components 250.41 -> 256.51, chat 139.28 -> 145.23,
// headless 80.07 -> 83.06, auth 13.52 -> 13.98, plugin 15.13 -> 15.61, widget 28.2 -> 28.66,
// i18n 5.98 -> 6.44, locales 11.46 -> 12.38, embed 80.93 -> 81.31 kB. New capability: a
// shell that could not show what the agent waits for. The cards are not loaded lazily: the
// first one must appear the moment a request arrives, and size-limit could not see the
// difference anyway (it inlines `import()`). Limits: index 277.5 -> 286, components 250.5 -> 257,
// chat 139.5 -> 146, headless 80.5 -> 83.5, auth 13.6 -> 14, plugin 15.5 -> 16, widget 28.5 -> 29,
// i18n 6 -> 6.5, locales 11.5 -> 12.5 kB.
// 2026-11-08, the composer's `/` menu: `useSlashMenu` (query, ranking, keys), `TSlashCommandMenu`
// and `TChatApp`'s wiring (`slashCommands`: the menu in place of `#composer-suggestions`, keys
// before the composer, the combobox ARIA), a `slash` section in both catalogues and the
// `popoverShadow` token. Measured against the approval cards commit built the same way: index
// 285.28 -> 287.16, components 256.51 -> 258.41, chat 145.23 -> 147.06, headless 83.06 -> 83.52,
// auth 13.98 -> 14.08, i18n 6.44 -> 6.54, locales 12.38 -> 12.56 kB. Limits: index 286 -> 288,
// components 257 -> 259.5, chat 146 -> 148, headless 83.5 -> 84, auth 14 -> 14.5, i18n 6.5 -> 7,
// locales 12.5 -> 13 kB.
// 2026-11-08, the full-size image viewer becomes `TImageViewer` (a dark layer with the picture,
// its set's stepping and count, focus trap, scroll lock, phone back layer) in place of a `TDialog`,
// and the images of one message or one tool call form a set (`image-group.ts`); an
// `imageViewer` section in both catalogues and one token. Measured against the context meter
// commit built the same way: index 287.03 -> 288.09, components 258.19 -> 259.14, chat 147.06 ->
// 147.77, widget 28.75 -> 29.59, embed 81.39 -> 78.64 kB. `embed` comes down: its only path to
// `TDialog` was the attachment viewer, and the new viewer is lighter. `widget` goes up: it already
// carried `TDialog` for its own "open larger", and now the tool card's viewer too. Limits: index
// 288 -> 289, widget 29 -> 30 kB; embed ratchets 81.5 -> 79.5 kB.
//
// 2026-11-09, thread icons: `threadIcon` on TChatApp / TThreadList / TRailThreadList /
// TRecentsPage maps a conversation (its `origin`) to an icon before its title, drawn by the new
// TThreadIcon through `resolveThreadIcon` (thread-icon.ts); `useChatThreads` sends a declared
// origin. Measured against origin/main (3ed77ef25) built the same way: index 288.09 -> 288.57,
// components 259.14 -> 259.61, chat 147.77 -> 148.17, headless 83.75 -> 83.81 kB. New capability,
// not creep. Limits: components 259.5 -> 260, chat 148 -> 148.5 kB; index (289) and headless (84)
// still hold.
// 2026-11-09, the gaps pages found building on the controls alone: TSplitButton, TBadge,
// TInlineField, TContextMenu, TPopover (all in the controls entry), TDockedDialog (components
// only), useElementSize, a toast action, TButton soft / link / inverse, the tooltip side from
// `data-tooltip-side`, TPopoverMenu at a point, the shared menu keys. Measured against main
// (7d67e40c6, with the thread icons above) built the same way: index 288.57 -> 293.23,
// components 259.61 -> 263.2, controls 17.32 -> 20.36, chat 148.17 -> 148.48, headless 83.81 ->
// 84.25 kB (the tooltip side and useElementSize; chat carries the shared tooltip, popover menu and
// toast code). The other barrels move by 0.04 kB at most (two locale keys). New capability, not
// creep. Limits: index 289 -> 294, components 260 -> 264, controls 17.5 -> 21, chat 148.5 -> 149,
// headless 84 -> 84.5 kB.
// (3ed77ef25, with the approval cards, slash menu, context meter and image viewer above) built
// the same way: index 288.09 -> 292.72, components 259.14 -> 262.72, controls 17.32 -> 20.36,
// chat 147.77 -> 148.09, headless 83.75 -> 84.2 kB (the tooltip side and useElementSize; chat
// carries the shared tooltip, popover menu and toast code). The other barrels move by 0.04 kB at
// most (two locale keys). New capability, not creep. Limits: index 289 -> 293.5, components
// 259.5 -> 263.5, controls 17.5 -> 21, chat 148 -> 148.5, headless 84 -> 84.5 kB.
//
// 2026-11-09, four gaps after that: TDataGrid (sort cycle, row order, column widths by drag and
// keys, the grid keys) and TInfoTip in the controls entry; vTooltip's long press on a touch
// screen; the threads registry TChatApp picks its approval queue from; `.t-input--code`; three
// locale keys. Measured against the budget commit above built the same way: index 293.23 ->
// 297.05, components 263.2 -> 266.25, controls 20.36 -> 23.49, chat 148.48 -> 148.95, headless
// 84.25 -> 84.76 kB (the long press is in the tooltip every barrel carries). New capability, not
// creep. Limits: index 294 -> 297.5, components 264 -> 266.5, controls 21 -> 24, chat 149 ->
// 149.5, headless 84.5 -> 85 kB.
//
// 2026-11-09, fixes after a consumer's run on real pointers: the event-path recorder (a click's
// path kept before any handler re-renders), TSplitButton `mainDisabled`, toast handles with
// `update`, two actions, a busy spinner and quiet updates, TSelect `bare`. Measured against the
// budget commit above built the same way: index 297.05 -> 297.8, components 266.25 -> 266.98,
// controls 23.49 -> 24.01, chat 148.95 -> 149.59, headless 84.76 -> 84.98 kB. Limits: index
// 297.5 -> 298.5, components 266.5 -> 267.5, controls 24 -> 24.5, chat 149.5 -> 150, headless
// 85 -> 85.5 kB.
// 2026-11-09, a conversation's queued messages kept on the server: `createMessageQueue` (reads
// while a message waits, follows the conversation when one went out, edits, removals, send-now),
// `TQueuedMessages` over the composer (in-place editing, held messages with their reason,
// folding), `useChatThreads` queueing behind an external run and registering its queue with the
// approval cards, `TChatApp`'s wiring, and a `queue` section in both catalogues. Measured against
// main with the conversation layout defaults (0c8e1987b) built the same way: index 298.13 ->
// 301.4, components 267.25 -> 269.37, chat 149.85 -> 151.89, headless 85.03 -> 86.33, plugin
// 15.82 -> 16.04, locales 12.78 -> 13.23 kB (i18n 6.66 -> 6.88, auth 14.2 -> 14.43, widget 29.66 ->
// 29.87, workflow 80.91 -> 81.12 kB stay inside theirs). New capability: messages written while an
// agent works had to be kept and sent by each consumer. Not loaded lazily: the list shows the
// moment a message is queued, and size-limit inlines `import()` anyway. Limits: index 298.5 ->
// 301.5, components 267.5 -> 269.5, chat 150 -> 152, headless 85.5 -> 86.5, plugin 16 -> 16.5,
// locales 13 -> 13.5 kB.
//
// 2026-11-09 (B8): the package no longer uses naive-ui. size-limit leaves
// peers out, so naive's buttons, inputs, modals and QR code never counted;
// the package's own controls that replace them do. Measured against main at
// 5a7f0ed06 (the thread message queue) built the same way:
//
//   index       301.40 -> 306.03 kB    chat     151.89 -> 157.24 kB
//   components  269.37 -> 274.74 kB    embed     78.91 ->  79.91 kB
//   auth         14.43 ->  14.54 kB    widget    29.87 ->  29.98 kB
//   i18n          6.88 ->   6.99 kB    locales   13.23 ->  13.45 kB
//
// About 5 kB of each of index / components / chat is the QR encoder (`uqr`)
// for authenticator enrolment, loaded with `import()` only when a setup asks
// for a code, which size-limit inlines. The catalogues grow by the names of
// icon-only buttons that were English literals. For an app the trade is the
// other way: it no longer loads naive-ui for this package at all. Limits:
// index 301.5 -> 306.5, components 269.5 -> 275.5 (274.90 with TQrCode), chat 152 -> 157.5, embed
// 79.5 -> 80.5, auth 14.5 -> 15, widget 30 -> 30.5, i18n 7 -> 7.5 kB.
//
// 2026-11-09 (sub-agents): an external agent's sub-agents are cards in the answer (`TSubAgentCard`,
// rendered by `TThreadMessage`), with `TSubAgentList` for a side panel, the pure model in
// `headless/sub-agents.ts` (which `useChatThreads` uses to route a background sub-agent's updates
// to the answer that dispatched it) and a `subAgents` section in both catalogues. Measured on this
// branch: index 310.20, components 278.14, chat 159.71, headless 87.27 kB. New capability, not
// creep: before it, a sub-agent's work was mixed into the reply. Limits: index 306.5 -> 310.5,
// components 275.5 -> 278.5, chat 158 -> 160, headless 86.5 -> 87.5 kB.
//
// 2026-11-09 (CLI tasks in the run line): a sub-agent and a background command are steps of their run, counted in
// its line and drawn as their own rows when it opens (TSubAgentStep, TBackgroundCommandStep); a sub-agent opens its
// own view (TSubAgentPanel, loaded with `import()` and inlined here by size-limit); the line shows the images its
// steps returned (TRunImages: thumbnails, hover preview, the viewer) and the lines they changed; the Claude Code
// file and shell tools got their own words; and a `tasks` section in both catalogues. TBackgroundTasks is in the
// components barrel only. Every surface that draws a reply carries the run line, so the embed widgets pay for it too
// (they show CLI agents the same way). Measured on this branch after the first round's limits: index 317.73,
// components 284.67, chat 166.27, embed 87.43, headless 87.45, widget 30.82, auth 15.09, plugin 16.72, i18n 7.53,
// locales 14.54 kB. Limits: index 310.5 -> 318, components 278.5 -> 285, chat 160 -> 166.5, embed 80.5 -> 87.5,
// headless 87.5 -> 88 (50 B left would fail on the next label), widget 30.5 -> 31, auth 15 -> 15.5, plugin
// 16.5 -> 17, i18n 7.5 -> 8, locales 14 -> 15 kB.
// 2026-11-10 (queued messages reach a running CLI turn): the queue takes attachments (read under the send rules and
// listed on each item), says where a message handed to the agent stands, words "Send now" / "Stop and send" by what
// the server says it will do, and the stream's `user_message` event puts the user's message between the answer so far
// and the rest. Measured against the commit before it built the same way: index 318.05 -> 318.89, components
// 284.92 -> 285.40, chat 166.54 -> 167.03, embed 87.71 -> 87.85, headless 87.65 -> 87.90 kB. Limits: index 318 -> 319,
// components 285 -> 286, chat 166.5 -> 167.5, embed 87.5 -> 88, headless 88 -> 88.5 kB.
// 2026-11-12 (a conversation's origin icon moves to the row's end, in the "..." menu's place): the index already
// read 319.04 kB before this change, over its 319 kB limit; the change adds the end cell, the stacked icon/menu
// rules and the origin label read before the title. Measured against the commit before it built the same way:
// index 319.04 -> 319.19, components 285.39 -> 285.55, chat 167.04 -> 167.18 kB. Limit: index 319 -> 319.5 kB.
// 2026-11-12 (running and unread conversations, the TSegmented running ring, toned nav badges): `useThreadAttention`
// and its wiring into `useChatThreads`, `TThreadStatus` at the end of the thread rows, the ring and the joined
// accessible name of a TSegmented option, the badge pill and the rail's badge. TSegmented's words come from the
// controls' own text (`controls-text.ts`), not the catalogue, so the controls barrel stays at 31.69 kB. Measured
// after: index 321.17, components 286.43, chat 167.89, headless 89.19 kB. Limits: index 319.5 -> 321.5, components
// 286 -> 287, chat 167.5 -> 168, headless 88.5 -> 89.5 kB.
// 2026-11-12 (TSegmented's sliding thumb, then its icon size as `--tnzi-ai-segmented-icon-size`): the thumb (an
// element placed and resized by script, with a ResizeObserver) left index at 321.6 and controls at 32.06 kB, over
// their 321.5 and 32 kB limits; the icon size (the options' height and width, the corner dot and ring worked out
// from it) adds nothing to the scripts and takes controls.css 9.99 -> 10.07 kB. Measured against the commit before
// the icon size built the same way. Limits: index 321.5 -> 322, controls 32 -> 32.5, controls.css 10 -> 10.5 kB.
export default [
  // Ratcheted down on 2026-08-02 after the @vue-flow leak was closed: these
  // read 254.6 / 238.4 kB while `components/index.ts` still re-exported the
  // workflow SFCs, and 192.78 / 182.84 kB once it stopped. Leaving the old
  // limits in place would have handed the ~60 kB straight back - a budget with
  // 85 kB of slack cannot notice the dependency creeping in again.
  // 2026-11-09, the menu entry's ends and window keys a tray window needed: TMenuItem `#leading`,
  // `#trailing`, `shortcut`, `expanded` (a turning chevron); TMenuPanel `windowKeys`. Measured
  // against the commit before them built the same way: index 306.44 -> 306.77, components
  // 275.24 -> 275.58, controls 31.14 -> 31.49 kB. New capability: the alternative was each app
  // writing the panel's internal item classes. Limits: index 306.5 -> 307, components
  // 275.5 -> 276, controls 31.5 -> 32 kB.
// 2026-11-12 (thinking effort in the composer's model picker): measured against the commit before it,
// built the same way: index 321.6 -> 322.9, components 286.9 -> 287.7, widget 30.98 -> 31.17, chat
// 167.89 -> 170.34 (the picker now renders TSegmented, which the chat entry did not reach before),
// embed 87.85 -> 88.04, plugin 16.88 -> 17.07, locales 14.82 -> 15.22 kB (effort names and hints in both
// languages). Limits: locales 15 -> 15.5, index 322 -> 323.5, components 287 -> 288.5,
// widget 31 -> 31.5, chat 168 -> 171, embed 88 -> 88.5, plugin 17 -> 17.5 kB.
// Then the model list per conversation (`useChatModels({ threadId })`, `rememberedEffort`) and the names of
// Codex's and Kimi's efforts: headless 89.47 -> 89.56 kB, over its 89.5. Limit: headless 89.5 -> 90 kB.
// 2026-11-14, what a widget page built from the controls needed (a dialog's styles in controls.css with
// a `dialogs` entry, `TSearchSelect`, `createAiI18nPlugin`, the widget's locale and forms). Measured
// against 0ecb40509 built the same way: index 325.07 -> 327.73, components 288.23 -> 290.56,
// controls 32.09 -> 34.47 (TSearchSelect and its loader), controls.css 10.13 -> 11.37 (the dialog
// chrome, TDialog, TConfirmHost and TSearchSelect), widget 31.26 -> 32.01, i18n 7.96 -> 8.41 kB (the
// plugin). index (325.07 / 323.5), headless (91 / 90) and auth (15.52 / 15.5) were already over on
// 0ecb40509, untouched here (headless and auth do not move); their limits now say what they weigh.
// New capability, not creep. Limits: index 323.5 -> 328.5, components 288.5 -> 291, controls
// 32.5 -> 35, controls.css 10.5 -> 12, widget 31.5 -> 32.5, headless 90 -> 91.5, auth 15.5 -> 16,
// i18n 8 -> 8.5 kB.
// 2026-11-14, TImageViewer zooms and pans (buttons, keys, wheel, pinch, drag, the pan bounds) and is
// exported from `dialogs` with its own words accessor. The viewer is reached (lazily, which size-limit
// counts) from every barrel that draws an attachment image, so they all carry it: measured against the
// previous note's figures, index 327.73 -> 329.84, components 290.56 -> 292.45, widget 32.01 -> 34.03,
// dialogs 9.55 -> 11.91 kB; chat 173.08 and embed 90.50 kB went past 171 / 88.5 by the same ~2 kB.
// New capability, not creep. Limits: index 328.5 -> 330.5, components 291 -> 293, dialogs 10 -> 12.5,
// widget 32.5 -> 34.5, chat 171 -> 173.5, embed 88.5 -> 91 kB.
  budget('index (whole package barrel)', 'dist/index.js', '330.5 kB'),
  budget('components barrel', 'dist/components.js', '293 kB'),
  // Buttons, fields, dropdowns, switches, menus, tooltip, with nothing but vue and @iconify/vue
  // behind them (conventions/controls-entry.test.ts keeps it that way).
  // 24.5 -> 29.5 kB (2026-11-09): measured 24.29 -> 29.17 kB with `TQrCode`. Almost all of it
  // is the QR encoder (`uqr`), loaded with `import()` on the first draw and inlined here by
  // size-limit; the component itself is a few hundred bytes.
  // 29.5 -> 31 kB (2026-11-09, B9): measured 29.17 -> 30.88 kB. The entry gained what a desktop
  // window's settings pages are built from, `TSettingGroup` and `TSettingRow`, and
  // `applyControlsTheme` (light / dark and an accent without the @tnzi/ui theme context, with
  // the contrast arithmetic that context does with colord). New capability, not creep.
  // 2026-11-09, gaps found by a desktop app built on the controls alone: TMenuPanel (the menu
  // panel and its entries in place, with arrow-key movement; TPopoverMenu now draws with it),
  // disableRemoteIcons in the controls entry (so such a page does not load the icons entry for
  // it), and the controls reading the --tnzi-ai-radius-* steps. Measured against 1927247a1
  // built the same way: controls 30.88 -> 31.14 kB, controls.css 9.50 -> 9.62 kB (index, components,
  // chat, headless grow by 0.05-0.43 kB and stay inside theirs). New capability, not creep.
  // Limit: controls 31 -> 31.5 kB.
  budget('controls barrel (form controls, menus, tooltip)', 'dist/controls.js', '35 kB'),
  // The controls stylesheet (`vite.controls-css.config.ts`): tokens, the global control classes,
  // the tooltip, the thin scrollbars and the scoped styles of the components the controls entry
  // reaches. Measured 9.5 kB on 2026-11-09 (B9), against 47.6 kB for the whole `style.css`.
  // What it may not contain is a convention test (conventions/controls-stylesheet.test.ts);
  // this is the weight.
  budget('controls.css (controls stylesheet)', 'dist/controls.css', '12 kB'),
  // TDialog and the confirmation for a page of controls (2026-11-14), with the page's overlay
  // stack (`@tnzi/ui/headless` over `@tnzi/core`) behind them. Its styles are in controls.css.
  // Measured 9.55 kB; 11.91 kB with TImageViewer (2026-11-14).
  budget('dialogs barrel (TDialog, confirmation, TImageViewer)', 'dist/dialogs.js', '12.5 kB'),
  // Reached from ui-admin's WorkflowEditor, which lazy-loads it; pulls @vue-flow.
  // Previously had no budget at all.
  budget('workflow barrel (@vue-flow canvas)', 'dist/workflow.js', '90 kB'),
  // Inline widgets: the frame (sandboxed runner iframe, lazy mount), the host
  // bridge and the bootstrap written into each widget's document, plus the
  // tool step shell they fall back to. Measured 19.48 kB on 2026-10-22.
  budget('widget barrel (inline show_widget frames)', 'dist/widget.js', '34.5 kB'),
  // 85 -> 88 kB on 2026-08-05: the shell gained three wired settings pages
  // (Account / Security / Personalization, which call the framework's own
  // user-facing routes) plus the theme provider that made every control inside
  // this package follow the product's palette (gone with naive-ui in B8). Both are capability the
  // barrel did not have, not creep - but the slack is small, so a further
  // increase should be argued rather than nudged.
  // 157.5 -> 158 kB (2026-11-09): measured 157.42 kB with the password placeholders and the
  // shared TQrCode (157.24 before); the 80 B left would fail on the next label.
  budget('chat barrel (TChatApp + markdown pipeline)', 'dist/chat.js', '173.5 kB'),
  // The embeddable widgets. Their whole value proposition is dropping onto a
  // third-party page, so this is the budget that matters most to defend.
  budget('embed barrel (floating / sidebar / inline chat)', 'dist/embed.js', '91 kB'),
  budget('headless barrel (incl. eager markdown-it ~52.8 kB)', 'dist/headless.js', '91.5 kB'),
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
  // 12.5 -> 13 kB (2026-11-01): measured 12.91 kB after the sign-in page gained the one-screen
  // form (`singleScreen`: account and password on one screen, the form itself, its focus and
  // error handling). A real feature of this barrel, not drift; the headroom is 90 B.
  budget('auth barrel (sign-in / sign-up page)', 'dist/auth.js', '16 kB'),
  // DTO -> view-model mapping. Pure functions; if this ever approaches the
  // budget something with a component in it has been added to the barrel.
  budget('adapters barrel', 'dist/adapters.js', '3 kB'),
  // Application assembly. Lazy-loads the home component by contract, so this
  // should stay near the auth page's weight, not the chat tree's.
  // 14 -> 14.5 kB (2026-11-01): measured 14.24 kB. The barrel carries the auth page, which grew by
  // the one-screen form (see the auth barrel above), so it moves by the same amount.
  budget('plugin barrel (defineChatApp)', 'dist/plugin.js', '17.5 kB'),
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
  budget('i18n engine (bundles `en` as the fallback catalogue)', 'dist/i18n.js', '8.5 kB'),
  // 13.5 -> 14 kB (2026-11-09): measured 13.50 kB once the account settings' password boxes
  // say what to type (three placeholders in each catalogue).
  budget('locales barrel', 'dist/locales.js', '15.5 kB'),
  // Tiny today. Budgeted anyway because a `utils` barrel is the easiest place
  // for something heavy to be dropped without anyone noticing.
  budget('utils barrel', 'dist/utils.js', '3 kB'),
  budget('theme barrel', 'dist/theme.js', '3 kB'),
]
