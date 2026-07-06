import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import AccessLogs from '../../../src/pages/system/AccessLogs.vue'

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../../src/services/bridges/system-bridge', () => ({
  createSystemBridge: () => ({
    menus: { fetch: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn(), reorder: vi.fn() },
    settings: { fetch: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn() },
    accessLogs: {
      fetch: vi.fn(async () => ({
        items: [
          { id: 'a1', path: '/api/users', method: 'GET', statusCode: 200, responseTime: 42, ipAddress: '127.0.0.1', userName: 'admin', creationTime: '2026-01-01T00:00:00Z' },
          { id: 'a2', path: '/api/roles', method: 'POST', statusCode: 201, responseTime: 88, ipAddress: '127.0.0.1', userName: 'admin', creationTime: '2026-01-01T00:01:00Z' },
        ],
        totalCount: 2,
        pageIndex: 1,
        pageSize: 20,
      })),
    },
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
  // The read-only View action renders its detail in a drawer (closed on mount).
  Drawer: { props: ['show'], template: '<div v-if="show"><slot /></div>' },
  DrawerContent: { template: '<div><slot /></div>' },
  VueDraggable: { template: '<div><slot /></div>' },
}

describe('AccessLogs page (Phase 3.15)', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('mounts and fetches access logs on load', async () => {
    const wrapper = mount(AccessLogs, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.find('.dt').exists()).toBe(true)
    expect(wrapper.find('.dt').attributes('data-rows')).toBe('2')
  })

  it('does not show Create button (read-only page)', async () => {
    const wrapper = mount(AccessLogs, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.find('.t-crud-page__create').exists()).toBe(false)
  })
})
