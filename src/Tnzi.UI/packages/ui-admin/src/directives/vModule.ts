/**
 * `v-module` directive - hide/remove an element when the backend host did NOT
 * load a required framework module (per `GET /admin/shell/modules`). The
 * module-availability twin of `v-permission`.
 *
 * Use modes:
 *   v-module="'chat'" - remove element unless Chat is loaded
 *   v-module="['chat','notification']" - remove unless ALL modules are loaded
 *   v-module.any="['a','b']" - remove only if NONE is loaded (any-of)
 *   v-module.hide="'chat'" - set visibility:hidden instead of removing
 *
 * Backed by `useAdminRouteStore.availableModules`. FAIL-OPEN while the signal
 * is unavailable (older backend / probe in flight / `moduleGating` disabled),
 * mirroring the sidebar menu filter. UNLIKE `v-permission` there is no
 * super-user bypass: module availability is a fact about the backend process,
 * orthogonal to who is signed in.
 *
 * Module names accept `"AI.Skills"` / `"ai.skills"` / `"ai-skills"` alike
 * (normalized like the route-level `meta.moduleGate`).
 */

import { watchEffect, type Directive, type DirectiveBinding, type WatchStopHandle } from 'vue'
import { useAdminRouteStore } from '../stores/useAdminRouteStore'
import { normalizeModuleName } from '../services/admin-shell-modules'

type ModuleValue = string | string[]

/** Per-element reactive effect handle so unmounted can dispose it. */
const stopHandles = new WeakMap<HTMLElement, WatchStopHandle>()

function evaluate(value: ModuleValue, anyMode: boolean): boolean {
  const routeStore = useAdminRouteStore()
  const available = routeStore.availableModules
  // Fail-open while the loaded-module signal is unavailable - mirrors the
  // sidebar module gate. A missing signal must never blank UI.
  if (available === null) return true
  const loaded = (m: string): boolean => available.has(normalizeModuleName(m))
  if (typeof value === 'string') return loaded(value)
  if (Array.isArray(value)) {
    return anyMode ? value.some(loaded) : value.every(loaded)
  }
  return false
}

function apply(el: HTMLElement, binding: DirectiveBinding<ModuleValue>): void {
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

export const vModule: Directive<HTMLElement, ModuleValue> = {
  mounted(el, binding) {
    // Reactive: re-apply whenever the module signal changes (it loads after
    // mount via the install() probe, refreshes after login) - `evaluate`
    // reads reactive store state, so watchEffect tracks it for free.
    stopHandles.set(el, watchEffect(() => apply(el, binding)))
  },
  updated(el, binding) {
    // Binding VALUE changes (dynamic module names) re-run through the update
    // hook; the store dependency is already tracked by the mounted effect.
    apply(el, binding)
  },
  unmounted(el) {
    stopHandles.get(el)?.()
    stopHandles.delete(el)
  },
}
