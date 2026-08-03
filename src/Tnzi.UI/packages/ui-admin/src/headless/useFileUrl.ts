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
 */
import { ref, shallowRef, toValue, watch, type MaybeRefOrGetter, type Ref } from 'vue'
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
  /** True while a token is being minted. */
  loading: Ref<boolean>
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

  let sequence = 0

  watch(
    [() => toValue(fileId), () => toValue(options.isPublic)],
    async ([id, isPublic]) => {
      const current = ++sequence

      if (!id || !client) {
        url.value = null
        loading.value = false
        return
      }

      const resolver = getFileUrlResolver(client)

      if (isPublic) {
        url.value = resolver.plain(id, options.kind)
        loading.value = false
        return
      }

      loading.value = true
      const resolved = await resolver.resolve(id, options.kind)
      // A later id won the race - its answer is the one that belongs on screen.
      // List rows get recycled, so a late answer for the previous row would
      // briefly render another row's file.
      if (current !== sequence) return
      url.value = resolved
      loading.value = false
    },
    { immediate: true },
  )

  return { url, loading }
}

export interface UseFileUrlsReturn {
  /** id → URL. Ids the caller may not read are absent rather than mapped to null. */
  urls: Ref<Map<string, string>>
  /** True while a batch is being minted. */
  loading: Ref<boolean>
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

  let sequence = 0

  watch(
    // Key on the id list itself, not the array identity: a parent re-render
    // that hands over an equal-but-new array must not re-mint the batch.
    () => toValue(fileIds).filter(Boolean).join(','),
    async (joined) => {
      const current = ++sequence
      const ids = joined ? joined.split(',') : []

      if (ids.length === 0 || !client) {
        urls.value = new Map()
        loading.value = false
        return
      }

      loading.value = true
      const resolved = await getFileUrlResolver(client).resolveMany(ids, options.kind)
      if (current !== sequence) return
      urls.value = resolved
      loading.value = false
    },
    { immediate: true },
  )

  return { urls, loading }
}
