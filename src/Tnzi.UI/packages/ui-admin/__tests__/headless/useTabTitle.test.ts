import { describe, it, expect, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import { createPinia, setActivePinia } from 'pinia'
import { defineComponent, h, ref } from 'vue'
import { useTabTitle } from '../../src/headless/useTabTitle'
import { provideDesktopWindowId } from '../../src/headless/desktop-window-context'
import { useAdminTabStore } from '../../src/stores/useAdminTabStore'
import { useAdminDesktopStore } from '../../src/stores/useAdminDesktopStore'

const Blank = defineComponent({ render: () => h('div') })

describe('useTabTitle', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('writes the record name to the param-route tab (id = path, ignores ?section=)', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/admin/ai/agents/:id', name: 'ai.agents.detail', component: Blank }],
    })
    await router.push('/admin/ai/agents/A?section=versions')
    await router.isReady()

    const store = useAdminTabStore()
    // The shell opens the detail tab keyed by PATH (not fullPath).
    store.addTab({
      name: 'ai.agents.detail',
      path: '/admin/ai/agents/A',
      fullPath: '/admin/ai/agents/A?section=versions',
      params: { id: 'A' },
      query: { section: 'versions' },
      meta: {},
    })
    expect(store.tabs[0].id).toBe('/admin/ai/agents/A')

    const name = ref<string | null>(null)
    const Host = defineComponent({
      setup() {
        useTabTitle(() => name.value)
        return () => h('div')
      },
    })
    mount(Host, { global: { plugins: [router] } })
    await flushPromises()

    name.value = 'Foo' // record finished loading
    await flushPromises()
    // The title lands on the path-keyed tab even though the URL carries ?section=.
    expect(store.tabs[0].title).toBe('Foo')
  })

  it('is a no-op without a router (isolated mount)', () => {
    const name = ref<string>('X')
    const Host = defineComponent({
      setup() {
        useTabTitle(() => name.value)
        return () => h('div')
      },
    })
    // No router plugin → useRoute throws internally → swallowed, no crash.
    expect(() => mount(Host)).not.toThrow()
  })
})

describe('useTabTitle inside a desktop window', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('retargets to the window title bar instead of the tab store', async () => {
    const desktop = useAdminDesktopStore()
    const tabs = useAdminTabStore()
    const id = desktop.open({ name: 'ai.agents.detail', path: '/a/1', title: 'Agent Detail' })

    const Page = defineComponent({
      setup() {
        useTabTitle(ref('Support Bot'))
        return () => h('div')
      },
    })
    const Shell = defineComponent({
      setup() {
        provideDesktopWindowId(id)
        return () => h(Page)
      },
    })

    mount(Shell)
    await flushPromises()

    expect(desktop.find(id)!.title).toBe('Support Bot')
    // Nothing leaked into the tab bar - which the desktop layout does not even
    // render, and where two windows on one route would collide on a single key.
    expect(tabs.tabs).toHaveLength(0)
  })

  it('gives two windows on the same route independent titles', async () => {
    const desktop = useAdminDesktopStore()
    const a = desktop.open({ name: 'ai.agents.detail', path: '/a/1', title: 'Agent Detail' })
    const b = desktop.open({ name: 'ai.agents.detail', path: '/a/2', title: 'Agent Detail' })

    const makeShell = (id: string, name: string) =>
      defineComponent({
        setup() {
          provideDesktopWindowId(id)
          return () =>
            h(
              defineComponent({
                setup() {
                  useTabTitle(ref(name))
                  return () => h('div')
                },
              }),
            )
        },
      })

    mount(makeShell(a, 'Support Bot'))
    mount(makeShell(b, 'Sales Bot'))
    await flushPromises()

    expect(desktop.find(a)!.title).toBe('Support Bot')
    expect(desktop.find(b)!.title).toBe('Sales Bot')
  })
})
