<template>
  <NImage
    v-if="lightbox && url"
    :src="url"
    :img-props="{ alt, ...imgProps }"
    :object-fit="objectFit"
    v-bind="$attrs"
  />
  <img v-else-if="url" :src="url" :alt="alt" v-bind="$attrs" :style="objectFitStyle" />
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
    /**
     * How the picture fills its box (CSS `object-fit`). Applies in BOTH
     * branches; without it the browser default `fill` stretches a thumbnail to
     * whatever shape the tile is.
     *
     * ★ It has to be a prop. In the `lightbox` branch the picture is naive's
     * `NImage`, which is `inheritAttrs: false` and merges fallthrough attrs
     * onto its OUTER wrapper - so a `:style="{ objectFit }"` written here never
     * reaches the `<img>`; and `:img-props="{ style: { objectFit } }"` loses to
     * NImage's own `objectFit`, which it appends LAST to the same style array.
     * Passing `object-fit` through as a fallthrough attr does work at runtime
     * (NImage declares the prop, so it is extracted before `$attrs`), but a
     * consumer on `strictTemplates` cannot write it - hence this prop.
     *
     * `object-fit` only means something once the box is sized, and CSS is what
     * sizes it: a `class` / `:style` here lands on the `<img>` in the plain
     * branch but on the WRAPPER in the lightbox branch, so size the picture
     * itself through `:img-props="{ style: … }"` there.
     */
    objectFit?: 'fill' | 'contain' | 'cover' | 'none' | 'scale-down'
  }>(),
  { kind: 'preview', alt: '' },
)

const { url, loading } = useFileUrl(
  () => props.fileId,
  { kind: props.kind, isPublic: computed(() => props.isPublic) },
)

/** Undefined when unset, so the plain `<img>` keeps rendering exactly as before. */
const objectFitStyle = computed(() => (props.objectFit ? { objectFit: props.objectFit } : undefined))
</script>
