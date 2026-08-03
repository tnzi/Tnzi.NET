<template>
  <!--
    No href while the token is still being fetched, and none when the caller may
    not read the file. An `<a>` without href is NOT a link: it cannot be focused
    and clicking it does nothing - exactly the inert state we want.

    Writing `:href="url ?? ''"` instead would point at the current page, so a
    click reloads the whole app and throws away whatever the user was filling
    in. That bug shipped twice here before this component existed.
  -->
  <a
    :href="url ?? undefined"
    :download="download ? '' : undefined"
    :target="target"
    rel="noopener noreferrer"
    :aria-disabled="url ? undefined : 'true'"
    v-bind="$attrs"
  >
    <slot :loading="loading" :ready="!!url" />
  </a>
</template>

<script setup lang="ts">
/**
 * `TFileLink` - a link that downloads (or opens) a stored file, private or public.
 *
 * Same reason as `TFileImage`: browser-issued requests carry no Authorization
 * header, so a private file's URL needs a short-lived signed token fetched
 * first. This owns that fetch, and owns the inert-while-unresolved rule that is
 * easy to get subtly wrong at a call site.
 *
 * ```vue
 * <TFileLink :file-id="attachment.fileId">{{ attachment.fileName }}</TFileLink>
 * ```
 *
 * Safe inside `v-for` - see `TFileImage` for why the requests still batch.
 */
import { computed } from 'vue'
import type { FileUrlKind } from '@tnzi/core/services/storage'
import { useFileUrl } from '../../headless/useFileUrl'

defineOptions({ inheritAttrs: false })

const props = withDefaults(
  defineProps<{
    /** Stored file id. Null / empty renders the slot without a working link. */
    fileId?: string | null
    /** Skip the token round trip because the file is public. */
    isPublic?: boolean
    /** Which read endpoint to point at. Default `download` (forced attachment). */
    kind?: FileUrlKind
    /** Ask the browser to save rather than navigate. Default true. */
    download?: boolean
    target?: string
  }>(),
  { kind: 'download', download: true, target: '_blank' },
)

const { url, loading } = useFileUrl(
  () => props.fileId,
  { kind: props.kind, isPublic: computed(() => props.isPublic) },
)
</script>
