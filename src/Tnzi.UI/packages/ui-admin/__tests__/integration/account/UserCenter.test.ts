import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

// ── Bridge / client mocks (declared before the SFC import so the factories see
//    initialised spies - same ordering as the other page integration tests). ──
const me = {
  getProfile: vi.fn(async () => ({
    id: 'u1',
    userName: 'alice',
    nickname: 'Ali',
    email: 'a@a.com',
    phoneNumber: '',
    roles: ['Admin'],
    gender: 0,
  })),
  getDetail: vi.fn(async () => ({ avatarId: null, avatarUrl: null })),
  updateProfile: vi.fn(async (d: unknown) => ({ id: 'u1', userName: 'alice', ...(d as object) })),
  getSessions: vi.fn(async () => []),
  getLoginHistory: vi.fn(async () => []),
  getLinkedAccounts: vi.fn(async () => []),
  getTwoFactorStatus: vi.fn(async () => ({ isEnabled: false, supportedTypes: [], isTotpEnabled: false, methods: [] })),
  disableTwoFactorMethod: vi.fn(async () => undefined),
  setPreferredTwoFactor: vi.fn(async () => undefined),
  changePassword: vi.fn(async () => undefined),
  revokeSession: vi.fn(async () => undefined),
  revokeAllSessions: vi.fn(async () => undefined),
  unlinkAccount: vi.fn(async () => undefined),
  deactivate: vi.fn(async () => undefined),
  deleteAccount: vi.fn(async () => undefined),
  exportPersonalData: vi.fn(async () => ({})),
  sendChangeEmailCode: vi.fn(async () => undefined),
  confirmChangeEmail: vi.fn(async () => undefined),
  sendChangePhoneCode: vi.fn(async () => undefined),
  confirmChangePhone: vi.fn(async () => undefined),
  enableTwoFactor: vi.fn(async () => ''),
  disableTwoFactor: vi.fn(async () => undefined),
  suspendTwoFactor: vi.fn(async () => undefined),
  resumeTwoFactor: vi.fn(async () => undefined),
  getTotpSetup: vi.fn(async () => ({ sharedKey: 'S', authenticatorUri: 'otpauth://x' })),
  enableTotp: vi.fn(async () => undefined),
  disableTotp: vi.fn(async () => undefined),
}
const getAuthConfig = vi.fn(async () => ({
  allowEmailLogin: true,
  allowSmsLogin: true,
  oAuthProviders: [],
}))

vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))
vi.mock('../../../src/services/bridges/identity-bridge', () => ({
  createIdentityBridge: () => ({ me, getAuthConfig, oauthLoginUrl: () => '' }),
}))
vi.mock('../../../src/services/bridges/storage-bridge', () => ({
  createStorageBridge: () => ({
    files: { previewUrl: (id: string) => `/preview/${id}`, upload: vi.fn() },
  }),
}))

import UserCenter from '../../../src/pages/account/UserCenter.vue'
import { ADMIN_USER_CENTER_CONFIG_KEY } from '../../../src/plugin/userCenterConfig'
import type { AdminUserCenterConfig } from '../../../src/plugin/userCenterConfig'

function mountUserCenter(config?: AdminUserCenterConfig) {
  return mount(UserCenter, {
    global: config ? { provide: { [ADMIN_USER_CENTER_CONFIG_KEY]: config } } : {},
  })
}

describe('UserCenter (section-registry shell)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('mounts, loads the profile + auth config, and renders the six built-in sections', async () => {
    const wrapper = mountUserCenter()
    await flushPromises()

    expect(me.getProfile).toHaveBeenCalledTimes(1)
    expect(getAuthConfig).toHaveBeenCalledTimes(1)

    // Header shows the display name + role tag.
    expect(wrapper.text()).toContain('Ali')
    expect(wrapper.text()).toContain('Admin')

    // Left nav renders every built-in section label (en locale).
    const text = wrapper.text()
    for (const label of ['Profile', 'Security', 'Sessions', 'Login History', 'Linked Accounts', 'Danger Zone']) {
      expect(text).toContain(label)
    }

    // Default section = Profile → its form fields render (Username disabled input).
    expect(text).toContain('Username')
    expect(text).toContain('Nickname')
  })

  it('hides a built-in section via userCenter.hideSections', async () => {
    const wrapper = mountUserCenter({ hideSections: ['danger'] })
    await flushPromises()
    expect(wrapper.text()).not.toContain('Danger Zone')
    expect(wrapper.text()).toContain('Profile')
  })

  it('renders a consumer custom section in the nav', async () => {
    const Billing = { name: 'Billing', template: '<div>BILLING BODY</div>' }
    const wrapper = mountUserCenter({
      sections: [{ key: 'billing', label: 'Billing', icon: 'mdi:cc', group: 'Extras', component: Billing }],
    })
    await flushPromises()
    expect(wrapper.text()).toContain('Billing')
  })

  it('switches the active section (Profile → Security) and renders the target body', async () => {
    const wrapper = mountUserCenter()
    await flushPromises()
    // Security section not yet mounted → no password sub-title.
    expect(wrapper.text()).not.toContain('Change password')

    // Drive the section change through the detail engine (nav click emits this).
    const host = wrapper.findComponent({ name: 'TDetailLayout' })
    ;(host.vm as unknown as { onSection: (k: string) => void }).onSection('security')
    await flushPromises()

    expect(wrapper.text()).toContain('Change password')
    expect(me.getTwoFactorStatus).toHaveBeenCalled()
  })

  async function openSecurity(wrapper: ReturnType<typeof mountUserCenter>) {
    await flushPromises()
    const host = wrapper.findComponent({ name: 'TDetailLayout' })
    ;(host.vm as unknown as { onSection: (k: string) => void }).onSection('security')
    await flushPromises()
  }

  it('2FA (disabled): per-method rows are shown but disabled until the master switch is on', async () => {
    // Wire returns per-method state with PascalCase string types.
    me.getTwoFactorStatus.mockResolvedValue({
      isEnabled: false,
      supportedTypes: [],
      isTotpEnabled: false,
      methods: [
        { type: 'Totp', available: true, enabled: false, isPreferred: false },
        { type: 'Sms', available: true, enabled: false, isPreferred: false },
        { type: 'Email', available: true, enabled: false, isPreferred: false },
      ],
    })
    const wrapper = mountUserCenter()
    await openSecurity(wrapper)

    // Disabled → the method rows are VISIBLE (so the user can see what's
    // available) but their controls render disabled until 2FA is turned on.
    const before = wrapper.text()
    expect(before).toContain('Text message (SMS)')
    expect(before).toContain('Authenticator app')
    // The SMS/email method switches render disabled (the master switch does not).
    expect(wrapper.findAll('.n-switch--disabled').length).toBeGreaterThanOrEqual(1)

    // Flip the header master switch on → the rows become interactive.
    const sw = wrapper.find('.n-switch')
    expect(sw.exists()).toBe(true)
    await sw.trigger('click')
    await flushPromises()

    const text = wrapper.text()
    // SMS and Email each get their own row (distinct labels), TOTP has Set up.
    expect(text).toContain('Text message (SMS)')
    expect(text).toContain('Email code')
    expect(text).toContain('Set up')
    // Addresses are verified in this fixture → no method switch stays disabled.
    expect(wrapper.findAll('.n-switch--disabled').length).toBe(0)
  })

  it('2FA (TOTP channel disabled): authenticator row is hidden even after turning 2FA on', async () => {
    // Deployment turned EnableTotp off → backend omits TOTP from `methods`.
    me.getTwoFactorStatus.mockResolvedValue({
      isEnabled: false,
      supportedTypes: [],
      isTotpEnabled: false,
      methods: [
        { type: 'Sms', available: true, enabled: false, isPreferred: false },
        { type: 'Email', available: true, enabled: false, isPreferred: false },
      ],
    })
    const wrapper = mountUserCenter()
    await openSecurity(wrapper)

    const sw = wrapper.find('.n-switch')
    await sw.trigger('click')
    await flushPromises()

    const text = wrapper.text()
    // SMS / Email rows still render, but the authenticator (TOTP) row is gone.
    expect(text).toContain('Text message (SMS)')
    expect(text).toContain('Email code')
    expect(text).not.toContain('Authenticator app')
  })

  it('2FA (enabled: TOTP + SMS): master switch on, per-method rows with TOTP disable + preferred star', async () => {
    me.getTwoFactorStatus.mockResolvedValue({
      isEnabled: true,
      supportedTypes: ['Sms', 'Totp'],
      isTotpEnabled: true,
      preferredType: 'Totp',
      methods: [
        { type: 'Totp', available: true, enabled: true, isPreferred: true },
        { type: 'Sms', available: true, enabled: true, isPreferred: false },
      ],
    })
    const wrapper = mountUserCenter()
    await openSecurity(wrapper)

    // Master switch reflects the aggregate enabled flag (naive marks it active).
    const sw = wrapper.find('.n-switch')
    expect(sw.exists()).toBe(true)
    expect(sw.classes()).toContain('n-switch--active')

    const text = wrapper.text()
    expect(text).toContain('Text message (SMS)') // SMS row present
    expect(text).toContain('Authenticator app') // TOTP row
    expect(text).toContain('Enabled') // TOTP enabled tag
    expect(text).toContain('Disable') // TOTP individual remove
    // Two methods enabled → both an enable switch (SMS) and star affordances render.
    // Master + SMS switches = at least 2 switches present.
    expect(wrapper.findAll('.n-switch').length).toBeGreaterThanOrEqual(2)
  })

  it('2FA (active → master off): suspends (keeps config), never destructively disables', async () => {
    me.getTwoFactorStatus.mockResolvedValue({
      isEnabled: true,
      supportedTypes: ['Totp'],
      isTotpEnabled: true,
      preferredType: 'Totp',
      methods: [{ type: 'Totp', available: true, enabled: true, isPreferred: true }],
    })
    const wrapper = mountUserCenter()
    await openSecurity(wrapper)

    // Turn the master switch OFF → must SUSPEND (preserve), not wipe.
    const sw = wrapper.find('.n-switch')
    expect(sw.classes()).toContain('n-switch--active')
    await sw.trigger('click')
    await flushPromises()

    expect(me.suspendTwoFactor).toHaveBeenCalledTimes(1)
    expect(me.disableTwoFactor).not.toHaveBeenCalled()
  })

  it('2FA (suspended → master on): resumes the saved config; shows the "saved" hint', async () => {
    // Suspended state: master off (isEnabled=false) but a method stays configured.
    me.getTwoFactorStatus.mockResolvedValue({
      isEnabled: false,
      supportedTypes: ['Totp'],
      isTotpEnabled: true,
      preferredType: 'Totp',
      methods: [{ type: 'Totp', available: true, enabled: true, isPreferred: true }],
    })
    const wrapper = mountUserCenter()
    await openSecurity(wrapper)

    // Suspended hint reassures the config is kept; method rows stay visible but
    // disabled (master off) so the user sees the preserved setup.
    expect(wrapper.text()).toContain('Your configured methods are saved')

    // Turn the master switch ON → RESUME (not a fresh setup).
    const sw = wrapper.find('.n-switch')
    expect(sw.classes()).not.toContain('n-switch--active')
    await sw.trigger('click')
    await flushPromises()

    expect(me.resumeTwoFactor).toHaveBeenCalledTimes(1)
  })
})
