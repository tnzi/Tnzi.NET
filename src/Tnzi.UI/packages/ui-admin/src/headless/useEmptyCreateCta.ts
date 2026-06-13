import { computed, type ComputedRef } from 'vue'
import type { UseCrudPageReturn } from './useCrudPage'

export interface UseEmptyCreateCtaReturn {
  /** Whether the empty state should offer a Create call-to-action. */
  showCreateCta: ComputedRef<boolean>
  /** Translated button label (falls back to 'Create' without a translator). */
  createCtaLabel: ComputedRef<string>
  /** Click handler — opens the create form via `state.openCreate()`. */
  onEmptyCreate: () => void
}

/**
 * Shared "empty list → Create call-to-action" logic for the CRUD renderers
 * (`TTableRenderer` / `TCardRenderer`).
 *
 * The CTA only shows on a *first-load* empty list:
 *  - the page is creatable (`state.canCreate !== false` and `openCreate`
 *    exists — read-only pages built without `createData` show no CTA), and
 *  - no keyword / advanced filter is active. A search or filter pass with
 *    zero hits keeps the plain empty visual — a Create button there reads
 *    as noise ("create what, my search term?").
 *
 * Written defensively (optional chaining) because tests and embedding
 * consumers pass duck-typed partial states.
 */
export function useEmptyCreateCta<T, TId extends string | number = string | number>(
  getState: () => UseCrudPageReturn<T, TId>,
  getTranslate: () => ((key: string) => string) | undefined,
): UseEmptyCreateCtaReturn {
  const showCreateCta = computed(() => {
    const s = getState() as Partial<UseCrudPageReturn<T, TId>> | undefined
    if (!s || s.canCreate === false || typeof s.openCreate !== 'function') return false
    const q = s.query?.value
    if (!q) return true
    const hasKeyword = (q.searchText ?? '').trim().length > 0
    const hasFilters = Object.values(q.filters ?? {}).some((v) => v != null && v !== '')
    return !hasKeyword && !hasFilters
  })

  const createCtaLabel = computed(() => {
    const t = getTranslate()
    return t ? t('admin.crud.create') : 'Create'
  })

  function onEmptyCreate(): void {
    getState().openCreate()
  }

  return { showCreateCta, createCtaLabel, onEmptyCreate }
}
