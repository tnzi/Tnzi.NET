import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import LoginLogs from '../../../src/pages/identity/LoginLogs.vue'

// Hoisted so the vi.mock factories (evaluated before module init) can close
// over them. `mockRoute.query` is mutated per-test to drive the ?userId= deep
// link; `mockFetch` is asserted for the seeded filter.
const { mockRoute, mockFetch } = vi.hoisted(() => ({
  mockRoute: { query: {} as Record<string, unknown> },
  mockFetch: vi.fn(async () => ({
    items: [
      { id: 'l1', userName: 'alice', ipAddress: '127.0.0.1', userAgent: 'Chrome', isSuccess: true, loginTime: '2026-01-01' },
      { id: 'l2', userName: 'bob', ipAddress: '10.0.0.1', userAgent: 'Firefox', isSuccess: false, loginTime: '2026-01-02' },
    ],
    totalCount: 2,
    pageIndex: 1,
    pageSize: 20,
  })),
}))

vi.mock('vue-router', () => ({
  useRoute: () => mockRoute,
  useRouter: () => ({ push: vi.fn(), replace: vi.fn(), back: vi.fn() }),
}))
vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../../src/services/bridges/identity-bridge', () => ({
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
      fetch: mockFetch,
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

describe('LoginLogs (Phase 3 read-only page)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    mockRoute.query = {}
    mockFetch.mockClear()
  })

  it('mounts and fetches login logs on load', async () => {
    const wrapper = mount(LoginLogs, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.find('.dt').exists()).toBe(true)
    expect(wrapper.find('.dt').attributes('data-rows')).toBe('2')
  })

  it('does not render a Create button (read-only page)', async () => {
    const wrapper = mount(LoginLogs, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.find('.t-crud-page__create').exists()).toBe(false)
  })

  it('seeds the userId filter from a ?userId= deep link before loading', async () => {
    mockRoute.query = { userId: 'u1' }
    mount(LoginLogs, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(mockFetch).toHaveBeenCalled()
    const lastCall = mockFetch.mock.calls[mockFetch.mock.calls.length - 1] as unknown as [
      { filters: Record<string, unknown> },
    ]
    expect(lastCall[0].filters).toMatchObject({ userId: 'u1' })
  })

  it('loads unfiltered when no ?userId= is present', async () => {
    mount(LoginLogs, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(mockFetch).toHaveBeenCalled()
    const lastCall = mockFetch.mock.calls[mockFetch.mock.calls.length - 1] as unknown as [
      { filters: Record<string, unknown> },
    ]
    expect(lastCall[0].filters.userId).toBeUndefined()
  })
})
