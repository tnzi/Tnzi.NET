import { describe, it, expect } from 'vitest'
import type { Component } from 'vue'
import {
  resolveUserCenterSections,
  type UserCenterBuiltInDef,
} from '../../../src/pages/account/resolveSections'
import { deriveCapabilities } from '../../../src/pages/account/userCenterContext'
import type { AdminUserCenterConfig } from '../../../src/plugin/userCenterConfig'
import type { AuthConfigDto } from '@tnzi/core/services/identity'

// Placeholder component objects - the resolver only threads them through.
const C = (name: string): Component => ({ name }) as Component

const BUILTINS: UserCenterBuiltInDef[] = [
  { key: 'profile', component: C('profile'), group: 'account', order: 10, icon: 'i', labelKey: 'nav.profile' },
  { key: 'security', component: C('security'), group: 'account', order: 20, icon: 'i', labelKey: 'nav.security' },
  { key: 'sessions', component: C('sessions'), group: 'activity', order: 30, icon: 'i', labelKey: 'nav.sessions' },
  { key: 'history', component: C('history'), group: 'activity', order: 40, icon: 'i', labelKey: 'nav.history' },
  { key: 'linked', component: C('linked'), group: 'advanced', order: 50, icon: 'i', labelKey: 'nav.linked' },
  { key: 'danger', component: C('danger'), group: 'advanced', order: 60, icon: 'i', labelKey: 'nav.danger' },
]

// Default gates: translate = passthrough of last segment, everything allowed.
const deps = {
  t: (k: string) => k,
  can: () => true,
  hasModule: () => true,
  groupLabel: (g: string) => `G:${g}`,
}

function resolve(config: AdminUserCenterConfig, over: Partial<typeof deps> = {}) {
  return resolveUserCenterSections(BUILTINS, config, { ...deps, ...over })
}

describe('resolveUserCenterSections', () => {
  it('renders the six built-ins in group order by default', () => {
    const out = resolve({})
    expect(out.map((s) => s.key)).toEqual(['profile', 'security', 'sessions', 'history', 'linked', 'danger'])
    expect(out.map((s) => s.groupKey)).toEqual([
      'account',
      'account',
      'activity',
      'activity',
      'advanced',
      'advanced',
    ])
    // group label runs through groupLabel(); label through t(labelKey).
    expect(out[0]?.group).toBe('G:account')
    expect(out[0]?.label).toBe('nav.profile')
  })

  it('hides a built-in section via hideSections', () => {
    const out = resolve({ hideSections: ['danger'] })
    expect(out.map((s) => s.key)).not.toContain('danger')
    expect(out).toHaveLength(5)
  })

  it('reassigns a built-in to a custom group via sectionGroups', () => {
    const out = resolve({ sectionGroups: { linked: 'Connections' } })
    const linked = out.find((s) => s.key === 'linked')
    expect(linked?.groupKey).toBe('Connections')
    expect(linked?.group).toBe('G:Connections')
  })

  it('hides a whole group via hideGroups (after regrouping)', () => {
    // danger stays in advanced; linked moves to account → hideGroups: advanced
    // drops only danger, keeps the relocated linked.
    const out = resolve({ sectionGroups: { linked: 'account' }, hideGroups: ['advanced'] })
    expect(out.map((s) => s.key)).toContain('linked')
    expect(out.map((s) => s.key)).not.toContain('danger')
  })

  it('overrides a built-in section component (nav entry preserved)', () => {
    const Custom = C('custom-security')
    const out = resolve({ overrides: { security: Custom } })
    const security = out.find((s) => s.key === 'security')
    expect(security?.component).toBe(Custom)
    // label/icon/group unchanged.
    expect(security?.label).toBe('nav.security')
    expect(security?.groupKey).toBe('account')
  })

  it('appends a custom section under a custom group', () => {
    const Billing = C('billing')
    const out = resolve({
      sections: [{ key: 'billing', label: 'Billing', icon: 'mdi:cc', group: 'Connections', order: 25, component: Billing }],
    })
    const billing = out.find((s) => s.key === 'custom:billing')
    expect(billing).toBeDefined()
    expect(billing?.component).toBe(Billing)
    expect(billing?.groupKey).toBe('Connections')
    // order 25 lands between security (20) and sessions (30).
    const keys = out.map((s) => s.key)
    expect(keys.indexOf('custom:billing')).toBeGreaterThan(keys.indexOf('security'))
    expect(keys.indexOf('custom:billing')).toBeLessThan(keys.indexOf('sessions'))
  })

  it('excludes a custom section when its permission is not held', () => {
    const out = resolve(
      { sections: [{ key: 'admin-only', label: 'X', permission: 'x.view', component: C('x') }] },
      { can: (p) => p !== 'x.view' },
    )
    expect(out.map((s) => s.key)).not.toContain('custom:admin-only')
  })

  it('excludes a custom section when its module is unavailable', () => {
    const out = resolve(
      { sections: [{ key: 'billing', label: 'X', module: 'payment', component: C('x') }] },
      { hasModule: (m) => m !== 'payment' },
    )
    expect(out.map((s) => s.key)).not.toContain('custom:billing')
  })

  it('reorders built-ins via sectionOrder', () => {
    const out = resolve({ sectionOrder: { danger: 5 } })
    expect(out[0]?.key).toBe('danger')
  })
})

describe('deriveCapabilities', () => {
  it('fails open when the auth config probe returns null', () => {
    const caps = deriveCapabilities(null)
    expect(caps.emailChannel).toBe(true)
    expect(caps.smsChannel).toBe(true)
    expect(caps.oauthProviders).toEqual([])
  })

  it('follows backend channel config (sms-only deployment hides email change)', () => {
    const config = {
      allowSmsLogin: true,
      allowEmailLogin: false,
      codeLoginViaEmail: false,
      recoveryViaEmail: false,
      registerViaEmail: false,
      oAuthProviders: [{ provider: 'github', displayName: 'GitHub' }],
    } as unknown as AuthConfigDto
    const caps = deriveCapabilities(config)
    expect(caps.smsChannel).toBe(true)
    expect(caps.emailChannel).toBe(false)
    expect(caps.oauthProviders).toHaveLength(1)
  })

  it('treats any email channel flag as email available', () => {
    const config = { recoveryViaEmail: true } as unknown as AuthConfigDto
    expect(deriveCapabilities(config).emailChannel).toBe(true)
  })
})
