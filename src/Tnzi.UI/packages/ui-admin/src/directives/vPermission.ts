/**
 * `v-permission` directive — hide/remove an element when the current user
 * doesn't have a required permission code.
 *
 * Use modes:
 *   v-permission="'user.delete'"        — remove element if user lacks code
 *   v-permission="['user.delete','user.update']" — remove if user lacks ALL codes
 *   v-permission.any="['a','b']"        — remove only if user lacks BOTH (any-of)
 *   v-permission.hide="'user.delete'"   — set visibility:hidden instead of removing
 *
 * Backed by `useAdminAuthStore.hasPermission/hasAnyPermission/hasAllPermissions`
 * plus the `isSuperUser` bypass. Super-admins pass every check.
 */

import type { Directive, DirectiveBinding } from 'vue'
import { useAdminAuthStore } from '../stores/useAdminAuthStore'

type PermissionValue = string | string[]

function evaluate(value: PermissionValue, anyMode: boolean): boolean {
  const auth = useAdminAuthStore()
  if (auth.isSuperUser) return true
  if (typeof value === 'string') return auth.hasPermission(value)
  if (Array.isArray(value)) {
    return anyMode ? auth.hasAnyPermission(value) : auth.hasAllPermissions(value)
  }
  return false
}

function apply(el: HTMLElement, binding: DirectiveBinding<PermissionValue>): void {
  const allowed = evaluate(binding.value, !!binding.modifiers.any)
  if (allowed) {
    el.style.removeProperty('display')
    el.style.removeProperty('visibility')
    return
  }
  if (binding.modifiers.hide) {
    el.style.visibility = 'hidden'
  } else {
    el.style.display = 'none'
  }
}

export const vPermission: Directive<HTMLElement, PermissionValue> = {
  mounted(el, binding) {
    apply(el, binding)
  },
  updated(el, binding) {
    apply(el, binding)
  },
}
