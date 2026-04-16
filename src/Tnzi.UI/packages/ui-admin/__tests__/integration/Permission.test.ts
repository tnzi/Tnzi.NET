import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import Permission from '../../src/pages/authorization/Permission.vue'

// Mock the client composable — the page calls useAdminClient() to get an
// HttpClient for the bridge. Tests don't need a real client because the
// bridge factory is fully mocked below.
vi.mock('../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('../../src/services/bridges/authorization-bridge', () => ({
  createAuthorizationBridge: () => ({
    functionModules: { fetch: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn() },
    permissions: {
      fetch: vi.fn(async () => ({
        items: [
          { id: 'p1', code: 'user.view', name: 'View User', moduleId: 'm1', isEnabled: true, order: 1 },
          { id: 'p2', code: 'user.edit', name: 'Edit User', moduleId: 'm1', isEnabled: true, order: 2 },
        ],
        totalCount: 2,
        pageIndex: 1,
        pageSize: 20,
      })),
    },
    roleFunctions: { fetch: vi.fn() },
    entityRoles: { fetch: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn() },
  }),
}))

const stubs = {
  DataTable: { props: ['data'], template: '<div class="dt" :data-rows="data.length" />' },
  Pagination: { template: '<div />' },
  Input: { props: ['value'], template: '<input />' },
  Button: { template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Modal: { props: ['show'], template: '<div v-if="show"><slot /></div>' },
  Popover: { template: '<div><slot name="trigger" /></div>' },
  Checkbox: { template: '<input type="checkbox" />' },
  Form: { template: '<form><slot /></form>' },
  FormItem: { template: '<div><slot /></div>' },
  InputNumber: { template: '<input type="number" />' },
  Switch: { template: '<button />' },
  Select: { template: '<select />' },
  DatePicker: { template: '<input type="date" />' },
  VueDraggable: { template: '<div><slot /></div>' },
}

describe('Permission page (Phase 3.8)', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('mounts and fetches permissions on load', async () => {
    const wrapper = mount(Permission, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.find('.dt').exists()).toBe(true)
    expect(wrapper.find('.dt').attributes('data-rows')).toBe('2')
  })

  it('renders the toolbar', async () => {
    const wrapper = mount(Permission, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.html()).toContain('dt')
  })
})
