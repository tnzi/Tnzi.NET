import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

vi.mock('vue-router', () => ({
  useRoute: () => ({ meta: {}, query: {}, params: {} }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
}))
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))

const temporaryFiles = vi.fn(async () => [
  { id: 't1', originalName: 'tmp.bin', size: 4096, contentType: 'application/octet-stream', provider: 'local', creationTime: '2026-01-01T00:00:00Z', referenceCount: 0, extension: '.bin', fileName: 'tmp.bin', url: '/files/t1' },
])
const trigger = vi.fn(async () => 1)
vi.mock('../../../src/services/bridges/storage-bridge', () => ({
  createStorageBridge: () => ({ cleanup: { temporaryFiles, trigger } }),
}))

import Maintenance from '../../../src/pages/storage/Maintenance.vue'

describe('Maintenance page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    temporaryFiles.mockClear()
    trigger.mockClear()
  })

  it('mounts and lists temporary files', async () => {
    const wrapper = mount(Maintenance)
    await flushPromises()
    expect(temporaryFiles).toHaveBeenCalledTimes(1)
    expect(wrapper.text()).toContain('tmp.bin')
  })
})
