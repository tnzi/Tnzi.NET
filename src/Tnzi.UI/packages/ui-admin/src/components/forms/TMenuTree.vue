<script setup lang="ts">
import { computed } from 'vue'
import { NTree } from 'naive-ui'
import type { TreeOption, TreeDropInfo } from 'naive-ui'
import type { AdminMenuItem } from '../../stores/useAdminRouteStore'
import { useBreakpoint } from '../../headless/useBreakpoint'

interface Props {
  data: AdminMenuItem[]
}

const props = defineProps<Props>()
const treeData = computed(() => props.data as unknown as TreeOption[])

// Touch/phone (isSm): disable drag-reorder — dragging a block-line tree node
// on touch is unreliable and fights the vertical scroll. Reordering stays a
// desktop affordance.
const { isSm } = useBreakpoint()

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
    :draggable="!isSm"
    key-field="key"
    label-field="label"
    children-field="children"
    block-line
    @drop="onDrop"
  />
</template>

<style scoped>
/* Deep trees indent horizontally; cap overflow with a scroll fallback so a
   narrow container never gets pushed out sideways. */
.t-menu-tree {
  overflow-x: auto;
}
</style>
