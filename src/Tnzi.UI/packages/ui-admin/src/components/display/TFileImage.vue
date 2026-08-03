<template>
  <NImage
    v-if="lightbox && url"
    :src="url"
    :img-props="{ alt, ...imgProps }"
    v-bind="$attrs"
  />
  <img v-else-if="url" :src="url" :alt="alt" v-bind="$attrs" />
  <slot v-else name="fallback" :loading="loading" />
</template>

<script setup lang="ts">
/**
 * `TFileImage` - render a stored file as an image, private or public.
 *
 * Exists because a private file's URL cannot be built by string concatenation:
 * `<img>` issues its own request with no Authorization header, so the URL has
 * to carry a short-lived signed token that must be fetched first. Doing that at
 * every call site meant a composable, a null check and a `v-if` per image; this
 * collapses it to a tag.
 *
 * ```vue
 * <TFileImage :file-id="message.fileId" lightbox />
 * <TFileImage :file-id="user.avatarId" is-public />
 * ```
 *
 * **Safe inside `v-for`.** Each instance resolves its own id, and the shared
 * resolver still merges every request made in the same tick into one round
 * trip - so a list of N images costs one request, not N. `useFileUrls` is only
 * needed when the PARENT needs the URLs (feeding a column render function, a
 * third-party lightbox, an export).
 */
import { computed } from 'vue'
import { NImage } from 'naive-ui'
import type { FileUrlKind } from '@tnzi/core/services/storage'
import { useFileUrl } from '../../headless/useFileUrl'

defineOptions({ inheritAttrs: false })

const props = withDefaults(
  defineProps<{
    /** Stored file id. Null / empty renders the `fallback` slot instead. */
    fileId?: string | null
    /**
     * Skip the token round trip because the file is public
     * (`FileRecordDto.isPublic`). Saves one request per avatar.
     */
    isPublic?: boolean
    /** Which read endpoint to render from. Default `preview`. */
    kind?: FileUrlKind
    /**
     * Render through naive's `NImage` so the picture opens a zoom lightbox,
     * and participates in an ancestor `NImageGroup` (prev/next across a
     * thread). Plain `<img>` otherwise - a grid thumbnail wants no lightbox.
     */
    lightbox?: boolean
    alt?: string
    /** Extra props for the inner `<img>` when `lightbox` is on. */
    imgProps?: Record<string, unknown>
  }>(),
  { kind: 'preview', alt: '' },
)

const { url, loading } = useFileUrl(
  () => props.fileId,
  { kind: props.kind, isPublic: computed(() => props.isPublic) },
)
</script>
