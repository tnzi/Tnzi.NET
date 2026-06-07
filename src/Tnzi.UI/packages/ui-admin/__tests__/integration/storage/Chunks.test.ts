import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import Chunks from '../../../src/pages/storage/Chunks.vue'

// The page now calls useAdminClient() to get an HttpClient for the real
// bridge. Mock the composable since the bridge is also fully stubbed below.
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), delete: vi.fn() }),
}))

vi.mock('../../../src/services/bridges/storage-bridge', () => ({
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
    chunks: {
      fetch: vi.fn(async () => ({
        items: [
          { id: 'c1', uploadId: 'sess-1', chunkIndex: 0, size: 1024, status: 'completed', createdAt: '2024-01-01T00:00:00Z' },
          { id: 'c2', uploadId: 'sess-1', chunkIndex: 1, size: 1024, status: 'completed', createdAt: '2024-01-01T00:00:01Z' },
        ],
        totalCount: 2,
        pageIndex: 1,
        pageSize: 20,
      })),
      delete: vi.fn(async () => undefined),
    },
    versions: { fetch: vi.fn(), restore: vi.fn() },
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

describe('Chunks page (Phase 3.19)', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('mounts and fetches chunks on load', async () => {
    const wrapper = mount(Chunks, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.find('.dt').exists()).toBe(true)
    expect(wrapper.find('.dt').attributes('data-rows')).toBe('2')
  })
})
