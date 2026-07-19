<template>
  <div class="t-conv-list">
    <div class="t-conv-list__top">
      <TPresencePicker
        v-if="presence !== false && myStatus != null"
        :status="myStatus"
        :name="myName"
        :avatar-file-id="myAvatarFileId"
        :allow-invisible="allowInvisible !== false"
        @change="(s) => emit('set-status', s)"
      />
      <!-- Presence disabled: keep the self avatar (no dot, no status menu). -->
      <TChatAvatar
        v-else-if="myName"
        :name="myName"
        :file-id="myAvatarFileId"
        :size="30"
      />
      <div class="t-conv-list__search" :class="{ 't-conv-list__search--focused': focused }">
        <Icon icon="mdi:magnify" :width="16" class="t-conv-list__search-icon" />
        <input
          v-model="keyword"
          class="t-conv-list__search-input"
          :placeholder="t('window.search')"
          enterkeyhint="search"
          autocapitalize="off"
          autocorrect="off"
          @focus="focused = true"
          @blur="focused = false"
        />
        <button
          v-if="keyword"
          class="t-conv-list__search-clear"
          tabindex="-1"
          @click="keyword = ''"
        >
          <Icon icon="mdi:close-circle" :width="14" />
        </button>
      </div>
      <button class="t-conv-list__add" :title="t('window.newChat')" @click="emit('new-chat')">
        <Icon icon="mdi:plus" :width="18" />
      </button>
      <!-- Window-level controls appended by the host (phone-only close button,
           so the list view keeps the same 52px top line as the pane). -->
      <slot name="actions" />
    </div>
    <NScrollbar class="t-conv-list__scroll">
      <TConversationItem
        v-for="c in ordered"
        :key="c.id"
        :item="c"
        :active="c.id === activeId"
        :presence="presence"
        @select="emit('select', c.id)"
        @context-menu="(e) => onItemContextMenu(e, c)"
      />
      <div v-if="ordered.length === 0" class="t-conv-list__empty">{{ t('window.empty') }}</div>
    </NScrollbar>

    <!-- Right-click quick actions (pin / mute / mark-read / hide / delete).
         Manual x/y dropdown at the cursor; z-index must clear the chat NModal. -->
    <NDropdown
      trigger="manual"
      placement="bottom-start"
      size="small"
      :show="ctxShow"
      :x="ctxX"
      :y="ctxY"
      :options="ctxOptions"
      :z-index="POPOVER_Z"
      @select="onCtxSelect"
      @clickoutside="ctxShow = false"
    />

  </div>
</template>

<script setup lang="ts">
import { ref, computed, h } from 'vue'
import { NScrollbar, NDropdown, useDialog } from 'naive-ui'
import type { DropdownOption, DropdownDividerOption } from 'naive-ui'
import { Icon } from '@iconify/vue'
import type { ConversationListItemDto } from '@tnzi/core/services/chat'
import { ConversationType, UserPresenceStatus } from '@tnzi/core/services/chat'
import TConversationItem from './TConversationItem.vue'
import TPresencePicker from './TPresencePicker.vue'
import TChatAvatar from './TChatAvatar.vue'
import { translatePageKey } from '../../pages/_shared/translate'

const props = defineProps<{
  conversations: ConversationListItemDto[]
  activeId: string | null
  myStatus?: UserPresenceStatus
  myName?: string
  myAvatarFileId?: string
  /** Deployment presence toggle — false hides status dots and the status picker. */
  presence?: boolean
  /** Deployment invisible toggle — false drops "Invisible" from the status picker. */
  allowInvisible?: boolean
}>()

const emit = defineEmits<{
  select: [id: string]
  'new-chat': []
  'set-status': [UserPresenceStatus]
  'set-sticky': [id: string, sticky: boolean]
  'set-muted': [id: string, muted: boolean]
  'mark-read': [id: string]
  hide: [id: string]
  delete: [id: string]
}>()

const t = (k: string) => translatePageKey('chat', k)

const keyword = ref('')
const focused = ref(false)
defineExpose({ keyword })

// ── Right-click context menu (quick actions) ────────────────────────────────
// The dropdown must clear the chat NModal, same as every popover in the window.
const POPOVER_Z = 3000
const ctxShow = ref(false)
const ctxX = ref(0)
const ctxY = ref(0)
const ctxItem = ref<ConversationListItemDto | null>(null)

function onItemContextMenu(e: MouseEvent, c: ConversationListItemDto) {
  ctxItem.value = c
  ctxX.value = e.clientX
  ctxY.value = e.clientY
  ctxShow.value = true
}

// Menu items reuse the info panel's exact wording (Sticky on Top / Mute
// Notifications); the currently-active toggle carries a leading checkmark.
// Inactive items get a same-width blank so labels stay aligned.
const checkIcon = () => h(Icon, { icon: 'mdi:check', width: 15 })
const blankIcon = () => h('span', { style: 'display:inline-block;width:15px' })

const ctxOptions = computed<(DropdownOption | DropdownDividerOption)[]>(() => {
  const c = ctxItem.value
  if (!c) return []
  const options: (DropdownOption | DropdownDividerOption)[] = []
  // System notifications are plain announcements: no sticky / no mute (they
  // follow the ordinary sort rules and always notify).
  if (c.type !== ConversationType.System) {
    options.push(
      { key: 'sticky', label: t('window.sticky'), icon: c.isSticky ? checkIcon : blankIcon },
      { key: 'mute', label: t('window.mute'), icon: c.isMuted ? checkIcon : blankIcon },
    )
  }
  if (c.unreadCount > 0) options.push({ key: 'mark-read', label: t('window.ctxMarkRead'), icon: blankIcon })
  options.push(
    { key: 'hide', label: t('window.ctxHide'), icon: blankIcon },
    { key: 'ctx-divider', type: 'divider' },
    // Destructive: red label; selection opens a confirm dialog first.
    { key: 'delete', label: t('window.ctxDelete'), icon: blankIcon, props: { style: { color: 'var(--chat-danger, #e64340)' } } },
  )
  return options
})

// Standard confirm dialog (same chrome as the admin Log out confirm). Resolved
// defensively so the component still mounts in unit tests without an
// <n-dialog-provider>; the admin shell always provides one in the real app.
let dialog: ReturnType<typeof useDialog> | null = null
try {
  dialog = useDialog()
} catch {
  dialog = null
}

function confirmDelete(id: string) {
  if (!dialog) {
    // No dialog provider (bare/test mounts): fall back to the native confirm
    // rather than silently skipping the destructive-action gate.
    if (globalThis.confirm?.(t('window.deleteConversationConfirm'))) emit('delete', id)
    return
  }
  dialog.error({
    title: t('window.ctxDelete'),
    content: t('window.deleteConversationConfirm'),
    positiveText: t('window.ctxDelete'),
    negativeText: t('window.cancel'),
    onPositiveClick: () => {
      emit('delete', id)
    },
  })
}

function onCtxSelect(key: string | number) {
  ctxShow.value = false
  const c = ctxItem.value
  if (!c) return
  if (key === 'sticky') emit('set-sticky', c.id, !c.isSticky)
  else if (key === 'mute') emit('set-muted', c.id, !c.isMuted)
  else if (key === 'mark-read') emit('mark-read', c.id)
  else if (key === 'hide') emit('hide', c.id)
  else if (key === 'delete') confirmDelete(c.id)
}

const ordered = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  const filtered = kw
    ? props.conversations.filter((c) => (c.title ?? '').toLowerCase().includes(kw))
    : props.conversations

  // System conversations sort by the SAME rules as everything else (no forced
  // pin): sticky first, then latest activity.
  return [...filtered].sort((a, b) => {
    if (a.isSticky !== b.isSticky) return a.isSticky ? -1 : 1
    return new Date(b.lastMessageAt ?? 0).getTime() - new Date(a.lastMessageAt ?? 0).getTime()
  })
})
</script>

<style scoped>
.t-conv-list {
  display: flex;
  flex-direction: column;
  height: 100%;
  background: var(--chat-list-bg, #fafafa);
  border-right: 1px solid var(--chat-border, #eaeaea);
}

.t-conv-list__top {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 12px 12px 10px;
  flex-shrink: 0;
}

/* ── Search (custom, neutral focus — no themed ring) ─────────────────────── */
.t-conv-list__search {
  flex: 1;
  min-width: 0;
  display: flex;
  align-items: center;
  gap: 5px;
  height: 30px;
  padding: 0 8px;
  border-radius: 6px;
  background: var(--chat-search-bg, #e9e9e9);
  border: 1px solid transparent;
  transition: background 0.12s, border-color 0.12s;
}

.t-conv-list__search--focused {
  background: var(--chat-surface, #fff);
  border-color: var(--chat-border, #dcdcdc);
}

.t-conv-list__search-icon {
  flex-shrink: 0;
  color: var(--chat-text-3, #9b9b9b);
}

.t-conv-list__search-input {
  flex: 1;
  min-width: 0;
  border: none;
  outline: none;
  background: transparent;
  font-size: 13px;
  color: var(--chat-text, #1f1f1f);
  font-family: inherit;
}

.t-conv-list__search-input::placeholder {
  color: var(--chat-text-3, #a8a8a8);
}

.t-conv-list__search-clear {
  display: flex;
  align-items: center;
  flex-shrink: 0;
  border: none;
  background: transparent;
  padding: 0;
  cursor: pointer;
  color: var(--chat-text-3, #b0b0b0);
}

.t-conv-list__search-clear:hover {
  color: var(--chat-text-2, #8a8a8a);
}

/* ── Add button ─────────────────────────────────────────────────────────── */
.t-conv-list__add {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 30px;
  height: 30px;
  border: none;
  border-radius: 6px;
  background: var(--chat-search-bg, #e9e9e9);
  cursor: pointer;
  color: var(--chat-text-2, #5a5a5a);
  transition: background 0.12s, color 0.12s;
}

.t-conv-list__add:hover {
  background: var(--chat-hover, #e0e0e0);
  color: var(--chat-text, #1f1f1f);
}

.t-conv-list__scroll {
  flex: 1;
  min-height: 0;
}

.t-conv-list__empty {
  padding: 40px 16px;
  text-align: center;
  font-size: 13px;
  color: var(--chat-text-3, #b0b0b0);
}
</style>
