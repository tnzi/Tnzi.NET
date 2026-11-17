<script setup lang="ts">
/**
 * @experimental
 * TRunImages - the images a run's steps returned, as small thumbnails at the end of the run's line, so they show
 * without opening the run.
 *
 * Hovering a thumbnail (or giving it keyboard focus) shows a larger preview beside it that says which step it came
 * from (its words as the run lists it, "Read a file") and which of the run's images it is ("2 of 2"). Clicking (Enter or Space) opens the full-size viewer
 * (`TImageViewer`) on that image, with the run's other images one arrow key away; Escape closes it. On a touch
 * screen a tap opens the viewer at once (there is no hover there).
 *
 * The images are stored attachments, read through the render context (`resolveAttachmentUrl`, which
 * `useChatThreads` provides through `TChatApp`); one that cannot be read keeps an empty frame.
 */
import { computed, defineAsyncComponent, nextTick, onBeforeUnmount, reactive, ref, watch } from 'vue'
import { formatAiMessage, useAiI18n } from '../../i18n/index'
import { placeBeside } from '../../headless/place-beside'
import { useChatRenderContext } from '../chat/chat-render-context'
import type { ViewerImage } from '../chat/image-group'
import type { RunImage } from './run-extras'

const TImageViewer = defineAsyncComponent(() => import('../overlay/TImageViewer.vue'))

const props = defineProps<{
  images: readonly RunImage[]
}>()

const t = useAiI18n()
const context = useChatRenderContext()

/* Attachment id -> object URL (null: cannot be read). The resolver owns the URLs. */
const urls = reactive(new Map<string, string | null>())
watch(
  () => [props.images.map((image) => image.attachmentId), context.value.resolveAttachmentUrl] as const,
  ([ids, resolve]) => {
    for (const id of ids) {
      if (urls.has(id)) continue
      if (!resolve) {
        urls.set(id, null)
        continue
      }
      urls.set(id, null)
      void Promise.resolve()
        .then(() => resolve(id))
        .then((url) => urls.set(id, url ?? null), () => urls.set(id, null))
    }
  },
  { immediate: true },
)

const countOf = (index: number): string => formatAiMessage(t.value.tasks.imageCount, { index: index + 1, count: props.images.length })
const labelOf = (index: number): string => `${props.images[index]?.source ?? ''} · ${countOf(index)}`

/* The hover preview: one at a time, placed above the thumbnail (below when there is no room). */
const PREVIEW_WIDTH = 240
const PREVIEW_HEIGHT = 180
const preview = ref<{ index: number; top: number; left: number } | null>(null)
let hideTimer: ReturnType<typeof setTimeout> | undefined

function showPreview(index: number, element: HTMLElement): void {
  if (hideTimer) clearTimeout(hideTimer)
  const rect = element.getBoundingClientRect()
  const place = placeBeside(
    { top: rect.top, left: rect.left, width: rect.width, height: rect.height },
    { width: PREVIEW_WIDTH, height: PREVIEW_HEIGHT },
    { width: window.innerWidth, height: window.innerHeight },
    { side: 'top', align: 'end', gap: 8, edge: 8 },
  )
  preview.value = { index, top: place.top, left: place.left }
}

function hidePreview(): void {
  if (hideTimer) clearTimeout(hideTimer)
  hideTimer = setTimeout(() => (preview.value = null), 80)
}

function onPointerEnter(event: PointerEvent, index: number): void {
  if (event.pointerType === 'mouse' || event.pointerType === 'pen') showPreview(index, event.currentTarget as HTMLElement)
}

function onFocus(event: FocusEvent, index: number): void {
  const element = event.currentTarget as HTMLElement
  if (element.matches(':focus-visible')) showPreview(index, element)
}

/* The full-size viewer, fetched on the first open. */
const viewing = ref<number | null>(null)
const viewerMounted = ref(false)
const set = computed<ViewerImage[]>(() =>
  props.images.map((image, index) => ({ src: urls.get(image.attachmentId) ?? null, alt: labelOf(index) })),
)

async function open(index: number): Promise<void> {
  preview.value = null
  viewerMounted.value = true
  await nextTick()
  viewing.value = index
}

onBeforeUnmount(() => {
  if (hideTimer) clearTimeout(hideTimer)
})
</script>

<template>
  <span class="t-run-images" role="group" :aria-label="t.tasks.images">
    <button
      v-for="(image, index) in images"
      :key="`${image.attachmentId}:${index}`"
      type="button"
      class="t-run-images__thumb"
      :aria-label="labelOf(index)"
      data-run-image
      @pointerenter="onPointerEnter($event, index)"
      @pointerleave="hidePreview"
      @focus="onFocus($event, index)"
      @blur="hidePreview"
      @click="open(index)"
    >
      <img v-if="urls.get(image.attachmentId)" :src="urls.get(image.attachmentId)!" alt="" draggable="false" />
    </button>
    <Teleport to="body">
      <div
        v-if="preview"
        class="t-run-images__preview t-ai-scroll-scope"
        :style="{ top: `${preview.top}px`, left: `${preview.left}px` }"
        aria-hidden="true"
        data-run-image-preview
      >
        <img v-if="set[preview.index]?.src" :src="set[preview.index]!.src!" alt="" />
        <span class="t-run-images__caption">
          <span class="t-run-images__source">{{ images[preview.index]?.source }}</span>
          <span>{{ countOf(preview.index) }}</span>
        </span>
      </div>
    </Teleport>
    <TImageViewer v-if="viewerMounted" v-model:index="viewing" :images="set" />
  </span>
</template>

<style scoped>
.t-run-images {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  min-width: 0;
}
.t-run-images__thumb {
  flex-shrink: 0;
  display: block;
  width: 36px;
  height: 22px;
  margin: 0;
  padding: 0;
  overflow: hidden;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 4px;
  background: var(--tnzi-ai-surface);
  cursor: zoom-in;
}
.t-run-images__thumb:hover { border-color: var(--tnzi-ai-border-strong); }
.t-run-images__thumb:focus-visible { outline: var(--tnzi-ai-focus-outline); outline-offset: 1px; }
.t-run-images__thumb img {
  display: block;
  width: 100%;
  height: 100%;
  object-fit: cover;
}
/* Teleported to the body: the preview is above the page's content but below dialogs. */
.t-run-images__preview {
  position: fixed;
  z-index: var(--tnzi-ai-z-popover);
  box-sizing: border-box;
  display: flex;
  flex-direction: column;
  gap: 6px;
  width: 240px;
  max-height: 180px;
  padding: 6px;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 10px;
  background: var(--tnzi-ai-bg);
  box-shadow: var(--tnzi-ai-popover-shadow);
  pointer-events: none;
}
.t-run-images__preview img {
  display: block;
  width: 100%;
  min-height: 0;
  flex: 1 1 auto;
  object-fit: contain;
  border-radius: 6px;
  background: var(--tnzi-ai-surface);
}
.t-run-images__caption {
  display: flex;
  justify-content: space-between;
  gap: 8px;
  color: var(--tnzi-ai-text-secondary);
  font-size: 12px;
  line-height: 16px;
}
.t-run-images__source { color: var(--tnzi-ai-text); font-weight: 600; }
</style>
