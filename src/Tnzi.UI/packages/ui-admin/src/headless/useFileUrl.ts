/**
 * `useFileUrl` - turn a stored file id into a URL an `<img>` / `<a download>`
 * can actually fetch.
 *
 * Why this is async at all: those elements issue their own requests and cannot
 * send an Authorization header, while the framework authenticates with bearer
 * tokens only. A private file therefore needs a short-lived signed token
 * appended to its URL, and fetching that token is a request. Public files
 * (avatars, site assets) need none of it and resolve synchronously.
 *
 * ```vue
 * const src = useFileUrl(() => message.fileId)
 * <img v-if="src" :src="src">
 * ```
 *
 * The underlying resolver batches every id requested in the same tick into one
 * round trip and caches tokens until they near expiry, so a list of N images
 * costs one request, not N.
 *
 * **Signed URLs expire** (the storage module's `SignedUrlTtlSeconds`, 10
 * minutes by default), and an `<img>` that fetches after that gets a 404: a
 * tab pane remounting its children, a filter mounting new cards, a
 * `loading="lazy"` image scrolling into view. Both composables therefore
 * re-sign on their own shortly before the earliest token they hold expires,
 * without flipping `loading`; the URLs change and the browser fetches them
 * again. For a URL the server still refuses (the client clock running behind
 * the server's), call `refresh()` from the image's `error` handler:
 *
 * ```vue
 * const { urls, refresh } = useFileUrls(() => docs.map((d) => d.thumbnailId))
 * <img :src="urls.get(doc.thumbnailId)" @error="refresh(doc.thumbnailId)">
 * ```
 *
 * `refresh()` re-signs once; a token minted in the last few seconds is kept,
 * so an image that fails for another reason (no thumbnail for that file type)
 * keeps its URL and the browser does not loop on it.
 */
import { getCurrentScope, onScopeDispose, ref, shallowRef, toValue, watch, type MaybeRefOrGetter, type Ref } from 'vue'
import type { FileUrlKind } from '@tnzi/core/services/storage'
import { useAdminClient } from '../plugin/client'
import { getFileUrlResolver } from '../services/file-url-resolver'

export interface UseFileUrlOptions {
  /** Which read endpoint to point at. Default `preview` (inline rendering). */
  kind?: FileUrlKind
  /**
   * Skip the token round trip because the file is known to be public
   * (`FileRecordDto.isPublic`). Saves a request per avatar.
   */
  isPublic?: MaybeRefOrGetter<boolean | undefined>
}

export interface UseFileUrlReturn {
  /** Resolved URL, or `null` while loading / when the caller may not read it. */
  url: Ref<string | null>
  /** True while a token is being minted for a new id (not during a re-sign). */
  loading: Ref<boolean>
  /**
   * Re-sign now, for an `<img>` whose URL the server refused. Costs nothing
   * for a public file or a token minted moments ago.
   */
  refresh: () => Promise<void>
}

/** The longest delay `setTimeout` honours; anything above it fires at once. */
const MAX_TIMER_DELAY_MS = 2_147_483_647
/** Floor between two automatic re-signs of the same view. */
const MIN_RESIGN_DELAY_MS = 1_000

/**
 * The timer behind the automatic re-sign. Owned by the calling scope: it stops
 * when the component unmounts, including for a resolve that answers afterwards.
 */
function createResignTimer() {
  let timer: ReturnType<typeof setTimeout> | null = null
  // Outside an effect scope nothing would ever stop the timer, so no automatic
  // re-sign there; `refresh()` still works.
  let disposed = !getCurrentScope()

  function cancel(): void {
    if (timer === null) return
    clearTimeout(timer)
    timer = null
  }

  if (!disposed) {
    onScopeDispose(() => {
      disposed = true
      cancel()
    })
  }

  return {
    cancel,
    /** Run `resign` at `at` (epoch ms, from `FileUrlResolver.refreshAt`); `null` = nothing to re-sign. */
    schedule(at: number | null, resign: () => void): void {
      cancel()
      if (at === null || disposed) return
      // Rounded up to the whole second: tokens from one batch carry expiries a
      // few ms apart, and every view holding them should re-sign in the same
      // tick, so the resolver merges them into one request again.
      const due = Math.ceil(at / 1000) * 1000
      const delay = Math.min(Math.max(due - Date.now(), MIN_RESIGN_DELAY_MS), MAX_TIMER_DELAY_MS)
      timer = setTimeout(() => {
        timer = null
        resign()
      }, delay)
    },
  }
}

/**
 * Resolve one file id reactively. Re-resolves when the id changes; a stale
 * response from a previous id is discarded rather than overwriting the current
 * one (list rows get recycled, and a late answer for the old row is wrong).
 */
export function useFileUrl(
  fileId: MaybeRefOrGetter<string | null | undefined>,
  options: UseFileUrlOptions = {},
): UseFileUrlReturn {
  const url = shallowRef<string | null>(null)
  const loading = ref(false)
  // `useAdminClient(false)` so a component mounted outside a configured admin
  // app (unit tests, storybook-style harnesses) degrades to "no URL" instead of
  // throwing on mount.
  const client = useAdminClient(false)
  const resignTimer = createResignTimer()

  let sequence = 0

  /**
   * `resign` = the id is unchanged and its URL is being renewed: the URL on
   * screen keeps working meanwhile, so `loading` stays as it is.
   */
  async function load(resign: boolean): Promise<void> {
    const current = ++sequence
    resignTimer.cancel()
    const id = toValue(fileId)

    if (!id || !client) {
      url.value = null
      loading.value = false
      return
    }

    const resolver = getFileUrlResolver(client)

    if (toValue(options.isPublic)) {
      url.value = resolver.plain(id, options.kind)
      loading.value = false
      return
    }

    if (!resign) loading.value = true
    const resolved = await resolver.resolve(id, options.kind)
    // A later id won the race - its answer is the one that belongs on screen.
    // List rows get recycled, so a late answer for the previous row would
    // briefly render another row's file.
    if (current !== sequence) return
    url.value = resolved
    loading.value = false
    resignTimer.schedule(resolver.refreshAt([id]), () => void load(true))
  }

  watch([() => toValue(fileId), () => toValue(options.isPublic)], () => void load(false), { immediate: true })

  function refresh(): Promise<void> {
    const id = toValue(fileId)
    if (!id || !client || toValue(options.isPublic)) return Promise.resolve()
    getFileUrlResolver(client).invalidate([id])
    return load(true)
  }

  return { url, loading, refresh }
}

export interface UseFileUrlsReturn {
  /** id → URL. Ids the caller may not read are absent rather than mapped to null. */
  urls: Ref<Map<string, string>>
  /** True while a batch is being minted for a new id list (not during a re-sign). */
  loading: Ref<boolean>
  /**
   * Re-sign now, for an `<img>` whose URL the server refused. Pass the id of
   * the image that failed, or nothing to re-sign the whole list; either way
   * the ids still fresh in the cache cost no request.
   */
  refresh: (fileId?: string | null) => Promise<void>
}

function sameUrls(a: Map<string, string>, b: Map<string, string>): boolean {
  if (a.size !== b.size) return false
  for (const [id, url] of a) {
    if (b.get(id) !== url) return false
  }
  return true
}

/**
 * Resolve a whole list of file ids at once - for `v-for` over messages,
 * attachments or thumbnails, where calling {@link useFileUrl} per row is not
 * possible (composables cannot be created inside a loop).
 *
 * Look results up with `urls.get(id)`; a missing key means "not resolved yet or
 * not readable", which is the same thing as far as rendering goes.
 */
export function useFileUrls(
  fileIds: MaybeRefOrGetter<readonly (string | null | undefined)[]>,
  options: Pick<UseFileUrlOptions, 'kind'> = {},
): UseFileUrlsReturn {
  const urls = shallowRef(new Map<string, string>())
  const loading = ref(false)
  const client = useAdminClient(false)
  const resignTimer = createResignTimer()

  let sequence = 0
  let currentIds: string[] = []

  /** See {@link useFileUrl}: `resign` renews the URLs of an unchanged list. */
  async function load(ids: string[], resign: boolean): Promise<void> {
    const current = ++sequence
    resignTimer.cancel()
    currentIds = ids

    if (ids.length === 0 || !client) {
      urls.value = new Map()
      loading.value = false
      return
    }

    const resolver = getFileUrlResolver(client)
    if (!resign) loading.value = true
    const resolved = await resolver.resolveMany(ids, options.kind)
    if (current !== sequence) return
    // An unchanged map keeps its identity, so a re-sign that renewed nothing
    // (every token still fresh) does not re-render the list.
    if (!sameUrls(urls.value, resolved)) urls.value = resolved
    loading.value = false
    resignTimer.schedule(resolver.refreshAt(ids), () => void load(currentIds, true))
  }

  watch(
    // Key on the id list itself, not the array identity: a parent re-render
    // that hands over an equal-but-new array must not re-mint the batch.
    () => toValue(fileIds).filter(Boolean).join(','),
    (joined) => void load(joined ? joined.split(',') : [], false),
    { immediate: true },
  )

  function refresh(fileId?: string | null): Promise<void> {
    if (!client || currentIds.length === 0) return Promise.resolve()
    getFileUrlResolver(client).invalidate(fileId ? [fileId] : currentIds)
    return load(currentIds, true)
  }

  return { urls, loading, refresh }
}
