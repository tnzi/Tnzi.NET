import { computed, type ComputedRef } from 'vue'
import type { UseCrudPageReturn } from './useCrudPage'

/**
 * What a list says when it is empty and nobody is searching: what the page is
 * for, and what will appear here. Both strings are already translated, like a
 * page's `searchPlaceholder`.
 */
export interface ListEmptyContent {
  /** Replaces the generic "No data" line. */
  text?: string
  /** A muted sentence under it: what this page is for, how records get here. */
  hint?: string
}

export interface UseEmptyCreateCtaReturn {
  /** Whether the empty state should offer a Create call-to-action. */
  showCreateCta: ComputedRef<boolean>
  /** Translated button label (falls back to 'Create' without a translator). */
  createCtaLabel: ComputedRef<string>
  /** Click handler - opens the create form via `state.openCreate()`. */
  onEmptyCreate: () => void
  /** Whether a keyword or a filter is active: the list is empty because of a search. */
  filtered: ComputedRef<boolean>
  /**
   * The empty line: the page's own text on a first-load empty list, the
   * generic one (or undefined: TEmpty's 'No data') for a search miss.
   */
  emptyText: ComputedRef<string | undefined>
  /** The page's hint, on a first-load empty list only. */
  emptyHint: ComputedRef<string | undefined>
}

/**
 * Shared empty-state logic for the CRUD renderers (`TTableRenderer` /
 * `TCardRenderer` / `TItemRenderer`).
 *
 * The CTA only shows on a *first-load* empty list:
 *  - the page is creatable (`state.canCreate !== false` and `openCreate`
 *    exists - read-only pages built without `createData` show no CTA), and
 *  - no keyword / advanced filter is active. A search or filter pass with
 *    zero hits keeps the plain empty visual - a Create button there reads
 *    as noise ("create what, my search term?").
 *
 * The page's own empty text and hint follow the same split: they describe an
 * empty page, so a search that found nothing keeps the generic line.
 *
 * Written defensively (optional chaining) because tests and embedding
 * consumers pass duck-typed partial states.
 */
export function useEmptyCreateCta<T, TId extends string | number = string | number>(
  getState: () => UseCrudPageReturn<T, TId>,
  getTranslate: () => ((key: string) => string) | undefined,
  getContent: () => ListEmptyContent | undefined = () => undefined,
): UseEmptyCreateCtaReturn {
  const filtered = computed(() => {
    const s = getState() as Partial<UseCrudPageReturn<T, TId>> | undefined
    const q = s?.query?.value
    if (!q) return false
    const hasKeyword = (q.searchText ?? '').trim().length > 0
    const hasFilters = Object.values(q.filters ?? {}).some((v) => v != null && v !== '')
    return hasKeyword || hasFilters
  })

  const showCreateCta = computed(() => {
    const s = getState() as Partial<UseCrudPageReturn<T, TId>> | undefined
    if (!s || s.canCreate === false || typeof s.openCreate !== 'function') return false
    return !filtered.value
  })

  const createCtaLabel = computed(() => {
    const t = getTranslate()
    return t ? t('admin.crud.create') : 'Create'
  })

  const emptyText = computed(() => {
    const own = filtered.value ? undefined : getContent()?.text
    if (own) return own
    const t = getTranslate()
    // Without a translator let TEmpty fall back to 'No data' instead of
    // leaking the raw `admin.crud.empty` key.
    return t ? t('admin.crud.empty') : undefined
  })

  const emptyHint = computed(() => (filtered.value ? undefined : getContent()?.hint || undefined))

  function onEmptyCreate(): void {
    getState().openCreate()
  }

  return { showCreateCta, createCtaLabel, onEmptyCreate, filtered, emptyText, emptyHint }
}
