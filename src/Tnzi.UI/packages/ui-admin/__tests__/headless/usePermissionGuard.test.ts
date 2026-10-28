import { describe, it, expect, beforeEach } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { usePermissionGuard } from '../../src/headless/usePermissionGuard'
import { useAdminAuthStore } from '../../src/stores/useAdminAuthStore'
import { createApp } from 'vue'
import { provideAdminSettingsConfig } from '../../src/plugin/settings-config'

function setUser(permissions: string[], isSuper = false) {
  const auth = useAdminAuthStore()
  auth.setUserInfo({ id: '1', username: 'u', displayName: 'u', roles: [], permissions })
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
})
