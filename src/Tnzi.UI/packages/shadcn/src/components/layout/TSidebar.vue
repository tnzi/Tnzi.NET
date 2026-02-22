<script setup lang="ts">
import { computed, ref } from 'vue';
import type { IMenuItem, ISidebarEmits, ISidebarProps } from '@tnzi/core/components';
import { Button } from '../primitive/ui';

const props = withDefaults(defineProps<ISidebarProps>(), {
  items: () => [],
  collapsed: false,
  dark: false,
  logoText: 'Tnzi',
  width: 256,
  collapsedWidth: 64,
});

const emit = defineEmits<ISidebarEmits>();

const openKeys = ref<string[]>([]);
const activeKey = ref<string>('');

const filteredItems = computed(() => props.items.filter((item) => !item.hidden));

const isOpen = (key: string) => openKeys.value.includes(key);

const toggleOpen = (key: string) => {
  if (isOpen(key)) {
    openKeys.value = openKeys.value.filter((item) => item !== key);
  } else {
    openKeys.value = [...openKeys.value, key];
  }
};

const selectItem = (item: IMenuItem) => {
  activeKey.value = item.key;
  emit('menuClick', item);
};
</script>

<template>
  <aside class="flex h-full flex-col" :class="props.class" :style="props.style">
    <div class="flex h-14 items-center justify-between border-b px-3">
      <div class="flex min-w-0 items-center gap-2" :class="props.collapsed ? 'justify-center' : ''">
        <img v-if="props.logo" :src="props.logo" alt="logo" class="h-7 w-7 rounded" />
        <span v-else-if="!props.collapsed" class="truncate text-sm font-semibold">{{ props.logoText }}</span>
      </div>
      <Button variant="ghost" size="sm" @click="emit('collapseChange', !props.collapsed)">
        {{ props.collapsed ? '>' : '<' }}
      </Button>
    </div>

    <nav class="flex-1 overflow-auto p-2">
      <ul class="space-y-1">
        <li v-for="item in filteredItems" :key="item.key">
          <button
            type="button"
            class="flex w-full items-center gap-2 rounded-md px-2 py-2 text-sm transition hover:bg-accent"
            :class="activeKey === item.key ? 'bg-accent text-foreground' : 'text-muted-foreground'"
            @click="item.children?.length ? toggleOpen(item.key) : selectItem(item)"
          >
            <span v-if="item.icon" class="shrink-0">{{ item.icon }}</span>
            <span v-if="!props.collapsed" class="min-w-0 flex-1 truncate text-left">{{ item.label }}</span>
            <span v-if="!props.collapsed && item.badge" class="rounded-full bg-destructive px-1.5 py-0.5 text-xs text-destructive-foreground">
              {{ item.badge }}
            </span>
            <span v-if="!props.collapsed && item.children?.length" class="text-xs">{{ isOpen(item.key) ? '▼' : '▶' }}</span>
          </button>

          <ul v-if="!props.collapsed && item.children?.length && isOpen(item.key)" class="mt-1 space-y-1 pl-6">
            <li v-for="child in item.children" :key="child.key">
              <button
                type="button"
                class="w-full rounded-md px-2 py-1.5 text-left text-sm transition hover:bg-accent"
                :class="activeKey === child.key ? 'bg-accent text-foreground' : 'text-muted-foreground'"
                @click="selectItem(child)"
              >
                {{ child.label }}
              </button>
            </li>
          </ul>
        </li>
      </ul>
    </nav>
  </aside>
</template>
