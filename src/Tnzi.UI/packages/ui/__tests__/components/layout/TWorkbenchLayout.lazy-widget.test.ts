import { describe, it, expect } from 'vitest'
import { defineComponent, h, onMounted, onUnmounted } from 'vue'
import { mount, flushPromises } from '@vue/test-utils'
import TWorkbenchLayout from '../../../src/components/layout/TWorkbenchLayout.vue'
import { useWidget } from '../../../src/headless/layout/useWidget'
import type { WidgetDef } from '../../../src/components/layout/widget-types'

/**
 * A widget declared as an async factory (`component: () => import('./X.vue')`)
 * is the documented lazy-loading form. Wrapping it in a fresh
 * `defineAsyncComponent` on every render gave the vnode a new type each time,
 * so Vue unmounted the widget and mounted a new one whenever the card
 * re-rendered: a widget that marks its card busy while loading (as the
 * standard data hook does) refetched in a loop, and a draggable layout
 * remounted every lazy widget on each window resize.
 */
function lazyWidget(options: { busyWhileLoading?: boolean } = {}) {
  const counts = { mounted: 0, unmounted: 0 }
  const Widget = defineComponent({
    setup() {
      const widget = useWidget()
      onMounted(async () => {
        counts.mounted += 1
        // Bounded so a regression fails the assertion instead of looping forever.
        if (!options.busyWhileLoading || counts.mounted > 5) return
        widget.setBusy(true)
        await Promise.resolve()
        widget.setBusy(false)
      })
      onUnmounted(() => {
        counts.unmounted += 1
      })
      return () => h('div', { class: 'lazy-widget' }, 'loaded')
    },
  })
  // Shaped like the namespace `import()` resolves to, which Vue unwraps to `default`.
  const factory = () => Promise.resolve({ __esModule: true, default: Widget })
  return { counts, factory }
}

async function settle(rounds = 6) {
  for (let i = 0; i < rounds; i++) await flushPromises()
}

describe('TWorkbenchLayout - lazy widgets', () => {
  it('mounts a lazy widget once across re-renders of the layout', async () => {
    const { counts, factory } = lazyWidget()
    const widgets: WidgetDef[] = [{ id: 'sales', component: factory }]
    const wrapper = mount(TWorkbenchLayout, { props: { widgets } })
    await settle()
    expect(wrapper.find('.lazy-widget').exists()).toBe(true)
    expect(counts.mounted).toBe(1)

    await wrapper.setProps({ xGap: 8 })
    await settle()
    await wrapper.setProps({ yGap: 8 })
    await settle()

    expect(counts.mounted).toBe(1)
    expect(counts.unmounted).toBe(0)
    wrapper.unmount()
  })

  it('does not remount a lazy widget that marks its card busy while it loads', async () => {
    const { counts, factory } = lazyWidget({ busyWhileLoading: true })
    const wrapper = mount(TWorkbenchLayout, { props: { widgets: [{ id: 'kpi', component: factory }] } })
    await settle(12)

    expect(counts.mounted).toBe(1)
    expect(counts.unmounted).toBe(0)
    wrapper.unmount()
  })

  it('keeps the widget when a draggable layout reflows on resize', async () => {
    const { counts, factory } = lazyWidget()
    const wrapper = mount(TWorkbenchLayout, {
      props: { widgets: [{ id: 'chart', component: factory, span: { xs: 24, lg: 12 } }], layout: 'draggable' },
    })
    // The drag container is an optional peer loaded on first use; swapping the
    // placeholder container for it remounts the cards once. Let that finish so
    // the count below measures resizes only.
    await import('vue-draggable-plus')
    await settle()
    const mountedBefore = counts.mounted
    const unmountedBefore = counts.unmounted
    expect(wrapper.find('.lazy-widget').exists()).toBe(true)

    for (const width of [800, 1100, 1600]) {
      Object.defineProperty(window, 'innerWidth', { value: width, configurable: true })
      window.dispatchEvent(new Event('resize'))
      await settle(2)
    }

    expect(counts.mounted).toBe(mountedBefore)
    expect(counts.unmounted).toBe(unmountedBefore)
    wrapper.unmount()
  })
})
