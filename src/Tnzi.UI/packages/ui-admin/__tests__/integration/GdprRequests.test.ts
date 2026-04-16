import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import GdprRequests from '../../src/pages/identity/GdprRequests.vue'

const mockApprove = vi.fn(async () => undefined)
const mockDeny = vi.fn(async () => undefined)
const mockFetch = vi.fn(async () => ({
  items: [
    { id: 'g1', userId: 'u1', username: 'alice', requestType: 'export',   status: 'pending', requestedAt: '2026-01-01' },
    { id: 'g2', userId: 'u2', username: 'bob',   requestType: 'deletion', status: 'pending', requestedAt: '2026-01-02' },
  ],
  totalCount: 2,
  pageIndex: 1,
  pageSize: 20,
}))

vi.mock('../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../src/services/bridges/identity-bridge', () => ({
  createIdentityBridge: () => ({
    users: { fetch: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn() },
    roles: { fetch: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn() },
    tenants: { fetch: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn() },
    loginLogs: { fetch: vi.fn() },
    gdpr: {
      requestExport: vi.fn(),
      requestDeletion: vi.fn(),
      fetchRequests: mockFetch,
      approveRequest: mockApprove,
      denyRequest: mockDeny,
    },
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

describe('GdprRequests (Phase 3 page)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    mockApprove.mockClear()
    mockDeny.mockClear()
    mockFetch.mockClear()
  })

  it('mounts and fetches GDPR requests on load', async () => {
    const wrapper = mount(GdprRequests, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.find('.dt').exists()).toBe(true)
    expect(wrapper.find('.dt').attributes('data-rows')).toBe('2')
  })

  it('Approve button calls bridge.gdpr.approveRequest for selected items', async () => {
    const wrapper = mount(GdprRequests, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    // Select a row via the exposed crud state, then click approve
    const vm = wrapper.vm as unknown as { crud: { batchActions: { toggle: (id: string) => void } } }
    vm.crud.batchActions.toggle('g1')
    await nextTick()
    await wrapper.find('.gdpr-approve').trigger('click')
    await nextTick()
    expect(mockApprove).toHaveBeenCalledWith('g1')
  })

  it('Deny button calls bridge.gdpr.denyRequest for selected items', async () => {
    const wrapper = mount(GdprRequests, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    const vm = wrapper.vm as unknown as { crud: { batchActions: { toggle: (id: string) => void } } }
    vm.crud.batchActions.toggle('g2')
    await nextTick()
    await wrapper.find('.gdpr-deny').trigger('click')
    await nextTick()
    expect(mockDeny).toHaveBeenCalledWith('g2')
  })
})
