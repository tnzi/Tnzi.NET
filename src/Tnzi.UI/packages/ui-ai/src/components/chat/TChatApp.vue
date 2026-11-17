<script setup lang="ts">
/**
 * @experimental
 * TChatApp - production-grade chat application shell with Manus-inspired design.
 *
 * Drop-in chat product. Internally composes TCollapsibleSidebar +
 * TLandingPage + TThreadComposer + TArtifactPanel + TSettingsDialog +
 * TCommandPalette into a complete agent product shell. Consumers wire
 * data via props/events and override visuals via slots.
 *
 * Default behaviour out of the box:
 *   - Three-mode sidebar (expanded / icon-rail / hidden)
 *   - Brand lockup with T-monogram (override via `brand` slot or brandName/brandLogo/brandWordmark)
 *   - Thread list with new-chat + select + delete
 *   - Topbar with workspace title + actions slot
 *   - Landing page empty state (serif headline + composer + chips)
 *   - Message thread with reasoning trace, copy/regenerate actions
 *   - Sticky composer (uses TThreadComposer)
 *   - Optional artifact panel (right pane)
 *   - Settings dialog (Account / Appearance / About - extensible)
 *   - Command palette (Cmd+K) - optional, off by default
 *   - Auto theme application (light/dark/system)
 *
 * See a consumer chat app for a
 * real-world integration.
 */
import { computed, inject, nextTick, ref, shallowRef, watch, onMounted, onBeforeUnmount, useId, useSlots, useTemplateRef } from 'vue'
import TUsageLimitNotice from './TUsageLimitNotice.vue'
import TComposerModelPicker, { type ComposerModelOption } from './TComposerModelPicker.vue'
import type { UsageLimitNotice } from '../../headless/usage-limits'
import type { IconMotion } from '../../headless/icon-motion'
import { Icon } from '@iconify/vue'
import { TAvatar } from '@tnzi/ui/components'
import { THEME_CONTEXT_KEY, type ThemeContext } from '@tnzi/ui/headless'
import { applyThemeModeToDocument } from '@tnzi/ui/theme'
import { markAiApp } from '../../headless/app-root'
import type { ThemeMode } from '@tnzi/core/types'
import { normalizeThemeMode } from '@tnzi/core/types'
import TCollapsibleSidebar from '../layout/TCollapsibleSidebar.vue'
import TAsidePanel from '../layout/TAsidePanel.vue'
import TCommandPalette from '../overlay/TCommandPalette.vue'
import TSettingsDialog from '../overlay/TSettingsDialog.vue'
import type { DialogCloseReason } from '../overlay/dialog-motion'
import TConfirmHost from '../overlay/TConfirmHost.vue'
import TToastHost from '../controls/TToastHost.vue'
import TPendingApprovals from '../approvals/TPendingApprovals.vue'
import TQueuedMessages from './TQueuedMessages.vue'
import TSlashCommandMenu, { type SlashCommandsState } from './TSlashCommandMenu.vue'
import { textWithCommand, useSlashMenu, type SlashMenuItem } from '../../headless/slash-menu'
import type { ApprovalCardModel } from '../../headless/approval-card'
import type { ApprovalQueue, PendingInteraction } from '../../headless/approval-queue'
import { CHAT_THREADS_KEY, pickChatApprovals, pickChatMessageQueue, pickChatStopTask } from '../../headless/chat-approvals-key'
import { createSubAgentView, provideSubAgentView } from '../reasoning/sub-agent-view'
import type { MessageQueue } from '../../headless/message-queue'
import TLandingPage from './TLandingPage.vue'
import TSidebarNav from '../layout/TSidebarNav.vue'
import TChatRail from '../layout/TChatRail.vue'
import TRailButton from '../layout/TRailButton.vue'
import TRailFlyout from '../layout/TRailFlyout.vue'
import TRailThreadList from './TRailThreadList.vue'
import TThreadList from './TThreadList.vue'
import type { ThreadItem } from './TThreadList.vue'
import TRecentsPage from './TRecentsPage.vue'
import type { ThreadIconResolver } from './thread-icon'
import TThreadTitle from './TThreadTitle.vue'
import { RECENTS_VIEW_ID, type RecentThreadsController } from '../../headless/recents'
import type { NavItem, NavGroup, NavGroupAction, NavItemAction } from '../layout/TSidebarNav.vue'
import TUserMenu from '../overlay/TUserMenu.vue'
import TSettingRow from '../layout/TSettingRow.vue'
import TSettingGroup from '../layout/TSettingGroup.vue'
import TAppearanceAdmin from '../layout/TAppearanceAdmin.vue'
import type { UseGlobalAiThemeReturn } from '../../headless/useGlobalAiTheme'
import type { HttpClient } from '@tnzi/core/http'
import type { UserDto } from '@tnzi/core/services/identity'
import { useAccountSettings } from '../../headless/useAccountSettings'
import { useAiPersonalization } from '../../headless/useAiPersonalization'
import TAccountSettings from '../settings/TAccountSettings.vue'
import TSecuritySettings from '../settings/TSecuritySettings.vue'
import TPersonalizationSettings from '../settings/TPersonalizationSettings.vue'
import TUsageSettings from '../settings/TUsageSettings.vue'
import { useAiUsage } from '../../headless/useAiUsage'
import type { UserMenuItem, UserBarAction } from '../overlay/TUserMenu.vue'
import type { LandingChip } from './TLandingPage.vue'
import TThreadComposer from './TThreadComposer.vue'
import TThreadMessage from './TThreadMessage.vue'
import TThreadLoading from './TThreadLoading.vue'
import { useAutoScroll, type ChatScrollMode } from '../../headless/useAutoScroll'
import { useLatestInView } from '../../headless/useLatestInView'
import { usePinnedViewport } from '../../headless/usePinnedViewport'
import { useScrollMemory } from '../../headless/useScrollMemory'
import { enableTouchPress } from '../../headless/touch-press'
import { useAiI18n } from '../../i18n/index'
import type { ComposerAction } from './composer-types'
import { DEFAULT_COMPOSER_ACCEPT } from './composer-types'
import TArtifactPanel from '../artifact/TArtifactPanel.vue'
import type {
  ArtifactView,
  ArtifactPanelItem,
  ArtifactFile,
} from '../artifact/TArtifactPanel.vue'
import type { SettingsSection } from '../../headless/useSettingsDialog'
import type { CommandAction } from '../../headless/useCommandPalette'
import { useSidebarState, type SidebarMode } from '../../headless/useSidebarState'
import { conversationRoom, useSidebarAutoFit, SIDEBAR_AUTO_FIT_MIN_WIDTH } from '../../headless/useSidebarAutoFit'
import { useAsidePanel, provideChatAside } from '../../headless/useAsidePanel'
import { provideChatViewFill } from '../../headless/useChatViewFill'
import type { ChatMessage } from '../../headless/useChat'
import { continuesAfterBackgroundTask } from './thread-continuation'
import type { AddAttachmentsResult, RejectedAttachment } from '../../headless/useComposerAttachments'
import {
  provideChatRenderContext,
  type ChatFileRef,
  type ToolRendererMap,
} from './chat-render-context'

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/**
 * Theme preference. Alias of core's `ThemeMode`, where `'auto'` means "follow
 * the OS colour scheme".
 *
 * This used to be its own `'light' | 'dark' | 'system'` union. The `'system'`
 * spelling is still accepted at runtime (it is normalized on read), so an app
 * passing `theme="system"` keeps working.
 */
export type ThemePref = ThemeMode

/**
 * Which surface fills the main area.
 *
 * `'chat'` renders the built-in conversation (landing state or thread).
 * Any other string hands the area to the `view` slot while keeping the
 * sidebar and top bar in place, which is what a product needs when its nav
 * points at pages other than a conversation.
 */
export type ChatAppView = 'chat' | (string & {})

/**
 * Views this package ships navigation + page shells for.
 *
 * Every agent product grows the same handful of non-conversation screens, and
 * every one of them re-invents the nav entry, the icon, the label and its
 * translation. Declaring them here makes that one prop.
 *
 * The framework supplies the **entry and the shell**, never the data:
 * `@tnzi/ui-ai` owns no transport (Critical Rule #8). Enabling a view adds it
 * to the sidebar and switches `view` to its id; the consumer renders it in the
 * `view` slot, typically with `TResourcePage` + `TResourceCard`.
 */
export type BuiltinViewId = 'agents' | 'scheduled' | 'artifacts' | 'skills' | 'knowledge'

/** Icons are part of the contract: the whole point is not re-picking them. */
const BUILTIN_VIEW_ICONS: Record<BuiltinViewId, string> = {
  agents: 'lucide:bot',
  scheduled: 'lucide:clock',
  artifacts: 'lucide:library',
  skills: 'lucide:wand-2',
  knowledge: 'lucide:book-open',
}
/* The hover motion is part of the same contract as the icon: each one says
   what its entry is (the clock rings, the agent hops, the wand is waved). */
const BUILTIN_VIEW_ICON_MOTIONS: Record<BuiltinViewId, IconMotion> = {
  agents: 'hop',
  scheduled: 'ring',
  artifacts: 'pop',
  skills: 'tilt',
  knowledge: 'pop',
}

/**
 * What `addAttachments` did. `added` / `rejected` come from the composer's own
 * checks. `pending` holds files that could not land yet because a `#view` page
 * filled the main area and no composer was on screen: the shell asked to show
 * the conversation (`update:view`), and they land, through the same checks,
 * when its composer appears.
 */
export interface ChatAppAttachmentsResult extends AddAttachmentsResult {
  readonly pending: readonly File[]
}

/* Re-exported so consumers import every TChatApp-facing type from one module
   rather than tracking which shell component happens to own each one. */
export type { LandingChip, NavItem, NavGroup, NavGroupAction, NavItemAction, UserMenuItem, UserBarAction, ThreadItem }

// ---------------------------------------------------------------------------
// Props / events / slots
// ---------------------------------------------------------------------------

const props = withDefaults(
  defineProps<{
    // ── Brand ────────────────────────────────────────────────────────────
    /** Brand name shown in the sidebar header. Also the settings label and
     *  the top-bar fallback title, and the header's accessible name when a
     *  `brandWordmark` draws it. */
    brandName?: string
    /** Brand logo: 'monogram' renders the built-in T-mark; any other
     *  string is treated as an iconify name. Use the `brand` slot for
     *  fully custom markup. */
    brandLogo?: 'monogram' | string
    /** The brand name as a drawn logotype (an iconify name), shown in place
     *  of the `brandName` text in the sidebar header: 1em of the name's font
     *  size high, as wide as its own aspect ratio makes it, centred in the
     *  line the text occupied (which keeps its height). `currentColor` in it
     *  follows the theme like the text did, other fills keep their colour.
     *  `brandName` stays what a screen reader hears. */
    brandWordmark?: string

    // ── Sidebar nav ─────────────────────────────────────────────────────
    /** Flat primary nav. Rendered as one untitled group. */
    mainNav?: ReadonlyArray<NavItem>
    /**
     * Grouped nav rendered under `mainNav`. Each group may carry a heading,
     * be collapsible, and expose its own header actions. Use this for the
     * "Projects" / "Workspaces" style sections that sit between the primary
     * nav and the thread history.
     */
    navGroups?: ReadonlyArray<NavGroup>
    /**
     * Built-in views to expose in the sidebar, in the order given. Each adds a
     * nav entry with a translated label and a standard icon, and marks itself
     * active when `view` matches its id. Rendering stays with the consumer via
     * the `view` slot - this only removes the boilerplate of declaring the
     * entries.
     */
    builtinViews?: ReadonlyArray<BuiltinViewId>
    /** Initial sidebar mode. Ignored when a persisted mode is found under
     *  `sidebarStorageKey`. */
    initialSidebarMode?: SidebarMode
    /** localStorage key the sidebar mode is remembered under. Pass `null` to
     *  disable persistence and always start from `initialSidebarMode`. */
    sidebarStorageKey?: string | null
    /** Viewport width (px) below which the sidebar auto-collapses to hidden;
     *  the previous desktop mode is restored on the way back up. */
    sidebarMobileBreakpoint?: number
    /** Expanded sidebar width (px). */
    sidebarWidth?: number
    /**
     * Fold the expanded sidebar into the rail by itself when the conversation
     * would be narrower than `conversationMinWidth` beside it (an open side
     * panel and artifact pane counted), and unfold it when there is room again
     * (`conversationMinWidth` + 80px). Follows the window, the shell's own
     * size and the side panel opening, closing and being resized. Only a
     * sidebar it folded is unfolded: a rail or a hidden sidebar the user chose
     * stays, and nothing it does is written to `sidebarStorageKey`. A user
     * expanding it while the room is short keeps it expanded. No effect on the
     * narrow-screen drawer. Default `true`.
     */
    sidebarAutoCollapse?: boolean
    /** Narrowest conversation column (px) `sidebarAutoCollapse` keeps beside the expanded sidebar. Default 560. */
    conversationMinWidth?: number
    /** Section heading above the threads list. Defaults to the current
     *  locale's; pass '' for none. */
    threadsLabel?: string
    /** Label for the new-chat entry. Defaults to the current locale's. */
    newChatLabel?: string
    /** Iconify name for the new-chat entry. A product that calls this
     *  something else usually wants its own glyph too. */
    newChatIcon?: string
    /** Hover motion of the new-chat icon (see `NavItem.iconMotion`). The
     *  default suits the default pen; `null` keeps the icon still. */
    newChatIconMotion?: IconMotion | null
    /**
     * Contribute the built-in new-chat entry at the top of the nav. Turn it off
     * when the product already carries "New chat" among its own nav entries -
     * otherwise it appears twice, in both the expanded sidebar and the rail.
     */
    showNewChatButton?: boolean
    /**
     * Whether the landing state on show is the new-chat destination, so the
     * new-chat entry is highlighted there (in the expanded sidebar and the
     * rail alike). Pass `false` while a product uses the landing state for
     * something else, such as an existing item opened beside it; clicking the
     * entry still starts a new chat. Default `true`.
     */
    landingIsNewChat?: boolean
    /** Hover delete affordance on each thread row (with inline confirm). Default true. */
    enableThreadDelete?: boolean
    /** Inline delete-confirm prompt label. Defaults to the current locale's. */
    deleteConfirmLabel?: string
    /**
     * The conversations button of the collapsed rail: hovering, focusing or
     * clicking it opens the recent conversations beside the rail
     * (`TRailFlyout` + `TRailThreadList`, from `threads`; with `recents`, a
     * "View all" row). It appears when `threads` has any, or when the
     * `#rail-threads` slot is given (a product whose list is something else
     * supplies it there). `false` never shows it.
     */
    railThreads?: boolean
    /** Name and heading of that button and its panel. Defaults to `threadsLabel`, then the locale's. */
    railThreadsLabel?: string
    /** Iconify name of that button. */
    railThreadsIcon?: string

    // ── Threads ─────────────────────────────────────────────────────────
    threads?: ReadonlyArray<ThreadItem>
    activeThreadId?: string
    /**
     * A thread being opened: `useChatThreads().openingThreadId`. From the click
     * until its transcript is shown, its row is highlighted and the main area
     * shows a loading state for it (`TThreadLoading`) instead of the previous
     * transcript; sending waits until it has opened. Without it nothing on
     * screen changes until the transcript arrives.
     */
    openingThreadId?: string

    // ── Chat thread ─────────────────────────────────────────────────────
    messages?: ReadonlyArray<ChatMessage>
    isStreaming?: boolean
    /**
     * While `isStreaming`, let the conversation's composer queue a message: with
     * text or attachments in it the primary button (and Enter) emits
     * `queue(content, files)` and empties the composer, and Stop moves to a
     * second button beside it. Keeping the queued messages and sending them
     * when the turn ends is the app's (list them in `#thread-composer-top`).
     * Default false: while streaming the composer offers only Stop.
     *
     * Not needed with a server message queue (`messageQueue`): while its
     * turn has a server run the composer queues by itself.
     */
    queueWhileRunning?: boolean
    /**
     * The conversation's queued messages on the server (`useChatThreads().messageQueue`): listed
     * above the composer, each editable, removable and sendable now. While a turn of an external
     * agent runs (`messageQueue.available`) the composer queues: a message with words, attachments or
     * both is emitted as `send`, which `useChatThreads().send` queues on the server (the running agent
     * gets it at once when its turn can take messages). Without it the shell uses the queue of the `useChatThreads` whose `messages` it shows
     * (picked as the approval cards are); `null` turns the list and the queueing off (an app with its own queue, which then
     * uses `queueWhileRunning` and `queue`).
     */
    messageQueue?: MessageQueue | null
    /** Composer text - v-model'able. */
    inputText?: string
    /** Defaults to the current locale's. */
    composerPlaceholder?: string
    /** Extra composer toolbar buttons (declarative - "more buttons"). */
    composerActions?: ReadonlyArray<ComposerAction>
    /**
     * Attributes for the composer's textarea, landing and thread alike (the
     * combobox ARIA of a `#composer-suggestions` menu). See TThreadComposer `inputAttrs`.
     */
    composerInputAttrs?: Readonly<Record<string, string | number | boolean | undefined>>
    /**
     * The commands of the composer's `/` menu (a CLI agent's skills and commands). Given (even
     * empty), typing a slash at the start of an empty box opens the menu above the composer,
     * filtered by what follows the slash; the arrows move, Enter or Tab writes `/<command> ` into the
     * box, Escape closes it until the text changes. The box gets the combobox ARIA. Where the list
     * comes from is the app's: read it on `slash-query`. `null` / not given: no menu.
     */
    slashCommands?: ReadonlyArray<SlashMenuItem> | null
    /** Where reading `slashCommands` stands: the menu says "Reading…" or that it failed while the list is empty. Default `'ready'`. */
    slashCommandsState?: SlashCommandsState
    /** A line under the `/` menu's list (why some commands are missing, say). */
    slashCommandsNote?: string | null
    /**
     * What the conversation waits on its user for, shown as cards above the composer: an external
     * agent's permission requests, questions and plans, and the application's own kinds.
     * `useChatThreads().approvals`; without it the shell uses the queue of the `useChatThreads` in
     * an ancestor component whose `messages` array is the one given here (with a single instance,
     * that one; with several and none matching, no cards), so a shell wired to the hook shows them
     * by itself. `null` turns the cards off.
     */
    approvals?: ApprovalQueue | null
    /**
     * A card model for a waiting item (an application's own kind, or its own wording of a built-in
     * one); null leaves it to the framework's card or the `#approval-card` slot.
     */
    describeApproval?: ((item: PendingInteraction) => ApprovalCardModel | null) | null
    /** Built-in voice (speech-to-text) mic button. Default true. */
    enableVoice?: boolean
    /**
     * Iconify name of the composer's send button, on the home screen and in a
     * conversation alike (a brand's own mark, registered like any other icon).
     * Only the send state: stop, voice and end-dictation keep their icons.
     * Default `lucide:arrow-up`.
     */
    sendIcon?: string
    /** Built-in attachment button (paperclip + drag/paste). Default false. */
    enableAttachments?: boolean
    /** Accepted attachment file types (`<input accept>` syntax). Checked for every
     *  way a file arrives: picker, drop, paste and `addAttachments`. */
    composerAccept?: string
    /** Max attachment size in bytes. Defaults to the composer's (10 MB). */
    composerMaxFileSize?: number
    /** Voice recognition language (BCP-47). */
    voiceLang?: string
    /** Agent display name. Defaults to the current locale's. */
    agentName?: string
    /** Small label rendered after the agent name (e.g. "Pro", "Lite"). */
    agentLabel?: string
    /**
     * The agent's mark in each answer's byline: an Iconify icon name (register
     * the product's own with `addIcon` / `addCollection`). Default
     * `lucide:sparkles`. The `agent-mark` slot (`{ message, streaming,
     * placement }`) replaces the mark's tile entirely.
     */
    agentIcon?: string
    /**
     * The mark on an answer that is still being written, e.g. an animated
     * variant of `agentIcon`. Unset, the mark does not change while streaming.
     * With `showAgentByline` off it is the "still writing" mark instead of the
     * pulsing dot (so is the `agent-mark` slot, with `placement: 'pending'`).
     */
    agentStreamingIcon?: string
    /**
     * The row above each answer: the agent's mark, `agentName` and `agentLabel`.
     * Default true. Off, an answer starts with its text (a reply still being
     * written shows a pulsing dot until its first words arrive, then the
     * caret at the end of the text).
     */
    showAgentByline?: boolean
    /** Show the tool-call cards a turn reports (live and on reopen). Default true. */
    showToolCalls?: boolean
    /** Show the token line under each answer. Default false. */
    showUsage?: boolean
    /**
     * Tool renderers by tool name: a product's own view of its own tools (a screenshot
     * tool as a picture, say). Every other tool shows the generic card. A name also
     * matches an MCP-prefixed tool (`mcp__server__name`); app-wide renderers
     * (`defineChatApp({ toolRenderers })`) apply too, and these win on the same name.
     */
    toolRenderers?: ToolRendererMap
    /**
     * An object URL for a stored attachment of the open thread (images and PDFs the user
     * sent, images tools returned): `useChatThreads().resolveAttachmentUrl`. Without it
     * stored images show as placeholders.
     */
    resolveAttachmentUrl?: (attachmentId: string) => Promise<string | null>
    /**
     * Where a file card links to (a file the agent handed over carries the URI its tool
     * gave). Return null/undefined for the default: an attachment's own content, or the
     * URI when it is an http(s) or site-relative address.
     */
    fileHref?: (file: ChatFileRef) => string | null | undefined
    /** Offer to edit the user's messages (emits `edit` with the new text). Default false. */
    enableEdit?: boolean

    // ── History paging ──────────────────────────────────────────────────
    /** More threads exist than `threads` holds: the list shows "Show more" (`load-more-threads`). */
    threadsHasMore?: boolean
    /** A page of threads is loading. */
    threadsLoadingMore?: boolean
    /** A Search action in the thread list's header, opening a search box (`search-threads`). */
    threadSearchable?: boolean
    /**
     * Recent conversations: `useChatThreads().recents`. Given, the thread list's
     * header gets View all and a filter / group / sort menu, each row's menu gets
     * Rename and Archive / Unarchive, and `view === 'recents'` (View all) shows
     * the full Recents page (`TRecentsPage`, or the `#recents` slot). Deleting
     * still goes out as `delete-thread`.
     */
    recents?: RecentThreadsController | null
    /**
     * Leave conversations out of the Recents page, for a product that lists some
     * of them elsewhere: the same predicate it applies before handing `threads` in.
     */
    recentsInclude?: (thread: ThreadItem) => boolean
    /**
     * Maps a thread to the icon shown before its title, usually from its
     * `origin`: `(thread) => ({ icon, label })` with an Iconify name the app
     * bundles, or null for none (no icon, no space kept). One resolver for the sidebar
     * list, the collapsed rail's list and the Recents page, so a conversation
     * looks the same in all three. The label is the tooltip and accessible name.
     */
    threadIcon?: ThreadIconResolver
    /** The open thread has messages before the first one shown: scrolling to the top emits `load-older-messages`. */
    hasOlderMessages?: boolean
    /** Older messages are loading. */
    loadingOlderMessages?: boolean

    // ── Locale ──────────────────────────────────────────────────────────
    /**
     * Selectable interface languages. The built-in Appearance pane renders a
     * picker when this is non-empty - the package ships an i18n mechanism
     * (`createAiI18n` / `useAiI18n`), so leaving the settings pane with no way
     * to reach it was an omission rather than a decision.
     *
     * Kept a plain list instead of reading the locale registry: which
     * languages a product actually ships is the product's call, not ours.
     */
    locales?: ReadonlyArray<{ id: string; label: string }>
    /** Selected language id. v-model'able via `update:locale`. */
    locale?: string
    /** Top-bar title; defaults to active thread title or brandName. */
    threadTitle?: string
    /**
     * The title of a conversation by id, for the top bar while that conversation
     * opens (`openingThreadId`): `threadTitle` still names the one being left
     * then. Needed for conversations the product lists somewhere other than
     * `threads`, such as its own `navGroups`; one in `threads` is found there
     * without it. Return null or undefined when unknown.
     */
    threadTitleOf?: (threadId: string) => string | null | undefined
    /** Max width of the conversation content column (CSS length, e.g. "1100px"). */
    contentWidth?: string | number
    /**
     * How the conversation scrolls when a message is sent and its answer
     * streams. `'chat'` (default): the sent message comes into view with the
     * end of the previous answer above it, and the view follows the answer
     * until the message reaches the top, then stays, so a long answer is read
     * from its start. `'follow'`: the view follows the newest output while it
     * streams, for an agent whose turn is a long run of steps. In both,
     * scrolling up stops following at once and coming back to the bottom
     * resumes it, and a conversation opens at its latest turn.
     */
    scrollMode?: ChatScrollMode
    /** Fine-print line under the composer (model disclaimer, usage note). */
    disclaimer?: string
    /**
     * Models the user may pick for the next turn (`useChatModels().models`).
     * More than one, or one that offers efforts, shows a picker in the composer;
     * `v-model:model` carries the choice, which goes to `useChatThreads({ model })`
     * so the turn uses it.
     */
    models?: ReadonlyArray<ComposerModelOption>
    /** The picked model id (`v-model:model`); the first of `models` when null or unknown. */
    model?: string | null
    /**
     * The picked thinking effort (`v-model:effort`, typically `useChatModels().effort`,
     * updated through `selectEffort`); goes to `useChatThreads({ effort })`. The picker
     * offers the efforts of the current model (`ComposerModelOption.efforts`).
     */
    effort?: string | null
    /**
     * A usage limit a turn ran into (`useChatThreads().usageLimit`), shown above
     * the composer with its reset time. Supports `v-model:usage-limit-notice`:
     * dismissing it, or its reset passing, emits null.
     *
     * With `accountClient` the shell also reads the user's usage figures itself:
     * it says so before a send when a window is already at its limit (keeping
     * the text and files in the composer), and hints when one is close.
     */
    usageLimitNotice?: UsageLimitNotice | null

    // ── Main area ───────────────────────────────────────────────────────
    /**
     * Which surface fills the main area. `'chat'` (default) renders the
     * built-in conversation; any other value renders the `view` slot while
     * keeping the sidebar and top bar. This is what lets one shell host both
     * the conversation and the product's other pages.
     *
     * Supports `v-model`. The shell renders the nav and the thread list, so it
     * is the only thing that knows a click there means "show me that page" or
     * "show me the conversation"; with `v-model:view` it routes itself. Bind
     * one-way instead to route by hand from `nav` / `new-chat` / `select-thread`
     * - `update:view` is then simply ignored.
     */
    view?: ChatAppView

    // ── Top bar ─────────────────────────────────────────────────────────
    /** Render the top bar. Set false to give the full height to the main
     *  area (an embedded chat with no chrome of its own). */
    showTopbar?: boolean
    /** Model name shown in the top bar's left slot when `view === 'chat'`.
     *  Clicking it emits `open-model-picker`. Omit to show the workspace
     *  title instead. */
    modelName?: string

    // ── Sidebar footer / account ────────────────────────────────────────
    /** Render the built-in account status bar in the sidebar footer. */
    showAccountBar?: boolean
    /** Replaces the account menu's built-in items (account, settings). */
    userMenuItems?: ReadonlyArray<UserMenuItem>
    /** Appended to the account menu after the built-ins, above sign-out.
     *  The usual way to add product-specific entries. */
    userMenuExtraItems?: ReadonlyArray<UserMenuItem>
    /** Icon buttons in the account row, flush right of the account button
     *  (an apps launcher, notifications, feedback). `label` is both the
     *  tooltip and the accessible name; a click emits `user-action` with the
     *  action's `id`. In the collapsed rail they are not shown (the rail has
     *  the avatar only); in the mobile drawer they are, at touch size. */
    userBarActions?: ReadonlyArray<UserBarAction>
    /** Secondary line in the account menu header (email, workspace, plan). */
    accountSubtitle?: string
    accountAvatar?: string | null
    /** Show the account-switcher affordance. */
    accountSwitchable?: boolean

    // ── Landing empty state ─────────────────────────────────────────────
    /** Show the landing empty state when there are no messages. */
    showLanding?: boolean
    /** Defaults to the current locale's. */
    landingGreeting?: string
    /** Optional subtitle rendered below the landing greeting. */
    landingSubline?: string
    /** Defaults to the current locale's. */
    landingPlaceholder?: string
    landingChips?: ReadonlyArray<LandingChip>

    // ── Settings ────────────────────────────────────────────────────────
    enableSettings?: boolean
    /**
     * A `useGlobalAiTheme()` controller. Supplying one adds the deployment-wide
     * appearance pane to the Appearance section: a privileged user edits the
     * product's look right where they can see it and publishes it for everyone.
     * Omit it and the settings dialog is exactly as before - the pane never
     * renders, so a product without a super-admin story pays nothing.
     */
    globalTheme?: UseGlobalAiThemeReturn | null
    /**
     * HTTP client for the built-in wired settings pages (Account, Security,
     * Personalization, Usage). They call the framework's own **user-facing**
     * routes - `/users/profile/*` in `Tnzi.Identity`, `/user-profile` and
     * `/usage-limits/me` in `Tnzi.AI` - so the client is all a consumer
     * supplies; there is no data to pass. The usage figures also drive the
     * composer's usage-limit notice (see `usageLimitNotice`).
     *
     * Omit it on a deployment without those modules and the pages hide
     * themselves rather than rendering a form that cannot save.
     */
    accountClient?: HttpClient | null
    /**
     * The Account page's "Delete account" group (with `accountClient`).
     * `false` hides it; `{ blockedReason }` keeps it visible but disabled with
     * that reason (last owner, an active subscription). On success the shell
     * emits `account-deleted`, which is where the consumer signs out.
     */
    accountDeletion?: boolean | { blockedReason?: string | null }
    /**
     * `(key, fallback?) => string` for the appearance pane's copy. The rest of
     * the dialog reads the `locales` prop; this pane takes an injected
     * translator because it is also usable outside `TChatApp`.
     */
    translateSetting?: (key: string, fallback?: string) => string
    /** Sections of the settings dialog. Defaults to the built-in ones (account,
     *  security, personalization, usage, appearance, about), named in the current
     *  locale. */
    settingsSections?: ReadonlyArray<SettingsSection>
    /** Defaults to the current locale's. */
    settingsTitle?: string
    /** Asked before the settings dialog closes (unsaved changes); `false` keeps it open. */
    settingsBeforeClose?: (reason: DialogCloseReason) => boolean | void | Promise<boolean | void>
    /** Show the filter box in the settings dialog's left rail. Worth turning
     *  on past roughly half a dozen sections. */
    settingsSearchable?: boolean
    /** Show the account identity block atop the settings dialog's left rail. */
    settingsShowAccount?: boolean
    /** Account info for the built-in settings Account section (props-driven,
     *  consumer-supplied - ui-ai stays business-agnostic). */
    accountName?: string
    accountEmail?: string
    accountRole?: string

    // ── Command palette ─────────────────────────────────────────────────
    enableCommandPalette?: boolean
    commandActions?: ReadonlyArray<CommandAction>

    // ── Theme ───────────────────────────────────────────────────────────
    theme?: ThemePref
    /** When true (default), TChatApp puts the document into the resolved theme
     *  on mount and whenever it changes, which is what switches the
     *  `--tnzi-ai-*` palette. With a host `@tnzi/ui` theme context (the one
     *  `defineChatApp()` or `createTnziUi()` provides) it sets that context's
     *  mode, so the `--tnzi-*` tokens the context writes follow too; without
     *  one it toggles the `dark` class. Set false if your app owns the theme
     *  mode. */
    autoApplyTheme?: boolean

    // ── Artifact ────────────────────────────────────────────────────────
    artifact?: ArtifactPanelItem | null
    artifactFiles?: ReadonlyArray<ArtifactFile>
    artifactView?: ArtifactView
    artifactWidth?: number

    // ── Aside (right-side panel the product fills through the `aside` slot) ──
    /** localStorage key the per-conversation open / width / tab state is kept under. `null` keeps it in memory. */
    asideStorageKey?: string | null
    /** Whether a conversation that was never opened or closed starts with the panel open. */
    asideDefaultOpen?: boolean
    asideDefaultWidth?: number
    asideMinWidth?: number
    asideMaxWidth?: number
    /** The tab a conversation starts on; the product decides what the ids mean. */
    asideDefaultTab?: string
    /** Accessible name of the panel and title of its narrow-screen layer. */
    asideLabel?: string
    /** Show the top bar button that opens and closes the panel. */
    showAsideToggle?: boolean
    /**
     * On a wide screen, the panel takes the full height of the main area, from its very top, and
     * the top bar spans only the conversation (its actions and the panel toggle end beside the
     * panel). For a panel that opens with its own row of controls. Default: the panel sits under a
     * top bar that spans the whole main area. A narrow screen's full-size layer is the same either way.
     * The `aside` slot is told (`fullHeight`), and the panel's content gets the window's corner insets
     * as `--tnzi-ai-aside-inset-top` / `--tnzi-ai-aside-inset-right` (0 when it is not full height).
     */
    asideFullHeight?: boolean
    /**
     * Offer the panel on the landing page too (a new conversation with no messages yet), with the
     * same frame, resizing and stored width as in a conversation; the landing content recentres in
     * the remaining width and the top bar toggle appears there. Its state is the new conversation's
     * (the draft slot), handed to the conversation the first message creates, and the panel stays
     * mounted across that switch. For a start page whose panel has something to show before the
     * first message (a project's files and history). Default: no panel on the landing page.
     */
    asideOnLanding?: boolean
    /**
     * The side panel's tab that shows a sub-agent's own view (`TSubAgentPanel`). With it, opening a sub-agent (its
     * step in a run, its row in `TBackgroundTasks`) opens the side panel on this tab, and closing the view returns
     * to the tab that was shown before; the `aside` slot renders `TSubAgentPanel` for it. Without it (default), or
     * without an `aside` slot, a sub-agent's step opens its view in place, inside its run.
     */
    subAgentAside?: string | null
    /**
     * How a task of the conversation (a sub-agent, a background command) is stopped. Default: the `stopTask` of the
     * `useChatThreads` whose conversation is on screen (picked as the approval cards are). `null` = no Stop buttons.
     */
    stopTask?: ((taskId: string) => Promise<boolean> | boolean | void) | null

    // ── Responsive ──────────────────────────────────────────────────────
    /**
     * Whether to use the mobile drawer presentation (overlay + backdrop +
     * page scroll lock). Leave unset to follow `sidebarMobileBreakpoint`,
     * which is the same signal that auto-collapses the sidebar. Pass a value
     * only to put the breakpoint under the host app's own responsive system.
     */
    isMobile?: boolean
    /**
     * Below the narrow breakpoint, pin the shell to the visible part of the
     * screen (`position: fixed` over the visual viewport, page overscroll
     * off), so only the conversation scrolls and the composer stays at the
     * bottom edge, above the keyboard when it is open (`usePinnedViewport`).
     * Set false when the shell is not the whole page on a phone.
     */
    pinNarrowViewport?: boolean
  }>(),
  {
    brandName: 'Tnzi AI',
    brandLogo: 'monogram',
    mainNav: () => [],
    navGroups: () => [],
    builtinViews: () => [],
    initialSidebarMode: 'expanded',
    sidebarStorageKey: 'tnzi-ui-ai-sidebar-mode',
    sidebarMobileBreakpoint: 768,
    sidebarWidth: 300,
    sidebarAutoCollapse: true,
    conversationMinWidth: SIDEBAR_AUTO_FIT_MIN_WIDTH,
    /* No default for any user-visible label (`threadsLabel`, `newChatLabel`,
       `deleteConfirmLabel`, `composerPlaceholder`, `agentName`, the landing
       copy, `settingsSections`, `settingsTitle`): each falls back to the
       dictionary where it is rendered, so leaving it unset translates instead
       of pinning the shell to English. */
    newChatIcon: 'lucide:square-pen',
    newChatIconMotion: 'tilt',
    showNewChatButton: true,
  landingIsNewChat: true,
    enableThreadDelete: true,
    railThreads: true,
    railThreadsLabel: undefined,
    railThreadsIcon: 'lucide:messages-square',
    threads: () => [],
    openingThreadId: undefined,
    threadTitleOf: undefined,
    messages: () => [],
    isStreaming: false,
    queueWhileRunning: false,
    inputText: '',
    composerActions: () => [],
    composerInputAttrs: undefined,
    slashCommands: null,
    slashCommandsState: 'ready',
    slashCommandsNote: null,
    approvals: undefined,
    messageQueue: undefined,
    describeApproval: null,
    enableVoice: true,
    enableAttachments: false,
    composerAccept: DEFAULT_COMPOSER_ACCEPT,
    voiceLang: 'en-US',
    showAgentByline: true,
    showToolCalls: true,
    scrollMode: 'chat',
    showUsage: false,
    locales: () => [],
    locale: '',
    showLanding: true,
    landingChips: () => [],
    enableSettings: true,
    globalTheme: null,
    translateSetting: undefined,
    settingsSearchable: false,
    settingsBeforeClose: undefined,
    settingsShowAccount: false,
    enableCommandPalette: false,
    commandActions: () => [],
    theme: 'light',
    autoApplyTheme: true,
    artifact: null,
    artifactFiles: () => [],
    artifactView: 'code',
    artifactWidth: 520,
    asideStorageKey: 'tnzi-ui-ai-aside',
    asideDefaultOpen: false,
    asideDefaultWidth: 420,
    asideMinWidth: 280,
    asideMaxWidth: 720,
    asideDefaultTab: undefined,
    asideLabel: undefined,
    showAsideToggle: true,
    asideFullHeight: false,
    asideOnLanding: false,
    subAgentAside: null,
    stopTask: undefined,
    isMobile: undefined,
    pinNarrowViewport: true,
    view: 'chat',
    showTopbar: true,
    showAccountBar: false,
    userMenuItems: undefined,
    userMenuExtraItems: () => [],
    userBarActions: () => [],
    accountSubtitle: '',
    accountAvatar: null,
    accountSwitchable: false,
    disclaimer: '',
    models: () => [],
    model: null,
    effort: null,
    usageLimitNotice: null,
    toolRenderers: () => ({}),
    resolveAttachmentUrl: undefined,
    fileHref: undefined,
    enableEdit: false,
    threadsHasMore: false,
    threadsLoadingMore: false,
    threadSearchable: false,
    recents: null,
    recentsInclude: undefined,
    threadIcon: undefined,
    hasOlderMessages: false,
    loadingOlderMessages: false,
  },
)

const emit = defineEmits<{
  // Sidebar
  'new-chat': []
  'select-thread': [threadId: string]
  'delete-thread': [threadId: string]
  nav: [item: NavItem]
  /** A nav group's header action was clicked (e.g. "new project"). */
  'nav-group-action': [groupId: string, actionId: string]
  /**
   * A nav row's trailing action was clicked (`NavItem.action`, e.g. a "+" on
   * "Projects"). Only this fires: the row is not selected and `view` does not move.
   */
  'nav-item-action': [itemId: string, actionId: string]
  /**
   * `v-model:view`. Emitted alongside `nav` (the entry's id) and alongside
   * `new-chat` / `select-thread` (back to the conversation). Ignore it to keep
   * routing by hand.
   */
  'update:view': [view: ChatAppView]

  // Account bar
  /** A user-menu item was activated. Built-in ids: account / settings / sign-out.
   *  `account` and `settings` also open the settings dialog (on the Account
   *  section for `account`). */
  'user-menu': [id: string]
  /** An account-bar icon button was clicked. */
  'user-action': [id: string]
  'switch-account': []

  // Top bar
  /** The model name in the top bar was clicked. */
  'open-model-picker': []

  // Composer
  send: [content: string, files: File[]]
  /**
   * A message written while `isStreaming` (`queueWhileRunning`). Same arguments
   * as `send` (the files exactly as the composer held them); the composer is
   * emptied. Not checked against the usage figures: nothing is sent yet.
   * With a server message queue in effect (`messageQueue.available`) it is
   * emitted as `send` instead, and not as `queue`.
   */
  queue: [content: string, files: File[]]
  'composer-action': [id: string]
  /**
   * A key pressed in the composer (landing or thread), before the composer acts
   * on it. An open `#composer-suggestions` menu handles its navigation keys here
   * and calls `preventDefault()` so Enter picks instead of sending.
   */
  'composer-keydown': [event: KeyboardEvent]
  /**
   * The name being typed after a slash at the start of the box, or `null` when no command is being
   * typed: read the `slashCommands` for the conversation on screen when it stops being null.
   */
  'slash-query': [query: string | null]
  stop: []
  'update:input-text': [value: string]
  /** The user picked a model in the composer's picker. */
  'update:model': [modelId: string]
  'update:effort': [effort: string]
  /** The usage-limit notice was dismissed, or its reset passed. */
  'update:usage-limit-notice': [notice: UsageLimitNotice | null]
  /** Files the composer refused (over `composerMaxFileSize`, or not matched by
   *  `composerAccept`), from any way they arrived, `addAttachments` included. */
  'attachments-rejected': [rejected: readonly RejectedAttachment[]]

  // Landing
  'select-suggestion': [chip: LandingChip]

  // Message actions
  copy: [messageId: string]
  regenerate: [messageId: string]
  /** A user message was edited: send `content` in its place (`useChatThreads().editMessage`). */
  edit: [messageId: string, content: string]
  feedback: [messageId: string, type: 'positive' | 'negative', reason?: string]

  // History paging
  /** The thread search changed ('' = cleared). */
  'search-threads': [keyword: string]
  /** The user asked for the next page of threads. */
  'load-more-threads': []
  /** The thread was scrolled to its top while older messages exist. */
  'load-older-messages': []

  // Settings + theme
  'update:theme': [theme: ThemePref]
  'update:locale': [locale: string]
  'open-settings': []
  'sign-out': []
  /** The user's profile changed on the Account page (display name, photo).
   *  Refresh whatever copy the shell renders from (`accountAvatar`). */
  'profile-change': [profile: UserDto]
  /** The account was deleted; every session is already revoked server-side. */
  'account-deleted': []

  // Artifact
  'close-artifact': []
  'update:artifact-view': [view: ArtifactView]
  'update:artifact-width': [width: number]

  // Aside
  'aside-change': [state: { open: boolean, tab: string | undefined }]
}>()

// ---------------------------------------------------------------------------
// Internal state
// ---------------------------------------------------------------------------

/* Sidebar state comes from useSidebarState so TChatApp inherits its two
   behaviours for free: the mode is remembered in localStorage across reloads,
   and dropping below `sidebarMobileBreakpoint` collapses the sidebar and
   restores the previous desktop mode on the way back up (a page opened on a
   narrow viewport starts collapsed). Writes go through `setMode` (not the raw
   ref) so every desktop change is persisted, including the direct
   `sidebarMode = 'icon'` assignments in the template; drawer opens and closes
   on a narrow viewport are not. */
const t = useAiI18n()
/* The cards above the composer: the queue given (`null` turns them off), or that of the
   `useChatThreads` in an ancestor whose messages are the ones shown here; with one instance, that
   one; with several and no match, none (see `chat-approvals-key.ts`). */
const chatThreads = inject(CHAT_THREADS_KEY, null)
const pickedApprovals = computed(() => pickChatApprovals(chatThreads, props.messages))
const approvalQueue = computed(() => (props.approvals === undefined ? pickedApprovals.value.approvals : props.approvals))
/* On a phone, while a card waits, the composer keeps only its bar (stop, the pickers): the cards
   get the room, and what is typed waits until they are answered. */
const cardsWaiting = computed(() => (approvalQueue.value?.items.value.length ?? 0) > 0)
const awaitingAnswer = computed(() => effectiveIsMobile.value && cardsWaiting.value)
/* The queued messages above the composer: the queue given, or that of the `useChatThreads` whose
   conversation is on screen, picked as the cards are (`null` turns them off). While its turn has a
   server run the composer queues through it, attachments included. */
const messageQueue = computed<MessageQueue | null>(() =>
  props.messageQueue === undefined ? pickChatMessageQueue(chatThreads, props.messages) : props.messageQueue,
)
const serverQueueOn = computed(() => !!messageQueue.value?.available.value)
const effectiveQueueWhileRunning = computed(() => props.queueWhileRunning || serverQueueOn.value)

const sidebar = useSidebarState({
  initialMode: props.initialSidebarMode,
  storageKey: props.sidebarStorageKey,
  mobileBreakpoint: props.sidebarMobileBreakpoint,
})
const sidebarMode = computed<SidebarMode>({
  get: () => sidebar.mode.value,
  set: (v) => sidebar.setMode(v),
})

/* `useSidebarState` already tracks the viewport against
   `sidebarMobileBreakpoint` - that is what auto-collapses the sidebar. Falling
   back to it means the drawer presentation and the auto-collapse cannot
   disagree.

   They used to: `isMobile` defaulted to `false`, so on a narrow viewport the
   sidebar collapsed (composable) but expanding it again rendered a 300px
   inline column instead of an overlay (prop). Measured at 520px: the main
   column was left 220px, the model name wrapped onto three lines and message
   bubbles broke one word per line. Backdrop and scroll lock were off too,
   since both hang off the same flag. */
const effectiveIsMobile = computed(() => props.isMobile ?? sidebar.isMobile.value)

/* The brand header's toggle: on a wide screen it folds the sidebar into the
   rail, in the narrow drawer it closes the drawer. Also handed to a `brand`
   slot override, which would otherwise have no way to collapse. */
const headerToggleLabel = computed(() => (effectiveIsMobile.value ? t.value.sidebar.close : t.value.sidebar.collapse))
function collapseFromHeader(): void {
  sidebarMode.value = effectiveIsMobile.value ? 'hidden' : 'icon'
}

/* Whether the conversation list is folded away under its heading. Remembered
   next to the sidebar mode, under the same key (and not at all without one). */
const threadsCollapsedKey = props.sidebarStorageKey ? `${props.sidebarStorageKey}:recents-collapsed` : null
function readThreadsCollapsed(): boolean {
  if (!threadsCollapsedKey || typeof localStorage === 'undefined') return false
  try {
    return localStorage.getItem(threadsCollapsedKey) === '1'
  } catch {
    return false
  }
}
const threadsCollapsed = ref(readThreadsCollapsed())
watch(threadsCollapsed, (collapsed) => {
  if (!threadsCollapsedKey || typeof localStorage === 'undefined') return
  try {
    if (collapsed) localStorage.setItem(threadsCollapsedKey, '1')
    else localStorage.removeItem(threadsCollapsedKey)
  } catch {
    // Storage blocked: the fold still applies for this page.
  }
})

const settingsOpen = ref(false)
/* The section the settings dialog shows. Tracks the user's own navigation in
   the dialog, so reopening it lands where they left off. */
const settingsSection = ref<string | undefined>(undefined)
const commandPaletteOpen = ref(false)
const copiedId = ref<string | null>(null)

const composerText = computed({
  get: () => props.inputText,
  set: (v) => emit('update:input-text', v),
})

const hasMessages = computed(() => (props.messages?.length ?? 0) > 0)

/* The composer's `/` menu: open while a command is typed at the start of an empty box, when the
   app gives commands. It takes the composer's navigation keys before the composer does. */
const slashMenuId = `t-slash-menu-${useId()}`
const slashMenu = useSlashMenu({
  text: composerText,
  items: computed(() => props.slashCommands ?? []),
  onPick: (item) => {
    composerText.value = textWithCommand(item)
    void nextTick(focusComposer)
  },
})
const slashOpen = computed(() => props.slashCommands != null && slashMenu.open.value)
watch(slashMenu.query, (query) => emit('slash-query', query))
function onComposerKeydown(event: KeyboardEvent): void {
  if (slashOpen.value && slashMenu.handleKey(event)) event.preventDefault()
  emit('composer-keydown', event)
}
const effectiveComposerInputAttrs = computed(() => {
  if (props.slashCommands == null) return props.composerInputAttrs
  const listed = slashOpen.value && slashMenu.results.value.length > 0
  return {
    ...props.composerInputAttrs,
    role: 'combobox',
    'aria-autocomplete': 'list',
    'aria-expanded': String(slashOpen.value),
    'aria-controls': listed ? slashMenuId : undefined,
    'aria-activedescendant': listed ? `${slashMenuId}-${slashMenu.highlighted.value}` : undefined,
  }
})

/* The right-side panel. State is per conversation; a conversation that has no id yet shares a
   draft slot that is handed to the real id once the first turn creates it. */
const aside = useAsidePanel({
  key: () => props.activeThreadId,
  storageKey: props.asideStorageKey,
  defaultOpen: props.asideDefaultOpen,
  defaultWidth: props.asideDefaultWidth,
  minWidth: props.asideMinWidth,
  maxWidth: props.asideMaxWidth,
  defaultTab: props.asideDefaultTab,
})
watch(
  () => props.activeThreadId,
  (id, previous) => {
    if (id && !previous) aside.adoptDraft(id)
  },
)
watch([aside.open, aside.tab], ([open, tab]) => emit('aside-change', { open, tab }))
provideChatAside({ open: aside.open, tab: aside.tab, show: aside.show, hide: aside.hide, toggle: aside.toggle, setTab: aside.setTab })

/* `view` decides whether the main area is the conversation or the consumer's
   own page. The sidebar and top bar are outside this branch on purpose: a
   product's nav must survive navigating away from the chat. */
const CHAT_VIEW_ID = 'chat'

const isChatView = computed(() => props.view === CHAT_VIEW_ID)
/* The landing page: a conversation view with no messages, unless the shell was told to skip it or a
   picked conversation is still loading. It is drawn inside the workspace, in the thread's place, so
   the panel beside it is the same element before and after the first message. */
const showsLanding = computed(() => !hasMessages.value && props.showLanding && !isOpeningThread.value)
/* The panel exists only where the conversation does (not on a product's own `view`, and not on the
   landing page unless `asideOnLanding`), and only when the product gave it content. */
const asideAvailable = computed(
  () => !!slots.aside && isChatView.value && (props.asideOnLanding || !showsLanding.value),
)
const asideShown = computed(() => asideAvailable.value && aside.open.value)
/* `asideFullHeight`: the main area becomes a grid whose last column is the panel's, from the top.
   It holds for as long as the panel is available, open or not, so opening it moves nothing up or
   down; the narrow screen keeps its overlay. */
const asideFullHeightLayout = computed(() => props.asideFullHeight && asideAvailable.value && !effectiveIsMobile.value)
/* The full Recents page is the shell's own, when it has the controller to fill it. */
const isRecentsView = computed(() => props.view === RECENTS_VIEW_ID && props.recents != null)
/* A page of the product's, in the `view` slot. */
const isPageView = computed(() => !isChatView.value && !isRecentsView.value)
/* The slot's section stays mounted once a page has been shown (see the template). */
const pageViewMounted = ref(isPageView.value)
watch(isPageView, (page) => {
  if (page) pageViewMounted.value = true
})
const threadsLabelText = computed(() => props.threadsLabel ?? t.value.chat.threadsLabel)
/* `threadsLabel: ''` hides the expanded list's heading; the rail button still needs a name. */
const railThreadsLabelText = computed(
  () => props.railThreadsLabel || props.threadsLabel || t.value.chat.threadsLabel,
)

/* New chat is a nav destination like any other - it opens the landing page -
   so it renders through TSidebarNav rather than as its own button. It used to
   be hand-written markup in this file whose geometry was pixel-matched to
   `.t-sidebar-nav__item` by hand, and the copy had already drifted: it sat at
   x=0 instead of the nav's 8px inset (icon and label 8px left of every entry
   below it) and carried `font-weight: 500` nothing else had. Duplicated
   geometry cannot be kept in sync by discipline; sharing the row markup is
   what makes "same kind of thing" true rather than aspirational. */
const NEW_CHAT_NAV_ID = '__new-chat'

/* Falls back to the dictionary so this entry translates like every other one.
   The prop stays as the override for products that call it something else. */
const newChatText = computed(() => props.newChatLabel ?? t.value.chat.newChat)
const composerPlaceholderText = computed(() => props.composerPlaceholder ?? t.value.chat.placeholder)
const settingsTitleText = computed(() => props.settingsTitle ?? t.value.sidebar.settings)

/* Active on the landing page, which is where it navigates to. The rail's copy
   of this button used to light up on `hasMessages || activeThreadId` - the
   exact inverse - so the two disagreed about what the entry meant. */
const isLandingView = computed(
  () => isChatView.value && !props.activeThreadId && !props.openingThreadId && !hasMessages.value,
)

/* A conversation picked in the sidebar whose transcript has not arrived. The
   main area says so (`TThreadLoading`) instead of keeping the previous
   transcript up, so the click shows at once and the screen never offers a
   composer that would send to a conversation other than the one highlighted. */
const isOpeningThread = computed(
  () => !!props.openingThreadId && props.openingThreadId !== props.activeThreadId,
)

/* One unlabelled leading group: new chat, the consumer's flat `mainNav`, and
   the built-in views are all primary destinations with no heading, and a
   heading is the only thing that makes a group its own visual unit. Splitting
   them put a 14px gap in the expanded sidebar and a rule in the rail between
   entries that read as one list. Titled `navGroups` still follow as their own
   sections. Empty when nothing contributes, otherwise TSidebarNav would draw
   the group's margins around nothing. */
const primaryNavGroup = computed<NavGroup | null>(() => {
  const newChat: NavItem[] = props.showNewChatButton
    ? [
        {
          id: NEW_CHAT_NAV_ID,
          label: newChatText.value,
          icon: props.newChatIcon,
          iconMotion: props.newChatIconMotion ?? undefined,
          active: isLandingView.value && props.landingIsNewChat,
        },
      ]
    : []

  const builtin: NavItem[] = props.builtinViews.map((id) => ({
    id,
    label: t.value.views[id],
    icon: BUILTIN_VIEW_ICONS[id],
    iconMotion: BUILTIN_VIEW_ICON_MOTIONS[id],
    active: props.view === id,
  }))

  const items = [...newChat, ...props.mainNav, ...builtin]
  return items.length > 0 ? { id: '__primary', items } : null
})

/* The primary group is pinned above the scroll area; the consumer's titled
   sections scroll with the conversation history below them. Reaching New chat
   should never require scrolling back up a long history - which is exactly
   what happened before, since the whole sidebar was one scroller.
   (Manus splits at the same seam: its "New task" / "Agent" block sits outside
   the scroller while Projects and Tasks scroll.)
   Titled sections stay in the scroller on purpose - pinning them would eat
   the history's height, and unlike the primary run they can be arbitrarily
   long. */
const pinnedNavGroups = computed<ReadonlyArray<NavGroup>>(() =>
  primaryNavGroup.value ? [primaryNavGroup.value] : [],
)

/* Full list, in visual order. The rail draws every entry at once (it has no
   scroll split), and the top bar looks up the active view here. */
const navGroupsResolved = computed<ReadonlyArray<NavGroup>>(() => [
  ...pinnedNavGroups.value,
  ...props.navGroups,
])

/* Now that the shell routes, "navigate" also has to mean the drawer gets out
   of the way: on a phone the sidebar is an overlay, so every destination it
   sends you to would otherwise open underneath it. Outside the guard below -
   picking the conversation you are already on still has to close the drawer. */
function goToView(id: ChatAppView): void {
  if (effectiveIsMobile.value) sidebarMode.value = 'hidden'
  /* Guarded so a click on the page you are already on is not a mutation. On a
     plain ref the write would be a no-op anyway, but a consumer whose setter
     has side effects (a router push, an analytics event, a fetch) would see
     one per click on the current entry. */
  if (props.view !== id) emit('update:view', id)
}

/* Every path back to the conversation goes through these two, so the routing
   cannot be right in the sidebar and missing in the rail. Both surfaces, plus
   the thread list's own "+", call them. */
function startNewChat(): void {
  goToView(CHAT_VIEW_ID)
  emit('new-chat')
}

function openThread(threadId: string): void {
  goToView(CHAT_VIEW_ID)
  emit('select-thread', threadId)
}

/* New chat keeps its own event - consumers wire it to starting a conversation,
   not to a page. Everything else is a destination and moves `view`. */
function onNavSelect(item: NavItem): void {
  if (item.id === NEW_CHAT_NAV_ID) {
    startNewChat()
    return
  }
  goToView(item.id)
  emit('nav', item)
}

/* The nav entry that `view` is currently on, so the top bar can name it. Both
   the top bar's own comment and `TResourcePage` ("page name in the top bar,
   not repeated in the body" - which is why those pages render no title of
   their own) promised this, but the bar only ever read `threadTitle` - so a
   view opened while a conversation was active sat under that conversation's
   title, and one opened from a fresh session sat under the brand name. */
const activeViewLabel = computed(() => {
  if (isChatView.value) return ''
  if (props.view === RECENTS_VIEW_ID) return threadsLabelText.value
  for (const group of navGroupsResolved.value) {
    const hit = group.items.find((item) => item.id === props.view)
    if (hit) return hit.label
  }
  return ''
})

/* While a thread opens, the bar names it: `threadTitle` still describes the
   conversation being left. The product's own lookup first (it also knows the
   conversations it lists outside `threads`), then the thread list. */
const openingThreadTitle = computed(() => {
  const id = props.openingThreadId
  if (!isOpeningThread.value || !id) return undefined
  return props.threadTitleOf?.(id) || props.threads.find((thread) => thread.id === id)?.title
})

/* Nothing to name (a new chat, a conversation still untitled, a view with no
   nav label) leaves the bar empty, with the sidebar shown or hidden alike: the
   brand belongs to the sidebar header, and in the bar it reads as a page title
   for a page that has none. */
const topbarTitle = computed(
  () =>
    activeViewLabel.value
    || (isOpeningThread.value ? openingThreadTitle.value : props.threadTitle)
    || '',
)

/* The open conversation's title becomes editable, with its own menu, when the
   shell can act on it: a `recents` controller (rename / archive), a saved
   conversation (not the landing page, not one still being created or opened),
   and the conversation view. */
const activeThreadItem = computed(() => {
  const id = props.activeThreadId
  if (!id) return undefined
  return props.threads.find((thread) => thread.id === id)
    ?? props.recents?.page.threads.value.find((thread) => thread.id === id)
})
const canManageThreadTitle = computed(
  () =>
    props.recents != null
    && isChatView.value
    && !!props.activeThreadId
    && !isOpeningThread.value
    && !activeThreadItem.value?.pending,
)
/* What the bar shows right after a rename or an archive, until the lists the
   title and the archive state come from catch up (or for a conversation the
   lists do not hold, such as one archived out of the Active list). */
const threadTitleEdits = ref<{ id: string; title?: string; archived?: boolean } | null>(null)
watch(() => props.activeThreadId, () => (threadTitleEdits.value = null))
const editableThreadTitle = computed(() => {
  const edit = threadTitleEdits.value
  return (edit?.id === props.activeThreadId ? edit?.title : undefined) ?? topbarTitle.value
})
const activeThreadArchived = computed(() => {
  const edit = threadTitleEdits.value
  return (edit?.id === props.activeThreadId ? edit?.archived : undefined) ?? Boolean(activeThreadItem.value?.archived)
})

function noteThreadEdit(patch: { title?: string; archived?: boolean }): void {
  const id = props.activeThreadId
  if (!id) return
  const current = threadTitleEdits.value?.id === id ? threadTitleEdits.value : { id }
  threadTitleEdits.value = { ...current, ...patch }
}

async function renameActiveThread(title: string): Promise<void> {
  const id = props.activeThreadId
  if (!id || !props.recents) return
  const previous = threadTitleEdits.value
  noteThreadEdit({ title })
  if (!(await props.recents.rename(id, title))) threadTitleEdits.value = previous
}

async function setActiveThreadArchived(archived: boolean): Promise<void> {
  const id = props.activeThreadId
  if (!id || !props.recents) return
  const ok = archived ? await props.recents.archive(id) : await props.recents.unarchive(id)
  if (ok) noteThreadEdit({ archived })
}

const hasPinnedNav = computed(() => pinnedNavGroups.value.length > 0)

/* Sections that render nothing on their own - they need either an
   `accountClient` to call or a consumer slot to fill them. */
const WIRED_SETTINGS_IDS = ['security', 'personalization', 'usage'] as const

const slots = useSlots()

/* A sub-agent's own view: in the side panel on `subAgentAside`'s tab when the product shows it there, else in place
   inside its run. Stopping a task goes through the conversation's `useChatThreads` unless `stopTask` says otherwise. */
const subAgentView = provideSubAgentView(createSubAgentView({
  panel: () => !!props.subAgentAside && !!slots.aside,
  stopTask: () => (props.stopTask === undefined ? pickChatStopTask(chatThreads, props.messages) : props.stopTask),
}))
/* What the side panel was before a sub-agent opened in it, to put back when the view closes. */
let asideBeforeSubAgent: { open: boolean; tab: string | undefined } | null = null
watch(
  () => subAgentView.current.value,
  (id, previous) => {
    const tab = props.subAgentAside
    if (!tab || !slots.aside) return
    if (id) {
      if (!previous) asideBeforeSubAgent = { open: aside.open.value, tab: aside.tab.value }
      aside.show(tab)
      return
    }
    const before = asideBeforeSubAgent
    asideBeforeSubAgent = null
    if (!before || aside.tab.value !== tab || !aside.open.value) return
    if (!before.open) aside.hide()
    else if (before.tab !== undefined && before.tab !== tab) aside.setTab(before.tab)
  },
)
/* The side panel closed (or turned to another tab): no sub-agent is shown any more. */
watch([aside.open, aside.tab], ([open, tab]) => {
  if (subAgentView.current.value && props.subAgentAside && (!open || tab !== props.subAgentAside)) {
    asideBeforeSubAgent = null
    subAgentView.close()
  }
})
/* Another conversation: no sub-agent of the previous one stays open. */
watch(() => props.activeThreadId, () => subAgentView.close())

/* The rail's conversations button: something to list, or the product's own list. */
const showRailThreads = computed(
  () => props.railThreads && (props.threads.length > 0 || Boolean(slots['rail-threads'])),
)

/* The built-in sections, named in the current locale. Security and
   Personalization are listed but render nothing without an `accountClient`; a
   consumer on a different backend drops them by passing its own list. Listing
   them here is what makes them built-in rather than something every product
   re-declares. */
const settingsSectionList = computed<ReadonlyArray<SettingsSection>>(
  () =>
    props.settingsSections ?? [
      { id: 'account', label: t.value.account.accountLabel, icon: 'lucide:circle-user-round' },
      { id: 'security', label: t.value.settings.security, icon: 'lucide:shield-check' },
      { id: 'personalization', label: t.value.settings.personalization, icon: 'lucide:sparkles' },
      { id: 'usage', label: t.value.settings.usage, icon: 'lucide:gauge' },
      { id: 'appearance', label: t.value.settings.appearance, icon: 'lucide:settings' },
      { id: 'about', label: t.value.settings.about, icon: 'lucide:info' },
    ],
)

/* A listed section that opens an empty pane is worse than an absent one: it
   reads as a broken page rather than a capability this deployment does not
   have. So the wired sections drop out unless something can actually fill
   them - the client, or a consumer slot of the same id. */
const effectiveSettingsSections = computed(() =>
  settingsSectionList.value.filter((section) => {
    if (!WIRED_SETTINGS_IDS.includes(section.id as (typeof WIRED_SETTINGS_IDS)[number])) return true
    return accountSettings !== null || Boolean(slots[`settings-${section.id}`])
  }),
)

/* Section ids the shell renders itself. The three originals plus the pages
   that arrived once `accountClient` was a thing: their data is the framework's
   own and their routes are user-facing, so there is nothing for a consumer to
   fetch. A consumer can still take any of them over by filling the matching
   `#settings-{id}` slot. */
const BUILTIN_SETTINGS_IDS = new Set([
  'account',
  'appearance',
  'about',
  'security',
  'personalization',
  'usage',
])

const customSettingsSections = computed(() =>
  settingsSectionList.value.filter((s) => !BUILTIN_SETTINGS_IDS.has(s.id)),
)

/* Controllers for the wired pages.
   Built once during setup, not inside a computed: these register refs and are
   composables, so re-running them on a dependency change would create a fresh
   set of state on every read. Account and Security deliberately share one
   controller - they read the same profile, and two would fetch it twice and
   then disagree after a write. `null` when no client was supplied, which is
   what hides both pages. */
const accountSettings = props.accountClient
  ? useAccountSettings({
      client: props.accountClient,
      onProfileChange: (profile) => emit('profile-change', profile),
      onAccountDeleted: () => emit('account-deleted'),
    })
  : null
const personalization = props.accountClient
  ? useAiPersonalization({ client: props.accountClient })
  : null
const usage = props.accountClient ? useAiUsage({ client: props.accountClient }) : null

/* ── Usage limits around the composer ─────────────────────────────────
   What the composer says about usage, most pressing first: a turn that was
   refused (by the server, or here before sending), a window the figures say
   is at its limit, then a quieter hint when one is close. Without an
   `accountClient` there are no figures and only a refusal can show. */
const blockedNotice = ref<UsageLimitNotice | null>(null)
const dismissedWarning = ref<string | null>(null)
const warningKey = (w: { window: string; resetsAt: string | null }) => `${w.window}|${w.resetsAt ?? ''}`

interface ComposerUsageNotice {
  tone: 'limit' | 'warning'
  window: UsageLimitNotice['window']
  resetsAt: string | null
  usedPercent: number | null
  dismissible: boolean
  draftKept: boolean
}

const composerNotice = computed<ComposerUsageNotice | null>(() => {
  if (blockedNotice.value) {
    return { tone: 'limit', ...blockedNotice.value, usedPercent: null, dismissible: true, draftKept: true }
  }
  if (props.usageLimitNotice) {
    return { tone: 'limit', ...props.usageLimitNotice, usedPercent: null, dismissible: true, draftKept: false }
  }
  const exceeded = usage?.exceeded.value
  if (exceeded) return { tone: 'limit', ...exceeded, usedPercent: null, dismissible: false, draftKept: false }
  const warning = usage?.warning.value
  if (warning && dismissedWarning.value !== warningKey(warning)) {
    return { tone: 'warning', window: warning.window, resetsAt: warning.resetsAt, usedPercent: warning.usedPercent, dismissible: true, draftKept: false }
  }
  return null
})

function dismissComposerNotice(): void {
  if (blockedNotice.value) {
    blockedNotice.value = null
    return
  }
  if (props.usageLimitNotice) {
    emit('update:usage-limit-notice', null)
    return
  }
  const warning = usage?.warning.value
  if (warning) dismissedWarning.value = warningKey(warning)
}

/* The reset passed while the notice was up: drop it and read the figures again. */
function onComposerNoticeExpired(): void {
  blockedNotice.value = null
  if (props.usageLimitNotice) emit('update:usage-limit-notice', null)
  void usage?.load()
}

if (usage) {
  onMounted(() => { void usage.load() })
  // Every finished turn moves the figures; so does a refusal.
  watch(() => props.isStreaming, (streaming, was) => {
    if (was && !streaming) void usage.load()
  })
  watch(() => props.usageLimitNotice, (notice) => {
    if (notice) void usage.load()
  })
}

const showModelPicker = computed(() => {
  if (props.models.length > 1) return true
  const current = props.models.find((m) => m.id === props.model) ?? props.models[0]
  return (current?.efforts?.length ?? 0) > 0
})

const themeOptions = computed<ReadonlyArray<{ id: ThemePref; label: string }>>(() => [
  { id: 'light', label: t.value.settings.themeLight },
  { id: 'dark', label: t.value.settings.themeDark },
  { id: 'auto', label: t.value.settings.themeSystem },
])

/* The OS colour-scheme preference has to live in a ref: `matchMedia().matches`
   is a plain boolean read, so a computed that calls it tracks nothing and
   would never re-evaluate when the OS flips. The media-query listener below
   writes this ref, which is what makes `theme="auto"` reactive. */
const prefersDark = ref(false)

/* Normalized so a consumer still passing the legacy `theme="system"` resolves
   the same as `theme="auto"` instead of silently falling through to light. */
const themePref = computed<ThemeMode>(() => normalizeThemeMode(props.theme, 'auto'))

const resolvedTheme = computed<'light' | 'dark'>(() => {
  if (themePref.value === 'auto') return prefersDark.value ? 'dark' : 'light'
  return themePref.value === 'dark' ? 'dark' : 'light'
})

const isDark = computed(() => resolvedTheme.value === 'dark')

// ---------------------------------------------------------------------------
// Theme application
// ---------------------------------------------------------------------------

/* The `@tnzi/ui` theme context of the host app, when there is one
   (`defineChatApp()` provides it for every chat product, `createTnziUi()` for
   an app that mounts the shell inside its own UI). */
const hostTheme = inject<ThemeContext | undefined>(THEME_CONTEXT_KEY, undefined)

/* Applying the theme means putting the document into the resolved mode: the
   package stylesheet declares the full light and dark `--tnzi-ai-*` palettes
   and switches between them off the `.dark` class. TChatApp deliberately does
   NOT push token values of its own here - writing inline variables would pin
   them above any palette the host app configured on `:root`. Consumers who
   want different colours call `applyAiTheme()` from `@tnzi/ui-ai/theme`.

   ★ With a host theme context the mode goes THROUGH the context. The context
   writes the mode-dependent `--tnzi-*` tokens (base text, containers,
   borders) inline on `<html>`, which outranks the stylesheet's `.dark` block,
   and it writes the `.dark` class itself whenever its settings change. Toggling
   only the class left those tokens in the context's mode: a user who picked
   Dark got a dark canvas with markdown headings, tool-call cards and borders
   still painted light, and the next change to the context (a published brand
   colour) put the class back to light as well. */
function applyTheme(): void {
  if (!props.autoApplyTheme) return
  // Pass the ALREADY-resolved value, not `themePref`: this component resolves
  // 'auto' against the `prefersDark` ref (kept current by the media-query
  // listener below), while the context and `applyThemeModeToDocument` would
  // re-read matchMedia themselves. Handing them 'light'/'dark' keeps
  // `<html>.dark` and the component's own `--dark` class from ever disagreeing.
  if (hostTheme) {
    hostTheme.setMode(resolvedTheme.value)
    return
  }
  // Routed through @tnzi/ui so the ecosystem has one writer of the dark class.
  applyThemeModeToDocument(resolvedTheme.value)
}

watch(resolvedTheme, applyTheme, { immediate: false })

// ---------------------------------------------------------------------------
// Listen to OS colour-scheme changes when in 'auto' mode
// ---------------------------------------------------------------------------

let systemMediaQuery: MediaQueryList | null = null
function onSystemThemeChange(event: MediaQueryListEvent): void {
  prefersDark.value = event.matches
}

onMounted(() => {
  if (typeof window !== 'undefined' && window.matchMedia) {
    systemMediaQuery = window.matchMedia('(prefers-color-scheme: dark)')
    prefersDark.value = systemMediaQuery.matches
    systemMediaQuery.addEventListener('change', onSystemThemeChange)
  }
  applyTheme()
  /* A press shows on iOS too (`styles/touch.css`). */
  enableTouchPress()
  /* The page-wide rules (body face, thin scrollbars, focus line) apply while
     the shell is on the page (`headless/app-root.ts`). */
  releaseAppMark = markAiApp()
})

let releaseAppMark: (() => void) | null = null

onBeforeUnmount(() => {
  systemMediaQuery?.removeEventListener('change', onSystemThemeChange)
  systemMediaQuery = null
  releaseAppMark?.()
  releaseAppMark = null
})

// ---------------------------------------------------------------------------
// Event handlers
// ---------------------------------------------------------------------------

function pickTheme(next: ThemePref): void {
  emit('update:theme', next)
}

/* The built-in menu ids drive built-in surfaces; everything else is the
   consumer's. `user-menu` fires for all of them either way, so a consumer can
   observe or override without losing the default behaviour.

   `account` opens the settings dialog on its Account section, which is where
   the account lives in this shell. It used to only re-emit, so in a product
   that did not listen the built-in Account entry closed the menu and did
   nothing else. */
function onUserMenuSelect(id: string): void {
  if (id === 'settings') {
    openSettings()
  } else if (id === 'account') {
    openSettings('account')
  } else if (id === 'sign-out') {
    emit('sign-out')
  }
  emit('user-menu', id)
}

/* With a `section` the dialog opens there, and only if the built-in dialog is
   on and has that section: a product that turned the dialog off or dropped its
   Account section keeps `account` to itself.
   Without one the request is dropped, so the narrow sheet opens on its section
   list; the wide dialog still reopens on the section the user left it on (the
   dialog keeps that itself). */
function openSettings(section?: string): void {
  if (section) {
    const available =
      props.enableSettings && effectiveSettingsSections.value.some((s) => s.id === section)
    if (!available) return
    settingsSection.value = section
  } else {
    settingsSection.value = undefined
  }
  settingsOpen.value = true
  emit('open-settings')
}

function onLandingSubmit(text: string, files: File[] = []): void {
  if (!text.trim() && files.length === 0) return
  void sendUnlessAtLimit(text, files)
}

function onChipClick(chip: LandingChip): void {
  emit('select-suggestion', chip)
  if (chip.prompt != null) {
    emit('update:input-text', chip.prompt)
  }
}

function onComposerSend(text: string, files: File[]): void {
  if (!text.trim() && files.length === 0) return
  void sendUnlessAtLimit(text, files)
}

function onComposerQueue(text: string, files: File[]): void {
  if (!text.trim() && files.length === 0) return
  // The server queue takes it through `send`: `useChatThreads().send` queues while the run goes on,
  // with the turn's model and metadata, as any send. Not checked against the usage figures: it goes later.
  if (serverQueueOn.value) emit('send', text, files)
  else emit('queue', text, files)
  emit('update:input-text', '')
}

/**
 * Send, unless the usage figures say a window is at its limit. Then the
 * figures are read again first (an administrator may have reset the window,
 * or the reading is old); when the limit still holds the text stays in the
 * composer, the files go back into it, and the notice says when it resets -
 * instead of a round trip the server would refuse. Without figures (no
 * `accountClient`) every send goes through and the server decides.
 */
async function sendUnlessAtLimit(text: string, files: File[]): Promise<void> {
  if (usage?.currentLimit()) {
    await usage.load()
    const limit = usage.currentLimit()
    if (limit) {
      blockedNotice.value = limit
      // The composer cleared its chips as it emitted; hand them back.
      if (files.length > 0) mountedComposer()?.addAttachments(files)
      return
    }
  }
  blockedNotice.value = null
  emit('send', text, files)
  emit('update:input-text', '')
}

/* Files placed from outside (a capture button, files the operating system
   handed over). Exactly one composer is on screen whenever the conversation is
   showing: the landing hero when there are no messages, the thread composer
   otherwise. */
const landingRef = useTemplateRef<InstanceType<typeof TLandingPage>>('landingRef')
const rootRef = useTemplateRef<HTMLElement>('rootRef')
/* On a phone the shell covers exactly the visible screen and the page under it
   does not move: only the conversation scrolls (`pinNarrowViewport`). */
const pinnedToViewport = computed(() => effectiveIsMobile.value && props.pinNarrowViewport)
usePinnedViewport(rootRef, pinnedToViewport)

/* The shell's own width, for `sidebarAutoCollapse`: the window's until the
   shell is laid out (so a narrow first load starts on the rail instead of
   folding after the first paint), then the element's, which also covers a
   shell embedded in something narrower than the window. */
const shellWidth = ref(typeof window === 'undefined' ? 1280 : window.innerWidth)
function measureShell(): void {
  const measured = rootRef.value?.clientWidth ?? 0
  shellWidth.value = measured > 0 ? measured : typeof window === 'undefined' ? shellWidth.value : window.innerWidth
}
let shellObserver: ResizeObserver | null = null
onMounted(() => {
  measureShell()
  window.addEventListener('resize', measureShell)
  if (typeof ResizeObserver !== 'undefined' && rootRef.value) {
    shellObserver = new ResizeObserver(measureShell)
    shellObserver.observe(rootRef.value)
  }
})
onBeforeUnmount(() => {
  if (typeof window !== 'undefined') window.removeEventListener('resize', measureShell)
  shellObserver?.disconnect()
  shellObserver = null
})
useSidebarAutoFit(sidebar, {
  room: () =>
    conversationRoom({
      containerWidth: shellWidth.value,
      sidebarWidth: props.sidebarWidth,
      asideWidth: asideShown.value ? aside.width.value : 0,
      artifactWidth: props.artifact ? props.artifactWidth : 0,
    }),
  enabled: () => props.sidebarAutoCollapse && !effectiveIsMobile.value,
  minWidth: () => props.conversationMinWidth,
})
const threadComposerRef = useTemplateRef<InstanceType<typeof TThreadComposer>>('threadComposerRef')
const pendingAttachments = shallowRef<File[]>([])

function mountedComposer(): Pick<InstanceType<typeof TThreadComposer>, 'addAttachments' | 'focus'> | null {
  return threadComposerRef.value ?? landingRef.value ?? null
}

/**
 * Place files in the mounted composer, through the same checks and the same
 * `attachments-rejected` event as a drop; they wait there for the user's text.
 * Null when attachments are off. With a `#view` page in the main area there is
 * no composer: the files are held (`pending`), the shell asks to show the
 * conversation, and they land when its composer appears.
 */
function addAttachments(incoming: FileList | readonly File[]): ChatAppAttachmentsResult | null {
  if (!props.enableAttachments) return null
  const composer = mountedComposer()
  if (composer) {
    const result = composer.addAttachments(incoming)
    return result ? { ...result, pending: [] } : null
  }
  const held = Array.from(incoming)
  pendingAttachments.value = [...pendingAttachments.value, ...held]
  goToView(CHAT_VIEW_ID)
  return { added: [], rejected: [], pending: held }
}

/** Focus the mounted composer's text box, caret at the end. */
function focusComposer(): void {
  mountedComposer()?.focus()
}

/* Held files land as soon as a composer is on screen. Turning attachments off
   drops them: they would otherwise appear the moment it is turned back on. */
watch(
  [landingRef, threadComposerRef, () => props.enableAttachments],
  () => {
    if (!props.enableAttachments) {
      pendingAttachments.value = []
      return
    }
    const composer = mountedComposer()
    if (!composer || pendingAttachments.value.length === 0) return
    const held = pendingAttachments.value
    pendingAttachments.value = []
    composer.addAttachments(held)
  },
  { flush: 'post' },
)

defineExpose({
  addAttachments,
  focusComposer,
  /** Open the side panel, optionally on a given tab. */
  openAside: aside.show,
  closeAside: aside.hide,
  toggleAside: aside.toggle,
})

const rootStyle = computed<Record<string, string> | undefined>(() => {
  if (props.contentWidth == null) return undefined
  const w = typeof props.contentWidth === 'number' ? `${props.contentWidth}px` : props.contentWidth
  return { '--tnzi-ai-content-width': w }
})

/* The thread's scrolling (`useAutoScroll`). It opens at the latest turn and
   follows the bottom while the reader is there; scrolling up stops it at once.
   What a send does depends on `scrollMode`: 'chat' brings the sent message
   into view under the end of the previous answer and follows the answer no
   further than that message reaching the top; 'follow' goes to the bottom and
   keeps following. The composer sits inside the scroller, so its keys and
   menus do not count as the reader scrolling. */
const {
  containerRef: threadScrollRef,
  isAtBottom: threadAtBottom,
  scrollToBottom,
  followTurn,
} = useAutoScroll({ notContent: '.t-thread-composer-wrap', openAt: () => takeConversationReturn() })
const turnTopOffset = (): number => (effectiveIsMobile.value ? 12 : 24)
/* The "scroll to the latest" button: up while the end of the last message (or
   of what `#thread-end` shows after it) is below the composer, measured on every scroll and every change of size
   (`useLatestInView`), so a step collapsed with nothing scrolled takes it
   down. What `#thread-composer-top` shows above the box counts as covering the
   conversation. Clicking it goes to the bottom and follows from there. */
const latestInView = useLatestInView({
  container: threadScrollRef,
  latest: () => {
    // What `#thread-end` shows after the last message is the conversation's end.
    const end = threadScrollRef.value?.querySelector<HTMLElement>(':scope > .t-chat-app__thread-col > .t-chat-app__thread-end')
    if (end && end.childElementCount > 0) return end
    const last = props.messages[props.messages.length - 1]
    return last ? messageElement(last.id) : null
  },
  viewportBottom: () => {
    const wrap = threadScrollRef.value?.querySelector(':scope > .t-chat-app__thread-col > .t-thread-composer-wrap')
    // What `#thread-composer-top` shows covers the conversation too; an empty
    // one is not drawn (and measures 0).
    const top = wrap?.querySelector(':scope > .t-thread-composer__top')
    const edge = top && top.childElementCount > 0 ? top : wrap?.querySelector(':scope > .t-thread-composer')
    return edge?.getBoundingClientRect().top
  },
})
const showScrollToLatest = computed(
  () => latestInView.hiddenBelow.value && !isOpeningThread.value && props.messages.length > 0,
)
function scrollToLatest(): void {
  scrollToBottom('smooth')
}

const lastUserMessageId = computed(() => {
  for (let i = props.messages.length - 1; i >= 0; i -= 1) {
    if (props.messages[i]!.role === 'user') return props.messages[i]!.id
  }
  return undefined
})
function messageElement(id: string): HTMLElement | null {
  return threadScrollRef.value?.querySelector<HTMLElement>(`[data-message-id="${CSS.escape(id)}"]`) ?? null
}
/* A new user message at the end of the conversation on screen is a turn being
   sent (from the composer, an edit, or the product): bring it into view and
   follow its answer the way `scrollMode` says. Opening
   another conversation is not (its transcript arrives with a new thread id, or
   at the end of a load), and neither is a jump of more than one turn (a
   transcript arriving at once). */
watch(
  [lastUserMessageId, () => props.activeThreadId, () => props.openingThreadId, () => props.messages.length],
  ([lastUser, active, opening, count], [previousUser, previousActive, previousOpening, previousCount]) => {
    if (count === 0) return
    const switched = active !== previousActive && !!previousActive
    const opened = !previousActive && !!active && previousCount === 0
    if (switched || opened || !!opening || (!!previousOpening && !opening)) return
    if (!lastUser || lastUser === previousUser || count - (previousCount ?? 0) > 2) return
    if (props.scrollMode === 'follow') scrollToBottom('auto')
    else followTurn(() => messageElement(lastUser), { topOffset: turnTopOffset })
  },
  { flush: 'post' },
)

/* A thread that was opened starts at its latest turn, at once. The scroller is
   shared between threads, so stick-to-bottom alone got this wrong both ways:
   leaving a thread scrolled up opened the next one at the same offset (its
   oldest messages, measured 30726px above the end of a 100-message thread),
   and leaving one at the bottom smooth-scrolled through the whole new
   transcript. The end of a loading state counts too, whatever ended it: the
   transcript shown afterwards (the opened one, or the previous one when the
   open failed or was cancelled) is mounted afresh under a scroller that the
   placeholder had shrunk to the top. A new conversation receiving its id is not
   an open (the id arrives with the first answer, which is already on screen). */
watch(
  [() => props.activeThreadId, () => props.openingThreadId],
  ([active, opening], [previousActive, previousOpening]) => {
    const loadingEnded = !!previousOpening && !opening
    const switched = !!previousActive && !!active && active !== previousActive
    if (loadingEnded || switched) {
      scrollToBottom('auto')
    }
  },
  { flush: 'post' },
)

/* Leaving the conversation for a page and coming back opens it where it was
   left, not at its latest turn: the transcript is drawn again from scratch
   (the page took its place), so the position is noted on the way out and
   handed to `useAutoScroll` (`openAt`) when the scroller reappears. Left at
   the bottom, it comes back following the bottom as before. Only for the
   conversation that was left: another one picked from the page opens at its
   latest turn. */
let conversationReturn: { threadId: string | undefined; top: number } | null = null
function takeConversationReturn(): number | null {
  const noted = conversationReturn
  conversationReturn = null
  return noted && noted.threadId === props.activeThreadId ? noted.top : null
}
watch(
  isChatView,
  (chat, wasChat) => {
    if (chat || !wasChat) return
    const el = threadScrollRef.value
    conversationReturn = el && !threadAtBottom.value ? { threadId: props.activeThreadId, top: el.scrollTop } : null
  },
  { flush: 'pre' },
)

/* Each page keeps its own scroll position (`useScrollMemory`): noted when it
   is left (before the swap, while it is still on screen), put back when it is
   shown again, and held while its content is still arriving. The product's
   pages share one scroller, kept mounted (see the template), the Recents page
   has its own. */
const pageViewRef = ref<HTMLElement | null>(null)
const recentsViewRef = ref<HTMLElement | null>(null)
const viewScroll = useScrollMemory()
/* A page that fills the view area and scrolls itself (`useChatViewFill`) takes away the area's own
   scrolling and its scrollbar's room while it is on screen; every other page keeps both. Counted,
   so a page leaving and the next arriving in either order ends right. */
const viewFillClaims = ref(0)
provideChatViewFill({
  claim() {
    viewFillClaims.value += 1
    let released = false
    return () => {
      if (released) return
      released = true
      viewFillClaims.value -= 1
    }
  },
})
const viewFilled = computed(() => viewFillClaims.value > 0)
function viewScroller(view: ChatAppView): HTMLElement | null {
  if (view === CHAT_VIEW_ID) return null
  return view === RECENTS_VIEW_ID && props.recents != null ? recentsViewRef.value : pageViewRef.value
}
watch(
  () => props.view,
  (_view, previous) => {
    const el = previous == null ? null : viewScroller(previous)
    if (el && previous != null) viewScroll.save(previous, el)
  },
  { flush: 'pre' },
)
watch(
  () => props.view,
  (view) => {
    const el = viewScroller(view)
    if (el) viewScroll.restore(view, el)
    else viewScroll.cancel()
  },
  { flush: 'post' },
)

/* What the message renderers need from the product: tool renderers, attachment URLs,
   file links. */
provideChatRenderContext(computed(() => ({
  toolRenderers: props.toolRenderers ?? {},
  resolveAttachmentUrl: props.resolveAttachmentUrl,
  fileHref: props.fileHref,
})))

/* Older messages: reaching the top of the thread asks for the page before. */
const OLDER_MESSAGES_THRESHOLD = 80
/* Something is scrolled up out of view: the top edge of the conversation fades
   under the top bar instead of cutting a line in half. Not at the top, where
   it would only dim the first message. */
const threadScrolled = ref(false)
function onThreadScroll(event: Event): void {
  const el = event.target as HTMLElement
  threadScrolled.value = el.scrollTop > 0
  if (el.scrollTop <= OLDER_MESSAGES_THRESHOLD && props.hasOlderMessages && !props.loadingOlderMessages) {
    emit('load-older-messages')
  }
}
watch(threadScrollRef, (el, previous) => {
  previous?.removeEventListener('scroll', onThreadScroll)
  el?.addEventListener('scroll', onThreadScroll, { passive: true })
  threadScrolled.value = (el?.scrollTop ?? 0) > 0
})
onBeforeUnmount(() => threadScrollRef.value?.removeEventListener('scroll', onThreadScroll))

/* Messages put before the ones on screen must not move what the user is reading: keep
   the distance from the bottom across the prepend. */
let prependAnchor: { height: number; top: number } | null = null
watch(
  () => props.messages,
  (next, previous) => {
    const el = threadScrollRef.value
    const first = previous?.[0]
    if (!el || !first || !next.length || next[0]?.id === first.id) return
    if (next.some((m) => m.id === first.id)) prependAnchor = { height: el.scrollHeight, top: el.scrollTop }
  },
  { flush: 'pre' },
)
watch(
  () => props.messages,
  () => {
    const el = threadScrollRef.value
    const anchor = prependAnchor
    prependAnchor = null
    if (el && anchor) el.scrollTop = anchor.top + (el.scrollHeight - anchor.height)
  },
  { flush: 'post' },
)


/* Timer handle so unmounting mid-confirmation does not leave a callback
   pointing at a destroyed component's state. */
let copyResetTimer: ReturnType<typeof setTimeout> | null = null
onBeforeUnmount(() => {
  if (copyResetTimer != null) clearTimeout(copyResetTimer)
})

async function onCopyMessage(message: ChatMessage): Promise<void> {
  emit('copy', message.id)
  try {
    if (typeof navigator !== 'undefined' && navigator.clipboard) {
      await navigator.clipboard.writeText(message.content)
    }
    copiedId.value = message.id
    if (copyResetTimer != null) clearTimeout(copyResetTimer)
    copyResetTimer = setTimeout(() => {
      copyResetTimer = null
      if (copiedId.value === message.id) copiedId.value = null
    }, 1500)
  } catch {
    // ignore - clipboard may be unavailable in sandboxed contexts
  }
}

function onArtifactViewChange(v: ArtifactView): void {
  emit('update:artifact-view', v)
}

function onArtifactWidthChange(w: number): void {
  emit('update:artifact-width', w)
}
</script>

<template>
  <div
    ref="rootRef"
    class="t-chat-app"
    :class="{ 't-chat-app--dark': isDark, 't-chat-app--narrow': effectiveIsMobile, 't-chat-app--pinned': pinnedToViewport, 't-chat-app--awaiting': awaitingAnswer }"
    :style="rootStyle"
  >
    <!-- ───────────────────────── Sidebar ───────────────────────── -->
    <TCollapsibleSidebar
      v-model="sidebarMode"
      :width="sidebarWidth"
      :rail-width="56"
      :is-mobile="effectiveIsMobile"
    >
      <!-- Brand header -->
      <template #header>
        <!-- The brand mark is the collapse control: the logo at rest, the
             panel icon on hover or keyboard focus, both drawn in one box so
             the swap moves nothing. The button is named by its action; the
             logo inside it is decorative. A full `brand` override receives the
             same action as slot props, so replacing the header keeps collapse. -->
        <slot
          name="brand"
          :toggle-sidebar="collapseFromHeader"
          :toggle-label="headerToggleLabel"
          :narrow="effectiveIsMobile"
        >
          <div class="t-chat-app__brand">
            <button
              type="button"
              class="t-chat-app__brand-toggle"
              :aria-label="headerToggleLabel"
              :title="headerToggleLabel"
              @click="collapseFromHeader"
            >
              <span class="t-chat-app__brand-mark" aria-hidden="true">
                <svg
                  v-if="brandLogo === 'monogram'"
                  class="t-chat-app__brand-mark-svg"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  stroke-width="2.5"
                  stroke-linecap="round"
                  stroke-linejoin="round"
                >
                  <path d="M5 6 L19 6" />
                  <path d="M12 6 L12 19" />
                  <circle cx="12" cy="19" r="1.4" fill="currentColor" stroke="none" />
                </svg>
                <Icon v-else :icon="brandLogo" />
              </span>
              <Icon icon="lucide:panel-left" class="t-chat-app__brand-toggle-icon" aria-hidden="true" />
            </button>
            <strong v-if="brandWordmark" class="t-chat-app__brand-name t-chat-app__brand-wordmark">
              <Icon :icon="brandWordmark" class="t-chat-app__brand-wordmark-glyph" />
              <span v-if="brandName" class="sr-only">{{ brandName }}</span>
            </strong>
            <strong v-else class="t-chat-app__brand-name">{{ brandName }}</strong>
            <div v-if="$slots['brand-actions']" class="t-chat-app__brand-actions">
              <slot name="brand-actions" :narrow="effectiveIsMobile" />
            </div>
          </div>
        </slot>
      </template>

      <!-- Primary nav, pinned above the scroll area. New chat and the product's
           destinations stay reachable however long the history gets. -->
      <template v-if="hasPinnedNav" #nav>
        <slot name="sidebar-nav" :nav="mainNav" :groups="pinnedNavGroups">
          <TSidebarNav
            :groups="pinnedNavGroups"
            @select="onNavSelect"
            @group-action="(groupId, actionId) => emit('nav-group-action', groupId, actionId)"
            @item-action="(itemId, actionId) => emit('nav-item-action', itemId, actionId)"
          />
        </slot>
      </template>

      <!-- Sidebar content (expanded mode): titled sections + threads list -->
      <template #content>
        <slot name="sidebar-sections" :groups="navGroups">
          <TSidebarNav
            v-if="navGroups.length > 0"
            :groups="navGroups"
            @select="onNavSelect"
            @group-action="(groupId, actionId) => emit('nav-group-action', groupId, actionId)"
            @item-action="(itemId, actionId) => emit('nav-item-action', itemId, actionId)"
          />
        </slot>

        <slot
          name="sidebar-content"
          :threads="threads"
          :active-thread-id="activeThreadId"
          :opening-thread-id="openingThreadId"
          :recents="recents"
        >
          <TThreadList
            v-model:collapsed="threadsCollapsed"
            :threads="threads"
            :active-thread-id="activeThreadId"
            :opening-thread-id="openingThreadId"
            :label="threadsLabel"
            :enable-delete="enableThreadDelete"
            :confirm-label="deleteConfirmLabel"
            :searchable="threadSearchable"
            :has-more="threadsHasMore"
            :loading-more="threadsLoadingMore"
            :preferences="recents?.preferences.value"
            :thread-icon="threadIcon"
            :show-view-all="recents != null"
            :enable-rename="recents != null"
            :enable-archive="recents != null"
            @select="openThread"
            @delete="(id) => emit('delete-thread', id)"
            @add="startNewChat"
            @search="(keyword) => emit('search-threads', keyword)"
            @load-more="emit('load-more-threads')"
            @update:preferences="(patch) => recents?.setPreferences(patch)"
            @view-all="goToView(RECENTS_VIEW_ID)"
            @rename="(id, title) => recents?.rename(id, title)"
            @archive="(id) => recents?.archive(id)"
            @unarchive="(id) => recents?.unarchive(id)"
          />
        </slot>
      </template>

      <!-- Sidebar footer -->
      <template #footer>
        <slot name="sidebar-footer">
          <slot name="sidebar-footer-above" />

          <TUserMenu
            v-if="showAccountBar"
            :name="accountName"
            :subtitle="accountSubtitle || accountEmail"
            :avatar-src="accountAvatar"
            :items="userMenuItems"
            :extra-items="userMenuExtraItems"
            :actions="userBarActions"
            :switchable="accountSwitchable"
            @select="onUserMenuSelect"
            @action="(id) => emit('user-action', id)"
            @switch-account="emit('switch-account')"
          >
            <template v-if="$slots['user-menu-header']" #menu-header>
              <slot name="user-menu-header" />
            </template>
            <template v-if="$slots['user-menu-footer']" #menu-footer>
              <slot name="user-menu-footer" />
            </template>
          </TUserMenu>

          <div v-else class="t-chat-app__foot-icons">
            <button
              v-if="enableSettings"
              type="button"
              class="t-chat-app__foot-btn"
              :aria-label="t.sidebar.settings"
              @click="openSettings()"
            >
              <Icon icon="lucide:settings-2" />
            </button>
            <button
              v-if="enableCommandPalette"
              type="button"
              class="t-chat-app__foot-btn"
              :aria-label="t.sidebar.commandPalette"
              @click="commandPaletteOpen = true"
            >
              <Icon icon="lucide:command" />
            </button>
          </div>
        </slot>
      </template>

      <!-- Collapsed rail -->
      <template #rail>
        <slot name="rail" :mode="sidebarMode" :main-nav="mainNav">
          <!-- New chat arrives through `groups` like every other entry; the
               rail has no special case for it either. -->
          <TChatRail
            :groups="navGroupsResolved"
            :brand-logo="brandLogo"
            :expand-label="t.sidebar.expand"
            @expand="sidebarMode = 'expanded'"
            @select="onNavSelect"
          >
            <!-- The recent conversations, beside the rail: collapsing the sidebar
                 must not put them out of reach. -->
            <template v-if="showRailThreads" #after-groups>
              <TRailFlyout
                class="t-chat-app__rail-threads"
                :label="railThreadsLabelText"
                :icon="railThreadsIcon"
              >
                <template #default="{ close }">
                  <slot
                    name="rail-threads"
                    :close="close"
                    :threads="threads"
                    :active-thread-id="activeThreadId"
                    :opening-thread-id="openingThreadId"
                    :select="(id: string) => { close(); openThread(id) }"
                  >
                    <TRailThreadList
                      :threads="threads"
                      :active-thread-id="activeThreadId"
                      :opening-thread-id="openingThreadId"
                      :show-view-all="recents != null"
                      :thread-icon="threadIcon"
                      @select="(id) => { close(); openThread(id) }"
                      @view-all="() => { close(); goToView(RECENTS_VIEW_ID) }"
                    />
                  </slot>
                </template>
              </TRailFlyout>
            </template>
            <template #bottom>
              <TRailButton
                v-if="enableSettings && !showAccountBar"
                class="t-chat-rail__btn"
                icon="lucide:settings-2"
                :label="t.sidebar.settings"
                @click="openSettings()"
              />
              <!-- With the account bar on, the avatar is the rail's account
                   affordance and carries settings inside its menu. -->
              <TUserMenu
                v-if="showAccountBar"
                compact
                :name="accountName"
                :subtitle="accountSubtitle || accountEmail"
                :avatar-src="accountAvatar"
                :items="userMenuItems"
                :extra-items="userMenuExtraItems"
                :switchable="accountSwitchable"
                @select="onUserMenuSelect"
                @switch-account="emit('switch-account')"
              />
            </template>
          </TChatRail>
        </slot>
      </template>
    </TCollapsibleSidebar>

    <!-- ───────────────────────── Main area ───────────────────────── -->
    <main
      class="t-chat-app__main"
      :class="{ 't-chat-app__main--aside-full-height': asideFullHeightLayout, 't-chat-app__main--landing': showsLanding }"
      :style="asideFullHeightLayout ? { '--t-chat-app-aside-width': asideShown ? `${aside.width.value}px` : '0px' } : undefined"
    >
      <header
        v-if="showTopbar"
        class="t-chat-app__topbar"
        :class="{ 't-chat-app__topbar--corner-free': asideFullHeightLayout && asideShown }"
      >
        <button
          v-if="sidebarMode === 'hidden'"
          type="button"
          class="t-chat-app__topbar-toggle"
          :aria-label="t.sidebar.show"
          @click="sidebarMode = 'expanded'"
        >
          <Icon icon="lucide:panel-left-open" />
        </button>

        <!-- Left cluster. `modelName` turns it into a model picker for the
             conversation view; otherwise it names the workspace or the page
             the `view` slot is showing. -->
        <slot
          name="topbar-left"
          :title="topbarTitle"
          :has-thread="hasMessages"
          :view="view"
        >
          <button
            v-if="modelName && isChatView"
            type="button"
            class="t-chat-app__model-btn"
            @click="emit('open-model-picker')"
          >
            <span>{{ modelName }}</span>
            <Icon icon="lucide:chevron-down" />
          </button>
          <slot v-else name="topbar-title" :title="topbarTitle" :has-thread="hasMessages">
            <TThreadTitle
              v-if="canManageThreadTitle"
              class="t-chat-app__thread-title"
              :title="editableThreadTitle"
              :archived="activeThreadArchived"
              :enable-delete="enableThreadDelete"
              @rename="renameActiveThread"
              @archive="setActiveThreadArchived(true)"
              @unarchive="setActiveThreadArchived(false)"
              @delete="activeThreadId && emit('delete-thread', activeThreadId)"
            />
            <!-- No element for an empty title: the spacer after it keeps the
                 bar's own controls where they are, and the bar its height. -->
            <span v-else-if="topbarTitle" class="t-chat-app__workspace-switcher">
              <span>{{ topbarTitle }}</span>
            </span>
          </slot>
        </slot>

        <slot name="topbar-center" :view="view" />

        <div class="t-chat-app__topbar-spacer" />

        <slot name="topbar-actions" :has-thread="hasMessages" :view="view" />

        <button
          v-if="asideAvailable && showAsideToggle"
          type="button"
          class="t-chat-app__topbar-toggle t-chat-app__aside-toggle"
          :class="{ 'is-active': aside.open.value }"
          :aria-label="aside.open.value ? t.chat.asideHide : t.chat.asideShow"
          :aria-expanded="aside.open.value"
          @click="aside.toggle()"
        >
          <Icon icon="lucide:panel-right" />
        </button>
      </header>

      <!-- Consumer-owned page. Sits inside the shell so the sidebar and top
           bar survive navigating away from the conversation. -->
      <section
        v-if="isRecentsView"
        ref="recentsViewRef"
        class="t-chat-app__view t-ai-scroll"
        :class="{ 't-chat-app__view--fill': viewFilled }"
      >
        <slot name="recents" :controller="recents">
          <TRecentsPage
            :controller="recents!"
            :title="threadsLabelText"
            :active-thread-id="activeThreadId"
            :include="recentsInclude"
            :thread-icon="threadIcon"
            :enable-delete="enableThreadDelete"
            :confirm-label="deleteConfirmLabel"
            @select="openThread"
            @delete="(id) => emit('delete-thread', id)"
          />
        </slot>
      </section>
      <!-- The product's pages. Mounted from the first page shown and then kept,
           hidden while the conversation or Recents is on screen, so a
           `<KeepAlive>` in the slot keeps its pages across a visit to a
           conversation (unmounting the section would take the KeepAlive and its
           cache with it). The slot is told the current `view` and whether the
           section is on screen (`active`). -->
      <section
        v-if="pageViewMounted"
        v-show="isPageView"
        ref="pageViewRef"
        class="t-chat-app__view t-ai-scroll"
        :class="{ 't-chat-app__view--fill': viewFilled }"
      >
        <slot name="view" :view="view" :active="isPageView" />
      </section>

      <!-- Conversation view: the landing page or the thread, the artifact pane and the panel -->
      <section
        v-if="isChatView"
        class="t-chat-app__workspace"
        :class="{ 't-chat-app__workspace--has-artifact': !!artifact && !showsLanding, 't-chat-app__workspace--has-aside': asideShown }"
      >
        <!-- Landing empty state, in the thread's place: the panel beside it is not
             remounted when the first message turns it into a conversation. -->
        <TLandingPage
          v-if="showsLanding"
          ref="landingRef"
          class="t-chat-app__landing"
          v-model="composerText"
          :greeting="landingGreeting"
          :subline="landingSubline"
          :chips="landingChips"
          :placeholder="landingPlaceholder"
          :disabled="isStreaming"
          :compact="effectiveIsMobile"
          :composer-actions="composerActions"
          :enable-voice="enableVoice"
          :enable-attachments="enableAttachments"
          :accept="composerAccept"
          :max-file-size="composerMaxFileSize"
          :voice-lang="voiceLang"
          :send-icon="sendIcon"
          :input-attrs="effectiveComposerInputAttrs"
          @submit="onLandingSubmit"
          @chip-click="onChipClick"
          @action="emit('composer-action', $event)"
          @attachments-rejected="emit('attachments-rejected', $event)"
          @input-keydown="onComposerKeydown"
        >
          <template v-if="$slots['composer-suggestions'] || slashCommands != null" #suggestions="context">
            <TSlashCommandMenu
              v-if="slashOpen"
              :id="slashMenuId"
              :items="slashMenu.results.value"
              :highlighted="slashMenu.highlighted.value"
              :state="slashCommandsState"
              :note="slashCommandsNote"
              @pick="slashMenu.pick"
              @highlight="(index) => (slashMenu.highlighted.value = index)"
            />
            <slot v-else name="composer-suggestions" v-bind="context" />
          </template>
          <template v-if="$slots['landing-plan']" #plan>
            <slot name="landing-plan" />
          </template>
          <template v-if="$slots['landing-headline']" #headline>
            <slot name="landing-headline" />
          </template>
          <template v-if="$slots['landing-subline']" #subline>
            <slot name="landing-subline" />
          </template>
          <template v-if="$slots['landing-chips']" #chips>
            <slot name="landing-chips" :chips="landingChips" />
          </template>
          <template v-if="composerNotice" #composer-notice>
            <TUsageLimitNotice
              variant="banner"
              :tone="composerNotice.tone"
              :window="composerNotice.window"
              :resets-at="composerNotice.resetsAt"
              :used-percent="composerNotice.usedPercent"
              :dismissible="composerNotice.dismissible"
              :draft-kept="composerNotice.draftKept"
              @dismiss="dismissComposerNotice"
              @expired="onComposerNoticeExpired"
            />
          </template>
          <template v-if="$slots['composer-left']" #composer-left>
            <slot name="composer-left" />
          </template>
          <template v-if="$slots['composer-right'] || showModelPicker" #composer-right>
            <TComposerModelPicker
              v-if="showModelPicker"
              :models="models"
              :model-value="model"
              :effort="effort"
              :disabled="isStreaming"
              @update:model-value="emit('update:model', $event)"
              @update:effort="emit('update:effort', $event)"
            />
            <slot name="composer-right" />
          </template>
          <template v-if="$slots['landing-footer']" #footer>
            <slot name="landing-footer" />
          </template>
        </TLandingPage>

        <div v-else ref="threadScrollRef" class="t-chat-app__thread t-ai-scroll">
          <div class="t-chat-app__thread-col">
          <!-- The top edge fades the way the bottom does under the composer. -->
          <div class="t-chat-app__top-fade" :class="{ 'is-visible': threadScrolled }" aria-hidden="true" />
          <TThreadLoading v-if="isOpeningThread" />
          <template v-else>
          <div v-if="loadingOlderMessages" class="t-chat-app__older" role="status">
            {{ t.chat.loadingOlder }}
          </div>
          <div v-for="(msg, index) in messages" :key="msg.id" class="t-chat-app__message" :data-message-id="msg.id">
            <!-- Two answers with no user message between them: the agent went on
                 by itself after a background task. A mark between them, so the
                 second does not read as the first shown twice. -->
            <div
              v-if="continuesAfterBackgroundTask(messages[index - 1], msg)"
              class="t-wave-divider t-chat-app__continued"
            >
              <span>{{ t.chat.continuedAfterBackgroundTask }}</span>
            </div>
            <slot name="message" :message="msg" :copied="copiedId === msg.id">
              <TThreadMessage
                :message="msg"
                :agent-name="agentName"
                :agent-label="agentLabel"
                :agent-icon="agentIcon"
                :agent-streaming-icon="agentStreamingIcon"
                :show-agent-byline="showAgentByline"
                :show-tool-calls="showToolCalls"
                :show-usage="showUsage"
                :copied="copiedId === msg.id"
                :enable-edit="enableEdit"
                :edit-disabled="isStreaming"
                @copy="onCopyMessage(msg)"
                @regenerate="emit('regenerate', $event)"
                @feedback="(id, type) => emit('feedback', id, type)"
                @edit="(id, content) => emit('edit', id, content)"
              >
                <template v-if="$slots['agent-mark']" #agent-mark="mark">
                  <slot name="agent-mark" v-bind="mark" />
                </template>
              </TThreadMessage>
            </slot>
          </div>
          <!-- The end of the conversation: what the app puts after the last message
               (a mark that the session ended), scrolling with the messages. -->
          <div v-if="$slots['thread-end']" class="t-chat-app__thread-end">
            <slot name="thread-end" :narrow="effectiveIsMobile" />
          </div>
          </template>

          <TThreadComposer
            ref="threadComposerRef"
            v-model="composerText"
            :placeholder="composerPlaceholderText"
            :disabled="(isStreaming && !effectiveQueueWhileRunning) || isOpeningThread"
            :running="isStreaming"
            :queue-while-running="effectiveQueueWhileRunning"
            :compact="effectiveIsMobile"
            :composer-actions="composerActions"
            :enable-voice="enableVoice"
            :enable-attachments="enableAttachments"
            :accept="composerAccept"
            :max-file-size="composerMaxFileSize"
            :voice-lang="voiceLang"
            :send-icon="sendIcon"
            :input-attrs="effectiveComposerInputAttrs"
            @send="onComposerSend"
            @stop="emit('stop')"
            @queue="onComposerQueue"
            @action="emit('composer-action', $event)"
            @attachments-rejected="emit('attachments-rejected', $event)"
            @input-keydown="onComposerKeydown"
          >
            <template v-if="$slots['composer-suggestions'] || slashCommands != null" #suggestions="context">
              <TSlashCommandMenu
                v-if="slashOpen"
                :id="slashMenuId"
                :items="slashMenu.results.value"
                :highlighted="slashMenu.highlighted.value"
                :state="slashCommandsState"
                :note="slashCommandsNote"
                @pick="slashMenu.pick"
                @highlight="(index) => (slashMenu.highlighted.value = index)"
              />
              <slot v-else name="composer-suggestions" v-bind="context" />
            </template>
            <template v-if="showScrollToLatest" #above>
              <button
                type="button"
                class="t-chat-app__to-latest"
                :aria-label="t.chat.scrollToLatest"
                @click="scrollToLatest"
              >
                <Icon icon="lucide:arrow-down" />
              </button>
            </template>
            <!-- Above the box, top to bottom: the app's own content, the queued messages, then the
                 cards the agent waits on. The cards sit nearest the box: they are what needs an answer
                 now, and on a phone they take the box's place. While a card waits the queue folds to its
                 title line (any width: the band scrolls at 40% of the screen, and the card must not
                 be pushed out of it); the user can open it again. -->
            <template v-if="$slots['thread-composer-top'] || approvalQueue || messageQueue" #top>
              <slot name="thread-composer-top" :narrow="effectiveIsMobile" />
              <TQueuedMessages v-if="messageQueue" :queue="messageQueue" :folded="cardsWaiting" />
              <TPendingApprovals v-if="approvalQueue" :queue="approvalQueue" :describe="describeApproval">
                <template v-if="$slots['approval-card']" #card="card">
                  <slot name="approval-card" v-bind="card" />
                </template>
              </TPendingApprovals>
            </template>
            <template v-if="composerNotice" #notice>
              <TUsageLimitNotice
                variant="banner"
                :tone="composerNotice.tone"
                :window="composerNotice.window"
                :resets-at="composerNotice.resetsAt"
                :used-percent="composerNotice.usedPercent"
                :dismissible="composerNotice.dismissible"
                :draft-kept="composerNotice.draftKept"
                @dismiss="dismissComposerNotice"
                @expired="onComposerNoticeExpired"
              />
            </template>
            <template v-if="$slots['thread-composer-left']" #left>
              <slot name="thread-composer-left" />
            </template>
            <template #right>
              <TComposerModelPicker
                v-if="showModelPicker"
                :models="models"
                :model-value="model"
                :effort="effort"
                :disabled="isStreaming"
                @update:model-value="emit('update:model', $event)"
                @update:effort="emit('update:effort', $event)"
              />
              <!-- Stop is the composer's primary button while `isStreaming`
                   (beside the queue button with `queueWhileRunning`). -->
              <slot name="thread-composer-right" />
            </template>
            <!-- Rides inside the composer's sticky wrapper, so the fine print
                 stays put instead of scrolling away with the messages. On a
                 narrow screen the `disclaimer` text is left out: it takes a
                 line of conversation on every screen of a phone and says
                 nothing new after the first. A product's own `thread-footer`
                 content may be functional, so it stays and gets `narrow`. -->
            <template v-if="(disclaimer && !effectiveIsMobile) || $slots['thread-footer']" #footer>
              <slot name="thread-footer" :narrow="effectiveIsMobile">{{ disclaimer }}</slot>
            </template>
          </TThreadComposer>
          </div>
        </div>

        <TArtifactPanel
          v-if="artifact && !showsLanding"
          :artifact="artifact"
          :files="artifactFiles"
          :view="artifactView"
          :width="artifactWidth"
          class="t-chat-app__artifact-slot"
          @update:view="onArtifactViewChange"
          @update:width="onArtifactWidthChange"
        />

        <TAsidePanel
          v-if="asideShown"
          class="t-chat-app__aside"
          :width="aside.width.value"
          :min-width="aside.minWidth"
          :max-width="aside.maxWidth"
          :narrow="effectiveIsMobile"
          :label="asideLabel"
          @update:width="aside.setWidth"
          @close="aside.hide()"
        >
          <slot
            name="aside"
            :tab="aside.tab.value"
            :set-tab="aside.setTab"
            :close="aside.hide"
            :narrow="effectiveIsMobile"
            :full-height="asideFullHeightLayout"
            :thread-id="activeThreadId"
          />
        </TAsidePanel>
      </section>
    </main>

    <!-- ───────────────────────── Settings dialog ───────────────────────── -->
    <TSettingsDialog
      v-if="enableSettings"
      v-model="settingsOpen"
      v-model:active-section="settingsSection"
      :narrow="effectiveIsMobile"
      :sections="effectiveSettingsSections"
      :title="settingsTitleText"
      :searchable="settingsSearchable"
      :before-close="settingsBeforeClose"
      :show-account="settingsShowAccount"
      :account-name="accountName"
      :account-subtitle="accountSubtitle || accountEmail"
      :account-avatar="accountAvatar"
      :switchable="accountSwitchable"
      @switch-account="emit('switch-account')"
    >
      <!-- Account. With a client this is the wired self-service page (profile
           + password against `/users/profile/*`); without one it falls back to
           the read-only card built from props, which is all a deployment
           without Tnzi.Identity can honestly show. -->
      <template #account>
        <slot name="settings-account">
          <TAccountSettings v-if="accountSettings" :controller="accountSettings" :deletion="accountDeletion ?? true" />
          <div v-else class="t-chat-app__account">
            <div class="t-chat-app__account-card">
              <TAvatar
                class="t-chat-app__account-avatar"
                :name="accountName"
                :size="52"
                color="var(--tnzi-ai-accent)"
                text-color="var(--tnzi-ai-on-accent)"
              />
              <div class="t-chat-app__account-meta">
                <div class="t-chat-app__account-name">{{ accountName || t.account.fallbackName }}</div>
                <div v-if="accountRole" class="t-chat-app__account-role">{{ accountRole }}</div>
              </div>
              <!-- Suppressed when the account bar is on: sign-out already sits
                   in its menu, one click from the sidebar, and two entry points
                   for the same destructive action is a UX bug rather than a
                   convenience. -->
              <button
                v-if="!showAccountBar"
                type="button"
                class="t-chat-app__account-signout"
                :aria-label="t.account.signOut"
                @click="emit('sign-out')"
              >
                <Icon icon="lucide:log-out" />
              </button>
            </div>
            <TSettingGroup v-if="accountEmail || accountRole" :separator="false">
              <TSettingRow
                v-if="accountEmail"
                :label="t.account.email"
                :description="t.account.emailHint"
              >
                <span class="t-chat-app__account-value">{{ accountEmail }}</span>
              </TSettingRow>
              <TSettingRow
                v-if="accountRole"
                :label="t.account.role"
                :description="t.account.roleHint"
              >
                <span class="t-chat-app__account-badge">{{ accountRole }}</span>
              </TSettingRow>
            </TSettingGroup>
          </div>
        </slot>
      </template>

      <!-- Appearance with built-in theme picker -->
      <template #appearance>
        <slot name="settings-appearance" :theme="theme" :pick-theme="pickTheme">
          <TSettingGroup :title="t.settings.appearance">
            <TSettingRow
              v-if="locales.length > 0"
              :label="t.settings.language"
              :description="t.settings.languageHint"
            >
              <select
                class="t-chat-app__select"
                :value="locale"
                @change="emit('update:locale', ($event.target as HTMLSelectElement).value)"
              >
                <option v-for="l in locales" :key="l.id" :value="l.id">{{ l.label }}</option>
              </select>
            </TSettingRow>

            <TSettingRow :label="t.settings.theme" :description="t.settings.themeHint" stacked>
              <div class="t-chat-app__theme-tiles">
                <button
                  v-for="opt in themeOptions"
                  :key="opt.id"
                  type="button"
                  class="t-chat-app__theme-tile"
                  :class="{ 'is-selected': theme === opt.id }"
                  @click="pickTheme(opt.id)"
                >
                  {{ opt.label }}
                </button>
              </div>
            </TSettingRow>
          </TSettingGroup>

          <!-- The deployment-wide pane. Rendered only when the consumer wired a
               `globalTheme` controller: without one there is nothing to publish
               to, and an inert "Publish to everyone" button is worse than no
               button. Whether the CONTROLS (vs a read-only note) show is the
               controller's `canManage`, and the backend's
               `system.appearance.update` is the actual wall. -->
          <TAppearanceAdmin v-if="globalTheme" :theme="globalTheme" :translate="translateSetting" />
        </slot>
      </template>

      <!-- Security: two-factor + active sessions. Wired, so it renders only
           with a client - see `accountClient`. -->
      <template #security>
        <slot name="settings-security">
          <TSecuritySettings v-if="accountSettings" :controller="accountSettings" />
        </slot>
      </template>

      <!-- Personalization: the user's AI profile, applied to every new
           conversation. -->
      <template #personalization>
        <slot name="settings-personalization">
          <TPersonalizationSettings v-if="personalization" :controller="personalization" />
        </slot>
      </template>

      <!-- Usage: the user's own token quota. Read-only - a user cannot raise
           their own limit. -->
      <template #usage>
        <slot name="settings-usage">
          <TUsageSettings v-if="usage" :controller="usage" />
        </slot>
      </template>

      <!-- About -->
      <template #about>
        <slot name="settings-about">
          <div class="t-chat-app__settings-group">
            <div class="t-chat-app__settings-label">{{ brandName }}</div>
            <div class="t-chat-app__settings-desc">
              {{ t.settings.poweredBy }}
            </div>
          </div>
        </slot>
      </template>

      <!-- Above the open section's name (a notice that concerns every section). -->
      <template v-if="$slots['settings-lead']" #lead>
        <slot name="settings-lead" :section="settingsSection" />
      </template>

      <!-- Pass-through for custom sections (any id not in the default set) -->
      <template
        v-for="section in customSettingsSections"
        :key="section.id"
        #[section.id]="slotProps"
      >
        <slot :name="`settings-${section.id}`" v-bind="slotProps" />
      </template>
    </TSettingsDialog>

    <!-- ───────────────────────── Command palette ───────────────────────── -->
    <TCommandPalette
      v-if="enableCommandPalette"
      v-model="commandPaletteOpen"
      :actions="commandActions"
    />

    <!-- The app's one confirmation dialog and its notes: what `useConfirm()`
         and `useToast()` show, from any page in the shell. -->
    <TConfirmHost />
    <TToastHost :narrow="effectiveIsMobile" />
  </div>
</template>

<style scoped>
/* ===========================================================================
 *  TChatApp - Manus-aligned visual layer.
 *
 *  CSS variables come from `packages/ui-ai/src/styles/index.css` (auto-loaded
 *  by the package entry); consumers can override any of them on `:root` or
 *  any ancestor of TChatApp.
 *
 *  BEM prefix: `.t-chat-app__*`. Variant classes use `--variantName`,
 *  state classes use `is-*`. No utility / atomic classes here.
 * ========================================================================= */

/* ─── Root layout ──────────────────────────────────────────────────────── */
.t-chat-app {
  display: flex;
  width: 100%;
  height: 100%;
  background: var(--tnzi-ai-bg);
  color: var(--tnzi-ai-text);
  font-family: var(--tnzi-ai-font-body);
  font-size: 14px;
  -webkit-font-smoothing: antialiased;
  -moz-osx-font-smoothing: grayscale;
}
/* Narrow screen (a phone): the layout changes, the sizes do not shrink. The
   desktop sizes are dense on purpose (32px rows, 13-14px text, for a mouse);
   on a phone the same values read as a scaled-down page and miss the thumb.
   These tokens are read by the sidebar rows, the top bar's and sidebar's icon
   buttons and the text fields (with the desktop value as each one's fallback),
   so only the narrow shell gets touch sizes: rows and icon buttons 44px, row
   text 16px, field text 16px (iOS zooms the page into a smaller field). */
.t-chat-app--narrow {
  --tnzi-ai-row-height: 44px;
  --tnzi-ai-row-font-size: 16px;
  --tnzi-ai-row-icon-size: 20px;
  --tnzi-ai-icon-button-size: 44px;
  --tnzi-ai-row-action-size: 36px;
  --tnzi-ai-field-font-size: 16px;
}
/* Pinned over the visual viewport (`usePinnedViewport` writes the variables;
   without them it is the full dynamic viewport). */
.t-chat-app--pinned {
  position: fixed;
  left: 0;
  right: 0;
  top: var(--tnzi-ai-viewport-top, 0px);
  height: var(--tnzi-ai-viewport-height, 100dvh);
  overscroll-behavior: none;
}

/* ─── Brand header ─────────────────────────────────────────────────────── */
.t-chat-app__brand {
  display: flex;
  align-items: center;
  gap: 8px;
  height: 56px;
  padding: 12px 10px;
  box-sizing: border-box;
}
.t-chat-app__brand-mark {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  border-radius: 10px;
  background: var(--tnzi-ai-brand-mark-bg);
  color: var(--tnzi-ai-brand-mark-fg);
  flex-shrink: 0;
}
/* The monogram and an Iconify `brandLogo` alike: both are the tile's svg. */
.t-chat-app__brand-mark > svg {
  width: var(--tnzi-ai-brand-mark-glyph-size, 18px);
  height: var(--tnzi-ai-brand-mark-glyph-size, 18px);
}
.t-chat-app__brand-name {
  flex: 1;
  min-width: 0;
  font-family: var(--tnzi-ai-font-display);
  font-size: 17px;
  font-weight: 500;
  letter-spacing: -0.01em;
  margin-left: 2px;
  color: var(--tnzi-ai-text);
}
/* The wordmark keeps the line the name's text occupied (`1lh`) and sits in the
   middle of it, so the mark beside it and everything below stay put. Iconify
   draws it 1em high (the name's font size) and as wide as its viewBox; a narrow
   sidebar shrinks it rather than pushing the header actions out of the row. */
.t-chat-app__brand-wordmark {
  display: flex;
  align-items: center;
  min-height: 1lh;
}
.t-chat-app__brand-wordmark-glyph {
  display: block;
  max-width: 100%;
}
/* The logo tile is the collapse button. At rest it shows the mark; on hover
   or keyboard focus the panel icon takes its place inside the same box, so the
   swap shifts nothing. Its box reads the icon-button token (touch size on a
   narrow screen) and the 32px tile sits centred in it. */
.t-chat-app__brand-toggle {
  position: relative;
  width: var(--tnzi-ai-icon-button-size, 32px);
  height: var(--tnzi-ai-icon-button-size, 32px);
  padding: 0;
  border: none;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  border-radius: 10px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  transition: background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
              color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-chat-app__brand-toggle .t-chat-app__brand-mark {
  transition: opacity var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-chat-app__brand-toggle-icon {
  position: absolute;
  inset: 0;
  margin: auto;
  width: 18px;
  height: 18px;
  opacity: 0;
  pointer-events: none;
  transition: opacity var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-chat-app__brand-toggle:focus-visible {
  background: var(--tnzi-ai-sidebar-hover);
  color: var(--tnzi-ai-text);
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: 1px;
}
.t-chat-app__brand-toggle:focus-visible .t-chat-app__brand-mark {
  opacity: 0;
}
.t-chat-app__brand-toggle:focus-visible .t-chat-app__brand-toggle-icon {
  opacity: 1;
}
/* Hover swaps only where the primary pointer can hover. A touch browser keeps
   `:hover` on the last tapped spot, and the top bar's open button sits right
   where this toggle lands, so an unguarded rule opened the drawer showing the
   panel icon instead of the logo. On touch the logo stays the logo; tapping it
   still closes the drawer. */
@media (hover: hover) {
  .t-chat-app__brand-toggle:hover {
    background: var(--tnzi-ai-sidebar-hover);
    color: var(--tnzi-ai-text);
  }
  .t-chat-app__brand-toggle:hover .t-chat-app__brand-mark {
    opacity: 0;
  }
  .t-chat-app__brand-toggle:hover .t-chat-app__brand-toggle-icon {
    opacity: 1;
  }
}
@media (prefers-reduced-motion: reduce) {
  .t-chat-app__brand-toggle,
  .t-chat-app__brand-toggle .t-chat-app__brand-mark,
  .t-chat-app__brand-toggle-icon {
    transition: none;
  }
}
/* The product's own controls at the header's right end (a mode switch, for
   one). The name before it takes the free width, so this sits flush right. */
.t-chat-app__brand-actions {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-shrink: 0;
  margin-left: auto;
}

/* The nav owns its own row geometry (`.t-sidebar-nav__item` in TSidebarNav).
   This file used to carry a second copy of it for the new-chat button, plus a
   `.t-chat-app__nav` container that nothing had rendered since the nav moved
   into TSidebarNav. Both are gone - there is one row style now. */

/* ─── Sidebar footer ───────────────────────────────────────────────────── */
/* The sidebar footer is a padded stack now, so this row only needs its own
   gap - its inset and its separation from the list above come from there. */
.t-chat-app__foot-icons {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 2px 4px;
}
.t-chat-app__foot-btn {
  width: 28px;
  height: 28px;
  border: none;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  border-radius: 6px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 16px;
  transition: background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
              color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-chat-app__foot-btn:hover {
  background: var(--tnzi-ai-sidebar-hover);
  color: var(--tnzi-ai-text);
}

/* ─── Main pane + topbar ───────────────────────────────────────────────── */
.t-chat-app__main {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
}
/* The window's own controls (a frameless desktop window's minimise / maximise
   / close) may sit over the top right corner: the bar keeps clear of
   `--tnzi-ai-window-inset-right`. When a full-height panel is open the corner
   is the panel's, and the bar keeps its plain gutter. */
.t-chat-app__topbar {
  display: flex;
  align-items: center;
  gap: 8px;
  height: 52px;
  padding: 0 calc(16px + var(--tnzi-ai-window-inset-right, 0px)) 0 16px;
  flex-shrink: 0;
  background: transparent;
}
.t-chat-app__topbar--corner-free {
  padding-right: 16px;
}
.t-chat-app__topbar-toggle {
  flex-shrink: 0;
  width: var(--tnzi-ai-icon-button-size, 32px);
  height: var(--tnzi-ai-icon-button-size, 32px);
  border: none;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  border-radius: 6px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 18px;
  transition: background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
              color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-chat-app__topbar-toggle:hover {
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
}
.t-chat-app__topbar-spacer { flex: 1; }

/* Model picker in the top bar's left cluster. Reads as a label until hovered,
   matching the workspace switcher beside it. */
.t-chat-app__model-btn {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  height: 32px;
  padding: 0 10px;
  border: none;
  background: transparent;
  border-radius: 8px;
  color: var(--tnzi-ai-text);
  font-family: inherit;
  font-size: 15px;
  font-weight: 500;
  cursor: pointer;
  transition: background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-chat-app__model-btn:hover { background: var(--tnzi-ai-hover); }
.t-chat-app__model-btn .iconify {
  font-size: 15px;
  color: var(--tnzi-ai-text-secondary);
}

/* Consumer-owned page area. Scrolls on its own so a long page does not push
   the shell around, and imposes no padding: the page owns its own layout.
   The scrollbar's room is kept whether or not the page overflows, so moving
   between a long page and a short one does not slide the content and its
   toolbar sideways by the bar's width. Where scrollbars overlay the content
   (iOS, macOS by default) the gutter is zero and nothing is reserved. */
.t-chat-app__view {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  scrollbar-gutter: stable;
  position: relative;
}
/* A page that fills the area and scrolls inside itself (`useChatViewFill`): the area never
   scrolls, so it keeps no room for a bar that would never show. `hidden` rather than `clip`, so
   the scroll memory can still set the area's position while the class comes and goes. */
.t-chat-app__view--fill {
  overflow: hidden;
  scrollbar-gutter: auto;
}

/* The editable title owns the top bar's free room and nothing else: basis 0,
   so it never takes room from the controls beside it (an auto basis let a long
   title shrink them along with itself, measured at 390px: 32px action buttons
   squeezed to 18px), and a grow weight that dwarfs the spacer's. Up to a cap
   (520px, at most 55% of the bar): past it the title ellipsises, so however
   long it still reads as a title and the rest of the bar stays empty. Inside,
   the title (or its rename field) keeps the width of its text. Scoped under
   the bar so the cap wins over the title component's own `max-width: 100%`. */
.t-chat-app__topbar .t-chat-app__thread-title {
  flex: 1000 1 0%;
  min-width: 0;
  max-width: min(520px, 55%);
}
/* A plain title (a page of the product's) is as wide as its text, to the same cap. */
.t-chat-app__topbar .t-chat-app__workspace-switcher {
  flex: 0 1 auto;
  min-width: 0;
  max-width: min(520px, 55%);
}
.t-chat-app__workspace-switcher > span {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-chat-app__workspace-switcher {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px 10px;
  border-radius: 8px;
  font-size: 15px;
  font-weight: 500;
  color: var(--tnzi-ai-text);
  cursor: default;
}

/* ─── Workspace + thread ──────────────────────────────────────────────── */
.t-chat-app__workspace {
  flex: 1;
  display: flex;
  min-height: 0;
  overflow: hidden;
}
/* Two layers on purpose. The scroller spans the full pane so its scrollbar
   sits against the window edge; the reading column is centred *inside* it.
   Collapsing the two - a `max-width` scroller with `margin: 0 auto` - parks
   the scrollbar at the column's right edge, floating in the middle of the
   pane. */
.t-chat-app__thread {
  flex: 1;
  min-width: 0;
  width: 100%;
  overflow-y: auto;
  /* Room for the scrollbar on both edges, whether or not the conversation
     overflows yet: the centred column (and the composer in it) does not move
     when a growing reply first brings the bar in, and stays centred on the
     same axis as the landing page it replaces. Zero where scrollbars overlay
     the content. */
  scrollbar-gutter: stable both-edges;
  /* Reaching either end of the conversation does not hand the drag on to the
     page (on iOS that moved the whole shell, composer included). */
  overscroll-behavior: contain;
  /* A containing block for what it scrolls: an absolutely positioned element in
     the conversation (a visually hidden label, a tooltip without a positioned
     parent) is placed and clipped here. Without it such an element belongs to
     the page and, laid out deep in the scrolled content, grew the document: the
     page got its own scrollbar and the shell slid up when it was scrolled. */
  position: relative;
}
/* The mark before an answer the agent went on with by itself (a wavy line with
   words, `.t-wave-divider`): 14px of air on each side, inside the message's
   own box so the column's gap above it stays as it is. */
.t-chat-app__continued {
  margin: 14px 0;
}
/* A wavy mark the app ends the conversation with sits as far from the last
   message as the one between two answers. */
.t-chat-app__thread-end > :slotted(.t-wave-divider) {
  margin: 14px 0;
}
.t-chat-app__older {
  padding: 4px 0 12px;
  text-align: center;
  font-size: 12px;
  color: var(--tnzi-ai-text-tertiary);
}
.t-chat-app__thread-col {
  /* No bottom padding: the sticky composer supplies it. `bottom: 0` pins the
     composer to the scroller's padding box, so any padding-bottom here is a
     strip the composer cannot cover and messages scroll through. */
  --t-chat-app-col-pad-top: 24px;
  padding: var(--t-chat-app-col-pad-top) 24px 0;
  display: flex;
  flex-direction: column;
  --t-chat-app-col-gap: 24px;
  gap: var(--t-chat-app-col-gap);
  max-width: var(--tnzi-ai-content-width, 820px);
  width: 100%;
  margin: 0 auto;
  /* Lets the composer's `margin-top: auto` push it to the bottom even when the
     thread is nearly empty. */
  min-height: 100%;
  box-sizing: border-box;
}
/* When the artifact pane is visible, narrow the thread column and keep it
 * centered between the sidebar and the artifact (matches Manus behaviour). */
.t-chat-app__aside-toggle.is-active {
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
}
.t-chat-app__workspace--has-artifact .t-chat-app__thread-col {
  max-width: 620px;
}
/* Beside the panel the conversation keeps a readable width (480px, or 60% of
   the area when that is less) and the panel gives way first. */
.t-chat-app__workspace--has-aside .t-chat-app__thread,
.t-chat-app__workspace--has-aside .t-chat-app__landing {
  min-width: min(480px, 60%);
}
.t-chat-app__workspace--has-aside .t-chat-app__aside {
  flex-shrink: 1;
}
/* Narrow screen (below `sidebarMobileBreakpoint`): the desktop gutters around
   the conversation and in the top bar are decorative there and cost a phone
   width and lines. Nothing interactive changes size. */
.t-chat-app--narrow .t-chat-app__topbar {
  height: 48px;
  padding: 0 calc(8px + var(--tnzi-ai-window-inset-right, 0px)) 0 8px;
}
/* A card waits on a phone: the composer keeps only its bar, so the cards are not squeezed off the screen. */
.t-chat-app--awaiting :deep(.t-thread-composer__input),
.t-chat-app--awaiting :deep(.t-thread-composer__files) {
  display: none;
}
.t-chat-app--narrow .t-chat-app__thread-col {
  --t-chat-app-col-pad-top: 12px;
  padding: var(--t-chat-app-col-pad-top) 12px 0;
  --t-chat-app-col-gap: 16px;
}
/* The fade at the top edge of the conversation: pinned to the top of the
   scroller (sticky), drawn over what scrolls under it, and taking no room (the
   margins cancel the column's top padding and the gap after it). The pointer
   passes through to the message underneath. Mirrors the composer's fade at the
   bottom (canvas colour to transparent over 24px). */
.t-chat-app__top-fade {
  --t-chat-app-fade: 24px;
  position: sticky;
  top: 0;
  z-index: 2;
  flex-shrink: 0;
  height: var(--t-chat-app-fade);
  margin-top: calc(-1 * var(--t-chat-app-col-pad-top));
  margin-bottom: calc(-1 * (var(--t-chat-app-fade) + var(--t-chat-app-col-gap)) + var(--t-chat-app-col-pad-top));
  background: linear-gradient(to bottom, var(--tnzi-ai-bg) 0%, transparent 100%);
  pointer-events: none;
  opacity: 0;
  transition: opacity var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-chat-app__top-fade.is-visible { opacity: 1; }
/* "Scroll to the latest": floats above the composer while the view is not at
   the end of the conversation. */
.t-chat-app__to-latest {
  width: 36px;
  height: 36px;
  margin-bottom: 12px;
  border: 1px solid var(--tnzi-ai-border-strong);
  border-radius: 999px;
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text-secondary);
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.08);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 18px;
  cursor: pointer;
  transition: background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
    color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-chat-app__to-latest:hover {
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
}
.t-chat-app--narrow .t-chat-app__to-latest {
  width: 40px;
  height: 40px;
}

/* The panel's content reads the window corner it has to keep clear of; only a
   full-height panel reaches the top of the window (below). */
.t-chat-app__aside {
  --tnzi-ai-aside-inset-top: 0px;
  --tnzi-ai-aside-inset-right: 0px;
}

/* ─── Full-height aside (`asideFullHeight`) ────────────────────────────────
   Three columns: the conversation, the artifact pane (empty when there is
   none) and the panel. The top bar spans the first two; the panel spans both
   rows. The workspace's own box steps aside (`display: contents`) so its
   children are the grid's, without moving the panel in the DOM: its content
   is not remounted when the screen crosses the narrow breakpoint. The panel's
   column is its width (0 while closed), capped at 70% of the area as the
   panel caps itself in the workspace: the cap has to be the column's, since a
   fixed-width item sizes an auto or fit-content column to its full width and a
   percentage on the item would resolve against that column. The conversation
   keeps a readable width first (480px, or 60% of the area when that is less):
   the panel's column gives way to it. Beside the landing page the panel is
   capped at 30% (`--tnzi-ai-aside-landing-max-share`): what a start page shows
   beside itself (a project's files) is a list, and the page is the point. The
   area clips what the workspace clipped (the artifact pane's bottom margin
   past its full height). */
.t-chat-app__main--aside-full-height {
  display: grid;
  grid-template-columns:
    minmax(min(480px, 60%), 1fr)
    auto
    minmax(0, min(var(--t-chat-app-aside-width, 0px), var(--tnzi-ai-aside-max-share, 70%)));
  grid-template-rows: auto minmax(0, 1fr);
  overflow: hidden;
}
.t-chat-app__main--aside-full-height.t-chat-app__main--landing {
  grid-template-columns:
    minmax(min(480px, 60%), 1fr)
    auto
    minmax(0, min(var(--t-chat-app-aside-width, 0px), var(--tnzi-ai-aside-landing-max-share, 30%)));
}
.t-chat-app__main--aside-full-height > .t-chat-app__topbar {
  grid-column: 1 / 3;
  grid-row: 1;
  min-width: 0;
}
.t-chat-app__main--aside-full-height > .t-chat-app__workspace {
  display: contents;
}
.t-chat-app__main--aside-full-height .t-chat-app__thread,
.t-chat-app__main--aside-full-height .t-chat-app__landing {
  grid-column: 1;
  grid-row: 2;
  min-height: 0;
}
.t-chat-app__main--aside-full-height .t-chat-app__artifact-slot {
  grid-column: 2;
  grid-row: 2;
  min-height: 0;
}
.t-chat-app__main--aside-full-height .t-chat-app__aside {
  grid-column: 3;
  grid-row: 1 / 3;
  min-height: 0;
  max-width: 100%;
  /* The panel's top right corner is the window's: what the panel draws there
     (its own head, a row of view icons) keeps clear of the window's controls
     by reading these two (0 when the panel is not full height). */
  --tnzi-ai-aside-inset-top: var(--tnzi-ai-window-inset-top, 0px);
  --tnzi-ai-aside-inset-right: var(--tnzi-ai-window-inset-right, 0px);
}

/* ─── Artifact panel slot ──────────────────────────────────────────────── */
.t-chat-app__artifact-slot {
  flex-shrink: 0;
  margin: 12px 12px 12px 0;
}

/* ─── Settings: built-in Account profile card ──────────────────────────── */
.t-chat-app__account {
  display: flex;
  flex-direction: column;
  gap: 20px;
}
.t-chat-app__account-card {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 16px;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 14px;
  background: var(--tnzi-ai-surface);
}
.t-chat-app__account-meta {
  flex: 1;
  min-width: 0;
}
.t-chat-app__account-name {
  font-size: 16px;
  font-weight: 600;
  color: var(--tnzi-ai-text);
}
.t-chat-app__account-role {
  font-size: 13px;
  color: var(--tnzi-ai-text-tertiary);
  margin-top: 2px;
}
.t-chat-app__account-signout {
  width: 36px;
  height: 36px;
  flex-shrink: 0;
  border: none;
  background: transparent;
  color: var(--tnzi-ai-text-tertiary);
  border-radius: 10px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 18px;
  transition: background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
    color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-chat-app__account-signout:hover {
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
}
.t-chat-app__account-value {
  font-size: 14px;
  color: var(--tnzi-ai-text-secondary);
}
.t-chat-app__account-badge {
  padding: 4px 10px;
  border-radius: 999px;
  font-size: 12px;
  font-weight: 500;
  background: var(--tnzi-ai-accent-soft);
  color: var(--tnzi-ai-accent-text);
}

/* ─── Settings dialog content ──────────────────────────────────────────── */
.t-chat-app__settings-empty {
  font-size: 13px;
  color: var(--tnzi-ai-text-tertiary);
}
.t-chat-app__settings-group {
  margin-bottom: 24px;
}
.t-chat-app__settings-label {
  font-size: 11px;
  font-weight: 500;
  letter-spacing: 0.04em;
  color: var(--tnzi-ai-text-tertiary);
  margin-bottom: 14px;
}
.t-chat-app__settings-desc {
  font-size: 13px;
  line-height: 1.5;
  color: var(--tnzi-ai-text-secondary);
}
.t-chat-app__select {
  height: 34px;
  min-width: 160px;
  padding: 0 10px;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 8px;
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text);
  font-family: inherit;
  font-size: 13px;
  cursor: pointer;
}
.t-chat-app__theme-tiles {
  display: flex;
  gap: 14px;
  flex-wrap: wrap;
}
.t-chat-app__theme-tile {
  flex: 1;
  min-width: 100px;
  height: 36px;
  border: 1px solid var(--tnzi-ai-border);
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text);
  border-radius: 10px;
  cursor: pointer;
  font-size: 13px;
  font-family: inherit;
  transition: background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
              border-color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-chat-app__theme-tile:hover { background: var(--tnzi-ai-hover); }
.t-chat-app__theme-tile.is-selected {
  border-color: var(--tnzi-ai-accent);
  background: var(--tnzi-ai-accent-soft);
  font-weight: 500;
  color: var(--tnzi-ai-text);
}

</style>
