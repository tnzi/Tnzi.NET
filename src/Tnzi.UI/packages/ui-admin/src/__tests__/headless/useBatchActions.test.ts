import { describe, it, expect, vi } from 'vitest'
import { useBatchActions, type BatchAction } from '../../headless/useBatchActions'

const actions: BatchAction[] = [
  { key: 'delete', label: 'Delete', variant: 'danger', confirmMessage: 'Are you sure?' },
  { key: 'export', label: 'Export' },
]

describe('useBatchActions', () => {
  it('initializes with empty selection', () => {
    const ba = useBatchActions({ actions })
    expect(ba.selectedKeys.value).toEqual([])
    expect(ba.hasSelection.value).toBe(false)
    expect(ba.selectedCount.value).toBe(0)
  })

  it('reflects selection state when selectedKeys are updated', () => {
    const ba = useBatchActions({ actions })
    ba.selectedKeys.value = ['1', '2', '3']
    expect(ba.hasSelection.value).toBe(true)
    expect(ba.selectedCount.value).toBe(3)
  })

  it('execute calls onAction with the correct key and a snapshot of selected keys', async () => {
    const onAction = vi.fn().mockResolvedValue(undefined)
    const ba = useBatchActions({ actions, onAction })
    ba.selectedKeys.value = ['a', 'b']
    await ba.execute('delete')
    expect(onAction).toHaveBeenCalledWith('delete', ['a', 'b'])
  })

  it('execute does nothing when selection is empty', async () => {
    const onAction = vi.fn().mockResolvedValue(undefined)
    const ba = useBatchActions({ actions, onAction })
    await ba.execute('delete')
    expect(onAction).not.toHaveBeenCalled()
  })

  it('clearSelection resets selectedKeys to empty', () => {
    const ba = useBatchActions({ actions })
    ba.selectedKeys.value = ['x', 'y']
    ba.clearSelection()
    expect(ba.selectedKeys.value).toEqual([])
    expect(ba.hasSelection.value).toBe(false)
  })
})
