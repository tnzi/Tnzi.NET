import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import type { TwoFactorStatusDto, UserSignInPolicyDto } from '@tnzi/core/services/identity'

/**
 * `UserSecurityBlocks` - the two sign-in security blocks without the section
 * chrome, for a host whose record page already has the section they belong in.
 *
 * Locks the contract a host relies on: nothing but a `userId` goes in, no
 * section bar comes out, both halves load, and `reload()` re-fetches both.
 */
const bridge = {
  userSecurity: {
    getTwoFactorStatus: vi.fn<(id: string) => Promise<TwoFactorStatusDto>>(),
    suspendTwoFactor: vi.fn(async () => undefined),
    resumeTwoFactor: vi.fn(async () => undefined),
    enableTwoFactorMethod: vi.fn(async () => undefined),
    disableTwoFactorMethod: vi.fn(async () => undefined),
    setPreferredTwoFactor: vi.fn(async () => undefined),
    resetTwoFactor: vi.fn(async () => undefined),
    getSignInPolicy: vi.fn<(id: string) => Promise<UserSignInPolicyDto>>(),
    setIpAllowList: vi.fn(),
  },
}

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../../src/services/bridges/identity-bridge', () => ({ createIdentityBridge: () => bridge }))

import UserSecurityBlocks from '../../../src/pages/identity/sections/UserSecurityBlocks.vue'
import TSignInPolicyPanel from '../../../src/components/auth/TSignInPolicyPanel.vue'
import * as root from '../../../src/index'
import { useAdminAuthStore } from '../../../src/stores/useAdminAuthStore'

const status: TwoFactorStatusDto = {
  isEnabled: false,
  supportedTypes: [] as never,
  isTotpEnabled: false,
  preferredType: null,
  methods: [{ type: 'Email' as never, available: true, enabled: false, isPreferred: false }],
}
const policy: UserSignInPolicyDto = {
  userId: 'u1',
  ipAllowListEnabled: false,
  allowedIps: null,
  entries: [],
  exemptedByRoles: [],
  callerIpAddress: null,
}

describe('UserSecurityBlocks', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    useAdminAuthStore().setUserInfo({ id: 'op', username: 'operator', roles: [], permissions: ['user.view', 'user.security'] })
    bridge.userSecurity.getTwoFactorStatus.mockResolvedValue(status)
    bridge.userSecurity.getSignInPolicy.mockResolvedValue(policy)
  })

  it('is on the package root, as is the allow-list panel on its own', () => {
    expect(root.UserSecurityBlocks).toBe(UserSecurityBlocks)
    expect(root.TSignInPolicyPanel).toBe(TSignInPolicyPanel)
  })

  it('renders both blocks for the userId with no section chrome around them', async () => {
    const w = mount(UserSecurityBlocks, {
      props: { userId: 'u1' },
      global: { stubs: { Spin: { template: '<div><slot /></div>' } } },
    })
    await flushPromises()

    expect(bridge.userSecurity.getTwoFactorStatus).toHaveBeenCalledWith('u1')
    expect(bridge.userSecurity.getSignInPolicy).toHaveBeenCalledWith('u1')
    expect(w.find('.t-detail-section').exists()).toBe(false)
    const titles = w.findAll('.t-detail-block__title > span').map((n) => n.text())
    expect(titles).toEqual(['Two-factor authentication', 'Sign-in IP allow-list'])
    // One rule between the two blocks, drawn here.
    expect(w.findAll('.usb__block')).toHaveLength(2)
  })

  it('reload() re-fetches both halves', async () => {
    const w = mount(UserSecurityBlocks, {
      props: { userId: 'u1' },
      global: { stubs: { Spin: { template: '<div><slot /></div>' } } },
    })
    await flushPromises()
    vi.clearAllMocks()

    await (w.vm as unknown as { reload: () => Promise<void> }).reload()

    expect(bridge.userSecurity.getTwoFactorStatus).toHaveBeenCalledTimes(1)
    expect(bridge.userSecurity.getSignInPolicy).toHaveBeenCalledTimes(1)
  })
})
