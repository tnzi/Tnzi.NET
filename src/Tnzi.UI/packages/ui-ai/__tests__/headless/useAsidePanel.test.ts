import { beforeEach, describe, expect, it } from 'vitest'
import { effectScope, nextTick, ref } from 'vue'
import { useAsidePanel } from '../../src/headless/useAsidePanel'

const STORE = 'test-aside'

describe('useAsidePanel', () => {
  beforeEach(() => localStorage.clear())

  it('starts closed with the default width and tab', () => {
    const p = useAsidePanel({ key: 'a', storageKey: null, defaultWidth: 400, defaultTab: 'files' })
    expect(p.open.value).toBe(false)
    expect(p.width.value).toBe(400)
    expect(p.tab.value).toBe('files')
  })

  it('honours defaultOpen for a conversation never touched', () => {
    expect(useAsidePanel({ key: 'a', storageKey: null, defaultOpen: true }).open.value).toBe(true)
  })

  it('keeps state per conversation', async () => {
    const key = ref('a')
    const p = useAsidePanel({ key, storageKey: null })
    p.show('changes')
    p.setWidth(500)
    expect([p.open.value, p.tab.value, p.width.value]).toEqual([true, 'changes', 500])
    key.value = 'b'
    await nextTick()
    expect([p.open.value, p.tab.value, p.width.value]).toEqual([false, undefined, 420])
    key.value = 'a'
    await nextTick()
    expect([p.open.value, p.tab.value, p.width.value]).toEqual([true, 'changes', 500])
  })

  it('clamps the width to the bounds', () => {
    const p = useAsidePanel({ key: 'a', storageKey: null, minWidth: 300, maxWidth: 600 })
    p.setWidth(10)
    expect(p.width.value).toBe(300)
    p.setWidth(9999)
    expect(p.width.value).toBe(600)
  })

  it('toggles and records an explicit close over defaultOpen', () => {
    const p = useAsidePanel({ key: 'a', storageKey: null, defaultOpen: true })
    p.hide()
    expect(p.open.value).toBe(false)
    p.toggle()
    expect(p.open.value).toBe(true)
  })

  it('show without a tab keeps the current tab', () => {
    const p = useAsidePanel({ key: 'a', storageKey: null })
    p.setTab('x')
    p.show()
    expect(p.tab.value).toBe('x')
  })

  it('persists and restores per conversation', () => {
    const first = useAsidePanel({ key: 'a', storageKey: STORE })
    first.show('t')
    first.setWidth(480)
    const second = useAsidePanel({ key: 'a', storageKey: STORE })
    expect([second.open.value, second.tab.value, second.width.value]).toEqual([true, 't', 480])
    expect(useAsidePanel({ key: 'other', storageKey: STORE }).open.value).toBe(false)
  })

  it('ignores corrupt stored data', () => {
    localStorage.setItem(STORE, '{not json')
    expect(useAsidePanel({ key: 'a', storageKey: STORE }).open.value).toBe(false)
    localStorage.setItem(STORE, JSON.stringify({ a: { open: 'yes', width: 'wide' } }))
    expect(useAsidePanel({ key: 'a', storageKey: STORE }).open.value).toBe(false)
  })

  it('forgets the least recently changed conversations past maxEntries', () => {
    const key = ref('1')
    const p = useAsidePanel({ key, storageKey: STORE, maxEntries: 2 })
    for (const id of ['1', '2', '3']) {
      key.value = id
      p.show()
    }
    expect(Object.keys(JSON.parse(localStorage.getItem(STORE)!))).toEqual(['2', '3'])
  })

  it('hands the draft state to the real id and never persists the draft', () => {
    const key = ref<string | undefined>(undefined)
    const p = useAsidePanel({ key, storageKey: STORE })
    p.show('progress')
    expect(localStorage.getItem(STORE)).toBe('{}')
    key.value = 'thread-1'
    p.adoptDraft('thread-1')
    expect([p.open.value, p.tab.value]).toEqual([true, 'progress'])
    expect(Object.keys(JSON.parse(localStorage.getItem(STORE)!))).toEqual(['thread-1'])
    key.value = undefined
    expect(p.open.value).toBe(false)
  })

  it('works inside an effect scope', () => {
    const scope = effectScope()
    const p = scope.run(() => useAsidePanel({ key: 'a', storageKey: null }))!
    p.show()
    expect(p.open.value).toBe(true)
    scope.stop()
  })
})
