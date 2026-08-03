/**
 * Declarative row-action model for the CRUD table's operation column.
 *
 * A page declares a flat `RowAction[]` list; {@link TRowActions} arranges them
 * with the "at most N inline, overflow → More▾" rule and the operation column
 * auto-sizes to fit (see {@link estimateRowActionsWidth}). The built-in
 * `editAction` / `viewAction` / `deleteAction` factories wire the common
 * operations to the `useCrudPage` state so the common case stays a one-liner:
 *
 * ```ts
 * const rowActions = [
 *   editAction(crud),
 *   { key: 'detail', label: 'detail', onClick: openDetail },
 *   deleteAction(crud),
 * ]
 * ```
 */
import type { UseCrudPageReturn } from './useCrudPage'

export type RowActionType =
  | 'default'
  | 'primary'
  | 'info'
  | 'success'
  | 'warning'
  | 'error'

export interface RowAction<T = Record<string, unknown>> {
  /** Stable key (also the default i18n label source for `edit`/`view`/`delete`). */
  key: string
  /** Button text. Plain string, an i18n key, or a per-row function. Omit for
   *  the built-in `edit`/`view`/`delete` keys to get the framework labels. */
  label?: string | ((row: T) => string)
  /** Naive button type - `error` for destructive, `primary` for the main action. */
  type?: RowActionType
  /** Optional leading iconify icon. */
  icon?: string
  /** Click handler. The built-in factories wire this to the CRUD state. */
  onClick?: (row: T) => void | Promise<void>
  /** Conditional visibility - when it returns false the action is dropped for
   *  that row (replaces the `isEnabled ? {enable} : {disable}` idiom). */
  show?: (row: T) => boolean
  /** Per-row disabled state. */
  disabled?: (row: T) => boolean
  /** Confirm before firing. `true` → default text; string → custom text/i18n
   *  key; function → per-row text. Inline actions use a popconfirm; overflow
   *  (More▾) actions use a dialog. */
  confirm?: boolean | string | ((row: T) => string)
  /** Render a divider before this item when it lands in the More▾ dropdown. */
  divider?: boolean
}

/**
 * Build the standard primary "Edit" action wired to `state.openEdit`.
 * Hidden per row when `state.canUpdate` is false (no update callback, or the
 * page's `permission` config denies `.update` for the current user) - the
 * check runs at render time via `show`, so it stays reactive to permission
 * loads. A caller-supplied `show` is AND-composed on top.
 */
export function editAction<T, TId extends string | number = string | number>(
  state: UseCrudPageReturn<T, TId>,
  opts: Partial<RowAction<T>> = {},
): RowAction<T> {
  const callerShow = opts.show
  return {
    key: 'edit',
    type: 'primary',
    ...opts,
    show: (row) => state.canUpdate !== false && (callerShow ? callerShow(row) : true),
    onClick: opts.onClick ?? ((row) => state.openEdit(row)),
  }
}

/** Build the standard read-only "View" action wired to `state.openView`. */
export function viewAction<T, TId extends string | number = string | number>(
  state: UseCrudPageReturn<T, TId>,
  opts: Partial<RowAction<T>> = {},
): RowAction<T> {
  return {
    key: 'view',
    ...opts,
    onClick: opts.onClick ?? ((row) => state.openView(row)),
  }
}

/**
 * Build the standard destructive "Delete" action (confirm + `state.handleDelete`).
 * Hidden per row when `state.canDelete` is false - same permission-reactive
 * `show` composition as {@link editAction}.
 */
export function deleteAction<T, TId extends string | number = string | number>(
  state: UseCrudPageReturn<T, TId>,
  opts: Partial<RowAction<T>> = {},
): RowAction<T> {
  const callerShow = opts.show
  return {
    key: 'delete',
    type: 'error',
    confirm: true,
    ...opts,
    show: (row) => state.canDelete !== false && (callerShow ? callerShow(row) : true),
    onClick:
      opts.onClick ??
      (async (row) => {
        await state.handleDelete([state.rowKey(row)])
      }),
  }
}

export interface RowActionsLayout {
  /** Max inline slots before the tail collapses into More▾ (default 2). */
  maxInline?: number
  /** Disable collapsing - render every action inline (default false). */
  collapse?: boolean
}

const GAP = 8
const CELL_PADDING = 24
const BTN_PADDING = 24
const ICON_WIDTH = 18
const MORE_WIDTH = 70
const MIN_WIDTH = 72

/** Rough rendered width of a small ghost button's label (CJK ≈ 15px, else 8px). */
function labelWidth(label: string): number {
  let w = 0
  for (const ch of label) {
    // The class intentionally includes the full-width space (U+3000) to size CJK glyphs.
    // eslint-disable-next-line no-irregular-whitespace
    w += /[㐀-鿿豈-﫿　-〿＀-￯]/.test(ch) ? 15 : 8
  }
  return Math.ceil(w) + BTN_PADDING
}

/** Best-effort static label for width estimation (function labels can't be
 *  resolved without a row, so fall back to a representative width). */
function staticLabel<T>(a: RowAction<T>): string {
  if (typeof a.label === 'string') return a.label
  const builtin: Record<string, string> = { edit: 'Edit', view: 'View', delete: 'Delete' }
  const hit = builtin[a.key]
  if (hit) return hit
  if (typeof a.label === 'function') return 'Action' // unknown per-row text
  return a.key
}

/**
 * Estimate the operation column width that fits the declared actions under the
 * given collapse layout. Assumes every action could be visible (worst case for
 * `show`) so the column never clips. A single action returns roughly one
 * button's width; never less than {@link MIN_WIDTH}.
 */
export function estimateRowActionsWidth<T>(
  actions: RowAction<T>[],
  layout: RowActionsLayout = {},
): number {
  const list = actions ?? []
  const maxInline = layout.maxInline ?? 2
  const collapse = layout.collapse ?? true
  const count = list.length
  if (count === 0) return MIN_WIDTH

  let inline: RowAction<T>[]
  let hasMore = false
  if (!collapse || count <= maxInline) {
    inline = list
  } else {
    inline = list.slice(0, Math.max(0, maxInline - 1))
    hasMore = true
  }

  let w = 0
  for (const a of inline) w += labelWidth(staticLabel(a)) + (a.icon ? ICON_WIDTH : 0)
  if (hasMore) w += MORE_WIDTH

  const slots = inline.length + (hasMore ? 1 : 0)
  w += GAP * Math.max(0, slots - 1)
  w += CELL_PADDING
  return Math.max(MIN_WIDTH, Math.ceil(w))
}

/**
 * Split a row's actions into the inline cluster + the overflow that lands in
 * the More▾ dropdown, applying the collapse rule. Shared by the table cell and
 * the mobile card footer so both render identically.
 */
export function splitRowActions<T>(
  actions: RowAction<T>[],
  row: T,
  layout: RowActionsLayout = {},
): { inline: RowAction<T>[]; overflow: RowAction<T>[] } {
  const maxInline = layout.maxInline ?? 2
  const collapse = layout.collapse ?? true
  const visible = (actions ?? []).filter((a) => (a.show ? a.show(row) : true))
  if (!collapse || visible.length <= maxInline) {
    return { inline: visible, overflow: [] }
  }
  return {
    inline: visible.slice(0, Math.max(0, maxInline - 1)),
    overflow: visible.slice(Math.max(0, maxInline - 1)),
  }
}
