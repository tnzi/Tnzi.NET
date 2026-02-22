<script setup lang="ts">
import { ref, computed } from 'vue';
import { usePlaygroundTheme } from '../composables/usePlaygroundTheme';

import ThemeSection from '../sections/ThemeSection.vue';
import AdaptersSection from '../sections/AdaptersSection.vue';
import StoresSection from '../sections/StoresSection.vue';
import AuthSection from '../sections/AuthSection.vue';
import FormsSection from '../sections/FormsSection.vue';
import DataSection from '../sections/DataSection.vue';
import CardsSection from '../sections/CardsSection.vue';
import NavigationSection from '../sections/NavigationSection.vue';
import NativeSection from '../sections/NativeSection.vue';
import IconsSection from '../sections/IconsSection.vue';

const { isDark, toggleDarkMode } = usePlaygroundTheme();

// TabBar 导航定义 (5 tabs)
const tabItems = [
  { key: 'theme', label: 'Theme', icon: 'brush-o' },
  { key: 'adapters', label: 'Adapters', icon: 'apps-o' },
  { key: 'components', label: 'Components', icon: 'orders-o' },
  { key: 'data', label: 'Data', icon: 'bar-chart-o' },
  { key: 'nav', label: 'Navigation', icon: 'guide-o' },
];

const activeTab = ref(0);

// NavBar 标题根据当前 tab 动态变化
const navTitle = computed(() => {
  const titles = ['Theme', 'Adapters & Stores', 'Auth & Forms', 'Data & Cards', 'Nav & Native & Icons'];
  return titles[activeTab.value] ?? 'Vant Playground';
});
</script>

<template>
  <div class="mobile-layout">
    <!-- 顶部导航栏 (flex-shrink: 0, 不滚动) -->
    <van-nav-bar :title="navTitle">
      <template #left>
        <span class="nav-brand">@tnzi/vant</span>
      </template>
      <template #right>
        <van-icon
          :name="isDark ? 'bulb-o' : 'moon-o'"
          size="20"
          @click="toggleDarkMode"
        />
      </template>
    </van-nav-bar>

    <!-- 内容区 (flex: 1, 仅此区域滚动) -->
    <div class="content">
      <ThemeSection v-show="activeTab === 0" />

      <template v-if="activeTab === 1">
        <AdaptersSection />
        <van-divider>Stores</van-divider>
        <StoresSection />
      </template>

      <template v-if="activeTab === 2">
        <AuthSection />
        <van-divider>Forms</van-divider>
        <FormsSection />
      </template>

      <template v-if="activeTab === 3">
        <DataSection />
        <van-divider>Cards</van-divider>
        <CardsSection />
      </template>

      <template v-if="activeTab === 4">
        <NavigationSection />
        <van-divider>Native Vant</van-divider>
        <NativeSection />
        <van-divider>Icons (Iconify)</van-divider>
        <IconsSection />
      </template>
    </div>

    <!-- 底部 TabBar (flex-shrink: 0, 不滚动) -->
    <van-tabbar v-model="activeTab">
      <van-tabbar-item
        v-for="(tab, index) in tabItems"
        :key="tab.key"
        :icon="tab.icon"
        :name="index"
      >
        {{ tab.label }}
      </van-tabbar-item>
    </van-tabbar>
  </div>
</template>

<style scoped>
.mobile-layout {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 100vh;
  background: var(--van-background, #f7f8fa);
}

.content {
  flex: 1;
  overflow-y: auto;
  padding: 4px 0;
}

.nav-brand {
  font-size: 13px;
  font-weight: 600;
  color: var(--van-nav-bar-title-text-color, #323233);
}

/* NavBar: 不使用 fixed, 通过 flex 布局固定在顶部 */
.mobile-layout :deep(.van-nav-bar) {
  flex-shrink: 0;
  height: 40px;
  line-height: 40px;
  position: static !important;
}

.mobile-layout :deep(.van-nav-bar__content) {
  height: 40px;
}

.mobile-layout :deep(.van-nav-bar__title) {
  font-size: 15px;
}

/* TabBar: 不使用 fixed, 通过 flex 布局固定在底部 */
.mobile-layout :deep(.van-tabbar) {
  flex-shrink: 0;
  position: static !important;
}
</style>
