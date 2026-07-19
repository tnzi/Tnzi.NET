<template>
  <TChatDialog
    :show="show"
    :title="title"
    :close-label="t('close')"
    width="360px"
    @update:show="emit('update:show', $event)"
  >
    <!-- Search (filters the directory; blank shows the starting contact list) -->
    <div class="t-member-picker__search" :class="{ 't-member-picker__search--focused': focused }">
      <Icon icon="mdi:magnify" :width="15" class="t-member-picker__search-icon" />
      <input
        v-model="keyword"
        class="t-member-picker__search-input"
        :placeholder="t('window.search')"
        enterkeyhint="search"
        autocapitalize="off"
        autocorrect="off"
        @input="onSearch"
        @focus="focused = true"
        @blur="focused = false"
      />
      <button v-if="keyword" class="t-member-picker__search-clear" tabindex="-1" @click="clearKeyword">
        <Icon icon="mdi:close-circle" :width="13" />
      </button>
    </div>

    <!-- Candidate list — fixed height on desktop so the dialog footprint stays
         constant; on a phone (isSm) it flexes to a dvh-capped height so it
         never exceeds the viewport with the keyboard up. Inline height:
         NScrollbar's root does not inherit the scoped attr. -->
    <NScrollbar class="t-member-picker__list" :style="{ height: isSm ? 'auto' : '280px', maxHeight: isSm ? '50dvh' : undefined }">
      <div
        v-for="c in candidates"
        :key="c.userId"
        class="t-member-picker__item"
        :class="{ 't-member-picker__item--selected': selected.has(c.userId) }"
        @click="toggle(c)"
      >
        <TChatAvatar :name="c.name" :file-id="c.avatarFileId" :seed="c.userId" :size="30" />
        <span class="t-member-picker__name">{{ c.name }}</span>
        <NCheckbox :checked="selected.has(c.userId)" @click.stop @update:checked="toggle(c)" />
      </div>
      <div v-if="candidates.length === 0" class="t-member-picker__empty">
        {{ loading ? t('window.loading') : t('window.empty') }}
      </div>
    </NScrollbar>

    <template #footer>
      <NButton size="small" @click="close">{{ t('close') }}</NButton>
      <NButton
        size="small"
        type="primary"
        :loading="loading || confirming"
        :disabled="selected.size === 0"
        @click="onConfirm"
      >
        {{ confirmLabel }}<span v-if="selected.size > 0"> ({{ selected.size }})</span>
      </NButton>
    </template>
  </TChatDialog>
</template>

<script setup lang="ts">
import { ref, reactive, watch, onUnmounted } from 'vue'
import { NScrollbar, NCheckbox, NButton } from 'naive-ui'
import { Icon } from '@iconify/vue'
import type { ChatContactDto } from '@tnzi/core/services/chat'
import { useChatStore } from '../../stores/useChatStore'
import { translatePageKey } from '../../pages/_shared/translate'
import { useBreakpoint } from '../../headless/useBreakpoint'
import TChatDialog from './TChatDialog.vue'
import TChatAvatar from './TChatAvatar.vue'

const props = withDefaults(
  defineProps<{
    show: boolean
    title: string
    confirmLabel: string
    /** User ids to hide from the candidate list (already members, self, peer). */
    excludeIds?: string[]
    loading?: boolean
  }>(),
  { excludeIds: () => [], loading: false },
)

const emit = defineEmits<{
  'update:show': [v: boolean]
  confirm: [contacts: ChatContactDto[]]
}>()

const t = (k: string) => translatePageKey('chat', k)
const store = useChatStore()
// Phone (<md) flexes the fixed list height so it never exceeds the viewport.
const { isSm } = useBreakpoint()

const keyword = ref('')
const focused = ref(false)
const candidates = ref<ChatContactDto[]>([])
const loading = ref(false)
// `loading` prop = parent's confirm-in-flight flag; mirror it locally so the
// confirm button spinner reflects the add/create request too.
const confirming = ref(false)
watch(() => props.loading, (v) => { confirming.value = v })
// Keep the full contact objects (not just ids) so the caller gets names for a
// default group title; survives across searches that change the candidate list.
const selected = reactive(new Map<string, ChatContactDto>())

function reset() {
  keyword.value = ''
  candidates.value = []
  selected.clear()
}

async function load(kw: string) {
  loading.value = true
  try {
    const results = await store.searchContacts(kw.trim())
    const exclude = new Set(props.excludeIds)
    candidates.value = results.filter((c) => !exclude.has(c.userId))
  } finally {
    loading.value = false
  }
}

// Open → reset + show the starting candidate list immediately.
watch(() => props.show, (open) => {
  if (open) { reset(); void load('') }
})

let timer: ReturnType<typeof setTimeout> | null = null
function onSearch() {
  if (timer) clearTimeout(timer)
  timer = setTimeout(() => void load(keyword.value), 300)
}

function clearKeyword() {
  keyword.value = ''
  void load('')
}

function toggle(c: ChatContactDto) {
  if (selected.has(c.userId)) selected.delete(c.userId)
  else selected.set(c.userId, c)
}

function onConfirm() {
  if (selected.size === 0) return
  emit('confirm', [...selected.values()])
}

function close() { emit('update:show', false) }

onUnmounted(() => { if (timer) clearTimeout(timer) })

defineExpose({ reset, load, candidates, selected, toggle, onConfirm, keyword })
</script>

<style scoped>
.t-member-picker__search {
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

.t-member-picker__search--focused {
  background: var(--chat-surface, #fff);
  border-color: var(--chat-border, #dcdcdc);
}

.t-member-picker__search-icon {
  flex-shrink: 0;
  color: var(--chat-text-3, #9b9b9b);
}

.t-member-picker__search-input {
  flex: 1;
  min-width: 0;
  border: none;
  outline: none;
  background: transparent;
  font-size: 13px;
  color: var(--chat-text, #1f1f1f);
  font-family: inherit;
}

.t-member-picker__search-input::placeholder {
  color: var(--chat-text-3, #a8a8a8);
}

.t-member-picker__search-clear {
  display: flex;
  align-items: center;
  flex-shrink: 0;
  border: none;
  background: transparent;
  padding: 0;
  cursor: pointer;
  color: var(--chat-text-3, #b0b0b0);
}

.t-member-picker__search-clear:hover {
  color: var(--chat-text-2, #8a8a8a);
}

/* Height set inline on NScrollbar (scoped attr not inherited by its root). */
.t-member-picker__item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 6px 6px;
  cursor: pointer;
  border-radius: 6px;
  transition: background 0.12s;
}

.t-member-picker__item:hover {
  background: var(--chat-hover, rgb(51 54 57 / 0.06));
}

.t-member-picker__item--selected,
.t-member-picker__item--selected:hover {
  background: var(--chat-active, rgb(51 54 57 / 0.1));
}

.t-member-picker__name {
  flex: 1;
  min-width: 0;
  font-size: 13.5px;
  color: var(--chat-text, #1f1f1f);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.t-member-picker__empty {
  padding: 40px 16px;
  text-align: center;
  font-size: 13px;
  color: var(--chat-text-3, #b0b0b0);
}
</style>
