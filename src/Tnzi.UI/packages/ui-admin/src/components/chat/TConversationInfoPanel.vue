<template>
  <div class="t-conv-info">
    <div v-if="detail" class="t-conv-info__inner">
      <!-- ── GROUP: member filter + member grid ──────────────────────────── -->
      <div v-if="isGroup" class="t-conv-info__block">
        <div class="t-conv-info__filter" :class="{ 't-conv-info__filter--focused': filterFocused }">
          <Icon icon="mdi:magnify" :width="15" class="t-conv-info__filter-icon" />
          <input
            v-model="memberFilter"
            class="t-conv-info__filter-input"
            :placeholder="t('window.searchMembers')"
            enterkeyhint="search"
            autocapitalize="off"
            autocorrect="off"
            @focus="filterFocused = true"
            @blur="filterFocused = false"
          />
          <button v-if="memberFilter" class="t-conv-info__filter-clear" tabindex="-1" @click="memberFilter = ''">
            <Icon icon="mdi:close-circle" :width="13" />
          </button>
        </div>
        <TMemberGrid
          :members="filteredMembers"
          :can-add="isOwner"
          :can-remove="isOwner"
          :my-id="myId"
          @add="openAddMembers"
          @message="onMessageMember"
          @remove="onRemoveMember"
        />
      </div>

      <!-- ── DIRECT: peer + add (→ create group) ──────────────────────────── -->
      <div v-else class="t-conv-info__block">
        <TMemberGrid
          :members="detail.members"
          :can-add="true"
          @add="openCreateGroup"
          @message="onMessageMember"
        />
      </div>

      <!-- ── Editable fields (hover-edit, equal-height, clearable) ────────── -->
      <div class="t-conv-info__fields">
        <TInfoField
          v-if="isGroup"
          :label="t('window.groupName')"
          :value="detail.title"
          :editable="isOwner"
          :placeholder="t('window.notSet')"
          :loading="renaming"
          @save="onRename"
        />
        <TInfoField
          v-if="isGroup"
          :label="t('window.notice')"
          :value="detail.notice ?? ''"
          :editable="isOwner"
          multiline
          :placeholder="t('window.notSetByOwner')"
          :loading="savingNotice"
          @save="onSaveNotice"
        />
        <TInfoField
          v-if="isGroup"
          :label="t('window.myAlias')"
          :value="detail.myAlias ?? ''"
          editable
          :placeholder="t('window.notSet')"
          :loading="savingAlias"
          @save="onSaveAlias"
        />
        <TInfoField
          :label="t('window.remark')"
          :value="detail.myRemark ?? ''"
          editable
          :placeholder="t('window.notSet')"
          :loading="savingRemark"
          @save="onSaveRemark"
        />
      </div>

      <!-- ── Action rows (search / mute / sticky) ─────────────────────────── -->
      <div class="t-conv-info__rows">
        <div class="t-conv-info__row t-conv-info__row--nav" @click="searchHistoryOpen = true">
          <span class="t-conv-info__row-label">{{ t('window.searchHistory') }}</span>
          <Icon icon="mdi:chevron-right" :width="18" class="t-conv-info__row-chevron" />
        </div>
        <div class="t-conv-info__row">
          <span class="t-conv-info__row-label">{{ t('window.mute') }}</span>
          <NSwitch size="small" :value="detail.isMuted" @update:value="onToggleMute" />
        </div>
        <div class="t-conv-info__row">
          <span class="t-conv-info__row-label">{{ t('window.sticky') }}</span>
          <NSwitch size="small" :value="detail.isSticky" @update:value="onToggleSticky" />
        </div>
      </div>

      <!-- ── Danger zone ──────────────────────────────────────────────────── -->
      <!-- placement="top-end": the danger rows sit at the right edge of the
           narrow info panel, so a centered popconfirm overflows the window's
           right edge and gets clipped. Right-aligning it makes the (wider)
           confirm bubble extend leftward into the message area instead. -->
      <div class="t-conv-info__danger">
        <NPopconfirm :z-index="POPOVER_Z" placement="top-end" :style="CONFIRM_STYLE" @positive-click="onClearHistory">
          <template #trigger>
            <button class="t-conv-info__danger-btn">{{ t('window.clearHistory') }}</button>
          </template>
          {{ t('window.clearConfirm') }}
        </NPopconfirm>

        <template v-if="isGroup">
          <NPopconfirm v-if="isOwner" :z-index="POPOVER_Z" placement="top-end" :style="CONFIRM_STYLE" @positive-click="onDissolve">
            <template #trigger>
              <button class="t-conv-info__danger-btn">{{ t('window.dissolveGroup') }}</button>
            </template>
            {{ t('window.dissolveConfirm') }}
          </NPopconfirm>
          <NPopconfirm v-else :z-index="POPOVER_Z" placement="top-end" :style="CONFIRM_STYLE" @positive-click="onLeave">
            <template #trigger>
              <button class="t-conv-info__danger-btn">{{ t('window.leaveGroup') }}</button>
            </template>
            {{ t('window.leaveConfirm') }}
          </NPopconfirm>
        </template>
      </div>
    </div>

    <div v-else-if="loading" class="t-conv-info__loading">{{ t('window.loading') }}</div>

    <!-- ── Dialogs (teleport to body; escape the 250px panel) ──────────────── -->
    <TMemberPickerDialog
      v-model:show="pickerOpen"
      :title="pickerMode === 'add' ? t('window.addMember') : t('window.createGroup')"
      :confirm-label="pickerMode === 'add' ? t('window.add') : t('window.createGroup')"
      :exclude-ids="pickerExcludeIds"
      :loading="pickerLoading"
      @confirm="onPickerConfirm"
    />
    <TSearchHistoryDialog
      v-model:show="searchHistoryOpen"
      :conversation-id="conversationId"
      @jump="onJump"
    />
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { Icon } from '@iconify/vue'
import { NPopconfirm, NSwitch } from 'naive-ui'
import { MemberRole, ConversationType } from '@tnzi/core/services/chat'
import type { ConversationDto, ChatContactDto } from '@tnzi/core/services/chat'
import { useChatStore } from '../../stores/useChatStore'
import { translatePageKey } from '../../pages/_shared/translate'
import TMemberGrid from './TMemberGrid.vue'
import TInfoField from './TInfoField.vue'
import TMemberPickerDialog from './TMemberPickerDialog.vue'
import TSearchHistoryDialog from './TSearchHistoryDialog.vue'

const props = defineProps<{
  show: boolean
  conversationId: string | null
  myId?: string
}>()

const emit = defineEmits<{
  'update:show': [v: boolean]
  changed: []
  'open-conversation': [id: string]
}>()

const t = (k: string) => translatePageKey('chat', k)
const store = useChatStore()

// Popovers/popconfirms must clear the chat NModal - give them a high z-index.
const POPOVER_Z = 3000
// Cap the confirm bubble width so a long sentence wraps to a compact box instead
// of stretching wide (which, at the panel's right edge, would clip off-screen).
const CONFIRM_STYLE = { maxWidth: '240px' }

const detail = ref<ConversationDto | null>(null)
const loading = ref(false)

// Per-action loading flags
const renaming = ref(false)
const savingNotice = ref(false)
const savingAlias = ref(false)
const savingRemark = ref(false)

// Group member filter (top search bar filters the member grid)
const memberFilter = ref('')
const filterFocused = ref(false)

// Dialog state
const pickerOpen = ref(false)
const pickerMode = ref<'add' | 'create-group'>('add')
const pickerLoading = ref(false)
const searchHistoryOpen = ref(false)

const isGroup = computed(() => detail.value?.type === ConversationType.Group)

const isOwner = computed(() => {
  if (!detail.value || !props.myId) return false
  return detail.value.members.find((m) => m.userId === props.myId)?.role === MemberRole.Owner
})

const filteredMembers = computed(() => {
  const list = detail.value?.members ?? []
  const kw = memberFilter.value.trim().toLowerCase()
  if (!kw) return list
  return list.filter((m) => (m.alias || m.name || '').toLowerCase().includes(kw))
})

const peerUserId = computed(() =>
  detail.value?.members.find((m) => m.userId !== props.myId)?.userId ?? null,
)

const pickerExcludeIds = computed(() => {
  if (pickerMode.value === 'add') return detail.value?.members.map((m) => m.userId) ?? []
  // create-group: exclude self + the direct peer (already in the new group)
  return [props.myId, peerUserId.value].filter(Boolean) as string[]
})

async function loadDetail() {
  if (!props.conversationId) return
  loading.value = true
  try {
    detail.value = await store.getConversationDetail(props.conversationId)
  } finally {
    loading.value = false
  }
}

function resetState() {
  detail.value = null
  memberFilter.value = ''
  pickerOpen.value = false
  searchHistoryOpen.value = false
}

watch(
  [() => props.show, () => props.conversationId],
  ([show]) => {
    if (show && props.conversationId) {
      resetState()
      void loadDetail()
    } else if (!show) {
      resetState()
    }
  },
)

async function reload() {
  await loadDetail()
  emit('changed')
}

// ── Editable field saves ─────────────────────────────────────────────────────
async function onRename(value: string) {
  if (!props.conversationId || !value) return // group name can't be blank
  renaming.value = true
  try {
    await store.renameGroup(props.conversationId, value)
    await reload()
  } finally {
    renaming.value = false
  }
}

async function onSaveNotice(value: string) {
  if (!props.conversationId) return
  savingNotice.value = true
  try {
    await store.setNotice(props.conversationId, value || null)
    await reload()
  } finally {
    savingNotice.value = false
  }
}

async function onSaveAlias(value: string) {
  if (!props.conversationId) return
  savingAlias.value = true
  try {
    // Empty string clears (backend: "" = clear, null = don't touch).
    await store.setMemberSettings(props.conversationId, { alias: value })
    await reload()
  } finally {
    savingAlias.value = false
  }
}

async function onSaveRemark(value: string) {
  if (!props.conversationId) return
  savingRemark.value = true
  try {
    await store.setMemberSettings(props.conversationId, { remark: value })
    await reload()
  } finally {
    savingRemark.value = false
  }
}

// ── Member removal (owner-only, confirmed inside the member popover) ─────────
async function onRemoveMember(userId: string) {
  if (!props.conversationId) return
  await store.removeMember(props.conversationId, userId)
  await reload()
}

// ── Toggles (optimistic) ─────────────────────────────────────────────────────
async function onToggleMute(value: boolean) {
  if (!props.conversationId || !detail.value) return
  const prev = detail.value.isMuted
  detail.value.isMuted = value
  try {
    await store.setMemberSettings(props.conversationId, { isMuted: value })
    emit('changed')
  } catch {
    if (detail.value) detail.value.isMuted = prev
  }
}

async function onToggleSticky(value: boolean) {
  if (!props.conversationId || !detail.value) return
  const prev = detail.value.isSticky
  detail.value.isSticky = value
  try {
    await store.setMemberSettings(props.conversationId, { isSticky: value })
    emit('changed')
  } catch {
    if (detail.value) detail.value.isSticky = prev
  }
}

// ── Member picker (add members / create group) ───────────────────────────────
function openAddMembers() {
  pickerMode.value = 'add'
  pickerOpen.value = true
}

function openCreateGroup() {
  pickerMode.value = 'create-group'
  pickerOpen.value = true
}

async function onPickerConfirm(contacts: ChatContactDto[]) {
  if (!props.conversationId || contacts.length === 0) return
  pickerLoading.value = true
  try {
    if (pickerMode.value === 'add') {
      await store.addMembers(props.conversationId, contacts.map((c) => c.userId))
      pickerOpen.value = false
      await reload()
    } else {
      // Create a group from this direct chat: peer + the picked contacts.
      if (!peerUserId.value) return
      const peerName = detail.value?.title ?? ''
      const names = [peerName, ...contacts.map((c) => c.name)].filter(Boolean)
      const title = names.length > 0 ? names.slice(0, 5).join(', ') : 'Group'
      const memberIds = [peerUserId.value, ...contacts.map((c) => c.userId)]
      const cid = await store.createGroup(title, memberIds)
      pickerOpen.value = false
      emit('open-conversation', cid)
      emit('update:show', false)
    }
  } finally {
    pickerLoading.value = false
  }
}

// ── Search history jump (v1: refocus the conversation) ───────────────────────
function onJump() {
  if (props.conversationId) emit('open-conversation', props.conversationId)
  searchHistoryOpen.value = false
}

// ── Member → direct message ──────────────────────────────────────────────────
async function onMessageMember(userId: string) {
  if (userId === props.myId) return
  const cid = await store.startDirect(userId)
  emit('open-conversation', cid)
  emit('update:show', false)
}

// ── Danger zone ──────────────────────────────────────────────────────────────
async function onClearHistory() {
  if (!props.conversationId) return
  await store.clearHistory(props.conversationId)
  await reload()
}

async function onDissolve() {
  if (!props.conversationId) return
  await store.dissolveGroup(props.conversationId)
  emit('changed')
  emit('update:show', false)
}

async function onLeave() {
  if (!props.conversationId) return
  await store.leaveGroup(props.conversationId)
  emit('changed')
  emit('update:show', false)
}

defineExpose({ detail, isOwner, isGroup, loadDetail })
</script>

<style scoped>
.t-conv-info {
  width: 250px;
  height: 100%;
  background: var(--chat-surface, #fff);
  border-left: 1px solid var(--chat-border, #e6e6e6);
  overflow: hidden;
}

/* Phone: the panel slides over the whole conversation body, so its content must
   fill the width (the fixed 250px left the messages bleeding through on the
   right). No left border - there's no adjacent column on a single-column phone. */
@media (max-width: 768px) {
  .t-conv-info {
    width: 100%;
    border-left: none;
  }
}

.t-conv-info__inner {
  height: 100%;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
}

/* ── Block (members) ─────────────────────────────────────────────────────── */
.t-conv-info__block {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 14px 14px 12px;
}

/* ── Member filter (top search) ──────────────────────────────────────────── */
.t-conv-info__filter {
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

.t-conv-info__filter--focused {
  background: var(--chat-surface, #fff);
  border-color: var(--chat-border, #dcdcdc);
}

.t-conv-info__filter-icon {
  flex-shrink: 0;
  color: var(--chat-text-3, #9b9b9b);
}

.t-conv-info__filter-input {
  flex: 1;
  min-width: 0;
  border: none;
  outline: none;
  background: transparent;
  font-size: 13px;
  color: var(--chat-text, #1f1f1f);
  font-family: inherit;
}

.t-conv-info__filter-input::placeholder {
  color: var(--chat-text-3, #a8a8a8);
}

.t-conv-info__filter-clear {
  display: flex;
  align-items: center;
  flex-shrink: 0;
  border: none;
  background: transparent;
  padding: 0;
  cursor: pointer;
  color: var(--chat-text-3, #b0b0b0);
}

.t-conv-info__filter-clear:hover {
  color: var(--chat-text-2, #8a8a8a);
}

/* ── Editable fields ─────────────────────────────────────────────────────── */
.t-conv-info__fields {
  display: flex;
  flex-direction: column;
  border-top: 1px solid var(--chat-border, #e6e6e6);
}

/* ── Action rows ─────────────────────────────────────────────────────────── */
.t-conv-info__rows {
  display: flex;
  flex-direction: column;
}

.t-conv-info__row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  min-height: 44px;
  padding: 0 14px;
  border-bottom: 1px solid var(--chat-border, #e6e6e6);
}

.t-conv-info__row--nav {
  cursor: pointer;
}

.t-conv-info__row--nav:hover {
  background: var(--chat-hover, rgb(51 54 57 / 0.04));
}

.t-conv-info__row-label {
  font-size: 13.5px;
  color: var(--chat-text, #1f1f1f);
}

.t-conv-info__row-chevron {
  color: var(--chat-text-3, #c4c4c4);
  flex-shrink: 0;
}

/* ── Danger zone ─────────────────────────────────────────────────────────── */
.t-conv-info__danger {
  display: flex;
  flex-direction: column;
  padding-top: 4px;
}

.t-conv-info__danger-btn {
  border: none;
  background: transparent;
  cursor: pointer;
  text-align: center;
  min-height: 42px;
  padding: 0 14px;
  font-size: 13.5px;
  color: var(--chat-presence-busy, #e64340);
  transition: background 0.15s;
}

.t-conv-info__danger-btn:hover {
  background: rgb(230 67 64 / 0.06);
}

/* ── Loading ─────────────────────────────────────────────────────────────── */
.t-conv-info__loading {
  padding: 24px;
  text-align: center;
  font-size: 13px;
  color: var(--chat-text-3, #aaa);
}
</style>
