import { describe, it, expect } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import { defineComponent, h } from 'vue'
import TAdminAutoBreadcrumb from '../../../src/components/layout/TAdminAutoBreadcrumb.vue'

const Blank = defineComponent({ render: () => h('div') })

function makeRouter(): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/admin',
        name: 'admin-root',
        component: Blank,
        children: [
          {
            path: 'ai',
            name: 'ai',
            meta: { title: 'AI' },
            children: [
              { path: 'agents', name: 'ai.agents', component: Blank, meta: { title: 'Agents' } },
              {
                path: 'agents/:id',
                name: 'ai.agents.detail',
                component: Blank,
                meta: { title: 'Agent Detail', hideInMenu: true, activeMenu: 'ai.agents' },
              },
            ],
          },
        ],
      },
    ],
  })
}

const stubs = {
  Breadcrumb: { template: '<div class="nb"><slot /></div>' },
  BreadcrumbItem: { template: '<span class="nbi"><slot /></span>' },
  TSvgIcon: true,
}

async function mountAt(path: string) {
  const router = makeRouter()
  await router.push(path)
  await router.isReady()
  const wrapper = mount(TAdminAutoBreadcrumb, {
    props: { showIcon: false },
    global: { plugins: [router], stubs },
  })
  await flushPromises()
  return wrapper
}

describe('TAdminAutoBreadcrumb', () => {
  it('builds a plain chain for ordinary leaf routes', async () => {
    const wrapper = await mountAt('/admin/ai/agents')
    const labels = wrapper.findAll('.nbi').map((i) => i.text())
    expect(labels).toEqual(['AI', 'Agents'])
  })

  it('uses meta.activeMenu to rebuild the parent chain + trailing detail crumb', async () => {
    // Without the activeMenu wiring this collapses to just ["AI"] because the
    // detail route is hidden and not part of the list page's matched chain.
    const wrapper = await mountAt('/admin/ai/agents/42')
    const labels = wrapper.findAll('.nbi').map((i) => i.text())
    expect(labels).toEqual(['AI', 'Agents', 'Agent Detail'])
  })
})
