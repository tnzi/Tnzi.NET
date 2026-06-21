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

const byFile = vi.fn(async () => [
  { id: 'r1', fileId: 'f1', entityType: 'User', entityId: 'u1', fieldName: 'Avatar', isTemporary: false, creationTime: '2026-01-01T00:00:00Z' },
])
vi.mock('../../../src/services/bridges/storage-bridge', () => ({
  createStorageBridge: () => ({ references: { byFile, byEntity: vi.fn() } }),
}))

import References from '../../../src/pages/storage/References.vue'

describe('References page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    byFile.mockClear()
  })

  it('mounts and queries references for a file id', async () => {
    const wrapper = mount(References)
    await flushPromises()
    // Set the file id then click the query button.
    const vm = wrapper.vm as unknown as { fileId: string }
    vm.fileId = 'f1'
    await flushPromises()
    const buttons = wrapper.findAll('button')
    await buttons[buttons.length - 1]!.trigger('click')
    await flushPromises()
    expect(byFile).toHaveBeenCalledWith('f1')
    expect(wrapper.text()).toContain('User')
  })
})
