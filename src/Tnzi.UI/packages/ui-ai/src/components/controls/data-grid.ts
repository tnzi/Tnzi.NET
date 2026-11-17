/**
 * What `TDataGrid` decides without rendering: the shape of its columns, the
 * sort a header click moves to, the order the rows are shown in, and the
 * widths a resize produces. Pure, so it is tested without a page.
 */

/** One column of a `TDataGrid`. */
export interface DataGridColumn {
  /** The header's words. */
  readonly name: string
  /** What the header's tooltip says (a column's type, say); the name when unset. */
  readonly hint?: string
  /**
   * `number`: cells to the right with tabular digits, sorted by value; `text`
   * (default): sorted as people read it. `muted` draws the cells in the
   * secondary colour (binary data, an id nobody reads).
   */
  readonly kind?: 'text' | 'number'
  readonly muted?: boolean
  /** Turns sorting off for this column (its header is then plain text). */
  readonly sortable?: boolean
  /** A sort of its own, for values the default compare does not know; `null`s are kept last for it. */
  readonly compare?: (a: unknown, b: unknown) => number
  /** How a value reads in its cell; `String(value)` by default. Not called for `null` / `undefined`. */
  readonly format?: (value: unknown) => string
}

export type DataGridSortDirection = 'asc' | 'desc'

/** The column (its index) the rows are sorted by, and which way. */
export interface DataGridSort {
  readonly column: number
  readonly direction: DataGridSortDirection
}

/** A cell, by its row's index in `rows` (not its place on screen) and its column's index. */
export interface DataGridCell {
  readonly row: number
  readonly column: number
}

/** The narrowest a column is dragged to. */
export const DATA_GRID_MIN_COLUMN_WIDTH = 48
/** How far an arrow key moves a column's edge; Shift moves it four times as far. */
export const DATA_GRID_KEY_STEP = 16

/** The sort after a click on a header: ascending, then descending, then as given. */
export function nextDataGridSort(sort: DataGridSort | null, column: number): DataGridSort | null {
  if (sort?.column !== column) return { column, direction: 'asc' }
  return sort.direction === 'asc' ? { column, direction: 'desc' } : null
}

function isMissing(value: unknown): boolean {
  return value === null || value === undefined
}

/**
 * The order to show the rows in, as indexes into `rows`: by the sorted
 * column (numbers by value, text as people read it in `locale`, or the
 * column's own `compare`), missing values last either way, rows that tie in
 * the order given.
 */
export function sortDataGridRows(
  rows: ReadonlyArray<ReadonlyArray<unknown>>,
  sort: DataGridSort | null,
  column: DataGridColumn | undefined,
  locale?: string,
): number[] {
  const order = rows.map((_, index) => index)
  if (!sort || !column) return order
  const collator = new Intl.Collator(locale, { numeric: true, sensitivity: 'base' })
  const sign = sort.direction === 'asc' ? 1 : -1
  const compare = column.compare ?? ((a: unknown, b: unknown): number => {
    if (column.kind === 'number') {
      const difference = Number(a) - Number(b)
      if (!Number.isNaN(difference) && difference !== 0) return difference
    }
    return collator.compare(String(a), String(b))
  })
  return order.sort((a, b) => {
    const left = rows[a]?.[sort.column]
    const right = rows[b]?.[sort.column]
    if (isMissing(left) || isMissing(right)) {
      if (isMissing(left) && isMissing(right)) return a - b
      return isMissing(left) ? 1 : -1
    }
    return sign * compare(left, right) || a - b
  })
}

/** `widths` with column `column` set to `width`, never narrower than the minimum. */
export function withColumnWidth(widths: readonly number[], column: number, width: number): number[] {
  return widths.map((current, index) => (index === column ? Math.max(DATA_GRID_MIN_COLUMN_WIDTH, Math.round(width)) : current))
}

/** A cell's text: the column's `format`, else `String(value)`; `nullText` for a missing value. */
export function dataGridCellText(value: unknown, column: DataGridColumn | undefined, nullText: string): string {
  if (isMissing(value)) return nullText
  return column?.format ? column.format(value) : String(value)
}
