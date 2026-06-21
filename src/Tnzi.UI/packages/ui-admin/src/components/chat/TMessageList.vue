<template>
  <NScrollbar ref="scrollbarRef" class="t-msg-list" @scroll="onScroll">
    <!-- NImageGroup turns every image bubble in the thread into one preview
         group, so the lightbox can page prev/next across all of them. -->
    <NImageGroup>
      <div ref="innerRef" class="t-msg-list__inner">
        <template v-for="row in rows" :key="row.key">
          <div v-if="row.type === 'sep'" class="t-msg-list__sep">
            <span class="t-msg-list__sep-label">{{ row.label }}</span>
          </div>
          <TMessageBubble
            v-else
            :message="row.msg"
            :mine="isMine(row.msg)"
            :show-sender="isGroup"
            :my-name="myName"
            :my-avatar-file-id="myAvatarFileId"
          />
        </template>
      </div>
    </NImageGroup>
  </NScrollbar>
</template>

<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted, computed, nextTick } from 'vue'
import { NScrollbar, NImageGroup } from 'naive-ui'
import { MessageContentType } from '@tnzi/core/services/chat'
import type { ChatMessageDto } from '@tnzi/core/services/chat'
import TMessageBubble from './TMessageBubble.vue'
import { translatePageKey } from '../../pages/_shared/translate'
import { formatMessageSeparator, shouldShowSeparator } from './time'

const props = defineProps<{
  messages: ChatMessageDto[]
  myId?: string
  myName?: string
  myAvatarFileId?: string | null
  isGroup: boolean
}>()

const scrollbarRef = ref<InstanceType<typeof NScrollbar> | null>(null)
const yesterdayLabel = () => translatePageKey('chat', 'window.yesterday')

type Row =
  | { type: 'sep'; key: string; label: string }
  | { type: 'msg'; key: string; msg: ChatMessageDto }

const rows = computed<Row[]>(() => {
  const out: Row[] = []
  let prevTime: string | null = null
  for (const msg of props.messages) {
    if (shouldShowSeparator(prevTime, msg.sentAt)) {
      out.push({ type: 'sep', key: `sep-${msg.id}`, label: formatMessageSeparator(msg.sentAt, yesterdayLabel()) })
    }
    out.push({ type: 'msg', key: msg.id, msg })
    prevTime = msg.sentAt
  }
  return out
})

function isMine(msg: ChatMessageDto): boolean {
  if (msg.contentType === MessageContentType.System) return false
  if (!msg.senderId || !props.myId) return false
  return msg.senderId === props.myId
}

function scrollToBottom() {
  const inst = scrollbarRef.value as
    | { scrollTo?: (o: { top: number }) => void; $el?: HTMLElement }
    | null
  if (!inst || typeof inst.scrollTo !== 'function') return
  // Prefer the real content height over a magic number so long threads reach the bottom.
  const el = inst.$el?.querySelector?.('.t-msg-list__inner') as HTMLElement | undefined
  inst.scrollTo({ top: el ? el.scrollHeight + 9999 : 9_999_999 })
}

// Track whether the user is pinned to the bottom. We only auto-scroll on content
// growth (new message / an image finishing load) when they already were — so
// reading scrolled-up history is never yanked away.
const innerRef = ref<HTMLElement | null>(null)
const atBottom = ref(true)

function onScroll(e: Event) {
  const el = e.target as HTMLElement
  if (!el) return
  atBottom.value = el.scrollHeight - el.scrollTop - el.clientHeight < 120
}

let ro: ResizeObserver | null = null

onMounted(() => {
  void nextTick(scrollToBottom)
  // Images have no height until they load, so the length-watch scroll above fires
  // too early for them. A ResizeObserver on the content re-pins to the bottom once
  // the image (or any late-growing content) settles — but only if we were at the
  // bottom, so it doesn't fight a user reading history.
  if (innerRef.value && typeof ResizeObserver !== 'undefined') {
    ro = new ResizeObserver(() => {
      if (atBottom.value) void nextTick(scrollToBottom)
    })
    ro.observe(innerRef.value)
  }
})

onUnmounted(() => {
  ro?.disconnect()
  ro = null
})

watch(
  () => props.messages.length,
  () => {
    // A brand-new message means the user (almost certainly the sender, or someone
    // watching) wants to see it — pin to bottom and let the ResizeObserver keep it
    // there as media loads.
    atBottom.value = true
    void nextTick(scrollToBottom)
  },
)
</script>

<style scoped>
.t-msg-list {
  flex: 1;
  min-height: 0;
}

.t-msg-list__inner {
  display: flex;
  flex-direction: column;
  padding: 14px 0 16px;
  gap: 3px;
}

.t-msg-list__sep {
  display: flex;
  justify-content: center;
  margin: 12px 0 6px;
}

.t-msg-list__sep-label {
  font-size: 12px;
  color: var(--chat-text-3, #b0b0b0);
  background: var(--chat-system-bg, rgba(0, 0, 0, 0.035));
  border-radius: 5px;
  padding: 2px 8px;
}
</style>
