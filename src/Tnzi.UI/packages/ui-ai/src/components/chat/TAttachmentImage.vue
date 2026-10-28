<script setup lang="ts">
/**
 * @experimental
 * TAttachmentImage - an image a message carries: one the user sent, or one a tool returned.
 *
 * A stored attachment is read through the render context's resolver (the signed-in client
 * fetches it; a plain `<img src>` could not), an image still in memory (just sent) is shown
 * from its data. Clicking opens it full size in a new tab.
 */
import { computed } from 'vue'
import { useAiI18n } from '../../i18n/index'
import { useAttachmentUrl } from '../../headless/useAttachmentUrl'
import { useChatRenderContext } from './chat-render-context'

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
</script>

<template>
  <a
    v-if="shown"
    :href="shown"
    target="_blank"
    rel="noopener noreferrer"
    class="t-attachment-image"
    :class="{ 't-attachment-image--large': large }"
  >
    <img :src="shown" :alt="alt ?? ''" loading="lazy" />
  </a>
  <div
    v-else
    class="t-attachment-image t-attachment-image--placeholder"
    :class="{ 't-attachment-image--large': large, 'is-loading': loading }"
    :title="failed ? t.chat.attachmentUnavailable : undefined"
    role="img"
    :aria-label="alt ?? t.chat.attachmentUnavailable"
  />
</template>

<style scoped>
.t-attachment-image {
  display: block;
  width: 80px;
  height: 80px;
  overflow: hidden;
  border-radius: 8px;
  border: 1px solid var(--tnzi-ai-border);
  background: var(--tnzi-ai-surface);
}
.t-attachment-image img {
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
