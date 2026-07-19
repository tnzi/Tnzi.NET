import { describe, it, expect } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import { createPinia, setActivePinia, type Pinia } from 'pinia'
import { defineComponent, h } from 'vue'
import TAdminAutoBreadcrumb from '../../../src/components/layout/TAdminAutoBreadcrumb.vue'
import { useAdminBreadcrumbStore } from '../../../src/stores/useAdminBreadcrumbStore'

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

// Mirrors Contoso: the parent list (`contoso-matters` = "Files") is itself
// hideInMenu (reached only via a client), and the detail's activeMenu points at
// it. Before the fix the activeMenu-rebuilt chain was re-filtered by hideInMenu
// and collapsed to just the leaf.
function makeHiddenParentRouter(): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/admin',
        name: 'admin-root',
        component: Blank,
        children: [
          { path: 'matters', name: 'contoso-matters', component: Blank, meta: { title: 'Files', hideInMenu: true } },
          {
            path: 'matters/:id',
            name: 'contoso-matter-detail',
            component: Blank,
            meta: { title: 'File', hideInMenu: true, activeMenu: 'contoso-matters' },
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

async function mountAt(path: string, router: Router = makeRouter(), pinia?: Pinia) {
  await router.push(path)
  await router.isReady()
  const plugins: unknown[] = [router]
  if (pinia) plugins.push(pinia)
  const wrapper = mount(TAdminAutoBreadcrumb, {
    props: { showIcon: false },
    global: { plugins, stubs },
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

  it('keeps a hideInMenu activeMenu parent instead of collapsing to the leaf', async () => {
    // Regression: a detail whose activeMenu points at a hidden list must still
    // show "Files / File", not just "File".
    const wrapper = await mountAt('/admin/matters/9', makeHiddenParentRouter())
    const labels = wrapper.findAll('.nbi').map((i) => i.text())
    expect(labels).toEqual(['Files', 'File'])
  })

  it('renders a page-contributed full trail (cross-entity drill)', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const store = useAdminBreadcrumbStore()
    store.setTrail('/admin/ai/agents/42', [
      { label: 'Clients', to: '/admin/clients' },
      { label: 'John Smith', to: '/admin/clients/7?section=files' },
      { label: 'File 2024-0912' },
    ])
    const wrapper = await mountAt('/admin/ai/agents/42', makeRouter(), pinia)
    const labels = wrapper.findAll('.nbi').map((i) => i.text())
    expect(labels).toEqual(['Clients', 'John Smith', 'File 2024-0912'])
  })

  it('overrides only the leaf label when a page contributes one', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const store = useAdminBreadcrumbStore()
    store.setLeafLabel('/admin/ai/agents/42', 'Support Bot')
    const wrapper = await mountAt('/admin/ai/agents/42', makeRouter(), pinia)
    const labels = wrapper.findAll('.nbi').map((i) => i.text())
    // Parent chain intact, only the trailing "Agent Detail" replaced.
    expect(labels).toEqual(['AI', 'Agents', 'Support Bot'])
  })
})
