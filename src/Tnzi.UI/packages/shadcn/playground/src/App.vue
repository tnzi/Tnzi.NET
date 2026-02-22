<template>
  <div class="pg-app-root">
    <!-- Sonner Toaster -->
    <Toaster position="top-right" :duration="3000" rich-colors />
    <!-- Dialog Provider (programmatic dialog support) -->
    <TDialogProvider />

    <div class="pg-main-layout">
      <!-- 深色侧边栏 -->
      <aside class="pg-sidebar">
        <div class="logo-area">
          <div class="logo-box">
            <span>T</span>
          </div>
          <span class="logo-text">Tnzi UI</span>
        </div>
        <nav class="nav-container">
          <div v-for="group in menuGroups" :key="group.label" class="menu-group">
            <div class="menu-group-title">{{ group.label }}</div>
            <ul class="menu-list">
              <li v-for="item in group.items" :key="item.key">
                <button
                  type="button"
                  class="menu-item"
                  :class="{ active: activeKey === item.key }"
                  @click="activeKey = item.key"
                >
                  <Icon :icon="item.icon" :width="18" :height="18" class="menu-icon" />
                  <span>{{ item.label }}</span>
                </button>
              </li>
            </ul>
          </div>
        </nav>
      </aside>

      <!-- 右侧内容区 -->
      <div class="pg-content-area">
        <!-- 顶部 Header -->
        <header class="pg-header">
          <div class="pg-header-left">
            <div class="breadcrumb">
              <span>{{ breadcrumbGroup }}</span>
              <span class="breadcrumb-sep">/</span>
              <span class="current">{{ activeLabel }}</span>
            </div>
          </div>
          <div class="pg-header-right">
            <button class="theme-toggle" @click="toggleTheme">
              <Icon :icon="resolvedMode === 'dark' ? 'material-symbols:light-mode' : 'material-symbols:dark-mode'" :width="18" :height="18" />
            </button>
          </div>
        </header>

        <!-- 主体内容（可滚动） -->
        <main class="pg-main">
          <ThemeSection v-if="activeKey === 'theme'" />
          <AdaptersSection v-else-if="activeKey === 'adapters'" />
          <StoresSection v-else-if="activeKey === 'stores'" />
          <AuthSection v-else-if="activeKey === 'auth'" />
          <FormsSection v-else-if="activeKey === 'forms'" />
          <DataSection v-else-if="activeKey === 'data'" />
          <CardsSection v-else-if="activeKey === 'cards'" />
          <NavigationSection v-else-if="activeKey === 'navigation'" />
          <LayoutSection v-else-if="activeKey === 'layout'" />
          <ExamplesSection v-else-if="activeKey === 'examples'" />
          <DocsSection v-else-if="activeKey === 'docs'" />
          <IconsSection v-else-if="activeKey === 'icons'" />
        </main>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue';
import { Icon } from '@iconify/vue';
import { Toaster } from 'vue-sonner';
import { TDialogProvider } from '@tnzi/shadcn';
import { getSectionsForPlatform, resolveThemeMode } from '@tnzi/core/playground';
import { usePlaygroundTheme } from './composables/usePlaygroundTheme';
import ThemeSection from './sections/ThemeSection.vue';
import AdaptersSection from './sections/AdaptersSection.vue';
import StoresSection from './sections/StoresSection.vue';
import AuthSection from './sections/AuthSection.vue';
import FormsSection from './sections/FormsSection.vue';
import DataSection from './sections/DataSection.vue';
import CardsSection from './sections/CardsSection.vue';
import NavigationSection from './sections/NavigationSection.vue';
import LayoutSection from './sections/LayoutSection.vue';
import ExamplesSection from './sections/ExamplesSection.vue';
import DocsSection from './sections/DocsSection.vue';
import IconsSection from './sections/IconsSection.vue';

const iconMap: Record<string, string> = {
  theme: 'material-symbols:palette',
  adapters: 'material-symbols:extension',
  stores: 'material-symbols:storage',
  icons: 'material-symbols:emoji-symbols',
  auth: 'material-symbols:login',
  forms: 'material-symbols:edit-note',
  data: 'material-symbols:table-chart',
  cards: 'material-symbols:badge',
  navigation: 'material-symbols:menu',
  layout: 'material-symbols:dashboard',
  examples: 'material-symbols:widgets',
  docs: 'material-symbols:open-in-new',
};

const menuGroups = [
  {
    label: 'PLAYGROUND',
    items: [
      { key: 'theme', label: 'Theme', icon: iconMap.theme },
      { key: 'adapters', label: 'Adapters', icon: iconMap.adapters },
      { key: 'stores', label: 'Stores', icon: iconMap.stores },
      { key: 'icons', label: 'Icons', icon: iconMap.icons },
    ],
  },
  {
    label: 'COMPONENTS',
    items: [
      { key: 'auth', label: 'Auth', icon: iconMap.auth },
      { key: 'forms', label: 'Forms', icon: iconMap.forms },
      { key: 'data', label: 'Data', icon: iconMap.data },
      { key: 'cards', label: 'Cards', icon: iconMap.cards },
      { key: 'navigation', label: 'Navigation', icon: iconMap.navigation },
      { key: 'layout', label: 'Layout', icon: iconMap.layout },
    ],
  },
  {
    label: 'SHADCN UI',
    items: [
      { key: 'examples', label: 'Examples', icon: iconMap.examples },
      { key: 'docs', label: 'Documentation', icon: iconMap.docs },
    ],
  },
];

const { themeConfig, updateMode } = usePlaygroundTheme();

const activeKey = ref('theme');

const resolvedMode = computed(() => resolveThemeMode(themeConfig.value.mode));

function toggleTheme() {
  updateMode(resolvedMode.value === 'dark' ? 'light' : 'dark');
}

const activeLabel = computed(() => {
  for (const group of menuGroups) {
    const item = group.items.find(i => i.key === activeKey.value);
    if (item) return item.label;
  }
  return '';
});

const breadcrumbGroup = computed(() => {
  const key = activeKey.value;
  if (['theme', 'adapters', 'stores', 'icons'].includes(key)) return 'Playground';
  if (['examples', 'docs'].includes(key)) return 'Shadcn UI';
  return 'Components';
});
</script>

<style>
/* ===== 全局 CSS 变量系统 ===== */
:root {
  --pg-bg: #f8fafc;
  --pg-header-bg: #ffffff;
  --pg-border: #e2e8f0;
  --pg-border-light: #f1f5f9;
  --pg-text: #0f172a;
  --pg-text-muted: #64748b;
  --pg-panel-bg: #ffffff;
  --pg-button-bg: #f1f5f9;
  --pg-code-bg: #0f172a;
  --pg-sidebar-bg: #0f172a;
  --pg-sidebar-border: #1e293b;
  --pg-on-primary: #ffffff;
  --pg-preview-bg: #ffffff;
  --pg-preview-content-bg: #f8fafc;
  --pg-preview-grid: #e2e8f0;
  --pg-primary: #6366f1;
  --pg-primary-rgb: 99, 102, 241;
  --pg-card-shadow: 0 1px 3px rgba(0, 0, 0, 0.06), 0 1px 2px rgba(0, 0, 0, 0.04);
}

html.dark {
  --pg-bg: #0f172a;
  --pg-header-bg: #1e293b;
  --pg-border: #334155;
  --pg-border-light: #334155;
  --pg-text: #f1f5f9;
  --pg-text-muted: #94a3b8;
  --pg-panel-bg: #1e293b;
  --pg-button-bg: #334155;
  --pg-preview-bg: #1e293b;
  --pg-preview-content-bg: #0f172a;
  --pg-preview-grid: #334155;
  --pg-card-shadow: 0 1px 3px rgba(0, 0, 0, 0.25);
}

/* 全局滚动条 */
::-webkit-scrollbar {
  width: 6px;
  height: 6px;
}
::-webkit-scrollbar-track {
  background: transparent;
}
::-webkit-scrollbar-thumb {
  background: rgba(148, 163, 184, 0.2);
  border-radius: 10px;
}
::-webkit-scrollbar-thumb:hover {
  background: rgba(148, 163, 184, 0.4);
}

body {
  margin: 0;
  background-color: var(--pg-bg);
}
</style>

<style scoped>
/* ===== 根布局 ===== */
.pg-app-root {
  height: 100vh;
  overflow: hidden;
}

.pg-main-layout {
  display: flex;
  height: 100vh;
}

/* ===== 深色侧边栏 ===== */
.pg-sidebar {
  flex-shrink: 0;
  width: 240px;
  display: flex;
  flex-direction: column;
  background-color: var(--pg-sidebar-bg);
  border-right: 1px solid var(--pg-sidebar-border);
  overflow: hidden;
}

.logo-area {
  padding: 20px 20px 16px;
  display: flex;
  align-items: center;
  gap: 10px;
}

.logo-box {
  width: 32px;
  height: 32px;
  background: linear-gradient(135deg, #818cf8, #6366f1);
  border-radius: 8px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.logo-box span {
  color: white;
  font-size: 16px;
  font-weight: 800;
}

.logo-text {
  font-size: 16px;
  font-weight: 700;
  color: #ffffff;
  letter-spacing: -0.01em;
}

.nav-container {
  flex: 1;
  overflow-y: auto;
  padding: 0 10px;
}

.nav-container::-webkit-scrollbar-thumb {
  background: rgba(255, 255, 255, 0.1);
}
.nav-container::-webkit-scrollbar-thumb:hover {
  background: rgba(255, 255, 255, 0.2);
}

.menu-group {
  margin-bottom: 4px;
}

.menu-group-title {
  font-size: 10px;
  font-weight: 700;
  letter-spacing: 0.1em;
  color: rgba(100, 116, 139, 0.5);
  padding: 20px 8px 4px;
  text-transform: uppercase;
}

.menu-list {
  list-style: none;
  margin: 0;
  padding: 0;
}

.menu-item {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  height: 36px;
  padding: 0 8px;
  border: none;
  border-radius: 8px;
  background: transparent;
  color: rgba(148, 163, 184, 0.85);
  font-size: 13px;
  cursor: pointer;
  transition: all 0.15s;
}

.menu-item:hover {
  color: #ffffff;
  background: rgba(255, 255, 255, 0.06);
}

.menu-item.active {
  color: #ffffff;
  background: rgba(129, 140, 248, 0.15);
}

.menu-item.active:hover {
  background: rgba(129, 140, 248, 0.2);
}

.menu-icon {
  color: rgba(148, 163, 184, 0.7);
  flex-shrink: 0;
}

.menu-item:hover .menu-icon {
  color: #ffffff;
}

.menu-item.active .menu-icon {
  color: #818cf8;
}

/* ===== 内容区 ===== */
.pg-content-area {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-width: 0;
  overflow: hidden;
}

/* ===== 顶部 Header ===== */
.pg-header {
  flex-shrink: 0;
  height: 56px;
  background-color: var(--pg-header-bg);
  border-bottom: 1px solid var(--pg-border);
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 24px;
}

.pg-header-left {
  display: flex;
  align-items: center;
}

.pg-header-right {
  display: flex;
  align-items: center;
  gap: 8px;
}

.breadcrumb {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 14px;
  color: var(--pg-text-muted);
}

.breadcrumb .breadcrumb-sep {
  color: var(--pg-border);
}

.breadcrumb .current {
  color: var(--pg-text);
  font-weight: 600;
}

.theme-toggle {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  border: none;
  border-radius: 6px;
  background: transparent;
  color: var(--pg-text-muted);
  cursor: pointer;
  transition: all 0.15s;
}

.theme-toggle:hover {
  background: var(--pg-button-bg);
  color: var(--pg-text);
}

/* ===== 主体内容区 ===== */
.pg-main {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  background-color: var(--pg-bg);
  padding: 32px;
}
</style>
