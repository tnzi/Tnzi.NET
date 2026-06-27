import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import GdprRequests from '../../../src/pages/identity/GdprRequests.vue'

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

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../../src/services/bridges/identity-bridge', () => ({
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

// `useMessage` requires NMessageProvider in the tree — stub the import so
// the page can call `message.success` / `error` without a provider.
vi.mock('naive-ui', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('naive-ui')
  return {
    ...actual,
    useMessage: () => ({
      success: vi.fn(),
      error: vi.fn(),
      info: vi.fn(),
      warning: vi.fn(),
    }),
  }
})

// Naive UI components are internally named WITHOUT the N prefix. The
// Popconfirm stub renders its `trigger` slot and forwards the inner
// button's click event as `positive-click` so tests can fire the
// confirmed action with a single click (mirrors what NPopconfirm does
// after the user clicks "OK" in production).
const stubs = {
  DataTable: { props: ['data'], template: '<div class="dt" :data-rows="data.length" />' },
  Pagination: { template: '<div />' },
  Input: { props: ['value'], template: '<input />' },
  Button: { template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Modal: { props: ['show'], template: '<div v-if="show"><slot /></div>' },
  Popover: { template: '<div><slot name="trigger" /></div>' },
  Popconfirm: {
    emits: ['positive-click'],
    template: '<div class="popconfirm" @click="$emit(\'positive-click\')"><slot name="trigger" /></div>',
  },
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

  it('batch approve invokes bridge.gdpr.approveRequest after popconfirm', async () => {
    const wrapper = mount(GdprRequests, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    const vm = wrapper.vm as unknown as { crud: { batchActions: { toggle: (id: string) => void } } }
    vm.crud.batchActions.toggle('g1')
    await nextTick()
    // Find the batch-approve popconfirm wrapper and click it (the stub
    // fires `positive-click` on click, mirroring NPopconfirm post-confirm).
    const approveBtn = wrapper.find('.gdpr-approve')
    expect(approveBtn.exists()).toBe(true)
    // Click the surrounding popconfirm stub (parent .popconfirm)
    await approveBtn.element.closest('.popconfirm')?.dispatchEvent(new Event('click', { bubbles: true }))
    await nextTick()
    await new Promise(r => setTimeout(r, 5))
    expect(mockApprove).toHaveBeenCalledWith('g1')
  })

  it('batch reject invokes bridge.gdpr.denyRequest after popconfirm', async () => {
    const wrapper = mount(GdprRequests, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    const vm = wrapper.vm as unknown as { crud: { batchActions: { toggle: (id: string) => void } } }
    vm.crud.batchActions.toggle('g2')
    await nextTick()
    const denyBtn = wrapper.find('.gdpr-deny')
    expect(denyBtn.exists()).toBe(true)
    await denyBtn.element.closest('.popconfirm')?.dispatchEvent(new Event('click', { bubbles: true }))
    await nextTick()
    await new Promise(r => setTimeout(r, 5))
    expect(mockDeny).toHaveBeenCalledWith('g2')
  })

  it('does not show batch actions when nothing is selected', async () => {
    const wrapper = mount(GdprRequests, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    // With no rows selected, the batch-actions bar doesn't render — the
    // gdpr-approve / gdpr-deny triggers should not exist in the DOM.
    expect(wrapper.find('.gdpr-approve').exists()).toBe(false)
    expect(wrapper.find('.gdpr-deny').exists()).toBe(false)
  })
})
