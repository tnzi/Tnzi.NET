import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
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
})
