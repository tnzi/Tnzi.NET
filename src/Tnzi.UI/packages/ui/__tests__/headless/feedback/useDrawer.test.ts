import { describe, it, expect } from 'vitest'
import { useDrawer } from '../../../src/headless/feedback/useDrawer'

describe('useDrawer', () => {
  it('defaults placement to right and width to 400px', () => {
    const { state } = useDrawer()
    expect(state.value.show).toBe(false)
    expect(state.value.placement).toBe('right')
    expect(state.value.width).toBe('400px')
    expect(state.value.title).toBe('')
  })

  it('honors constructor defaults', () => {
    const { state } = useDrawer({ title: 'Hi', width: '600px', placement: 'left' })
    expect(state.value.title).toBe('Hi')
    expect(state.value.width).toBe('600px')
    expect(state.value.placement).toBe('left')
  })

  it('open() sets show=true and merges options', () => {
    const { state, open } = useDrawer()
    open({ title: 'Details', width: '500px', placement: 'bottom' })
    expect(state.value.show).toBe(true)
    expect(state.value.title).toBe('Details')
    expect(state.value.width).toBe('500px')
    expect(state.value.placement).toBe('bottom')
  })

  it('open() without args keeps previous fields but flips show', () => {
    const { state, open } = useDrawer({ title: 'Persist', width: '300px' })
    open()
    expect(state.value.show).toBe(true)
    expect(state.value.title).toBe('Persist')
    expect(state.value.width).toBe('300px')
  })

  it('close() sets show=false without touching other fields', () => {
    const { state, open, close } = useDrawer({ title: 'T', width: '400px' })
    open()
    close()
    expect(state.value.show).toBe(false)
    expect(state.value.title).toBe('T')
  })
})
