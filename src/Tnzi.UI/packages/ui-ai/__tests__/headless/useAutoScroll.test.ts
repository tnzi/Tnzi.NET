import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { createApp, defineComponent, h, nextTick, ref } from 'vue'
import { useAutoScroll } from '../../src/headless/useAutoScroll'

type AutoScrollHook = ReturnType<typeof useAutoScroll>

interface MountedHost {
  hook: AutoScrollHook
  el: HTMLElement
  unmount: () => void
}

function mountHost(threshold = 20, follow = true): MountedHost {
  let captured!: AutoScrollHook
  const Host = defineComponent({
    setup() {
      const hook = useAutoScroll({ threshold, follow })
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

  // follow: false is TChatApp's reading model: a streaming reply never moves
  // the view, but whether the view is still at the bottom keeps up with the
  // content, for the "scroll to the latest" button.
  it('follow: false never scrolls on new content, and re-measures isAtBottom', async () => {
    const host = mountHost(20, false)
    await nextTick()
    const scrollTo = vi.fn()
    Object.defineProperty(host.el, 'scrollTo', { value: scrollTo, configurable: true })
    Object.defineProperty(host.el, 'scrollHeight', { value: 1000, configurable: true })
    Object.defineProperty(host.el, 'clientHeight', { value: 100, configurable: true })
    Object.defineProperty(host.el, 'scrollTop', { value: 900, configurable: true, writable: true })
    expect(host.hook.isAtBottom.value).toBe(true)

    Object.defineProperty(host.el, 'scrollHeight', { value: 1600, configurable: true })
    host.el.appendChild(document.createElement('div'))
    await nextTick()
    flushRaf()
    expect(scrollTo).not.toHaveBeenCalled()
    expect(host.hook.isAtBottom.value).toBe(false)
    host.unmount()
  })

  // TChatApp renders its thread scroller only once there are messages (the
  // landing page comes first), so the container appears after mount. Binding
  // in onMounted alone left it with no scroll listener and no observer, and
  // streaming answers grew below the fold without the view following them.
  function mountLateHost() {
    const shown = ref(false)
    let captured!: AutoScrollHook
    const Host = defineComponent({
      setup() {
        captured = useAutoScroll({ threshold: 20 })
        return () => (shown.value ? h('div', { ref: captured.containerRef, class: 'late' }) : h('span'))
      },
    })
    const root = document.createElement('div')
    document.body.appendChild(root)
    const app = createApp(Host)
    app.mount(root)
    return {
      shown,
      hook: () => captured,
      el: () => root.querySelector('.late') as HTMLElement,
      unmount: () => { app.unmount(); root.remove() },
    }
  }

  function scrolledUp(el: HTMLElement) {
    Object.defineProperty(el, 'scrollHeight', { value: 1000, configurable: true })
    Object.defineProperty(el, 'clientHeight', { value: 100, configurable: true })
    Object.defineProperty(el, 'scrollTop', { value: 0, configurable: true, writable: true })
    el.dispatchEvent(new Event('scroll'))
  }

  it('attaches to a container that is rendered after mount', async () => {
    const host = mountLateHost()
    host.shown.value = true
    await nextTick()
    await nextTick()

    scrolledUp(host.el())
    expect(host.hook().isAtBottom.value).toBe(false)
    host.unmount()
  })

  it('follows new content in a container rendered after mount', async () => {
    const host = mountLateHost()
    host.shown.value = true
    await nextTick()
    await nextTick()
    const scrollTo = vi.fn()
    Object.defineProperty(host.el(), 'scrollTo', { value: scrollTo, configurable: true })

    host.el().appendChild(document.createElement('div'))
    await nextTick()
    await new Promise((resolve) => setTimeout(resolve, 0))
    flushRaf()

    expect(scrollTo).toHaveBeenCalled()
    host.unmount()
  })

  it('moves to a re-created container and lets go of the old one', async () => {
    const host = mountLateHost()
    host.shown.value = true
    await nextTick()
    await nextTick()
    const first = host.el()

    host.shown.value = false
    await nextTick()
    host.shown.value = true
    await nextTick()
    await nextTick()
    const second = host.el()
    expect(second).not.toBe(first)

    scrolledUp(first)
    expect(host.hook().isAtBottom.value).toBe(true)
    scrolledUp(second)
    expect(host.hook().isAtBottom.value).toBe(false)
    host.unmount()
  })

  it('opens a newly attached container at the bottom', async () => {
    const host = mountLateHost()
    const scrollHeight = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'scrollHeight')
    Object.defineProperty(HTMLElement.prototype, 'scrollHeight', { configurable: true, get: () => 800 })
    try {
      host.shown.value = true
      await nextTick()
      await nextTick()
      expect(host.el().scrollTop).toBe(800)
    } finally {
      if (scrollHeight) Object.defineProperty(HTMLElement.prototype, 'scrollHeight', scrollHeight)
      else delete (HTMLElement.prototype as unknown as Record<string, unknown>).scrollHeight
      host.unmount()
    }
  })

  it('unmount triggers cleanup path', async () => {
    const host = mountHost()
    await nextTick()
    expect(() => host.unmount()).not.toThrow()
  })
})
