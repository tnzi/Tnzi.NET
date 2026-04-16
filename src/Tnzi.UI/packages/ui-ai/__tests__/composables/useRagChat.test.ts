import { describe, it, expect } from 'vitest'
import { useRagChat } from '../../src/composables/useRagChat'

describe('useRagChat', () => {
  it('starts with empty bases, empty citations, rag disabled', () => {
    const r = useRagChat()
    expect(r.selectedBaseIds.value).toEqual([])
    expect(r.citations.value).toEqual([])
    expect(r.isRagEnabled.value).toBe(false)
  })

  it('toggleBase adds when not present', () => {
    const r = useRagChat()
    r.toggleBase('kb1')
    expect(r.selectedBaseIds.value).toEqual(['kb1'])
    expect(r.isRagEnabled.value).toBe(true)
  })

  it('toggleBase removes when present', () => {
    const r = useRagChat()
    r.toggleBase('kb1')
    r.toggleBase('kb2')
    r.toggleBase('kb1')
    expect(r.selectedBaseIds.value).toEqual(['kb2'])
  })

  it('toggleBase round-trip to empty disables rag', () => {
    const r = useRagChat()
    r.toggleBase('kb1')
    r.toggleBase('kb1')
    expect(r.isRagEnabled.value).toBe(false)
  })

  it('clearBases empties selection', () => {
    const r = useRagChat()
    r.toggleBase('kb1')
    r.toggleBase('kb2')
    r.clearBases()
    expect(r.selectedBaseIds.value).toEqual([])
  })

  it('setCitations replaces citation list (copied, not aliased)', () => {
    const r = useRagChat()
    const arr = [{ title: 'A', url: '/a' }]
    r.setCitations(arr)
    expect(r.citations.value).toHaveLength(1)
    arr.push({ title: 'B', url: '/b' })
    expect(r.citations.value).toHaveLength(1)
  })

  it('clearCitations empties list', () => {
    const r = useRagChat()
    r.setCitations([{ title: 'A', url: '/a' }])
    r.clearCitations()
    expect(r.citations.value).toEqual([])
  })
})
