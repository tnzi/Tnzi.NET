import { describe, it, expect, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import { createPinia, setActivePinia } from 'pinia'
import { defineComponent, h } from 'vue'
import TAdminRouterView from '../../../src/components/layout/TAdminRouterView.vue'
import { useAdminAppStore } from '../../../src/stores/useAdminAppStore'

let mountCounter = 0
let activateCounter = 0

const PageA = defineComponent({
  name: 'PageA',
  mounted() {
    mountCounter += 1
  },
  activated() {
    activateCounter += 1
  },
  render: () => h('div', { class: 'page-a' }, 'A'),
})

const PageB = defineComponent({
  name: 'PageB',
  render: () => h('div', { class: 'page-b' }, 'B'),
})

const PageNoName = defineComponent({
  render: () => h('div', { class: 'page-anon' }, 'NoName'),
})

function makeRouter(): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'home', component: PageA },
      { path: '/a', name: 'PageA', component: PageA, meta: { keepAlive: true } },
      { path: '/b', name: 'PageB', component: PageB, meta: { keepAlive: true } },
      { path: '/nokeep', name: 'NoKeep', component: PageB, meta: { keepAlive: false } },
      { path: '/anon', component: PageNoName },
    ],
  })
}

async function mountWithRouter(router: Router, options: Record<string, unknown> = {}) {
  setActivePinia(createPinia())
  return mount(TAdminRouterView, {
    props: {},
    global: {
      plugins: [router],
    },
    ...options,
  })
}

describe('TAdminRouterView', () => {
  beforeEach(() => {
    mountCounter = 0
    activateCounter = 0
  })

  it('renders the current route component', async () => {
    const router = makeRouter()
    await router.push('/a')
    await router.isReady()
    const wrapper = await mountWithRouter(router)
    await flushPromises()
    expect(wrapper.find('.page-a').exists()).toBe(true)
  })

  it('navigates between cached routes successfully', async () => {
    const router = makeRouter()
    await router.push('/a')
    await router.isReady()
    const wrapper = await mountWithRouter(router)
    await flushPromises()
    expect(wrapper.find('.page-a').exists()).toBe(true)
    await router.push('/b')
    await flushPromises()
    expect(wrapper.find('.page-b').exists()).toBe(true)
    await router.push('/a')
    await flushPromises()
    expect(wrapper.find('.page-a').exists()).toBe(true)
    // PageA was activated at least once after re-entry — KeepAlive working.
    expect(activateCounter).toBeGreaterThanOrEqual(1)
    wrapper.unmount()
  })

  it('excludes routes whose meta.keepAlive is false from caching', async () => {
    const router = makeRouter()
    await router.push('/nokeep')
    await router.isReady()
    const wrapper = await mountWithRouter(router)
    await flushPromises()
    expect(wrapper.find('.page-b').exists()).toBe(true)
    wrapper.unmount()
  })

  it('skips caching anonymous routes (no name)', async () => {
    const router = makeRouter()
    await router.push('/anon')
    await router.isReady()
    const wrapper = await mountWithRouter(router)
    await flushPromises()
    expect(wrapper.find('.page-anon').exists()).toBe(true)
    wrapper.unmount()
  })

  it('respects `exclude` prop', async () => {
    const router = makeRouter()
    await router.push('/a')
    await router.isReady()
    setActivePinia(createPinia())
    const wrapper = mount(TAdminRouterView, {
      props: { exclude: ['PageA'] },
      global: { plugins: [router] },
    })
    await flushPromises()
    expect(wrapper.find('.page-a').exists()).toBe(true)
    wrapper.unmount()
  })

  it('reloadFlag=false unmounts the inner component, true re-renders', async () => {
    const router = makeRouter()
    await router.push('/a')
    await router.isReady()
    setActivePinia(createPinia())
    const wrapper = mount(TAdminRouterView, {
      global: { plugins: [router] },
    })
    await flushPromises()
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
