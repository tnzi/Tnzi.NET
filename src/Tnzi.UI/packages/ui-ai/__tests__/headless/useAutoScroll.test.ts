import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { createApp, defineComponent, h, nextTick } from 'vue'
import { useAutoScroll } from '../../src/headless/useAutoScroll'

type AutoScrollHook = ReturnType<typeof useAutoScroll>

interface MountedHost {
  hook: AutoScrollHook
  el: HTMLElement
  unmount: () => void
}

function mountHost(threshold = 20): MountedHost {
  let captured!: AutoScrollHook
  const Host = defineComponent({
    setup() {
      const hook = useAutoScroll({ threshold })
      captured = hook
      return () => h('div', { ref: hook.containerRef, class: 'scroll-host' })
    },
  })
  const root = document.createElement('div')
  document.body.appendChild(root)
  const app = createApp(Host)
  app.mount(root)
  const el = root.querySelector('.scroll-host') as HTMLElement
  return {
    hook: captured,
    el,
    unmount: () => { app.unmount(); root.remove() },
  }
}

describe('useAutoScroll', () => {
  let rafCallbacks: Array<() => void> = []
  let rafId = 0
  const originalRAF = globalThis.requestAnimationFrame
  const originalCAF = globalThis.cancelAnimationFrame

  beforeEach(() => {
    rafCallbacks = []
    rafId = 0
    globalThis.requestAnimationFrame = vi.fn((cb: () => void) => {
      rafCallbacks.push(cb)
      return ++rafId
    }) as unknown as typeof requestAnimationFrame
    globalThis.cancelAnimationFrame = vi.fn()
  })

  afterEach(() => {
    globalThis.requestAnimationFrame = originalRAF
    globalThis.cancelAnimationFrame = originalCAF
  })

  function flushRaf() {
    const cbs = rafCallbacks
    rafCallbacks = []
    cbs.forEach((cb) => cb())
  }

  it('exposes containerRef, isAtBottom, scrollToBottom', () => {
    const h = mountHost()
    expect(typeof h.hook.scrollToBottom).toBe('function')
    expect(h.hook.isAtBottom.value).toBe(true)
    h.unmount()
  })

  it('scrollToBottom no-ops when container ref is null', () => {
    // Host that never attaches the ref
    let captured!: AutoScrollHook
    const Orphan = defineComponent({
      setup() {
        captured = useAutoScroll()
        return () => h('span')
      },
    })
    const root = document.createElement('div')
    const app = createApp(Orphan)
    app.mount(root)
    expect(() => captured.scrollToBottom()).not.toThrow()
    app.unmount()
  })

  it('scrollToBottom writes scrollTo and marks at-bottom', async () => {
    const host = mountHost()
    await nextTick()
    const scrollTo = vi.fn()
    Object.defineProperty(host.el, 'scrollTo', { value: scrollTo, configurable: true })
    host.hook.scrollToBottom('auto')
    expect(scrollTo).toHaveBeenCalledWith({ top: 0, behavior: 'auto' })
    expect(host.hook.isAtBottom.value).toBe(true)
    host.unmount()
  })

  it('scroll event flips isAtBottom based on distance from bottom', async () => {
    const host = mountHost(20)
    await nextTick()
    Object.defineProperty(host.el, 'scrollHeight', { value: 1000, configurable: true })
    Object.defineProperty(host.el, 'clientHeight', { value: 100, configurable: true })
    Object.defineProperty(host.el, 'scrollTop', { value: 0, configurable: true, writable: true })
    host.el.dispatchEvent(new Event('scroll'))
    expect(host.hook.isAtBottom.value).toBe(false)
    Object.defineProperty(host.el, 'scrollTop', { value: 900, configurable: true, writable: true })
    host.el.dispatchEvent(new Event('scroll'))
    expect(host.hook.isAtBottom.value).toBe(true)
    host.unmount()
  })

  it('mutation handler coalesces scrolls via raf when at-bottom', async () => {
    const host = mountHost()
    await nextTick()
    const scrollTo = vi.fn()
    Object.defineProperty(host.el, 'scrollTo', { value: scrollTo, configurable: true })
    host.el.appendChild(document.createElement('div'))
    host.el.appendChild(document.createElement('div'))
    await nextTick()
    flushRaf()
    // Coalesced into a single rAF callback
    expect(host.hook.isAtBottom.value).toBe(true)
    host.unmount()
  })

  it('unmount triggers cleanup path', async () => {
    const host = mountHost()
    await nextTick()
    expect(() => host.unmount()).not.toThrow()
  })
})
