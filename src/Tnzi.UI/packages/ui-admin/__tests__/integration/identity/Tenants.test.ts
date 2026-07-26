import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import Tenants from '../../../src/pages/identity/Tenants.vue'

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
      fetch: vi.fn(async () => ({
        items: [
          { id: 't1', name: 'Acme Corp', code: 'ACME', status: 'active', plan: 'pro' },
          { id: 't2', name: 'Beta LLC', code: 'BETA', status: 'suspended', plan: 'free' },
        ],
        totalCount: 2,
        pageIndex: 1,
        pageSize: 20,
      })),
      create: vi.fn(async (data: unknown) => ({ ...(data as object), id: 't3' })),
      update: vi.fn(async (id: string, data: unknown) => ({ id, ...(data as object) })),
      delete: vi.fn(async () => undefined),
    },
    loginLogs: { fetch: vi.fn() },
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

describe('Tenants page', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  // The page renders a card per tenant, not a data table: a deployment has a
  // handful of tenants and the questions asked of the list ("who is on here,
  // are they live, when do they lapse") are per-record, not column comparisons.
  it('mounts and renders one card per tenant', async () => {
    const wrapper = mount(Tenants, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.findAll('.tenant-card')).toHaveLength(2)
    expect(wrapper.text()).toContain('Acme Corp')
    expect(wrapper.text()).toContain('BETA')
  })

  it('opens form modal when Create button is clicked', async () => {
    const wrapper = mount(Tenants, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    // Card/row pages use the shell's own create button; only TCrudPage adds
    // the legacy `t-crud-page__create` alias on top of it.
    await wrapper.find('.t-list-shell__create').trigger('click')
    expect(wrapper.find('form').exists()).toBe(true)
  })
})
