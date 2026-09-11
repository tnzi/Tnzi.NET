import { defineStore } from 'pinia'
import { ref } from 'vue'

/**
 * One breadcrumb crumb a page can contribute at runtime. `to` (a router path)
 * makes the crumb navigable; omit it for a static (non-clickable) crumb such as
 * the trailing leaf. `icon` is optional and overrides the route-derived glyph.
 */
export interface BreadcrumbItem {
  label: string
  to?: string
  icon?: string
}

/**
 * Holds per-route breadcrumb contributions so a detail page can inject either
 * the record's name (leaf label) or an entire cross-entity drill trail that the
 * static route tree cannot express - e.g. `Clients / <name> / File / <number>`,
 * where the file was reached THROUGH a client even though `clients/:id` and
 * `matters/:id` are sibling (flat) routes.
 *
 * Contributions are keyed by the SAME per-instance route key the tab store uses
 * (`multiInstanceKey`), so KeepAlive multi-instance detail pages (customer A vs
 * customer B) never clobber each other. `TAdminAutoBreadcrumb` reads the
 * contribution matching the ACTIVE route and falls back to the
 * `route.matched` / `meta.activeMenu` walk when none is present.
 *
 * Pages write through the {@link useBreadcrumbTrail} / {@link useBreadcrumbLabel}
 * composables, which also auto-clear the entry on unmount.
 */
export const useAdminBreadcrumbStore = defineStore('admin-breadcrumb', () => {
  const trails = ref<Record<string, BreadcrumbItem[]>>({})
  const leafLabels = ref<Record<string, string>>({})
  const pending = ref<Record<string, true>>({})

  /** Replace the full trail contributed for `key`. */
  function setTrail(key: string, items: BreadcrumbItem[]): void {
    trails.value = { ...trails.value, [key]: items }
    resolvePending(key)
  }

  /** Override just the trailing (leaf) crumb label for `key`. */
  function setLeafLabel(key: string, label: string): void {
    leafLabels.value = { ...leafLabels.value, [key]: label }
    resolvePending(key)
  }

  /**
   * Declare that `key`'s leaf is COMING - the page owns it, but the record it
   * names has not loaded yet.
   *
   * Without this the breadcrumb had no way to tell "this page contributes
   * nothing" from "this page contributes something it does not have yet", so it
   * fell back to the route-derived title for the gap. On a detail route that
   * title is inherited from the LIST (`Clients / Clients`), which is a wrong
   * label the reader watches get replaced by the right one a moment later. A
   * held leaf renders as a placeholder instead: one transition, from "loading"
   * to the record, and never a name that was never true.
   *
   * Cleared by the first `setTrail` / `setLeafLabel`, by `clear`, and by the
   * contributing composable's timeout (a record that never arrives must not
   * leave a placeholder pulsing forever - see `use-breadcrumb`).
   */
  function markPending(key: string): void {
    if (key in pending.value) return
    pending.value = { ...pending.value, [key]: true }
  }

  /** Drop the held state for `key` - the leaf is known, or giving up on it. */
  function resolvePending(key: string): void {
    if (!(key in pending.value)) return
    const next = { ...pending.value }
    delete next[key]
    pending.value = next
  }

  /** Is `key`'s leaf held pending its record? */
  function isPending(key: string): boolean {
    return key in pending.value
  }

  /** Drop every contribution for `key` (called on the contributing page's unmount). */
  function clear(key: string): void {
    resolvePending(key)
    if (key in trails.value) {
      const next = { ...trails.value }
      delete next[key]
      trails.value = next
    }
    if (key in leafLabels.value) {
      const next = { ...leafLabels.value }
      delete next[key]
      leafLabels.value = next
    }
  }

  function trailFor(key: string): BreadcrumbItem[] | undefined {
    return trails.value[key]
  }

  function leafLabelFor(key: string): string | undefined {
    return leafLabels.value[key]
  }

  return {
    trails,
    leafLabels,
    pending,
    setTrail,
    setLeafLabel,
    markPending,
    resolvePending,
    isPending,
    clear,
    trailFor,
    leafLabelFor,
  }
})
