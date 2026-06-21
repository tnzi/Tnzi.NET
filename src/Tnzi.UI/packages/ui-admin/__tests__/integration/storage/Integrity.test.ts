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

const batchVerify = vi.fn(async () => ({
  totalChecked: 10,
  healthy: 8,
  missing: 1,
  corrupted: 1,
  errors: 0,
  problems: [
    { fileId: 'f1', originalName: 'gone.png', physicalFileExists: false, status: 'Missing', error: 'not found' },
  ],
}))
vi.mock('../../../src/services/bridges/storage-bridge', () => ({
  createStorageBridge: () => ({ integrity: { verifyOne: vi.fn(), batchVerify } }),
}))

import Integrity from '../../../src/pages/storage/Integrity.vue'

describe('Integrity page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    batchVerify.mockClear()
  })

  it('mounts and runs batch verification on click', async () => {
    const wrapper = mount(Integrity)
    await flushPromises()
    // Locate the verify button by its label (NInputNumber renders stepper buttons first).
    const btn = wrapper.findAll('button').find((b) => b.text().includes('Verify'))
    expect(btn).toBeTruthy()
    await btn!.trigger('click')
    await flushPromises()
    expect(batchVerify).toHaveBeenCalledTimes(1)
    expect(wrapper.text()).toContain('gone.png')
  })
})
