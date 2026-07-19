<template>
  <div class="t-info-field">
    <span class="t-info-field__label">{{ label }}</span>

    <!-- Display mode: value + hover pencil (editable only) -->
    <div v-if="!editing" class="t-info-field__row">
      <div
        class="t-info-field__value"
        :class="{
          't-info-field__value--editable': editable,
          't-info-field__value--muted': !value,
          't-info-field__value--multiline': multiline,
        }"
        @click="editable && startEdit()"
      >
        {{ value || placeholder }}
      </div>
      <button
        v-if="editable"
        class="t-info-field__edit-btn"
        :title="t('edit')"
        @click.stop="startEdit()"
      >
        <Icon icon="mdi:pencil-outline" :width="15" />
      </button>
    </div>

    <!-- Edit mode: same footprint as the value so the row never jumps -->
    <div v-else class="t-info-field__edit">
      <NInput
        ref="inputRef"
        v-model:value="draft"
        :type="multiline ? 'textarea' : 'text'"
        :autosize="multiline ? { minRows: 2, maxRows: 5 } : undefined"
        size="small"
        clearable
        :loading="loading"
        :placeholder="placeholder"
        @keydown="onKeydown"
        @blur="onBlur"
      />
      <div v-if="multiline" class="t-info-field__edit-actions">
        <NButton size="tiny" :disabled="loading" @click="cancel">{{ t('close') }}</NButton>
        <NButton size="tiny" type="primary" :loading="loading" @click="save">{{ t('window.save') }}</NButton>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, nextTick } from 'vue'
import { NInput, NButton } from 'naive-ui'
import { Icon } from '@iconify/vue'
import { translatePageKey } from '../../pages/_shared/translate'

const props = withDefaults(
  defineProps<{
    label: string
    value: string
    editable?: boolean
    /** Textarea (with explicit Save/Cancel) instead of a single-line input. */
    multiline?: boolean
    placeholder?: string
    loading?: boolean
  }>(),
  { editable: false, multiline: false, placeholder: '', loading: false },
)

const emit = defineEmits<{ save: [value: string] }>()

const t = (k: string) => translatePageKey('chat', k)

const editing = ref(false)
const draft = ref('')
const inputRef = ref<{ focus: () => void } | null>(null)

async function startEdit() {
  draft.value = props.value ?? ''
  editing.value = true
  await nextTick()
  inputRef.value?.focus()
}

function save() {
  const next = draft.value.trim()
  editing.value = false
  // Only emit when the value actually changed — a blur right after Enter (or an
  // unchanged textarea) is then a harmless no-op instead of a duplicate request.
  if (next !== (props.value ?? '')) emit('save', next)
}

function cancel() {
  editing.value = false
}

// Single-line: Enter saves. Textarea: Enter is a newline (handled by save buttons).
function onKeydown(e: KeyboardEvent) {
  if (props.multiline) return
  if (e.key === 'Enter') {
    e.preventDefault()
    save()
  }
}

function onBlur() {
  // Single-line autosaves on blur; textarea waits for the explicit Save button.
  if (!props.multiline) save()
}
</script>

<style scoped>
.t-info-field {
  display: flex;
  flex-direction: column;
  gap: 3px;
  padding: 9px 14px;
  border-bottom: 1px solid var(--chat-border, #e6e6e6);
}

.t-info-field__label {
  font-size: 12px;
  color: var(--chat-text-2, #8a8a8a);
}

/* Row keeps a fixed min-height that matches the small NInput so toggling
   between display and edit never shifts the layout. */
.t-info-field__row {
  display: flex;
  align-items: center;
  gap: 4px;
  min-height: 28px;
}

.t-info-field__value {
  flex: 1;
  min-width: 0;
  display: flex;
  align-items: center;
  min-height: 28px;
  font-size: 13.5px;
  color: var(--chat-text, #1f1f1f);
  word-break: break-word;
  border-radius: 4px;
  margin: 0 -4px;
  padding: 0 4px;
}

.t-info-field__value--multiline {
  display: block;
  white-space: pre-wrap;
  line-height: 1.5;
  padding: 4px;
}

.t-info-field__value--editable {
  cursor: pointer;
  transition: background 0.12s;
}

.t-info-field__value--editable:hover {
  background: var(--chat-hover, rgb(51 54 57 / 0.05));
}

.t-info-field__value--muted {
  color: var(--chat-text-3, #b0b0b0);
}

.t-info-field__edit-btn {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  border: none;
  background: transparent;
  border-radius: 4px;
  cursor: pointer;
  color: var(--chat-text-3, #b0b0b0);
  opacity: 0;
  transition: opacity 0.12s, background 0.12s, color 0.12s;
}

/* Reveal the pencil on row hover (WeChat-style inline edit affordance). */
.t-info-field__row:hover .t-info-field__edit-btn {
  opacity: 1;
}

/* Touch devices have no hover, so the hover-only pencil would never appear and
   the field would read as non-editable. Keep it permanently visible there. */
@media (hover: none) {
  .t-info-field__edit-btn {
    opacity: 1;
  }
}

.t-info-field__edit-btn:hover {
  background: var(--chat-hover, rgb(51 54 57 / 0.08));
  color: var(--chat-text-2, #6f6f6f);
}

.t-info-field__edit {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.t-info-field__edit-actions {
  display: flex;
  justify-content: flex-end;
  gap: 6px;
}
</style>
