<script setup lang="ts">
/**
 * @experimental
 * TAttachmentImage - an image a message carries: one the user sent, or one a tool returned.
 *
 * A stored attachment is read through the render context's resolver (the signed-in client
 * fetches it; a plain `<img src>` could not), an image still in memory (just sent) is shown
 * from its data.
 *
 * Clicking it (or Enter / Space on it) shows it full size in a viewer over the page
 * (`TImageViewer`), not in a new tab. A new tab cannot show either kind: browsers refuse to
 * open a data: URL as a page, and an app embedding the UI hands new windows to the system
 * browser, where the page's blob: URLs do not exist. The images of one group (a message's
 * attachments, one tool call's pictures: `provideImageGroup`) are one set there, stepped
 * through with the arrow keys. Escape, the close button, a click beside the picture and on a
 * phone the back gesture close the viewer, and focus returns to the image.
 *
 * The viewer is fetched on the first click, not with the message: most thumbnails are never
 * opened, and the embeddable widgets carry this renderer onto third-party pages.
 */
import { computed, defineAsyncComponent, onBeforeUnmount, ref } from 'vue'
import { useAiI18n, formatAiMessage } from '../../i18n/index'
import { useAttachmentUrl } from '../../headless/useAttachmentUrl'
import { useChatRenderContext } from './chat-render-context'
import { useImageGroup, type ImageGroupMember, type ViewerImage } from './image-group'

const props = withDefaults(
  defineProps<{
    /** A stored attachment. */
    attachmentId?: string | null
    /** An image already at hand (a data URL, or any URL the browser can load). */
    src?: string | null
    alt?: string | null
    /** Show it large (a tool's screenshot) rather than as a thumbnail. */
    large?: boolean
  }>(),
  { attachmentId: null, src: null, alt: null, large: false },
)

const t = useAiI18n()
const context = useChatRenderContext()
const { url, loading, failed } = useAttachmentUrl(
  () => (props.src ? null : props.attachmentId),
  () => context.value.resolveAttachmentUrl,
)

const shown = computed(() => props.src || url.value)
const label = computed(() => props.alt || t.value.chat.image)
const openLabel = computed(() => formatAiMessage(t.value.chat.openFile, { name: label.value }))

const TImageViewer = defineAsyncComponent(() => import('../overlay/TImageViewer.vue'))

/* This image as the viewer shows it, in its group when there is one. */
const member: ImageGroupMember = { image: () => ({ src: shown.value || null, alt: label.value }) }
const group = useImageGroup()
const leave = group?.join(member)
onBeforeUnmount(() => leave?.())

/* The set the viewer steps through, read when it opens; and the picture it shows. */
const set = ref<readonly ViewerImage[]>([])
const viewing = ref<number | null>(null)
/* Mounted from the first open on: closing must reach the viewer, which hands focus back. */
const viewerMounted = ref(false)
function openViewer(): void {
  viewerMounted.value = true
  const members = group?.members.value ?? [member]
  set.value = members.map((m) => m.image())
  viewing.value = Math.max(0, members.indexOf(member))
}
</script>

<template>
  <button
    v-if="shown"
    type="button"
    class="t-attachment-image"
    :class="{ 't-attachment-image--large': large }"
    :aria-label="openLabel"
    aria-haspopup="dialog"
    @click="openViewer"
  >
    <img :src="shown" :alt="alt ?? ''" loading="lazy" />
  </button>
  <div
    v-else
    class="t-attachment-image t-attachment-image--placeholder"
    :class="{ 't-attachment-image--large': large, 'is-loading': loading }"
    :title="failed ? t.chat.attachmentUnavailable : undefined"
    role="img"
    :aria-label="alt ?? t.chat.attachmentUnavailable"
  />
  <TImageViewer v-if="viewerMounted" v-model:index="viewing" :images="set" />
</template>

<style scoped>
.t-attachment-image {
  display: block;
  width: 80px;
  height: 80px;
  padding: 0;
  overflow: hidden;
  border-radius: 8px;
  border: 1px solid var(--tnzi-ai-border);
  background: var(--tnzi-ai-surface);
  font: inherit;
  cursor: zoom-in;
}
button.t-attachment-image:focus-visible {
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: 2px;
}
.t-attachment-image img {
  display: block;
  width: 100%;
  height: 100%;
  object-fit: cover;
}
.t-attachment-image--large {
  width: auto;
  height: auto;
  max-width: min(100%, 480px);
  min-height: 48px;
  min-width: 96px;
}
.t-attachment-image--large img {
  height: auto;
  object-fit: contain;
}
.t-attachment-image--placeholder.is-loading {
  animation: t-attachment-image-pulse 1.4s ease-in-out infinite;
}
@keyframes t-attachment-image-pulse {
  50% { opacity: 0.5; }
}
</style>
