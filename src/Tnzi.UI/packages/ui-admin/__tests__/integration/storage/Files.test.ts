import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import Files from '../../../src/pages/storage/Files.vue'

vi.mock('vue-router', () => ({
  useRoute: () => ({ meta: {}, query: {}, params: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
}))
vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../../src/services/bridges/storage-bridge', () => ({
  createStorageBridge: () => ({
    files: {
      fetch: vi.fn(async () => ({
        items: [
          { id: 'f1', fileName: 'photo.jpg', originalName: 'photo.jpg', size: 1024, contentType: 'image/jpeg', url: '/files/f1', referenceCount: 0, extension: '.jpg', provider: 'local' },
          { id: 'f2', fileName: 'doc.pdf', originalName: 'doc.pdf', size: 2048, contentType: 'application/pdf', url: '/files/f2', referenceCount: 0, extension: '.pdf', provider: 'local' },
        ],
        totalCount: 2,
        pageIndex: 1,
        pageSize: 20,
      })),
      create: vi.fn(async (d: unknown) => ({ ...(d as object), id: 'f-new' })),
      update: vi.fn(async (id: string, d: unknown) => ({ id, ...(d as object) })),
      delete: vi.fn(async () => undefined),
      downloadUrl: vi.fn(() => '/api/files/f1/download'),
      initUpload: vi.fn(async () => ({ uploadId: 'sess-1' })),
      uploadChunk: vi.fn(async () => undefined),
      completeUpload: vi.fn(async () => ({ url: '/files/f-new' })),
    },
    chunks: { fetch: vi.fn(), delete: vi.fn() },
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
  TChunkFileUpload: { props: ['uploader'], template: '<div class="chunk-upload" />' },
}

describe('Files page (Phase 3.18)', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('mounts and fetches files on load', async () => {
    const wrapper = mount(Files, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.find('.dt').exists()).toBe(true)
    expect(wrapper.find('.dt').attributes('data-rows')).toBe('2')
  })

  it('renders TChunkFileUpload for file uploads', async () => {
    const wrapper = mount(Files, { global: { stubs } })
    await nextTick()
    await new Promise(r => setTimeout(r, 10))
    expect(wrapper.find('.chunk-upload').exists()).toBe(true)
  })
})
