<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import type { IMenuItem } from '@tnzi/core/types/shared-ui';
import { Button } from '../primitive/ui';

interface IMenuProps {
  items: IMenuItem[];
  activeKey?: string;
  openedKeys?: string[];
  horizontal?: boolean;
  mode?: 'light' | 'dark';
  class?: string | string[];
  style?: string | Record<string, string | number>;
}

interface IMenuEmits {
  select: [key: string, item: IMenuItem];
  openChange: [keys: string[]];
}

const props = withDefaults(defineProps<IMenuProps>(), {
  items: () => [],
  openedKeys: () => [],
  horizontal: false,
  mode: 'light',
});

const emit = defineEmits<IMenuEmits>();

const internalOpenKeys = ref<string[]>([...(props.openedKeys ?? [])]);

watch(
  () => props.openedKeys,
  (value) => {
    internalOpenKeys.value = [...(value ?? [])];
  },
);

const visibleItems = computed(() => props.items.filter((item) => !item.hidden));

const isOpen = (key: string) => internalOpenKeys.value.includes(key);

const toggleOpen = (key: string) => {
  if (isOpen(key)) {
    internalOpenKeys.value = internalOpenKeys.value.filter((item) => item !== key);
  } else {
    internalOpenKeys.value = [...internalOpenKeys.value, key];
  }
  emit('openChange', internalOpenKeys.value);
};

const selectItem = (item: IMenuItem) => {
  if (item.disabled) return;
  emit('select', item.key, item);
};
</script>

<template>
  <nav class="t-menu" :class="[props.class, props.horizontal ? 'w-full' : '']" :style="props.style">
    <ul :class="props.horizontal ? 'flex flex-wrap items-center gap-2' : 'space-y-1'">
      <li v-for="item in visibleItems" :key="item.key">
        <Button
          :variant="props.activeKey === item.key ? 'default' : 'ghost'"
          size="sm"
          class="w-full justify-start"
          :disabled="item.disabled"
          @click="item.children?.length ? toggleOpen(item.key) : selectItem(item)"
        >
          <span v-if="item.icon" class="mr-2 text-xs">{{ item.icon }}</span>
          <span class="truncate">{{ item.label }}</span>
          <span v-if="item.badge" class="ml-2 rounded-full bg-destructive px-1.5 py-0.5 text-xs text-destructive-foreground">
            {{ item.badge }}
          </span>
          <span v-if="item.children?.length" class="ml-auto text-xs opacity-70">{{ isOpen(item.key) ? '▼' : '▶' }}</span>
        </Button>

        <ul v-if="item.children?.length && isOpen(item.key)" class="mt-1 space-y-1 pl-4">
          <li v-for="child in item.children" :key="child.key">
            <Button
              :variant="props.activeKey === child.key ? 'secondary' : 'ghost'"
              size="sm"
              class="w-full justify-start"
              :disabled="child.disabled"
              @click="selectItem(child)"
            >
              {{ child.label }}
            </Button>
          </li>
        </ul>
      </li>
    </ul>
  </nav>
</template>
