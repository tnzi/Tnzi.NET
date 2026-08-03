import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { defineComponent, h, ref } from 'vue'

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

// The real `useSafeMessage()` degrades to a noop outside an NMessageProvider,
// which would make the save-attribution assertions unobservable.
const messageApi = {
  success: vi.fn(),
  error: vi.fn(),
  warning: vi.fn(),
  info: vi.fn(),
  loading: vi.fn(),
  create: vi.fn(),
  destroyAll: vi.fn(),
}

vi.mock('../../../src/pages/_shared/safe-message', () => ({
  useSafeMessage: () => messageApi,
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
import { ADMIN_USER_CENTER_CONFIG_KEY } from '../../../src/plugin/user-center-config'
import type { AdminUserCenterConfig } from '../../../src/plugin/user-center-config'
import { useUserCenterProfileExtra } from '../../../src/pages/account/useUserCenterProfileExtra'

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

  it('renders userCenter.profile.extra inside the built-in Profile section, after the save bar', async () => {
    const Extra = { name: 'ProfileExtra', template: '<div class="probe-extra">CONTACT BLOCK</div>' }
    const wrapper = mountUserCenter({ profile: { extra: Extra } })
    await flushPromises()

    // The framework's own identity fields + save bar are still there - the point
    // of the extension slot is that the app does NOT re-implement them.
    expect(wrapper.text()).toContain('Username')
    const saveBar = wrapper.find('.t-uc-save-bar')
    expect(saveBar.exists()).toBe(true)

    expect(wrapper.find('.probe-extra').exists()).toBe(true)
    // Page order: identity fields → framework Reset/Save → extension block.
    const html = wrapper.html()
    expect(html.indexOf('t-uc-save-bar')).toBeLessThan(html.indexOf('probe-extra'))
  })

  it('accepts a loader for profile.extra (same contract as section components)', async () => {
    const Extra = { name: 'ProfileExtraAsync', template: '<div class="probe-extra-async">LOADED</div>' }
    // `__esModule` mimics what a real `() => import('./X.vue')` resolves to -
    // defineAsyncComponent only unwraps `.default` for module-shaped results.
    const wrapper = mountUserCenter({
      profile: { extra: () => Promise.resolve({ __esModule: true, default: Extra }) },
    })
    await flushPromises()

    expect(wrapper.find('.probe-extra-async').exists()).toBe(true)
  })

  it('renders no extension block when profile.extra is not configured', async () => {
    const wrapper = mountUserCenter()
    await flushPromises()
    expect(wrapper.find('.t-uc-save-bar').exists()).toBe(true)
    expect(wrapper.find('.probe-extra').exists()).toBe(false)
  })

  // ── profile.extra joining the framework's single Reset/Save pair ──
  describe('profile.extra joined via useUserCenterProfileExtra', () => {
    type Handler = Parameters<typeof useUserCenterProfileExtra>[0]

    /** An extension block that registers a handler from its own setup(). */
    function joinedExtra(handler: Handler) {
      return defineComponent({
        name: 'ProfileExtraJoined',
        setup() {
          useUserCenterProfileExtra(handler)
          return () => h('div', { class: 'probe-extra-joined' }, 'JOINED BLOCK')
        },
      })
    }

    const barButtons = (w: ReturnType<typeof mountUserCenter>) =>
      w.findAll('.t-uc-save-bar button')
    const clickSave = async (w: ReturnType<typeof mountUserCenter>) => {
      const buttons = barButtons(w)
      await buttons[buttons.length - 1]!.trigger('click')
      await flushPromises()
    }

    it('Save writes the identity fields first, then awaits the registered handler', async () => {
      const extraSave = vi.fn(async () => undefined)
      const wrapper = mountUserCenter({ profile: { extra: joinedExtra({ save: extraSave }) } })
      await flushPromises()

      await clickSave(wrapper)

      expect(me.updateProfile).toHaveBeenCalledTimes(1)
      expect(extraSave).toHaveBeenCalledTimes(1)
      // Order is part of the contract: identity endpoint, then the app's own.
      expect(me.updateProfile.mock.invocationCallOrder[0]!).toBeLessThan(
        extraSave.mock.invocationCallOrder[0]!,
      )
      // One combined success, no error.
      expect(messageApi.success).toHaveBeenCalledTimes(1)
      expect(messageApi.error).not.toHaveBeenCalled()
    })

    it('moves the single save bar below the block (one form, one Reset/Save pair)', async () => {
      const wrapper = mountUserCenter({
        profile: { extra: joinedExtra({ save: vi.fn(async () => undefined) }) },
      })
      await flushPromises()

      const html = wrapper.html()
      expect(wrapper.find('.probe-extra-joined').exists()).toBe(true)
      // Exactly one bar, and it now sits AFTER the block it also governs.
      expect(wrapper.findAll('.t-uc-save-bar')).toHaveLength(1)
      expect(html.indexOf('probe-extra-joined')).toBeLessThan(html.indexOf('t-uc-save-bar'))
    })

    it('handler failure: names the extension half and leaves the identity half saved', async () => {
      // Identity write succeeds and returns a NEW nickname, so "the saved half
      // is reflected in the UI" is observable rather than asserted on faith.
      me.updateProfile.mockResolvedValueOnce({
        id: 'u1',
        userName: 'alice',
        nickname: 'Saved Ali',
        roles: ['Admin'],
      })
      const extraSave = vi.fn(async () => {
        throw new Error('detail backend down')
      })
      const wrapper = mountUserCenter({ profile: { extra: joinedExtra({ save: extraSave }) } })
      await flushPromises()

      await clickSave(wrapper)

      expect(me.updateProfile).toHaveBeenCalledTimes(1)
      expect(extraSave).toHaveBeenCalledTimes(1)
      // No blanket "save failed": the message says which half survived, and
      // carries the block's own error so the user can act on it.
      expect(messageApi.success).not.toHaveBeenCalled()
      const msg = String(messageApi.error.mock.calls.at(-1)?.[0] ?? '')
      expect(msg).toContain('Your account profile was saved')
      expect(msg).toContain('detail backend down')

      // The identity half is NOT rolled back and the UI agrees: the header
      // shows the value the server returned, and the form stopped counting as
      // unsaved (no dirty marker on the bar).
      expect(wrapper.text()).toContain('Saved Ali')
      expect(wrapper.find('.t-uc-save-bar__dirty').exists()).toBe(false)
    })

    it('identity failure: aborts before the handler runs, so nothing is written', async () => {
      me.updateProfile.mockRejectedValueOnce(new Error('profile backend down'))
      const extraSave = vi.fn(async () => undefined)
      const wrapper = mountUserCenter({ profile: { extra: joinedExtra({ save: extraSave }) } })
      await flushPromises()

      await clickSave(wrapper)

      expect(extraSave).not.toHaveBeenCalled()
      expect(messageApi.success).not.toHaveBeenCalled()
      const msg = String(messageApi.error.mock.calls.at(-1)?.[0] ?? '')
      expect(msg).toContain('Your account profile was not saved')
      expect(msg).toContain('profile backend down')
    })

    it('Reset restores both halves', async () => {
      const extraReset = vi.fn()
      const wrapper = mountUserCenter({
        profile: { extra: joinedExtra({ save: vi.fn(async () => undefined), reset: extraReset }) },
      })
      await flushPromises()

      await barButtons(wrapper)[0]!.trigger('click')
      await flushPromises()

      expect(extraReset).toHaveBeenCalledTimes(1)
    })

    it("folds the block's dirty() into the save bar's unsaved marker", async () => {
      // Reactive on purpose: the contract says `dirty()` is evaluated inside a
      // reactive effect, so a block backed by plain variables would never
      // refresh the marker.
      const blockDirty = ref(false)
      const wrapper = mountUserCenter({
        profile: {
          extra: joinedExtra({ save: vi.fn(async () => undefined), dirty: () => blockDirty.value }),
        },
      })
      await flushPromises()
      // Identity form untouched + block clean → nothing claimed.
      expect(wrapper.find('.t-uc-save-bar__dirty').exists()).toBe(false)

      // The block reports unsaved edits → the shared bar says so, even though
      // the framework's own fields never changed.
      blockDirty.value = true
      await flushPromises()
      expect(wrapper.find('.t-uc-save-bar__dirty').exists()).toBe(true)
    })

    it('unregistered block: behaviour is unchanged (framework never calls it)', async () => {
      const Plain = { name: 'ProfileExtraPlain', template: '<div class="probe-plain">PLAIN</div>' }
      const wrapper = mountUserCenter({ profile: { extra: Plain } })
      await flushPromises()

      // Save bar stays attached to the identity fields it governs, above the
      // self-contained block (which ships its own button).
      const html = wrapper.html()
      expect(html.indexOf('t-uc-save-bar')).toBeLessThan(html.indexOf('probe-plain'))

      await clickSave(wrapper)

      expect(me.updateProfile).toHaveBeenCalledTimes(1)
      expect(messageApi.success).toHaveBeenCalledTimes(1)
      // No dirty marker is introduced for pages without a joined block.
      expect(wrapper.find('.t-uc-save-bar__dirty').exists()).toBe(false)
    })
  })

  // ── Locking identity-core fields against self-service changes ──
  describe('profile.readonlyFields on identity-core fields', () => {
    /** The identity row carrying this placeholder. Scoped to the `.n-input`
     *  wrapper so "has a button" means exactly "the suffix `Change…` affordance
     *  is offered on THIS row" - the email and phone rows are otherwise
     *  identical (same label text on the button). */
    function identityRow(w: ReturnType<typeof mountUserCenter>, placeholder: string) {
      const row = w
        .findAll('.n-input')
        .find((el) => el.find(`input[placeholder="${placeholder}"]`).exists())
      expect(row, `no identity row rendered for placeholder ${placeholder}`).toBeTruthy()
      return row!
    }
    const EMAIL = 'your@email.com'
    const PHONE = '+1 555 0100'
    const offersChange = (w: ReturnType<typeof mountUserCenter>, placeholder: string) =>
      identityRow(w, placeholder).find('button').exists()

    it('offers both change flows by default (no config = unchanged behaviour)', async () => {
      const wrapper = mountUserCenter()
      await flushPromises()

      expect(offersChange(wrapper, EMAIL)).toBe(true)
      expect(offersChange(wrapper, PHONE)).toBe(true)
    })

    it("locking 'email' drops its Change button and leaves the phone flow alone", async () => {
      const wrapper = mountUserCenter({ profile: { readonlyFields: ['email'] } })
      await flushPromises()

      expect(offersChange(wrapper, EMAIL)).toBe(false)
      // Independent switches: an org that assigns login emails centrally still
      // wants staff to rebind their own mobile number.
      expect(offersChange(wrapper, PHONE)).toBe(true)
    })

    it("locking 'phone' drops its Change button and leaves the email flow alone", async () => {
      const wrapper = mountUserCenter({ profile: { readonlyFields: ['phone'] } })
      await flushPromises()

      expect(offersChange(wrapper, PHONE)).toBe(false)
      expect(offersChange(wrapper, EMAIL)).toBe(true)
    })

    it('still shows the value when locked (the user must know which address signs them in)', async () => {
      const wrapper = mountUserCenter({ profile: { readonlyFields: ['email', 'phone'] } })
      await flushPromises()

      expect(offersChange(wrapper, EMAIL)).toBe(false)
      expect(offersChange(wrapper, PHONE)).toBe(false)
      // The row is not hidden and not blanked - only the affordance is gone.
      expect(
        (identityRow(wrapper, EMAIL).find('input').element as HTMLInputElement).value,
      ).toBe('a@a.com')
    })

    it('is ANDed with the backend channel capability, not a substitute for it', async () => {
      // Deployment cannot do email at all → no change flow, with no readonlyFields
      // involved. Guards against collapsing the two gates into one: turning the
      // email channel off to stop rebinding would also kill login + recovery.
      getAuthConfig.mockResolvedValueOnce({
        allowEmailLogin: false,
        allowSmsLogin: true,
        oAuthProviders: [],
      })
      const wrapper = mountUserCenter()
      await flushPromises()

      expect(offersChange(wrapper, EMAIL)).toBe(false)
      expect(offersChange(wrapper, PHONE)).toBe(true)
    })
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
