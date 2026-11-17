/**
 * What a `TSearchSelect` lists for the words typed in its search box: the options it was given,
 * filtered here, or pages of options a loader fetches (a linked record out of thousands).
 *
 * The loader is called with the query and the offset of the page, and an `AbortSignal` that is
 * aborted as soon as its answer would no longer be shown (another word typed, the list closed);
 * an answer that arrives anyway is dropped. Typing waits `debounce` milliseconds before it asks.
 */
import { shallowRef, ref, type Ref, type ShallowRef } from 'vue'
import type { TSelectOption } from './options'

/** One choice of a `TSearchSelect`: a `TSelectOption` with an optional second piece of text. */
export interface TSearchSelectOption<V extends string | number = string> extends TSelectOption<V> {
  /** Quieter text at the end of the row (an e-mail, a code), to tell like labels apart. */
  readonly description?: string
}

/** One page of a loader's answer. */
export interface SearchSelectPage<V extends string | number = string> {
  readonly options: readonly TSearchSelectOption<V>[]
  /** More options follow this page for the same query. */
  readonly hasMore?: boolean
}

export interface SearchSelectLoadContext {
  /** How many options of this query are already listed: the page starts there. */
  readonly offset: number
  /** Aborted once the answer would no longer be shown. */
  readonly signal: AbortSignal
}

/** Fetches the options matching `query` (an empty query: the first ones of all). */
export type SearchSelectLoader<V extends string | number = string> = (
  query: string,
  context: SearchSelectLoadContext,
) => Promise<SearchSelectPage<V>>

/** The wait between the last key typed and the loader's call, in milliseconds. */
export const SEARCH_SELECT_DEBOUNCE_MS = 250

export interface SearchSelectSourceOptions<V extends string | number> {
  /** The options to filter when there is no loader. */
  readonly options: () => readonly TSearchSelectOption<V>[] | undefined
  /** Fetches pages of options; when set, `options` is not read. */
  readonly load: () => SearchSelectLoader<V> | undefined
  readonly debounce: () => number
}

export interface SearchSelectSource<V extends string | number> {
  readonly query: Ref<string>
  /** What the list shows now. */
  readonly items: Readonly<ShallowRef<readonly TSearchSelectOption<V>[]>>
  /** A first page is on its way (the list shows a loading row instead of "no matches"). */
  readonly loading: Readonly<Ref<boolean>>
  /** A further page is on its way. */
  readonly loadingMore: Readonly<Ref<boolean>>
  /** The last load failed; the error, for the list's failure row. */
  readonly error: Readonly<ShallowRef<unknown>>
  readonly hasMore: Readonly<Ref<boolean>>
  /** The search box changed: list what matches, after the debounce when loading. */
  search(query: string): void
  /** Lists the first page for the current query now (opening the list, Retry). */
  refresh(): void
  /** Appends the next page, when there is one and nothing is loading. */
  loadMore(): void
  /** Asks again for what failed: the first page, or the page that was to be appended. */
  retry(): void
  /** Forgets the query and stops anything in flight (the list closed). */
  reset(): void
}

/** Whether an option's label (or description) holds `query`, case- and accent-insensitively. */
export function searchSelectMatches(option: TSearchSelectOption<string | number>, query: string): boolean {
  const fold = (text: string) => text.normalize('NFD').replace(/\p{M}/gu, '').toLocaleLowerCase()
  const needle = fold(query.trim())
  if (!needle) return true
  return fold(option.label).includes(needle) || (option.description !== undefined && fold(option.description).includes(needle))
}

export function createSearchSelectSource<V extends string | number>(options: SearchSelectSourceOptions<V>): SearchSelectSource<V> {
  const query = ref('')
  const items = shallowRef<readonly TSearchSelectOption<V>[]>([])
  const loading = ref(false)
  const loadingMore = ref(false)
  const error = shallowRef<unknown>(null)
  const hasMore = ref(false)
  let controller: AbortController | null = null
  let timer: ReturnType<typeof setTimeout> | undefined
  /* Whether the page that failed was to be appended (Retry asks for it again, keeping the list). */
  let failedAppend = false

  function stop(): void {
    clearTimeout(timer)
    timer = undefined
    controller?.abort()
    controller = null
    loading.value = false
    loadingMore.value = false
  }

  function filterLocal(): void {
    const all = options.options() ?? []
    items.value = all.filter((option) => searchSelectMatches(option, query.value))
    hasMore.value = false
    error.value = null
  }

  async function fetchPage(append: boolean): Promise<void> {
    const load = options.load()
    if (!load) return
    controller?.abort()
    const current = new AbortController()
    controller = current
    const offset = append ? items.value.length : 0
    loading.value = !append
    loadingMore.value = append
    error.value = null
    failedAppend = false
    try {
      const page = await load(query.value, { offset, signal: current.signal })
      if (controller !== current) return
      items.value = append ? [...items.value, ...page.options] : [...page.options]
      hasMore.value = page.hasMore === true && page.options.length > 0
    } catch (failure) {
      if (controller !== current) return
      if (!append) items.value = []
      error.value = failure ?? new Error('The options could not be loaded')
      failedAppend = append
      // A failed further page still has more behind it; a failed first page has nothing.
      hasMore.value = append
    } finally {
      if (controller === current) {
        controller = null
        loading.value = false
        loadingMore.value = false
      }
    }
  }

  function refresh(): void {
    clearTimeout(timer)
    if (options.load()) void fetchPage(false)
    else filterLocal()
  }

  return {
    query,
    items,
    loading,
    loadingMore,
    error,
    hasMore,
    search(next) {
      query.value = next
      if (!options.load()) {
        filterLocal()
        return
      }
      clearTimeout(timer)
      controller?.abort()
      controller = null
      // The list says it is looking from the first key, not from when the loader is called.
      loading.value = true
      loadingMore.value = false
      timer = setTimeout(() => void fetchPage(false), Math.max(0, options.debounce()))
    },
    refresh,
    loadMore() {
      if (!options.load() || !hasMore.value || loading.value || loadingMore.value || error.value) return
      void fetchPage(true)
    },
    retry() {
      if (failedAppend) void fetchPage(true)
      else refresh()
    },
    reset() {
      stop()
      query.value = ''
      error.value = null
    },
  }
}
