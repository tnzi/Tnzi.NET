import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import Shares from '../../../src/pages/storage/Shares.vue'

vi.mock('vue-router', () => ({
  useRoute: () => ({ meta: {}, query: {}, params: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
}))
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

const batchRevoke = vi.fn(async () => 1)
vi.mock('../../../src/services/bridges/storage-bridge', () => ({
  createStorageBridge: () => ({
    shares: {
      fetch: vi.fn(async () => ({
        items: [
          { id: 's1', fileId: 'f1', originalName: 'a.png', shareToken: 'tok1', accessCount: 3, requirePassword: false, isEnabled: true, isExpired: false, isExhausted: false, creationTime: '2026-01-01T00:00:00Z' },
          { id: 's2', fileId: 'f2', originalName: 'b.pdf', shareToken: 'tok2', accessCount: 0, requirePassword: true, isEnabled: false, isExpired: false, isExhausted: false, creationTime: '2026-01-02T00:00:00Z' },
        ],
        totalCount: 2,
        pageIndex: 1,
        pageSize: 20,
      })),
      byFile: vi.fn(async () => []),
      batchRevoke,
    },
  }),
}))

const stubs = {
  DataTable: { props: ['data'], template: '<div class="dt" :data-rows="data.length" />' },
  Pagination: { template: '<div />' },
  Input: { props: ['value'], template: '<input />' },
  Button: { template: '<button @click="$emit(\'click\')"><slot /></button>' },
  Modal: { props: ['show'], template: '<div v-if="show"><slot /></div>' },
  Popconfirm: { template: '<div><slot name="trigger" /></div>' },
  Checkbox: { template: '<input type="checkbox" />' },
  Form: { template: '<form><slot /></form>' },
  FormItem: { template: '<div><slot /></div>' },
  InputNumber: { template: '<input type="number" />' },
  Switch: { template: '<button />' },
  Select: { template: '<select />' },
  DatePicker: { template: '<input type="date" />' },
  VueDraggable: { template: '<div><slot /></div>' },
}

describe('Shares page', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('mounts and lists active shares', async () => {
    const wrapper = mount(Shares, { global: { stubs } })
    await nextTick()
    await new Promise((r) => setTimeout(r, 10))
    expect(wrapper.find('.dt').exists()).toBe(true)
    expect(wrapper.find('.dt').attributes('data-rows')).toBe('2')
  })
})
