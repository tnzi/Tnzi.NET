import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { effectScope, nextTick } from 'vue'
import { useSidebarState } from '../../src/headless/useSidebarState'

describe('useSidebarState', () => {
  beforeEach(() => {
    localStorage.clear()
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1280 })
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('defaults to expanded mode', () => {
    const { mode } = useSidebarState({ storageKey: null })
    expect(mode.value).toBe('expanded')
  })

  it('accepts an initial mode', () => {
    const { mode } = useSidebarState({ initialMode: 'icon', storageKey: null })
    expect(mode.value).toBe('icon')
  })

  it('cycles expanded → icon → hidden via cycle()', () => {
    const { mode, cycle } = useSidebarState({ initialMode: 'expanded', storageKey: null })
    cycle()
    expect(mode.value).toBe('icon')
    cycle()
    expect(mode.value).toBe('hidden')
    cycle()
    expect(mode.value).toBe('expanded')
  })

  it('setMode updates mode directly', () => {
    const { mode, setMode } = useSidebarState({ storageKey: null })
    setMode('hidden')
    expect(mode.value).toBe('hidden')
  })

  it('persists mode to localStorage when storageKey given', () => {
    const { setMode } = useSidebarState({ storageKey: 'test-sidebar' })
    setMode('icon')
    expect(localStorage.getItem('test-sidebar')).toBe('icon')
  })

  it('restores mode from localStorage on init', () => {
    localStorage.setItem('test-sidebar', 'hidden')
    const { mode } = useSidebarState({ storageKey: 'test-sidebar' })
    expect(mode.value).toBe('hidden')
  })

  it('ignores invalid localStorage values', () => {
    localStorage.setItem('test-sidebar', 'garbage')
    const { mode } = useSidebarState({ storageKey: 'test-sidebar', initialMode: 'expanded' })
    expect(mode.value).toBe('expanded')
  })

  it('null storageKey disables persistence', () => {
    const { setMode } = useSidebarState({ storageKey: null })
    setMode('icon')
    expect(localStorage.length).toBe(0)
  })

  it('isMobile is true when window width < breakpoint', async () => {
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 500 })
    const { isMobile } = useSidebarState({ mobileBreakpoint: 768, storageKey: null })
    await nextTick()
    expect(isMobile.value).toBe(true)
  })

  it('isMobile is false when window width >= breakpoint', async () => {
    const { isMobile } = useSidebarState({ mobileBreakpoint: 768, storageKey: null })
    await nextTick()
    expect(isMobile.value).toBe(false)
  })

  it('transitioning to mobile forces mode to hidden and remembers desktop mode', async () => {
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1280 })
    const { mode, setMode } = useSidebarState({ mobileBreakpoint: 768, storageKey: null })
    setMode('icon')
    expect(mode.value).toBe('icon')

    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 500 })
    window.dispatchEvent(new Event('resize'))
    await nextTick()

    expect(mode.value).toBe('hidden')

    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1280 })
    window.dispatchEvent(new Event('resize'))
    await nextTick()

    expect(mode.value).toBe('icon')
  })
  // -------------------------------------------------------------------------
  // Cleanup (regression: the resize listener was attached unconditionally but
  // only detached inside a component, so every scope-less call leaked one.)
  // -------------------------------------------------------------------------

  it('detaches the resize listener via dispose()', async () => {
    const { mode, setMode, dispose } = useSidebarState({
      mobileBreakpoint: 768,
      storageKey: null,
    })
    setMode('icon')

    dispose()

    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 500 })
    window.dispatchEvent(new Event('resize'))
    await nextTick()

    // Still 'icon': the disposed instance no longer reacts to resizes.
    expect(mode.value).toBe('icon')
  })

  it('is idempotent when dispose() is called twice', () => {
    const { dispose } = useSidebarState({ storageKey: null })
    dispose()
    expect(() => dispose()).not.toThrow()
  })

  it('cleans up automatically when the owning effect scope stops', async () => {
    const scope = effectScope()
    const state = scope.run(() => useSidebarState({ mobileBreakpoint: 768, storageKey: null }))!
    state.setMode('icon')

    scope.stop()

    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 500 })
    window.dispatchEvent(new Event('resize'))
    await nextTick()

    expect(state.mode.value).toBe('icon')
  })
})
