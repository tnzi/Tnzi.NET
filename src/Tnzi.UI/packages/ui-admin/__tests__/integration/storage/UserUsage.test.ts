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

const topUsers = vi.fn(async () => [
  { userId: 'u1', fileCount: 12, totalSize: 2048, formattedSize: '2 KB' },
  { userId: 'u2', fileCount: 3, totalSize: 512, formattedSize: '512 B' },
])
const forUser = vi.fn(async () => ({ userId: 'u3', fileCount: 1, totalSize: 100, formattedSize: '100 B' }))
vi.mock('../../../src/services/bridges/storage-bridge', () => ({
  createStorageBridge: () => ({ userUsage: { topUsers, forUser } }),
}))

import UserUsage from '../../../src/pages/storage/UserUsage.vue'

describe('UserUsage page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    topUsers.mockClear()
    forUser.mockClear()
  })

  it('mounts and loads top users', async () => {
    const wrapper = mount(UserUsage)
    await flushPromises()
    expect(topUsers).toHaveBeenCalledTimes(1)
    expect(wrapper.text()).toContain('u1')
    expect(wrapper.text()).toContain('u2')
  })
})
