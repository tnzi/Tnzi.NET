<script setup lang="ts">
import { computed } from 'vue'
import { NTree } from 'naive-ui'
import type { TreeOption, TreeDropInfo } from 'naive-ui'
import type { AdminMenuItem } from '../../stores/useAdminRouteStore'

interface Props {
  data: AdminMenuItem[]
}

const props = defineProps<Props>()
const treeData = computed(() => props.data as unknown as TreeOption[])

const emit = defineEmits<{
  reorder: [tree: AdminMenuItem[]]
}>()

// Naive UI mutates the tree in-place on drop; re-emit the (now updated) data ref
function onDrop(_payload: TreeDropInfo) {
  emit('reorder', props.data)
}
</script>

<template>
  <NTree
    class="t-menu-tree"
    :data="treeData"
    :draggable="true"
    key-field="key"
    label-field="label"
    children-field="children"
    block-line
    @drop="onDrop"
  />
</template>
