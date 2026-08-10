<template>
  <div class="sr-editor">
    <p v-if="sequential" class="sr-editor__hint">
      <TSvgIcon icon="mdi:sort-numeric-ascending" :size="13" />
      {{ t('recipients.sequentialHint') }}
    </p>

    <div v-for="(row, index) in rows" :key="index" class="sr-row">
      <span v-if="sequential" class="sr-row__ordinal">{{ index + 1 }}</span>
      <NInput
        :value="row.role"
        size="small"
        :placeholder="t('recipients.role')"
        class="sr-row__role"
        @update:value="(v: string) => patch(index, { role: v })"
      />
      <NInput
        :value="row.name"
        size="small"
        :placeholder="t('recipients.name')"
        @update:value="(v: string) => patch(index, { name: v })"
      />
      <NInput
        :value="row.email ?? ''"
        size="small"
        :placeholder="t('recipients.email')"
        @update:value="(v: string) => patch(index, { email: v || null })"
      />
      <NButton
        quaternary
        size="small"
        type="error"
        :disabled="rows.length <= 1"
        @click="remove(index)"
      >
        <TSvgIcon icon="mdi:close" :size="16" />
      </NButton>
    </div>

    <NButton size="small" dashed block @click="add">
      <TSvgIcon icon="mdi:account-plus-outline" :size="16" />
      {{ t('recipients.add') }}
    </NButton>
  </div>
</template>

<script setup lang="ts">
/**
 * Recipient list for a new signing request.
 *
 * ★ `role` is what decides which fields this person is asked to fill - it is
 *   matched against the template's field roles, not free text about who they
 *   are. Getting it wrong produces a document that asks the wrong person to
 *   sign, so it leads the row rather than hiding behind the name.
 *
 * ★ The last row cannot be removed: a request with no recipients is not a
 *   draft of anything, and the backend rejects it. Blocking it here means the
 *   operator finds out while editing rather than on submit.
 */
import { computed } from 'vue'
import { NButton, NInput } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import type { CreateSignerDto } from '@tnzi/core/services/signing'

const props = defineProps<{
  modelValue?: CreateSignerDto[] | null
  sequential?: boolean
  translate: (key: string) => string
}>()

const emit = defineEmits<{ 'update:modelValue': [CreateSignerDto[]] }>()

const t = (key: string): string => props.translate(key)

const rows = computed<CreateSignerDto[]>(() =>
  props.modelValue?.length ? props.modelValue : [{ role: '', name: '', email: null }],
)

function patch(index: number, changes: Partial<CreateSignerDto>): void {
  emit(
    'update:modelValue',
    rows.value.map((row, i) => (i === index ? { ...row, ...changes } : row)),
  )
}

function remove(index: number): void {
  emit('update:modelValue', rows.value.filter((_, i) => i !== index))
}

function add(): void {
  emit('update:modelValue', [...rows.value, { role: '', name: '', email: null }])
}
</script>

<style scoped>
.sr-editor {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.sr-editor__hint {
  display: flex;
  align-items: center;
  gap: 4px;
  margin: 0;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
.sr-row {
  display: flex;
  align-items: center;
  gap: 8px;
}
.sr-row__ordinal {
  flex: 0 0 auto;
  width: 18px;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
.sr-row__role {
  max-width: 160px;
}
@media (max-width: 767px) {
  .sr-row {
    flex-wrap: wrap;
  }
  .sr-row__role {
    max-width: none;
  }
}
</style>
