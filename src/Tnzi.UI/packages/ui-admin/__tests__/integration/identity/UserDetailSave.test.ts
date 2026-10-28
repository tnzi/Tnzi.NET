import { beforeEach, describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * The admin user page's profile save against the replace-semantics update.
 *
 * ★★ The form edits every profile field but the avatar, and the server
 * replaces the profile as a whole. Sending the form as-is cleared the avatar
 * (and dropped its file reference) on every save.
 */
const api = vi.hoisted(() => ({
  getById: vi.fn(),
  update: vi.fn(),
}))

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../../src/services/bridges/identity-bridge', () => ({
  createIdentityBridge: () => ({
    users: { getById: api.getById, update: api.update, lock: vi.fn(), unlock: vi.fn(), resetPassword: vi.fn() },
  }),
}))
vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { id: '1' }, query: {} }),
  useRouter: () => ({
    replace: vi.fn(async () => undefined),
    push: vi.fn(async () => undefined),
    resolve: (to: { name: string }) => ({ path: `/admin/${String(to.name).replace(/\./g, '/')}` }),
  }),
  RouterLink: { name: 'RouterLink', props: ['to'], template: '<a><slot /></a>' },
}))

import UserDetail from '../../../src/pages/identity/UserDetail.vue'

const loaded = {
  id: '1',
  userName: 'ivy@corp.example',
  email: 'ivy@corp.example',
  phoneNumber: '+15550000001',
  firstName: 'Ivy',
  lastName: 'Lee',
  nickname: 'ivy',
  avatar: 'https://cdn.example/ivy.png',
  avatarId: 'file-1',
  gender: 2,
  birthday: '1990-05-01T00:00:00Z',
  bio: 'Payroll lead',
  address: '1 Main St',
  website: 'https://ivy.example',
  roles: ['Staff'],
}

interface PageVm {
  form: Record<string, unknown>
  save: () => Promise<void>
}

const stubs = {
  UserRolesSection: true,
  UserGrantsSection: true,
  UserSecuritySection: true,
  UserSessionsSection: true,
  UserLoginLogsSection: true,
}

describe('UserDetail page: profile save', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    api.getById.mockReset().mockResolvedValue({ ...loaded })
    api.update.mockReset().mockResolvedValue({ ...loaded })
  })

  it('carries the avatar through when saving the profile', async () => {
    const wrapper = mount(UserDetail, { props: { id: '1' }, global: { stubs } })
    await flushPromises()
    const vm = wrapper.vm as unknown as PageVm

    vm.form.bio = 'Payroll and benefits lead'
    await vm.save()

    expect(api.update).toHaveBeenCalledTimes(1)
    const [id, body] = api.update.mock.calls[0]
    expect(id).toBe('1')
    expect(body).toMatchObject({
      bio: 'Payroll and benefits lead',
      firstName: 'Ivy',
      nickname: 'ivy',
      avatarId: 'file-1',
      avatarUrl: 'https://cdn.example/ivy.png',
    })
  })
})
