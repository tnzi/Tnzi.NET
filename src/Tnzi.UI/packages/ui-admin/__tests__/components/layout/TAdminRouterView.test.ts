import { describe, it, expect, beforeEach } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import { createMemoryHistory, createRouter, type Router, type RouteRecordRaw } from 'vue-router'
import { createPinia, setActivePinia } from 'pinia'
import { defineComponent, h, onActivated, onMounted, onUnmounted, ref, watch } from 'vue'
import { RouterView, useRoute } from 'vue-router'
import TAdminRouterView from '../../../src/components/layout/TAdminRouterView.vue'
import { useAdminAppStore } from '../../../src/stores/useAdminAppStore'
import { useAdminTabStore } from '../../../src/stores/useAdminTabStore'

/**
 * Route names and component names differ on purpose. KeepAlive matches its
 * `include` against COMPONENT names, while the route table speaks in route
 * names; a fixture that names both the same hides exactly that mismatch (the
 * cache never held anything and the old test still passed).
 */
interface Counters {
  mounted: number
  unmounted: number
  activated: number
  queryChanges: string[]
}

const counters: Record<string, Counters> = {}

function count(name: string): Counters {
  counters[name] ??= { mounted: 0, unmounted: 0, activated: 0, queryChanges: [] }
  return counters[name]!
}

/** A list page with local state and a watcher on its own route's query. */
function listPage(label: string) {
  return defineComponent({
    name: `${label}ListPage`,
    setup() {
      const route = useRoute()
      const typed = ref('')
      onMounted(() => (count(label).mounted += 1))
      onUnmounted(() => (count(label).unmounted += 1))
      onActivated(() => (count(label).activated += 1))
      watch(
        () => route.query.q,
        (q) => count(label).queryChanges.push(String(q ?? '')),
      )
      return () =>
        h('div', { class: `page-${label}` }, [
          h('input', { class: 'typed', value: typed.value, onInput: (e: Event) => (typed.value = (e.target as HTMLInputElement).value) }),
        ])
    },
  })
}

const PageA = listPage('a')
const PageB = listPage('b')
const PageNoKeep = listPage('nokeep')
const PageHidden = listPage('hidden')

const DetailPage = defineComponent({
  name: 'RecordDetailPage',
  setup() {
    count('detail').mounted += 1
    const route = useRoute()
    return () => h('div', { class: 'page-detail' }, String(route.params.id))
  },
})

const PageNested = listPage('nested')

/** A layout route with its own RouterView, as a consumer app may nest one. */
const NestedLayout = defineComponent({
  name: 'SectionLayout',
  setup: () => () => h('section', { class: 'layout' }, [h(RouterView)]),
})

const PageHome = defineComponent({
  name: 'HomePage',
  setup: () => () => h('div', { class: 'page-home' }, 'Home'),
})

const PageAnon = defineComponent({
  setup: () => () => h('div', { class: 'page-anon' }, 'NoName'),
})

const routes: RouteRecordRaw[] = [
  { path: '/', name: 'home', component: PageHome },
  { path: '/a', name: 'list.a', component: PageA, meta: { keepAlive: true } },
  { path: '/b', name: 'list.b', component: PageB, meta: { keepAlive: true } },
  { path: '/nokeep', name: 'list.nokeep', component: PageNoKeep },
  { path: '/hidden', name: 'list.hidden', component: PageHidden, meta: { keepAlive: true, hideInTab: true } },
  { path: '/anon', component: PageAnon },
  {
    path: '/section',
    component: NestedLayout,
    children: [{ path: 'nested', name: 'section.nested', component: PageNested, meta: { keepAlive: true } }],
  },
  { path: '/detail/:id', name: 'record.detail', component: DetailPage },
]

function makeRouter(): Router {
  return createRouter({ history: createMemoryHistory(), routes })
}

/** Navigate, then record the tab the way the admin shell does on every route change. */
async function go(router: Router, path: string): Promise<void> {
  await router.push(path)
  const r = router.currentRoute.value
  if (typeof r.name === 'string') {
    useAdminTabStore().addTab({
      name: r.name,
      fullPath: r.fullPath,
      path: r.path,
      query: r.query as Record<string, unknown>,
      params: r.params as Record<string, unknown>,
      meta: r.meta as Record<string, unknown>,
    })
  }
  await flushPromises()
}

async function shell(path: string, props: Record<string, unknown> = {}): Promise<{ router: Router; wrapper: VueWrapper }> {
  setActivePinia(createPinia())
  const router = makeRouter()
  await router.push('/')
  await router.isReady()
  const wrapper = mount(TAdminRouterView, { props, global: { plugins: [router] } })
  await go(router, path)
  return { router, wrapper }
}

async function type(wrapper: VueWrapper, selector: string, value: string): Promise<void> {
  await wrapper.find(`${selector} .typed`).setValue(value)
}

describe('TAdminRouterView', () => {
  beforeEach(() => {
    for (const key of Object.keys(counters)) delete counters[key]
    count('detail').mounted = 0
  })

  it('renders the current route component', async () => {
    const { wrapper } = await shell('/a')
    expect(wrapper.find('.page-a').exists()).toBe(true)
    wrapper.unmount()
  })

  it('keeps a page and what was typed into it while its tab is open', async () => {
    const { router, wrapper } = await shell('/a')
    await type(wrapper, '.page-a', 'half-written filter')

    await go(router, '/b')
    expect(wrapper.find('.page-a').exists()).toBe(false)
    await go(router, '/a')

    expect(count('a').mounted).toBe(1)
    expect(count('a').unmounted).toBe(0)
    expect(count('a').activated).toBe(2)
    expect((wrapper.find('.page-a .typed').element as HTMLInputElement).value).toBe('half-written filter')
    wrapper.unmount()
  })

  it('closing the tab drops the kept page', async () => {
    const { router, wrapper } = await shell('/a')
    await go(router, '/b')
    useAdminTabStore().removeTab('list.a')
    await flushPromises()
    expect(count('a').unmounted).toBe(1)

    await go(router, '/a')
    expect(count('a').mounted).toBe(2)
    wrapper.unmount()
  })

  it('a route without meta.keepAlive is mounted afresh each visit', async () => {
    const { router, wrapper } = await shell('/nokeep')
    await go(router, '/b')
    await go(router, '/nokeep')
    expect(count('nokeep').mounted).toBe(2)
    wrapper.unmount()
  })

  it('a page that has no tab is not kept once left', async () => {
    const { router, wrapper } = await shell('/hidden')
    await go(router, '/b')
    expect(count('hidden').unmounted).toBe(1)
    await go(router, '/hidden')
    expect(count('hidden').mounted).toBe(2)
    wrapper.unmount()
  })

  it('cache=false (the theme\'s "Cache tab pages" switch) keeps nothing', async () => {
    const { router, wrapper } = await shell('/a')
    await wrapper.setProps({ cache: false })
    await go(router, '/b')
    await go(router, '/a')
    expect(count('a').mounted).toBe(2)
    wrapper.unmount()
  })

  // The theme's switch reaches the cache only through the shell's binding;
  // nothing else would notice the binding going missing.
  it('the admin shell binds the theme switch "Cache tab pages" to cache', () => {
    const shellSource = readFileSync(resolve(__dirname, '../../../src/router/AdminShellRoot.vue'), 'utf8')
    expect(shellSource).toMatch(/<TAdminRouterView[^>]*:cache="themeStore\.tabCache"/)
  })

  it('respects the `exclude` prop', async () => {
    const { router, wrapper } = await shell('/a', { exclude: ['list.a'] })
    await go(router, '/b')
    await go(router, '/a')
    expect(count('a').mounted).toBe(2)
    wrapper.unmount()
  })

  it('reload mounts the page on screen afresh', async () => {
    const { wrapper } = await shell('/a')
    await type(wrapper, '.page-a', 'stale')
    await useAdminAppStore().reloadPage()
    await flushPromises()

    expect(count('a').mounted).toBe(2)
    expect((wrapper.find('.page-a .typed').element as HTMLInputElement).value).toBe('')
    wrapper.unmount()
  })

  it('a kept page does not see another page\'s route, and catches up when shown again', async () => {
    const { router, wrapper } = await shell('/a')
    await go(router, '/b?q=other-page')
    expect(count('a').queryChanges).toEqual([])

    await go(router, '/a?q=handed-over')
    expect(count('a').queryChanges).toEqual(['handed-over'])
    expect(count('a').mounted).toBe(1)
    wrapper.unmount()
  })

  it('a RouterView nested in a kept layout keeps showing its own page in the background', async () => {
    const { router, wrapper } = await shell('/section/nested')
    await go(router, '/b')
    expect(count('nested').unmounted).toBe(0)
    await go(router, '/section/nested')
    expect(count('nested').mounted).toBe(1)
    wrapper.unmount()
  })

  it('mounts a SEPARATE instance per record for param-based detail routes', async () => {
    const { router, wrapper } = await shell('/detail/A')
    expect(wrapper.find('.page-detail').text()).toBe('A')
    expect(count('detail').mounted).toBe(1)

    await go(router, '/detail/B')
    // Keyed per record, so B is a fresh instance (not A reused) and the page
    // shows B's id, not A's stale data.
    expect(wrapper.find('.page-detail').text()).toBe('B')
    expect(count('detail').mounted).toBe(2)
    wrapper.unmount()
  })

  it('renders anonymous routes (no name), uncached', async () => {
    const { wrapper } = await shell('/anon')
    expect(wrapper.find('.page-anon').exists()).toBe(true)
    wrapper.unmount()
  })

  it('reloadFlag=false unmounts the inner component, true re-renders', async () => {
    const { wrapper } = await shell('/a')
    expect(wrapper.find('.page-a').exists()).toBe(true)

    const appStore = useAdminAppStore()
    appStore.reloadFlag = false
    await flushPromises()
    expect(wrapper.find('.page-a').exists()).toBe(false)

    appStore.reloadFlag = true
    await flushPromises()
    expect(wrapper.find('.page-a').exists()).toBe(true)
    wrapper.unmount()
  })
})
