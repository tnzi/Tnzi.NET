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
 * See the Acme chat app (projects/acme/src/Acme.UI/chat) for a
 * real-world integration.
 */
import { computed, ref, watch, onMounted, onBeforeUnmount } from 'vue'
import { Icon } from '@iconify/vue'
import { TAvatar, applyThemeModeToDocument } from '@tnzi/ui'
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
    /** Label for the prominent new-chat button at the top of the sidebar. */
    newChatLabel?: string
    /**
     * Render the built-in new-chat button above the nav. Turn it off when the
     * product already carries "New chat" as a nav entry, which is the common
     * case once `navGroups` is in play - otherwise the entry appears twice,
     * in both the expanded sidebar and the collapsed rail.
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
    newChatLabel: 'New chat',
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
    settingsSections: () => [
      { id: 'account', label: 'Account', icon: 'lucide:circle-user-round' },
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
const isChatView = computed(() => props.view === 'chat')

/* `mainNav` becomes an unlabelled leading group so the flat prop and the
   grouped prop render through one component. Kept out of the array entirely
   when empty, otherwise TSidebarNav would draw the group's margins around
   nothing. */
/* Built-in views render as one unlabelled group between the primary nav and
   the consumer's own groups: they are destinations, not a titled section. */
const builtinNavGroup = computed<NavGroup | null>(() => {
  if (props.builtinViews.length === 0) return null
  return {
    id: '__builtin',
    items: props.builtinViews.map((id) => ({
      id,
      label: t.value.views[id],
      icon: BUILTIN_VIEW_ICONS[id],
      active: props.view === id,
    })),
  }
})

const navGroupsResolved = computed<ReadonlyArray<NavGroup>>(() => {
  const flat: NavGroup[] =
    props.mainNav.length > 0 ? [{ id: '__main', items: props.mainNav }] : []
  const builtin = builtinNavGroup.value ? [builtinNavGroup.value] : []
  return [...flat, ...builtin, ...props.navGroups]
})

const hasNav = computed(() => navGroupsResolved.value.length > 0)
const effectiveSettingsSections = computed(() => props.settingsSections)
const customSettingsSections = computed(() =>
  props.settingsSections.filter(
    (s) => s.id !== 'account' && s.id !== 'appearance' && s.id !== 'about',
  ),
)

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

      <!-- Sidebar content (expanded mode): nav + threads list -->
      <template #content>
        <button
          v-if="showNewChatButton"
          type="button"
          class="t-chat-app__nav-item t-chat-app__new-chat"
          @click="emit('new-chat')"
        >
          <Icon icon="lucide:square-pen" class="t-chat-app__nav-icon" />
          <span>{{ newChatLabel }}</span>
        </button>
        <slot name="sidebar-nav" :nav="mainNav" :groups="navGroupsResolved">
          <TSidebarNav
            v-if="hasNav"
            :groups="navGroupsResolved"
            @select="(item) => emit('nav', item)"
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
            @select="(id) => emit('select-thread', id)"
            @delete="(id) => emit('delete-thread', id)"
            @add="emit('new-chat')"
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
          <TChatRail
            :groups="navGroupsResolved"
            :brand-logo="brandLogo"
            :expand-label="t.sidebar.expand"
            @expand="sidebarMode = 'expanded'"
            @select="(item) => emit('nav', item)"
          >
            <template #top>
              <button
                v-if="showNewChatButton"
                type="button"
                class="t-chat-rail__btn"
                :class="{ 'is-active': hasMessages || activeThreadId }"
                :aria-label="t.chat.newChat"
                @click="emit('new-chat')"
              >
                <Icon icon="lucide:square-pen" />
              </button>
            </template>
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
          :title="threadTitle ?? brandName"
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
          <slot v-else name="topbar-title" :title="threadTitle ?? brandName" :has-thread="hasMessages">
            <span class="t-chat-app__workspace-switcher">
              <span>{{ threadTitle || brandName }}</span>
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
      <!-- Default Account section - real profile card (props-driven) -->
      <template #account>
        <slot name="settings-account">
          <div class="t-chat-app__account">
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

/* ─── Main nav (expanded) ──────────────────────────────────────────────── */
.t-chat-app__nav {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 0 8px 8px;
}
.t-chat-app__nav-item {
  display: flex;
  align-items: center;
  gap: 12px;
  width: 100%;
  height: 36px;
  padding: 0 9px;
  border: none;
  background: transparent;
  border-radius: 10px;
  color: var(--tnzi-ai-text);
  font-family: inherit;
  font-size: 14px;
  cursor: pointer;
  text-align: left;
  transition: background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-chat-app__nav-item:hover { background: var(--tnzi-ai-hover); }
.t-chat-app__nav-item.is-active {
  background: var(--tnzi-ai-accent-soft);
  color: var(--tnzi-ai-accent);
  font-weight: 500;
}
.t-chat-app__nav-icon {
  width: 18px;
  height: 18px;
  font-size: 18px;
  flex-shrink: 0;
  color: var(--tnzi-ai-text-secondary);
}
.t-chat-app__nav-item.is-active .t-chat-app__nav-icon {
  color: var(--tnzi-ai-accent);
}

/* ─── Sidebar footer ───────────────────────────────────────────────────── */
.t-chat-app__foot-icons {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 6px 12px 10px;
  border-top: 1px solid var(--tnzi-ai-divider);
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

/* ─── New-chat button (prominent, top of sidebar) ──────────────────────── */
.t-chat-app__new-chat {
  margin-bottom: 4px;
  font-weight: 500;
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
