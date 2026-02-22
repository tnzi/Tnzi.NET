<script setup lang="ts">
import type { ITabBarEmits, ITabBarProps } from '@tnzi/core/components';
import { Button } from '../primitive/ui';

const props = withDefaults(defineProps<ITabBarProps>(), {
  tabs: () => [],
  fixed: false,
  safeAreaInsetBottom: false,
});

const emit = defineEmits<ITabBarEmits>();

const tabBadge = (key: string, fallback?: number) => props.badge?.[key] ?? fallback;
</script>

<template>
  <nav
    class="border-t bg-background p-1"
    :class="[props.class, props.fixed ? 'sticky bottom-0 z-40' : '', props.safeAreaInsetBottom ? 'pb-3' : '']"
    :style="props.style"
  >
    <ul class="grid grid-cols-2 gap-1 sm:grid-cols-4">
      <li v-for="tab in props.tabs" :key="tab.key">
        <Button
          :variant="props.activeKey === tab.key ? 'default' : 'ghost'"
          size="sm"
          class="w-full justify-center"
          :disabled="tab.disabled"
          @click="emit('change', tab.key, tab)"
        >
          <span v-if="tab.icon" class="mr-1 text-xs">{{ tab.icon }}</span>
          <span>{{ tab.label }}</span>
          <span
            v-if="tabBadge(tab.key, tab.badge) != null"
            class="ml-1 rounded-full bg-destructive px-1.5 py-0.5 text-xs text-destructive-foreground"
          >
            {{ tabBadge(tab.key, tab.badge) }}
          </span>
        </Button>
      </li>
    </ul>
  </nav>
</template>
