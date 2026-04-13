import { describe, it, expect, vi } from 'vitest'
import { usePermissionGuard } from '../../headless/usePermissionGuard'

function makeGuard(overrides: Partial<Parameters<typeof usePermissionGuard>[0]> = {}) {
  return usePermissionGuard({
    isAuthenticated: () => true,
    hasPermission: () => true,
    hasRole: () => true,
    ...overrides,
  })
}

describe('usePermissionGuard', () => {
  describe('canAccess', () => {
    it('allows access with no restrictions', () => {
      const guard = makeGuard()
      expect(guard.canAccess({})).toBe(true)
    })

    it('denies access when requiresAuth and not authenticated', () => {
      const guard = makeGuard({ isAuthenticated: () => false })
      expect(guard.canAccess({ requiresAuth: true })).toBe(false)
    })

    it('allows access when requiresAuth and authenticated', () => {
      const guard = makeGuard({ isAuthenticated: () => true })
      expect(guard.canAccess({ requiresAuth: true })).toBe(true)
    })

    it('denies access when missing required permission', () => {
      const guard = makeGuard({ hasPermission: (p) => p !== 'admin.users.delete' })
      expect(guard.canAccess({ permissions: ['admin.users.delete'] })).toBe(false)
    })

    it('allows access when all permissions are present', () => {
      const guard = makeGuard({ hasPermission: () => true })
      expect(guard.canAccess({ permissions: ['admin.read', 'admin.write'] })).toBe(true)
    })

    it('denies access when missing required role', () => {
      const guard = makeGuard({ hasRole: () => false })
      expect(guard.canAccess({ roles: ['admin'] })).toBe(false)
    })

    it('allows access when any required role matches', () => {
      const guard = makeGuard({ hasRole: (r) => r === 'moderator' })
      expect(guard.canAccess({ roles: ['admin', 'moderator'] })).toBe(true)
    })

    it('denies when auth fails even if permissions pass', () => {
      const guard = makeGuard({ isAuthenticated: () => false, hasPermission: () => true })
      expect(guard.canAccess({ requiresAuth: true, permissions: ['read'] })).toBe(false)
    })
  })

  describe('guardRoute', () => {
    it('calls onUnauthorized when not authenticated', () => {
      const onUnauthorized = vi.fn()
      const guard = makeGuard({ isAuthenticated: () => false, onUnauthorized })
      const result = guard.guardRoute({ requiresAuth: true })
      expect(result).toBe(false)
      expect(onUnauthorized).toHaveBeenCalledOnce()
    })

    it('calls onForbidden when missing permission', () => {
      const onForbidden = vi.fn()
      const guard = makeGuard({ hasPermission: () => false, onForbidden })
      const result = guard.guardRoute({ permissions: ['admin'] })
      expect(result).toBe(false)
      expect(onForbidden).toHaveBeenCalledOnce()
    })

    it('calls onForbidden when missing role', () => {
      const onForbidden = vi.fn()
      const guard = makeGuard({ hasRole: () => false, onForbidden })
      const result = guard.guardRoute({ roles: ['admin'] })
      expect(result).toBe(false)
      expect(onForbidden).toHaveBeenCalledOnce()
    })

    it('returns true and calls no callbacks when access allowed', () => {
      const onUnauthorized = vi.fn()
      const onForbidden = vi.fn()
      const guard = makeGuard({ onUnauthorized, onForbidden })
      const result = guard.guardRoute({ requiresAuth: true, permissions: ['read'], roles: ['user'] })
      expect(result).toBe(true)
      expect(onUnauthorized).not.toHaveBeenCalled()
      expect(onForbidden).not.toHaveBeenCalled()
    })

    it('does not call onUnauthorized when requiresAuth is absent', () => {
      const onUnauthorized = vi.fn()
      const guard = makeGuard({ isAuthenticated: () => false, onUnauthorized })
      guard.guardRoute({})
      expect(onUnauthorized).not.toHaveBeenCalled()
    })
  })
})
