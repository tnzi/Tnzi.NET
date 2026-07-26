import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import Dictionaries from '../../../src/pages/system/Dictionaries.vue'

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../../src/services/bridges/system-bridge', () => ({
  createSystemBridge: () => ({
    settings: {
      fetch: vi.fn(async () => ({
        items: [
          { id: 's1', key: 'site.name', value: 'My App', group: 'general', isSystem: false, sortOrder: 1 },
          { id: 's2', key: 'site.theme', value: 'dark', group: 'ui', isSystem: false, sortOrder: 2 },
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

describe('Dictionaries page (Phase 3.13)', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  // Key/value pairs render as document rows: the key leads and the value gets
  // the right-hand slot, instead of a middle table column that truncated first.
  it('mounts and renders one row per entry, key and value both visible', async () => {
    const wrapper = mount(Dictionaries, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.findAll('.t-item-card')).toHaveLength(2)
    expect(wrapper.text()).toContain('site.name')
    expect(wrapper.text()).toContain('My App')
  })

  it('opens form modal when Create button is clicked', async () => {
    const wrapper = mount(Dictionaries, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    // Card/row pages use the shell's own create button; only TCrudPage adds
    // the legacy `t-crud-page__create` alias on top of it.
    await wrapper.find('.t-list-shell__create').trigger('click')
    expect(wrapper.find('form').exists()).toBe(true)
  })
})
