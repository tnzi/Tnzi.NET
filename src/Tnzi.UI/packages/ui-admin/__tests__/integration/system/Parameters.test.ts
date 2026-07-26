import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import Parameters from '../../../src/pages/system/Parameters.vue'

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../../src/services/bridges/system-bridge', () => ({
  createSystemBridge: () => ({
    settings: {
      fetch: vi.fn(async () => ({
        items: [
          // valueType is the SettingValueType member name (JsonStringEnumConverter wire shape).
          { id: 'p1', key: 'app.maxUploadSize', value: '10', valueType: 'Integer', group: 'app' },
          { id: 'p2', key: 'app.enableSignup', value: 'true', valueType: 'Boolean', group: 'app' },
        ],
        totalCount: 2,
        pageIndex: 1,
        pageSize: 20,
      })),
      create: vi.fn(async (d: unknown) => ({ ...(d as object), id: 'new' })),
      update: vi.fn(async (id: string, d: unknown) => ({ id, ...(d as object) })),
      delete: vi.fn(async () => undefined),
    },
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

describe('Parameters page (Phase 3.14)', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('mounts and fetches parameters on load', async () => {
    const wrapper = mount(Parameters, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.find('.dt').exists()).toBe(true)
    expect(wrapper.find('.dt').attributes('data-rows')).toBe('2')
  })

  it('opens form modal when Create button is clicked', async () => {
    const wrapper = mount(Parameters, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    await wrapper.find('.t-crud-page__create').trigger('click')
    expect(wrapper.find('form').exists()).toBe(true)
  })
})
