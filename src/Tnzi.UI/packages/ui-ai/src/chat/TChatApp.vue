<script setup lang="ts">
/**
 * @experimental
 * TChatApp — production-grade chat application shell with Manus-inspired design.
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
 *   - Settings dialog (Account / Appearance / About — extensible)
 *   - Command palette (Cmd+K) — optional, off by default
 *   - Auto theme application (light/dark/system)
 *
 * See packages/ui-ai/playground/src/components/AppShell.vue for the
 * reference implementation; TChatApp distils its reusable parts into
 * a packaged component.
 */
import { computed, ref, watch, onMounted, onBeforeUnmount } from 'vue'
import { Icon } from '@iconify/vue'
import TCollapsibleSidebar from '@/shell/TCollapsibleSidebar.vue'
import TCommandPalette from '@/shell/TCommandPalette.vue'
import TSettingsDialog from '@/shell/TSettingsDialog.vue'
import TLandingPage from '@/shell/TLandingPage.vue'
import type { LandingChip } from '@/shell/TLandingPage.vue'
import TThreadComposer from '@/components/chat/TThreadComposer.vue'
import TReasoningStage from '@/components/chat/TReasoningStage.vue'
import TArtifactPanel from '@/components/artifact/TArtifactPanel.vue'
import type {
  ArtifactView,
  ArtifactPanelItem,
  ArtifactFile,
} from '@/components/artifact/TArtifactPanel.vue'
import type { SettingsSection } from '@/composables/useSettingsDialog'
import type { CommandAction } from '@/composables/useCommandPalette'
import type { SidebarMode } from '@/composables/useSidebarState'
import type { ChatMessage } from '@/composables/useChat'
import { applyAiTheme, lightTokens, darkTokens } from '@/themes/tokens'

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

export interface ThreadItem {
  readonly id: string
  readonly title: string
  readonly updatedAt?: string
}

export interface NavItem {
  readonly id: string
  readonly label: string
  readonly icon: string
  readonly active?: boolean
}

export type ThemePref = 'light' | 'dark' | 'system'

// Re-export the LandingChip type so consumers can import a single source.
export type { LandingChip }

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
    mainNav?: ReadonlyArray<NavItem>
    /** Initial sidebar mode. */
    initialSidebarMode?: SidebarMode
    /** Expanded sidebar width (px). */
    sidebarWidth?: number
    /** Section heading above the threads list. */
    threadsLabel?: string

    // ── Threads ─────────────────────────────────────────────────────────
    threads?: ReadonlyArray<ThreadItem>
    activeThreadId?: string

    // ── Chat thread ─────────────────────────────────────────────────────
    messages?: ReadonlyArray<ChatMessage>
    isStreaming?: boolean
    /** Composer text — v-model'able. */
    inputText?: string
    composerPlaceholder?: string
    /** Agent display name. */
    agentName?: string
    /** Small label rendered after the agent name (e.g. "Pro", "Lite"). */
    agentLabel?: string
    /** Top-bar title; defaults to active thread title or brandName. */
    threadTitle?: string

    // ── Landing empty state ─────────────────────────────────────────────
    /** Show the landing empty state when there are no messages. */
    showLanding?: boolean
    landingGreeting?: string
    landingPlaceholder?: string
    landingChips?: ReadonlyArray<LandingChip>

    // ── Settings ────────────────────────────────────────────────────────
    enableSettings?: boolean
    settingsSections?: ReadonlyArray<SettingsSection>
    settingsTitle?: string

    // ── Command palette ─────────────────────────────────────────────────
    enableCommandPalette?: boolean
    commandActions?: ReadonlyArray<CommandAction>

    // ── Theme ───────────────────────────────────────────────────────────
    theme?: ThemePref
    /** When true (default), TChatApp calls `applyAiTheme()` on mount and
     *  whenever the theme changes. Set false if your app owns theme. */
    autoApplyTheme?: boolean

    // ── Artifact ────────────────────────────────────────────────────────
    artifact?: ArtifactPanelItem | null
    artifactFiles?: ReadonlyArray<ArtifactFile>
    artifactView?: ArtifactView
    artifactWidth?: number

    // ── Responsive ──────────────────────────────────────────────────────
    isMobile?: boolean
  }>(),
  {
    brandName: 'Tnzi AI',
    brandLogo: 'monogram',
    mainNav: () => [],
    initialSidebarMode: 'expanded',
    sidebarWidth: 300,
    threadsLabel: 'All tasks',
    threads: () => [],
    messages: () => [],
    isStreaming: false,
    inputText: '',
    composerPlaceholder: 'Type a message…',
    agentName: 'Assistant',
    showLanding: true,
    landingGreeting: 'What can I do for you?',
    landingPlaceholder: 'Assign a task or ask anything',
    landingChips: () => [],
    enableSettings: true,
    settingsSections: () => [
      { id: 'account', label: 'Account', icon: 'lucide:circle-user-round' },
      { id: 'appearance', label: 'Appearance', icon: 'lucide:settings' },
      { id: 'about', label: 'About', icon: 'lucide:info' },
    ],
    settingsTitle: 'Settings',
    enableCommandPalette: false,
    commandActions: () => [],
    theme: 'light',
    autoApplyTheme: true,
    artifact: null,
    artifactFiles: () => [],
    artifactView: 'code',
    artifactWidth: 520,
    isMobile: false,
  },
)

const emit = defineEmits<{
  // Sidebar
  'new-chat': []
  'select-thread': [threadId: string]
  'delete-thread': [threadId: string]
  nav: [item: NavItem]

  // Composer
  send: [content: string, files: File[]]
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
  'open-settings': []

  // Artifact
  'close-artifact': []
  'update:artifact-view': [view: ArtifactView]
  'update:artifact-width': [width: number]
}>()

// ---------------------------------------------------------------------------
// Internal state
// ---------------------------------------------------------------------------

const sidebarMode = ref<SidebarMode>(props.initialSidebarMode)
const settingsOpen = ref(false)
const commandPaletteOpen = ref(false)
const copiedId = ref<string | null>(null)

const composerText = computed({
  get: () => props.inputText,
  set: (v) => emit('update:input-text', v),
})

const hasMessages = computed(() => (props.messages?.length ?? 0) > 0)
const effectiveSettingsSections = computed(() => props.settingsSections)
const customSettingsSections = computed(() =>
  props.settingsSections.filter(
    (s) => s.id !== 'account' && s.id !== 'appearance' && s.id !== 'about',
  ),
)

const themeOptions: ReadonlyArray<{ id: ThemePref; label: string }> = [
  { id: 'light', label: 'Light' },
  { id: 'dark', label: 'Dark' },
  { id: 'system', label: 'Follow System' },
]

const resolvedTheme = computed<'light' | 'dark'>(() => {
  if (props.theme === 'system') {
    if (typeof window === 'undefined' || !window.matchMedia) return 'light'
    return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
  }
  return props.theme === 'dark' ? 'dark' : 'light'
})

const isDark = computed(() => resolvedTheme.value === 'dark')

// ---------------------------------------------------------------------------
// Theme application
// ---------------------------------------------------------------------------

function applyTheme(): void {
  if (!props.autoApplyTheme) return
  if (typeof document === 'undefined') return
  applyAiTheme(isDark.value ? darkTokens : lightTokens)
  document.documentElement.classList.toggle('dark', isDark.value)
}

watch(resolvedTheme, applyTheme, { immediate: false })

// ---------------------------------------------------------------------------
// Listen to system theme changes when in 'system' mode
// ---------------------------------------------------------------------------

let systemMediaQuery: MediaQueryList | null = null
function onSystemThemeChange(): void {
  if (props.theme === 'system') applyTheme()
}

onMounted(() => {
  applyTheme()
  if (typeof window !== 'undefined' && window.matchMedia) {
    systemMediaQuery = window.matchMedia('(prefers-color-scheme: dark)')
    systemMediaQuery.addEventListener('change', onSystemThemeChange)
  }
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

function onLandingSubmit(text: string): void {
  if (!text.trim()) return
  emit('send', text, [])
  emit('update:input-text', '')
}

function onChipClick(chip: LandingChip): void {
  emit('select-suggestion', chip)
  if (chip.prompt != null) {
    emit('update:input-text', chip.prompt)
  }
}

function onComposerSend(text: string): void {
  if (!text.trim()) return
  emit('send', text, [])
  emit('update:input-text', '')
}

async function onCopyMessage(message: ChatMessage): Promise<void> {
  emit('copy', message.id)
  try {
    if (typeof navigator !== 'undefined' && navigator.clipboard) {
      await navigator.clipboard.writeText(message.content)
    }
    copiedId.value = message.id
    setTimeout(() => {
      if (copiedId.value === message.id) copiedId.value = null
    }, 1500)
  } catch {
    // ignore — clipboard may be unavailable in sandboxed contexts
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
  <div class="t-chat-app" :class="{ 't-chat-app--dark': isDark }">
    <!-- ───────────────────────── Sidebar ───────────────────────── -->
    <TCollapsibleSidebar
      v-model="sidebarMode"
      :width="sidebarWidth"
      :rail-width="56"
      :is-mobile="isMobile"
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
              aria-label="Collapse sidebar"
              @click="sidebarMode = 'icon'"
            >
              <Icon icon="lucide:panel-left" />
            </button>
          </div>
        </slot>
      </template>

      <!-- Sidebar content (expanded mode): nav + threads list -->
      <template #content>
        <slot name="sidebar-nav" :nav="mainNav">
          <nav v-if="mainNav.length > 0" class="t-chat-app__nav">
            <button
              v-for="item in mainNav"
              :key="item.id"
              type="button"
              class="t-chat-app__nav-item"
              :class="{ 'is-active': item.active }"
              @click="emit('nav', item)"
            >
              <Icon :icon="item.icon" class="t-chat-app__nav-icon" />
              <span>{{ item.label }}</span>
            </button>
          </nav>
        </slot>

        <slot
          name="sidebar-content"
          :threads="threads"
          :active-thread-id="activeThreadId"
        >
          <div class="t-chat-app__section">
            <div class="t-chat-app__section-head">
              <span>{{ threadsLabel }}</span>
              <button
                type="button"
                class="t-chat-app__section-act"
                aria-label="New chat"
                @click="emit('new-chat')"
              >
                <Icon icon="lucide:plus" />
              </button>
            </div>
            <button
              v-for="thread in threads"
              :key="thread.id"
              type="button"
              class="t-chat-app__thread-row"
              :class="{ 'is-active': thread.id === activeThreadId }"
              @click="emit('select-thread', thread.id)"
            >
              <Icon icon="lucide:message-square" class="t-chat-app__thread-icon" />
              <span class="t-chat-app__thread-title">{{ thread.title }}</span>
            </button>
          </div>
        </slot>
      </template>

      <!-- Sidebar footer -->
      <template #footer>
        <slot name="sidebar-footer">
          <div class="t-chat-app__foot-icons">
            <button
              v-if="enableSettings"
              type="button"
              class="t-chat-app__foot-btn"
              aria-label="Settings"
              @click="settingsOpen = true; emit('open-settings')"
            >
              <Icon icon="lucide:settings-2" />
            </button>
            <button
              v-if="enableCommandPalette"
              type="button"
              class="t-chat-app__foot-btn"
              aria-label="Command palette"
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
          <div class="t-chat-app__rail">
            <button
              type="button"
              class="t-chat-app__rail-brand"
              aria-label="Expand sidebar"
              @click="sidebarMode = 'expanded'"
            >
              <span class="t-chat-app__rail-brand-logo" aria-hidden="true">
                <svg
                  v-if="brandLogo === 'monogram'"
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
              <Icon icon="lucide:panel-left-open" class="t-chat-app__rail-brand-expand" />
            </button>
            <div class="t-chat-app__rail-group t-chat-app__rail-group--top">
              <button
                type="button"
                class="t-chat-app__rail-btn"
                :class="{ 'is-active': hasMessages || activeThreadId }"
                aria-label="New chat"
                @click="emit('new-chat')"
              >
                <Icon icon="lucide:square-pen" />
              </button>
              <button
                v-for="item in mainNav"
                :key="item.id"
                type="button"
                class="t-chat-app__rail-btn"
                :class="{ 'is-active': item.active }"
                :aria-label="item.label"
                @click="emit('nav', item)"
              >
                <Icon :icon="item.icon" />
              </button>
            </div>
            <div class="t-chat-app__rail-group t-chat-app__rail-group--bottom">
              <button
                v-if="enableSettings"
                type="button"
                class="t-chat-app__rail-btn"
                aria-label="Settings"
                @click="settingsOpen = true; emit('open-settings')"
              >
                <Icon icon="lucide:settings-2" />
              </button>
            </div>
          </div>
        </slot>
      </template>
    </TCollapsibleSidebar>

    <!-- ───────────────────────── Main area ───────────────────────── -->
    <main class="t-chat-app__main">
      <header class="t-chat-app__topbar">
        <button
          v-if="sidebarMode === 'hidden'"
          type="button"
          class="t-chat-app__topbar-toggle"
          aria-label="Show sidebar"
          @click="sidebarMode = 'expanded'"
        >
          <Icon icon="lucide:panel-left-open" />
        </button>

        <slot name="topbar-title" :title="threadTitle ?? brandName" :has-thread="hasMessages">
          <span class="t-chat-app__workspace-switcher">
            <span>{{ threadTitle || brandName }}</span>
          </span>
        </slot>

        <div class="t-chat-app__topbar-spacer" />

        <slot name="topbar-actions" :has-thread="hasMessages" />
      </header>

      <!-- Landing empty state -->
      <TLandingPage
        v-if="!hasMessages && showLanding"
        v-model="composerText"
        :greeting="landingGreeting"
        :chips="landingChips"
        :placeholder="landingPlaceholder"
        @submit="onLandingSubmit"
        @chip-click="onChipClick"
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
        <div class="t-chat-app__thread">
          <template v-for="msg in messages" :key="msg.id">
            <slot name="message" :message="msg" :copied="copiedId === msg.id">
              <div class="t-chat-app__msg" :class="`t-chat-app__msg--${msg.role}`">
                <div
                  v-if="msg.role === 'user'"
                  class="t-chat-app__msg-bubble t-chat-app__msg-bubble--user"
                >
                  {{ msg.content }}
                </div>

                <template v-else>
                  <div class="t-chat-app__msg-role">
                    <span class="t-chat-app__msg-brand" aria-hidden="true">
                      <Icon icon="lucide:sparkles" />
                    </span>
                    <strong>{{ msg.agentName || agentName }}</strong>
                    <span v-if="agentLabel" class="t-chat-app__msg-tag">{{ agentLabel }}</span>
                    <span
                      v-if="msg.isStreaming"
                      class="t-chat-app__msg-streaming"
                      aria-label="streaming"
                    >●</span>
                  </div>

                  <TReasoningStage
                    v-if="msg.reasoning"
                    :status="msg.isStreaming ? 'running' : 'done'"
                  >
                    {{ msg.reasoning }}
                  </TReasoningStage>

                  <div class="t-chat-app__msg-body">{{ msg.content }}</div>

                  <div v-if="!msg.isStreaming" class="t-chat-app__msg-actions">
                    <button
                      type="button"
                      class="t-chat-app__msg-action"
                      :aria-label="copiedId === msg.id ? 'Copied' : 'Copy'"
                      @click="onCopyMessage(msg)"
                    >
                      <Icon :icon="copiedId === msg.id ? 'lucide:check' : 'lucide:copy'" />
                    </button>
                    <button
                      type="button"
                      class="t-chat-app__msg-action"
                      aria-label="Regenerate"
                      @click="emit('regenerate', msg.id)"
                    >
                      <Icon icon="lucide:refresh-ccw" />
                    </button>
                    <button
                      type="button"
                      class="t-chat-app__msg-action"
                      aria-label="Like"
                      @click="emit('feedback', msg.id, 'positive')"
                    >
                      <Icon icon="lucide:thumbs-up" />
                    </button>
                    <button
                      type="button"
                      class="t-chat-app__msg-action"
                      aria-label="Dislike"
                      @click="emit('feedback', msg.id, 'negative')"
                    >
                      <Icon icon="lucide:thumbs-down" />
                    </button>
                  </div>
                </template>
              </div>
            </slot>
          </template>

          <TThreadComposer
            v-model="composerText"
            :placeholder="composerPlaceholder"
            :disabled="isStreaming"
            @send="onComposerSend"
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
                  aria-label="Stop"
                  @click="emit('stop')"
                >
                  <Icon icon="lucide:square" />
                </button>
              </slot>
            </template>
          </TThreadComposer>
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
    >
      <!-- Default Account section -->
      <template #account>
        <slot name="settings-account">
          <div class="t-chat-app__settings-empty">
            Configure your account section by providing a
            <code>#settings-account</code> slot.
          </div>
        </slot>
      </template>

      <!-- Appearance with built-in theme picker -->
      <template #appearance>
        <slot name="settings-appearance" :theme="theme" :pick-theme="pickTheme">
          <div class="t-chat-app__settings-group">
            <div class="t-chat-app__settings-label">Appearance</div>
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
          </div>
        </slot>
      </template>

      <!-- About -->
      <template #about>
        <slot name="settings-about">
          <div class="t-chat-app__settings-group">
            <div class="t-chat-app__settings-label">{{ brandName }}</div>
            <div class="t-chat-app__settings-desc">
              Powered by Tnzi.NET — modular AI framework.
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
 *  TChatApp — Manus-aligned visual layer.
 *
 *  Every dimension / colour / motion value below is sourced from the playground
 *  AppShell.vue reference (`packages/ui-ai/playground/src/components/AppShell.vue`).
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
  background: linear-gradient(135deg, #1f1f1f 0%, #3a3a3a 100%);
  color: #ffffff;
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
  background: var(--tnzi-ai-selected);
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
  color: var(--tnzi-ai-text);
}

/* ─── Sidebar sections (threads list) ──────────────────────────────────── */
.t-chat-app__section {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 14px 10px 4px;
}
.t-chat-app__section-head {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 0 10px 4px;
  color: var(--tnzi-ai-text-tertiary);
  font-size: 11px;
  font-weight: 500;
  letter-spacing: 0.01em;
}
.t-chat-app__section-head > span:first-child { flex: 1; }
.t-chat-app__section-act {
  width: 20px;
  height: 20px;
  border: none;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  border-radius: 4px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 14px;
}
.t-chat-app__section-act:hover {
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
}

/* Thread rows share the same shape as nav-item (h-36, p-9, gap-12, radius-10,
 * icon-18) so every line in the expanded sidebar visually aligns. */
.t-chat-app__thread-row {
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
  min-width: 0;
  transition: background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-chat-app__thread-row:hover { background: var(--tnzi-ai-hover); }
.t-chat-app__thread-row.is-active { background: var(--tnzi-ai-selected); }
.t-chat-app__thread-icon {
  flex-shrink: 0;
  width: 18px;
  height: 18px;
  font-size: 18px;
  color: var(--tnzi-ai-text-secondary);
}
.t-chat-app__thread-title {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
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

/* ─── Collapsed rail ───────────────────────────────────────────────────── */
.t-chat-app__rail {
  display: flex;
  flex-direction: column;
  align-items: center;
  height: 100%;
  width: 100%;
  padding: 0;
  background: transparent;
}
/* Rail brand: monogram with hover-swap-to-panel-icon (Manus pattern). The
 * swap is keyed on `.t-chat-app__rail:hover` so any pointer inside the rail
 * triggers it, not just hovering the logo itself. */
.t-chat-app__rail-brand {
  position: relative;
  width: 36px;
  height: 36px;
  margin-top: 10px;
  margin-bottom: 4px;
  border: none;
  border-radius: 10px;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  font-size: 18px;
  transition: background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
              color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-chat-app__rail-brand:hover { background: var(--tnzi-ai-hover); }
.t-chat-app__rail-brand-logo,
.t-chat-app__rail-brand-expand {
  position: absolute;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -50%);
  transition: opacity 150ms var(--tnzi-ai-easing);
}
.t-chat-app__rail-brand-logo {
  width: 28px;
  height: 28px;
  border-radius: 8px;
  background: linear-gradient(135deg, #1f1f1f 0%, #3a3a3a 100%);
  color: #ffffff;
  display: flex;
  align-items: center;
  justify-content: center;
}
.t-chat-app__rail-brand-logo > svg {
  width: 16px;
  height: 16px;
}
.t-chat-app__rail-brand-expand {
  opacity: 0;
  font-size: 18px;
  width: 18px;
  height: 18px;
}
.t-chat-app__rail:hover .t-chat-app__rail-brand-logo { opacity: 0; }
.t-chat-app__rail:hover .t-chat-app__rail-brand-expand { opacity: 1; }
.t-chat-app__rail:hover .t-chat-app__rail-brand { color: var(--tnzi-ai-text); }

.t-chat-app__rail-group {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 2px;
  padding: 8px 0;
}
.t-chat-app__rail-group--top { margin-top: 4px; }
.t-chat-app__rail-group--bottom {
  margin-top: auto;
  padding: 8px 0 12px;
  border-top: 1px solid var(--tnzi-ai-divider);
  width: 100%;
  align-items: center;
}

.t-chat-app__rail-btn {
  width: 36px;
  height: 36px;
  border: none;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  border-radius: 10px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 18px;
  transition: background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
              color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-chat-app__rail-btn:hover {
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
}
.t-chat-app__rail-btn.is-active {
  background: var(--tnzi-ai-selected);
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
.t-chat-app__thread {
  flex: 1;
  min-width: 0;
  overflow-y: auto;
  padding: 24px;
  display: flex;
  flex-direction: column;
  gap: 24px;
  max-width: 820px;
  width: 100%;
  margin: 0 auto;
}
/* When the artifact pane is visible, narrow the thread column and keep it
 * centered between the sidebar and the artifact (matches Manus behaviour). */
.t-chat-app__workspace--has-artifact .t-chat-app__thread {
  max-width: 620px;
  margin: 0 auto;
  padding: 24px;
}

/* ─── Messages ─────────────────────────────────────────────────────────── */
.t-chat-app__msg {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.t-chat-app__msg--user { align-items: flex-end; }
.t-chat-app__msg-bubble--user {
  padding: 10px 16px;
  background: var(--tnzi-ai-surface);
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 18px;
  max-width: 75%;
  font-size: 15px;
  line-height: 1.6;
  color: var(--tnzi-ai-text);
  white-space: pre-wrap;
  word-break: break-word;
}
.t-chat-app__msg-role {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 13px;
  color: var(--tnzi-ai-text-secondary);
}
.t-chat-app__msg-role strong {
  font-weight: 500;
  color: var(--tnzi-ai-text);
}
.t-chat-app__msg-brand {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
  border-radius: 6px;
  background: var(--tnzi-ai-text);
  color: var(--tnzi-ai-bg);
  font-size: 13px;
  flex-shrink: 0;
}
.t-chat-app__msg-tag {
  font-size: 10px;
  padding: 1px 6px;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 4px;
  color: var(--tnzi-ai-text-tertiary);
  font-weight: 500;
  letter-spacing: 0.02em;
}
.t-chat-app__msg-streaming {
  color: var(--tnzi-ai-accent);
  animation: t-chat-app-pulse 1s infinite;
}
@keyframes t-chat-app-pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.3; }
}
.t-chat-app__msg-body {
  font-size: 15px;
  line-height: 1.6;
  white-space: pre-wrap;
  word-break: break-word;
  color: var(--tnzi-ai-text);
}

/* Message action buttons — pill chips. Default rendering is icon-only (a
 * 30×30 circle); when content includes a label text span next to the icon
 * the chip widens to a pill via the cap classes below. */
.t-chat-app__msg-actions {
  display: flex;
  align-items: center;
  gap: 4px;
  margin-top: 8px;
}
.t-chat-app__msg-action {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  width: 30px;
  height: 30px;
  padding: 0;
  border: 1px solid var(--tnzi-ai-border);
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text);
  border-radius: 999px;
  font-family: inherit;
  font-size: 12px;
  font-weight: 500;
  cursor: pointer;
  transition: background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-chat-app__msg-action:hover { background: var(--tnzi-ai-hover); }
.t-chat-app__msg-action > .iconify {
  font-size: 13px;
  color: var(--tnzi-ai-text-secondary);
}
/* When the action contains text (icon + label), drop the fixed width so the
 * chip expands to fit the label. Use the modifier `--withLabel`. */
.t-chat-app__msg-action--with-label {
  width: auto;
  padding: 0 12px;
}

/* ─── Artifact panel slot ──────────────────────────────────────────────── */
.t-chat-app__artifact-slot {
  flex-shrink: 0;
  margin: 12px 12px 12px 0;
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
