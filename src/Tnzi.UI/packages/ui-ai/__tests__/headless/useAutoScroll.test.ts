import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { createApp, defineComponent, h, nextTick, ref, withDirectives } from 'vue'
import { useAutoScroll, followTop } from '../../src/headless/useAutoScroll'

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

/**
 * A scroller with a settable content height: scrollTop is clamped the way a
 * browser clamps it, and scroll events are dispatched by the test.
 */
function layout(el: HTMLElement, height: number, clientHeight: number) {
  const geometry = { el, height, clientHeight, top: 0 }
  Object.defineProperty(el, 'scrollHeight', { configurable: true, get: () => geometry.height })
  Object.defineProperty(el, 'clientHeight', { configurable: true, get: () => geometry.clientHeight })
  Object.defineProperty(el, 'scrollTop', {
    configurable: true,
    get: () => geometry.top,
    set: (v: number) => { geometry.top = Math.max(0, Math.min(v, geometry.height - geometry.clientHeight)) },
  })
  Object.defineProperty(el, 'scrollTo', {
    configurable: true,
    value: (opts: ScrollToOptions) => { el.scrollTop = opts.top ?? el.scrollTop },
  })
  el.getBoundingClientRect = () => ({ top: 0, bottom: geometry.clientHeight } as DOMRect)
  return geometry
}

/** MutationObserver callbacks are microtasks. */
async function flushObservers() {
  await Promise.resolve()
  await nextTick()
  await Promise.resolve()
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
    const geometry = layout(host.el(), 1000, 100)
    geometry.el.scrollTop = 900
    geometry.height = 1400

    host.el().appendChild(document.createElement('div'))
    await flushObservers()

    expect(host.el().scrollTop).toBe(1300)
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

describe('followTop', () => {
  it('goes to the bottom without a limit', () => {
    expect(followTop({ scrollTop: 0, scrollHeight: 1000, clientHeight: 400, limitTop: null })).toBe(600)
  })
  it('stops at the limit when it comes before the bottom', () => {
    expect(followTop({ scrollTop: 0, scrollHeight: 1000, clientHeight: 400, limitTop: 350 })).toBe(350)
  })
  it('goes to the bottom when the limit is further down', () => {
    expect(followTop({ scrollTop: 0, scrollHeight: 1000, clientHeight: 400, limitTop: 900 })).toBe(600)
  })
  it('never moves the view up', () => {
    expect(followTop({ scrollTop: 500, scrollHeight: 1000, clientHeight: 400, limitTop: 350 })).toBeNull()
    expect(followTop({ scrollTop: 600, scrollHeight: 1000, clientHeight: 400, limitTop: null })).toBeNull()
  })
  it('content shorter than the view needs no scroll', () => {
    expect(followTop({ scrollTop: 0, scrollHeight: 300, clientHeight: 400, limitTop: null })).toBeNull()
  })
})

describe('useAutoScroll following', () => {
  interface Host {
    hook: ReturnType<typeof useAutoScroll>
    el: HTMLElement
    geometry: ReturnType<typeof layout>
    grow: (height: number) => Promise<void>
    scrollTo: (top: number) => void
    unmount: () => void
  }

  function mount(opts: Parameters<typeof useAutoScroll>[0] = {}): Host {
    let hook!: ReturnType<typeof useAutoScroll>
    const shown = ref(false)
    const Host = defineComponent({
      setup() {
        hook = useAutoScroll({ threshold: 50, ...opts })
        return () => shown.value
          ? h('div', { ref: hook.containerRef, class: 'scroller' }, [
              h('div', { class: 'content' }, [
                h('div', { class: 'question', 'data-q': '' }),
                h('div', { class: 'nested' }),
                h('button', { class: 'step', 'aria-expanded': 'false' }),
              ]),
              h('div', { class: 'composer' }, [h('button', { class: 'menu', 'aria-expanded': 'false' }), h('textarea')]),
            ])
          : h('span')
      },
    })
    const root = document.createElement('div')
    document.body.appendChild(root)
    const app = createApp(Host)
    app.mount(root)
    let geometry!: ReturnType<typeof layout>
    shown.value = true
    return {
      get hook() { return hook },
      get el() { return root.querySelector('.scroller') as HTMLElement },
      get geometry() { return geometry },
      set geometry(g) { geometry = g },
      async grow(height: number) {
        geometry.height = height
        const content = root.querySelector('.content')!
        content.appendChild(document.createElement('p'))
        await flushObservers()
      },
      scrollTo(top: number) {
        this.el.scrollTop = top
        this.el.dispatchEvent(new Event('scroll'))
      },
      unmount: () => { app.unmount(); root.remove() },
    } as Host
  }

  async function ready(opts?: Parameters<typeof useAutoScroll>[0]): Promise<Host> {
    const host = mount(opts)
    await nextTick()
    await nextTick()
    host.geometry = layout(host.el, 1000, 400)
    host.hook.scrollToBottom('auto')
    host.el.dispatchEvent(new Event('scroll'))
    return host
  }

  function wheel(el: Element, deltaY: number) {
    el.dispatchEvent(new WheelEvent('wheel', { deltaY, bubbles: true }))
  }

  it('follows growth while at the bottom', async () => {
    const host = await ready()
    expect(host.el.scrollTop).toBe(600)
    await host.grow(1300)
    expect(host.el.scrollTop).toBe(900)
    expect(host.hook.following.value).toBe(true)
    host.unmount()
  })

  it('a wheel up stops following at once, even by a few pixels', async () => {
    const host = await ready()
    wheel(host.el, -3)
    expect(host.hook.following.value).toBe(false)
    host.scrollTo(597)
    await host.grow(1300)
    expect(host.el.scrollTop).toBe(597)
    host.unmount()
  })

  it('coming back down to the bottom resumes following', async () => {
    const host = await ready()
    wheel(host.el, -100)
    host.scrollTo(300)
    await host.grow(1300)
    expect(host.el.scrollTop).toBe(300)
    wheel(host.el, 100)
    host.scrollTo(880)
    expect(host.hook.following.value).toBe(true)
    await host.grow(1600)
    expect(host.el.scrollTop).toBe(1200)
    host.unmount()
  })

  it('scrolling up by other means (the scrollbar) stops following', async () => {
    const host = await ready()
    host.scrollTo(200)
    expect(host.hook.following.value).toBe(false)
    await host.grow(1300)
    expect(host.el.scrollTop).toBe(200)
    host.unmount()
  })

  it('content shrinking under a view at the bottom is not the reader scrolling up', async () => {
    const host = await ready()
    host.geometry.height = 800
    host.el.scrollTop = host.el.scrollTop
    host.el.dispatchEvent(new Event('scroll'))
    expect(host.el.scrollTop).toBe(400)
    expect(host.hook.following.value).toBe(true)
    await host.grow(1200)
    expect(host.el.scrollTop).toBe(800)
    host.unmount()
  })

  it('a touch dragging the content down stops following; dragging back to the bottom resumes', async () => {
    const host = await ready()
    const touch = (type: string, y: number) =>
      host.el.dispatchEvent(Object.assign(new Event(type, { bubbles: true }), { touches: [{ clientY: y }] }))
    touch('touchstart', 100)
    touch('touchmove', 140)
    expect(host.hook.following.value).toBe(false)
    host.scrollTo(560)
    touch('touchmove', 100)
    expect(host.hook.following.value).toBe(true)
    host.unmount()
  })

  it('upward keys stop following, except in a text field or outside the content', async () => {
    const host = await ready({ notContent: '.composer' })
    const key = (target: Element, k: string) =>
      target.dispatchEvent(new KeyboardEvent('keydown', { key: k, bubbles: true }))
    key(host.el.querySelector('textarea')!, 'ArrowUp')
    expect(host.hook.following.value).toBe(true)
    key(host.el.querySelector('.composer .menu')!, 'ArrowUp')
    expect(host.hook.following.value).toBe(true)
    key(host.el, 'PageUp')
    expect(host.hook.following.value).toBe(false)
    host.unmount()
  })

  it('opening a disclosure in the content stops following, one in the composer does not', async () => {
    const host = await ready({ notContent: '.composer' })
    host.el.querySelector('.composer .menu')!.dispatchEvent(new Event('pointerdown', { bubbles: true }))
    expect(host.hook.following.value).toBe(true)
    host.el.querySelector('.step')!.dispatchEvent(new Event('pointerdown', { bubbles: true }))
    expect(host.hook.following.value).toBe(false)
    await host.grow(1300)
    expect(host.el.scrollTop).toBe(600)
    host.unmount()
  })

  it('a wheel taken by a nested scroller that can still move is not the reader leaving', async () => {
    const host = await ready()
    const nested = host.el.querySelector('.nested') as HTMLElement
    nested.style.overflowY = 'auto'
    Object.defineProperty(nested, 'scrollHeight', { value: 500 })
    Object.defineProperty(nested, 'clientHeight', { value: 100 })
    Object.defineProperty(nested, 'scrollTop', { value: 50 })
    wheel(nested, -100)
    expect(host.hook.following.value).toBe(true)
    host.unmount()
  })

  it('a wheel up in content that does not scroll keeps following', async () => {
    const host = await ready()
    host.geometry.height = 300
    host.el.scrollTop = 0
    wheel(host.el, -100)
    expect(host.hook.following.value).toBe(true)
    host.unmount()
  })

  it('scrollToBottom resumes following after the reader left', async () => {
    const host = await ready()
    wheel(host.el, -100)
    host.scrollTo(100)
    host.hook.scrollToBottom('smooth')
    expect(host.hook.following.value).toBe(true)
    expect(host.el.scrollTop).toBe(600)
    host.el.dispatchEvent(new Event('scrollend'))
    await host.grow(1300)
    expect(host.el.scrollTop).toBe(900)
    host.unmount()
  })

  it('growth during a smooth scroll to the bottom waits for it to land, then catches up', async () => {
    const host = await ready()
    wheel(host.el, -100)
    host.scrollTo(100)
    host.hook.scrollToBottom('smooth')
    await host.grow(1300)
    expect(host.el.scrollTop).toBe(600)
    host.el.dispatchEvent(new Event('scrollend'))
    expect(host.el.scrollTop).toBe(900)
    host.unmount()
  })

  it('a smooth scroll that never reports its end is caught up with after a while', async () => {
    vi.useFakeTimers()
    try {
      const host = await ready()
      host.hook.scrollToBottom('smooth')
      host.geometry.height = 1300
      vi.advanceTimersByTime(1000)
      expect(host.el.scrollTop).toBe(900)
      host.unmount()
    } finally {
      vi.useRealTimers()
    }
  })

  describe('followTurn', () => {
    function placeQuestion(host: Host, top: number) {
      const q = host.el.querySelector('.question') as HTMLElement
      q.getBoundingClientRect = () => ({ top: top - host.el.scrollTop } as DOMRect)
      return q
    }

    it('brings the sent message into view and follows until it reaches the top', async () => {
      const host = await ready()
      host.scrollTo(100)
      // The question at 1000, a short reply below it: the view only has to
      // reach the bottom, and the previous content stays above the question.
      host.geometry.height = 1200
      const q = placeQuestion(host, 1000)
      host.hook.followTurn(() => q, { topOffset: 24 })
      expect(host.el.scrollTop).toBe(800)
      await host.grow(1600)
      expect(host.el.scrollTop).toBe(976)
      await host.grow(3000)
      expect(host.el.scrollTop).toBe(976)
      expect(host.hook.isAtBottom.value).toBe(false)
      host.unmount()
    })

    it('a reader who scrolls on to the bottom lifts the limit', async () => {
      const host = await ready()
      host.geometry.height = 1200
      const q = placeQuestion(host, 1000)
      host.hook.followTurn(() => q, { topOffset: 24 })
      await host.grow(3000)
      expect(host.el.scrollTop).toBe(976)
      wheel(host.el, 100)
      host.scrollTo(2600)
      await host.grow(3400)
      expect(host.el.scrollTop).toBe(3000)
      host.unmount()
    })

    it('reaching the bottom before the limit keeps it', async () => {
      const host = await ready()
      host.geometry.height = 1200
      const q = placeQuestion(host, 1000)
      host.hook.followTurn(() => q, { topOffset: 24 })
      wheel(host.el, 100)
      host.scrollTo(800)
      await host.grow(3000)
      expect(host.el.scrollTop).toBe(976)
      host.unmount()
    })

    it('scrolling up during the answer stops following', async () => {
      const host = await ready()
      host.geometry.height = 1200
      const q = placeQuestion(host, 1000)
      host.hook.followTurn(() => q, { topOffset: 24 })
      wheel(host.el, -100)
      host.scrollTo(700)
      await host.grow(1600)
      expect(host.el.scrollTop).toBe(700)
      host.unmount()
    })

    it('scrollToBottom drops the limit', async () => {
      const host = await ready()
      host.geometry.height = 1200
      const q = placeQuestion(host, 1000)
      host.hook.followTurn(() => q, { topOffset: 24 })
      await host.grow(3000)
      host.hook.scrollToBottom('auto')
      expect(host.el.scrollTop).toBe(2600)
      await host.grow(3200)
      expect(host.el.scrollTop).toBe(2800)
      host.unmount()
    })
  })
})

describe('useAutoScroll openAt', () => {
  /* A container that appears later, its geometry set as it is created (before
     the composable attaches to it). */
  function mountLater(openAt: () => number | null, height: number) {
    let captured!: AutoScrollHook
    const show = ref(false)
    let geometry!: ReturnType<typeof layout>
    const withGeometry = { created: (el: HTMLElement) => { geometry = layout(el, height, 500) } }
    const Host = defineComponent({
      setup() {
        captured = useAutoScroll({ threshold: 20, openAt })
        return () => (show.value ? withDirectives(h('div', { ref: captured.containerRef, class: 'scroll-host' }), [[withGeometry]]) : null)
      },
    })
    const root = document.createElement('div')
    document.body.appendChild(root)
    const app = createApp(Host)
    app.mount(root)
    return {
      hook: () => captured,
      show: async () => { show.value = true; await flushObservers() },
      geometry: () => geometry,
      el: () => root.querySelector('.scroll-host') as HTMLElement,
      unmount: () => { app.unmount(); root.remove() },
    }
  }

  it('opens where it is asked to, not following', async () => {
    const host = mountLater(() => 700, 3000)
    await host.show()
    expect(host.el().scrollTop).toBe(700)
    expect(host.hook().following.value).toBe(false)
    host.unmount()
  })

  it('keeps applying the position while the content grows, until the reader scrolls', async () => {
    const host = mountLater(() => 1500, 1200)
    await host.show()
    expect(host.el().scrollTop).toBe(700) // as far as the content allows
    host.geometry().height = 2600
    host.el().appendChild(document.createElement('p'))
    await flushObservers()
    expect(host.el().scrollTop).toBe(1500)
    host.unmount()
  })

  it('lets the reader take over before the position is reached', async () => {
    const host = mountLater(() => 1500, 1200)
    await host.show()
    host.el().dispatchEvent(new WheelEvent('wheel', { deltaY: -40 }))
    host.geometry().height = 2600
    host.el().appendChild(document.createElement('p'))
    await flushObservers()
    expect(host.el().scrollTop).toBe(700)
    host.unmount()
  })

  it('opens at the latest turn when it has no position', async () => {
    const host = mountLater(() => null, 3000)
    await host.show()
    expect(host.el().scrollTop).toBe(2500)
    expect(host.hook().following.value).toBe(true)
    host.unmount()
  })
})
