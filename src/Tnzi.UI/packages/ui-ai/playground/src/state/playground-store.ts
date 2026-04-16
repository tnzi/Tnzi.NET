import { ref, computed, watch } from 'vue'
import type { SidebarMode } from '@tnzi/ui-ai/shell'
import { loadScenarios, type LoadedScenarioIndex } from '../mock/loader'

const STORAGE_KEY = 'tnzi-ui-ai-playground-state'

interface PersistedState {
  sidebarMode: SidebarMode
  theme: 'light' | 'dark'
  locale: 'en' | 'zh-cn'
}

function readPersisted(): Partial<PersistedState> {
  if (typeof localStorage === 'undefined') return {}
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    return raw ? (JSON.parse(raw) as Partial<PersistedState>) : {}
  } catch {
    return {}
  }
}

function writePersisted(state: PersistedState): void {
  if (typeof localStorage === 'undefined') return
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(state))
  } catch {
    // ignore quota / SSR
  }
}

function initialScenarioId(index: LoadedScenarioIndex): string {
  if (typeof window !== 'undefined') {
    const match = window.location.hash.match(/^#\/s\/([^/]+)/)
    if (match && match[1] && index.byId.has(match[1])) {
      return match[1]
    }
  }
  return index.scenarios[0]?.meta.id ?? ''
}

let instance: ReturnType<typeof createStoreInternal> | null = null

function createStoreInternal() {
  const scenarioIndex = loadScenarios()
  const persisted = readPersisted()

  const currentScenarioId = ref(initialScenarioId(scenarioIndex))
  const sidebarMode = ref<SidebarMode>(persisted.sidebarMode ?? 'expanded')
  const commandPaletteOpen = ref(false)
  const settingsOpen = ref(false)
  const settingsSection = ref('appearance')
  const theme = ref<'light' | 'dark'>(persisted.theme ?? 'light')
  const locale = ref<'en' | 'zh-cn'>(persisted.locale ?? 'en')

  const currentScenario = computed(() => scenarioIndex.byId.get(currentScenarioId.value) ?? null)

  function selectScenario(id: string): void {
    if (!scenarioIndex.byId.has(id)) return
    currentScenarioId.value = id
    if (typeof window !== 'undefined') {
      window.location.hash = `#/s/${id}`
    }
  }

  watch([sidebarMode, theme, locale], ([mode, t, l]) => {
    writePersisted({ sidebarMode: mode, theme: t, locale: l })
  })

  // Factory runs outside a component setup context, so we attach the
  // hashchange listener directly. Vue onMounted would throw here.
  if (typeof window !== 'undefined') {
    window.addEventListener('hashchange', () => {
      const match = window.location.hash.match(/^#\/s\/([^/]+)/)
      if (match && match[1] && scenarioIndex.byId.has(match[1])) {
        currentScenarioId.value = match[1]
      }
    })
  }

  return {
    scenarioIndex,
    currentScenarioId,
    currentScenario,
    sidebarMode,
    commandPaletteOpen,
    settingsOpen,
    settingsSection,
    theme,
    locale,
    selectScenario,
  }
}

export function usePlaygroundStore(): ReturnType<typeof createStoreInternal> {
  if (!instance) instance = createStoreInternal()
  return instance
}
