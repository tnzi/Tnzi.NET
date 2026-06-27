<script setup lang="ts">
/**
 * `TPermissionTree` — a checkable tree primitive for permission / function
 * assignment UIs (role → functions, group → permissions, …).
 *
 * Thin, opinionated facade over naive-ui `NTree` with the defaults an
 * assignment tree wants (checkable + cascade + check-strategy="all" +
 * block-line + expand-all) pre-set, while still exposing every knob a real
 * page needs (per-node `disabled` / `checkboxDisabled` / `suffix` ride on the
 * `TreeOption` data, and the field names / strategy / filter are props).
 *
 * Accepts naive `TreeOption[]` directly (richer than a fixed `{id,name}`
 * shape) so callers can attach `suffix` render fns, disable module parents,
 * prefix synthetic keys, etc. — `RoleFunctions.vue` is the reference consumer.
 * Set `:checkable="false" :selectable="true"` to reuse it as a single-select
 * navigation tree.
 */
import { NTree } from 'naive-ui'
import type { TreeOption } from 'naive-ui'

interface Props {
  /** Naive tree nodes (carry per-node label/suffix/disabled/checkboxDisabled). */
  data: TreeOption[]
  /** Checked node keys (v-model:checked-keys). */
  checkedKeys?: string[]
  /** Selected node keys (v-model:selected-keys) — for navigation mode. */
  selectedKeys?: string[]
  /** Show checkboxes. Default true. */
  checkable?: boolean
  /** Allow node selection (highlight). Default false (assignment mode). */
  selectable?: boolean
  /** Parent/child checkbox linkage. Default true. */
  cascade?: boolean
  /** Which keys `update:checked-keys` reports. Default 'all'. */
  checkStrategy?: 'all' | 'parent' | 'child'
  /** Whole-row click target. Default true. */
  blockLine?: boolean
  /** Expand every node on mount. Default true. */
  defaultExpandAll?: boolean
  /** Filter pattern (naive built-in highlight/filter). */
  pattern?: string
  keyField?: string
  labelField?: string
  childrenField?: string
}

const props = withDefaults(defineProps<Props>(), {
  checkedKeys: () => [],
  selectedKeys: () => [],
  checkable: true,
  selectable: false,
  cascade: true,
  checkStrategy: 'all',
  blockLine: true,
  defaultExpandAll: true,
  pattern: undefined,
  keyField: 'key',
  labelField: 'label',
  childrenField: 'children',
})

const emit = defineEmits<{
  'update:checkedKeys': [keys: string[]]
  'update:selectedKeys': [keys: string[]]
  change: [keys: string[]]
}>()

function onChecked(keys: Array<string | number>): void {
  const out = keys.map(String)
  emit('update:checkedKeys', out)
  emit('change', out)
}

function onSelected(keys: Array<string | number>): void {
  emit('update:selectedKeys', keys.map(String))
}
</script>

<template>
  <NTree
    class="t-permission-tree"
    :data="data"
    :checked-keys="checkedKeys"
    :selected-keys="selectedKeys"
    :checkable="checkable"
    :selectable="selectable"
    :cascade="cascade"
    :check-strategy="checkStrategy"
    :block-line="blockLine"
    :default-expand-all="defaultExpandAll"
    :pattern="pattern"
    :key-field="keyField"
    :label-field="labelField"
    :children-field="childrenField"
    @update:checked-keys="onChecked"
    @update:selected-keys="onSelected"
  />
</template>
