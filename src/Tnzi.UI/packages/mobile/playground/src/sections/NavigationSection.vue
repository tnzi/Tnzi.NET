<script setup lang="ts">
import { ref } from 'vue';
import { showToast } from 'vant';
import { demoMenuItems, demoTabs } from '../data';

// TNavBar
const onNavBack = () => {
  showToast('Back pressed');
};

// TMenu
const activeMenuKey = ref('dashboard');
const openedMenuKeys = ref<string[]>([]);
const onMenuSelect = (key: string) => {
  activeMenuKey.value = key;
  showToast(`Menu: ${key}`);
};
const onMenuOpenChange = (keys: string[]) => {
  openedMenuKeys.value = keys;
};

// TTabBar
const activeTabKey = ref('home');
const onTabChange = (key: string) => {
  activeTabKey.value = key;
  showToast(`Tab: ${key}`);
};
</script>

<template>
  <div class="navigation-section">
    <!-- TNavBar -->
    <van-cell-group inset title="TNavBar">
      <div class="component-wrapper">
        <TNavBar title="Page Title" :show-back="true" @back="onNavBack" />
      </div>
      <div class="component-wrapper">
        <TNavBar title="No Back Button" :show-back="false" />
      </div>
    </van-cell-group>

    <!-- TMenu -->
    <van-cell-group inset title="TMenu">
      <div class="component-wrapper">
        <TMenu
          :items="demoMenuItems"
          :active-key="activeMenuKey"
          :opened-keys="openedMenuKeys"
          @select="onMenuSelect"
          @open-change="onMenuOpenChange"
        />
      </div>
    </van-cell-group>

    <!-- TTabBar -->
    <van-cell-group inset title="TTabBar (inline demo)">
      <div class="component-wrapper tabbar-demo">
        <TTabBar
          v-model:active-key="activeTabKey"
          :tabs="demoTabs"
          @change="onTabChange"
        />
      </div>
    </van-cell-group>
  </div>
</template>

<style scoped>
.navigation-section {
  padding-bottom: 8px;
}

.component-wrapper {
  padding: 4px 0;
}

.tabbar-demo {
  position: relative;
  height: 60px;
  overflow: hidden;
}
</style>
