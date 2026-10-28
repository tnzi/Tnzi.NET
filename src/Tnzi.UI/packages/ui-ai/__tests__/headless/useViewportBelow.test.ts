import { describe, it, expect, afterEach } from 'vitest'
import { effectScope, nextTick, ref } from 'vue'
import { useViewportBelow } from '../../src/headless/useViewportBelow'

const initialWidth = window.innerWidth

function resizeTo(width: number): void {
  Object.defineProperty(window, 'innerWidth', { value: width, configurable: true, writable: true })
  window.dispatchEvent(new Event('resize'))
}

afterEach(() => {
  resizeTo(initialWidth)
})

describe('useViewportBelow', () => {
  it('is true strictly below the breakpoint (the test useSidebarState uses)', () => {
    resizeTo(767)
    const scope = effectScope()
    const narrow = scope.run(() => useViewportBelow(768))!
    expect(narrow.value).toBe(true)
    resizeTo(768)
    expect(narrow.value).toBe(false)
    scope.stop()
  })

  it('follows the viewport as it is resized', () => {
    resizeTo(1280)
    const scope = effectScope()
    const narrow = scope.run(() => useViewportBelow(768))!
    expect(narrow.value).toBe(false)
    resizeTo(375)
    expect(narrow.value).toBe(true)
    scope.stop()
  })

  it('follows a breakpoint that changes', async () => {
    resizeTo(720)
    const breakpoint = ref(768)
    const scope = effectScope()
    const narrow = scope.run(() => useViewportBelow(breakpoint))!
    expect(narrow.value).toBe(true)
    breakpoint.value = 640
    await nextTick()
    expect(narrow.value).toBe(false)
    scope.stop()
  })

  it('stops listening when its scope ends', () => {
    resizeTo(1280)
    const scope = effectScope()
    const narrow = scope.run(() => useViewportBelow(768))!
    scope.stop()
    resizeTo(375)
    expect(narrow.value).toBe(false)
  })
})
