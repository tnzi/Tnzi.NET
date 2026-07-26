/**
 * `v-permission` directive - hide/remove an element when the current user
 * doesn't have a required permission code.
 *
 * Use modes:
 *   v-permission="'user.delete'" - remove element if user lacks code
 *   v-permission="['user.delete','user.update']" - remove if user lacks ALL codes
 *   v-permission.any="['a','b']" - remove only if user lacks BOTH (any-of)
 *   v-permission.hide="'user.delete'" - set visibility:hidden instead of removing
 *
 * Backed by `useAdminAuthStore.hasPermission/hasAnyPermission/hasAllPermissions`
 * plus the `isSuperUser` bypass. Super-admins pass every check.
 */

import { watchEffect, type Directive, type DirectiveBinding, type WatchStopHandle } from 'vue'
import { useAdminAuthStore } from '../stores/useAdminAuthStore'

type PermissionValue = string | string[]

/** Per-element reactive effect handle so unmounted can dispose it. */
const stopHandles = new WeakMap<HTMLElement, WatchStopHandle>()

function evaluate(value: PermissionValue, anyMode: boolean): boolean {
  const auth = useAdminAuthStore()
  // Fail-open while the user isn't loaded yet - mirrors the sidebar filter,
  // usePermissionGuard and useCrudPage action gating. Backend [ApiAuthorize]
  // remains the real enforcement.
  if (auth.isSuperUser || auth.userInfo === null) return true
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
    // Reactive: re-apply whenever the auth store changes (permissions load
    // after mount, super flag flips, sign-out), not only on component
    // re-render - `evaluate` reads reactive store state, so watchEffect
    // tracks it for free.
    stopHandles.set(el, watchEffect(() => apply(el, binding)))
  },
  updated(el, binding) {
    // Binding VALUE changes (dynamic codes) re-run through the update hook;
    // the store dependency is already tracked by the mounted effect.
    apply(el, binding)
  },
  unmounted(el) {
    stopHandles.get(el)?.()
    stopHandles.delete(el)
  },
}
