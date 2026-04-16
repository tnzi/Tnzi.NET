import { describe, it, expect } from 'vitest'
import { useEmbedMode } from '../../src/composables/useEmbedMode'

describe('useEmbedMode', () => {
  it('defaults to floating mode, closed, not minimized', () => {
    const e = useEmbedMode()
    expect(e.mode.value).toBe('floating')
    expect(e.isOpen.value).toBe(false)
    expect(e.isMinimized.value).toBe(false)
  })

  it('honors initialMode constructor arg', () => {
    expect(useEmbedMode('sidebar').mode.value).toBe('sidebar')
    expect(useEmbedMode('inline').mode.value).toBe('inline')
  })

  it('setMode changes mode', () => {
    const e = useEmbedMode()
    e.setMode('sidebar')
    expect(e.mode.value).toBe('sidebar')
    e.setMode('inline')
    expect(e.mode.value).toBe('inline')
  })

  it('open() sets isOpen=true and clears minimized', () => {
    const e = useEmbedMode()
    e.minimize()
    e.open()
    expect(e.isOpen.value).toBe(true)
    expect(e.isMinimized.value).toBe(false)
  })

  it('close() sets isOpen=false and clears minimized', () => {
    const e = useEmbedMode()
    e.open()
    e.minimize()
    e.close()
    expect(e.isOpen.value).toBe(false)
    expect(e.isMinimized.value).toBe(false)
  })

  it('toggle flips between open and closed', () => {
    const e = useEmbedMode()
    e.toggle()
    expect(e.isOpen.value).toBe(true)
    e.toggle()
    expect(e.isOpen.value).toBe(false)
  })

  it('minimize/expand control minimized state', () => {
    const e = useEmbedMode()
    e.minimize()
    expect(e.isMinimized.value).toBe(true)
    e.expand()
    expect(e.isMinimized.value).toBe(false)
  })
})
