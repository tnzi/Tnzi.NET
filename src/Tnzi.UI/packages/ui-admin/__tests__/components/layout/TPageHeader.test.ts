import { describe, it, expect } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import TPageHeader from '../../../src/components/layout/TPageHeader.vue'

const stubs = {
  Popover: { name: 'Popover', template: '<div><slot name="trigger" /><slot /></div>' },
  SvgIcon: true,
}

function mountHeader(props = {}, slots = {}) {
  const global: Record<string, unknown> = { stubs }
  return mount(TPageHeader, { props, slots, global })
}

async function mountHeaderWithRouter(props = {}) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/u', name: 'identity.users', meta: { title: 'tnzi.admin.modules.identity.users.title' }, component: { template: '<div/>' } }],
  })
  await router.push('/u')
  await router.isReady()
  const w = mount(TPageHeader, { props, global: { stubs, plugins: [router] } })
  await flushPromises()
  return w
}

describe('TPageHeader', () => {
  it('renders the title prop', () => {
    const w = mountHeader({ title: 'User Management' })
    expect(w.find('.t-page-header__title').text()).toBe('User Management')
  })

  it('shows the ⓘ help popover only when help is set', () => {
    expect(mountHeader({ title: 'X' }).find('.t-page-header__help').exists()).toBe(false)
    expect(mountHeader({ title: 'X', help: 'Explains X' }).find('.t-page-header__help').exists()).toBe(true)
  })

  it('lets #title slot replace the whole left region', () => {
    const w = mountHeader({ title: 'X' }, { title: '<div class="custom-title">Avatar+Name</div>' })
    expect(w.find('.custom-title').exists()).toBe(true)
    expect(w.find('.t-page-header__title').exists()).toBe(false)
  })

  it('renders the #actions slot', () => {
    const w = mountHeader({ title: 'X' }, { actions: '<button class="act">Go</button>' })
    expect(w.find('.t-page-header__actions .act').exists()).toBe(true)
  })

  it('marks the bar as wrap-safe (flex-wrap) for narrow screens', () => {
    const w = mountHeader({ title: 'X' }, { actions: '<button class="a">A</button>' })
    expect(w.find('.t-page-header__bar').exists()).toBe(true)
    expect(w.find('.t-page-header__actions').exists()).toBe(true)
  })

  it('falls back to route.meta.title when no title prop', async () => {
    const w = await mountHeaderWithRouter({ translate: (k: string) => (k.endsWith('.title') ? 'Users' : k) })
    await w.vm.$nextTick()
    expect(w.find('.t-page-header__title').text()).toBe('Users')
  })
})
