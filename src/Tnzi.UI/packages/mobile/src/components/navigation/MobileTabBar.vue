<script setup lang="ts">
import type { ITabItem } from '@tnzi/core/types/shared-ui';

interface ITabBarProps {
  tabs: ITabItem[];
  activeKey?: string;
  badge?: Record<string, number>;
  fixed?: boolean;
  safeAreaInsetBottom?: boolean;
  class?: string | string[];
  style?: string | Record<string, string | number>;
}

interface ITabBarEmits {
  change: [key: string, tab: ITabItem];
}

const props = withDefaults(defineProps<ITabBarProps>(), {
  tabs: () => [],
  fixed: true,
  safeAreaInsetBottom: true,
});

const emit = defineEmits<ITabBarEmits>();

const model = defineModel<string>('activeKey', { default: '' });

const tabBadge = (key: string, fallback?: number) => props.badge?.[key] ?? fallback;

const onChange = (key: string | number) => {
  const value = String(key);
  model.value = value;
  const tab = props.tabs.find((item) => item.key === value);
  if (tab) emit('change', value, tab as ITabItem);
};
</script>

<template>
  <van-tabbar
    :model-value="model || props.activeKey"
    :fixed="props.fixed"
    :safe-area-inset-bottom="props.safeAreaInsetBottom"
    :class="props.class"
    :style="props.style"
    @change="onChange"
  >
    <van-tabbar-item
      v-for="tab in props.tabs"
      :key="tab.key"
      :name="tab.key"
      :icon="tab.icon"
      :badge="tabBadge(tab.key, tab.badge)"
      :disabled="tab.disabled"
    >
      {{ tab.label }}
    </van-tabbar-item>
  </van-tabbar>
</template>
