import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import type { UserSignInPolicyDto } from '@tnzi/core/services/identity'

/**
 * `TSignInPolicyPanel` keyed on `userId`.
 *
 * Save writes the form to the CURRENT `userId`. So whatever fills the form has
 * to belong to that account: a response for the previous one arriving late
 * would put A's allow-list in front of the operator under B, and one click on
 * Save would store it on B.
 */
const bridge = {
  userSecurity: {
    getSignInPolicy: vi.fn<(id: string) => Promise<UserSignInPolicyDto>>(),
    setIpAllowList: vi.fn<(id: string, data: unknown) => Promise<UserSignInPolicyDto>>(),
  },
}

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../../src/services/bridges/identity-bridge', () => ({ createIdentityBridge: () => bridge }))

import TSignInPolicyPanel from '../../../src/components/auth/TSignInPolicyPanel.vue'
import { useAdminAuthStore } from '../../../src/stores/useAdminAuthStore'

const policyOf = (userId: string, allowedIps: string): UserSignInPolicyDto => ({
  userId,
  ipAllowListEnabled: true,
  allowedIps,
  entries: [],
  exemptedByRoles: [],
  callerIpAddress: null,
})

const textarea = (w: ReturnType<typeof mount>) => w.find('textarea').element as HTMLTextAreaElement

describe('TSignInPolicyPanel - switching accounts', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    useAdminAuthStore().setUserInfo({ id: 'op', username: 'operator', roles: [], permissions: ['user.view', 'user.security'] })
  })

  it("keeps the previous account's late response out of the form", async () => {
    let resolveA: (p: UserSignInPolicyDto) => void = () => {}
    bridge.userSecurity.getSignInPolicy.mockImplementation((id: string) =>
      id === 'user-a'
        ? new Promise<UserSignInPolicyDto>((resolve) => { resolveA = resolve })
        : Promise.resolve(policyOf('user-b', '10.0.0.2')),
    )
    const w = mount(TSignInPolicyPanel, { props: { userId: 'user-a' } })
    await flushPromises()

    await w.setProps({ userId: 'user-b' })
    await flushPromises()
    resolveA(policyOf('user-a', '10.0.0.1'))
    await flushPromises()

    expect(textarea(w).value).toBe('10.0.0.2')
  })

  it("does not leave the previous account's list editable while the next one loads", async () => {
    bridge.userSecurity.getSignInPolicy.mockImplementation((id: string) =>
      id === 'user-a' ? Promise.resolve(policyOf('user-a', '10.0.0.1')) : new Promise<UserSignInPolicyDto>(() => {}),
    )
    const w = mount(TSignInPolicyPanel, { props: { userId: 'user-a' } })
    await flushPromises()
    expect(textarea(w).value).toBe('10.0.0.1')

    await w.setProps({ userId: 'user-b' })
    await flushPromises()

    expect(w.find('textarea').exists()).toBe(false)
  })
})
