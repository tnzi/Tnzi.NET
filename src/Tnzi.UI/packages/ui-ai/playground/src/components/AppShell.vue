<script setup lang="ts">
/**
 * Tnzi UI-AI playground shell — Manus-inspired layout.
 *
 * Composes TCollapsibleSidebar / TCommandPalette / TSettingsDialog into a
 * three-pane agent-product shell: expanded 300px sidebar with brand +
 * main nav + Projects section + All tasks list + promo footer; main area
 * with transparent top bar and a serif-headline empty state containing a
 * composer and suggestion chips. When a scenario is selected the main area
 * plays it through the MockChatEngine (thread left, artifact right).
 */
import { computed, ref, watch, onBeforeUnmount, onMounted, nextTick, shallowRef } from 'vue'
import { Icon } from '@iconify/vue'
import {
  TCollapsibleSidebar,
  TCommandPalette,
  TSettingsDialog,
  TLandingPage,
  useSidebarState,
  type SettingsSection,
} from '@tnzi/ui-ai/shell'
import {
  TReasoningStage,
  TStatusBanner,
  TTaskDoneRow,
  TFollowUpList,
  TUpgradeBanner,
  TThreadComposer,
  TArtifactPanel,
} from '@tnzi/ui-ai/components'
import { applyAiTheme, lightTokens, darkTokens } from '@tnzi/ui-ai/themes'
import { usePlaygroundStore } from '../state/playground-store'
import { useCommandActions } from '../state/actions'
import { createMockChatEngine, type MockChatEngine } from '../mock/engine'

const store = usePlaygroundStore()
const actions = useCommandActions()

const { mode } = useSidebarState({
  initialMode: store.sidebarMode.value,
  storageKey: null,
})

watch(mode, (val) => { store.sidebarMode.value = val })
watch(() => store.sidebarMode.value, (val) => { if (val !== mode.value) mode.value = val })

watch(
  () => store.theme.value,
  (val) => {
    applyAiTheme(val === 'dark' ? darkTokens : lightTokens)
    document.documentElement.classList.toggle('dark', val === 'dark')
  },
  { immediate: true },
)

const engine = shallowRef<MockChatEngine | null>(null)

watch(
  () => store.currentScenario.value,
  (next) => {
    if (engine.value) {
      engine.value.dispose()
      engine.value = null
    }
    if (!next) return
    const nextEngine = createMockChatEngine(next)
    engine.value = nextEngine
    nextTick(() => nextEngine.controls.play())
  },
  { immediate: true },
)

onBeforeUnmount(() => { engine.value?.dispose() })

function closeMenusOnDocumentClick(): void {
  if (projectMenuOpen.value || filterMenuOpen.value || attachMenu.value || toolsMenu.value) {
    projectMenuOpen.value = false
    filterMenuOpen.value = false
    attachMenu.value = false
    toolsMenu.value = false
  }
}
onMounted(() => {
  document.addEventListener('click', closeMenusOnDocumentClick)
})
onBeforeUnmount(() => {
  document.removeEventListener('click', closeMenusOnDocumentClick)
})

// Settings sections — consumer-configurable via slot pattern (pass the
// array + a slot for each section id). The list below mirrors what a
// Tnzi.AI-powered product is likely to surface; add or remove entries
// to match the features the current backend exposes.
const sections: readonly SettingsSection[] = [
  { id: 'account',         label: 'Account',         icon: 'lucide:circle-user-round' },
  { id: 'appearance',      label: 'Settings',        icon: 'lucide:settings' },
  { id: 'usage',           label: 'Usage',           icon: 'lucide:gauge' },
  { id: 'skills',          label: 'Skills',          icon: 'lucide:wand-sparkles' },
  { id: 'personalization', label: 'Personalization', icon: 'lucide:user-round-cog' },
  { id: 'memory',          label: 'Memory',          icon: 'lucide:brain' },
  { id: 'data',            label: 'Data controls',   icon: 'lucide:database' },
  { id: 'connectors',      label: 'Connectors',      icon: 'lucide:plug-2' },
  { id: 'about',           label: 'About',           icon: 'lucide:info' },
]

/** Which main view is currently shown. "home" is the empty-state greeting
 * when no scenario is active; scenario selection supersedes view state. */
type MainView = 'home' | 'agent' | 'library'
const view = ref<MainView>('home')

function goHome(): void {
  view.value = 'home'
  store.currentScenarioId.value = ''
}
function goAgent(): void {
  view.value = 'agent'
  store.currentScenarioId.value = ''
}
function goLibrary(): void {
  view.value = 'library'
  store.currentScenarioId.value = ''
}

/** Main nav items in the expanded sidebar. Icon-only versions sit in the rail. */
const mainNav = [
  { id: 'new', label: 'New task', icon: 'lucide:square-pen', action: goHome },
  { id: 'agent', label: 'Agent', icon: 'lucide:bot-message-square', action: goAgent },
  { id: 'search', label: 'Search', icon: 'lucide:search', action: () => { store.commandPaletteOpen.value = true } },
  { id: 'library', label: 'Library', icon: 'lucide:library', action: goLibrary },
] as const

// When user picks a scenario from the sidebar list, leave view state as-is
// (scenario playback renders regardless). goHome/goAgent/goLibrary clear it.

const isMobile = ref(false)

function onToggleSidebar(): void {
  if (mode.value === 'expanded') mode.value = 'icon'
  else if (mode.value === 'icon') mode.value = 'hidden'
  else mode.value = 'expanded'
}

const currentTitle = computed(() => {
  if (store.currentScenario.value) return store.currentScenario.value.meta.title
  if (view.value === 'agent') return 'Agent'
  if (view.value === 'library') return 'Library'
  return 'Tnzi 1.0 Lite'
})
const scenarios = computed(() => store.scenarioIndex.scenarios)
const hasScenario = computed(() => !!engine.value && !!store.currentScenario.value)

// Settings — Appearance picker tiles + communication preferences toggles.
type ThemePref = 'light' | 'dark' | 'system'
const themePref = ref<ThemePref>(store.theme.value === 'dark' ? 'dark' : 'light')
const themeOptions: readonly { id: ThemePref; label: string }[] = [
  { id: 'light', label: 'Light' },
  { id: 'dark', label: 'Dark' },
  { id: 'system', label: 'Follow System' },
]
function pickTheme(next: ThemePref): void {
  themePref.value = next
  if (next === 'system') {
    const prefersDark = typeof window !== 'undefined'
      && window.matchMedia?.('(prefers-color-scheme: dark)').matches
    store.theme.value = prefersDark ? 'dark' : 'light'
  } else {
    store.theme.value = next
  }
}

const commsPrefs = ref({
  productUpdates: true,
  queuedTaskStarted: true,
  weeklyDigest: false,
})

const languageOptions = [
  { id: 'en', label: 'English' },
  { id: 'zh-cn', label: '中文' },
] as const

// Projects + button popover + All tasks filter popover (simple dropdowns).
const projectMenuOpen = ref(false)
const filterMenuOpen = ref(false)

// Tooltips for the collapsed rail — shown on hover via a single
// computed ref so we can render one floating label instead of HTML title.
const railTooltip = ref<{ y: number; label: string } | null>(null)
function showRailTooltip(label: string, ev: MouseEvent): void {
  const el = ev.currentTarget as HTMLElement
  const rect = el.getBoundingClientRect()
  railTooltip.value = { y: Math.round(rect.top + rect.height / 2), label }
}
function hideRailTooltip(): void {
  railTooltip.value = null
}

// Hover-triggered rail popovers (Manus-style). A shared timer stays open
// briefly after mouseleave so the cursor can travel onto the popover
// itself without flicker.
const railPopoverOpen = ref<null | 'tasks'>(null)
const railPopoverY = ref(0)
let railPopoverTimer: number | null = null
function showRailPopover(kind: 'tasks', ev: MouseEvent): void {
  if (railPopoverTimer != null) {
    clearTimeout(railPopoverTimer)
    railPopoverTimer = null
  }
  const el = ev.currentTarget as HTMLElement
  const rect = el.getBoundingClientRect()
  railPopoverY.value = Math.round(rect.top)
  railPopoverOpen.value = kind
  hideRailTooltip()
}
function scheduleHideRailPopover(): void {
  if (railPopoverTimer != null) clearTimeout(railPopoverTimer)
  railPopoverTimer = window.setTimeout(() => {
    railPopoverOpen.value = null
    railPopoverTimer = null
  }, 180)
}
function cancelHideRailPopover(): void {
  if (railPopoverTimer != null) {
    clearTimeout(railPopoverTimer)
    railPopoverTimer = null
  }
}

// Message thread interactivity — per-message feedback and reasoning disclosure.
const messageFeedback = ref<Record<string, number>>({})
const copiedId = ref<string | null>(null)
async function copyMessage(id: string, content: string): Promise<void> {
  try {
    await navigator.clipboard.writeText(content)
    copiedId.value = id
    setTimeout(() => {
      if (copiedId.value === id) copiedId.value = null
    }, 1500)
  } catch {
    // ignore — clipboard may be unavailable in sandboxed contexts
  }
}

const agentFeatures = [
  { icon: 'lucide:id-card', title: 'Brand-consistent AI identity', desc: 'Trained on your workflows, integrated with your tools.' },
  { icon: 'lucide:monitor-smartphone', title: 'Persistent memory & computer', desc: '24/7 cloud assistant that keeps full context and memory.' },
  { icon: 'lucide:wand-sparkles', title: 'Custom skills', desc: 'Equip your assistant with expert knowledge in specific areas.' },
  { icon: 'lucide:messages-square', title: 'Works in your messenger', desc: 'Available on Telegram, Line, and Slack. More coming soon.' },
] as const

const composerText = ref('')
const upgradeBannerDismissed = ref(false)

/* Artifact panel state. `view` and `width` both bind two-way to
   TArtifactPanel via v-model. Width is persisted to localStorage so
   the user-chosen size survives page reloads — matches Manus where
   the artifact panel remembers its width across sessions. */
const ARTIFACT_WIDTH_KEY = 'tnzi-ai-playground:artifact-width'
const artifactView = ref<'preview' | 'code' | 'history'>('code')
const artifactWidth = ref<number>(
  (() => {
    if (typeof localStorage === 'undefined') return 520
    const stored = Number(localStorage.getItem(ARTIFACT_WIDTH_KEY))
    return Number.isFinite(stored) && stored >= 320 ? stored : 520
  })(),
)
watch(artifactWidth, (v) => {
  if (typeof localStorage !== 'undefined') {
    localStorage.setItem(ARTIFACT_WIDTH_KEY, String(v))
  }
})
const artifactActiveFile = ref<string>('')
const artifactPreviewDevice = ref<'desktop' | 'tablet' | 'mobile'>('desktop')

/* Mock preview URL so the Preview tab demonstrates the new mini-browser
   chrome. In a real app this would point at the artifact's runtime
   sandbox. We point at vitepress.dev as a stand-in; iframes can render
   any same-origin or CORS-friendly site. */
const demoPreviewUrl = computed<string>(() =>
  store.currentScenarioId.value === '04-artifact-todo'
    ? 'https://vitepress.dev/'
    : '',
)

/* Demo project: a multi-file mock used as `files` prop on TArtifactPanel
   when scenario 04-artifact-todo is active. Exercises both the file
   tree (nested folders) and the multi-file selection flow without
   modifying the scenario events array. */
const demoProjectFiles = computed(() => {
  if (store.currentScenarioId.value !== '04-artifact-todo') return []
  return [
    {
      path: 'src/components/Todo.vue',
      content: engine.value?.state.artifact.value?.content ?? '',
    },
    {
      path: 'src/components/TodoItem.vue',
      content: `<script setup lang="ts">\ndefineProps<{ text: string; done: boolean }>()\n<\/script>\n\n<template>\n  <li :class="{ done }">{{ text }}<\/li>\n<\/template>\n`,
    },
    {
      path: 'src/composables/useLocalStorage.ts',
      content: `import { ref, watch } from 'vue'\n\nexport function useLocalStorage<T>(key: string, fallback: T) {\n  const stored = localStorage.getItem(key)\n  const value = ref<T>(stored ? JSON.parse(stored) : fallback)\n  watch(value, (v) => localStorage.setItem(key, JSON.stringify(v)), { deep: true })\n  return value\n}\n`,
    },
    {
      path: 'src/main.ts',
      content: `import { createApp } from 'vue'\nimport Todo from './components/Todo.vue'\n\ncreateApp(Todo).mount('#app')\n`,
    },
    {
      path: 'src/styles.css',
      content: `body {\n  margin: 0;\n  font-family: system-ui, sans-serif;\n}\n.done {\n  text-decoration: line-through;\n  color: #999;\n}\n`,
    },
    {
      path: 'public/favicon.svg',
      content: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32"><circle cx="16" cy="16" r="14" fill="#42b883"/><\/svg>\n`,
    },
    {
      path: 'package.json',
      content: `{\n  "name": "todo-demo",\n  "private": true,\n  "version": "0.0.0",\n  "type": "module",\n  "scripts": {\n    "dev": "vite",\n    "build": "vite build"\n  },\n  "dependencies": {\n    "vue": "^3.4.0"\n  }\n}\n`,
    },
    {
      path: 'tsconfig.json',
      content: `{\n  "compilerOptions": {\n    "target": "ES2020",\n    "module": "ESNext",\n    "moduleResolution": "Bundler",\n    "strict": true,\n    "jsx": "preserve"\n  },\n  "include": ["src"]\n}\n`,
    },
  ]
})

/** Per-scenario follow-up prompts surfaced after a task completes.
 *  Keyed by scenario id so adding new scenarios just means extending the
 *  object — no template changes needed. Empty array means "no suggestions
 *  for this scenario", which hides the whole `.suggestions` block. */
const followUpsByScenario: Record<string, readonly string[]> = {
  '04-artifact-todo': [
    'Add drag-and-drop reordering with a visual drop indicator.',
    'Persist todos to localStorage so they survive a page refresh.',
    'Split the component into TodoList / TodoItem / TodoInput so it scales.',
  ],
  '02-deep-reasoning': [
    'Walk through the same problem with a different substitution strategy.',
    'Add a verification step showing a second algebraic approach.',
  ],
}
const scenarioFollowUps = computed<readonly string[]>(() => {
  const id = store.currentScenarioId.value ?? ''
  return followUpsByScenario[id] ?? []
})

const suggestionChips = [
  { id: 'slides',  icon: 'lucide:presentation', label: 'Create slides',        prompt: 'Help me build a slide deck about ' },
  { id: 'website', icon: 'lucide:globe',        label: 'Build website',        prompt: 'Build me a landing page for ' },
  { id: 'desktop', icon: 'lucide:monitor',      label: 'Develop desktop apps', prompt: 'Scaffold a desktop app that ' },
  { id: 'design',  icon: 'lucide:palette',      label: 'Design',               prompt: 'Design a visual system for ' },
  { id: 'more',    icon: '',                    label: 'More' },
]

// Composer action buttons — realistic preset menus. Each action is a dropdown
// so there's something to click; consumers can pass their own set of tools.
const attachMenu = ref(false)
const toolsMenu = ref(false)
function closeComposerMenus(): void {
  attachMenu.value = false
  toolsMenu.value = false
}
const attachOptions = [
  { icon: 'lucide:file',       label: 'Upload from computer' },
  { icon: 'lucide:image',      label: 'Upload image' },
  { icon: 'lucide:github',     label: 'Connect repository' },
  { icon: 'lucide:link',       label: 'Paste link' },
  { icon: 'lucide:folder',     label: 'Pick from project' },
] as const
const toolsOptions = [
  { icon: 'lucide:globe',          label: 'Web search',    on: true  },
  { icon: 'lucide:database',       label: 'Knowledge base', on: false },
  { icon: 'lucide:terminal',       label: 'Shell',         on: true  },
  { icon: 'lucide:code',           label: 'Code interpreter', on: true },
  { icon: 'lucide:image-plus',     label: 'Image generator', on: false },
] as const
</script>

<template>
  <div class="app-shell">
    <TCollapsibleSidebar
      v-model="mode"
      :width="300"
      :rail-width="52"
      :is-mobile="isMobile"
    >
      <!-- Brand lockup: logo + wordmark + collapse toggle (expanded only).
           In collapsed/rail mode the brand mark alone lives in the #rail
           slot and doubles as the expand trigger via a hover swap. -->
      <template #header>
        <div class="brand">
          <div class="brand-mark" aria-hidden="true">
            <!-- Stylized T-monogram: a horizontal bar sitting on a vertical
                 stem, rendered as a single <path> with stroke-linecap round.
                 Reads as a brand mark rather than a decorative icon. -->
            <svg
              class="brand-mark-svg"
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
          </div>
          <strong class="brand-name">Tnzi AI</strong>
          <button
            type="button"
            class="brand-collapse"
            aria-label="Collapse sidebar"
            @click="mode = 'icon'"
          >
            <Icon icon="lucide:panel-left" />
          </button>
        </div>
      </template>

      <!-- Expanded content: main nav + Projects + All tasks + promo + footer -->
      <template #content>
        <nav class="nav-main">
          <button
            v-for="item in mainNav"
            :key="item.id"
            type="button"
            class="nav-item"
            :class="{
              'is-active':
                (item.id === 'new' && view === 'home' && !store.currentScenarioId.value) ||
                (item.id === 'agent' && view === 'agent') ||
                (item.id === 'library' && view === 'library')
            }"
            @click="item.action"
          >
            <Icon :icon="item.icon" class="nav-icon" />
            <span>{{ item.label }}</span>
          </button>
        </nav>

        <div class="section">
          <div class="section-head">
            <span>Projects</span>
            <div class="section-act-wrap">
              <button
                type="button"
                class="section-act"
                aria-label="New project"
                @click.stop="projectMenuOpen = !projectMenuOpen; filterMenuOpen = false"
              >
                <Icon icon="lucide:plus" />
              </button>
              <div v-if="projectMenuOpen" class="section-menu" @click.stop>
                <button type="button" class="section-menu-item" @click="projectMenuOpen = false">
                  <Icon icon="lucide:folder-plus" />
                  <span>New project</span>
                </button>
                <button type="button" class="section-menu-item" @click="projectMenuOpen = false">
                  <Icon icon="lucide:folder-up" />
                  <span>Import folder</span>
                </button>
                <div class="section-menu-sep" />
                <button type="button" class="section-menu-item" @click="projectMenuOpen = false">
                  <Icon icon="lucide:book-open" />
                  <span>Browse templates</span>
                </button>
              </div>
            </div>
          </div>
          <button type="button" class="project-row">
            <Icon icon="lucide:folder" class="project-icon" />
            <span>Playground</span>
          </button>
        </div>

        <div class="section section--tasks">
          <div class="section-head">
            <span>All tasks</span>
            <div class="section-act-wrap">
              <button
                type="button"
                class="section-act"
                aria-label="Filter"
                @click.stop="filterMenuOpen = !filterMenuOpen; projectMenuOpen = false"
              >
                <Icon icon="lucide:list-filter" />
              </button>
              <div v-if="filterMenuOpen" class="section-menu" @click.stop>
                <button type="button" class="section-menu-item" @click="filterMenuOpen = false">
                  <Icon icon="lucide:check" class="section-menu-check" />
                  <span>All scenarios</span>
                </button>
                <button type="button" class="section-menu-item" @click="filterMenuOpen = false">
                  <Icon icon="lucide:circle-play" />
                  <span>Conversation only</span>
                </button>
                <button type="button" class="section-menu-item" @click="filterMenuOpen = false">
                  <Icon icon="lucide:file-code-2" />
                  <span>With artifact</span>
                </button>
                <div class="section-menu-sep" />
                <button type="button" class="section-menu-item" @click="filterMenuOpen = false">
                  <Icon icon="lucide:calendar" />
                  <span>Recent</span>
                </button>
              </div>
            </div>
          </div>
          <button
            v-for="scenario in scenarios"
            :key="scenario.meta.id"
            type="button"
            class="task-row"
            :class="{ 'is-active': scenario.meta.id === store.currentScenarioId.value }"
            @click="store.selectScenario(scenario.meta.id)"
          >
            <Icon :icon="scenario.meta.icon" class="task-icon" />
            <span class="task-title">{{ scenario.meta.title }}</span>
          </button>
        </div>
      </template>

      <!-- Footer: promo card + utility icon row -->
      <template #footer>
        <div class="promo">
          <div class="promo-body">
            <div class="promo-title">Share Tnzi with a friend</div>
            <div class="promo-sub">Get 500 credits each</div>
          </div>
          <Icon icon="lucide:chevron-right" class="promo-arrow" />
        </div>
        <div class="foot-icons">
          <button
            type="button"
            class="foot-btn"
            aria-label="Settings"
            @click="store.settingsOpen.value = true"
          >
            <Icon icon="lucide:settings-2" />
          </button>
          <button type="button" class="foot-btn" aria-label="Apps">
            <Icon icon="lucide:grid-2x2" />
          </button>
          <button type="button" class="foot-btn" aria-label="Connectors">
            <Icon icon="lucide:plug-2" />
          </button>
        </div>
      </template>

      <!-- Collapsed rail: brand mark at top doubles as expand button
           (hover swaps the logo icon for a panel-left icon). -->
      <template #rail>
        <div class="rail">
          <button
            type="button"
            class="rail-brand rail-brand--button"
            aria-label="Open sidebar"
            @click="mode = 'expanded'"
            @mouseenter="(e) => showRailTooltip('Open sidebar', e)"
            @mouseleave="hideRailTooltip"
          >
            <!-- Same T-monogram as the expanded header, inside the rail's
                 dark square. Hovering anywhere on the rail swaps it for the
                 panel-left icon — the rail-brand--button rules below handle
                 the cross-fade. -->
            <span class="rail-brand-logo rail-brand-monogram" aria-hidden="true">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
                <path d="M5 6 L19 6" />
                <path d="M12 6 L12 19" />
                <circle cx="12" cy="19" r="1.4" fill="currentColor" stroke="none" />
              </svg>
            </span>
            <Icon icon="lucide:panel-left-open" class="rail-brand-expand" />
          </button>

          <div class="rail-group rail-group--top">
            <button
              v-for="item in mainNav"
              :key="item.id"
              type="button"
              class="rail-btn"
              :class="{ 'is-active': (item.id === 'new' && view === 'home' && !store.currentScenarioId.value)
                      || (item.id === 'agent' && view === 'agent')
                      || (item.id === 'library' && view === 'library') }"
              :aria-label="item.label"
              @click="item.action"
              @mouseenter="(e) => showRailTooltip(item.label, e)"
              @mouseleave="hideRailTooltip"
            >
              <Icon :icon="item.icon" />
            </button>
            <button
              type="button"
              class="rail-btn"
              aria-label="Tasks"
              @mouseenter="(e) => showRailPopover('tasks', e)"
              @mouseleave="scheduleHideRailPopover"
            >
              <Icon icon="lucide:list-checks" />
            </button>
          </div>

          <div class="rail-group rail-group--bottom">
            <button
              type="button"
              class="rail-btn"
              aria-label="Apps"
              @mouseenter="(e) => showRailTooltip('Apps', e)"
              @mouseleave="hideRailTooltip"
            >
              <Icon icon="lucide:grid-2x2" />
            </button>
            <button
              type="button"
              class="rail-btn"
              aria-label="Connectors"
              @mouseenter="(e) => showRailTooltip('Connectors', e)"
              @mouseleave="hideRailTooltip"
            >
              <Icon icon="lucide:plug-2" />
            </button>
            <button
              type="button"
              class="rail-btn"
              aria-label="Settings"
              @click="store.settingsOpen.value = true"
              @mouseenter="(e) => showRailTooltip('Settings', e)"
              @mouseleave="hideRailTooltip"
            >
              <Icon icon="lucide:settings-2" />
            </button>
          </div>
        </div>

        <!-- Floating tooltip for the collapsed rail -->
        <div
          v-if="railTooltip && mode === 'icon' && !railPopoverOpen"
          class="rail-tooltip"
          :style="{ top: `${railTooltip.y}px` }"
          role="tooltip"
        >
          {{ railTooltip.label }}
        </div>

        <!-- Hover-triggered popover: recent tasks list -->
        <div
          v-if="railPopoverOpen === 'tasks' && mode === 'icon'"
          class="rail-popover"
          :style="{ top: `${railPopoverY}px` }"
          @mouseenter="cancelHideRailPopover"
          @mouseleave="scheduleHideRailPopover"
        >
          <div class="rail-popover-head">Recent tasks</div>
          <button
            v-for="s in scenarios.slice(0, 8)"
            :key="s.meta.id"
            type="button"
            class="rail-popover-item"
            @click="store.selectScenario(s.meta.id); railPopoverOpen = null"
          >
            <Icon :icon="s.meta.icon" class="rail-popover-icon" />
            <span>{{ s.meta.title }}</span>
          </button>
        </div>
      </template>
    </TCollapsibleSidebar>

    <main class="main">
      <!-- Transparent top bar: workspace switcher left, action cluster right -->
      <header class="topbar">
        <!-- Topbar-mounted expand button is only for the fully-hidden drawer
             mode (mobile). In `icon` rail mode, the logo inside the rail owns
             the expand affordance (hover-swap to panel-left icon) — matches
             Manus's pattern where the collapsed nav has no separate topbar
             toggle. -->
        <button
          v-if="mode === 'hidden'"
          type="button"
          class="topbar-toggle"
          aria-label="Show sidebar"
          @click="mode = 'expanded'"
        >
          <Icon icon="lucide:panel-left-open" />
        </button>

        <button type="button" class="workspace-switcher">
          <span>{{ currentTitle }}</span>
          <Icon icon="lucide:chevron-down" />
        </button>

        <div class="topbar-spacer" />

        <div v-if="hasScenario" class="topbar-right">
          <button type="button" class="top-btn top-btn--share">
            <Icon icon="lucide:share-2" />
            <span>Share</span>
          </button>
          <button type="button" class="top-btn" aria-label="More">
            <Icon icon="lucide:more-horizontal" />
          </button>
          <button type="button" class="top-btn top-btn--publish">
            <Icon icon="lucide:upload" />
            <span>Publish</span>
          </button>
        </div>
        <div v-else class="topbar-right">
          <button type="button" class="top-btn" aria-label="Notifications">
            <Icon icon="lucide:bell" />
          </button>
          <div class="credit-badge">
            <Icon icon="lucide:sparkles" />
            <span>300</span>
          </div>
          <div class="avatar" aria-label="Account" />
        </div>
      </header>

      <!-- Agent upsell view: hero illustration + 4-up feature grid + CTA -->
      <section v-if="!hasScenario && view === 'agent'" class="agent-view">
        <div class="agent-hero">
          <div class="agent-hero-phone">
            <Icon icon="lucide:sparkles" class="agent-hero-bubble" />
            <div class="agent-hero-ring" aria-hidden="true">
              <span class="hero-orb hero-orb--tg"><Icon icon="lucide:send" /></span>
              <span class="hero-orb hero-orb--ms"><Icon icon="lucide:message-circle" /></span>
              <span class="hero-orb hero-orb--ln"><Icon icon="lucide:phone" /></span>
              <span class="hero-orb hero-orb--wa"><Icon icon="lucide:message-square-text" /></span>
              <span class="hero-orb hero-orb--sl"><Icon icon="lucide:hash" /></span>
            </div>
          </div>
        </div>
        <h1 class="agent-title t-ai-display md">Deploy your agent for business</h1>
        <div class="agent-grid">
          <div v-for="f in agentFeatures" :key="f.title" class="agent-card">
            <div class="agent-card-icon">
              <Icon :icon="f.icon" />
            </div>
            <div class="agent-card-title">{{ f.title }}</div>
            <div class="agent-card-desc">{{ f.desc }}</div>
          </div>
        </div>
        <button type="button" class="agent-cta">
          <Icon icon="lucide:rocket" />
          <span>Get started</span>
        </button>
      </section>

      <!-- Library view: filter bar + grouped artifact cards (empty stub) -->
      <section v-else-if="!hasScenario && view === 'library'" class="library-view">
        <h1 class="library-title t-ai-display md">Library</h1>
        <div class="library-toolbar">
          <div class="lib-left">
            <button type="button" class="lib-filter">
              <Icon icon="lucide:list-filter" />
              <span>All</span>
              <Icon icon="lucide:chevron-down" />
            </button>
            <button type="button" class="lib-filter">
              <Icon icon="lucide:star" />
              <span>My favorites</span>
            </button>
          </div>
          <div class="lib-right">
            <div class="lib-search">
              <Icon icon="lucide:search" />
              <input type="text" placeholder="Search files" />
            </div>
            <div class="lib-view-toggle">
              <button type="button" class="vtoggle is-active" aria-label="Grid view"><Icon icon="lucide:grid-2x2" /></button>
              <button type="button" class="vtoggle" aria-label="List view"><Icon icon="lucide:list" /></button>
            </div>
          </div>
        </div>
        <div class="library-empty">
          <Icon icon="lucide:folder-open" />
          <div class="library-empty-title">No artifacts yet</div>
          <div class="library-empty-sub">Run a task from the sidebar to populate your library.</div>
        </div>
      </section>

      <!-- Empty state rendered via TLandingPage shell component.
           Consumers can customize every region through the slots below. -->
      <TLandingPage
        v-else-if="!hasScenario"
        v-model="composerText"
        :chips="suggestionChips"
        @submit="() => { /* stubbed — real send would trigger a scenario */ }"
        @chip-click="(chip) => { if (chip.id === 'more') return; composerText = chip.prompt ?? composerText }"
      >
        <template #plan>
          <div class="plan-pill">
            <span class="plan-label">Free plan</span>
            <span class="plan-sep" aria-hidden="true">·</span>
            <a href="#" class="plan-link" @click.prevent>Start free trial</a>
          </div>
        </template>

        <template #composer-left>
          <!-- Attach menu (+ button) -->
          <div class="comp-menu-wrap">
            <button
              type="button"
              class="comp-btn"
              aria-label="Attach"
              @click.stop="attachMenu = !attachMenu; toolsMenu = false"
            >
              <Icon icon="lucide:plus" />
            </button>
            <div v-if="attachMenu" class="comp-menu" @click.stop>
              <button
                v-for="opt in attachOptions"
                :key="opt.label"
                type="button"
                class="comp-menu-item"
                @click="attachMenu = false"
              >
                <Icon :icon="opt.icon" />
                <span>{{ opt.label }}</span>
              </button>
            </div>
          </div>

          <!-- Tools menu (wrench button) -->
          <div class="comp-menu-wrap">
            <button
              type="button"
              class="comp-btn"
              aria-label="Tools"
              @click.stop="toolsMenu = !toolsMenu; attachMenu = false"
            >
              <Icon icon="lucide:wrench" />
              <span class="comp-btn-badge">{{ toolsOptions.filter(t => t.on).length }}</span>
            </button>
            <div v-if="toolsMenu" class="comp-menu comp-menu--wide" @click.stop>
              <div class="comp-menu-head">Enabled tools</div>
              <label
                v-for="opt in toolsOptions"
                :key="opt.label"
                class="comp-menu-item comp-menu-item--toggle"
              >
                <Icon :icon="opt.icon" />
                <span class="comp-menu-label">{{ opt.label }}</span>
                <span class="s-toggle" :class="{ 'is-on': opt.on }">
                  <input
                    type="checkbox"
                    :checked="opt.on"
                    @change="(e) => { /* demo only */ }"
                  />
                  <span class="s-toggle-knob" />
                </span>
              </label>
            </div>
          </div>

          <button type="button" class="comp-btn" aria-label="Computer">
            <Icon icon="lucide:monitor" />
          </button>
        </template>

        <template #composer-right>
          <button type="button" class="comp-btn" aria-label="Dictate">
            <Icon icon="lucide:mic" />
          </button>
        </template>
      </TLandingPage>

      <!-- Running scenario: chat thread + optional artifact pane -->
      <section v-else class="workspace" :class="{ 'workspace--has-artifact': !!engine!.state.artifact.value }">
        <div class="thread">
          <template v-for="msg in engine!.state.messages.value" :key="msg.id">
            <div class="msg" :class="`msg--${msg.role}`">
              <div v-if="msg.role === 'user'" class="msg-bubble msg-bubble--user">
                {{ msg.content }}
              </div>

              <template v-else>
                <div class="msg-role">
                  <span class="msg-brand">
                    <Icon icon="lucide:sparkles" />
                  </span>
                  <strong>{{ msg.agentName || 'Tnzi' }}</strong>
                  <span class="msg-lite">Lite</span>
                  <span v-if="msg.isStreaming" class="msg-streaming" aria-label="streaming">●</span>
                </div>
                <!-- Reasoning stage is now a reusable component published
                     under @tnzi/ui-ai/components. Playground passes the
                     streaming flag through so the status-circle animates
                     while reasoning is still being generated. -->
                <TReasoningStage
                  v-if="msg.reasoning"
                  :status="msg.isStreaming ? 'running' : 'done'"
                >
                  {{ msg.reasoning }}
                </TReasoningStage>
                <div class="msg-body">{{ msg.content }}</div>
                <div v-if="msg.citations?.length" class="msg-citations">
                  <div v-for="c in msg.citations" :key="c.id" class="citation">
                    <Icon icon="lucide:bookmark" class="citation-icon" />
                    <div class="citation-body">
                      <strong>{{ c.title }}</strong>
                      <span>{{ c.snippet }}</span>
                    </div>
                  </div>
                </div>
                <div v-if="!msg.isStreaming" class="msg-actions">
                  <button
                    type="button"
                    class="msg-action"
                    :aria-label="copiedId === msg.id ? 'Copied' : 'Copy'"
                    @click="copyMessage(msg.id, msg.content)"
                  >
                    <Icon :icon="copiedId === msg.id ? 'lucide:check' : 'lucide:copy'" />
                  </button>
                  <button type="button" class="msg-action msg-action--primary">
                    <Icon icon="lucide:bot" />
                    <span>Start agent</span>
                  </button>
                  <button type="button" class="msg-action">
                    <Icon icon="lucide:sparkles" />
                    <span>Create</span>
                    <Icon icon="lucide:chevron-down" />
                  </button>
                </div>
              </template>
            </div>
          </template>

          <!-- Stopped banner now uses the reusable TStatusBanner component
               from @tnzi/ui-ai/components. Variant `stopped` picks the
               amber palette + octagon-pause icon automatically. -->
          <TStatusBanner
            v-if="engine!.state.playbackState.value === 'done'
                  && store.currentScenarioId.value === '02-deep-reasoning'"
            variant="stopped"
            label="Tnzi has stopped — send a new message to continue"
          />

          <!-- Post-task UX: task-done row + follow-up suggestions + optional
               upgrade CTA. All three are reusable @tnzi/ui-ai/components —
               the playground just wires in scenario-specific data. -->
          <TTaskDoneRow
            v-if="engine!.state.playbackState.value === 'done'"
            :rating="messageFeedback['last'] ?? 0"
            @update:rating="(n) => messageFeedback['last'] = n"
          />

          <TFollowUpList
            v-if="engine!.state.playbackState.value === 'done' && scenarioFollowUps.length > 0"
            :items="scenarioFollowUps"
            @select="(text) => composerText = text"
          />

          <TUpgradeBanner
            v-if="engine!.state.playbackState.value === 'done'
                  && store.currentScenarioId.value === '04-artifact-todo'
                  && !upgradeBannerDismissed"
            cta-label="Upgrade"
            @cta="() => { /* would open upgrade flow */ }"
            @dismiss="upgradeBannerDismissed = true"
          >
            Need more intelligence? Switch to <strong>Tnzi Max</strong> for complex tasks.
          </TUpgradeBanner>

          <!-- Sticky bottom composer is now a reusable component. Left
               and right button clusters are injected via slots so the
               playground keeps its scenario-specific attach / tools /
               computer / dictate buttons without leaking that shape
               into the component API. -->
          <TThreadComposer
            v-model="composerText"
            :placeholder="`Send message to ${currentTitle || 'Tnzi'}`"
            @send="(text) => { /* demo only — real app would push a user message */ composerText = '' }"
          >
            <template #left>
              <button type="button" class="comp-btn" aria-label="Attach">
                <Icon icon="lucide:plus" />
              </button>
              <button type="button" class="comp-btn" aria-label="Tools">
                <Icon icon="lucide:wrench" />
              </button>
              <button type="button" class="comp-btn" aria-label="Computer">
                <Icon icon="lucide:monitor" />
              </button>
            </template>
            <template #right>
              <button type="button" class="comp-btn" aria-label="Dictate">
                <Icon icon="lucide:mic" />
              </button>
            </template>
          </TThreadComposer>
        </div>

        <!-- Artifact panel — reusable @tnzi/ui-ai component with built-in
             Shiki, tabs, resizable width and project-mode file list.
             The playground passes `files` to demo multi-file mode (with
             the scenario's main artifact + 3 mock companion files) and
             two-way binds view + width + active-file. -->
        <TArtifactPanel
          v-if="engine!.state.artifact.value"
          v-model:view="artifactView"
          v-model:width="artifactWidth"
          v-model:active-file="artifactActiveFile"
          v-model:preview-device="artifactPreviewDevice"
          :files="demoProjectFiles"
          :artifact="engine!.state.artifact.value"
          :preview-url="demoPreviewUrl"
          class="artifact-slot"
          @preview:edit="() => { /* would jump to editor */ }"
          @preview:navigate="(url) => { /* would push iframe nav */ }"
        />
      </section>
    </main>

    <TCommandPalette v-model="store.commandPaletteOpen.value" :actions="actions" />

    <TSettingsDialog
      v-model="store.settingsOpen.value"
      v-model:active-section="store.settingsSection.value"
      :sections="sections"
    >
      <template #account>
        <div class="s-group">
          <div class="s-label">Profile</div>
          <div class="s-profile">
            <div class="s-profile-avatar" aria-hidden="true" />
            <div class="s-profile-info">
              <div class="s-profile-name">Tuan Zi</div>
              <div class="s-profile-email">tuanzi@tnzi.local</div>
              <div class="s-profile-tier">
                <span class="s-chip">Personal</span>
                <span class="s-chip s-chip--accent">Free plan</span>
              </div>
            </div>
            <button type="button" class="s-btn-secondary">Edit</button>
          </div>
        </div>

        <div class="s-divider" />

        <div class="s-group">
          <div class="s-label">Credentials</div>
          <div class="s-row">
            <div class="s-row-body">
              <div class="s-row-title">Password</div>
              <div class="s-row-desc">Last changed 14 days ago.</div>
            </div>
            <button type="button" class="s-btn-secondary">Change</button>
          </div>
          <div class="s-row">
            <div class="s-row-body">
              <div class="s-row-title">Two-factor authentication</div>
              <div class="s-row-desc">Add an extra layer of security when signing in.</div>
            </div>
            <span class="s-toggle is-on">
              <span class="s-toggle-knob" />
            </span>
          </div>
        </div>

        <div class="s-divider" />

        <div class="s-group">
          <div class="s-label">Danger zone</div>
          <div class="s-row">
            <div class="s-row-body">
              <div class="s-row-title">Delete account</div>
              <div class="s-row-desc">Permanently remove your account and all associated data.</div>
            </div>
            <button type="button" class="s-btn-danger">Delete</button>
          </div>
        </div>
      </template>

      <template #appearance>
        <div class="s-group">
          <div class="s-label">General</div>
          <div class="s-row s-row--stack">
            <div class="s-row-label">Language</div>
            <select
              class="s-select"
              :value="store.locale.value"
              @change="store.locale.value = ($event.target as HTMLSelectElement).value as 'en' | 'zh-cn'"
            >
              <option v-for="opt in languageOptions" :key="opt.id" :value="opt.id">{{ opt.label }}</option>
            </select>
          </div>

          <div class="s-row s-row--stack">
            <div class="s-row-label">Appearance</div>
            <div class="s-theme-tiles">
              <button
                v-for="opt in themeOptions"
                :key="opt.id"
                type="button"
                class="s-tile"
                :class="{ 'is-selected': themePref === opt.id }"
                @click="pickTheme(opt.id)"
              >
                <div class="s-tile-preview" :class="`s-tile-preview--${opt.id}`">
                  <span class="s-tile-bar s-tile-bar--lg" />
                  <span class="s-tile-bar s-tile-bar--md" />
                  <span class="s-tile-bar s-tile-bar--sm" />
                </div>
                <div class="s-tile-label">{{ opt.label }}</div>
              </button>
            </div>
          </div>
        </div>

        <div class="s-divider" />

        <div class="s-group">
          <div class="s-label">Communication preferences</div>
          <label class="s-row">
            <div class="s-row-body">
              <div class="s-row-title">Receive product updates</div>
              <div class="s-row-desc">Receive early access to feature releases and success stories to optimize your workflow.</div>
            </div>
            <span class="s-toggle" :class="{ 'is-on': commsPrefs.productUpdates }">
              <input
                type="checkbox"
                :checked="commsPrefs.productUpdates"
                @change="commsPrefs.productUpdates = !commsPrefs.productUpdates"
              />
              <span class="s-toggle-knob" />
            </span>
          </label>
          <label class="s-row">
            <div class="s-row-body">
              <div class="s-row-title">Email me when my queued task starts</div>
              <div class="s-row-desc">When enabled, we'll send you a timely email once your task finishes queuing and begins processing.</div>
            </div>
            <span class="s-toggle" :class="{ 'is-on': commsPrefs.queuedTaskStarted }">
              <input
                type="checkbox"
                :checked="commsPrefs.queuedTaskStarted"
                @change="commsPrefs.queuedTaskStarted = !commsPrefs.queuedTaskStarted"
              />
              <span class="s-toggle-knob" />
            </span>
          </label>
          <label class="s-row">
            <div class="s-row-body">
              <div class="s-row-title">Weekly digest</div>
              <div class="s-row-desc">A summary of your tasks, artifacts and token usage delivered every Monday morning.</div>
            </div>
            <span class="s-toggle" :class="{ 'is-on': commsPrefs.weeklyDigest }">
              <input
                type="checkbox"
                :checked="commsPrefs.weeklyDigest"
                @change="commsPrefs.weeklyDigest = !commsPrefs.weeklyDigest"
              />
              <span class="s-toggle-knob" />
            </span>
          </label>
        </div>

        <div class="s-divider" />

        <div class="s-row">
          <div class="s-row-body">
            <div class="s-row-title">Manage Cookies</div>
            <div class="s-row-desc">Review and change your cookie consent choices.</div>
          </div>
          <button type="button" class="s-btn-secondary">Manage</button>
        </div>
      </template>

      <template #usage>
        <div class="s-group">
          <div class="s-label">Credit balance</div>
          <div class="s-usage-card">
            <div class="s-usage-num">300<span> / 300</span></div>
            <div class="s-usage-bar"><span class="s-usage-fill" style="width: 100%" /></div>
            <div class="s-usage-meta">
              <span>Resets in 14 days</span>
              <button type="button" class="s-btn-secondary">Upgrade</button>
            </div>
          </div>
        </div>

        <div class="s-divider" />

        <div class="s-group">
          <div class="s-label">Token usage (this month)</div>
          <div class="s-metric-grid">
            <div class="s-metric">
              <div class="s-metric-label">Input tokens</div>
              <div class="s-metric-value">128,440</div>
            </div>
            <div class="s-metric">
              <div class="s-metric-label">Output tokens</div>
              <div class="s-metric-value">42,891</div>
            </div>
            <div class="s-metric">
              <div class="s-metric-label">Total cost</div>
              <div class="s-metric-value">$0.00</div>
              <div class="s-metric-hint">Mock data</div>
            </div>
            <div class="s-metric">
              <div class="s-metric-label">Tasks run</div>
              <div class="s-metric-value">37</div>
            </div>
          </div>
        </div>
      </template>

      <template #skills>
        <div class="s-group">
          <div class="s-label">Installed skills</div>
          <div class="s-skill-grid">
            <div
              v-for="skill in [
                { name: 'Web search',      icon: 'lucide:globe',         desc: 'Retrieve fresh information from the open web.', on: true },
                { name: 'Shell',           icon: 'lucide:terminal',      desc: 'Run shell commands in a sandboxed environment.', on: true },
                { name: 'Code interpreter',icon: 'lucide:code',          desc: 'Execute Python, Node, and shell snippets.',     on: true },
                { name: 'Image generator', icon: 'lucide:image-plus',    desc: 'Create images from a natural-language brief.',   on: false },
                { name: 'Document reader', icon: 'lucide:file-text',     desc: 'Extract text from PDFs, Word, and slides.',      on: false },
                { name: 'Browser',         icon: 'lucide:app-window',    desc: 'Drive a real Chromium instance.',                on: false },
              ]"
              :key="skill.name"
              class="s-skill-card"
              :class="{ 'is-on': skill.on }"
            >
              <div class="s-skill-head">
                <div class="s-skill-icon"><Icon :icon="skill.icon" /></div>
                <span class="s-toggle" :class="{ 'is-on': skill.on }">
                  <span class="s-toggle-knob" />
                </span>
              </div>
              <div class="s-skill-name">{{ skill.name }}</div>
              <div class="s-skill-desc">{{ skill.desc }}</div>
            </div>
          </div>
          <button type="button" class="s-btn-secondary s-btn-block">
            <Icon icon="lucide:plus" /> Browse skill store
          </button>
        </div>
      </template>

      <template #personalization>
        <div class="s-group">
          <div class="s-label">About you</div>
          <div class="s-row s-row--stack">
            <div class="s-row-label">What should Tnzi call you?</div>
            <input class="s-input" placeholder="Preferred name" value="Tuan" />
          </div>
          <div class="s-row s-row--stack">
            <div class="s-row-label">What do you do?</div>
            <input class="s-input" placeholder="e.g. software engineer at ..." />
          </div>
          <div class="s-row s-row--stack">
            <div class="s-row-label">What traits should Tnzi have?</div>
            <textarea class="s-input s-textarea" rows="3" placeholder="e.g. concise, curious, technically rigorous" />
          </div>
          <div class="s-row s-row--stack">
            <div class="s-row-label">Anything else Tnzi should know about you?</div>
            <textarea class="s-input s-textarea" rows="3" />
          </div>
        </div>
      </template>

      <template #memory>
        <div class="s-group">
          <div class="s-label">Memory</div>
          <div class="s-row">
            <div class="s-row-body">
              <div class="s-row-title">Remember facts across sessions</div>
              <div class="s-row-desc">Tnzi will remember details you share to personalize future responses.</div>
            </div>
            <span class="s-toggle is-on"><span class="s-toggle-knob" /></span>
          </div>
        </div>

        <div class="s-divider" />

        <div class="s-group">
          <div class="s-label">Stored memories (3)</div>
          <div class="s-memory-list">
            <div v-for="m in [
              { text: 'Primary language is TypeScript; prefers strict types.', at: '2 days ago' },
              { text: 'Working on a Vue 3 + UnoCSS component library called @tnzi/ui-ai.', at: '5 days ago' },
              { text: 'Runs the Tnzi.NET modular framework on .NET 10.', at: '11 days ago' },
            ]" :key="m.text" class="s-memory-item">
              <Icon icon="lucide:book-marked" class="s-memory-icon" />
              <div class="s-memory-body">
                <div class="s-memory-text">{{ m.text }}</div>
                <div class="s-memory-meta">{{ m.at }}</div>
              </div>
              <button type="button" class="s-icon-btn" aria-label="Delete">
                <Icon icon="lucide:x" />
              </button>
            </div>
          </div>
          <button type="button" class="s-btn-secondary s-btn-danger-ghost">
            Clear all memory
          </button>
        </div>
      </template>

      <template #data>
        <div class="s-group">
          <div class="s-label">Data controls</div>
          <div class="s-row">
            <div class="s-row-body">
              <div class="s-row-title">Improve the model for everyone</div>
              <div class="s-row-desc">Allow your conversations to be used for training future models.</div>
            </div>
            <span class="s-toggle"><span class="s-toggle-knob" /></span>
          </div>
          <div class="s-row">
            <div class="s-row-body">
              <div class="s-row-title">Chat history &amp; training</div>
              <div class="s-row-desc">New chats are saved and may be used for training.</div>
            </div>
            <span class="s-toggle is-on"><span class="s-toggle-knob" /></span>
          </div>
        </div>

        <div class="s-divider" />

        <div class="s-group">
          <div class="s-label">Export</div>
          <div class="s-row">
            <div class="s-row-body">
              <div class="s-row-title">Export all data</div>
              <div class="s-row-desc">Download a ZIP archive of your tasks, messages and artifacts.</div>
            </div>
            <button type="button" class="s-btn-secondary">Request export</button>
          </div>
        </div>
      </template>

      <template #connectors>
        <div class="s-group">
          <div class="s-label">Connected services</div>
          <div class="s-connector-list">
            <div v-for="c in [
              { name: 'GitHub',        icon: 'lucide:github',   status: 'Connected', on: true },
              { name: 'Google Drive',  icon: 'lucide:folder',   status: 'Connected', on: true },
              { name: 'Notion',        icon: 'lucide:book-open',status: 'Available', on: false },
              { name: 'Slack',         icon: 'lucide:hash',     status: 'Available', on: false },
              { name: 'Linear',        icon: 'lucide:circle-dot',status: 'Available', on: false },
            ]" :key="c.name" class="s-connector">
              <div class="s-connector-icon"><Icon :icon="c.icon" /></div>
              <div class="s-connector-body">
                <div class="s-connector-name">{{ c.name }}</div>
                <div class="s-connector-status" :class="{ 'is-on': c.on }">{{ c.status }}</div>
              </div>
              <button type="button" class="s-btn-secondary">{{ c.on ? 'Manage' : 'Connect' }}</button>
            </div>
          </div>
        </div>
      </template>

      <template #about>
        <div class="s-group">
          <div class="s-label">About</div>
          <div class="s-about">
            <p>
              <strong>Tnzi UI-AI playground</strong> — a high-fidelity chat
              application that plays scripted mock scenarios, exercising the
              components in <code>@tnzi/ui-ai</code> through realistic
              conversations.
            </p>
            <p>
              Visual style is a warm-neutral palette inspired by contemporary
              agent products, built on the package's own shell components
              (<code>TCollapsibleSidebar</code>, <code>TCommandPalette</code>,
              <code>TSettingsDialog</code>) and CSS variable theme system.
            </p>
            <div class="s-meta-row">
              <span class="s-meta-label">Version</span>
              <span class="s-meta-value">0.2.0-preview.4</span>
            </div>
            <div class="s-meta-row">
              <span class="s-meta-label">Build</span>
              <span class="s-meta-value">vite + vue 3</span>
            </div>
          </div>
        </div>
      </template>
    </TSettingsDialog>
  </div>
</template>

<style scoped>
/* ----- shell frame ----- */
.app-shell {
  display: flex;
  height: 100vh;
  font-family: var(--tnzi-ai-font-body);
  background: var(--tnzi-ai-bg);
  color: var(--tnzi-ai-text);
}

/* ----- brand ----- */
.brand {
  display: flex;
  align-items: center;
  gap: 8px;
  /* Match Manus header `h-[56px] py-[12px] pe-[10px]` — 56px tall, 10px
     right padding for the collapse button, slightly generous left padding
     to line up the 32px brand mark with the 36px rail icons below. */
  height: 56px;
  padding: 12px 10px 12px 10px;
}
.brand-mark {
  display: flex;
  align-items: center;
  justify-content: center;
  /* Brand mark matches the rail item footprint (36x36) so the logo stays
     visually anchored at the same spot when collapsing/expanding. */
  width: 32px;
  height: 32px;
  border-radius: 10px;
  background: linear-gradient(135deg, #1f1f1f 0%, #3a3a3a 100%);
  color: #fff;
  flex-shrink: 0;
  /* Distinctive monogram styling — drawn via inline SVG instead of a
     generic lucide sparkle so the wordmark reads as a real brand. */
}
.brand-mark-svg {
  width: 18px;
  height: 18px;
}
.brand-name {
  flex: 1;
  font-family: var(--tnzi-ai-font-display);
  font-size: 17px;
  font-weight: 500;
  letter-spacing: -0.01em;
  margin-left: 2px;
}
.brand-collapse {
  /* 32x32 tap target with 18px icon — same visual weight as the Manus
     header toggle (rounded-md hover rect containing an 18px panel-left). */
  width: 32px;
  height: 32px;
  border-radius: 8px;
  border: none;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 18px;
  flex-shrink: 0;
  transition: background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
              color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.brand-collapse:hover { background: var(--tnzi-ai-hover); color: var(--tnzi-ai-text); }

/* ----- main nav -----
   Dimensions pinned to Manus's rail item shape:
     h-[36px]  p-[9px]  gap-[12px]  rounded-[10px]  icon size-[18px]
   so the expanded label row lines up perfectly with the collapsed rail. */
.nav-main {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 0 8px 8px;
}
.nav-item {
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
}
.nav-item:hover { background: var(--tnzi-ai-hover); }
.nav-item.is-active { background: var(--tnzi-ai-selected); font-weight: 500; }
.nav-icon {
  font-size: 18px;
  width: 18px;
  height: 18px;
  flex-shrink: 0;
  color: var(--tnzi-ai-text-secondary);
}
.nav-item.is-active .nav-icon { color: var(--tnzi-ai-text); }

/* ----- sections (Projects / All tasks) ----- */
.section {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 14px 10px 4px;
}
.section-head {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 0 10px 4px;
  color: var(--tnzi-ai-text-tertiary);
  font-size: 11px;
  font-weight: 500;
  text-transform: none;
  letter-spacing: 0.01em;
}
.section-head > span:first-child { flex: 1; }
.section-act {
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
.section-act:hover { background: var(--tnzi-ai-hover); color: var(--tnzi-ai-text); }

/* Projects / All tasks rows share the same shape as .nav-item so every
   line in the expanded sidebar lines up perfectly — matches Manus where
   every `.rounded-[10px]` row is h-36 p-9 gap-12 with an 18px icon. */
.project-row, .task-row {
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
}
.project-row:hover, .task-row:hover { background: var(--tnzi-ai-hover); }
.task-row.is-active { background: var(--tnzi-ai-selected); }
.task-icon, .project-icon {
  flex-shrink: 0;
  width: 18px;
  height: 18px;
  font-size: 18px;
  color: var(--tnzi-ai-text-secondary);
}
.task-title {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

/* ----- footer: promo + utility icons ----- */
.promo {
  display: flex;
  align-items: center;
  gap: 10px;
  margin: 8px 10px;
  padding: 10px 12px;
  background: var(--tnzi-ai-surface);
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 10px;
  cursor: pointer;
  transition: background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.promo:hover { background: var(--tnzi-ai-hover); }
.promo-body { flex: 1; min-width: 0; }
.promo-title { font-size: 13px; font-weight: 500; color: var(--tnzi-ai-text); }
.promo-sub { font-size: 11px; color: var(--tnzi-ai-text-secondary); margin-top: 2px; }
.promo-arrow { color: var(--tnzi-ai-text-secondary); font-size: 16px; }

.foot-icons {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 6px 12px 10px;
  border-top: 1px solid var(--tnzi-ai-divider);
}
.foot-btn {
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
}
.foot-btn:hover { background: var(--tnzi-ai-hover); color: var(--tnzi-ai-text); }

/* ----- rail (collapsed, 52px) ----- */
.rail {
  display: flex;
  flex-direction: column;
  align-items: center;
  height: 100%;
  padding: 0;
  background: transparent;
}
/* Rail top-of-column expand trigger — no background, just the icon.
 * Defaults to the brand sparkle; any hover on the rail container swaps it
 * to a panel-left-open glyph (Manus-style logo-becomes-button pattern). */
.rail-brand {
  width: 36px;
  height: 36px;
  /* Manus header is h-[56px] with py-[12px] → 32px content area + 12 top/bot.
     Our rail uses a 36px tap target and centers vertically inside a 56px slot. */
  margin-top: 10px;
  margin-bottom: 4px;
  border-radius: 10px;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 18px;
  position: relative;
}
.rail-brand--button {
  border: none;
  padding: 0;
  cursor: pointer;
  transition: background 150ms var(--tnzi-ai-easing),
              color 150ms var(--tnzi-ai-easing);
}
.rail-brand--button .rail-brand-logo,
.rail-brand--button .rail-brand-expand {
  position: absolute;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -50%);
  transition: opacity 150ms var(--tnzi-ai-easing);
}
.rail-brand--button .rail-brand-expand { opacity: 0; }
/* Rail logo monogram: dark rounded square with the same T mark used in the
   expanded header — keeps the brand identity consistent across modes. */
.rail-brand-monogram {
  width: 28px;
  height: 28px;
  border-radius: 8px;
  background: linear-gradient(135deg, #1f1f1f 0%, #3a3a3a 100%);
  color: #fff;
  display: flex;
  align-items: center;
  justify-content: center;
}
.rail-brand-monogram svg {
  width: 16px;
  height: 16px;
}

/* The swap triggers on ANY hover of the rail container, not just on
 * direct hover of the logo button — matches the Manus interaction where
 * the top icon becomes a button the moment the cursor enters the rail. */
.rail:hover .rail-brand--button .rail-brand-logo { opacity: 0; }
.rail:hover .rail-brand--button .rail-brand-expand { opacity: 1; }
.rail:hover .rail-brand--button { color: var(--tnzi-ai-text); }
.rail-brand--button:hover {
  background: var(--tnzi-ai-hover);
}
.rail-group {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 2px;
  padding: 8px 0;
}
.rail-group--top { margin-top: 4px; }
.rail-group--bottom {
  margin-top: auto;
  padding: 8px 0 12px;
  border-top: 1px solid var(--tnzi-ai-divider);
  width: 100%;
  align-items: center;
}
.rail-btn {
  width: 36px;
  height: 36px;
  border-radius: 10px;
  border: none;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  /* 18px icon — matches Manus rail item `.size-[18px]`. */
  font-size: 18px;
  transition: background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
              color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.rail-btn:hover {
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
}
.rail-btn.is-active {
  background: var(--tnzi-ai-selected);
  color: var(--tnzi-ai-text);
}

/* ----- Rail hover popover (recent tasks etc) ----- */
.rail-popover {
  position: fixed;
  left: 60px;
  z-index: 25;
  min-width: 280px;
  max-width: 320px;
  background: var(--tnzi-ai-surface);
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 12px;
  box-shadow: 0 12px 32px rgba(0, 0, 0, 0.08);
  padding: 6px;
  display: flex;
  flex-direction: column;
  gap: 2px;
  animation: rail-popover-in 150ms var(--tnzi-ai-easing);
}
@keyframes rail-popover-in {
  from { opacity: 0; transform: translateX(-4px); }
  to   { opacity: 1; transform: translateX(0); }
}
.rail-popover-head {
  font-size: 11px;
  font-weight: 500;
  color: var(--tnzi-ai-text-tertiary);
  text-transform: uppercase;
  letter-spacing: 0.04em;
  padding: 8px 10px 6px;
}
.rail-popover-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 10px;
  border: none;
  background: transparent;
  border-radius: 8px;
  font-family: inherit;
  font-size: 13px;
  color: var(--tnzi-ai-text);
  text-align: left;
  cursor: pointer;
  width: 100%;
  min-width: 0;
}
.rail-popover-item:hover { background: var(--tnzi-ai-hover); }
.rail-popover-icon {
  flex-shrink: 0;
  font-size: 15px;
  color: var(--tnzi-ai-text-secondary);
}
.rail-popover-item > span {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  min-width: 0;
}

/* ----- Agent upsell view ----- */
.agent-view {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 32px 24px 64px;
  overflow-y: auto;
}
.agent-hero {
  width: 320px;
  height: 200px;
  position: relative;
  display: flex;
  align-items: center;
  justify-content: center;
  margin-bottom: 20px;
}
.agent-hero-phone {
  width: 110px;
  height: 160px;
  border-radius: 18px;
  background: var(--tnzi-ai-surface);
  border: 1px solid var(--tnzi-ai-border);
  position: relative;
  display: flex;
  align-items: flex-end;
  justify-content: center;
  padding-bottom: 20px;
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.04);
}
.agent-hero-bubble {
  position: absolute;
  bottom: 24px;
  left: 12px;
  right: 12px;
  padding: 10px;
  background: var(--tnzi-ai-bg);
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 10px;
  color: var(--tnzi-ai-text-secondary);
  font-size: 20px;
}
.agent-hero-ring { position: absolute; inset: 0; }
.hero-orb {
  position: absolute;
  width: 38px;
  height: 38px;
  border-radius: 999px;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 16px;
  color: #fff;
  border: 2px solid var(--tnzi-ai-bg);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.08);
}
.hero-orb--tg { top: 8px; left: 80px; background: #29a2e0; }
.hero-orb--ms { top: 0; left: 150px; background: #0084ff; }
.hero-orb--ln { top: 20px; right: 70px; background: #00c300; }
.hero-orb--wa { top: 58px; left: 50px; background: #25d366; }
.hero-orb--sl { top: 70px; right: 38px; background: #4a154b; }
.agent-title { margin: 0 0 24px; text-align: center; }
.agent-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 16px;
  max-width: 1000px;
  width: 100%;
  margin-bottom: 24px;
}
@media (max-width: 960px) {
  .agent-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
}
.agent-card {
  padding: 18px 18px 20px;
  background: var(--tnzi-ai-surface);
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 14px;
  display: flex;
  flex-direction: column;
  gap: 8px;
  transition: border-color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
              transform var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.agent-card:hover {
  border-color: var(--tnzi-ai-border-strong);
  transform: translateY(-1px);
}
.agent-card-icon {
  width: 28px;
  height: 28px;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--tnzi-ai-text-secondary);
  font-size: 18px;
  margin-bottom: 4px;
}
.agent-card-title { font-size: 14px; font-weight: 600; color: var(--tnzi-ai-text); }
.agent-card-desc { font-size: 13px; line-height: 1.5; color: var(--tnzi-ai-text-secondary); }
.agent-cta {
  display: inline-flex;
  align-items: center;
  gap: 10px;
  height: 44px;
  padding: 0 24px 0 10px;
  border-radius: 999px;
  border: none;
  background: var(--tnzi-ai-text);
  color: var(--tnzi-ai-bg);
  font-family: inherit;
  font-size: 15px;
  font-weight: 500;
  cursor: pointer;
}
.agent-cta > .iconify {
  width: 32px;
  height: 32px;
  border-radius: 999px;
  background: var(--tnzi-ai-accent);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 16px;
}

/* ----- Library view ----- */
.library-view {
  flex: 1;
  display: flex;
  flex-direction: column;
  padding: 0 40px 40px;
  overflow-y: auto;
}
.library-title { margin: 8px 0 16px; }
.library-toolbar {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 8px 0 24px;
}
.lib-left, .lib-right { display: flex; align-items: center; gap: 8px; }
.lib-right { margin-left: auto; }
.lib-filter {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  height: 32px;
  padding: 0 12px;
  border: 1px solid var(--tnzi-ai-border);
  background: var(--tnzi-ai-surface);
  border-radius: 999px;
  font-family: inherit;
  font-size: 13px;
  color: var(--tnzi-ai-text);
  cursor: pointer;
}
.lib-filter:hover { background: var(--tnzi-ai-hover); }
.lib-filter > .iconify { font-size: 14px; color: var(--tnzi-ai-text-secondary); }
.lib-search {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  height: 32px;
  padding: 0 14px;
  border: 1px solid var(--tnzi-ai-border);
  background: var(--tnzi-ai-surface);
  border-radius: 999px;
}
.lib-search > .iconify { font-size: 14px; color: var(--tnzi-ai-text-secondary); }
.lib-search input {
  border: none;
  outline: none;
  background: transparent;
  font-family: inherit;
  font-size: 13px;
  color: var(--tnzi-ai-text);
  width: 180px;
}
.lib-search input::placeholder { color: var(--tnzi-ai-text-tertiary); }
.lib-view-toggle {
  display: inline-flex;
  align-items: center;
  border: 1px solid var(--tnzi-ai-border);
  background: var(--tnzi-ai-surface);
  border-radius: 999px;
  padding: 2px;
}
.vtoggle {
  width: 28px;
  height: 28px;
  border: none;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  border-radius: 999px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 15px;
}
.vtoggle.is-active {
  background: var(--tnzi-ai-selected);
  color: var(--tnzi-ai-text);
}
.library-empty {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  color: var(--tnzi-ai-text-secondary);
  gap: 12px;
  padding: 60px 0;
}
.library-empty > .iconify { font-size: 40px; color: var(--tnzi-ai-text-tertiary); }
.library-empty-title { font-size: 15px; color: var(--tnzi-ai-text); font-weight: 500; }
.library-empty-sub { font-size: 13px; }

/* ----- main pane ----- */
.main { flex: 1; min-width: 0; display: flex; flex-direction: column; }

/* ----- top bar ----- */
.topbar {
  display: flex;
  align-items: center;
  gap: 8px;
  height: 52px;
  padding: 0 16px;
  background: transparent;
}
.topbar-toggle {
  width: 32px; height: 32px;
  border: none;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  border-radius: 6px;
  cursor: pointer;
  display: flex; align-items: center; justify-content: center;
}
.topbar-toggle:hover { background: var(--tnzi-ai-hover); color: var(--tnzi-ai-text); }

.workspace-switcher {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px 10px;
  border: none;
  background: transparent;
  color: var(--tnzi-ai-text);
  font-family: inherit;
  font-size: 15px;
  font-weight: 500;
  border-radius: 8px;
  cursor: pointer;
}
.workspace-switcher:hover { background: var(--tnzi-ai-hover); }
.workspace-switcher > .iconify {
  font-size: 14px;
  color: var(--tnzi-ai-text-secondary);
}

.topbar-spacer { flex: 1; }

.topbar-right {
  display: flex;
  align-items: center;
  gap: 6px;
}
.top-btn {
  display: flex;
  align-items: center;
  gap: 6px;
  height: 32px;
  padding: 0 10px;
  border: 1px solid var(--tnzi-ai-border);
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text);
  border-radius: 999px;
  font-family: inherit;
  font-size: 13px;
  font-weight: 500;
  cursor: pointer;
}
.top-btn:hover { background: var(--tnzi-ai-hover); }
.top-btn--publish {
  background: var(--tnzi-ai-text);
  border-color: var(--tnzi-ai-text);
  color: var(--tnzi-ai-bg);
}
.top-btn--publish:hover { opacity: 0.9; background: var(--tnzi-ai-text); }

.credit-badge {
  display: flex;
  align-items: center;
  gap: 6px;
  height: 32px;
  padding: 0 12px;
  background: var(--tnzi-ai-surface);
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 999px;
  font-size: 13px;
  font-weight: 500;
  color: var(--tnzi-ai-text);
}
.credit-badge .iconify { color: var(--tnzi-ai-accent); font-size: 14px; }

.avatar {
  width: 32px;
  height: 32px;
  border-radius: 999px;
  background: linear-gradient(135deg, #e8b87a, #6a4a2a 55%, #1a1a1a);
  cursor: pointer;
}

/* ----- empty state ----- */
.empty {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 40px 24px 80px;
  overflow-y: auto;
}
.plan-pill {
  display: inline-flex;
  align-items: center;
  gap: 10px;
  height: 30px;
  padding: 0 14px;
  background: var(--tnzi-ai-surface);
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 999px;
  font-size: 13px;
  color: var(--tnzi-ai-text);
  margin-bottom: 22px;
}
.plan-sep { color: var(--tnzi-ai-text-tertiary); }
.plan-link {
  color: var(--tnzi-ai-accent);
  text-decoration: none;
  font-weight: 500;
}
.plan-link:hover { text-decoration: underline; }

.greeting {
  margin: 0 0 28px;
  text-align: center;
}

.composer {
  width: 100%;
  max-width: 768px;
  background: var(--tnzi-ai-surface);
  border: 1px solid var(--tnzi-ai-border-strong);
  border-radius: var(--tnzi-ai-composer-radius);
  box-shadow: var(--tnzi-ai-composer-shadow);
  padding: 14px 4px 10px;
  transition: border-color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.composer:focus-within {
  border-color: var(--tnzi-ai-text-secondary);
}
.composer-input {
  width: 100%;
  border: none;
  outline: none;
  resize: none;
  font-family: inherit;
  font-size: 15px;
  line-height: 22px;
  background: transparent;
  color: var(--tnzi-ai-text);
  padding: 0 18px;
  min-height: 22px;
  max-height: 200px;
}
.composer-input::placeholder { color: var(--tnzi-ai-text-tertiary); }
.composer-bar {
  display: flex;
  align-items: center;
  padding: 8px 10px 0;
}
.composer-left, .composer-right {
  display: flex;
  align-items: center;
  gap: 2px;
}
.composer-right { margin-left: auto; }
.comp-btn {
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
  font-size: 17px;
}
.comp-btn:hover { background: var(--tnzi-ai-hover); color: var(--tnzi-ai-text); }

.send-btn {
  width: 32px;
  height: 32px;
  border: none;
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text-tertiary);
  border-radius: 999px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 16px;
  margin-left: 4px;
  transition: background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
              color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.send-btn.is-ready {
  background: var(--tnzi-ai-text);
  color: var(--tnzi-ai-bg);
}

.chips {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  justify-content: center;
  margin-top: 18px;
  max-width: 768px;
}
.chip {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  height: 32px;
  padding: 0 14px;
  border: 1px solid var(--tnzi-ai-border);
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text);
  border-radius: 999px;
  font-family: inherit;
  font-size: 13px;
  cursor: pointer;
  transition: background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.chip:hover { background: var(--tnzi-ai-hover); }
.chip .iconify { color: var(--tnzi-ai-text-secondary); font-size: 15px; }

/* ----- workspace (running scenario) ----- */
.workspace { flex: 1; display: flex; min-height: 0; overflow: hidden; }
.thread {
  flex: 1;
  min-width: 0;
  overflow-y: auto;
  padding: 24px 24px 24px;
  display: flex;
  flex-direction: column;
  gap: 24px;
  max-width: 820px;
  width: 100%;
  margin: 0 auto;
}
/* Bottom composer is owned by @tnzi/ui-ai's TThreadComposer — the old
   `.thread-composer*` CSS was removed when the template migrated. */
.msg { display: flex; flex-direction: column; gap: 8px; }
.msg--user { align-items: flex-end; }
.msg-role {
  display: flex; align-items: center; gap: 6px;
  font-size: 13px; color: var(--tnzi-ai-text-secondary);
}
.msg-role strong { font-weight: 500; color: var(--tnzi-ai-text); }
.msg-lite {
  font-size: 10px;
  padding: 1px 6px;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 4px;
  color: var(--tnzi-ai-text-tertiary);
}
.msg-streaming { color: var(--tnzi-ai-accent); animation: pulse 1s infinite; }
.msg-reasoning {
  font-size: 13px;
  color: var(--tnzi-ai-text-secondary);
  border-left: 2px solid var(--tnzi-ai-border);
  padding: 4px 0 4px 12px;
}
.msg-reasoning summary {
  cursor: pointer;
  list-style: none;
  font-weight: 500;
}
.msg-reasoning summary::-webkit-details-marker { display: none; }
.msg-reasoning > div { margin-top: 6px; font-style: italic; }
.msg-body {
  white-space: pre-wrap;
  line-height: 1.6;
  font-size: 15px;
  color: var(--tnzi-ai-text);
}
.msg--user .msg-body {
  padding: 10px 16px;
  background: var(--tnzi-ai-surface);
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 16px;
  max-width: 75%;
}
.msg-citations {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-top: 4px;
}
.citation {
  display: flex;
  flex-direction: column;
  gap: 2px;
  font-size: 12px;
  padding: 8px 12px;
  background: var(--tnzi-ai-hover);
  border-radius: 8px;
}
.citation strong { color: var(--tnzi-ai-text); }
.citation span { color: var(--tnzi-ai-text-secondary); }

/* Artifact panel is owned by @tnzi/ui-ai's TArtifactPanel — the old
   `.artifact*` / `.tab*` / `.artifact-code*` / `.artifact-preview*` /
   `.artifact-history*` blocks were removed when the template migrated.
   The only remaining rule is the outer positioning wrapper below. */
.artifact-slot {
  margin: 12px 12px 12px 0;
}

@keyframes pulse { 0%,100% { opacity: 1; } 50% { opacity: 0.3; } }

/* ========================================================================
 *  Section popover menus (Projects + / All tasks filter)
 * ====================================================================== */
.section-act-wrap { position: relative; display: inline-flex; }
.section-menu {
  position: absolute;
  top: 100%;
  right: 0;
  margin-top: 6px;
  z-index: 20;
  min-width: 200px;
  background: var(--tnzi-ai-surface);
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 12px;
  box-shadow: 0 12px 32px rgba(0, 0, 0, 0.08);
  padding: 6px;
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.section-menu-item {
  display: flex;
  align-items: center;
  gap: 10px;
  height: 34px;
  padding: 0 10px;
  border: none;
  background: transparent;
  border-radius: 8px;
  color: var(--tnzi-ai-text);
  font-family: inherit;
  font-size: 13px;
  text-align: left;
  cursor: pointer;
}
.section-menu-item:hover { background: var(--tnzi-ai-hover); }
.section-menu-item > .iconify { color: var(--tnzi-ai-text-secondary); font-size: 15px; }
.section-menu-check { color: var(--tnzi-ai-accent) !important; }
.section-menu-sep {
  height: 1px;
  background: var(--tnzi-ai-divider);
  margin: 4px 4px;
}

/* ========================================================================
 *  Rail floating tooltip (replaces HTML title in icon mode)
 * ====================================================================== */
.rail-tooltip {
  position: fixed;
  left: 60px;
  transform: translateY(-50%);
  padding: 6px 10px;
  background: #1f1e1c;
  color: #f8f8f7;
  font-size: 12px;
  font-weight: 500;
  border-radius: 6px;
  white-space: nowrap;
  pointer-events: none;
  z-index: 30;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.12);
  animation: tooltip-in 120ms var(--tnzi-ai-easing);
}
@keyframes tooltip-in {
  from { opacity: 0; transform: translate(-4px, -50%); }
  to { opacity: 1; transform: translate(0, -50%); }
}

/* ========================================================================
 *  Message thread enhancements
 * ====================================================================== */
/* Artifact-mode thread: center the chat column in the remaining space
   between the sidebar and the artifact panel. Previous version used
   `margin: 0 16px 0 auto` which pinned the column to the artifact edge
   and left a large empty gap on the left — plus it knocked the sticky
   composer out of alignment with the message list. Centering fixes both. */
.workspace--has-artifact .thread {
  max-width: 620px;
  margin: 0 auto;
  padding: 24px 24px 24px;
}
.msg-bubble--user {
  padding: 10px 16px;
  background: var(--tnzi-ai-surface);
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 18px;
  max-width: 75%;
  font-size: 15px;
  line-height: 1.6;
  color: var(--tnzi-ai-text);
}
.msg-brand {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
  border-radius: 6px;
  background: var(--tnzi-ai-text);
  color: var(--tnzi-ai-bg);
  font-size: 13px;
}
/* Reasoning stage, status banner, task-done row, follow-ups list and
   upgrade banner all moved to @tnzi/ui-ai/components:
     TReasoningStage / TStatusBanner / TTaskDoneRow /
     TFollowUpList / TUpgradeBanner
   The old inline CSS has been deleted along with this comment. */
.msg-actions {
  display: flex;
  align-items: center;
  gap: 4px;
  margin-top: 8px;
}
.msg-action {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  height: 30px;
  padding: 0 10px;
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
.msg-action:hover { background: var(--tnzi-ai-hover); }
.msg-action > .iconify { font-size: 13px; color: var(--tnzi-ai-text-secondary); }
.msg-action:first-child {
  width: 30px;
  padding: 0;
  justify-content: center;
}
.msg-action--primary > .iconify:first-child { color: var(--tnzi-ai-accent); }
.citation {
  display: flex;
  gap: 10px;
  padding: 10px 12px;
  background: var(--tnzi-ai-surface);
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 10px;
}
.citation-icon { color: var(--tnzi-ai-text-secondary); font-size: 16px; margin-top: 1px; }
.citation-body { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
.citation-body strong { font-size: 13px; color: var(--tnzi-ai-text); font-weight: 500; }
.citation-body span { font-size: 12px; color: var(--tnzi-ai-text-secondary); }
/* Task-done, follow-ups, upgrade-banner, feedback-row: all migrated to
   @tnzi/ui-ai/components (TTaskDoneRow / TFollowUpList / TUpgradeBanner).
   Corresponding CSS blocks removed alongside this comment. */

/* ========================================================================
 *  Settings dialog content (slots delivered from AppShell)
 * ====================================================================== */
.s-group { margin-bottom: 8px; }
.s-group + .s-divider { margin: 24px 0; }
.s-divider { height: 1px; background: var(--tnzi-ai-divider); margin: 24px 0; }
.s-label {
  font-size: 11px;
  font-weight: 500;
  letter-spacing: 0.04em;
  text-transform: none;
  color: var(--tnzi-ai-text-tertiary);
  margin-bottom: 14px;
}
.s-row {
  display: flex;
  align-items: flex-start;
  gap: 16px;
  padding: 12px 0;
}
.s-row--stack {
  flex-direction: column;
  align-items: stretch;
  gap: 8px;
}
.s-row-label {
  font-size: 14px;
  font-weight: 500;
  color: var(--tnzi-ai-text);
}
.s-row-body { flex: 1; min-width: 0; }
.s-row-title {
  font-size: 14px;
  font-weight: 500;
  color: var(--tnzi-ai-text);
}
.s-row-desc {
  font-size: 12px;
  color: var(--tnzi-ai-text-secondary);
  line-height: 1.5;
  margin-top: 4px;
}
.s-hint {
  font-size: 12px;
  color: var(--tnzi-ai-text-tertiary);
  margin-top: 2px;
}
.s-select {
  width: 240px;
  height: 36px;
  padding: 0 12px;
  background: var(--tnzi-ai-surface);
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 8px;
  font-family: inherit;
  font-size: 14px;
  color: var(--tnzi-ai-text);
  cursor: pointer;
  appearance: none;
  -webkit-appearance: none;
  background-image: linear-gradient(45deg, transparent 50%, currentColor 50%),
                    linear-gradient(135deg, currentColor 50%, transparent 50%);
  background-position: right 14px center, right 10px center;
  background-size: 4px 4px, 4px 4px;
  background-repeat: no-repeat;
  padding-right: 28px;
}
.s-select:disabled { opacity: 0.6; cursor: not-allowed; }

.s-theme-tiles {
  display: flex;
  gap: 14px;
  flex-wrap: wrap;
}
.s-tile {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
  padding: 0;
  border: none;
  background: transparent;
  cursor: pointer;
  font-family: inherit;
}
.s-tile-preview {
  width: 110px;
  height: 72px;
  border-radius: 10px;
  border: 2px solid var(--tnzi-ai-border);
  position: relative;
  overflow: hidden;
  display: flex;
  flex-direction: column;
  justify-content: center;
  gap: 6px;
  padding: 12px 14px;
  transition: border-color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
              transform var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.s-tile:hover .s-tile-preview {
  transform: translateY(-1px);
}
.s-tile.is-selected .s-tile-preview {
  border-color: var(--tnzi-ai-accent);
  box-shadow: 0 0 0 3px rgba(0, 129, 242, 0.18);
}
.s-tile-preview--light { background: #f8f8f7; }
.s-tile-preview--light .s-tile-bar { background: #34322d; }
.s-tile-preview--dark { background: #1c1c1b; }
.s-tile-preview--dark .s-tile-bar { background: #ededec; }
.s-tile-preview--system {
  background: linear-gradient(135deg, #f8f8f7 0%, #f8f8f7 49%, #1c1c1b 51%, #1c1c1b 100%);
}
.s-tile-preview--system .s-tile-bar {
  background: linear-gradient(90deg, #34322d 0%, #34322d 49%, #ededec 51%, #ededec 100%);
}
.s-tile-bar {
  height: 6px;
  border-radius: 3px;
  opacity: 0.8;
}
.s-tile-bar--lg { width: 60%; }
.s-tile-bar--md { width: 80%; opacity: 0.5; }
.s-tile-bar--sm { width: 40%; opacity: 0.5; }
.s-tile-label {
  font-size: 13px;
  color: var(--tnzi-ai-text-secondary);
}
.s-tile.is-selected .s-tile-label {
  color: var(--tnzi-ai-text);
  font-weight: 500;
}

.s-toggle {
  flex-shrink: 0;
  position: relative;
  display: inline-flex;
  align-items: center;
  width: 36px;
  height: 20px;
  border-radius: 999px;
  background: rgba(0, 0, 0, 0.18);
  cursor: pointer;
  transition: background var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
  padding: 0;
}
.s-toggle input {
  position: absolute;
  inset: 0;
  opacity: 0;
  cursor: pointer;
  margin: 0;
}
.s-toggle-knob {
  width: 16px;
  height: 16px;
  border-radius: 999px;
  background: #ffffff;
  margin: 2px;
  transition: transform var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.15);
}
.s-toggle.is-on {
  background: var(--tnzi-ai-accent);
}
.s-toggle.is-on .s-toggle-knob {
  transform: translateX(16px);
}

.s-btn-secondary {
  height: 32px;
  padding: 0 16px;
  border: 1px solid var(--tnzi-ai-border-strong);
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text);
  border-radius: 999px;
  font-family: inherit;
  font-size: 13px;
  font-weight: 500;
  cursor: pointer;
}
.s-btn-secondary:hover { background: var(--tnzi-ai-hover); }

.s-about p {
  font-size: 14px;
  line-height: 1.6;
  color: var(--tnzi-ai-text);
  margin: 0 0 12px;
}
.s-about code {
  font-family: var(--tnzi-ai-font-mono);
  font-size: 13px;
  padding: 1px 6px;
  background: var(--tnzi-ai-hover);
  border-radius: 4px;
}
.s-meta-row {
  display: flex;
  gap: 16px;
  font-size: 13px;
  padding: 8px 0;
  border-bottom: 1px solid var(--tnzi-ai-divider);
}
.s-meta-row:last-child { border-bottom: none; }
.s-meta-label {
  width: 80px;
  color: var(--tnzi-ai-text-secondary);
}
.s-meta-value {
  color: var(--tnzi-ai-text);
  font-family: var(--tnzi-ai-font-mono);
  font-size: 12px;
}

/* ---- Account section ---- */
.s-profile {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 16px;
  background: var(--tnzi-ai-surface);
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 12px;
}
.s-profile-avatar {
  width: 56px;
  height: 56px;
  border-radius: 999px;
  background: linear-gradient(135deg, #e8b87a, #6a4a2a 55%, #1a1a1a);
  flex-shrink: 0;
}
.s-profile-info { flex: 1; min-width: 0; }
.s-profile-name { font-size: 16px; font-weight: 600; color: var(--tnzi-ai-text); }
.s-profile-email { font-size: 13px; color: var(--tnzi-ai-text-secondary); margin-top: 2px; }
.s-profile-tier { display: flex; gap: 6px; margin-top: 8px; }
.s-chip {
  display: inline-flex;
  align-items: center;
  height: 22px;
  padding: 0 10px;
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text-secondary);
  border-radius: 999px;
  font-size: 11px;
  font-weight: 500;
}
.s-chip--accent {
  background: var(--tnzi-ai-accent-soft);
  color: var(--tnzi-ai-accent);
}
.s-btn-danger {
  height: 32px;
  padding: 0 16px;
  border: 1px solid rgba(200, 30, 30, 0.3);
  background: transparent;
  color: #c81e1e;
  border-radius: 999px;
  font-family: inherit;
  font-size: 13px;
  font-weight: 500;
  cursor: pointer;
}
.s-btn-danger:hover { background: rgba(200, 30, 30, 0.06); }
.s-btn-danger-ghost {
  color: #c81e1e;
  border-color: rgba(200, 30, 30, 0.25);
  margin-top: 12px;
}
.s-btn-block {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  margin-top: 14px;
}

/* ---- Usage ---- */
.s-usage-card {
  padding: 18px 20px;
  background: var(--tnzi-ai-surface);
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 12px;
}
.s-usage-num {
  font-family: var(--tnzi-ai-font-display);
  font-size: 32px;
  color: var(--tnzi-ai-text);
}
.s-usage-num span {
  font-size: 16px;
  color: var(--tnzi-ai-text-tertiary);
  font-family: inherit;
}
.s-usage-bar {
  height: 6px;
  background: var(--tnzi-ai-hover);
  border-radius: 999px;
  margin: 14px 0 12px;
  overflow: hidden;
}
.s-usage-fill {
  display: block;
  height: 100%;
  background: var(--tnzi-ai-accent);
  border-radius: 999px;
  transition: width var(--tnzi-ai-duration-slow) var(--tnzi-ai-easing);
}
.s-usage-meta {
  display: flex;
  align-items: center;
  justify-content: space-between;
  font-size: 13px;
  color: var(--tnzi-ai-text-secondary);
}
.s-metric-grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 10px;
}
.s-metric {
  padding: 14px 16px;
  background: var(--tnzi-ai-surface);
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 10px;
}
.s-metric-label { font-size: 11px; color: var(--tnzi-ai-text-tertiary); text-transform: uppercase; letter-spacing: 0.04em; }
.s-metric-value { font-size: 22px; font-weight: 600; color: var(--tnzi-ai-text); margin-top: 4px; }
.s-metric-hint { font-size: 11px; color: var(--tnzi-ai-text-tertiary); margin-top: 2px; }

/* ---- Skills grid ---- */
.s-skill-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}
.s-skill-card {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 16px;
  background: var(--tnzi-ai-surface);
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 12px;
  transition: border-color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.s-skill-card.is-on { border-color: var(--tnzi-ai-accent-soft); }
.s-skill-head { display: flex; align-items: center; justify-content: space-between; }
.s-skill-icon {
  width: 32px; height: 32px;
  border-radius: 8px;
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
  display: flex; align-items: center; justify-content: center;
  font-size: 16px;
}
.s-skill-name { font-size: 14px; font-weight: 600; color: var(--tnzi-ai-text); }
.s-skill-desc { font-size: 12px; color: var(--tnzi-ai-text-secondary); line-height: 1.5; }

/* ---- Personalization inputs ---- */
.s-input {
  width: 100%;
  height: 38px;
  padding: 0 14px;
  background: var(--tnzi-ai-surface);
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 10px;
  font-family: inherit;
  font-size: 14px;
  color: var(--tnzi-ai-text);
  outline: none;
  transition: border-color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.s-input:focus { border-color: var(--tnzi-ai-accent); }
.s-input::placeholder { color: var(--tnzi-ai-text-tertiary); }
.s-textarea { height: auto; padding: 10px 14px; resize: vertical; line-height: 1.5; }

/* ---- Memory list ---- */
.s-memory-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.s-memory-item {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  padding: 12px 14px;
  background: var(--tnzi-ai-surface);
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 10px;
}
.s-memory-icon {
  flex-shrink: 0;
  margin-top: 2px;
  color: var(--tnzi-ai-text-secondary);
  font-size: 16px;
}
.s-memory-body { flex: 1; min-width: 0; }
.s-memory-text { font-size: 13px; color: var(--tnzi-ai-text); line-height: 1.5; }
.s-memory-meta { font-size: 11px; color: var(--tnzi-ai-text-tertiary); margin-top: 4px; }
.s-icon-btn {
  width: 28px; height: 28px;
  border: none;
  background: transparent;
  color: var(--tnzi-ai-text-tertiary);
  border-radius: 6px;
  cursor: pointer;
  display: flex; align-items: center; justify-content: center;
  font-size: 14px;
  flex-shrink: 0;
}
.s-icon-btn:hover { background: var(--tnzi-ai-hover); color: var(--tnzi-ai-text); }

/* ---- Connectors list ---- */
.s-connector-list { display: flex; flex-direction: column; gap: 8px; }
.s-connector {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 12px 16px;
  background: var(--tnzi-ai-surface);
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 12px;
}
.s-connector-icon {
  width: 36px; height: 36px;
  border-radius: 8px;
  background: var(--tnzi-ai-hover);
  display: flex; align-items: center; justify-content: center;
  font-size: 18px;
  color: var(--tnzi-ai-text);
  flex-shrink: 0;
}
.s-connector-body { flex: 1; min-width: 0; }
.s-connector-name { font-size: 14px; font-weight: 500; color: var(--tnzi-ai-text); }
.s-connector-status { font-size: 12px; color: var(--tnzi-ai-text-tertiary); margin-top: 2px; }
.s-connector-status.is-on { color: #2e8b57; }

/* ---- Composer menus (attach / tools) ---- */
.comp-menu-wrap { position: relative; display: inline-flex; }
.comp-menu {
  position: absolute;
  bottom: calc(100% + 10px);
  left: 0;
  z-index: 15;
  min-width: 220px;
  background: var(--tnzi-ai-surface);
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 12px;
  box-shadow: 0 12px 32px rgba(0, 0, 0, 0.08);
  padding: 6px;
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.comp-menu--wide { min-width: 260px; }
.comp-menu-head {
  font-size: 11px;
  font-weight: 500;
  color: var(--tnzi-ai-text-tertiary);
  text-transform: uppercase;
  letter-spacing: 0.04em;
  padding: 8px 10px 6px;
}
.comp-menu-item {
  display: flex;
  align-items: center;
  gap: 10px;
  height: 36px;
  padding: 0 10px;
  border: none;
  background: transparent;
  border-radius: 8px;
  color: var(--tnzi-ai-text);
  font-family: inherit;
  font-size: 13px;
  text-align: left;
  cursor: pointer;
}
.comp-menu-item:hover { background: var(--tnzi-ai-hover); }
.comp-menu-item > .iconify { color: var(--tnzi-ai-text-secondary); font-size: 15px; flex-shrink: 0; }
.comp-menu-item--toggle { cursor: default; }
.comp-menu-item--toggle:hover { background: transparent; }
.comp-menu-label { flex: 1; }
.comp-btn-badge {
  position: absolute;
  top: -2px;
  right: -4px;
  min-width: 16px;
  height: 16px;
  padding: 0 4px;
  background: var(--tnzi-ai-accent);
  color: #fff;
  border-radius: 999px;
  font-size: 10px;
  font-weight: 600;
  display: flex;
  align-items: center;
  justify-content: center;
  pointer-events: none;
}
.comp-btn { position: relative; }

</style>
