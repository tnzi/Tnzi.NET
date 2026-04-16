import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import LoginLog from '../../src/pages/identity/LoginLog.vue'

vi.mock('../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../src/services/bridges/identity-bridge', () => ({
  createIdentityBridge: () => ({
    users: {
      fetch: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn(),
    },
    roles: {
      fetch: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn(),
    },
    tenants: {
      fetch: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn(),
    },
    loginLogs: {
      fetch: vi.fn(async () => ({
        items: [
          { id: 'l1', username: 'alice', ip: '127.0.0.1', userAgent: 'Chrome', success: true, loginAt: '2026-01-01' },
          { id: 'l2', username: 'bob',   ip: '10.0.0.1',  userAgent: 'Firefox', success: false, loginAt: '2026-01-02' },
        ],
        totalCount: 2,
        pageIndex: 1,
        pageSize: 20,
      })),
    },
    gdpr: { requestExport: vi.fn(), requestDeletion: vi.fn() },
  }),
}))

// Naive UI components are internally named WITHOUT the N prefix
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

describe('LoginLog (Phase 3 read-only page)', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('mounts and fetches login logs on load', async () => {
    const wrapper = mount(LoginLog, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.find('.dt').exists()).toBe(true)
    expect(wrapper.find('.dt').attributes('data-rows')).toBe('2')
  })

  it('does not render a Create button (read-only page)', async () => {
    const wrapper = mount(LoginLog, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.find('.t-crud-page__create').exists()).toBe(false)
  })
})
