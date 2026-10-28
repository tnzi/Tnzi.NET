<script setup lang="ts">
/**
 * @experimental
 * TFileCard - a file in the conversation, as a card: one the user attached, or one the
 * agent handed over (a tool's file).
 *
 * Where it links to is the product's call (`fileHref` in the render context): a file
 * handed over by a tool carries the URI the tool gave, which only the product knows how to
 * open. Without one, an attachment opens its own content and a tool's file opens its URI
 * when a browser can (an `http(s)` or site-relative address); otherwise the card is not a link.
 */
import { computed, ref, watch } from 'vue'
import { Icon } from '@iconify/vue'
import { useAiI18n, formatAiMessage } from '../../i18n/index'
import { fileIconForName } from '../../utils/file-icon'
import { formatFileSize } from '@tnzi/core/utils'
import { defaultToolFileHref, useChatRenderContext, type ChatFileRef } from './chat-render-context'

const props = defineProps<{
  file: ChatFileRef
}>()

const t = useAiI18n()
const context = useChatRenderContext()

const displayName = computed(() => props.file.name || props.file.uri?.split('/').pop() || 'File')
const meta = computed(() => {
  const parts: string[] = []
  const extension = displayName.value.includes('.') ? displayName.value.split('.').pop()?.toUpperCase() : undefined
  if (extension && extension.length <= 5) parts.push(extension)
  if (props.file.size) parts.push(formatFileSize(props.file.size))
  return parts.join(' · ')
})

/* The link: the product's, else the attachment's own content (resolved on demand), else
   the tool's URI when a browser can open it. */
const href = ref<string | null>(null)
watch(
  () => [props.file, context.value] as const,
  async ([file, ctx]) => {
    const custom = ctx.fileHref?.(file)
    if (custom) {
      href.value = custom
      return
    }
    if (file.attachmentId && ctx.resolveAttachmentUrl) {
      href.value = null
      const resolved = await ctx.resolveAttachmentUrl(file.attachmentId)
      if (props.file === file) href.value = resolved
      return
    }
    href.value = defaultToolFileHref(file)
  },
  { immediate: true },
)

const label = computed(() => formatAiMessage(t.value.chat.openFile, { name: displayName.value }))
</script>

<template>
  <component
    :is="href ? 'a' : 'div'"
    class="t-file-card"
    :class="{ 't-file-card--link': !!href }"
    v-bind="href ? { href, target: '_blank', rel: 'noopener noreferrer', 'aria-label': label } : {}"
  >
    <span class="t-file-card__icon" aria-hidden="true">
      <Icon :icon="fileIconForName(displayName)" />
    </span>
    <span class="t-file-card__text">
      <span class="t-file-card__name">{{ displayName }}</span>
      <span v-if="meta" class="t-file-card__meta">{{ meta }}</span>
    </span>
    <Icon v-if="href" icon="lucide:arrow-up-right" class="t-file-card__open" aria-hidden="true" />
  </component>
</template>

<style scoped>
.t-file-card {
  display: inline-flex;
  align-items: center;
  gap: 10px;
  max-width: 320px;
  padding: 8px 12px 8px 8px;
  border-radius: 10px;
  border: 1px solid var(--tnzi-ai-border);
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text);
  text-decoration: none;
}
.t-file-card--link:hover { background: var(--tnzi-ai-hover); }
.t-file-card__icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  flex-shrink: 0;
  border-radius: 8px;
  background: var(--tnzi-ai-bg);
  color: var(--tnzi-ai-text-secondary);
  font-size: 18px;
}
.t-file-card__text {
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.t-file-card__name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 13px;
  font-weight: 500;
}
.t-file-card__meta {
  font-size: 12px;
  color: var(--tnzi-ai-text-secondary);
}
.t-file-card__open {
  flex-shrink: 0;
  color: var(--tnzi-ai-text-secondary);
}
</style>
