<template>
  <div class="t-conv-info">
    <div v-if="detail" class="t-conv-info__inner">
      <!-- Common header: avatar + title (compact) -->
      <div class="t-conv-info__head">
        <TChatAvatar
          :name="detail.title"
          :file-id="detail.avatarFileId"
          :seed="detail.id"
          :size="44"
        />
        <div class="t-conv-info__head-text">
          <span class="t-conv-info__title">{{ detail.title }}</span>
          <span v-if="isGroup" class="t-conv-info__subtitle">
            {{ detail.members.length }} {{ t('window.members') }}
          </span>
        </div>
      </div>

      <!-- ── DIRECT: create group ────────────────────────────────────── -->
      <div v-if="!isGroup" class="t-conv-info__block">
        <button class="t-conv-info__create-group-btn" @click="createGroupOpen = !createGroupOpen">
          <span class="t-conv-info__create-group-icon">+</span>
          {{ t('window.createGroup') }}
        </button>
        <div v-if="createGroupOpen" class="t-conv-info__add">
          <NInput
            v-model:value="createGroupKeyword"
            size="small"
            :placeholder="t('window.search')"
            clearable
            @input="onSearchCreateGroup"
          />
          <NScrollbar v-if="createGroupCandidates.length > 0" class="t-conv-info__candidates">
            <div
              v-for="c in createGroupCandidates"
              :key="c.userId"
              class="t-conv-info__candidate"
              :class="{ 't-conv-info__candidate--selected': createGroupSelectedIds.has(c.userId) }"
              @click="toggleCreateGroupCandidate(c.userId)"
            >
              <span class="t-conv-info__candidate-name">{{ c.name }}</span>
              <NCheckbox
                :checked="createGroupSelectedIds.has(c.userId)"
                @click.stop
                @update:checked="toggleCreateGroupCandidate(c.userId)"
              />
            </div>
          </NScrollbar>
          <NButton
            size="small"
            type="primary"
            :loading="creatingGroup"
            :disabled="createGroupSelectedIds.size === 0"
            @click="onCreateGroup"
          >
            {{ t('window.createGroup') }}
          </NButton>
        </div>
      </div>

      <!-- ── GROUP: members ──────────────────────────────────────────── -->
      <div v-if="isGroup" class="t-conv-info__block">
        <TMemberGrid
          :members="detail.members"
          :can-add="isOwner"
          @add="addOpen = !addOpen"
          @message="onMessageMember"
        />

        <!-- Inline add-member flow (owner only) -->
        <div v-if="isOwner && addOpen" class="t-conv-info__add">
          <NInput
            v-model:value="addKeyword"
            size="small"
            :placeholder="t('window.search')"
            clearable
            @input="onSearchAdd"
          />
          <NScrollbar v-if="addCandidates.length > 0" class="t-conv-info__candidates">
            <div
              v-for="c in addCandidates"
              :key="c.userId"
              class="t-conv-info__candidate"
              :class="{ 't-conv-info__candidate--selected': addSelectedIds.has(c.userId) }"
              @click="toggleAddCandidate(c.userId)"
            >
              <span class="t-conv-info__candidate-name">{{ c.name }}</span>
              <NCheckbox
                :checked="addSelectedIds.has(c.userId)"
                @click.stop
                @update:checked="toggleAddCandidate(c.userId)"
              />
            </div>
          </NScrollbar>
          <NButton
            v-if="addSelectedIds.size > 0"
            size="small"
            type="primary"
            :loading="adding"
            @click="onAddMembers"
          >
            {{ t('window.add') }} ({{ addSelectedIds.size }})
          </NButton>
        </div>
      </div>

      <!-- ── Editable fields (compact WeChat label+value rows) ───────── -->
      <div class="t-conv-info__fields">
        <!-- GROUP: name (owner editable) -->
        <div v-if="isGroup" class="t-conv-info__field">
          <span class="t-conv-info__field-label">{{ t('window.groupName') }}</span>
          <div
            v-if="!isOwner || editing !== 'title'"
            class="t-conv-info__field-value"
            :class="{ 't-conv-info__field-value--editable': isOwner }"
            @click="isOwner && startEdit('title')"
          >
            {{ detail.title }}
          </div>
          <div v-else class="t-conv-info__field-edit">
            <NInput
              ref="titleInputRef"
              v-model:value="editTitle"
              size="small"
              :loading="renaming"
              @keydown.enter="onRename"
              @blur="onRename"
            />
          </div>
        </div>

        <!-- GROUP: notice (owner editable / read-only) -->
        <div v-if="isGroup" class="t-conv-info__field">
          <span class="t-conv-info__field-label">{{ t('window.notice') }}</span>
          <div
            v-if="!isOwner || editing !== 'notice'"
            class="t-conv-info__field-value"
            :class="{
              't-conv-info__field-value--editable': isOwner,
              't-conv-info__field-value--muted': !detail.notice,
            }"
            @click="isOwner && startEdit('notice')"
          >
            {{ detail.notice || t('window.notSetByOwner') }}
          </div>
          <div v-else class="t-conv-info__field-edit">
            <NInput
              v-model:value="editNotice"
              type="textarea"
              size="small"
              :autosize="{ minRows: 2, maxRows: 5 }"
              :placeholder="t('window.notice')"
            />
            <div class="t-conv-info__field-edit-actions">
              <NButton size="tiny" @click="cancelEdit">{{ t('close') }}</NButton>
              <NButton size="tiny" type="primary" :loading="savingNotice" @click="onSaveNotice">
                {{ t('window.save') }}
              </NButton>
            </div>
          </div>
        </div>

        <!-- GROUP: my alias -->
        <div v-if="isGroup" class="t-conv-info__field">
          <span class="t-conv-info__field-label">{{ t('window.myAlias') }}</span>
          <div
            v-if="editing !== 'alias'"
            class="t-conv-info__field-value t-conv-info__field-value--editable"
            :class="{ 't-conv-info__field-value--muted': !editAlias }"
            @click="startEdit('alias')"
          >
            {{ editAlias || t('window.notSet') }}
          </div>
          <div v-else class="t-conv-info__field-edit">
            <NInput
              v-model:value="editAlias"
              size="small"
              :placeholder="t('window.myAlias')"
              @keydown.enter="onSaveAlias"
              @blur="onSaveAlias"
            />
          </div>
        </div>

        <!-- Remark (direct + group) -->
        <div class="t-conv-info__field">
          <span class="t-conv-info__field-label">{{ t('window.remark') }}</span>
          <div
            v-if="editing !== 'remark'"
            class="t-conv-info__field-value t-conv-info__field-value--editable"
            :class="{ 't-conv-info__field-value--muted': !editRemark }"
            @click="startEdit('remark')"
          >
            {{ editRemark || t('window.notSet') }}
          </div>
          <div v-else class="t-conv-info__field-edit">
            <NInput
              v-model:value="editRemark"
              size="small"
              :placeholder="t('window.remark')"
              @keydown.enter="onSaveRemark"
              @blur="onSaveRemark"
            />
          </div>
        </div>
      </div>

      <!-- ── Action rows (search / mute / sticky) ────────────────────── -->
      <div class="t-conv-info__rows">
        <!-- Search chat history -->
        <div class="t-conv-info__row" :class="{ 't-conv-info__row--open': searchOpen }" @click="toggleSearch">
          <span class="t-conv-info__row-label">{{ t('window.searchHistory') }}</span>
          <Icon icon="mdi:chevron-right" :width="18" class="t-conv-info__row-chevron" />
        </div>
        <div v-if="searchOpen" class="t-conv-info__search">
          <NInput
            v-model:value="searchKeyword"
            size="small"
            :placeholder="t('window.search')"
            clearable
            @input="onSearchHistory"
          />
          <NScrollbar v-if="searchResults.length > 0" class="t-conv-info__search-results">
            <div
              v-for="m in searchResults"
              :key="m.id"
              class="t-conv-info__search-item"
              @click="onSearchResultClick"
            >
              <div class="t-conv-info__search-meta">
                <span class="t-conv-info__search-sender">{{ m.senderName || '—' }}</span>
                <span class="t-conv-info__search-time">{{ formatDateTime(m.sentAt) }}</span>
              </div>
              <div class="t-conv-info__search-content">{{ m.content }}</div>
            </div>
          </NScrollbar>
        </div>

        <!-- Mute -->
        <div class="t-conv-info__row">
          <span class="t-conv-info__row-label">{{ t('window.mute') }}</span>
          <NSwitch size="small" :value="detail.isMuted" @update:value="onToggleMute" />
        </div>

        <!-- Sticky -->
        <div class="t-conv-info__row">
          <span class="t-conv-info__row-label">{{ t('window.sticky') }}</span>
          <NSwitch size="small" :value="detail.isSticky" @update:value="onToggleSticky" />
        </div>
      </div>

      <!-- ── Danger zone (compact red rows) ──────────────────────────── -->
      <div class="t-conv-info__danger">
        <NPopconfirm @positive-click="onClearHistory">
          <template #trigger>
            <button class="t-conv-info__danger-btn">{{ t('window.clearHistory') }}</button>
          </template>
          {{ t('window.clearConfirm') }}
        </NPopconfirm>

        <template v-if="isGroup">
          <NPopconfirm v-if="isOwner" @positive-click="onDissolve">
            <template #trigger>
              <button class="t-conv-info__danger-btn">{{ t('window.dissolveGroup') }}</button>
            </template>
            {{ t('window.dissolveConfirm') }}
          </NPopconfirm>
          <NPopconfirm v-else @positive-click="onLeave">
            <template #trigger>
              <button class="t-conv-info__danger-btn">{{ t('window.leaveGroup') }}</button>
            </template>
            {{ t('window.leaveConfirm') }}
          </NPopconfirm>
        </template>
      </div>
    </div>

    <div v-else-if="loading" class="t-conv-info__loading">{{ t('window.loading') }}</div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, reactive, nextTick, onUnmounted } from 'vue'
import { Icon } from '@iconify/vue'
import { NInput, NScrollbar, NButton, NCheckbox, NPopconfirm, NSwitch } from 'naive-ui'
import { MemberRole, ConversationType } from '@tnzi/core/services/chat'
import type { ConversationDto, ChatContactDto, ChatMessageDto } from '@tnzi/core/services/chat'
import { formatDateTime } from '@tnzi/core'
import { useChatStore } from '../../stores/useChatStore'
import { translatePageKey } from '../../pages/_shared/translate'
import TChatAvatar from './TChatAvatar.vue'
import TMemberGrid from './TMemberGrid.vue'

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

const detail = ref<ConversationDto | null>(null)
const loading = ref(false)

// Editable fields
const editTitle = ref('')
const editNotice = ref('')
const editAlias = ref('')
const editRemark = ref('')

// Which field is currently in inline-edit mode (WeChat-compact pattern):
// a single-line value row turns into an inline editor on tap.
type EditField = 'title' | 'notice' | 'alias' | 'remark' | null
const editing = ref<EditField>(null)
const titleInputRef = ref<{ focus: () => void } | null>(null)

// Search-chat-history row expands an inline search box below it.
const searchOpen = ref(false)

async function startEdit(field: Exclude<EditField, null>) {
  editing.value = field
  await nextTick()
  if (field === 'title') titleInputRef.value?.focus()
}

function cancelEdit() {
  // Reset the in-flight edit back to the persisted detail value.
  if (detail.value) {
    editTitle.value = detail.value.title ?? ''
    editNotice.value = detail.value.notice ?? ''
    editAlias.value = detail.value.myAlias ?? ''
    editRemark.value = detail.value.myRemark ?? ''
  }
  editing.value = null
}

function toggleSearch() {
  searchOpen.value = !searchOpen.value
  if (!searchOpen.value) {
    searchKeyword.value = ''
    searchResults.value = []
  }
}

// Loading flags per action
const renaming = ref(false)
const savingNotice = ref(false)
const savingAlias = ref(false)
const savingRemark = ref(false)
const adding = ref(false)

// Add-member flow
const addOpen = ref(false)
const addKeyword = ref('')
const addCandidates = ref<ChatContactDto[]>([])
const addSelectedIds = reactive(new Set<string>())

// Create-group flow (Direct branch)
const createGroupOpen = ref(false)
const createGroupKeyword = ref('')
const createGroupCandidates = ref<ChatContactDto[]>([])
const createGroupSelectedIds = reactive(new Set<string>())
const creatingGroup = ref(false)

// Search chat history
const searchKeyword = ref('')
const searchResults = ref<ChatMessageDto[]>([])

const isGroup = computed(() => detail.value?.type === ConversationType.Group)

const isOwner = computed(() => {
  if (!detail.value || !props.myId) return false
  return detail.value.members.find((m) => m.userId === props.myId)?.role === MemberRole.Owner
})

async function loadDetail() {
  if (!props.conversationId) return
  loading.value = true
  try {
    const d = await store.getConversationDetail(props.conversationId)
    detail.value = d
    editTitle.value = d.title ?? ''
    editNotice.value = d.notice ?? ''
    editAlias.value = d.myAlias ?? ''
    editRemark.value = d.myRemark ?? ''
  } finally {
    loading.value = false
  }
}

function resetState() {
  detail.value = null
  editing.value = null
  searchOpen.value = false
  addOpen.value = false
  addKeyword.value = ''
  addCandidates.value = []
  addSelectedIds.clear()
  createGroupOpen.value = false
  createGroupKeyword.value = ''
  createGroupCandidates.value = []
  createGroupSelectedIds.clear()
  searchKeyword.value = ''
  searchResults.value = []
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

// ── Group: rename ──────────────────────────────────────────────────────────
// Inline-edit save: triggered by Enter or blur. Close edit mode and skip the
// network call when the value is unchanged, so a blur right after an Enter-save
// (input still focused on Enter, fires blur on the subsequent click-away) is a
// harmless no-op instead of a duplicate request.
async function onRename() {
  editing.value = null
  if (!props.conversationId || !editTitle.value.trim()) return
  if (editTitle.value.trim() === (detail.value?.title ?? '')) return
  renaming.value = true
  try {
    await store.renameGroup(props.conversationId, editTitle.value.trim())
    await reload()
  } finally {
    renaming.value = false
  }
}

// ── Group: notice ──────────────────────────────────────────────────────────
async function onSaveNotice() {
  editing.value = null
  if (!props.conversationId) return
  savingNotice.value = true
  try {
    await store.setNotice(props.conversationId, editNotice.value.trim() || null)
    await reload()
  } finally {
    savingNotice.value = false
  }
}

// ── Group: my alias ────────────────────────────────────────────────────────
async function onSaveAlias() {
  editing.value = null
  if (!props.conversationId) return
  if ((editAlias.value.trim() || '') === (detail.value?.myAlias ?? '')) return
  savingAlias.value = true
  try {
    // Send the trimmed value verbatim — an empty string clears the alias
    // (backend treats `""` as "clear", `null` as "don't touch"). Using `|| null`
    // here would silently drop a clear.
    await store.setMemberSettings(props.conversationId, { alias: editAlias.value.trim() })
    await reload()
  } finally {
    savingAlias.value = false
  }
}

// ── Remark (direct + group) ────────────────────────────────────────────────
async function onSaveRemark() {
  editing.value = null
  if (!props.conversationId) return
  if ((editRemark.value.trim() || '') === (detail.value?.myRemark ?? '')) return
  savingRemark.value = true
  try {
    // Send the trimmed value verbatim — an empty string clears the remark
    // (backend treats `""` as "clear", `null` as "don't touch"). Using `|| null`
    // here would silently drop a clear.
    await store.setMemberSettings(props.conversationId, { remark: editRemark.value.trim() })
    await reload()
  } finally {
    savingRemark.value = false
  }
}

// ── Toggles ────────────────────────────────────────────────────────────────
// Optimistic toggles: flip the local detail immediately so the switch responds
// instantly, then persist in the background (the store refreshes the list on its
// own). Revert on failure. The previous version awaited two serial round-trips
// before the `:value`-bound switch moved — which read as a stuck/laggy toggle.
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

// ── Add members (group / owner) ────────────────────────────────────────────
let addTimer: ReturnType<typeof setTimeout> | null = null
function onSearchAdd() {
  if (addTimer) clearTimeout(addTimer)
  addTimer = setTimeout(async () => {
    if (!addKeyword.value.trim()) {
      addCandidates.value = []
      return
    }
    const results = await store.searchContacts(addKeyword.value.trim())
    const existingIds = new Set(detail.value?.members.map((m) => m.userId) ?? [])
    addCandidates.value = results.filter((c) => !existingIds.has(c.userId))
  }, 300)
}

function toggleAddCandidate(userId: string) {
  if (addSelectedIds.has(userId)) addSelectedIds.delete(userId)
  else addSelectedIds.add(userId)
}

async function onAddMembers() {
  if (!props.conversationId || addSelectedIds.size === 0) return
  adding.value = true
  try {
    await store.addMembers(props.conversationId, [...addSelectedIds])
    addSelectedIds.clear()
    addKeyword.value = ''
    addCandidates.value = []
    addOpen.value = false
    await reload()
  } finally {
    adding.value = false
  }
}

// ── Create group from Direct (Direct branch) ───────────────────────────────
let createGroupTimer: ReturnType<typeof setTimeout> | null = null
function onSearchCreateGroup() {
  if (createGroupTimer) clearTimeout(createGroupTimer)
  createGroupTimer = setTimeout(async () => {
    if (!createGroupKeyword.value.trim()) { createGroupCandidates.value = []; return }
    const results = await store.searchContacts(createGroupKeyword.value.trim())
    const peerUserId = detail.value?.members.find((m) => m.userId !== props.myId)?.userId
    createGroupCandidates.value = results.filter((c) => c.userId !== props.myId && c.userId !== peerUserId)
  }, 300)
}

function toggleCreateGroupCandidate(userId: string) {
  if (createGroupSelectedIds.has(userId)) createGroupSelectedIds.delete(userId)
  else createGroupSelectedIds.add(userId)
}

async function onCreateGroup() {
  if (!detail.value) return
  const peerUserId = detail.value.members.find((m) => m.userId !== props.myId)?.userId
  if (!peerUserId) return
  creatingGroup.value = true
  try {
    // WeChat-style default name: the group is named after its participants
    // (the direct peer + the picked contacts). The owner can rename it later
    // from this same panel. (Was `${peerName}…` — a stray ellipsis read as a bug.)
    const peerName = detail.value.title ?? ''
    const selectedNames = createGroupCandidates.value
      .filter((c) => createGroupSelectedIds.has(c.userId))
      .map((c) => c.name)
      .filter(Boolean)
    const names = [peerName, ...selectedNames].filter(Boolean)
    const title = names.length > 0 ? names.slice(0, 5).join(', ') : (peerName || 'Group')
    const memberIds = [peerUserId, ...createGroupSelectedIds]
    const cid = await store.createGroup(title, memberIds)
    createGroupOpen.value = false
    createGroupSelectedIds.clear()
    createGroupKeyword.value = ''
    createGroupCandidates.value = []
    emit('open-conversation', cid)
    emit('update:show', false)
  } finally {
    creatingGroup.value = false
  }
}

// ── Search chat history ────────────────────────────────────────────────────
let searchTimer: ReturnType<typeof setTimeout> | null = null
function onSearchHistory() {
  if (searchTimer) clearTimeout(searchTimer)
  searchTimer = setTimeout(async () => {
    if (!props.conversationId || !searchKeyword.value.trim()) {
      searchResults.value = []
      return
    }
    const thread = await store.searchMessages(props.conversationId, searchKeyword.value.trim())
    searchResults.value = thread.messages
  }, 300)
}

function onSearchResultClick() {
  // v1: refocus the conversation (scroll-to-message is a future follow-up).
  if (props.conversationId) emit('open-conversation', props.conversationId)
}

// ── Member → message ───────────────────────────────────────────────────────
async function onMessageMember(userId: string) {
  if (userId === props.myId) return
  const cid = await store.startDirect(userId)
  emit('open-conversation', cid)
  emit('update:show', false)
}

// ── Danger zone ────────────────────────────────────────────────────────────
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

onUnmounted(() => {
  if (addTimer) clearTimeout(addTimer)
  if (searchTimer) clearTimeout(searchTimer)
  if (createGroupTimer) clearTimeout(createGroupTimer)
})
</script>

<style scoped>
.t-conv-info {
  width: 250px;
  height: 100%;
  background: var(--chat-surface, #fff);
  border-left: 1px solid var(--chat-border, #e6e6e6);
  overflow: hidden;
}

.t-conv-info__inner {
  height: 100%;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
}

/* ── Header (compact avatar + name) ─────────────────────────────────────── */
.t-conv-info__head {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 14px 14px 12px;
}

.t-conv-info__head-text {
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.t-conv-info__title {
  font-size: 15px;
  font-weight: 600;
  color: var(--chat-text, #1f1f1f);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.t-conv-info__subtitle {
  font-size: 12px;
  color: var(--chat-text-3, #a8a8a8);
}

/* ── Generic block (members / create-group) ─────────────────────────────── */
.t-conv-info__block {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 4px 14px 12px;
}

/* ── Create group button (Direct branch) ─────────────────────────────────── */
.t-conv-info__create-group-btn {
  display: flex;
  align-items: center;
  gap: 6px;
  border: 1px dashed var(--chat-border, #dcdcdc);
  background: transparent;
  border-radius: 6px;
  padding: 7px 10px;
  cursor: pointer;
  font-size: 13px;
  color: var(--chat-text-2, #6f6f6f);
  transition: border-color 0.15s, color 0.15s;
}

.t-conv-info__create-group-btn:hover {
  border-color: var(--chat-text-2, #6f6f6f);
  color: var(--chat-text, #1f1f1f);
}

.t-conv-info__create-group-icon {
  font-size: 16px;
  line-height: 1;
  font-weight: 300;
}

/* ── Add members ────────────────────────────────────────────────────────── */
.t-conv-info__add {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.t-conv-info__candidates {
  max-height: 140px;
}

.t-conv-info__candidate {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 4px;
  cursor: pointer;
  border-radius: 4px;
  transition: background 0.15s;
}

.t-conv-info__candidate:hover {
  background: var(--chat-hover, rgb(51 54 57 / 0.06));
}

.t-conv-info__candidate--selected,
.t-conv-info__candidate--selected:hover {
  background: var(--chat-active, rgb(51 54 57 / 0.1));
}

.t-conv-info__candidate-name {
  flex: 1;
  font-size: 13px;
  color: var(--chat-text, #1f1f1f);
}

/* ── Editable fields (WeChat label + value rows) ────────────────────────── */
.t-conv-info__fields {
  display: flex;
  flex-direction: column;
  border-top: 1px solid var(--chat-border, #e6e6e6);
}

.t-conv-info__field {
  display: flex;
  flex-direction: column;
  gap: 3px;
  padding: 9px 14px;
  border-bottom: 1px solid var(--chat-border, #e6e6e6);
}

.t-conv-info__field-label {
  font-size: 12px;
  color: var(--chat-text-2, #8a8a8a);
}

.t-conv-info__field-value {
  font-size: 13.5px;
  color: var(--chat-text, #1f1f1f);
  white-space: pre-wrap;
  word-break: break-word;
  border-radius: 4px;
  margin: -1px -4px;
  padding: 1px 4px;
}

.t-conv-info__field-value--editable {
  cursor: pointer;
  transition: background 0.12s;
}

.t-conv-info__field-value--editable:hover {
  background: var(--chat-hover, rgb(51 54 57 / 0.05));
}

.t-conv-info__field-value--muted {
  color: var(--chat-text-3, #b0b0b0);
}

.t-conv-info__field-edit {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.t-conv-info__field-edit-actions {
  display: flex;
  justify-content: flex-end;
  gap: 6px;
}

/* ── Action rows (search / mute / sticky) ───────────────────────────────── */
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

.t-conv-info__row--open {
  border-bottom: none;
}

.t-conv-info__row-label {
  font-size: 13.5px;
  color: var(--chat-text, #1f1f1f);
}

/* The search-history row behaves like a navigable item (chevron + pointer). */
.t-conv-info__rows > .t-conv-info__row:first-child {
  cursor: pointer;
}

.t-conv-info__rows > .t-conv-info__row:first-child:hover {
  background: var(--chat-hover, rgb(51 54 57 / 0.04));
}

.t-conv-info__row-chevron {
  color: var(--chat-text-3, #c4c4c4);
  flex-shrink: 0;
  transition: transform 0.18s;
}

.t-conv-info__row--open .t-conv-info__row-chevron {
  transform: rotate(90deg);
}

/* ── Inline search box (below the search-history row) ───────────────────── */
.t-conv-info__search {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 0 14px 10px;
  border-bottom: 1px solid var(--chat-border, #e6e6e6);
}

.t-conv-info__search-results {
  max-height: 200px;
}

.t-conv-info__search-item {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 6px 4px;
  cursor: pointer;
  border-radius: 4px;
  transition: background 0.15s;
}

.t-conv-info__search-item:hover {
  background: var(--chat-hover, rgb(51 54 57 / 0.06));
}

.t-conv-info__search-meta {
  display: flex;
  justify-content: space-between;
  gap: 8px;
  font-size: 11px;
  color: var(--chat-text-3, #a8a8a8);
}

.t-conv-info__search-sender {
  font-weight: 600;
  color: var(--chat-text-2, #6f6f6f);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.t-conv-info__search-time {
  flex-shrink: 0;
}

.t-conv-info__search-content {
  font-size: 13px;
  color: var(--chat-text, #1f1f1f);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

/* ── Danger zone (compact red rows) ─────────────────────────────────────── */
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

/* ── Loading ────────────────────────────────────────────────────────────── */
.t-conv-info__loading {
  padding: 24px;
  text-align: center;
  font-size: 13px;
  color: var(--chat-text-3, #aaa);
}
</style>
