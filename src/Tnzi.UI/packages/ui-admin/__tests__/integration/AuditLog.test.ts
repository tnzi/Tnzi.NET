import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import AuditLog from '../../src/pages/audit/AuditLog.vue'

vi.mock('../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../src/services/bridges/audit-bridge', () => ({
  createAuditBridge: () => ({
    logs: {
      fetch: vi.fn(async () => ({
        items: [
          { id: 'op1', functionName: 'GetUser', userId: 'u1', userName: 'admin', elapsed: 42, startTime: '2026-01-01T00:00:00Z', resultType: 1, entityEntries: [], creationTime: '2026-01-01T00:00:00Z' },
          { id: 'op2', functionName: 'CreateRole', userId: 'u1', userName: 'admin', elapsed: 88, startTime: '2026-01-01T00:01:00Z', resultType: 1, entityEntries: [], creationTime: '2026-01-01T00:01:00Z' },
        ],
        totalCount: 2,
        pageIndex: 1,
        pageSize: 20,
      })),
      create: vi.fn(),
      update: vi.fn(),
      delete: vi.fn(),
      exportCsvUrl: vi.fn(() => '/admin/audit-operations/export/csv'),
    },
    operations: {
      fetch: vi.fn(async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 })),
      create: vi.fn(),
      update: vi.fn(),
      delete: vi.fn(),
    },
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
}

describe('AuditLog page (Phase 3.22)', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('mounts and fetches audit logs on load', async () => {
    const wrapper = mount(AuditLog, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.find('.dt').exists()).toBe(true)
    expect(wrapper.find('.dt').attributes('data-rows')).toBe('2')
  })

  it('does not show Create button (read-only page)', async () => {
    const wrapper = mount(AuditLog, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.find('.t-crud-page__create').exists()).toBe(false)
  })
})
