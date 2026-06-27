<template>
  <TChatDialog
    :show="show"
    :title="t('window.newChat')"
    :close-label="t('close')"
    width="360px"
    @update:show="emit('update:show', $event)"
  >
    <!-- Search (filters the directory; blank shows the starting contact list) -->
    <div class="t-new-chat__search" :class="{ 't-new-chat__search--focused': focused }">
      <Icon icon="mdi:magnify" :width="15" class="t-new-chat__search-icon" />
      <input
        v-model="keyword"
        class="t-new-chat__search-input"
        :placeholder="t('window.search')"
        @input="onSearch"
        @focus="focused = true"
        @blur="focused = false"
      />
      <button v-if="keyword" class="t-new-chat__search-clear" tabindex="-1" @click="clearKeyword">
        <Icon icon="mdi:close-circle" :width="13" />
      </button>
    </div>

    <!-- Contact list — fixed height so the dialog footprint never jumps as the
         list fills in (shown immediately on open, no need to type first). The
         height is inline because NScrollbar's root does not inherit the scoped
         style attribute, so a scoped height rule would silently not apply. -->
    <NScrollbar class="t-new-chat__list" style="height: 300px">
      <div
        v-for="c in contacts"
        :key="c.userId"
        class="t-new-chat__contact"
        :class="{ 't-new-chat__contact--selected': selectedIds.has(c.userId) }"
        @click="toggleContact(c)"
      >
        <TChatAvatar :name="c.name" :file-id="c.avatarFileId" :seed="c.userId" :size="34" />
        <span class="t-new-chat__name">{{ c.name }}</span>
        <NCheckbox
          class="t-new-chat__check"
          :checked="selectedIds.has(c.userId)"
          @click.stop
          @update:checked="toggleContact(c)"
        />
      </div>
      <div v-if="contacts.length === 0" class="t-new-chat__empty">
        {{ loading ? t('window.loading') : t('window.empty') }}
      </div>
    </NScrollbar>

    <!-- Group name (when ≥2 selected) -->
    <NInput
      v-if="selected.length >= 2"
      v-model:value="groupName"
      size="small"
      :placeholder="t('window.groupName')"
    />

    <template #footer>
      <NButton
        size="small"
        type="primary"
        :loading="submitting"
        :disabled="!canSubmit"
        @click="onConfirm"
      >
        {{ confirmLabel }}<span v-if="selected.length > 0"> ({{ selected.length }})</span>
      </NButton>
    </template>
  </TChatDialog>
</template>

<script setup lang="ts">
import { ref, reactive, computed, watch } from 'vue'
import { NScrollbar, NCheckbox, NInput, NButton } from 'naive-ui'
import { Icon } from '@iconify/vue'
import type { ChatContactDto } from '@tnzi/core/services/chat'
import { useChatStore } from '../../stores/useChatStore'
import { translatePageKey } from '../../pages/_shared/translate'
import TChatDialog from './TChatDialog.vue'
import TChatAvatar from './TChatAvatar.vue'

const props = defineProps<{ show: boolean }>()
const emit = defineEmits<{
  'update:show': [v: boolean]
  created: [conversationId: string]
}>()

const t = (k: string) => translatePageKey('chat', k)
const store = useChatStore()

const keyword = ref('')
const focused = ref(false)
const contacts = ref<ChatContactDto[]>([])
const loading = ref(false)
const selectedIds = reactive(new Set<string>())
const selected = ref<ChatContactDto[]>([])
const groupName = ref('')
const submitting = ref(false)

const confirmLabel = computed(() =>
  selected.value.length >= 2 ? t('window.createGroup') : t('window.startChat'),
)
const canSubmit = computed(() => {
  if (submitting.value || selected.value.length === 0) return false
  if (selected.value.length >= 2) return !!groupName.value.trim()
  return true
})

// Load the directory list (blank keyword = starting list) so contacts show up
// the moment the dialog opens — no "type to see anything" dead state.
async function load(kw: string) {
  loading.value = true
  try {
    contacts.value = await store.searchContacts(kw.trim())
  } finally {
    loading.value = false
  }
}

let searchTimer: ReturnType<typeof setTimeout> | null = null
function onSearch() {
  if (searchTimer) clearTimeout(searchTimer)
  searchTimer = setTimeout(() => void load(keyword.value), 300)
}

function clearKeyword() {
  keyword.value = ''
  void load('')
}

// Open → reset + show the starting contact list immediately.
watch(() => props.show, (open) => {
  if (open) {
    reset()
    void load('')
  }
})

function toggleContact(c: ChatContactDto) {
  if (selectedIds.has(c.userId)) {
    selectedIds.delete(c.userId)
    selected.value = selected.value.filter((s) => s.userId !== c.userId)
  } else {
    selectedIds.add(c.userId)
    selected.value = [...selected.value, c]
  }
}

function reset() {
  keyword.value = ''
  contacts.value = []
  selectedIds.clear()
  selected.value = []
  groupName.value = ''
  submitting.value = false
}

function onConfirm() {
  if (selected.value.length >= 2) void onCreateGroup()
  else void onStartDirect()
}

async function onStartDirect() {
  const contact = selected.value[0]
  if (!contact) return
  submitting.value = true
  try {
    const id = await store.startDirect(contact.userId)
    emit('created', id)
    emit('update:show', false)
    reset()
  } finally {
    submitting.value = false
  }
}

async function onCreateGroup() {
  if (selected.value.length < 2 || !groupName.value.trim()) return
  submitting.value = true
  try {
    const id = await store.createGroup(groupName.value.trim(), selected.value.map((s) => s.userId))
    emit('created', id)
    emit('update:show', false)
    reset()
  } finally {
    submitting.value = false
  }
}

defineExpose({ reset, contacts, selected, selectedIds, groupName, keyword, toggleContact, onStartDirect, onCreateGroup })
</script>

<style scoped>
/* Search pill — matches the conversation list search styling */
.t-new-chat__search {
  display: flex;
  align-items: center;
  gap: 5px;
  height: 32px;
  padding: 0 8px;
  border-radius: 6px;
  background: var(--chat-search-bg, #e9e9e9);
  border: 1px solid transparent;
  transition: background 0.12s, border-color 0.12s;
}

.t-new-chat__search--focused {
  background: var(--chat-surface, #fff);
  border-color: var(--chat-border, #dcdcdc);
}

.t-new-chat__search-icon {
  flex-shrink: 0;
  color: var(--chat-text-3, #9b9b9b);
}

.t-new-chat__search-input {
  flex: 1;
  min-width: 0;
  border: none;
  outline: none;
  background: transparent;
  font-size: 13px;
  color: var(--chat-text, #1f1f1f);
  font-family: inherit;
}

.t-new-chat__search-input::placeholder {
  color: var(--chat-text-3, #a8a8a8);
}

.t-new-chat__search-clear {
  display: flex;
  align-items: center;
  flex-shrink: 0;
  border: none;
  background: transparent;
  padding: 0;
  cursor: pointer;
  color: var(--chat-text-3, #b0b0b0);
}

.t-new-chat__search-clear:hover {
  color: var(--chat-text-2, #8a8a8a);
}

/* Fixed-height list (set inline on NScrollbar) → dialog footprint stays constant
   while browsing/searching. */
.t-new-chat__contact {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 6px 6px;
  border-radius: 6px;
  cursor: pointer;
  transition: background 0.12s;
}

.t-new-chat__contact:hover {
  background: var(--chat-hover, rgb(51 54 57 / 0.06));
}

.t-new-chat__contact--selected,
.t-new-chat__contact--selected:hover {
  background: var(--chat-active, rgb(13 148 136 / 0.12));
}

.t-new-chat__name {
  flex: 1;
  min-width: 0;
  font-size: 13.5px;
  color: var(--chat-text, #1a1a1a);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.t-new-chat__check {
  flex-shrink: 0;
}

.t-new-chat__empty {
  padding: 40px 16px;
  text-align: center;
  color: var(--chat-text-3, #aaa);
  font-size: 13px;
}
</style>
