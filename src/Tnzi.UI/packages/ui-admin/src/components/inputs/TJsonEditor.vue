<script setup lang="ts">
/**
 * `TJsonEditor` — textarea-based JSON editor with format/validate/auto-fix.
 *
 * Keeps the bundle tiny (no Monaco / CodeMirror dependency). For users who
 * need syntax highlighting + autocomplete, swap in `@guolao/vue-monaco-editor`
 * in your own page. This component covers the 90% case (config blobs,
 * environment overrides, theme snapshots).
 *
 * v-model:value is the JSON string. The component does NOT auto-parse — the
 * consumer decides when to convert to an object.
 */
import { ref, computed, watch } from 'vue'
import { Icon } from '@iconify/vue'
import { NButton } from 'naive-ui'
import { useBreakpoint } from '../../headless/useBreakpoint'

// Touch: bump the toolbar buttons from tiny → small (paired with the
// pointer:coarse ≥40px hit-area rule below).
const { isTouch } = useBreakpoint()

interface Props {
  value: string
  rows?: number
  placeholder?: string
  readonly?: boolean
  /** Run format on blur. */
  autoFormat?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  value: '',
  rows: 8,
  placeholder: '{}',
  readonly: false,
  autoFormat: true,
})

const emit = defineEmits<{
  'update:value': [value: string]
}>()

const errorMessage = ref<string | null>(null)

const isValid = computed(() => errorMessage.value === null)

function tryParse(text: string): { ok: true; value: unknown } | { ok: false; error: string } {
  if (!text.trim()) {
    return { ok: true, value: null }
  }
  try {
    return { ok: true, value: JSON.parse(text) }
  } catch (err) {
    return {
      ok: false,
      error: err instanceof Error ? err.message : 'Invalid JSON',
    }
  }
}

function handleInput(event: Event): void {
  const text = (event.target as HTMLTextAreaElement).value
  emit('update:value', text)
}

function handleBlur(event: FocusEvent): void {
  const text = (event.target as HTMLTextAreaElement).value
  const result = tryParse(text)
  errorMessage.value = result.ok ? null : result.error
  if (result.ok && props.autoFormat && text.trim()) {
    const formatted = JSON.stringify(result.value, null, 2)
    if (formatted !== text) {
      emit('update:value', formatted)
    }
  }
}

function format(): void {
  const result = tryParse(props.value)
  if (result.ok) {
    errorMessage.value = null
    if (props.value.trim()) {
      emit('update:value', JSON.stringify(result.value, null, 2))
    }
  } else {
    errorMessage.value = result.error
  }
}

function minify(): void {
  const result = tryParse(props.value)
  if (result.ok && result.value !== null) {
    errorMessage.value = null
    emit('update:value', JSON.stringify(result.value))
  } else if (!result.ok) {
    errorMessage.value = result.error
  }
}

watch(
  () => props.value,
  () => {
    if (errorMessage.value) {
      // Clear error once the user starts typing again
      errorMessage.value = null
    }
  },
)

defineExpose({ format, minify, isValid, errorMessage })
</script>

<template>
  <div class="t-json-editor" :class="{ 't-json-editor--invalid': !isValid }">
    <div class="t-json-editor__toolbar">
      <NButton
        class="t-json-editor__btn"
        :size="isTouch ? 'small' : 'tiny'"
        :disabled="readonly"
        @click="format"
      >
        <template #icon>
          <Icon icon="mdi:code-braces" width="14" height="14" />
        </template>
        Format
      </NButton>
      <NButton
        class="t-json-editor__btn"
        :size="isTouch ? 'small' : 'tiny'"
        :disabled="readonly"
        @click="minify"
      >
        <template #icon>
          <Icon icon="mdi:arrow-collapse-horizontal" width="14" height="14" />
        </template>
        Minify
      </NButton>
      <span
        class="t-json-editor__status"
        :class="{ 't-json-editor__status--error': !isValid }"
      >
        <Icon
          :icon="isValid ? 'mdi:check-circle' : 'mdi:alert-circle'"
          width="14"
          height="14"
        />
        {{ isValid ? 'valid' : errorMessage }}
      </span>
    </div>
    <textarea
      class="t-json-editor__textarea"
      :value="value"
      :rows="rows"
      :placeholder="placeholder"
      :readonly="readonly"
      spellcheck="false"
      @input="handleInput"
      @blur="handleBlur"
    />
  </div>
</template>

<style scoped>
.t-json-editor {
  display: flex;
  flex-direction: column;
  border: 1px solid var(--tnzi-border);
  border-radius: 6px;
  overflow: hidden;
}
.t-json-editor--invalid {
  border-color: var(--tnzi-error);
}
.t-json-editor__toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 8px;
  background: var(--tnzi-layout-bg, #f5f7fa);
  border-bottom: 1px solid var(--tnzi-border);
}
.t-json-editor__status {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  margin-left: auto;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
.t-json-editor__status--error {
  color: var(--tnzi-error);
  max-width: 60%;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-json-editor__textarea {
  width: 100%;
  padding: 10px 12px;
  border: none;
  outline: none;
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 12.5px;
  line-height: 1.55;
  background: var(--tnzi-container-bg, #ffffff);
  color: var(--tnzi-base-text);
  resize: vertical;
}

/* Touch: enlarge the Format/Minify hit area to ≥40px and let the status text
   wrap onto its own line instead of truncating (little horizontal room on
   phones). Desktop keeps the compact tiny toolbar untouched. */
@media (pointer: coarse) {
  .t-json-editor__toolbar {
    flex-wrap: wrap;
    gap: 10px;
  }
  .t-json-editor__btn {
    min-height: 40px;
  }
  .t-json-editor__status,
  .t-json-editor__status--error {
    max-width: 100%;
    overflow: visible;
    text-overflow: clip;
    white-space: normal;
  }
}
</style>
