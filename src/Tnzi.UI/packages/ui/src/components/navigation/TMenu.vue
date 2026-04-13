<script setup lang="ts">
import { computed } from 'vue'
import { NMenu } from 'naive-ui'
import type { IMenuItem } from '@tnzi/core'
import { convertToMenuOptions } from '../../utils/naive-helpers'

interface IMenuProps {
  items: IMenuItem[];
  activeKey?: string;
  openedKeys?: string[];
  horizontal?: boolean;
  mode?: 'light' | 'dark';
  class?: string | string[];
  style?: string | Record<string, string | number>;
}

const props = withDefaults(defineProps<IMenuProps>(), {
  horizontal: false,
  mode: 'light',
})

const emit = defineEmits<{
  select: [key: string, item: IMenuItem]
  openChange: [keys: string[]]
}>()

const menuOptions = computed(() => convertToMenuOptions(props.items))

function findMenuItem(items: IMenuItem[], key: string): IMenuItem | undefined {
  for (const item of items) {
    if (item.key === key) return item
    if (item.children) {
      const found = findMenuItem(item.children, key)
      if (found) return found
    }
  }
  return undefined
}

function handleSelect(key: string) {
  const item = findMenuItem(props.items, key)
  if (!item) return
  emit('select', key, item)
}

function handleExpandedKeysUpdate(keys: string[]) {
  emit('openChange', keys)
}
</script>

<template>
  <NMenu
    :value="activeKey"
    :options="menuOptions"
    :expanded-keys="openedKeys"
    :mode="horizontal ? 'horizontal' : 'vertical'"
    :inverted="mode === 'dark'"
    @update:value="handleSelect"
    @update:expanded-keys="handleExpandedKeysUpdate"
  />
</template>
