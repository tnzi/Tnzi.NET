<script setup lang="ts">
/**
 * Playground AppShell - slim consumer of `@tnzi/ui-ai/chat`'s `TChatApp`.
 *
 * The point of this file is to show that a playground / demo app can pick
 * up the canonical `TChatApp` shell and only contribute the bits that are
 * domain-specific:
 *   - scenario picker sidebar (`<ScenarioSidebar/>` via `#sidebar-content`)
 *   - replay controls (`<ReplayControls/>` via `#topbar-actions`)
 *   - showcased-components footer (`#landing-footer`)
 *   - standalone command palette + settings dialog (driven imperatively by
 *     the playground store so command actions can open them)
 *
 * The chat surface - sidebar chrome, landing empty state, message thread,
 * composer, reasoning trace, artifact panel, stop button, theme tokens - * comes entirely from `<TChatApp>`. No Manus markup is re-implemented here.
 */
import { computed, ref, shallowRef, watch, onUnmounted } from 'vue'
import { TChatApp, type ThemePref, type LandingChip } from '@tnzi/ui-ai/chat'
import { TCommandPalette, TSettingsDialog } from '@tnzi/ui-ai/shell'
import type { ArtifactPanelItem } from '@tnzi/ui-ai/components'
// `ChatMessage` is exposed under the alias `ChatMessageData` from the
// composables barrel because the top-level package barrel also exports a
// Vue component named `ChatMessage`.
import type { ChatMessageData as ChatMessage } from '@tnzi/ui-ai/composables'
import { usePlaygroundStore } from '../state/playground-store'
import { useCommandActions } from '../state/actions'
import {
  createMockChatEngine,
  type MockChatEngine,
  type MockChatMessage,
} from '../mock/engine'
import type { CitationMock } from '../mock/types'
import ScenarioSidebar from './ScenarioSidebar.vue'
import ReplayControls from './ReplayControls.vue'

// ---------------------------------------------------------------------------
// State
// ---------------------------------------------------------------------------

const store = usePlaygroundStore()
const commandActions = useCommandActions()
const inputText = ref('')

// Engine lifecycle: a fresh engine per scenario; auto-play on switch.
const engine = shallowRef<MockChatEngine | null>(null)

function disposeEngine(): void {
  engine.value?.dispose()
  engine.value = null
}

watch(
  () => store.currentScenario.value,
  (scenario) => {
    disposeEngine()
    if (!scenario) return
    const next = createMockChatEngine(scenario)
    engine.value = next
    next.controls.play()
  },
  { immediate: true },
)

onUnmounted(disposeEngine)

// ---------------------------------------------------------------------------
// MockChatMessage → ChatMessage (TChatApp consumes the `useChat` shape)
// ---------------------------------------------------------------------------

function renderCitations(citations: readonly CitationMock[] | undefined): string {
  if (!citations || citations.length === 0) return ''
  const lines = citations.map((c, i) => {
    const tail = c.url ? ` - ${c.url}` : ''
    return `[${i + 1}] ${c.title}${tail}`
  })
  return `\n\nReferences:\n${lines.join('\n')}`
}

function toChatMessage(m: MockChatMessage): ChatMessage {
  return {
    id: m.id,
    role: m.role,
    content: m.content + renderCitations(m.citations),
    reasoning: m.reasoning ?? null,
    agentName: m.agentName ?? null,
    model: m.model ?? null,
    isStreaming: m.isStreaming ?? false,
    createdAt: '', // unused by TChatApp template
  }
}

const messages = computed<ChatMessage[]>(() => {
  const e = engine.value
  if (!e) return []
  return e.state.messages.value.map(toChatMessage)
})

const artifact = computed<ArtifactPanelItem | null>(() => {
  const a = engine.value?.state.artifact.value
  if (!a) return null
  return { title: a.title, content: a.content }
})

// ---------------------------------------------------------------------------
// Threads = scenarios (the scenario IS the thread in playground)
// ---------------------------------------------------------------------------

const threads = computed(() =>
  store.scenarioIndex.scenarios.map((s) => ({
    id: s.meta.id,
    title: s.meta.title,
  })),
)

const threadTitle = computed(() => store.currentScenario.value?.meta.title ?? '')

const landingGreeting = computed(() => {
  const s = store.currentScenario.value
  return s ? s.meta.description : 'Pick a showcase scenario from the sidebar.'
})

const landingChips = computed<readonly LandingChip[]>(() => {
  const s = store.currentScenario.value
  if (!s) return []
  return [
    { id: 'play', label: 'Play this scenario', icon: 'lucide:play' },
    { id: 'skip', label: 'Skip to end', icon: 'lucide:fast-forward' },
  ]
})

// ---------------------------------------------------------------------------
// Engine playback derived state
// ---------------------------------------------------------------------------

const playbackState = computed(() => engine.value?.state.playbackState.value ?? 'idle')
const speed = computed(() => engine.value?.state.speed.value ?? 1)
const isStreaming = computed(() => playbackState.value === 'playing')

// ---------------------------------------------------------------------------
// Theme - store is 'light' | 'dark'; TChatApp accepts ThemePref incl. 'system'
// ---------------------------------------------------------------------------

const themePref = computed<ThemePref>(() => store.theme.value)

function onThemeChange(next: ThemePref): void {
  // Ignore 'system' - playground store does not persist that mode.
  if (next === 'light' || next === 'dark') store.theme.value = next
}

// ---------------------------------------------------------------------------
// TChatApp events
// ---------------------------------------------------------------------------

function onSelectThread(id: string): void {
  store.selectScenario(id)
}

function onSend(text: string): void {
  // Playground is read-only - composer "send" is a no-op past skipping ahead.
  if (!text.trim()) return
  engine.value?.controls.skipToEnd()
  inputText.value = ''
}

function onChipClick(chip: LandingChip): void {
  if (chip.id === 'play') engine.value?.controls.play()
  if (chip.id === 'skip') engine.value?.controls.skipToEnd()
}

// ---------------------------------------------------------------------------
// Replay controls
// ---------------------------------------------------------------------------

function onPlay(): void {
  engine.value?.controls.play()
}
function onPause(): void {
  engine.value?.controls.pause()
}
function onSkipToEnd(): void {
  engine.value?.controls.skipToEnd()
}
function onReset(): void {
  engine.value?.controls.reset()
}
function onSpeed(next: number): void {
  engine.value?.controls.setSpeed(next)
}

// ---------------------------------------------------------------------------
// Settings dialog (standalone - store-driven open + section navigation)
// ---------------------------------------------------------------------------

const settingsSections = [
  { id: 'appearance', label: 'Appearance', icon: 'lucide:palette' },
  { id: 'about', label: 'About', icon: 'lucide:info' },
] as const

const themeOptions = [
  { id: 'light' as const, label: 'Light' },
  { id: 'dark' as const, label: 'Dark' },
]
</script>

<template>
  <div class="playground-shell">
    <TChatApp
      brand-name="ui-ai Playground"
      :threads="threads"
      :active-thread-id="store.currentScenarioId.value"
      :messages="messages"
      :is-streaming="isStreaming"
      :artifact="artifact"
      v-model:input-text="inputText"
      :theme="themePref"
      :initial-sidebar-mode="store.sidebarMode.value"
      :show-landing="messages.length === 0"
      :landing-greeting="landingGreeting"
      :landing-chips="landingChips"
      composer-placeholder="Composer is read-only - replay drives the conversation"
      :thread-title="threadTitle"
      agent-name="Mock Assistant"
      agent-label="DEMO"
      :enable-settings="false"
      :enable-command-palette="false"
      @select-thread="onSelectThread"
      @send="onSend"
      @stop="onPause"
      @select-suggestion="onChipClick"
      @update:theme="onThemeChange"
    >
      <!-- Sidebar: scenario picker replaces TChatApp's default thread list. -->
      <template #sidebar-content>
        <ScenarioSidebar />
      </template>

      <!-- Topbar: replay controls + imperative palette / settings buttons. -->
      <template #topbar-actions>
        <ReplayControls
          :playback-state="playbackState"
          :speed="speed"
          @play="onPlay"
          @pause="onPause"
          @skip-to-end="onSkipToEnd"
          @reset="onReset"
          @update:speed="onSpeed"
        />
        <button
          type="button"
          class="playground-shell__topbar-btn"
          aria-label="Command palette"
          title="Command palette (Ctrl+K)"
          @click="store.commandPaletteOpen.value = true"
        >
          ⌘K
        </button>
        <button
          type="button"
          class="playground-shell__topbar-btn"
          aria-label="Settings"
          @click="store.settingsOpen.value = true"
        >
          ⚙
        </button>
      </template>

      <!-- Landing footer: list components showcased by current scenario. -->
      <template #landing-footer>
        <div
          v-if="store.currentScenario.value"
          class="playground-shell__showcase"
        >
          <span class="playground-shell__showcase-label">Components in this demo:</span>
          <span
            v-for="c in store.currentScenario.value.meta.componentsShowcased"
            :key="c"
            class="playground-shell__showcase-tag"
          >{{ c }}</span>
        </div>
      </template>
    </TChatApp>

    <!-- Standalone palette: store-driven so command actions can open it. -->
    <TCommandPalette
      v-model="store.commandPaletteOpen.value"
      :actions="commandActions"
    />

    <!-- Standalone settings dialog: same reason. -->
    <TSettingsDialog
      v-model="store.settingsOpen.value"
      :sections="settingsSections"
      title="Playground Settings"
    >
      <template #appearance>
        <div class="playground-shell__section">
          <div class="playground-shell__section-label">Theme</div>
          <div class="playground-shell__theme-row">
            <button
              v-for="opt in themeOptions"
              :key="opt.id"
              type="button"
              class="playground-shell__theme-pill"
              :class="{ 'is-active': store.theme.value === opt.id }"
              @click="store.theme.value = opt.id"
            >
              {{ opt.label }}
            </button>
          </div>
        </div>
      </template>

      <template #about>
        <div class="playground-shell__section">
          <div class="playground-shell__section-label">ui-ai Playground</div>
          <p class="playground-shell__section-text">
            Living component matrix for <code>@tnzi/ui-ai</code>. The chat
            shell is rendered by <code>&lt;TChatApp&gt;</code> from
            <code>@tnzi/ui-ai/chat</code>; this playground only contributes
            the scenario picker, replay controls, and showcase footer.
          </p>
        </div>
      </template>
    </TSettingsDialog>
  </div>
</template>

<style scoped>
.playground-shell {
  width: 100%;
  height: 100%;
}

.playground-shell__topbar-btn {
  width: 32px;
  height: 32px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  background: transparent;
  border: 1px solid var(--tnzi-ai-border, #e5e7eb);
  border-radius: 6px;
  cursor: pointer;
  color: inherit;
  font-size: 12px;
  font-family: var(--tnzi-ai-font-mono, ui-monospace, monospace);
}
.playground-shell__topbar-btn:hover {
  background: var(--tnzi-ai-hover, #f3f4f6);
}

.playground-shell__showcase {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
  max-width: 720px;
  margin: 16px auto 0;
  font-size: 11px;
  color: var(--tnzi-ai-text-tertiary, #6b7280);
}
.playground-shell__showcase-label {
  letter-spacing: 0.02em;
  margin-right: 4px;
}
.playground-shell__showcase-tag {
  padding: 2px 8px;
  border: 1px solid var(--tnzi-ai-border, #e5e7eb);
  border-radius: 999px;
  font-family: var(--tnzi-ai-font-mono, ui-monospace, monospace);
}

.playground-shell__section {
  padding: 8px 0 16px;
}
.playground-shell__section-label {
  font-size: 13px;
  font-weight: 500;
  margin-bottom: 12px;
}
.playground-shell__section-text {
  font-size: 13px;
  line-height: 1.6;
  opacity: 0.8;
  margin: 0;
}
.playground-shell__section-text code {
  padding: 2px 6px;
  border-radius: 4px;
  background: var(--tnzi-ai-surface, #f3f4f6);
  font-family: var(--tnzi-ai-font-mono, ui-monospace, monospace);
  font-size: 12px;
}

.playground-shell__theme-row {
  display: flex;
  gap: 8px;
}
.playground-shell__theme-pill {
  padding: 6px 14px;
  border: 1px solid var(--tnzi-ai-border, #e5e7eb);
  border-radius: 999px;
  background: transparent;
  cursor: pointer;
  color: inherit;
  font-size: 12px;
}
.playground-shell__theme-pill.is-active {
  border-color: var(--tnzi-ai-accent, #3b82f6);
  background: var(--tnzi-ai-accent-soft, rgba(59, 130, 246, 0.1));
}
</style>
