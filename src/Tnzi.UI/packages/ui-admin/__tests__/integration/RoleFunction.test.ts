import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import RoleFunction from '../../src/pages/authorization/RoleFunction.vue'

// Mock the client composable — the page calls useAdminClient() to get an
// HttpClient for the bridge. Tests don't need a real client because the
// bridge factory is fully mocked below.
vi.mock('../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

vi.mock('../../src/services/bridges/authorization-bridge', () => ({
  createAuthorizationBridge: () => ({
    functionModules: { fetch: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn() },
    permissions: { fetch: vi.fn() },
    roleFunctions: {
      fetch: vi.fn(async () => ({
        items: [
          { id: 'rf1', roleId: 'r1', functionId: 'f1', functionCode: 'user.view', functionName: 'View User', moduleId: 'm1', isEnabled: true, creationTime: '2026-04-14T00:00:00Z' },
          { id: 'rf2', roleId: 'r1', functionId: 'f2', functionCode: 'user.edit', functionName: 'Edit User', moduleId: 'm1', isEnabled: true, creationTime: '2026-04-14T00:00:00Z' },
        ],
        totalCount: 2,
        pageIndex: 1,
        pageSize: 20,
      })),
    },
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

describe('RoleFunction page (Phase 3.9)', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('mounts without error', async () => {
    const wrapper = mount(RoleFunction, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.find('.dt').exists()).toBe(true)
  })

  it('renders the data table', async () => {
    const wrapper = mount(RoleFunction, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.html()).toContain('dt')
  })
})
