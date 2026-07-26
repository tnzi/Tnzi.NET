import { ref, computed, watch, type Ref, type ComputedRef } from 'vue'

export interface CommandAction {
  id: string
  label: string
  description?: string
  icon?: string
  category?: string
  keywords?: readonly string[]
  shortcut?: readonly string[]
  run: () => void | Promise<void>
}

export interface UseCommandPaletteOptions {
  actions: Ref<readonly CommandAction[]>
  maxResults?: number
}

export interface UseCommandPaletteReturn {
  open: Ref<boolean>
  query: Ref<string>
  results: ComputedRef<readonly CommandAction[]>
  highlightedIndex: Ref<number>
  show: () => void
  hide: () => void
  toggle: () => void
  moveUp: () => void
  moveDown: () => void
  activate: () => Promise<void>
}

function scoreAction(action: CommandAction, query: string): number {
  if (!query) return 1
  const q = query.toLowerCase()
  const label = action.label.toLowerCase()
  if (label.includes(q)) return 10
  if (action.category?.toLowerCase().includes(q)) return 5
  const kwHit = action.keywords?.some((k) => k.toLowerCase().includes(q)) ?? false
  if (kwHit) return 3
  if (action.description?.toLowerCase().includes(q)) return 1
  return 0
}

/**
 * @experimental
 * Manages a command palette over a dynamic action registry.
 *
 * Uses plain case-insensitive substring matching with keyword/category
 * boost - intentionally dependency-free (no Fuse.js). Exposes keyboard
 * navigation helpers and an activate() that runs the highlighted action
 * and closes the palette.
 */
export function useCommandPalette(options: UseCommandPaletteOptions): UseCommandPaletteReturn {
  const maxResults = options.maxResults ?? 50
  const open = ref(false)
  const query = ref('')
  const highlightedIndex = ref(0)

  const results = computed<readonly CommandAction[]>(() => {
    const list = options.actions.value
    if (!query.value) return list.slice(0, maxResults)
    const scored = list
      .map((action) => ({ action, score: scoreAction(action, query.value) }))
      .filter((entry) => entry.score > 0)
      .sort((a, b) => b.score - a.score)
      .map((entry) => entry.action)
      .slice(0, maxResults)
    return scored
  })

  watch(
    query,
    () => {
      highlightedIndex.value = 0
    },
    { flush: 'sync' },
  )

  watch(
    results,
    (list) => {
      if (highlightedIndex.value >= list.length) {
        highlightedIndex.value = Math.max(0, list.length - 1)
      }
    },
    { flush: 'sync' },
  )

  function show(): void {
    query.value = ''
    highlightedIndex.value = 0
    open.value = true
  }

  function hide(): void {
    open.value = false
  }

  function toggle(): void {
    if (open.value) hide()
    else show()
  }

  function moveUp(): void {
    highlightedIndex.value = Math.max(0, highlightedIndex.value - 1)
  }

  function moveDown(): void {
    const max = Math.max(0, results.value.length - 1)
    highlightedIndex.value = Math.min(max, highlightedIndex.value + 1)
  }

  async function activate(): Promise<void> {
    const action = results.value[highlightedIndex.value]
    if (!action) return
    try {
      await action.run()
    } finally {
      hide()
    }
  }

  return { open, query, results, highlightedIndex, show, hide, toggle, moveUp, moveDown, activate }
}
