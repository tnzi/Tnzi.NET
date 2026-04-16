import { beforeEach, describe, expect, it } from 'vitest'
import { useColumnSettings, type ColumnDef } from '../../src/headless/useColumnSettings'

const baseColumns: ColumnDef[] = [
  { key: 'id', title: 'ID' },
  { key: 'name', title: 'Name' },
  { key: 'email', title: 'Email', visible: false },
  { key: 'createdAt', title: 'Created At' },
]

describe('useColumnSettings', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  it('initialises with provided columns', () => {
    const cs = useColumnSettings({ pageId: 'users', columns: baseColumns })
    expect(cs.orderedKeys.value).toEqual(['id', 'name', 'email', 'createdAt'])
    expect(cs.hiddenKeys.value.has('email')).toBe(true)
    expect(cs.visibleColumns.value.map((c) => c.key)).toEqual(['id', 'name', 'createdAt'])
  })

  it('hide(key) flips visibility', () => {
    const cs = useColumnSettings({ pageId: 'users', columns: baseColumns })
    cs.hide('name')
    expect(cs.hiddenKeys.value.has('name')).toBe(true)
    expect(cs.visibleColumns.value.map((c) => c.key)).toEqual(['id', 'createdAt'])
  })

  it('show(key) restores visibility', () => {
    const cs = useColumnSettings({ pageId: 'users', columns: baseColumns })
    cs.show('email')
    expect(cs.hiddenKeys.value.has('email')).toBe(false)
    expect(cs.visibleColumns.value.map((c) => c.key)).toContain('email')
  })

  it('reorder(newKeys) updates ordering', () => {
    const cs = useColumnSettings({ pageId: 'users', columns: baseColumns })
    cs.reorder(['createdAt', 'id', 'name', 'email'])
    expect(cs.orderedKeys.value).toEqual(['createdAt', 'id', 'name', 'email'])
    expect(cs.visibleColumns.value.map((c) => c.key)).toEqual(['createdAt', 'id', 'name'])
  })

  it('reset() restores original config', () => {
    const cs = useColumnSettings({ pageId: 'users', columns: baseColumns })
    cs.hide('id')
    cs.show('email')
    cs.reorder(['name', 'id', 'createdAt', 'email'])
    cs.reset()
    expect(cs.orderedKeys.value).toEqual(['id', 'name', 'email', 'createdAt'])
    expect(cs.hiddenKeys.value.has('email')).toBe(true)
    expect(cs.hiddenKeys.value.has('id')).toBe(false)
  })

  it('persists state to localStorage under pageId key', async () => {
    const cs = useColumnSettings({ pageId: 'users', columns: baseColumns })
    cs.hide('name')
    cs.reorder(['name', 'id', 'email', 'createdAt'])
    await new Promise((r) => setTimeout(r, 0))
    const raw = localStorage.getItem('tnzi-admin:cols:users')
    expect(raw).not.toBeNull()
    const parsed = JSON.parse(raw!)
    expect(parsed.hidden).toContain('name')
    expect(parsed.order).toEqual(['name', 'id', 'email', 'createdAt'])
  })

  it('restores state from localStorage on init', () => {
    localStorage.setItem(
      'tnzi-admin:cols:users',
      JSON.stringify({ hidden: ['id'], order: ['createdAt', 'name', 'email', 'id'] }),
    )
    const cs = useColumnSettings({ pageId: 'users', columns: baseColumns })
    expect(cs.orderedKeys.value).toEqual(['createdAt', 'name', 'email', 'id'])
    expect(cs.hiddenKeys.value.has('id')).toBe(true)
    expect(cs.visibleColumns.value.map((c) => c.key)).toEqual(['createdAt', 'name', 'email'])
  })
})
