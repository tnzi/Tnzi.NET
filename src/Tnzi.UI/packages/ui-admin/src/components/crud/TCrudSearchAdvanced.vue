<template>
  <NForm
    class="t-crud-page__search-advanced t-crud-search-advanced"
    :label-placement="labeled ? 'top' : 'left'"
    :show-feedback="false"
    :show-require-mark="false"
  >
    <NGrid responsive="screen" item-responsive :x-gap="12" :y-gap="12">
      <!-- Compact (default): label-less, placeholder carries the field name,
           responsive span ladder 1/2/3/4. Labeled (drawer): one field per row,
           full label on top. -->
      <NGi v-for="field in searchFields" :key="field.key" :span="labeled ? '24' : '24 s:12 m:8 l:6'">
        <NFormItem :show-label="labeled" :label="labeled ? fieldLabel(field) : undefined" :path="field.key">
          <component :is="renderSearchField(field)" />
        </NFormItem>
      </NGi>
      <NGi v-if="!hideSubmit" :span="searchButtonSpan" class="t-crud-page__search-actions-cell">
        <NSpace justify="end" class="t-crud-page__search-actions">
          <NButton type="primary" @click="onApplyAdvancedSearch">
            {{ t('admin.crud.search') }}
          </NButton>
        </NSpace>
      </NGi>
    </NGrid>
  </NForm>
</template>

<script setup lang="ts">
import { computed, h, reactive, watch, type VNodeChild } from 'vue'
import {
  NButton,
  NDatePicker,
  NForm,
  NFormItem,
  NGi,
  NGrid,
  NInput,
  NInputNumber,
  NSelect,
  NSpace,
  NSwitch,
} from 'naive-ui'
import type { FormSchemaItem } from '../../pages/_shared/form-schema'
import type { SearchableState } from './TCrudSearch.vue'

/**
 * A search field, optionally carrying a custom `render` escape hatch. When
 * `render` is set it takes precedence over the built-in `type` switch, so a
 * page can drop a daterange / cascader / any bespoke control into advanced
 * search without the renderer needing to know the type. `render` receives the
 * live search model and should write back via the model (e.g.
 * `model.foo = v`). Backward compatible: every existing `FormSchemaItem[]`
 * still satisfies this (render is optional).
 */
export interface SearchFieldItem extends FormSchemaItem {
  render?: (model: Record<string, unknown>) => VNodeChild
}

/**
 * Advanced (multi-field) search grid. Extracted from {@link TCrudSearch} so
 * the same grid can be reused in two layouts:
 *  - inside {@link TCrudSearch} (collapsed below the simple keyword row), and
 *  - inside {@link TListShell}'s white page-header card (the search lives in
 *    the header bar; the advanced grid drops below it on toggle).
 * Owns its own `searchModel`; commits via `state.setFilters(...)` + refresh.
 */
interface Props {
  state: SearchableState
  searchFields?: SearchFieldItem[]
  translate?: (key: string) => string
  /** Hide the inline Search button (when an external footer drives apply/reset). */
  hideSubmit?: boolean
  /** Labeled layout: one field per row with its label on top (drawer use).
      Default (false) is the compact, label-less placeholder grid. */
  labeled?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  searchFields: undefined,
  translate: undefined,
  hideSubmit: false,
  labeled: false,
})

function t(key: string): string {
  return props.translate ? props.translate(key) : key
}

function maybeTranslate(value: string | undefined, fallback: string): string {
  if (!value) return fallback
  if (/^[a-z][a-zA-Z0-9]*(\.[a-zA-Z0-9]+)*$/.test(value)) return t(value)
  return value
}

function fieldLabel(item: FormSchemaItem): string {
  return maybeTranslate(item.label, item.label)
}

const searchModel = reactive<Record<string, unknown>>({})

const searchButtonSpan = computed<string>(() => {
  const count = props.searchFields?.length ?? 0
  if (count === 0) return '24'
  const sUsed = count % 2
  const mUsed = count % 3
  const lUsed = count % 4
  const sSpan = sUsed === 0 ? 12 : (2 - sUsed) * 12
  const mSpan = mUsed === 0 ? 12 : (3 - mUsed) * 8
  const lSpan = lUsed === 0 ? 12 : (4 - lUsed) * 6
  return `24 s:${sSpan} m:${mSpan} l:${lSpan}`
})

watch(
  () => props.searchFields,
  (fields) => {
    if (!fields) return
    for (const key of Object.keys(searchModel)) {
      if (!fields.find((f) => f.key === key)) delete searchModel[key]
    }
    for (const f of fields) {
      if (!(f.key in searchModel)) searchModel[f.key] = null
    }
  },
  { immediate: true, deep: false },
)

function renderSearchField(item: SearchFieldItem): unknown {
  // Custom render escape hatch wins over the builtin type switch - lets a page
  // drop a daterange / cascader / bespoke control into advanced search.
  if (item.render) return item.render(searchModel)
  const value = searchModel[item.key]
  const onUpdate = (v: unknown) => {
    searchModel[item.key] = v
  }
  const effectiveType = item.typeFn ? item.typeFn(searchModel) : item.type
  const placeholder = maybeTranslate(item.placeholder ?? item.label, item.label)
  switch (effectiveType) {
    case 'text':
      return h(NInput, {
        value: (value as string | null) ?? null,
        placeholder,
        clearable: true,
        'onUpdate:value': onUpdate,
      })
    case 'textarea':
      return h(NInput, {
        value: (value as string | null) ?? null,
        type: 'textarea',
        placeholder,
        clearable: true,
        'onUpdate:value': onUpdate,
      })
    case 'number':
      return h(NInputNumber, {
        value: (value as number | null) ?? null,
        min: item.min,
        max: item.max,
        clearable: true,
        'onUpdate:value': onUpdate,
      })
    case 'switch':
      return h(NSwitch, { value: value as boolean, 'onUpdate:value': onUpdate })
    case 'select':
      return h(NSelect, {
        value: (value as string | number | null) ?? null,
        options: item.options ?? [],
        clearable: true,
        placeholder,
        'onUpdate:value': onUpdate,
      })
    case 'date':
      return h(NDatePicker, {
        value: value as number | null,
        clearable: true,
        type: 'date',
        'onUpdate:value': onUpdate,
      })
    default:
      return null
  }
}

function onApplyAdvancedSearch(): void {
  const filters: Record<string, unknown> = {}
  for (const [k, v] of Object.entries(searchModel)) {
    if (v !== null && v !== undefined && v !== '') filters[k] = v
  }
  props.state.setFilters(filters)
  void props.state.refresh()
}

function resetAdvancedSearch(): void {
  for (const key of Object.keys(searchModel)) searchModel[key] = null
  props.state.setFilters({})
  void props.state.refresh()
}

defineExpose({ apply: onApplyAdvancedSearch, reset: resetAdvancedSearch })
</script>

<style scoped>
/* No card chrome - the advanced grid is always embedded inside a parent
   container (TCrudSearch's collapse panel or TListShell's header card). */
.t-crud-page__search-actions-cell {
  padding-right: 12px;
}
.t-crud-page__search-actions {
  width: 100%;
}
</style>
