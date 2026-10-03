import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'

/** Set (or clear) `window.PublicKeyCredential` without replacing `window`. */
function setPublicKeyCredential(value: unknown): void {
  if (value === undefined) {
    delete (window as unknown as Record<string, unknown>).PublicKeyCredential
    return
  }
  ;(window as unknown as Record<string, unknown>).PublicKeyCredential = value
}
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

// Passkey management in the user centre. Two things carry the weight here:
//   1. the group is gated on a capability that folds in BOTH the deployment
//      flag and browser support - offering enrolment on a browser without the
//      JSON bridges fails midway, after the user has already been prompted;
//   2. dismissing the system dialog resolves to `null` and is NOT an error.

const passkeyRows = [
  { credentialId: 'cred-1', name: 'My iPhone', createdAt: '2026-08-16T00:00:00Z', isBackedUp: true },
  { credentialId: 'cred-2', name: null, createdAt: '2026-08-15T00:00:00Z', isBackedUp: false },
]

const me = {
  getProfile: vi.fn(async () => ({
    id: 'u1', userName: 'alice', nickname: 'Ali', email: 'a@a.com', phoneNumber: '', roles: ['Admin'], gender: 0,
  })),
  getDetail: vi.fn(async () => ({ avatarId: null, avatarUrl: null })),
  updateProfile: vi.fn(async () => ({ id: 'u1', userName: 'alice' })),
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
  getPasskeys: vi.fn(async () => passkeyRows),
  registerPasskey: vi.fn(async () => passkeyRows[0]),
  removePasskey: vi.fn(async () => undefined),
}

const authConfig = { allowEmailLogin: true, allowSmsLogin: true, oAuthProviders: [], enablePasskey: true }
const getAuthConfig = vi.fn(async () => authConfig)

const messageApi = {
  success: vi.fn(), error: vi.fn(), warning: vi.fn(), info: vi.fn(),
  loading: vi.fn(), create: vi.fn(), destroyAll: vi.fn(),
}

vi.mock('../../../src/pages/_shared/safe-message', () => ({ useSafeMessage: () => messageApi }))
vi.mock('../../../src/plugin/client', () => ({
  useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }),
}))
vi.mock('../../../src/services/bridges/identity-bridge', () => ({
  createIdentityBridge: () => ({ me, getAuthConfig, oauthLoginUrl: () => '' }),
}))
vi.mock('../../../src/services/bridges/storage-bridge', () => ({
  createStorageBridge: () => ({ files: { previewUrl: (id: string) => `/preview/${id}`, upload: vi.fn() } }),
}))

import SecuritySection from '../../../src/pages/account/sections/SecuritySection.vue'
import {
  createUserCenterState,
  provideUserCenterContext,
} from '../../../src/pages/account/user-center-context'
import { makePageTranslator } from '../../../src/pages/_shared/translate'

/**
 * Mount the section inside a host that installs the REAL user-centre context,
 * so the capability derivation under test is the production one (auth-config
 * probe + browser support) rather than a hand-written stand-in that could drift
 * from it.
 */
function mountSecurity() {
  const Host = {
    components: { SecuritySection },
    setup() {
      const bridge = { me, getAuthConfig, oauthLoginUrl: () => '' }
      const ctx = createUserCenterState({
        bridge: bridge as never,
        storage: { files: { previewUrl: (id: string) => `/preview/${id}` } } as never,
        authStore: { userInfo: null } as never,
        message: messageApi as never,
        t: makePageTranslator('account.userCenter'),
        config: {},
        logoutAndRedirect: () => undefined,
      })
      provideUserCenterContext(ctx)
      // UserCenter.vue runs the capability probe on mount; the section reacts to
      // its result, so the host has to run it too or every capability reads false.
      void ctx.loadAuthConfig()
      return {}
    },
    template: '<SecuritySection />',
  }
  return mount(Host)
}

describe('SecuritySection - passkeys', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    authConfig.enablePasskey = true
    // The capability check reads the browser's WebAuthn JSON bridges, which
    // happy-dom does not implement. Stub the property, NOT the whole `window` -
    // naive-ui needs the real one (it calls window.addEventListener during setup).
    setPublicKeyCredential({
      parseCreationOptionsFromJSON: () => ({}),
      parseRequestOptionsFromJSON: () => ({}),
    })
  })

  afterEach(() => setPublicKeyCredential(undefined))

  it('lists the registered passkeys when the deployment enables them', async () => {
    const wrapper = mountSecurity()
    await flushPromises()

    expect(me.getPasskeys).toHaveBeenCalled()
    expect(wrapper.text()).toContain('My iPhone')
    // A credential with no name still needs a label to be removable.
    expect(wrapper.text()).toContain('Unnamed passkey')
  })

  /**
   * Fail-closed: passkeys default to off server-side, so a deployment that has
   * not enabled them must not advertise an enrolment flow that would 400.
   */
  it('renders nothing and issues no request when the deployment has passkeys off', async () => {
    authConfig.enablePasskey = false

    const wrapper = mountSecurity()
    await flushPromises()

    expect(me.getPasskeys).not.toHaveBeenCalled()
    expect(wrapper.text()).not.toContain('Add a passkey')
  })

  /** Same gate, other half: the browser cannot run the ceremony. */
  it('renders nothing when the browser lacks the WebAuthn JSON bridges', async () => {
    // The credential type shipped years before the JSON bridges; testing for it
    // alone reports "supported" on browsers that then throw at parse time.
    setPublicKeyCredential(function Stub() {})

    const wrapper = mountSecurity()
    await flushPromises()

    expect(me.getPasskeys).not.toHaveBeenCalled()
    expect(wrapper.text()).not.toContain('Add a passkey')
  })

  /**
   * ★ Dismissing the system dialog is a normal outcome. Reporting it as a
   * failure would be lying about what happened, and would train users to
   * distrust a prompt they legitimately cancelled.
   */
  it('treats a dismissed system dialog as a non-event, not an error', async () => {
    me.registerPasskey.mockResolvedValueOnce(null as never)

    const wrapper = mountSecurity()
    await flushPromises()
    me.getPasskeys.mockClear()

    await (wrapper.findComponent(SecuritySection).vm as unknown as { addPasskey: () => Promise<void> }).addPasskey()

    expect(messageApi.error).not.toHaveBeenCalled()
    expect(messageApi.success).not.toHaveBeenCalled()
    // No reload either - nothing changed.
    expect(me.getPasskeys).not.toHaveBeenCalled()
  })

  it('reloads the list after a successful enrolment', async () => {
    const wrapper = mountSecurity()
    await flushPromises()
    me.getPasskeys.mockClear()

    await (wrapper.findComponent(SecuritySection).vm as unknown as { addPasskey: () => Promise<void> }).addPasskey()

    expect(messageApi.success).toHaveBeenCalled()
    expect(me.getPasskeys).toHaveBeenCalledTimes(1)
  })

  /**
   * When the deployment offers a key as the second step, the two-factor row
   * carries the inventory and the standalone block must not double it.
   */
  it('yields to the two-factor row when the deployment offers a key as the second step', async () => {
    me.getTwoFactorStatus.mockResolvedValueOnce({
      isEnabled: false,
      supportedTypes: [],
      isTotpEnabled: false,
      methods: [{ type: 'Passkey', available: true, enabled: false, isPreferred: false }],
    } as never)

    const wrapper = mountSecurity()
    await flushPromises()

    expect(wrapper.text()).not.toContain('Add a passkey')
    expect(wrapper.text()).toContain('Security key / passkey')
    // The row listed them instead.
    expect(wrapper.find('.t-2fa__keys').text()).toContain('My iPhone')
  })

  it('removes a credential and reloads', async () => {
    const wrapper = mountSecurity()
    await flushPromises()
    me.getPasskeys.mockClear()

    await (
      wrapper.findComponent(SecuritySection).vm as unknown as {
        removePasskey: (id: string) => Promise<void>
      }
    ).removePasskey('cred-1')

    expect(me.removePasskey).toHaveBeenCalledWith('cred-1')
    expect(me.getPasskeys).toHaveBeenCalledTimes(1)
  })
})
