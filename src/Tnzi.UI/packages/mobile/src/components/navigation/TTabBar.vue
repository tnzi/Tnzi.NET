<script setup lang="ts">
import type { ITabItem } from '@tnzi/core/types/shared-ui';

interface ITabBarProps {
  tabs?: ITabItem[];
  badge?: Record<string, number>;
  fixed?: boolean;
  safeAreaInsetBottom?: boolean;
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

// `activeKey` is declared by defineModel, not by defineProps: declaring it in
// both would shadow the model with a static prop.
const activeKey = defineModel<string>('activeKey', { default: '' });

const tabBadge = (key: string, fallback?: number) => props.badge?.[key] ?? fallback;

const onChange = (key: string | number) => {
  const value = String(key);
  activeKey.value = value;
  const tab = props.tabs.find((item) => item.key === value);
  if (tab) emit('change', value, tab as ITabItem);
};
</script>

<template>
  <van-tabbar
    :model-value="activeKey"
    :fixed="props.fixed"
    :safe-area-inset-bottom="props.safeAreaInsetBottom"
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
