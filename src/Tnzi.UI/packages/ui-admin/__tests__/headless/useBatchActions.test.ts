import { describe, expect, it } from 'vitest'
import { useBatchActions } from '../../src/headless/useBatchActions'

describe('useBatchActions', () => {
  it('starts with empty selection', () => {
    const ba = useBatchActions<string>()
    expect(ba.selectedCount.value).toBe(0)
    expect(ba.hasSelection.value).toBe(false)
    expect(ba.selectedIds.value).toEqual([])
  })

  it('select/unselect adds and removes ids', () => {
    const ba = useBatchActions<string>()
    ba.select('a')
    ba.select('b')
    expect(ba.selectedCount.value).toBe(2)
    expect(ba.hasSelection.value).toBe(true)
    expect(ba.isSelected('a')).toBe(true)
    ba.unselect('a')
    expect(ba.isSelected('a')).toBe(false)
    expect(ba.selectedCount.value).toBe(1)
  })

  it('toggle flips membership', () => {
    const ba = useBatchActions<number>()
    ba.toggle(1)
    expect(ba.isSelected(1)).toBe(true)
    ba.toggle(1)
    expect(ba.isSelected(1)).toBe(false)
  })

  it('selectAll replaces selection', () => {
    const ba = useBatchActions<string>()
    ba.select('x')
    ba.selectAll(['a', 'b', 'c'])
    expect(ba.selectedCount.value).toBe(3)
    expect(ba.isSelected('x')).toBe(false)
    expect(ba.isSelected('a')).toBe(true)
  })

  it('clear empties selection', () => {
    const ba = useBatchActions<string>()
    ba.selectAll(['a', 'b'])
    ba.clear()
    expect(ba.selectedCount.value).toBe(0)
    expect(ba.hasSelection.value).toBe(false)
  })

  it('selectedIds returns array copy', () => {
    const ba = useBatchActions<string>()
    ba.selectAll(['a', 'b'])
    const ids = ba.selectedIds.value
    expect(ids).toEqual(['a', 'b'])
    // mutating the returned array should not affect the store
    ids.push('c')
    expect(ba.selectedCount.value).toBe(2)
  })
})
