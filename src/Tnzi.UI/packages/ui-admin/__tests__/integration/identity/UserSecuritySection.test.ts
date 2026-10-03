import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import type { TwoFactorStatusDto, UserSignInPolicyDto } from '@tnzi/core/services/identity'

/**
 * Sign-in security of one account, administered by someone else.
 *
 * Locks the three decisions the section makes on its own: the authenticator
 * row never offers "enable" (enrolment needs the holder's device), a role
 * exemption is said out loud instead of letting the list look enforced, and
 * the write surface disappears without `user.security`.
 *
 * The section is hostable (a consumer persona page mounts it with only a
 * `userId`), so the tests drive it exactly the way a host would: no translator
 * and no permission flag are passed in; strings come from the page translator
 * and the write grant from the auth store. Both halves are hostable panels
 * (`TTwoFactorPanel` in admin mode, `TSignInPolicyPanel`) that resolve their
 * own English strings from the bundled dictionary; only the section bar goes
 * through the (echoing) page translator.
 */
const bridge = {
  userSecurity: {
    getTwoFactorStatus: vi.fn<() => Promise<TwoFactorStatusDto>>(),
    suspendTwoFactor: vi.fn(async () => undefined),
    resumeTwoFactor: vi.fn(async () => undefined),
    enableTwoFactorMethod: vi.fn(async () => undefined),
    disableTwoFactorMethod: vi.fn(async () => undefined),
    setPreferredTwoFactor: vi.fn(async () => undefined),
    resetTwoFactor: vi.fn(async () => undefined),
    getSignInPolicy: vi.fn<() => Promise<UserSignInPolicyDto>>(),
    setIpAllowList: vi.fn<(id: string, data: unknown) => Promise<UserSignInPolicyDto>>(),
  },
}

vi.mock('../../../src/plugin/client', () => ({ useAdminClient: () => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }) }))
vi.mock('../../../src/services/bridges/identity-bridge', () => ({ createIdentityBridge: () => bridge }))
// Keys echo back so assertions name the string they expect, not its English.
vi.mock('../../../src/pages/_shared/translate', () => ({
  makePageTranslator: (ns: string) => (key: string, named?: Record<string, unknown>) => {
    if (ns !== 'identity.users') throw new Error(`unexpected page namespace ${ns}`)
    return named ? `${key}:${Object.values(named).join(',')}` : key
  },
}))

import UserSecuritySection from '../../../src/pages/identity/sections/UserSecuritySection.vue'
import * as pagesBarrel from '../../../src/pages/index'
import { useAdminAuthStore } from '../../../src/stores/useAdminAuthStore'

const status = (over: Partial<TwoFactorStatusDto> = {}): TwoFactorStatusDto => ({
  isEnabled: true,
  supportedTypes: ['Totp', 'Email'] as never,
  isTotpEnabled: true,
  preferredType: 'Totp' as never,
  methods: [
    { type: 'Totp' as never, available: true, enabled: true, isPreferred: true },
    { type: 'Sms' as never, available: true, enabled: false, isPreferred: false },
    { type: 'Email' as never, available: true, enabled: true, isPreferred: false },
  ],
  ...over,
})

const policy = (over: Partial<UserSignInPolicyDto> = {}): UserSignInPolicyDto => ({
  userId: 'u1',
  ipAllowListEnabled: false,
  allowedIps: null,
  entries: [],
  exemptedByRoles: [],
  callerIpAddress: '198.51.100.7',
  ...over,
})

/** What the signed-in operator holds. `user.view` alone is the read-only case. */
function signIn(permissions: string[]): void {
  useAdminAuthStore().setUserInfo({ id: 'op', username: 'operator', roles: [], permissions })
}

function mountSection(canManage = true) {
  signIn(canManage ? ['user.view', 'user.security'] : ['user.view'])
  return mount(UserSecuritySection, {
    props: { userId: 'u1' },
    global: {
      stubs: {
        TDetailSection: { template: '<section><slot name="actions" /><slot /></section>' },
        Spin: { template: '<div><slot /></div>' },
        Popconfirm: { template: '<span><slot name="trigger" /></span>' },
        Alert: { props: ['title'], template: '<div class="alert-stub" :data-title="title"><slot /></div>' },
        Input: { props: ['value'], emits: ['update:value'], template: '<textarea class="ips" :value="value" @input="$emit(\'update:value\', $event.target.value)" />' },
        Switch: { props: ['value'], emits: ['update:value'], template: '<input type="checkbox" class="switch" :checked="value" @change="$emit(\'update:value\', $event.target.checked)" />' },
      },
    },
  })
}

describe('UserSecuritySection', () => {
  it('is on the public surface so a consumer persona page can host it', () => {
    expect(pagesBarrel.UserSecuritySection).toBe(UserSecuritySection)
  })

  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    bridge.userSecurity.getTwoFactorStatus.mockResolvedValue(status())
    bridge.userSecurity.getSignInPolicy.mockResolvedValue(policy())
  })

  it('loads both halves for the user and lists the methods the backend reported', async () => {
    const wrapper = mountSection()
    await flushPromises()

    expect(bridge.userSecurity.getTwoFactorStatus).toHaveBeenCalledWith('u1')
    expect(bridge.userSecurity.getSignInPolicy).toHaveBeenCalledWith('u1')
    const text = wrapper.text()
    expect(text).toContain('Authenticator app (TOTP)')
    expect(text).toContain('Text message (SMS)')
    expect(text).toContain('Email code')
    expect(wrapper.find('.t-2fa .t-detail-block__title .n-tag').text()).toBe('On')
  })

  it('never offers to enable the authenticator, only code-based methods', async () => {
    bridge.userSecurity.getTwoFactorStatus.mockResolvedValue(status({
      isEnabled: false,
      isTotpEnabled: false,
      preferredType: null,
      methods: [
        { type: 'Totp' as never, available: true, enabled: false, isPreferred: false },
        { type: 'Sms' as never, available: true, enabled: false, isPreferred: false },
      ],
    }))
    const wrapper = mountSection()
    await flushPromises()

    const enableButtons = wrapper.findAll('button').filter((b) => b.text() === 'Enable')
    // One row can be enabled here (SMS); the authenticator row has no enable button at all.
    expect(enableButtons).toHaveLength(1)
    await enableButtons[0].trigger('click')
    await flushPromises()
    expect(bridge.userSecurity.enableTwoFactorMethod).toHaveBeenCalledWith('u1', 'Sms')
    expect(bridge.userSecurity.enableTwoFactorMethod).not.toHaveBeenCalledWith('u1', 'Totp')
  })

  it('says out loud when a role exemption makes the allow-list inert', async () => {
    bridge.userSecurity.getSignInPolicy.mockResolvedValue(policy({
      ipAllowListEnabled: true,
      allowedIps: '10.0.0.0/8',
      entries: ['10.0.0.0/8'],
      exemptedByRoles: ['SuperAdmin'],
    }))
    const wrapper = mountSection()
    await flushPromises()

    const alert = wrapper.find('.alert-stub')
    expect(alert.exists()).toBe(true)
    expect(alert.text()).toContain('(SuperAdmin)')
    expect(alert.text()).toContain('stored but not enforced')
  })

  it('saves the allow-list verbatim and sends null for a cleared list', async () => {
    bridge.userSecurity.setIpAllowList.mockImplementation(async (_id, data) => {
      const d = data as { enabled: boolean; allowedIps: string | null }
      return policy({ ipAllowListEnabled: d.enabled, allowedIps: d.allowedIps })
    })
    const wrapper = mountSection()
    await flushPromises()

    await wrapper.find('.switch').setValue(true)
    await wrapper.find('.ips').setValue('# office\n203.0.113.5\n10.0.0.0/8')
    const save = wrapper.findAll('button').find((b) => b.text() === 'Save')!
    await save.trigger('click')
    await flushPromises()

    expect(bridge.userSecurity.setIpAllowList).toHaveBeenCalledWith('u1', {
      enabled: true,
      allowedIps: '# office\n203.0.113.5\n10.0.0.0/8',
    })

    await wrapper.find('.switch').setValue(false)
    await wrapper.find('.ips').setValue('   ')
    await save.trigger('click')
    await flushPromises()
    expect(bridge.userSecurity.setIpAllowList).toHaveBeenLastCalledWith('u1', { enabled: false, allowedIps: null })
  })

  it('renders read-only without the user.security grant', async () => {
    const wrapper = mountSection(false)
    await flushPromises()

    const labels = wrapper.findAll('button').map((b) => b.text())
    expect(labels).not.toContain('Save')
    expect(labels).not.toContain('detail.security.twoFactor.suspend')
    expect(labels).not.toContain('detail.security.twoFactor.disable')
    expect(wrapper.find('.ips').attributes('readonly')).toBeDefined()
  })
})
