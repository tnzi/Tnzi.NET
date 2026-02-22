<template>
  <div class="section-page">
    <div class="section-header">
      <h1 class="section-title">Navigation</h1>
      <p class="section-desc">Menu, breadcrumb, tabs, and tab bar components for app navigation.</p>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">Menu</h2>
      <PreviewBox>
        <div style="max-width: 260px; width: 100%">
          <n-card size="small">
            <TMenu
              :items="demoMenuItems"
              :active-key="menuValue ?? undefined"
              @select="handleMenuSelect"
            />
          </n-card>
          <n-text v-if="lastMenuKey" depth="3" style="display: block; margin-top: 8px">
            Selected: {{ lastMenuKey }}
          </n-text>
        </div>
      </PreviewBox>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">Breadcrumb</h2>
      <PreviewBox>
        <n-breadcrumb>
          <n-breadcrumb-item v-for="item in demoBreadcrumbs" :key="item.key">
            <span
              :style="{ cursor: item.clickable ? 'pointer' : 'default' }"
              @click="item.clickable && handleBreadcrumbClick(item.label)"
            >
              {{ item.label }}
            </span>
          </n-breadcrumb-item>
        </n-breadcrumb>
      </PreviewBox>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">Tab Bar</h2>
      <PreviewBox>
        <div style="width: 100%; max-width: 400px;">
          <div class="tabbar-demo">
            <div
              v-for="tab in tabBarItems"
              :key="tab.key"
              class="tabbar-demo__item"
              :class="{ active: tab.key === activeTabKey }"
              @click="activeTabKey = tab.key"
            >
              <n-icon :size="20">
                <Icon :icon="tab.icon" />
              </n-icon>
              <span>{{ tab.label }}</span>
            </div>
          </div>
          <n-text depth="3" style="display: block; margin-top: 8px; text-align: center;">
            Active: {{ activeTabKey }}
          </n-text>
        </div>
      </PreviewBox>
    </div>

    <div class="demo-block">
      <h2 class="demo-label">Tabs (Card Style)</h2>
      <PreviewBox full-width>
        <n-tabs v-model:value="cardTab" type="card">
          <n-tab-pane name="overview" tab="Overview">
            <n-p>Overview content goes here.</n-p>
          </n-tab-pane>
          <n-tab-pane name="analytics" tab="Analytics">
            <n-p>Analytics dashboard content.</n-p>
          </n-tab-pane>
          <n-tab-pane name="reports" tab="Reports">
            <n-p>Report listings and exports.</n-p>
          </n-tab-pane>
        </n-tabs>
      </PreviewBox>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { useMessage } from 'naive-ui';
import { Icon } from '@iconify/vue';
import { demoMenuItems, demoBreadcrumbs } from '@tnzi/core/playground';
import { TMenu, createNaiveMessageAdapter } from '@tnzi/naive-ui';
import PreviewBox from '../components/PreviewBox.vue';

const message = useMessage();
const msgAdapter = createNaiveMessageAdapter(message);

// Menu
const menuValue = ref<string | null>(null);
const lastMenuKey = ref('');

function handleMenuSelect(key: string) {
  menuValue.value = key;
  lastMenuKey.value = key;
}

// Breadcrumb
function handleBreadcrumbClick(label: string) {
  msgAdapter.info(`Navigate to: ${label}`);
}

// TabBar
const tabBarItems = [
  { key: 'home', label: 'Home', icon: 'material-symbols:home' },
  { key: 'orders', label: 'Orders', icon: 'material-symbols:receipt-long' },
  { key: 'messages', label: 'Messages', icon: 'material-symbols:chat' },
  { key: 'profile', label: 'Profile', icon: 'material-symbols:person' },
];
const activeTabKey = ref('home');
const cardTab = ref('overview');
</script>

<style scoped>
.section-page {
  max-width: 960px;
}
.section-header {
  margin-bottom: 32px;
}
.section-title {
  font-size: 32px;
  font-weight: 800;
  color: var(--pg-text, #0f172a);
  margin: 0 0 8px 0;
}
.section-desc {
  font-size: 15px;
  color: var(--pg-text-muted, #64748b);
  margin: 0;
  line-height: 1.6;
}
.demo-block {
  margin-bottom: 32px;
}
.demo-label {
  font-size: 16px;
  font-weight: 600;
  color: var(--pg-text, #0f172a);
  margin: 0 0 12px 0;
}

.tabbar-demo {
  display: flex;
  justify-content: space-around;
  align-items: center;
  height: 52px;
  border: 1px solid var(--pg-border, #e2e8f0);
  border-radius: 10px;
  background: var(--pg-panel-bg, #fff);
}

.tabbar-demo__item {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 2px;
  flex: 1;
  cursor: pointer;
  color: var(--pg-text-muted, #94a3b8);
  font-size: 11px;
  transition: color 0.2s;
  user-select: none;
}

.tabbar-demo__item.active {
  color: var(--pg-primary, #6366f1);
}
</style>
