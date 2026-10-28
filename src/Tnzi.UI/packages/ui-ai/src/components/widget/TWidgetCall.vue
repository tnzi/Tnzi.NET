<script setup lang="ts">
/**
 * @experimental
 * TWidgetCall - a `show_widget` call shown as what it made: the widget, live, inline in the reply.
 *
 * While the call is still being made it is a placeholder (never half a document); once it has
 * completed with a usable document it is the widget (`TWidgetFrame`) under a quiet bar with its
 * title and three actions: open it larger, copy its HTML, download it. A call that failed, or that
 * ended without a usable document, is the ordinary tool card (`TToolCallCard`).
 *
 * It is drawn from the stored call (its arguments carry the document), so a widget survives a
 * reload and history paging. Register it with `createWidgetToolRenderers({ runnerUrl })`.
 */
import { computed, ref } from 'vue'
import { Icon } from '@iconify/vue'
import { useFocusTrap } from '@tnzi/ui'
import { useAiI18n } from '../../i18n/index'
import type { ToolCallInfo } from '../../headless/useChat'
import { useBodyScrollLock } from '../../headless/useBodyScrollLock'
import TToolCallCard from '../reasoning/TToolCallCard.vue'
import TStreamLoader from '../streaming/TStreamLoader.vue'
import { toolCallState } from '../reasoning/tool-call-status'
import TWidgetFrame from './TWidgetFrame.vue'
import type { WidgetHostHandlers } from './widget-host'
import { readWidgetTheme } from './widget-theme'
import { standaloneWidgetDocument } from './widget-document'
import { parseWidgetInput, widgetFileName } from './widget-tool'

const props = withDefaults(
  defineProps<{
    toolCall: ToolCallInfo
    /** The message is still streaming. */
    streaming?: boolean
    /** The widget runner page (see `TWidgetFrame`). */
    runnerUrl: string
    /** The tallest an inline widget grows, in pixels. */
    maxHeight?: number
    /** What widgets may do beyond showing themselves. */
    handlers?: WidgetHostHandlers
  }>(),
  { streaming: false, maxHeight: 640, handlers: undefined },
)

const t = useAiI18n()

const state = computed(() => toolCallState(props.toolCall, props.streaming))
const input = computed(() => parseWidgetInput(props.toolCall.input))
/** The title while the call is still being made, when its arguments already say it. */
const pendingTitle = computed(() => {
  const value = props.toolCall.input
  if (value && typeof value === 'object' && !Array.isArray(value)) {
    const title = (value as Record<string, unknown>).title
    if (typeof title === 'string' && title.trim()) return title.trim()
  }
  return null
})
const view = computed<'building' | 'widget' | 'card'>(() => {
  if (state.value === 'running') return 'building'
  if (state.value === 'completed' && input.value) return 'widget'
  return 'card'
})

const root = ref<HTMLElement | null>(null)
const expanded = ref(false)
const dialog = ref<HTMLElement | null>(null)
const closeButton = ref<HTMLButtonElement | null>(null)
const copied = ref(false)
let copiedTimer: ReturnType<typeof setTimeout> | null = null

useFocusTrap(dialog, () => expanded.value, {
  onEscape: () => close(),
  initialFocus: () => closeButton.value,
})
useBodyScrollLock(() => expanded.value)

function open(): void {
  expanded.value = true
}

function close(): void {
  expanded.value = false
}

async function copySource(): Promise<void> {
  const html = input.value?.html
  if (!html || typeof navigator === 'undefined' || !navigator.clipboard) return
  try {
    await navigator.clipboard.writeText(html)
    copied.value = true
    if (copiedTimer) clearTimeout(copiedTimer)
    copiedTimer = setTimeout(() => (copied.value = false), 1600)
  } catch {
    // The page may not be allowed the clipboard; the source is still one download away.
  }
}

function download(): void {
  const widget = input.value
  if (!widget) return
  const document = standaloneWidgetDocument(widget.html, readWidgetTheme(root.value))
  const url = URL.createObjectURL(new Blob([document], { type: 'text/html;charset=utf-8' }))
  const link = window.document.createElement('a')
  link.href = url
  link.download = widgetFileName(widget.title)
  link.rel = 'noopener'
  link.click()
  setTimeout(() => URL.revokeObjectURL(url), 0)
}
</script>

<template>
  <div v-if="view === 'building'" class="t-widget t-widget--building" role="status">
    <TStreamLoader :size="14" />
    <span class="t-widget__building-label">{{ t.widget.building }}</span>
    <span v-if="pendingTitle" class="t-widget__building-title">{{ pendingTitle }}</span>
  </div>

  <figure v-else-if="view === 'widget' && input" ref="root" class="t-widget">
    <figcaption class="t-widget__bar">
      <span class="t-widget__title" :title="input.title">{{ input.title }}</span>
      <span class="t-widget__actions">
        <button type="button" class="t-widget__action" :title="t.widget.expand" :aria-label="t.widget.expand" @click="open">
          <Icon icon="lucide:maximize-2" aria-hidden="true" />
        </button>
        <button
          type="button"
          class="t-widget__action"
          :title="copied ? t.widget.copied : t.widget.copySource"
          :aria-label="copied ? t.widget.copied : t.widget.copySource"
          @click="copySource"
        >
          <Icon :icon="copied ? 'lucide:check' : 'lucide:copy'" aria-hidden="true" />
        </button>
        <button type="button" class="t-widget__action" :title="t.widget.download" :aria-label="t.widget.download" @click="download">
          <Icon icon="lucide:download" aria-hidden="true" />
        </button>
      </span>
    </figcaption>
    <TWidgetFrame :runner-url="runnerUrl" :html="input.html" :title="input.title" :max-height="maxHeight" :handlers="handlers" />

    <Teleport to="body">
      <div v-if="expanded" class="t-widget-overlay t-ai-scroll-scope" @click.self="close">
        <div ref="dialog" class="t-widget-overlay__dialog" role="dialog" aria-modal="true" :aria-label="input.title">
          <div class="t-widget-overlay__bar">
            <span class="t-widget__title">{{ input.title }}</span>
            <button ref="closeButton" type="button" class="t-widget__action" :title="t.widget.close" :aria-label="t.widget.close" @click="close">
              <Icon icon="lucide:x" aria-hidden="true" />
            </button>
          </div>
          <div class="t-widget-overlay__body">
            <TWidgetFrame :runner-url="runnerUrl" :html="input.html" :title="input.title" fill :lazy="false" :handlers="handlers" />
          </div>
        </div>
      </div>
    </Teleport>
  </figure>

  <TToolCallCard v-else :tool-call="toolCall" :streaming="streaming" />
</template>

<style scoped>
.t-widget {
  display: flex;
  flex-direction: column;
  width: 100%;
  min-width: 0;
  margin: 0;
  border-radius: 12px;
}
.t-widget--building {
  flex-direction: row;
  align-items: center;
  gap: 8px;
  min-height: 96px;
  padding: 12px 14px;
  border: 1px dashed var(--tnzi-ai-border-strong);
  color: var(--tnzi-ai-text-secondary);
  font-size: 14px;
}
.t-widget__building-title {
  min-width: 0;
  overflow: hidden;
  color: var(--tnzi-ai-text);
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-widget__bar,
.t-widget-overlay__bar {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
  min-height: 28px;
}
.t-widget__title {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  color: var(--tnzi-ai-text-secondary);
  font-size: 13px;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-widget__actions {
  display: flex;
  flex: none;
  gap: 2px;
  opacity: 0.6;
  transition: opacity var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-widget:hover .t-widget__actions,
.t-widget__actions:focus-within {
  opacity: 1;
}
@media (hover: none) {
  .t-widget__actions {
    opacity: 1;
  }
}
.t-widget__action {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  padding: 0;
  border: 0;
  border-radius: 8px;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  font-size: 15px;
  cursor: pointer;
}
.t-widget__action:hover {
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text);
}
.t-widget__action:focus-visible {
  outline: 2px solid var(--tnzi-ai-accent);
  outline-offset: 1px;
}
.t-widget-overlay {
  position: fixed;
  inset: 0;
  z-index: 2000;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: max(12px, env(safe-area-inset-top)) 12px max(12px, env(safe-area-inset-bottom));
  background: var(--tnzi-ai-backdrop);
  backdrop-filter: blur(var(--tnzi-ai-backdrop-blur));
}
.t-widget-overlay__dialog {
  display: flex;
  flex-direction: column;
  gap: 8px;
  width: min(1200px, 100%);
  height: min(900px, 100%);
  padding: 10px 12px 12px;
  border: var(--tnzi-ai-modal-border);
  border-radius: var(--tnzi-ai-modal-radius);
  background: var(--tnzi-ai-bg);
  color: var(--tnzi-ai-text);
}
.t-widget-overlay__body {
  flex: 1;
  min-height: 0;
}
</style>
