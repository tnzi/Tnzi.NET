import { describe, it, expect, vi } from 'vitest'
import {
  splitRowActions,
  estimateRowActionsWidth,
  editAction,
  viewAction,
  deleteAction,
  type RowAction,
} from '../../src/headless/rowActions'

type Row = { id: string; locked?: boolean }

const A = (key: string, extra: Partial<RowAction<Row>> = {}): RowAction<Row> => ({ key, ...extra })

describe('splitRowActions', () => {
  const row: Row = { id: '1' }

  it('keeps all inline when count <= maxInline', () => {
    const { inline, overflow } = splitRowActions([A('a'), A('b')], row, { maxInline: 2 })
    expect(inline.map((a) => a.key)).toEqual(['a', 'b'])
    expect(overflow).toEqual([])
  })

  it('collapses the tail into overflow when count > maxInline (default 2 → 1 inline + More)', () => {
    const { inline, overflow } = splitRowActions([A('a'), A('b'), A('c')], row, { maxInline: 2 })
    expect(inline.map((a) => a.key)).toEqual(['a'])
    expect(overflow.map((a) => a.key)).toEqual(['b', 'c'])
  })

  it('honours a larger maxInline', () => {
    const { inline, overflow } = splitRowActions([A('a'), A('b'), A('c'), A('d')], row, { maxInline: 3 })
    expect(inline.map((a) => a.key)).toEqual(['a', 'b'])
    expect(overflow.map((a) => a.key)).toEqual(['c', 'd'])
  })

  it('collapse=false renders every action inline', () => {
    const { inline, overflow } = splitRowActions([A('a'), A('b'), A('c'), A('d')], row, { collapse: false })
    expect(inline).toHaveLength(4)
    expect(overflow).toEqual([])
  })

  it('drops actions whose show() returns false BEFORE splitting', () => {
    const locked: Row = { id: '1', locked: true }
    const actions = [
      A('edit'),
      A('unlock', { show: (r) => r.locked === true }),
      A('lock', { show: (r) => r.locked !== true }),
      A('delete'),
    ]
    const { inline, overflow } = splitRowActions(actions, locked, { maxInline: 2 })
    // visible = [edit, unlock, delete] (lock hidden) → 3 > 2 → [edit] + More[unlock, delete]
    expect(inline.map((a) => a.key)).toEqual(['edit'])
    expect(overflow.map((a) => a.key)).toEqual(['unlock', 'delete'])
  })
})

describe('estimateRowActionsWidth', () => {
  it('returns a single-button width for one action (never the old fixed 150)', () => {
    const w = estimateRowActionsWidth([A('edit', { label: 'Edit' })])
    expect(w).toBeGreaterThanOrEqual(72)
    expect(w).toBeLessThan(120)
  })

  it('grows for two inline buttons', () => {
    const one = estimateRowActionsWidth([A('edit', { label: 'Edit' })])
    const two = estimateRowActionsWidth([A('edit', { label: 'Edit' }), A('del', { label: 'Delete' })])
    expect(two).toBeGreaterThan(one)
  })

  it('accounts for the More▾ button when actions overflow', () => {
    const w = estimateRowActionsWidth(
      [A('a', { label: 'Edit' }), A('b', { label: 'B' }), A('c', { label: 'C' })],
      { maxInline: 2 },
    )
    // 1 inline (Edit) + More → wider than a lone Edit button
    expect(w).toBeGreaterThan(estimateRowActionsWidth([A('a', { label: 'Edit' })]))
  })

  it('never returns less than the minimum even with no actions', () => {
    expect(estimateRowActionsWidth([])).toBeGreaterThanOrEqual(72)
  })

  it('collapse=false widens with every action (no More cap)', () => {
    const collapsed = estimateRowActionsWidth(
      [A('a', { label: 'Edit' }), A('b', { label: 'B' }), A('c', { label: 'C' }), A('d', { label: 'D' })],
      { maxInline: 2, collapse: true },
    )
    const expanded = estimateRowActionsWidth(
      [A('a', { label: 'Edit' }), A('b', { label: 'B' }), A('c', { label: 'C' }), A('d', { label: 'D' })],
      { collapse: false },
    )
    expect(expanded).toBeGreaterThan(collapsed)
  })
})

describe('built-in action factories', () => {
  function makeState() {
    return {
      openEdit: vi.fn(),
      openView: vi.fn(),
      handleDelete: vi.fn(),
      rowKey: (r: Row) => r.id,
    } as unknown as Parameters<typeof editAction<Row>>[0]
  }

  it('editAction wires onClick → state.openEdit and is primary', () => {
    const state = makeState()
    const a = editAction<Row>(state)
    expect(a.key).toBe('edit')
    expect(a.type).toBe('primary')
    a.onClick?.({ id: '1' })
    expect((state as unknown as { openEdit: ReturnType<typeof vi.fn> }).openEdit).toHaveBeenCalled()
  })

  it('viewAction wires onClick → state.openView', () => {
    const state = makeState()
    viewAction<Row>(state).onClick?.({ id: '1' })
    expect((state as unknown as { openView: ReturnType<typeof vi.fn> }).openView).toHaveBeenCalled()
  })

  it('deleteAction is destructive, confirms, and calls handleDelete with the row key', () => {
    const state = makeState()
    const a = deleteAction<Row>(state)
    expect(a.type).toBe('error')
    expect(a.confirm).toBe(true)
    a.onClick?.({ id: '42' })
    expect((state as unknown as { handleDelete: ReturnType<typeof vi.fn> }).handleDelete).toHaveBeenCalledWith(['42'])
  })

  it('factory opts override the defaults', () => {
    const state = makeState()
    const custom = vi.fn()
    const a = editAction<Row>(state, { label: 'Modify', onClick: custom })
    expect(a.label).toBe('Modify')
    a.onClick?.({ id: '1' })
    expect(custom).toHaveBeenCalled()
    expect((state as unknown as { openEdit: ReturnType<typeof vi.fn> }).openEdit).not.toHaveBeenCalled()
  })
})
