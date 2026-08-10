import { describe, it, expect, vi, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { NTabs } from 'naive-ui'
import TTabsPage from '../../src/components/layout/TTabsPage.vue'

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: { section: 'second' }, params: {}, path: '/x', fullPath: '/x?section=second', hash: '', name: 'x', meta: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))

const sections = [
  { name: 'first', label: 'First' },
  { name: 'second', label: 'Second' },
]

describe('TTabsPage', () => {
  it('emits the resolved section immediately, not just on user change', async () => {
    setActivePinia(createPinia())
    const wrapper = mount(TTabsPage, {
      props: { sections, defaultSection: 'first' },
      global: { stubs: { Tabs: { template: '<div><slot /></div>' }, TabPane: { template: '<div><slot /></div>' } } },
    })
    await flushPromises()

    // Deep-linked into `second`: a page binding `v-model:section` must see that
    // on first render, or its cross-tab controls render for the wrong tab.
    const emitted = wrapper.emitted('update:section')
    expect(emitted).toBeTruthy()
    expect(emitted![0]).toEqual(['second'])
  })

  describe('active-tab underline', () => {
    // Mounted against the REAL NTabs, not a stub: what is under test is that the
    // container hands naive's own instance to `useTabBarSync`. A unit test of the
    // hook alone proves the mechanism works, not that anything is plugged into
    // it - and "built but never wired up" is the failure mode this whole fix is
    // about (a badge lands, the strip re-flows, nobody re-measures).
    const originalResizeObserver = globalThis.ResizeObserver
    let fire: (() => void) | null = null
    let observed: Element[] = []

    function installFakeResizeObserver(): void {
      observed = []
      fire = null
      globalThis.ResizeObserver = class {
        constructor(private readonly cb: ResizeObserverCallback) {
          fire = () => this.cb([] as unknown as ResizeObserverEntry[], this as unknown as ResizeObserver)
        }
        observe(el: Element): void {
          observed.push(el)
        }
        unobserve(): void {}
        disconnect(): void {
          observed = []
        }
      } as unknown as typeof ResizeObserver
    }

    afterEach(() => {
      globalThis.ResizeObserver = originalResizeObserver
    })

    it('re-measures when a tab label changes size after mount', async () => {
      setActivePinia(createPinia())
      installFakeResizeObserver()

      const wrapper = mount(TTabsPage, { props: { sections, defaultSection: 'first' } })
      await flushPromises()

      // It found naive's own tab elements - i.e. the template ref reached the
      // real NTabs root and the nav was resolved inside it.
      expect(observed.map((el) => el.getAttribute('data-name'))).toEqual(['first', 'second'])

      const tabs = wrapper.findComponent(NTabs)
      const spy = vi.spyOn(tabs.vm as unknown as { syncBarPosition: () => void }, 'syncBarPosition')

      // A label grew (async count / status badge). naive cannot see this on its
      // own: its nav observer bails while the strip's own width is unchanged.
      fire!()

      expect(spy).toHaveBeenCalled()
    })
  })
})
