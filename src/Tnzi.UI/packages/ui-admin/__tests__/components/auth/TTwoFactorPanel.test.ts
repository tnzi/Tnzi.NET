import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import type { TwoFactorStatusDto } from '@tnzi/core/services/identity'

/**
 * `TTwoFactorPanel` - one list of methods, two subjects.
 *
 * What is locked here is the split the `mode` prop makes, because the list
 * looks the same on both sides and only the calls behind it differ:
 *
 *   - `self` writes through `me.*` and offers authenticator enrolment; it never
 *     needs a grant, and "Turn off" suspends (methods kept) rather than wiping.
 *   - `admin` writes through `userSecurity.*` with the userId, never offers to
 *     enrol an authenticator, adds "Reset", and goes read-only without
 *     `user.security`. A missing userId renders nothing and calls nothing - it
 *     must never quietly fall back to the caller's own account.
 *
 * The rows come straight from the backend's `methods` list, so a channel the
 * deployment switched off is simply absent.
 */
const me = {
  getTwoFactorStatus: vi.fn<() => Promise<TwoFactorStatusDto>>(),
  enableTwoFactor: vi.fn(async () => ''),
  disableTwoFactor: vi.fn(async () => undefined),
  suspendTwoFactor: vi.fn(async () => undefined),
  resumeTwoFactor: vi.fn(async () => undefined),
  disableTwoFactorMethod: vi.fn(async () => undefined),
  setPreferredTwoFactor: vi.fn(async () => undefined),
  getTotpSetup: vi.fn(async () => ({ sharedKey: 'SECRET', authenticatorUri: 'otpauth://totp/x' })),
  enableTotp: vi.fn(async () => undefined),
  disableTotp: vi.fn(async () => undefined),
  registerPasskey: vi.fn(async () => ({ credentialId: 'c1', name: 'YubiKey', createdAt: '2026-09-21T00:00:00Z', isBackedUp: false })),
  getPasskeys: vi.fn(async () => [
    { credentialId: 'c1', name: 'YubiKey 5C', createdAt: '2026-09-21T00:00:00Z', isBackedUp: false },
    { credentialId: 'c2', name: null, createdAt: '2026-09-20T00:00:00Z', isBackedUp: true },
  ]),
  removePasskey: vi.fn(async () => undefined),
}

/** happy-dom has no WebAuthn; the capability check reads these two bridges. */
function setPasskeySupport(on: boolean): void {
  const w = window as unknown as Record<string, unknown>
  if (on) w.PublicKeyCredential = { parseCreationOptionsFromJSON: () => ({}), parseRequestOptionsFromJSON: () => ({}) }
  else delete w.PublicKeyCredential
}
const userSecurity = {
  getTwoFactorStatus: vi.fn<(id: string) => Promise<TwoFactorStatusDto>>(),
  suspendTwoFactor: vi.fn(async () => undefined),
  resumeTwoFactor: vi.fn(async () => undefined),
  enableTwoFactorMethod: vi.fn(async () => undefined),
  disableTwoFactorMethod: vi.fn(async () => undefined),
  setPreferredTwoFactor: vi.fn(async () => undefined),
  resetTwoFactor: vi.fn(async () => undefined),
}
const bridge = { me, userSecurity }
/** Every `createIdentityBridge` call's deps, to see whether a step-up verifier was wired. */
const bridgeDeps: unknown[] = []

const messageApi = { success: vi.fn(), error: vi.fn(), warning: vi.fn(), info: vi.fn(), loading: vi.fn(), create: vi.fn(), destroyAll: vi.fn() }

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../../src/services/bridges/identity-bridge', () => ({
  createIdentityBridge: (deps: unknown) => {
    bridgeDeps.push(deps)
    return bridge
  },
}))
vi.mock('@tnzi/ui', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@tnzi/ui')>()),
  useSafeMessage: () => messageApi,
}))

import TTwoFactorPanel from '../../../src/components/auth/TTwoFactorPanel.vue'
import * as root from '../../../src/index'
import { useAdminAuthStore } from '../../../src/stores/useAdminAuthStore'

const status = (over: Partial<TwoFactorStatusDto> = {}): TwoFactorStatusDto => ({
  isEnabled: true,
  supportedTypes: ['Totp', 'Sms', 'Email'] as never,
  isTotpEnabled: true,
  preferredType: 'Totp' as never,
  methods: [
    { type: 'Totp' as never, available: true, enabled: true, isPreferred: true },
    { type: 'Sms' as never, available: true, enabled: false, isPreferred: false },
    { type: 'Email' as never, available: true, enabled: true, isPreferred: false },
  ],
  ...over,
})

/** Nothing on: every row offers its first step. */
const nothingSetUp = (): TwoFactorStatusDto =>
  status({
    isEnabled: false,
    isTotpEnabled: false,
    preferredType: null,
    methods: [
      { type: 'Totp' as never, available: true, enabled: false, isPreferred: false },
      { type: 'Sms' as never, available: true, enabled: false, isPreferred: false },
      { type: 'Email' as never, available: true, enabled: false, isPreferred: false },
    ],
  })

/** Turned off with the methods kept: `isEnabled` false while rows stay enabled. */
const suspended = (): TwoFactorStatusDto =>
  status({
    isEnabled: false,
    methods: [{ type: 'Totp' as never, available: true, enabled: true, isPreferred: true }],
  })

function signIn(permissions: string[]): void {
  useAdminAuthStore().setUserInfo({ id: 'op', username: 'operator', roles: [], permissions })
}

/**
 * Popconfirm renders its trigger inline and a `.confirm` button that stands in
 * for the user pressing "yes", so a guarded action can be driven without the
 * popover. `TStepUpModal` is stubbed to a marker so the test can see whether the
 * panel mounted a prompt of its own.
 */
function mountPanel(props: Record<string, unknown>) {
  return mount(TTwoFactorPanel, {
    props,
    global: {
      stubs: {
        Popconfirm: {
          emits: ['positive-click'],
          template: '<span class="pc"><slot name="trigger" /><button class="confirm" @click="$emit(\'positive-click\')" /></span>',
        },
        Spin: { template: '<div><slot /></div>' },
        TStepUpModal: { template: '<div class="own-step-up" />' },
        TTotpSetupModal: {
          props: ['show', 'fetchSetup', 'confirmCode'],
          emits: ['enabled', 'update:show'],
          template: '<div class="totp-setup" :data-show="String(show)"><button class="totp-done" @click="$emit(\'enabled\')" /></div>',
        },
      },
    },
  })
}

const buttons = (w: ReturnType<typeof mountPanel>) => w.findAll('button').map((b) => b.text()).filter(Boolean)
const rowOf = (w: ReturnType<typeof mountPanel>, title: string) =>
  w.findAll('.t-item-card').find((c) => c.find('.t-item-card__title').text() === title)!
/** The row's own action (Set up / Enable / Disable / Set as preferred), not a button inside the key list. */
const actionOf = (row: ReturnType<typeof rowOf>) => row.find('.t-item-card__ops button')
/** Press "yes" on the popconfirm whose trigger reads `label`. */
async function confirmAction(w: ReturnType<typeof mountPanel>, label: string): Promise<void> {
  const pc = w.findAll('.pc').find((p) => p.find('button').text() === label)!
  await pc.find('.confirm').trigger('click')
  await flushPromises()
}

describe('TTwoFactorPanel', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    bridgeDeps.length = 0
    me.getTwoFactorStatus.mockResolvedValue(status())
    userSecurity.getTwoFactorStatus.mockResolvedValue(status())
  })

  it('is on the package root so a consumer account page can host it', () => {
    expect(root.TTwoFactorPanel).toBe(TTwoFactorPanel)
  })

  describe('self mode', () => {
    it('lists the methods the backend reported with the state tag, through me.*', async () => {
      const w = mountPanel({ mode: 'self', bridge })
      await flushPromises()

      expect(me.getTwoFactorStatus).toHaveBeenCalledTimes(1)
      expect(userSecurity.getTwoFactorStatus).not.toHaveBeenCalled()
      const text = w.text()
      expect(text).toContain('Authenticator app (TOTP)')
      expect(text).toContain('Text message (SMS)')
      expect(text).toContain('Email code')
      expect(rowOf(w, 'Authenticator app (TOTP)').text()).toContain('Preferred')
      // The state tag reads the master flag, not the rows.
      expect(w.find('.t-2fa .t-detail-block__title .n-tag').text()).toBe('On')
    })

    it('offers to set up the authenticator here and enables code methods directly', async () => {
      me.getTwoFactorStatus.mockResolvedValue(nothingSetUp())
      const w = mountPanel({ mode: 'self', bridge })
      await flushPromises()

      expect(w.find('.t-2fa .t-detail-block__title .n-tag').text()).toBe('Not set up')
      // Nothing to turn off or back on yet: no header action at all.
      expect(w.find('.t-detail-block__actions').exists()).toBe(false)

      const totp = rowOf(w, 'Authenticator app (TOTP)')
      expect(totp.find('button').text()).toBe('Set up')
      // The QR modal is not mounted until asked for - it is the heavy piece.
      expect(w.find('.totp-setup').exists()).toBe(false)
      await totp.find('button').trigger('click')
      await flushPromises()
      expect(w.find('.totp-setup').attributes('data-show')).toBe('true')

      await rowOf(w, 'Text message (SMS)').find('button').trigger('click')
      await flushPromises()
      expect(me.enableTwoFactor).toHaveBeenCalledWith({ type: 'Sms' })
      expect(messageApi.success).toHaveBeenCalled()
    })

    it('announces an enrolled authenticator once, reloads and reports it', async () => {
      me.getTwoFactorStatus.mockResolvedValue(nothingSetUp())
      const w = mountPanel({ mode: 'self', bridge })
      await flushPromises()
      await rowOf(w, 'Authenticator app (TOTP)').find('button').trigger('click')
      await flushPromises()

      me.getTwoFactorStatus.mockResolvedValue(status())
      await w.find('.totp-done').trigger('click')
      await flushPromises()

      expect(messageApi.success).toHaveBeenCalledTimes(1)
      expect(me.getTwoFactorStatus).toHaveBeenCalledTimes(2)
      expect(w.emitted('updated')).toHaveLength(1)
      expect(w.find('.t-2fa .t-detail-block__title .n-tag').text()).toBe('On')
    })

    it('turns off by suspending, never by the destructive disable, and turns back on by resuming', async () => {
      const w = mountPanel({ mode: 'self', bridge })
      await flushPromises()

      me.getTwoFactorStatus.mockResolvedValue(suspended())
      await confirmAction(w, 'Turn off')
      expect(me.suspendTwoFactor).toHaveBeenCalledTimes(1)
      expect(me.disableTwoFactor).not.toHaveBeenCalled()
      expect(w.find('.t-2fa .t-detail-block__title .n-tag').text()).toBe('Off (methods kept)')

      const resume = w.findAll('button').find((b) => b.text() === 'Turn back on')!
      await resume.trigger('click')
      await flushPromises()
      expect(me.resumeTwoFactor).toHaveBeenCalledTimes(1)
    })

    it('has no reset and needs no grant', async () => {
      signIn(['nothing.at.all'])
      const w = mountPanel({ mode: 'self', bridge })
      await flushPromises()

      const labels = buttons(w)
      expect(labels).not.toContain('Reset')
      expect(labels).toContain('Turn off')
      expect(labels).toContain('Disable')
    })

    it('disables a method through the per-method call and stars the preferred one', async () => {
      const w = mountPanel({ mode: 'self', bridge })
      await flushPromises()

      const email = rowOf(w, 'Email code')
      await email.findAll('button').find((b) => b.text() === 'Set as preferred')!.trigger('click')
      await flushPromises()
      expect(me.setPreferredTwoFactor).toHaveBeenCalledWith('Email')

      await email.find('.pc .confirm').trigger('click')
      await flushPromises()
      expect(me.disableTwoFactorMethod).toHaveBeenCalledWith('Email')
    })

    it('reports every write through `updated` with the reloaded status', async () => {
      const w = mountPanel({ mode: 'self', bridge })
      await flushPromises()
      me.getTwoFactorStatus.mockResolvedValue(suspended())

      await confirmAction(w, 'Turn off')

      const emitted = w.emitted('updated')!
      expect(emitted).toHaveLength(1)
      expect((emitted[0][0] as TwoFactorStatusDto).isEnabled).toBe(false)
    })

    /**
     * ★ The writes the backend marks [RequireStepUp] only work through a bridge
     * that carries a verifier. A host bridge brings its own prompt; without one
     * the panel must build the bridge with a verifier AND render the modal that
     * answers it, or every guarded write dead-ends in a 401 toast.
     */
    it('builds its own bridge with a step-up prompt when none is passed, and only then', async () => {
      const own = mountPanel({ mode: 'self' })
      await flushPromises()
      expect(bridgeDeps).toHaveLength(1)
      expect(typeof (bridgeDeps[0] as { stepUp?: unknown }).stepUp).toBe('function')
      expect(own.find('.own-step-up').exists()).toBe(true)

      bridgeDeps.length = 0
      const hosted = mountPanel({ mode: 'self', bridge })
      await flushPromises()
      expect(bridgeDeps).toHaveLength(0)
      expect(hosted.find('.own-step-up').exists()).toBe(false)
    })
  })

  describe('admin mode', () => {
    it('reads and writes through userSecurity.* with the userId', async () => {
      signIn(['user.view', 'user.security'])
      const w = mountPanel({ mode: 'admin', userId: 'u1', bridge })
      await flushPromises()

      expect(userSecurity.getTwoFactorStatus).toHaveBeenCalledWith('u1')
      expect(me.getTwoFactorStatus).not.toHaveBeenCalled()

      await rowOf(w, 'Text message (SMS)').find('button').trigger('click')
      await flushPromises()
      expect(userSecurity.enableTwoFactorMethod).toHaveBeenCalledWith('u1', 'Sms')

      await confirmAction(w, 'Turn off')
      expect(userSecurity.suspendTwoFactor).toHaveBeenCalledWith('u1')

      await confirmAction(w, 'Reset')
      expect(userSecurity.resetTwoFactor).toHaveBeenCalledWith('u1')
    })

    it('never offers to enrol the authenticator for someone else', async () => {
      signIn(['user.view', 'user.security'])
      userSecurity.getTwoFactorStatus.mockResolvedValue(nothingSetUp())
      const w = mountPanel({ mode: 'admin', userId: 'u1', bridge })
      await flushPromises()

      expect(rowOf(w, 'Authenticator app (TOTP)').findAll('button')).toHaveLength(0)
      expect(buttons(w)).not.toContain('Set up')
      // The code methods still can be.
      expect(rowOf(w, 'Text message (SMS)').find('button').text()).toBe('Enable')
    })

    it('renders read-only without user.security', async () => {
      signIn(['user.view'])
      const w = mountPanel({ mode: 'admin', userId: 'u1', bridge })
      await flushPromises()

      expect(w.text()).toContain('Authenticator app (TOTP)')
      expect(buttons(w)).toEqual([])
    })

    it('shows the unavailable state and calls nothing without a userId - never the caller\'s own account', async () => {
      signIn(['user.view', 'user.security'])
      const w = mountPanel({ mode: 'admin', bridge })
      await flushPromises()

      expect(userSecurity.getTwoFactorStatus).not.toHaveBeenCalled()
      expect(me.getTwoFactorStatus).not.toHaveBeenCalled()
      expect(w.text()).toContain('Two-factor status is not available for this account.')
    })

    it('follows a userId change', async () => {
      signIn(['user.view'])
      const w = mountPanel({ mode: 'admin', userId: 'u1', bridge })
      await flushPromises()
      await w.setProps({ userId: 'u2' })
      await flushPromises()
      expect(userSecurity.getTwoFactorStatus).toHaveBeenLastCalledWith('u2')
    })

    it('mounts no step-up prompt of its own: the admin endpoints carry none', async () => {
      signIn(['user.view'])
      const w = mountPanel({ mode: 'admin', userId: 'u1' })
      await flushPromises()
      expect((bridgeDeps[0] as { stepUp?: unknown }).stepUp).toBeUndefined()
      expect(w.find('.own-step-up').exists()).toBe(false)
    })
  })

  /**
   * The fourth method. A registered key is enabled with one click on either
   * side (the credential itself lives under Passkeys); without one the row
   * says where to go first; a deployment that has not turned the channel on
   * has no row at all, exactly like the other channels.
   */
  it('lists the security key as a method, enabled over a registered credential on both sides', async () => {
    const withKey = status({
      isEnabled: false,
      isTotpEnabled: false,
      preferredType: null,
      methods: [
        { type: 'Passkey' as never, available: true, enabled: false, isPreferred: false },
        { type: 'Email' as never, available: true, enabled: false, isPreferred: false },
      ],
    })
    me.getTwoFactorStatus.mockResolvedValue(withKey)
    const self = mountPanel({ mode: 'self', bridge })
    await flushPromises()
    me.getPasskeys.mockResolvedValue([{ credentialId: 'c1', name: 'YubiKey 5C', createdAt: '2026-09-21T00:00:00Z', isBackedUp: false }])
    const row = rowOf(self, 'Security key / passkey')
    expect(row.text()).toContain('A YubiKey or other FIDO2 key')
    expect(actionOf(row).text()).toBe('Enable')
    await actionOf(row).trigger('click')
    await flushPromises()
    expect(me.enableTwoFactor).toHaveBeenCalledWith({ type: 'Passkey' })
    // No enrolment dialog: the key was registered under Passkeys, this is a switch.
    expect(self.find('.totp-setup').exists()).toBe(false)

    signIn(['user.view', 'user.security'])
    userSecurity.getTwoFactorStatus.mockResolvedValue(withKey)
    const admin = mountPanel({ mode: 'admin', userId: 'u1', bridge })
    await flushPromises()
    await rowOf(admin, 'Security key / passkey').find('button').trigger('click')
    await flushPromises()
    expect(userSecurity.enableTwoFactorMethod).toHaveBeenCalledWith('u1', 'Passkey')
  })

  /**
   * ★ No key registered is one click away on the holder's own device, exactly
   * like the authenticator: "Set up" registers the key and turns the method
   * on. The user never has to find a second block first.
   */
  describe('security key set-up from the row (self, nothing registered yet)', () => {
    const noKey = () =>
      status({
        isEnabled: false,
        isTotpEnabled: false,
        preferredType: null,
        methods: [{ type: 'Passkey' as never, available: false, enabled: false, isPreferred: false, requiresAddress: true }],
      })

    beforeEach(() => me.getPasskeys.mockResolvedValue([]))
    afterEach(() => setPasskeySupport(false))

    it('registers the key, enables the method, announces once and reports the change', async () => {
      setPasskeySupport(true)
      me.getTwoFactorStatus.mockResolvedValue(noKey())
      const w = mountPanel({ mode: 'self', bridge })
      await flushPromises()

      const row = rowOf(w, 'Security key / passkey')
      expect(row.text()).toContain('Available')
      expect(row.text()).not.toContain('Needs a registered passkey')
      expect(actionOf(row).text()).toBe('Set up')

      me.getTwoFactorStatus.mockResolvedValue(status())
      await actionOf(row).trigger('click')
      await flushPromises()

      expect(me.registerPasskey).toHaveBeenCalledTimes(1)
      expect(me.enableTwoFactor).toHaveBeenCalledWith({ type: 'Passkey' })
      expect(messageApi.success).toHaveBeenCalledTimes(1)
      expect(w.emitted('updated')).toHaveLength(1)
    })

    it('treats a closed system dialog as nothing happened: no enable, no toast, no reload', async () => {
      setPasskeySupport(true)
      me.getTwoFactorStatus.mockResolvedValue(noKey())
      me.registerPasskey.mockResolvedValueOnce(null as never)
      const w = mountPanel({ mode: 'self', bridge })
      await flushPromises()

      await actionOf(rowOf(w, 'Security key / passkey')).trigger('click')
      await flushPromises()

      expect(me.enableTwoFactor).not.toHaveBeenCalled()
      expect(messageApi.success).not.toHaveBeenCalled()
      expect(messageApi.error).not.toHaveBeenCalled()
      expect(me.getTwoFactorStatus).toHaveBeenCalledTimes(1)
      expect(w.emitted('updated')).toBeUndefined()
    })

    it('says where to go instead when this browser cannot register one', async () => {
      setPasskeySupport(false)
      me.getTwoFactorStatus.mockResolvedValue(noKey())
      const w = mountPanel({ mode: 'self', bridge })
      await flushPromises()

      const row = rowOf(w, 'Security key / passkey')
      expect(row.text()).toContain('Needs a registered passkey')
      expect(row.text()).toContain('This browser cannot register a passkey.')
      expect(row.findAll('button')).toHaveLength(0)
    })

    it('never registers a key for someone else: the admin row only says the account has none', async () => {
      setPasskeySupport(true)
      signIn(['user.view', 'user.security'])
      userSecurity.getTwoFactorStatus.mockResolvedValue(noKey())
      const w = mountPanel({ mode: 'admin', userId: 'u1', bridge })
      await flushPromises()

      const row = rowOf(w, 'Security key / passkey')
      expect(row.text()).toContain('The account has no passkey or security key registered yet.')
      expect(row.findAll('button')).toHaveLength(0)
      expect(me.registerPasskey).not.toHaveBeenCalled()
    })
  })

  /**
   * ★ Everything about security keys lives in the row: the registered keys are
   * listed inside it (name, date, synced, remove, add another), and the list
   * is only fetched when the row exists - listing is refused when the wiring
   * is off, and no endpoint lists another account's keys at all.
   */
  describe('the key inventory inside the row', () => {
    const withKeyRow = () =>
      status({
        methods: [
          { type: 'Passkey' as never, available: true, enabled: true, isPreferred: true },
          { type: 'Email' as never, available: true, enabled: false, isPreferred: false },
        ],
        preferredType: 'Passkey' as never,
      })

    // `clearAllMocks` keeps resolved values; the set-up cases above emptied the list.
    beforeEach(() =>
      me.getPasskeys.mockResolvedValue([
        { credentialId: 'c1', name: 'YubiKey 5C', createdAt: '2026-09-21T00:00:00Z', isBackedUp: false },
        { credentialId: 'c2', name: null, createdAt: '2026-09-20T00:00:00Z', isBackedUp: true },
      ]),
    )
    afterEach(() => setPasskeySupport(false))

    it('lists the holder\'s keys in the row and removes one through the self endpoints', async () => {
      setPasskeySupport(true)
      me.getTwoFactorStatus.mockResolvedValue(withKeyRow())
      const w = mountPanel({ mode: 'self', bridge })
      await flushPromises()

      const row = rowOf(w, 'Security key / passkey')
      expect(me.getPasskeys).toHaveBeenCalledTimes(1)
      expect(row.text()).toContain('YubiKey 5C')
      expect(row.text()).toContain('Unnamed key')
      expect(row.text()).toContain('Synced')
      expect(row.findAll('.t-2fa__key')).toHaveLength(2)
      expect(w.emitted('loaded')).toHaveLength(1)

      await row.findAll('.pc').find((p) => p.find('button').attributes('aria-label') === 'Remove')!.find('.confirm').trigger('click')
      await flushPromises()
      expect(me.removePasskey).toHaveBeenCalledWith('c1')
      // The list and the status both follow: the backend may have turned the method off.
      expect(me.getTwoFactorStatus).toHaveBeenCalledTimes(2)
      expect(me.getPasskeys).toHaveBeenCalledTimes(2)
      expect(messageApi.success).toHaveBeenCalledTimes(1)
    })

    it('adds another key from the row without touching the method switch', async () => {
      setPasskeySupport(true)
      me.getTwoFactorStatus.mockResolvedValue(withKeyRow())
      const w = mountPanel({ mode: 'self', bridge })
      await flushPromises()

      await rowOf(w, 'Security key / passkey').findAll('button').find((b) => b.text() === 'Add key')!.trigger('click')
      await flushPromises()

      expect(me.registerPasskey).toHaveBeenCalledTimes(1)
      expect(me.enableTwoFactor).not.toHaveBeenCalled()
      expect(messageApi.success).toHaveBeenCalledTimes(1)
    })

    it('offers no "add another" on a browser that cannot run the ceremony, but still lists and removes', async () => {
      setPasskeySupport(false)
      me.getTwoFactorStatus.mockResolvedValue(withKeyRow())
      const w = mountPanel({ mode: 'self', bridge })
      await flushPromises()

      const row = rowOf(w, 'Security key / passkey')
      expect(row.text()).toContain('YubiKey 5C')
      expect(row.findAll('button').map((b) => b.text())).not.toContain('Add key')
      expect(row.findAll('button[aria-label="Remove"]')).toHaveLength(2)
    })

    it('fetches no keys when the row is absent, and never on the admin side', async () => {
      me.getTwoFactorStatus.mockResolvedValue(status())
      mountPanel({ mode: 'self', bridge })
      await flushPromises()
      expect(me.getPasskeys).not.toHaveBeenCalled()

      signIn(['user.view', 'user.security'])
      userSecurity.getTwoFactorStatus.mockResolvedValue(withKeyRow())
      const admin = mountPanel({ mode: 'admin', userId: 'u1', bridge })
      await flushPromises()
      expect(me.getPasskeys).not.toHaveBeenCalled()
      expect(rowOf(admin, 'Security key / passkey').findAll('.t-2fa__key')).toHaveLength(0)
    })
  })

  it('skips a channel the deployment switched off and marks an unverified address', async () => {
    me.getTwoFactorStatus.mockResolvedValue(
      status({
        isEnabled: false,
        isTotpEnabled: false,
        preferredType: null,
        methods: [
          { type: 'Sms' as never, available: false, enabled: false, isPreferred: false, requiresAddress: true },
          { type: 'Email' as never, available: true, enabled: false, isPreferred: false },
        ],
      }),
    )
    const w = mountPanel({ mode: 'self', bridge })
    await flushPromises()

    expect(w.text()).not.toContain('Authenticator app (TOTP)')
    const sms = rowOf(w, 'Text message (SMS)')
    expect(sms.text()).toContain('Needs a verified address')
    expect(sms.text()).toContain('Add and verify a phone number')
    // Nothing to press until the address is verified.
    expect(sms.findAll('button')).toHaveLength(0)
    expect(rowOf(w, 'Email code').find('button').text()).toBe('Enable')
  })

  it('surfaces a failed status load as the empty state plus the reason', async () => {
    me.getTwoFactorStatus.mockRejectedValue(new Error('Two-factor service is not available'))
    const w = mountPanel({ mode: 'self', bridge })
    await flushPromises()

    expect(messageApi.error).toHaveBeenCalledWith('Two-factor service is not available')
    expect(w.text()).toContain('Your two-factor status could not be loaded.')
  })
})

describe('TTwoFactorPanel - switching accounts', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    signIn(['user.security'])
  })

  /**
   * An operator moving from A to B while A's status is still on its way must
   * see B's methods, not whichever response happened to arrive last - every
   * button on the rows acts on the current `userId`.
   */
  it("never paints the previous account's late response under the new one", async () => {
    let resolveA: (s: TwoFactorStatusDto) => void = () => {}
    userSecurity.getTwoFactorStatus.mockImplementation((id: string) =>
      id === 'user-a'
        ? new Promise<TwoFactorStatusDto>((resolve) => { resolveA = resolve })
        : Promise.resolve(status({ methods: [{ type: 'Sms' as never, available: true, enabled: true, isPreferred: true }] })),
    )
    const w = mountPanel({ mode: 'admin', userId: 'user-a', bridge })
    await flushPromises()

    await w.setProps({ userId: 'user-b' })
    await flushPromises()
    resolveA(status({ methods: [{ type: 'Email' as never, available: true, enabled: true, isPreferred: true }] }))
    await flushPromises()

    expect(w.text()).toContain('Text message (SMS)')
    expect(w.text()).not.toContain('Email code')
    // Only B's load reports: a host keyed on `loaded` must not see A's status.
    expect(w.emitted('loaded')).toHaveLength(1)
  })
})
