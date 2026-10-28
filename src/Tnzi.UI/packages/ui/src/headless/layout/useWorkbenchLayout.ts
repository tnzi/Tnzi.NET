/**
 * `useWorkbenchLayout` - drag-to-reorder + localStorage persistence for
 * the Workbench widget list.
 *
 * `TWorkbenchLayout` consumes this hook to keep the user-customised
 * widget order alive across page reloads. The hook is intentionally
 * framework-agnostic (no naive-ui imports) so the same persistence
 * logic can power test mounts and Storybook examples.
 *
 * Stored shape: a flat `string[]` of widget ids. Resolution is robust
 * to widgets being added / removed between releases:
 *   - Stored ids that no longer exist are silently dropped.
 *   - New widget defs not present in storage are appended in declaration
 *     order (so additions always become visible without a localStorage
 *     wipe).
 *
 * `persistKey` accepts either a plain string or a `Ref` - when the prop
 * changes (e.g. a tab-scoped workbench id swaps), the hook re-reads
 * persistedOrder from the new key.
 *
 * Sunk from `@tnzi/ui-admin/headless/useWorkbenchLayout.ts` in 0.2.x.
 */
import { computed, ref, unref, watch, type MaybeRef, type Ref } from 'vue'
import type { WidgetDef } from '../../components/layout/widget-types'

const DEFAULT_PERSIST_KEY = 'tnzi-workbench-order'

interface UseWorkbenchLayoutOptions {
  /** Source-of-truth widget array (declaration order). */
  widgets: Ref<WidgetDef[]>
  /** Enable drag-to-reorder + persistence. When false the hook is a no-op. */
  draggable: Ref<boolean>
  /**
   * localStorage key. Defaults to `'tnzi-workbench-order'`. Accepts a
   * reactive ref so parents can dynamically swap the persistence key
   * (e.g. tab-scoped workbench layouts) without re-mounting the hook.
   */
  persistKey?: MaybeRef<string | undefined>
}

export interface UseWorkbenchLayoutReturn {
  /** Widgets in resolved render order (respects user customisation). */
  orderedWidgets: Ref<WidgetDef[]>
  /**
   * Replace the order. Call after the drag handler has produced the new
   * array. Persists immediately when `draggable` is true.
   */
  setOrder: (next: WidgetDef[]) => void
  /** Reset to declaration order and clear persisted state. */
  resetOrder: () => void
}

function readPersisted(key: string): string[] | null {
  if (typeof window === 'undefined') return null
  try {
    const raw = window.localStorage.getItem(key)
    if (!raw) return null
    const parsed = JSON.parse(raw) as unknown
    return Array.isArray(parsed) && parsed.every((s) => typeof s === 'string')
      ? (parsed as string[])
      : null
  } catch {
    return null
  }
}

function writePersisted(key: string, value: string[]): void {
  if (typeof window === 'undefined') return
  try {
    window.localStorage.setItem(key, JSON.stringify(value))
  } catch {
    // Quota exceeded / private browsing - fall back to in-memory only.
  }
}

function clearPersisted(key: string): void {
  if (typeof window === 'undefined') return
  try {
    window.localStorage.removeItem(key)
  } catch {
    // ignore
  }
}

/**
 * Re-sort the widget array per a persisted id sequence. Pinned widgets
 * keep their declared slots (the same slots they hold before any order is
 * saved); the remaining slots are filled by the movable widgets in the
 * persisted id sequence, with any id not in `persistedOrder` appended in
 * declaration order so newly-added widgets stay visible.
 */
function applyOrder(widgets: WidgetDef[], persistedOrder: string[] | null): WidgetDef[] {
  if (!persistedOrder || persistedOrder.length === 0) return [...widgets]
  const byId = new Map(widgets.map((w) => [w.id, w]))
  const movable: WidgetDef[] = []
  const placed = new Set<string>()
  for (const id of persistedOrder) {
    const w = byId.get(id)
    if (!w || w.pinned || placed.has(id)) continue
    movable.push(w)
    placed.add(id)
  }
  for (const w of widgets) {
    if (!w.pinned && !placed.has(w.id)) movable.push(w)
  }
  let next = 0
  return widgets.map((w) => (w.pinned ? w : movable[next++]!))
}

export function useWorkbenchLayout(
  options: UseWorkbenchLayoutOptions,
): UseWorkbenchLayoutReturn {
  // `resolvedKey` tracks the live persistKey so dynamic swaps re-read
  // the right localStorage entry. Reading via `unref()` covers both
  // plain-string and Ref inputs.
  const resolvedKey = computed<string>(
    () => unref(options.persistKey) ?? DEFAULT_PERSIST_KEY,
  )

  const persistedOrder = ref<string[] | null>(readPersisted(resolvedKey.value))

  // When the persistKey changes, re-hydrate from the new bucket. The old
  // bucket is left untouched - callers needing to migrate data between
  // keys should orchestrate that explicitly.
  watch(resolvedKey, (key) => {
    persistedOrder.value = readPersisted(key)
  })

  const orderedWidgets = computed<WidgetDef[]>(() =>
    options.draggable.value
      ? applyOrder(options.widgets.value, persistedOrder.value)
      : [...options.widgets.value],
  )

  function setOrder(next: WidgetDef[]): void {
    if (!options.draggable.value) return
    const ids = next.map((w) => w.id)
    persistedOrder.value = ids
    writePersisted(resolvedKey.value, ids)
  }

  function resetOrder(): void {
    persistedOrder.value = null
    clearPersisted(resolvedKey.value)
  }

  // Turning `draggable` off shows declaration order through `orderedWidgets`
  // alone; the saved order is kept so turning it back on restores it.

  return { orderedWidgets, setOrder, resetOrder }
}
