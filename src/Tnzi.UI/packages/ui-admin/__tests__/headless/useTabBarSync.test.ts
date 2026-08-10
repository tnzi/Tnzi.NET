/**
 * `useTabBarSync` - re-measures naive-ui's active-tab underline when the strip
 * re-flows after mount.
 *
 * happy-dom ships a ResizeObserver that is a documented no-op stub (observe /
 * unobserve / disconnect all have empty bodies), so these tests install their
 * own and drive it by hand. That is also the only way to assert WHICH elements
 * get observed, which is half the behaviour: observing the panes instead of the
 * tabs would fire on every content reflow.
 */
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { defineComponent, nextTick, shallowRef, ref, type PropType } from 'vue'
import { mount } from '@vue/test-utils'
import { useTabBarSync, type TabBarSyncTarget } from '../../src/headless/useTabBarSync'

class FakeResizeObserver {
  static instances: FakeResizeObserver[] = []
  targets: Element[] = []
  disconnectCount = 0

  constructor(private readonly cb: ResizeObserverCallback) {
    FakeResizeObserver.instances.push(this)
  }

  observe(el: Element): void {
    this.targets.push(el)
  }

  unobserve(el: Element): void {
    this.targets = this.targets.filter((t) => t !== el)
  }

  disconnect(): void {
    this.targets = []
    this.disconnectCount += 1
  }

  /** Report a resize the way the browser would. */
  fire(): void {
    this.cb([] as unknown as ResizeObserverEntry[], this as unknown as ResizeObserver)
  }

  static get latest(): FakeResizeObserver {
    return FakeResizeObserver.instances[FakeResizeObserver.instances.length - 1]!
  }

  static observedNames(): (string | null)[] {
    return FakeResizeObserver.latest.targets.map((el) => el.getAttribute('data-name'))
  }
}

const originalResizeObserver = globalThis.ResizeObserver

/**
 * Stands in for the naive tab strip: a nav holding `[data-name]` tabs, plus a
 * pane that carries a `data-name` of its own (pane content is arbitrary
 * consumer markup - the hook must not reach into it).
 */
const Host = defineComponent({
  props: {
    names: { type: Array as PropType<string[]>, required: true },
    sync: { type: Function as PropType<() => void>, required: true },
  },
  setup(props) {
    const rootRef = ref<HTMLElement | null>(null)
    // Stable object with a live `$el`, so the hook sees the root element at its
    // own `onMounted` regardless of hook-registration order.
    const target = shallowRef<TabBarSyncTarget>({
      syncBarPosition: () => props.sync(),
      get $el() {
        return rootRef.value
      },
    })
    useTabBarSync(target, () => props.names.join('|'))
    return { rootRef }
  },
  template: `
    <div ref="rootRef" class="x-tabs">
      <div class="x-tabs-nav">
        <div v-for="n in names" :key="n" class="x-tabs-tab" :data-name="n"></div>
      </div>
      <div class="x-tabs-pane"><span data-name="pane-decoy"></span></div>
    </div>
  `,
})

describe('useTabBarSync', () => {
  beforeEach(() => {
    FakeResizeObserver.instances = []
    globalThis.ResizeObserver = FakeResizeObserver as unknown as typeof ResizeObserver
  })

  afterEach(() => {
    globalThis.ResizeObserver = originalResizeObserver
  })

  it('observes the tabs in the nav and nothing inside the panes', () => {
    mount(Host, { props: { names: ['a', 'b'], sync: vi.fn() } })

    expect(FakeResizeObserver.observedNames()).toEqual(['a', 'b'])
  })

  it('re-measures when an observed tab changes size', () => {
    const sync = vi.fn()
    mount(Host, { props: { names: ['a', 'b'], sync } })
    // One measurement at mount - the state naive itself leaves behind.
    expect(sync).toHaveBeenCalledTimes(1)

    // A label grew (badge arrived). This is the whole bug: naive never hears
    // about it, because the strip's own width did not change.
    FakeResizeObserver.latest.fire()

    expect(sync).toHaveBeenCalledTimes(2)
  })

  it('re-reads the strip and re-measures when the tab set changes', async () => {
    const sync = vi.fn()
    const wrapper = mount(Host, { props: { names: ['a', 'b'], sync } })
    sync.mockClear()

    // A tab inserted BEFORE the active one shifts it without resizing anything
    // that already existed, so no resize is ever reported - the measurement has
    // to be unconditional here.
    await wrapper.setProps({ names: ['a', 'x', 'b'] })

    expect(FakeResizeObserver.observedNames()).toEqual(['a', 'x', 'b'])
    expect(sync).toHaveBeenCalled()
  })

  it('does not re-read the strip when only a label changed', async () => {
    const wrapper = mount(Host, { props: { names: ['a', 'b'], sync: vi.fn() } })
    const before = FakeResizeObserver.latest.disconnectCount

    // Same tabs, new array identity: the rendered elements survive (both the
    // `v-for` and naive's panes key on the name), so re-observing would be pure
    // churn. Size changes are the observer's job.
    await wrapper.setProps({ names: ['a', 'b'] })
    await nextTick()

    expect(FakeResizeObserver.latest.disconnectCount).toBe(before)
  })

  it('degrades quietly when the target cannot measure itself', () => {
    // A stubbed NTabs (every test that stubs the tab components), a swapped-in
    // tab implementation, or a naive version without the method. Losing the
    // underline correction is acceptable; taking the page down with a TypeError
    // during mount is not - and this is a cosmetic enhancement.
    const Bare = defineComponent({
      setup() {
        const rootRef = ref<HTMLElement | null>(null)
        const target = shallowRef<TabBarSyncTarget>({
          get $el() {
            return rootRef.value
          },
        })
        useTabBarSync(target, () => 'x')
        return { rootRef }
      },
      template: `<div ref="rootRef"><div class="x-tabs-nav"><div data-name="a"></div></div></div>`,
    })

    expect(() => {
      const wrapper = mount(Bare)
      FakeResizeObserver.latest.fire()
      wrapper.unmount()
    }).not.toThrow()
  })

  it('stops observing when the owning scope goes away', () => {
    const wrapper = mount(Host, { props: { names: ['a', 'b'], sync: vi.fn() } })
    const observer = FakeResizeObserver.latest

    wrapper.unmount()

    expect(observer.disconnectCount).toBeGreaterThan(0)
    expect(observer.targets).toEqual([])
  })

  it('still measures where ResizeObserver does not exist (SSR / stubbed DOM)', async () => {
    // @ts-expect-error - deliberately removing the global for this case
    delete globalThis.ResizeObserver
    const sync = vi.fn()

    const wrapper = mount(Host, { props: { names: ['a', 'b'], sync } })
    await wrapper.setProps({ names: ['a'] })

    // Degraded, not broken: it cannot see a label grow, but the tab set still
    // re-measures and nothing throws.
    expect(sync).toHaveBeenCalledTimes(2)
  })
})
