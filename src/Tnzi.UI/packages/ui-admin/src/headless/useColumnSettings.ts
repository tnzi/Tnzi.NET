import { computed, ref, watch, type Ref, type ComputedRef, type VNode } from 'vue'

export interface ColumnDef<TRow = Record<string, unknown>> {
  key: string
  title: string
  visible?: boolean
  width?: number
  /**
   * Minimum column width (px). Unlike `width` (a fixed size), `minWidth`
   * makes the column ELASTIC: it grows to fill the available horizontal
   * space and only shrinks down to this floor. The table therefore fills
   * its container (no right-hand gap) and a horizontal scrollbar appears
   * only when the sum of every column's minimum width genuinely exceeds the
   * container — not on every wide-ish or empty table the way a fixed-`width`
   * sum did. Prefer `minWidth` for text columns (name, email, description);
   * keep `width` for fixed-content columns (status badges, dates, icons,
   * numbers, the operation column). Forwarded to NDataTable's column
   * `minWidth`. If both `width` and `minWidth` are set, `width` wins (naive
   * treats the column as fixed).
   */
  minWidth?: number
  fixed?: 'left' | 'right'
  /** Horizontal cell alignment. Forwarded to NDataTable's column `align`. */
  align?: 'left' | 'center' | 'right'
  /**
   * Truncate long cell text with an ellipsis instead of wrapping. Pass
   * `true` for plain truncation; pass `{ tooltip: true }` to also show
   * the full text in a hover tooltip. Forwarded to NDataTable's
   * `ellipsis` column option.
   */
  ellipsis?: boolean | { tooltip?: boolean }
  /**
   * Custom cell renderer. Receives the row object and returns a string or VNode.
   * Use for status badges, relative timestamps, action buttons, links, etc.
   *
   * @example
   * ```ts
   * { key: 'isEnabled', title: 'Status',
   *   render: (row) => h(TStatusBadge, { value: row.isEnabled }) }
   * ```
   */
  render?: (row: TRow) => VNode | string | number | null
  /**
   * Mobile card hint: promote this column to the card **title** when the
   * table collapses to the stacked card list on narrow viewports. When no
   * column is flagged, the first visible column is used. Has no effect on
   * the desktop table.
   */
  primary?: boolean
  /**
   * Mobile card hint: omit this column from the stacked card list on narrow
   * viewports (e.g. a wide/secondary field that only matters on desktop).
   * Still rendered in the desktop table.
   */
  mobileHidden?: boolean
}

export interface UseColumnSettingsOptions {
  pageId: string
  columns: ColumnDef[]
  storageKey?: string
}

export interface UseColumnSettingsReturn {
  visibleColumns: ComputedRef<ColumnDef[]>
  orderedKeys: Ref<string[]>
  hiddenKeys: Ref<Set<string>>
  /**
   * Runtime overrides for the `fixed` property — separate from the
   * ColumnDef's static value so the user's column-setting popover
   * choices don't mutate the page config. `null` means "explicitly
   * unfix", absent means "use the ColumnDef default".
   */
  fixedOverrides: Ref<Map<string, 'left' | 'right' | null>>
  hide: (key: string) => void
  show: (key: string) => void
  toggle: (key: string) => void
  reorder: (newKeys: string[]) => void
  /** Cycle the column's fixed state: undefined → left → right → undefined. */
  cycleFixed: (key: string) => void
  /** Read the effective fixed state (override > ColumnDef default). */
  getFixed: (key: string) => 'left' | 'right' | undefined
  reset: () => void
}

interface PersistedState {
  hidden: string[]
  order: string[]
  fixed?: Record<string, 'left' | 'right' | null>
}

export function useColumnSettings(options: UseColumnSettingsOptions): UseColumnSettingsReturn {
  const { pageId, columns } = options
  const storageKey = options.storageKey ?? `tnzi-admin:cols:${pageId}`

  const columnMap = new Map(columns.map((c) => [c.key, c]))
  const originalOrder = columns.map((c) => c.key)
  const defaultHidden = new Set(columns.filter((c) => c.visible === false).map((c) => c.key))

  const orderedKeys = ref<string[]>([...originalOrder])
  const hiddenKeys = ref<Set<string>>(new Set(defaultHidden))
  const fixedOverrides = ref<Map<string, 'left' | 'right' | null>>(new Map())

  // Attempt restore from storage
  try {
    const raw = typeof localStorage !== 'undefined' ? localStorage.getItem(storageKey) : null
    if (raw) {
      const parsed = JSON.parse(raw) as PersistedState
      if (
        Array.isArray(parsed.order) &&
        parsed.order.length === originalOrder.length &&
        parsed.order.every((k) => columnMap.has(k))
      ) {
        orderedKeys.value = [...parsed.order]
      }
      if (Array.isArray(parsed.hidden)) {
        hiddenKeys.value = new Set(parsed.hidden.filter((k) => columnMap.has(k)))
      }
      if (parsed.fixed && typeof parsed.fixed === 'object') {
        const map = new Map<string, 'left' | 'right' | null>()
        for (const [k, v] of Object.entries(parsed.fixed)) {
          if (!columnMap.has(k)) continue
          if (v === 'left' || v === 'right' || v === null) map.set(k, v)
        }
        fixedOverrides.value = map
      }
    }
  } catch {
    // ignore storage errors
  }

  function getFixed(key: string): 'left' | 'right' | undefined {
    if (fixedOverrides.value.has(key)) {
      const v = fixedOverrides.value.get(key)
      return v === null ? undefined : v
    }
    return columnMap.get(key)?.fixed
  }

  const visibleColumns = computed<ColumnDef[]>(() => {
    const out: ColumnDef[] = []
    for (const k of orderedKeys.value) {
      if (hiddenKeys.value.has(k)) continue
      const col = columnMap.get(k)
      if (!col) continue
      // Apply runtime fixed override so the user's pin-toggle choices
      // show up in the rendered table without mutating page config.
      out.push({ ...col, fixed: getFixed(k) })
    }
    return out
  })

  function hide(key: string): void {
    if (!columnMap.has(key)) return
    const next = new Set(hiddenKeys.value)
    next.add(key)
    hiddenKeys.value = next
  }

  function show(key: string): void {
    if (!columnMap.has(key)) return
    const next = new Set(hiddenKeys.value)
    next.delete(key)
    hiddenKeys.value = next
  }

  function toggle(key: string): void {
    if (hiddenKeys.value.has(key)) show(key)
    else hide(key)
  }

  function reorder(newKeys: string[]): void {
    // Preserve only known keys; ignore unknown
    const filtered = newKeys.filter((k) => columnMap.has(k))
    orderedKeys.value = [...filtered]
  }

  /** Cycle: undefined → 'left' → 'right' → undefined.
   *  Mirrors soybean's `handleFixed` rotation in table-column-setting.vue. */
  function cycleFixed(key: string): void {
    if (!columnMap.has(key)) return
    const current = getFixed(key)
    const next: 'left' | 'right' | null =
      current === undefined ? 'left' : current === 'left' ? 'right' : null
    const map = new Map(fixedOverrides.value)
    map.set(key, next)
    fixedOverrides.value = map
  }

  function reset(): void {
    orderedKeys.value = [...originalOrder]
    hiddenKeys.value = new Set(defaultHidden)
    fixedOverrides.value = new Map()
  }

  watch(
    [orderedKeys, hiddenKeys, fixedOverrides],
    () => {
      try {
        if (typeof localStorage === 'undefined') return
        const fixedRecord: Record<string, 'left' | 'right' | null> = {}
        fixedOverrides.value.forEach((v, k) => {
          fixedRecord[k] = v
        })
        const payload: PersistedState = {
          hidden: [...hiddenKeys.value],
          order: [...orderedKeys.value],
          fixed: fixedRecord,
        }
        localStorage.setItem(storageKey, JSON.stringify(payload))
      } catch {
        // ignore storage errors
      }
    },
    { deep: true },
  )

  return {
    visibleColumns,
    orderedKeys,
    hiddenKeys,
    fixedOverrides,
    hide,
    show,
    toggle,
    reorder,
    cycleFixed,
    getFixed,
    reset,
  }
}
