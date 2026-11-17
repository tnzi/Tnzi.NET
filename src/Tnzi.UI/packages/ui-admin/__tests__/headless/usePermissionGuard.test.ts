import { describe, it, expect, beforeEach } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { usePermissionGuard } from '../../src/headless/usePermissionGuard'
import { useAdminAuthStore } from '../../src/stores/useAdminAuthStore'
import { createApp } from 'vue'
import { provideAdminSettingsConfig } from '../../src/plugin/settings-config'

function setUser(permissions: string[], isSuper = false, roles: string[] = []) {
  const auth = useAdminAuthStore()
  auth.setUserInfo({ id: '1', username: 'u', displayName: 'u', roles, permissions })
  auth.setSuperUser(isSuper)
}

describe('usePermissionGuard.canAnySettings', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('fails open before the user is loaded (userInfo null)', () => {
    expect(usePermissionGuard().canAnySettings()).toBe(true)
  })

  it('is true for a super-user regardless of held codes', () => {
    setUser([], true)
    expect(usePermissionGuard().canAnySettings()).toBe(true)
  })

  it('is true when the user holds any per-group settings view code', () => {
    setUser(['chat.session.view', 'chat.settings.general.view'])
    expect(usePermissionGuard().canAnySettings()).toBe(true)
  })

  it('is true when the user holds system.parameter.view (Advanced section)', () => {
    setUser(['system.parameter.view'])
    expect(usePermissionGuard().canAnySettings()).toBe(true)
  })

  it('is false when the user holds no settings-related code', () => {
    setUser(['chat.session.view', 'user.view', 'ai.agent.view'])
    expect(usePermissionGuard().canAnySettings()).toBe(false)
  })

  it('reachability keys on the view code, not update-only', () => {
    // Granting update without view is a misconfiguration; the backend filters
    // groups by the view code, so reachability keys on view too.
    setUser(['chat.settings.general.update'])
    expect(usePermissionGuard().canAnySettings()).toBe(false)
  })

  // A billing manager whose only grant is payment.account.view: the billing
  // sections live only in the Settings Center, so their code must open it.
  it('is true when the user holds the permission of a registered custom section', () => {
    const section = { key: 'billing-subscription', label: 'Subscription', permission: 'payment.account.view', component: {} }
    provideAdminSettingsConfig(createApp({}), { sections: [section] })
    try {
      setUser(['payment.account.view'])
      expect(usePermissionGuard().canAnySettings()).toBe(true)
      setUser(['payment.account.manage'])
      expect(usePermissionGuard().canAnySettings()).toBe(false)
    } finally {
      provideAdminSettingsConfig(createApp({}), {})
    }
  })

  // A configuration whose endpoints answer super administrators by role, with
  // no permission code: the section must not open the gear for anyone else.
  it('a roles-only section opens the Settings Center for holders of the role only', () => {
    const section = { key: 'root', label: 'Root', roles: ['SuperAdmin'], component: {} }
    provideAdminSettingsConfig(createApp({}), { sections: [section] })
    try {
      setUser(['user.view'], false, ['Admin'])
      expect(usePermissionGuard().canAnySettings()).toBe(false)
      setUser(['user.view'], false, ['superadmin'])
      expect(usePermissionGuard().canAnySettings()).toBe(true)
    } finally {
      provideAdminSettingsConfig(createApp({}), {})
    }
  })

  it('a section declaring both a permission and roles opens it only when both hold', () => {
    const section = { key: 'b', label: 'B', permission: 'payment.account.view', roles: ['Finance'], component: {} }
    provideAdminSettingsConfig(createApp({}), { sections: [section] })
    try {
      setUser(['payment.account.view'], false, ['Admin'])
      expect(usePermissionGuard().canAnySettings()).toBe(false)
      setUser([], false, ['Finance'])
      expect(usePermissionGuard().canAnySettings()).toBe(false)
      setUser(['payment.account.view'], false, ['FINANCE'])
      expect(usePermissionGuard().canAnySettings()).toBe(true)
    } finally {
      provideAdminSettingsConfig(createApp({}), {})
    }
  })

  it('an ungated section does not open the Settings Center', () => {
    provideAdminSettingsConfig(createApp({}), { sections: [{ key: 'open', label: 'Open', component: {} }] })
    try {
      setUser(['user.view'])
      expect(usePermissionGuard().canAnySettings()).toBe(false)
    } finally {
      provideAdminSettingsConfig(createApp({}), {})
    }
  })
})

describe('usePermissionGuard.canAnyRole', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('fails open before the user is loaded and for a super-user', () => {
    expect(usePermissionGuard().canAnyRole(['SuperAdmin'])).toBe(true)
    setUser([], true, [])
    expect(usePermissionGuard().canAnyRole(['SuperAdmin'])).toBe(true)
  })

  it('matches any one role, case-insensitively; an empty list passes', () => {
    setUser([], false, ['Admin'])
    const { canAnyRole } = usePermissionGuard()
    expect(canAnyRole(['superadmin', 'ADMIN'])).toBe(true)
    expect(canAnyRole(['SuperAdmin'])).toBe(false)
    expect(canAnyRole([])).toBe(true)
  })
})
