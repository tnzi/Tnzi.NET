<template>
  <NModal
    :show="show"
    preset="card"
    :style="{ width: '440px', maxWidth: '92vw' }"
    :title="t('window.newChat')"
    :bordered="false"
    @update:show="emit('update:show', $event)"
  >
    <div class="t-new-chat">
      <!-- Search input -->
      <NInput
        v-model:value="keyword"
        class="t-new-chat__search"
        :placeholder="t('window.search')"
        clearable
        @input="onSearch"
      />

      <!-- Contact list -->
      <NScrollbar class="t-new-chat__list">
        <div
          v-for="c in contacts"
          :key="c.userId"
          class="t-new-chat__contact"
          :class="{ 't-new-chat__contact--selected': selectedIds.has(c.userId) }"
          @click="toggleContact(c)"
        >
          <TChatAvatar :name="c.name" :file-id="c.avatarFileId" :seed="c.userId" :size="36" />
          <span class="t-new-chat__name">{{ c.name }}</span>
          <NCheckbox
            class="t-new-chat__check"
            :checked="selectedIds.has(c.userId)"
            @click.stop
            @update:checked="toggleContact(c)"
          />
        </div>
        <div v-if="contacts.length === 0 && keyword" class="t-new-chat__empty">
          {{ t('window.empty') }}
        </div>
      </NScrollbar>

      <!-- Group name input (when ≥2 selected) -->
      <NInput
        v-if="selected.length >= 2"
        v-model:value="groupName"
        class="t-new-chat__group-name"
        :placeholder="t('window.groupName')"
      />

      <!-- Action footer -->
      <div class="t-new-chat__footer">
        <NButton
          v-if="selected.length === 1"
          type="primary"
          :loading="submitting"
          :disabled="submitting"
          @click="onStartDirect"
        >
          {{ t('window.startChat') }}
        </NButton>
        <NButton
          v-else-if="selected.length >= 2"
          type="primary"
          :loading="submitting"
          :disabled="submitting || !groupName.trim()"
          @click="onCreateGroup"
        >
          {{ t('window.createGroup') }}
        </NButton>
      </div>
    </div>
  </NModal>
</template>

<script setup lang="ts">
import { ref, reactive } from 'vue'
import { NModal, NInput, NScrollbar, NCheckbox, NButton } from 'naive-ui'
import type { ChatContactDto } from '@tnzi/core/services/chat'
import { useChatStore } from '../../stores/useChatStore'
import { translatePageKey } from '../../pages/_shared/translate'
import TChatAvatar from './TChatAvatar.vue'

const props = defineProps<{ show: boolean }>()
const emit = defineEmits<{
  'update:show': [v: boolean]
  created: [conversationId: string]
}>()

const t = (k: string) => translatePageKey('chat', k)
const store = useChatStore()

const keyword = ref('')
const contacts = ref<ChatContactDto[]>([])
const selectedIds = reactive(new Set<string>())
const selected = ref<ChatContactDto[]>([])
const groupName = ref('')
const submitting = ref(false)

let searchTimer: ReturnType<typeof setTimeout> | null = null

function onSearch() {
  if (searchTimer) clearTimeout(searchTimer)
  searchTimer = setTimeout(async () => {
    if (!keyword.value.trim()) { contacts.value = []; return }
    contacts.value = await store.searchContacts(keyword.value.trim())
  }, 300)
}

function toggleContact(c: ChatContactDto) {
  if (selectedIds.has(c.userId)) {
    selectedIds.delete(c.userId)
    selected.value = selected.value.filter(s => s.userId !== c.userId)
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
    const id = await store.createGroup(groupName.value.trim(), selected.value.map(s => s.userId))
    emit('created', id)
    emit('update:show', false)
    reset()
  } finally {
    submitting.value = false
  }
}

defineExpose({ reset, contacts, selected, selectedIds, groupName, keyword })
</script>

<style scoped>
.t-new-chat {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.t-new-chat__list {
  max-height: 280px;
}

.t-new-chat__contact {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 4px;
  border-radius: 6px;
  cursor: pointer;
  transition: background 0.15s;
}

.t-new-chat__contact:hover {
  background: rgb(var(--tnzi-base-text-rgb, 51 54 57) / 0.06);
}

.t-new-chat__contact--selected,
.t-new-chat__contact--selected:hover {
  background: rgb(var(--tnzi-primary-rgb, 13 148 136) / 0.12);
}

.t-new-chat__name {
  flex: 1;
  font-size: 14px;
  color: var(--tnzi-base-text, #1a1a1a);
}

.t-new-chat__check {
  flex-shrink: 0;
}

.t-new-chat__empty {
  padding: 24px;
  text-align: center;
  color: var(--tnzi-base-text-muted, #aaa);
  font-size: 13px;
}

.t-new-chat__footer {
  display: flex;
  justify-content: flex-end;
}
</style>
