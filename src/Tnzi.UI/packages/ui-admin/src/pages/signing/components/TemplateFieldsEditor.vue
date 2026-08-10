<template>
  <div class="sf-editor">
    <div v-if="!rows.length" class="sf-editor__empty">
      {{ t('fields.empty') }}
    </div>

    <div v-for="(row, index) in rows" :key="index" class="sf-row">
      <div class="sf-row__head">
        <span class="sf-row__ordinal">{{ index + 1 }}</span>
        <NInput
          :value="row.key"
          size="small"
          :placeholder="t('fields.key')"
          class="sf-row__key"
          @update:value="(v: string) => patch(index, { key: v })"
        />
        <NInput
          :value="row.label ?? ''"
          size="small"
          :placeholder="t('fields.label')"
          @update:value="(v: string) => patch(index, { label: v })"
        />
        <NButton quaternary size="small" type="error" @click="remove(index)">
          <TSvgIcon icon="mdi:close" :size="16" />
        </NButton>
      </div>

      <div class="sf-row__grid">
        <label class="sf-cell" :data-label="t('fields.type')">
          <NSelect
            :value="row.type"
            size="small"
            :options="typeOptions"
            @update:value="(v: SigningFieldType) => patch(index, { type: v })"
          />
        </label>

        <!-- Empty role = the sender pre-fills it. That is a real choice, so the
             control offers it explicitly rather than leaving the box blank. -->
        <label class="sf-cell" :data-label="t('fields.recipientRole')">
          <NInput
            :value="row.recipientRole ?? ''"
            size="small"
            clearable
            :placeholder="t('fields.senderFilled')"
            @update:value="(v: string) => patch(index, { recipientRole: v || null })"
          />
        </label>

        <label class="sf-cell" :data-label="t('fields.binding')">
          <NInput
            :value="row.binding ?? ''"
            size="small"
            clearable
            :placeholder="t('fields.bindingPlaceholder')"
            @update:value="(v: string) => patch(index, { binding: v || null })"
          />
        </label>

        <label class="sf-cell" :data-label="t('fields.placement')">
          <NSelect
            :value="row.placementMode"
            size="small"
            :options="placementOptions"
            @update:value="(v: FieldPlacementMode) => patch(index, { placementMode: v })"
          />
        </label>

        <label
          v-if="row.placementMode === FieldPlacementMode.Anchor"
          class="sf-cell sf-cell--wide"
          :data-label="t('fields.anchorText')"
        >
          <NInput
            :value="row.anchorText ?? ''"
            size="small"
            :placeholder="t('fields.anchorPlaceholder')"
            @update:value="(v: string) => patch(index, { anchorText: v || null })"
          />
        </label>

        <template v-else>
          <label class="sf-cell sf-cell--num" :data-label="t('fields.page')">
            <NInputNumber
              :value="row.page"
              size="small"
              :min="1"
              @update:value="(v: number | null) => patch(index, { page: v ?? 1 })"
            />
          </label>
          <!-- Normalized 0-1 from the top-left corner, same convention as
               Tnzi.Documents. Percent would be friendlier to type but would
               drift from what actually gets stored. -->
          <label
            v-for="axis in BOX_AXES"
            :key="axis"
            class="sf-cell sf-cell--num"
            :data-label="t(`fields.${axis}`)"
          >
            <NInputNumber
              :value="row[axis]"
              size="small"
              :min="0"
              :max="1"
              :step="0.01"
              @update:value="(v: number | null) => patch(index, { [axis]: v ?? 0 })"
            />
          </label>
        </template>

        <label class="sf-cell sf-cell--switch" :data-label="t('fields.required')">
          <NSwitch
            :value="row.required"
            size="small"
            @update:value="(v: boolean) => patch(index, { required: v })"
          />
        </label>
      </div>
    </div>

    <NButton size="small" dashed block :disabled="readonly" @click="add">
      <TSvgIcon icon="mdi:plus" :size="16" />
      {{ t('fields.add') }}
    </NButton>
  </div>
</template>

<script setup lang="ts">
/**
 * Row editor for a template's placed fields.
 *
 * This is a numeric editor, not a visual designer: you type the normalized box
 * rather than drag it onto a page preview. A drag-on-the-PDF designer is the
 * right long-term shape (Roadmap E3) but it is a feature in its own right, and
 * shipping the numeric form first is what makes `Composed` templates - where
 * the renderer captures the boxes itself and these values are informational -
 * usable today.
 *
 * ★ Every edit emits a NEW array; the model is never mutated in place. The
 *   backend rebuilds the field set wholesale on update, so a half-mutated
 *   local array would be silently persisted as the new truth.
 */
import { computed } from 'vue'
import { NButton, NInput, NInputNumber, NSelect, NSwitch } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import {
  FieldPlacementMode,
  SigningFieldType,
  type TemplateFieldInputDto,
} from '@tnzi/core/services/signing'
import {
  FIELD_PLACEMENT_OPTIONS,
  SIGNING_FIELD_TYPE_OPTIONS,
} from '../signing-config'

const props = defineProps<{
  modelValue?: TemplateFieldInputDto[] | null
  readonly?: boolean
  translate: (key: string) => string
}>()

const emit = defineEmits<{ 'update:modelValue': [TemplateFieldInputDto[]] }>()

const BOX_AXES = ['x', 'y', 'w', 'h'] as const

const t = (key: string): string => props.translate(key)

const rows = computed<TemplateFieldInputDto[]>(() => props.modelValue ?? [])

const typeOptions = SIGNING_FIELD_TYPE_OPTIONS.map((v) => ({ label: v, value: v }))
const placementOptions = FIELD_PLACEMENT_OPTIONS.map((v) => ({ label: v, value: v }))

function patch(index: number, changes: Partial<TemplateFieldInputDto>): void {
  emit(
    'update:modelValue',
    rows.value.map((row, i) => (i === index ? { ...row, ...changes } : row)),
  )
}

function remove(index: number): void {
  emit(
    'update:modelValue',
    rows.value.filter((_, i) => i !== index).map((row, i) => ({ ...row, sortOrder: i })),
  )
}

function add(): void {
  const next: TemplateFieldInputDto = {
    key: '',
    label: '',
    type: SigningFieldType.Text,
    recipientRole: null,
    binding: null,
    required: false,
    placementMode: FieldPlacementMode.Absolute,
    anchorText: null,
    page: 1,
    x: 0.1,
    y: 0.1,
    w: 0.3,
    h: 0.04,
    fontSize: null,
    sortOrder: rows.value.length,
  }
  emit('update:modelValue', [...rows.value, next])
}
</script>

<style scoped>
.sf-editor {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.sf-editor__empty {
  padding: 16px;
  text-align: center;
  color: var(--tnzi-base-text-muted);
  border: 1px dashed var(--tnzi-border);
  border-radius: var(--tnzi-admin-radius-sm, 6px);
}
.sf-row {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 10px 12px;
  border: 1px solid var(--tnzi-border);
  border-radius: var(--tnzi-admin-radius-sm, 6px);
  background: var(--tnzi-bg-deep);
}
.sf-row__head {
  display: flex;
  align-items: center;
  gap: 8px;
}
.sf-row__ordinal {
  flex: 0 0 auto;
  width: 20px;
  font-size: 12px;
  color: var(--tnzi-base-text-muted);
}
.sf-row__key {
  max-width: 200px;
}
.sf-row__grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(120px, 1fr));
  gap: 8px 12px;
}
.sf-cell {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}
.sf-cell--wide {
  grid-column: span 2;
}
.sf-cell--num {
  max-width: 120px;
}
.sf-cell--switch {
  justify-content: flex-end;
}
.sf-cell::before {
  content: attr(data-label);
  font-size: 11px;
  color: var(--tnzi-base-text-muted);
}

/* Below md the grid would squeeze eight numeric inputs into a phone width;
   one field per row keeps every label readable (see ui-content-page §5.5). */
@media (max-width: 767px) {
  .sf-row__grid {
    grid-template-columns: 1fr;
  }
  .sf-cell--wide {
    grid-column: span 1;
  }
  .sf-cell--num {
    max-width: none;
  }
}
</style>
