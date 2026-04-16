import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import MenuManagement from '../../src/pages/system/MenuManagement.vue'

vi.mock('../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../src/services/bridges/system-bridge', () => ({
  createSystemBridge: () => ({
    menus: {
      fetch: vi.fn(async () => ({
        items: [
          { id: 'm1', name: 'Dashboard', path: '/dashboard', sortOrder: 1, isHidden: false },
          { id: 'm2', name: 'Users', path: '/users', sortOrder: 2, isHidden: false },
        ],
        totalCount: 2,
        pageIndex: 1,
        pageSize: 20,
      })),
      create: vi.fn(async (d: unknown) => ({ ...(d as object), id: 'new' })),
      update: vi.fn(async (id: string, d: unknown) => ({ id, ...(d as object) })),
      delete: vi.fn(async () => undefined),
      reorder: vi.fn(async () => undefined),
    },
    settings: { fetch: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn() },
    accessLogs: { fetch: vi.fn() },
    scheduledJobs: { fetch: vi.fn(), trigger: vi.fn() },
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

describe('MenuManagement page (Phase 3.12)', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('mounts and fetches menus on load', async () => {
    const wrapper = mount(MenuManagement, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.find('.dt').exists()).toBe(true)
    expect(wrapper.find('.dt').attributes('data-rows')).toBe('2')
  })

  it('opens form modal when Create button is clicked', async () => {
    const wrapper = mount(MenuManagement, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    await wrapper.find('.t-crud-page__create').trigger('click')
    expect(wrapper.find('form').exists()).toBe(true)
  })
})
