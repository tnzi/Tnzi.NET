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
 *   - Brand lockup with T-monogram (override via `brand` slot or brandName/brandLogo)
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
import { computed, ref, watch, onMounted, onBeforeUnmount, useSlots } from 'vue'
import { Icon } from '@iconify/vue'
import { NConfigProvider } from 'naive-ui'
import { TAvatar, applyThemeModeToDocument } from '@tnzi/ui'
import { useAiNaiveTheme } from '../../headless/useAiNaiveTheme'
import type { ThemeMode } from '@tnzi/core/types'
import { normalizeThemeMode } from '@tnzi/core/types'
import TCollapsibleSidebar from '../layout/TCollapsibleSidebar.vue'
import TCommandPalette from '../overlay/TCommandPalette.vue'
import TSettingsDialog from '../overlay/TSettingsDialog.vue'
import TLandingPage from './TLandingPage.vue'
import TSidebarNav from '../layout/TSidebarNav.vue'
import TChatRail from '../layout/TChatRail.vue'
import TThreadList from './TThreadList.vue'
import type { ThreadItem } from './TThreadList.vue'
import type { NavItem, NavGroup } from '../layout/TSidebarNav.vue'
import TUserMenu from '../overlay/TUserMenu.vue'
import TSettingRow from '../layout/TSettingRow.vue'
import TSettingGroup from '../layout/TSettingGroup.vue'
import TAppearanceAdmin from '../layout/TAppearanceAdmin.vue'
import type { UseGlobalAiThemeReturn } from '../../headless/useGlobalAiTheme'
import type { HttpClient } from '@tnzi/core/http'
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
import { useAutoScroll } from '../../headless/useAutoScroll'
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
import type { ChatMessage } from '../../headless/useChat'

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

/* Re-exported so consumers import every TChatApp-facing type from one module
   rather than tracking which shell component happens to own each one. */
export type { LandingChip, NavItem, NavGroup, UserMenuItem, UserBarAction, ThreadItem }

// ---------------------------------------------------------------------------
// Props / events / slots
// ---------------------------------------------------------------------------

const props = withDefaults(
  defineProps<{
    // ── Brand ────────────────────────────────────────────────────────────
    /** Brand wordmark shown in the sidebar header. */
    brandName?: string
    /** Brand logo: 'monogram' renders the built-in T-mark; any other
     *  string is treated as an iconify name. Use the `brand` slot for
     *  fully custom markup. */
    brandLogo?: 'monogram' | string

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
    /** Section heading above the threads list. */
    threadsLabel?: string
    /** Label for the new-chat entry. Defaults to the current locale's. */
    newChatLabel?: string
    /** Iconify name for the new-chat entry. A product that calls this
     *  something else usually wants its own glyph too. */
    newChatIcon?: string
    /**
     * Contribute the built-in new-chat entry at the top of the nav. Turn it off
     * when the product already carries "New chat" among its own nav entries -
     * otherwise it appears twice, in both the expanded sidebar and the rail.
     */
    showNewChatButton?: boolean
    /** Hover delete affordance on each thread row (with inline confirm). Default true. */
    enableThreadDelete?: boolean
    /** Inline delete-confirm prompt label. */
    deleteConfirmLabel?: string

    // ── Threads ─────────────────────────────────────────────────────────
    threads?: ReadonlyArray<ThreadItem>
    activeThreadId?: string

    // ── Chat thread ─────────────────────────────────────────────────────
    messages?: ReadonlyArray<ChatMessage>
    isStreaming?: boolean
    /** Composer text - v-model'able. */
    inputText?: string
    composerPlaceholder?: string
    /** Extra composer toolbar buttons (declarative - "more buttons"). */
    composerActions?: ReadonlyArray<ComposerAction>
    /** Built-in voice (speech-to-text) mic button. Default true. */
    enableVoice?: boolean
    /** Built-in attachment button (paperclip + drag/paste). Default false. */
    enableAttachments?: boolean
    /** Accepted attachment file types. */
    composerAccept?: string
    /** Voice recognition language (BCP-47). */
    voiceLang?: string
    /** Agent display name. */
    agentName?: string
    /** Small label rendered after the agent name (e.g. "Pro", "Lite"). */
    agentLabel?: string

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
    /** Max width of the conversation content column (CSS length, e.g. "1100px"). */
    contentWidth?: string | number
    /** Fine-print line under the composer (model disclaimer, usage note). */
    disclaimer?: string

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
    /** Icon buttons on the account bar itself (notifications, feedback). */
    userBarActions?: ReadonlyArray<UserBarAction>
    /** Secondary line in the account menu header (email, workspace, plan). */
    accountSubtitle?: string
    accountAvatar?: string | null
    /** Show the account-switcher affordance. */
    accountSwitchable?: boolean

    // ── Landing empty state ─────────────────────────────────────────────
    /** Show the landing empty state when there are no messages. */
    showLanding?: boolean
    landingGreeting?: string
    /** Optional subtitle rendered below the landing greeting. */
    landingSubline?: string
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
     * Personalization). They call the framework's own **user-facing** routes -
     * `/users/profile/*` in `Tnzi.Identity` and `/user-profile` in `Tnzi.AI` -
     * so the client is all a consumer supplies; there is no data to pass.
     *
     * Omit it on a deployment without those modules and the pages hide
     * themselves rather than rendering a form that cannot save.
     */
    accountClient?: HttpClient | null
    /**
     * `(key, fallback?) => string` for the appearance pane's copy. The rest of
     * the dialog reads the `locales` prop; this pane takes an injected
     * translator because it is also usable outside `TChatApp`.
     */
    translateSetting?: (key: string, fallback?: string) => string
    settingsSections?: ReadonlyArray<SettingsSection>
    settingsTitle?: string
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
    /** When true (default), TChatApp toggles the `dark` class on the document
     *  element on mount and whenever the resolved theme changes, which is what
     *  switches the `--tnzi-ai-*` palette. Set false if your app owns the
     *  document theme class. */
    autoApplyTheme?: boolean

    // ── Artifact ────────────────────────────────────────────────────────
    artifact?: ArtifactPanelItem | null
    artifactFiles?: ReadonlyArray<ArtifactFile>
    artifactView?: ArtifactView
    artifactWidth?: number

    // ── Responsive ──────────────────────────────────────────────────────
    /**
     * Whether to use the mobile drawer presentation (overlay + backdrop +
     * page scroll lock). Leave unset to follow `sidebarMobileBreakpoint`,
     * which is the same signal that auto-collapses the sidebar. Pass a value
     * only to put the breakpoint under the host app's own responsive system.
     */
    isMobile?: boolean
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
    threadsLabel: 'All tasks',
    /* No default for `newChatLabel`: `newChatText` falls back to the
       dictionary, so leaving it unset translates instead of pinning the entry
       to English. */
    newChatIcon: 'lucide:square-pen',
    showNewChatButton: true,
    enableThreadDelete: true,
    deleteConfirmLabel: 'Delete?',
    threads: () => [],
    messages: () => [],
    isStreaming: false,
    inputText: '',
    composerPlaceholder: 'Type a message…',
    composerActions: () => [],
    enableVoice: true,
    enableAttachments: false,
    composerAccept: DEFAULT_COMPOSER_ACCEPT,
    voiceLang: 'en-US',
    agentName: 'Assistant',
    locales: () => [],
    locale: '',
    showLanding: true,
    landingGreeting: 'What can I do for you?',
    landingPlaceholder: 'Assign a task or ask anything',
    landingChips: () => [],
    enableSettings: true,
    globalTheme: null,
    translateSetting: undefined,
    /* Security and Personalization are listed but render nothing without an
       `accountClient`; a consumer on a different backend drops them from this
       array. Listing them here is what makes them built-in rather than
       something every product re-declares. */
    settingsSections: () => [
      { id: 'account', label: 'Account', icon: 'lucide:circle-user-round' },
      { id: 'security', label: 'Security', icon: 'lucide:shield-check' },
      { id: 'personalization', label: 'Personalization', icon: 'lucide:sparkles' },
      { id: 'appearance', label: 'Appearance', icon: 'lucide:settings' },
      { id: 'about', label: 'About', icon: 'lucide:info' },
    ],
    settingsTitle: 'Settings',
    settingsSearchable: false,
    settingsShowAccount: false,
    enableCommandPalette: false,
    commandActions: () => [],
    theme: 'light',
    autoApplyTheme: true,
    artifact: null,
    artifactFiles: () => [],
    artifactView: 'code',
    artifactWidth: 520,
    isMobile: undefined,
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
   * `v-model:view`. Emitted alongside `nav` (the entry's id) and alongside
   * `new-chat` / `select-thread` (back to the conversation). Ignore it to keep
   * routing by hand.
   */
  'update:view': [view: ChatAppView]

  // Account bar
  /** A user-menu item was activated. Built-in ids: account / settings / sign-out. */
  'user-menu': [id: string]
  /** An account-bar icon button was clicked. */
  'user-action': [id: string]
  'switch-account': []

  // Top bar
  /** The model name in the top bar was clicked. */
  'open-model-picker': []

  // Composer
  send: [content: string, files: File[]]
  'composer-action': [id: string]
  stop: []
  'update:input-text': [value: string]

  // Landing
  'select-suggestion': [chip: LandingChip]

  // Message actions
  copy: [messageId: string]
  regenerate: [messageId: string]
  edit: [messageId: string]
  feedback: [messageId: string, type: 'positive' | 'negative', reason?: string]

  // Settings + theme
  'update:theme': [theme: ThemePref]
  'update:locale': [locale: string]
  'open-settings': []
  'sign-out': []

  // Artifact
  'close-artifact': []
  'update:artifact-view': [view: ArtifactView]
  'update:artifact-width': [width: number]
}>()

// ---------------------------------------------------------------------------
// Internal state
// ---------------------------------------------------------------------------

/* Sidebar state comes from useSidebarState so TChatApp inherits its two
   behaviours for free: the mode is remembered in localStorage across reloads,
   and dropping below `sidebarMobileBreakpoint` collapses the sidebar and
   restores the previous desktop mode on the way back up. Writes go through
   `setMode` (not the raw ref) so persistence happens on every change,
   including the direct `sidebarMode = 'icon'` assignments in the template. */
const t = useAiI18n()

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

const settingsOpen = ref(false)
const commandPaletteOpen = ref(false)
const copiedId = ref<string | null>(null)

const composerText = computed({
  get: () => props.inputText,
  set: (v) => emit('update:input-text', v),
})

const hasMessages = computed(() => (props.messages?.length ?? 0) > 0)

/* `view` decides whether the main area is the conversation or the consumer's
   own page. The sidebar and top bar are outside this branch on purpose: a
   product's nav must survive navigating away from the chat. */
const CHAT_VIEW_ID = 'chat'

const isChatView = computed(() => props.view === CHAT_VIEW_ID)

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

/* Active on the landing page, which is where it navigates to. The rail's copy
   of this button used to light up on `hasMessages || activeThreadId` - the
   exact inverse - so the two disagreed about what the entry meant. */
const isLandingView = computed(
  () => isChatView.value && !props.activeThreadId && !hasMessages.value,
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
          active: isLandingView.value,
        },
      ]
    : []

  const builtin: NavItem[] = props.builtinViews.map((id) => ({
    id,
    label: t.value.views[id],
    icon: BUILTIN_VIEW_ICONS[id],
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
  for (const group of navGroupsResolved.value) {
    const hit = group.items.find((item) => item.id === props.view)
    if (hit) return hit.label
  }
  return ''
})

const topbarTitle = computed(
  () => activeViewLabel.value || props.threadTitle || props.brandName,
)

const hasPinnedNav = computed(() => pinnedNavGroups.value.length > 0)

/* Sections that render nothing on their own - they need either an
   `accountClient` to call or a consumer slot to fill them. */
const WIRED_SETTINGS_IDS = ['security', 'personalization', 'usage'] as const

const slots = useSlots()

/* A listed section that opens an empty pane is worse than an absent one: it
   reads as a broken page rather than a capability this deployment does not
   have. So the wired sections drop out unless something can actually fill
   them - the client, or a consumer slot of the same id. */
const effectiveSettingsSections = computed(() =>
  props.settingsSections.filter((section) => {
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
  props.settingsSections.filter((s) => !BUILTIN_SETTINGS_IDS.has(s.id)),
)

/* Controllers for the wired pages.
   Built once during setup, not inside a computed: these register refs and are
   composables, so re-running them on a dependency change would create a fresh
   set of state on every read. Account and Security deliberately share one
   controller - they read the same profile, and two would fetch it twice and
   then disagree after a write. `null` when no client was supplied, which is
   what hides both pages. */
const accountSettings = props.accountClient
  ? useAccountSettings({ client: props.accountClient })
  : null
const personalization = props.accountClient
  ? useAiPersonalization({ client: props.accountClient })
  : null
const usage = props.accountClient ? useAiUsage({ client: props.accountClient }) : null

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

/* Naive theme for this shell. `isDark` is the same value written to <html>,
   so the controls and the painted surfaces cannot disagree about the mode. */
const { theme: naiveTheme, themeOverrides: naiveThemeOverrides } = useAiNaiveTheme(isDark)

// ---------------------------------------------------------------------------
// Theme application
// ---------------------------------------------------------------------------

/* Applying the theme means toggling `.dark` on the document element: the
   package stylesheet declares the full light and dark `--tnzi-ai-*` palettes
   and switches between them off that class. TChatApp deliberately does NOT
   push token values of its own here - writing inline variables would pin them
   above any palette the host app configured on `:root`. Consumers who want
   different colours call `applyAiTheme()` from `@tnzi/ui-ai/theme`. */
function applyTheme(): void {
  if (!props.autoApplyTheme) return
  // Routed through @tnzi/ui so the ecosystem has one writer of the dark class.
  // Pass the ALREADY-resolved value, not `themePref`: this component resolves
  // 'auto' against the `prefersDark` ref (kept current by the media-query
  // listener below), while `applyThemeModeToDocument` would re-read matchMedia
  // itself. Handing it 'light'/'dark' keeps `<html>.dark` and the component's
  // own `--dark` class from ever disagreeing.
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
})

onBeforeUnmount(() => {
  systemMediaQuery?.removeEventListener('change', onSystemThemeChange)
  systemMediaQuery = null
})

// ---------------------------------------------------------------------------
// Event handlers
// ---------------------------------------------------------------------------

function pickTheme(next: ThemePref): void {
  emit('update:theme', next)
}

/* The two built-in menu ids drive built-in surfaces; everything else is the
   consumer's. `user-menu` fires for all of them either way, so a consumer can
   observe or override without losing the default behaviour. */
function onUserMenuSelect(id: string): void {
  if (id === 'settings') {
    settingsOpen.value = true
    emit('open-settings')
  } else if (id === 'sign-out') {
    emit('sign-out')
  }
  emit('user-menu', id)
}

function onLandingSubmit(text: string, files: File[] = []): void {
  if (!text.trim() && files.length === 0) return
  emit('send', text, files)
  emit('update:input-text', '')
}

function onChipClick(chip: LandingChip): void {
  emit('select-suggestion', chip)
  if (chip.prompt != null) {
    emit('update:input-text', chip.prompt)
  }
}

function onComposerSend(text: string, files: File[]): void {
  if (!text.trim() && files.length === 0) return
  emit('send', text, files)
  emit('update:input-text', '')
}

const rootStyle = computed<Record<string, string> | undefined>(() => {
  if (props.contentWidth == null) return undefined
  const w = typeof props.contentWidth === 'number' ? `${props.contentWidth}px` : props.contentWidth
  return { '--tnzi-ai-content-width': w }
})

// Stick-to-bottom auto-scroll for the message thread (re-attaches when the
// user scrolls back to the bottom; pauses while they read scrollback).
const { containerRef: threadScrollRef } = useAutoScroll()


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
  <!-- The shell provides the naive theme for everything it renders, including
       the modals and drawers it teleports. Without it the naive controls inside
       this package fell back to naive's stock look while the painted surfaces
       followed `--tnzi-ai-*`, so a product could set its colour and watch half
       the UI ignore it. Overrides come from the host's `@tnzi/ui` context when
       there is one - see `useAiNaiveTheme`. -->
  <!-- `abstract`: renders NO element. Without it naive inserts a
       `<div class="n-config-provider">` between the page and this shell,
       and `.t-chat-app`'s height chain resolves against a block div with
       no height - measured 1467px of content in a 711px viewport, with the
       composer and account bar pushed off screen. Theme still reaches every
       descendant (including teleported ones) because provide/inject follows
       the component tree, not the DOM. -->
  <NConfigProvider abstract :theme="naiveTheme" :theme-overrides="naiveThemeOverrides">
  <div class="t-chat-app" :class="{ 't-chat-app--dark': isDark }" :style="rootStyle">
    <!-- ───────────────────────── Sidebar ───────────────────────── -->
    <TCollapsibleSidebar
      v-model="sidebarMode"
      :width="sidebarWidth"
      :rail-width="56"
      :is-mobile="effectiveIsMobile"
    >
      <!-- Brand header -->
      <template #header>
        <slot name="brand">
          <div class="t-chat-app__brand">
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
            <strong class="t-chat-app__brand-name">{{ brandName }}</strong>
            <button
              type="button"
              class="t-chat-app__brand-collapse"
              :aria-label="t.sidebar.collapse"
              @click="sidebarMode = 'icon'"
            >
              <Icon icon="lucide:panel-left" />
            </button>
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
          />
        </slot>

        <slot
          name="sidebar-content"
          :threads="threads"
          :active-thread-id="activeThreadId"
        >
          <TThreadList
            :threads="threads"
            :active-thread-id="activeThreadId"
            :label="threadsLabel"
            :enable-delete="enableThreadDelete"
            :confirm-label="deleteConfirmLabel"
            @select="openThread"
            @delete="(id) => emit('delete-thread', id)"
            @add="startNewChat"
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
              @click="settingsOpen = true; emit('open-settings')"
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
            <template #bottom>
              <button
                v-if="enableSettings && !showAccountBar"
                type="button"
                class="t-chat-rail__btn"
                :aria-label="t.sidebar.settings"
                @click="settingsOpen = true; emit('open-settings')"
              >
                <Icon icon="lucide:settings-2" />
              </button>
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
    <main class="t-chat-app__main">
      <header v-if="showTopbar" class="t-chat-app__topbar">
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
            <span class="t-chat-app__workspace-switcher">
              <span>{{ topbarTitle }}</span>
            </span>
          </slot>
        </slot>

        <slot name="topbar-center" :view="view" />

        <div class="t-chat-app__topbar-spacer" />

        <slot name="topbar-actions" :has-thread="hasMessages" :view="view" />
      </header>

      <!-- Consumer-owned page. Sits inside the shell so the sidebar and top
           bar survive navigating away from the conversation. -->
      <section v-if="!isChatView" class="t-chat-app__view">
        <slot name="view" :view="view" />
      </section>

      <!-- Landing empty state -->
      <TLandingPage
        v-else-if="!hasMessages && showLanding"
        v-model="composerText"
        :greeting="landingGreeting"
        :subline="landingSubline"
        :chips="landingChips"
        :placeholder="landingPlaceholder"
        :composer-actions="composerActions"
        :enable-voice="enableVoice"
        :enable-attachments="enableAttachments"
        :accept="composerAccept"
        :voice-lang="voiceLang"
        @submit="onLandingSubmit"
        @chip-click="onChipClick"
        @action="emit('composer-action', $event)"
      >
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
        <template v-if="$slots['composer-left']" #composer-left>
          <slot name="composer-left" />
        </template>
        <template v-if="$slots['composer-right']" #composer-right>
          <slot name="composer-right" />
        </template>
        <template v-if="$slots['landing-footer']" #footer>
          <slot name="landing-footer" />
        </template>
      </TLandingPage>

      <!-- Active workspace: messages + optional artifact panel -->
      <section
        v-else
        class="t-chat-app__workspace"
        :class="{ 't-chat-app__workspace--has-artifact': !!artifact }"
      >
        <div ref="threadScrollRef" class="t-chat-app__thread">
          <div class="t-chat-app__thread-col">
          <template v-for="msg in messages" :key="msg.id">
            <slot name="message" :message="msg" :copied="copiedId === msg.id">
              <TThreadMessage
                :message="msg"
                :agent-name="agentName"
                :agent-label="agentLabel"
                :copied="copiedId === msg.id"
                @copy="onCopyMessage(msg)"
                @regenerate="emit('regenerate', $event)"
                @feedback="(id, type) => emit('feedback', id, type)"
              />
            </slot>
          </template>

          <TThreadComposer
            v-model="composerText"
            :placeholder="composerPlaceholder"
            :disabled="isStreaming"
            :composer-actions="composerActions"
            :enable-voice="enableVoice"
            :enable-attachments="enableAttachments"
            :accept="composerAccept"
            :voice-lang="voiceLang"
            @send="onComposerSend"
            @action="emit('composer-action', $event)"
          >
            <template v-if="$slots['thread-composer-left']" #left>
              <slot name="thread-composer-left" />
            </template>
            <template #right>
              <slot name="thread-composer-right">
                <button
                  v-if="isStreaming"
                  type="button"
                  class="t-chat-app__stop-btn"
                  :aria-label="t.chat.stop"
                  @click="emit('stop')"
                >
                  <Icon icon="lucide:square" />
                </button>
              </slot>
            </template>
            <!-- Rides inside the composer's sticky wrapper, so the fine print
                 stays put instead of scrolling away with the messages. -->
            <template v-if="disclaimer || $slots['thread-footer']" #footer>
              <slot name="thread-footer">{{ disclaimer }}</slot>
            </template>
          </TThreadComposer>
          </div>
        </div>

        <TArtifactPanel
          v-if="artifact"
          :artifact="artifact"
          :files="artifactFiles"
          :view="artifactView"
          :width="artifactWidth"
          class="t-chat-app__artifact-slot"
          @update:view="onArtifactViewChange"
          @update:width="onArtifactWidthChange"
        />
      </section>
    </main>

    <!-- ───────────────────────── Settings dialog ───────────────────────── -->
    <TSettingsDialog
      v-if="enableSettings"
      v-model="settingsOpen"
      :sections="effectiveSettingsSections"
      :title="settingsTitle"
      :searchable="settingsSearchable"
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
          <TAccountSettings v-if="accountSettings" :controller="accountSettings" />
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
  </div>
  </NConfigProvider>
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
.t-chat-app__brand-mark-svg {
  width: 18px;
  height: 18px;
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
.t-chat-app__brand-collapse {
  width: 32px;
  height: 32px;
  border: none;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  border-radius: 8px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 18px;
  flex-shrink: 0;
  transition: background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
              color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-chat-app__brand-collapse:hover {
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
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
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
}

/* ─── Main pane + topbar ───────────────────────────────────────────────── */
.t-chat-app__main {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
}
.t-chat-app__topbar {
  display: flex;
  align-items: center;
  gap: 8px;
  height: 52px;
  padding: 0 16px;
  flex-shrink: 0;
  background: transparent;
}
.t-chat-app__topbar-toggle {
  width: 32px;
  height: 32px;
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
   the shell around, and imposes no padding: the page owns its own layout. */
.t-chat-app__view {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
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
}
.t-chat-app__thread-col {
  /* No bottom padding: the sticky composer supplies it. `bottom: 0` pins the
     composer to the scroller's padding box, so any padding-bottom here is a
     strip the composer cannot cover and messages scroll through. */
  padding: 24px 24px 0;
  display: flex;
  flex-direction: column;
  gap: 24px;
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
.t-chat-app__workspace--has-artifact .t-chat-app__thread-col {
  max-width: 620px;
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
  color: var(--tnzi-ai-accent);
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

/* ─── Stop button (composer right slot, streaming-only) ────────────────── */
.t-chat-app__stop-btn {
  width: 30px;
  height: 30px;
  border: 1px solid var(--tnzi-ai-border);
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text);
  border-radius: 999px;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  font-size: 12px;
  transition: background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-chat-app__stop-btn:hover { background: var(--tnzi-ai-hover); }
</style>
