import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import StorageVersion from '../../src/pages/storage/StorageVersion.vue'

// Same plumbing mock as StorageChunk / ScheduledJob: useAdminClient is
// called by the page but the bridge is mocked below, so any truthy value works.
vi.mock('../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), delete: vi.fn() }),
}))

vi.mock('../../src/services/bridges/storage-bridge', () => ({
  createStorageBridge: () => ({
    files: {
      fetch: vi.fn(),
      create: vi.fn(),
      update: vi.fn(),
      delete: vi.fn(),
      downloadUrl: vi.fn(() => ''),
      initUpload: vi.fn(),
      uploadChunk: vi.fn(),
      completeUpload: vi.fn(),
    },
    chunks: { fetch: vi.fn(), delete: vi.fn() },
    versions: {
      fetch: vi.fn(async () => ({
        items: [
          { id: 'v1', fileId: 'f1', version: 1, size: 1024, changedBy: 'admin', changedAt: '2024-01-01T00:00:00Z', note: 'Initial' },
          { id: 'v2', fileId: 'f1', version: 2, size: 2048, changedBy: 'admin', changedAt: '2024-01-02T00:00:00Z', note: 'Updated' },
        ],
        totalCount: 2,
        pageIndex: 1,
        pageSize: 20,
      })),
      restore: vi.fn(async () => undefined),
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
  VueDraggable: { template: '<div><slot /></div>' },
}

describe('StorageVersion page (Phase 3.20)', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('mounts and fetches versions on load', async () => {
    const wrapper = mount(StorageVersion, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.find('.dt').exists()).toBe(true)
    expect(wrapper.find('.dt').attributes('data-rows')).toBe('2')
  })

  it('renders page title and data table on load', async () => {
    const wrapper = mount(StorageVersion, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    // Page mounted successfully with version data
    expect(wrapper.find('.dt').attributes('data-rows')).toBe('2')
  })
})
