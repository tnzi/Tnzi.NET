/**
 * `TDataGrid` and its pure helpers: the sort cycle and row order, header
 * sorting with `aria-sort` (local and remote), column widths by pointer and
 * keyboard, missing values, row numbers, and choosing cells with the keys of
 * an ARIA grid.
 */
import { afterEach, describe, expect, it } from 'vitest'
import { createApp, h, nextTick, ref, type App } from 'vue'

const { TDataGrid, nextDataGridSort, sortDataGridRows, DATA_GRID_MIN_COLUMN_WIDTH, DATA_GRID_KEY_STEP } = await import('../../src/components/controls/index')

let app: App | null = null
let host: HTMLElement | null = null

function mount(render: () => unknown): HTMLElement {
  host = document.createElement('div')
  document.body.appendChild(host)
  app = createApp({ render })
  app.mount(host)
  return host
}

afterEach(() => {
  app?.unmount()
  host?.remove()
  app = null
  host = null
  document.body.innerHTML = ''
})

const flush = async () => {
  await nextTick()
  await nextTick()
}

function key(target: Element, name: string, init: KeyboardEventInit = {}): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true, ...init })
  target.dispatchEvent(event)
  return event
}

const COLUMNS = [
  { name: 'id', kind: 'number' as const, hint: 'integer' },
  { name: 'name', hint: 'text' },
  { name: 'data', muted: true, sortable: false },
]
const ROWS = [
  [10, 'banana', null],
  [2, 'Apple', 'x'],
  [null, 'cherry', ''],
]

describe('data grid helpers', () => {
  it('cycles a column ascending, descending, then as given; another column starts ascending', () => {
    expect(nextDataGridSort(null, 1)).toEqual({ column: 1, direction: 'asc' })
    expect(nextDataGridSort({ column: 1, direction: 'asc' }, 1)).toEqual({ column: 1, direction: 'desc' })
    expect(nextDataGridSort({ column: 1, direction: 'desc' }, 1)).toBeNull()
    expect(nextDataGridSort({ column: 1, direction: 'desc' }, 0)).toEqual({ column: 0, direction: 'asc' })
  })

  it('orders numbers by value and text as read, missing values last both ways, ties as given', () => {
    expect(sortDataGridRows(ROWS, { column: 0, direction: 'asc' }, COLUMNS[0])).toEqual([1, 0, 2])
    expect(sortDataGridRows(ROWS, { column: 0, direction: 'desc' }, COLUMNS[0])).toEqual([0, 1, 2])
    expect(sortDataGridRows(ROWS, { column: 1, direction: 'asc' }, COLUMNS[1], 'en')).toEqual([1, 0, 2])
    expect(sortDataGridRows([[1], [1], [0]], { column: 0, direction: 'asc' }, { name: 'n', kind: 'number' })).toEqual([2, 0, 1])
    expect(sortDataGridRows(ROWS, null, COLUMNS[0])).toEqual([0, 1, 2])
  })
})

describe('TDataGrid', () => {
  function mountGrid(props: Record<string, unknown> = {}) {
    const sort = ref<unknown>(null)
    const widths = ref<number[] | null>(null)
    const active = ref<unknown>(null)
    const root = mount(() =>
      h(TDataGrid, {
        columns: COLUMNS,
        rows: ROWS,
        label: 'Rows of fruit',
        sort: sort.value,
        'onUpdate:sort': (value: unknown) => (sort.value = value),
        widths: widths.value,
        'onUpdate:widths': (value: number[] | null) => (widths.value = value),
        activeCell: active.value,
        'onUpdate:activeCell': (value: unknown) => (active.value = value),
        ...props,
      }),
    )
    const names = () => Array.from(root.querySelectorAll('tbody tr')).map((tr) => tr.querySelectorAll('td')[1]!.textContent)
    const headers = () => Array.from(root.querySelectorAll<HTMLElement>('thead th.t-data-grid__head'))
    return { root, sort, widths, active, names, headers }
  }

  it('is a named table with row numbers, numbers to the right and missing values set apart', () => {
    const { root } = mountGrid({ firstRow: 51 })
    const table = root.querySelector('table')!
    expect(table.getAttribute('aria-label')).toBe('Rows of fruit')
    expect(table.hasAttribute('role')).toBe(false)
    const numbers = Array.from(root.querySelectorAll('tbody th[scope="row"]')).map((th) => th.textContent)
    expect(numbers).toEqual(['51', '52', '53'])
    const first = root.querySelectorAll('tbody tr')[0]!.querySelectorAll('td')
    expect(first[0]!.className).toContain('is-number')
    expect(first[2]!.className).toContain('is-null')
    expect(first[2]!.textContent).toBe('NULL')
    const third = root.querySelectorAll('tbody tr')[2]!.querySelectorAll('td')
    expect(third[2]!.className).not.toContain('is-null')
    expect(third[2]!.className).toContain('is-muted')
  })

  it('a header click sorts the rows here and says so with aria-sort; a third click restores the order', async () => {
    const { names, headers, sort } = mountGrid()
    const button = () => headers()[1]!.querySelector<HTMLButtonElement>('[data-grid-sort]')!
    expect(headers()[1]!.getAttribute('aria-sort')).toBe('none')
    button().click()
    await flush()
    expect(sort.value).toEqual({ column: 1, direction: 'asc' })
    expect(headers()[1]!.getAttribute('aria-sort')).toBe('ascending')
    expect(names()).toEqual(['Apple', 'banana', 'cherry'])
    button().click()
    await flush()
    expect(headers()[1]!.getAttribute('aria-sort')).toBe('descending')
    expect(names()).toEqual(['cherry', 'banana', 'Apple'])
    button().click()
    await flush()
    expect(names()).toEqual(['banana', 'Apple', 'cherry'])
  })

  it('remote sorting only reports the sort; a column that does not sort has no button', async () => {
    const { names, headers, sort } = mountGrid({ sortMode: 'remote' })
    headers()[1]!.querySelector<HTMLButtonElement>('[data-grid-sort]')!.click()
    await flush()
    expect(sort.value).toEqual({ column: 1, direction: 'asc' })
    expect(names()).toEqual(['banana', 'Apple', 'cherry'])
    expect(headers()[2]!.querySelector('button')).toBeNull()
    expect(headers()[2]!.hasAttribute('aria-sort')).toBe(false)
  })

  it('the arrow keys on a column edge resize it, fixing every column first; Home takes it to the narrowest', async () => {
    const { headers, widths } = mountGrid()
    headers().forEach((th, index) => {
      th.getBoundingClientRect = () => ({ width: 100 + index * 10 }) as DOMRect
    })
    const edge = headers()[1]!.querySelector<HTMLElement>('[data-grid-resize]')!
    expect(edge.getAttribute('role')).toBe('separator')
    expect(edge.getAttribute('aria-label')).toBe('Resize column name')
    expect(edge.tabIndex).toBe(0)
    key(edge, 'ArrowRight')
    await flush()
    expect(widths.value).toEqual([100, 110 + DATA_GRID_KEY_STEP, 120])
    key(edge, 'ArrowLeft', { shiftKey: true })
    await flush()
    expect(widths.value![1]).toBe(110 + DATA_GRID_KEY_STEP - 4 * DATA_GRID_KEY_STEP)
    key(edge, 'Home')
    await flush()
    expect(widths.value![1]).toBe(DATA_GRID_MIN_COLUMN_WIDTH)
    expect(edge.getAttribute('aria-valuenow')).toBe(String(DATA_GRID_MIN_COLUMN_WIDTH))
  })

  it('dragging a column edge resizes it, never below the narrowest', async () => {
    const { headers, widths } = mountGrid()
    headers().forEach((th) => {
      th.getBoundingClientRect = () => ({ width: 100 }) as DOMRect
    })
    const edge = headers()[0]!.querySelector<HTMLElement>('[data-grid-resize]')!
    edge.dispatchEvent(new PointerEvent('pointerdown', { button: 0, clientX: 200, bubbles: true }))
    edge.dispatchEvent(new PointerEvent('pointermove', { clientX: 260, bubbles: true }))
    await flush()
    expect(widths.value![0]).toBe(160)
    edge.dispatchEvent(new PointerEvent('pointermove', { clientX: 0, bubbles: true }))
    edge.dispatchEvent(new PointerEvent('pointerup', { bubbles: true }))
    await flush()
    expect(widths.value![0]).toBe(DATA_GRID_MIN_COLUMN_WIDTH)
  })

  it('selectable: an ARIA grid with one Tab stop; arrows move, Enter chooses by the row in `rows`, Escape lets go', async () => {
    const { root, active, headers } = mountGrid({ selectable: true })
    expect(root.querySelector('table')!.getAttribute('role')).toBe('grid')
    const cells = () => Array.from(root.querySelectorAll<HTMLElement>('tbody td'))
    expect(cells().filter((cell) => cell.tabIndex === 0)).toHaveLength(1)
    headers()[1]!.querySelector<HTMLButtonElement>('[data-grid-sort]')!.click()
    await flush()
    const first = root.querySelector<HTMLElement>('[data-grid-cell="0:0"]')!
    first.focus()
    key(first, 'ArrowDown')
    await flush()
    const below = root.querySelector<HTMLElement>('[data-grid-cell="1:0"]')!
    expect(document.activeElement).toBe(below)
    key(below, 'ArrowRight')
    await flush()
    const chosen = root.querySelector<HTMLElement>('[data-grid-cell="1:1"]')!
    expect(document.activeElement).toBe(chosen)
    key(chosen, 'Enter')
    await flush()
    /* On screen row 1 is "banana" after sorting by name, row 0 of `rows`. */
    expect(active.value).toEqual({ row: 0, column: 1 })
    expect(chosen.getAttribute('aria-selected')).toBe('true')
    let reachedOutside = false
    const listener = () => (reachedOutside = true)
    document.addEventListener('keydown', listener)
    key(chosen, 'Escape')
    document.removeEventListener('keydown', listener)
    await flush()
    expect(active.value).toBeNull()
    expect(reachedOutside).toBe(false)
    key(chosen, 'End', { ctrlKey: true })
    await flush()
    expect(document.activeElement).toBe(root.querySelector('[data-grid-cell="2:2"]'))
  })

  it('a different set of columns clears the sort, the widths and the chosen cell', async () => {
    const columns = ref(COLUMNS)
    const sort = ref<unknown>({ column: 1, direction: 'asc' })
    const widths = ref<number[] | null>([100, 100, 100])
    mount(() =>
      h(TDataGrid, {
        columns: columns.value,
        rows: ROWS,
        label: 'Rows',
        sort: sort.value,
        'onUpdate:sort': (value: unknown) => (sort.value = value),
        widths: widths.value,
        'onUpdate:widths': (value: number[] | null) => (widths.value = value),
      }),
    )
    columns.value = [{ name: 'other' }]
    await flush()
    expect(sort.value).toBeNull()
    expect(widths.value).toBeNull()
  })
})
