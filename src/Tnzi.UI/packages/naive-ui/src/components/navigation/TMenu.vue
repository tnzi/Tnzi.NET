<script setup lang="ts">
import { computed } from 'vue'
import { NMenu } from 'naive-ui'
import type { MenuOption } from 'naive-ui'
import type { IMenuItem, IMenuProps } from '@tnzi/core'

interface Props {
  items: IMenuItem[]
  activeKey?: string
  openedKeys?: string[]
  horizontal?: boolean
  mode?: 'light' | 'dark'
}

const props = withDefaults(defineProps<Props>(), {
  horizontal: false,
  mode: 'light',
})

const emit = defineEmits<{
  select: [key: string, item: IMenuItem]
  openChange: [keys: string[]]
}>()

function convertToMenuOptions(items: IMenuItem[]): MenuOption[] {
  return items
    .filter((item) => !item.hidden)
    .map((item) => {
      const option: MenuOption = {
        key: item.key,
        label: item.label,
        disabled: item.disabled,
      }
      if (item.children && item.children.length > 0) {
        option.children = convertToMenuOptions(item.children)
      }
      return option
    })
}

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
  if (item) {
    emit('select', key, item)
  }
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
