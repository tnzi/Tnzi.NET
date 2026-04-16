<script setup lang="ts">
import { computed } from 'vue'
import { NTree } from 'naive-ui'
import type { TreeOption } from 'naive-ui'

export interface PermissionNode {
  id: string
  name: string
  children?: PermissionNode[]
}

interface Props {
  data: PermissionNode[]
  checkedKeys: string[]
}

const props = defineProps<Props>()
const treeData = computed(() => props.data as unknown as TreeOption[])

const emit = defineEmits<{
  'update:checkedKeys': [keys: string[]]
  change: [keys: string[]]
}>()

function onUpdate(keys: string[]) {
  emit('update:checkedKeys', keys)
  emit('change', keys)
}
</script>

<template>
  <NTree
    class="t-permission-tree"
    :data="treeData"
    :checked-keys="checkedKeys"
    checkable
    cascade
    key-field="id"
    label-field="name"
    children-field="children"
    @update:checked-keys="onUpdate"
  />
</template>
