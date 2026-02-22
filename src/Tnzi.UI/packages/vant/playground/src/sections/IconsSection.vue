<template>
  <div>
    <!-- Icon Collections -->
    <van-cell-group inset title="Icon Collections">
      <van-tabs v-model:active="activeTab" type="card" style="margin: 8px 0;">
        <van-tab v-for="col in collections" :key="col.prefix" :title="col.name">
          <div class="icon-grid">
            <div v-for="icon in col.icons" :key="icon" class="icon-cell" @click="copyIconName(`${col.prefix}:${icon}`)">
              <Icon :icon="`${col.prefix}:${icon}`" :width="22" :height="22" />
              <span class="icon-label">{{ icon }}</span>
            </div>
          </div>
        </van-tab>
      </van-tabs>
    </van-cell-group>

    <!-- Sizes -->
    <van-cell-group inset title="Sizes">
      <div class="size-row">
        <div v-for="size in [14, 18, 22, 28, 36]" :key="size" class="size-item">
          <Icon icon="material-symbols:favorite" :width="size" :height="size" style="color: var(--van-primary-color, #1989fa);" />
          <span class="size-label">{{ size }}px</span>
        </div>
      </div>
    </van-cell-group>

    <!-- Colors -->
    <van-cell-group inset title="Colors">
      <div class="color-row">
        <Icon icon="material-symbols:favorite" :width="26" :height="26" style="color: #1989fa;" />
        <Icon icon="material-symbols:check-circle" :width="26" :height="26" style="color: #07c160;" />
        <Icon icon="material-symbols:warning" :width="26" :height="26" style="color: #ff976a;" />
        <Icon icon="material-symbols:error" :width="26" :height="26" style="color: #ee0a24;" />
        <Icon icon="material-symbols:info" :width="26" :height="26" style="color: #1989fa;" />
        <Icon icon="material-symbols:star" :width="26" :height="26" style="color: #7232dd;" />
      </div>
    </van-cell-group>

    <!-- With Vant Components -->
    <van-cell-group inset title="With Components">
      <div style="padding: 12px 16px; display: flex; flex-wrap: wrap; gap: 8px;">
        <van-button type="primary" size="small" icon-position="left">
          <template #icon><Icon icon="material-symbols:add" :width="16" :height="16" style="margin-right: 2px;" /></template>
          Create
        </van-button>
        <van-button type="danger" size="small">
          <template #icon><Icon icon="material-symbols:delete-outline" :width="16" :height="16" style="margin-right: 2px;" /></template>
          Delete
        </van-button>
        <van-button type="success" size="small">
          <template #icon><Icon icon="material-symbols:check" :width="16" :height="16" style="margin-right: 2px;" /></template>
          Save
        </van-button>
      </div>
      <van-field left-icon="search" placeholder="Search with Vant icon" />
      <van-cell title="Custom right icon" is-link>
        <template #right-icon>
          <Icon icon="material-symbols:chevron-right" :width="20" :height="20" style="color: var(--van-cell-right-icon-color);" />
        </template>
      </van-cell>
      <van-cell title="With badge">
        <template #right-icon>
          <van-badge :content="5">
            <Icon icon="material-symbols:notifications" :width="20" :height="20" />
          </van-badge>
        </template>
      </van-cell>
    </van-cell-group>

    <!-- Icon Search -->
    <van-cell-group inset title="Icon Search">
      <van-field v-model="searchQuery" placeholder="Search icons..." clearable left-icon="search" />
      <div class="icon-grid" style="padding: 8px;">
        <div v-for="icon in filteredIcons" :key="icon" class="icon-cell" @click="copyIconName(icon)">
          <Icon :icon="icon" :width="22" :height="22" />
          <span class="icon-label">{{ icon.split(':')[1] }}</span>
        </div>
      </div>
      <van-empty v-if="filteredIcons.length === 0" description="No icons found" image="search" />
    </van-cell-group>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue';
import { Icon } from '@iconify/vue';
import { showToast } from 'vant';

const activeTab = ref(0);
const searchQuery = ref('');

const collections = [
  {
    name: 'Material',
    prefix: 'material-symbols',
    icons: ['home', 'search', 'settings', 'favorite', 'delete', 'add', 'edit', 'share', 'person', 'notifications', 'check-circle', 'info', 'warning', 'error', 'visibility'],
  },
  {
    name: 'Lucide',
    prefix: 'lucide',
    icons: ['home', 'search', 'settings', 'heart', 'trash-2', 'plus', 'edit', 'share-2', 'user', 'bell', 'check-circle', 'info', 'alert-triangle', 'x-circle', 'eye'],
  },
  {
    name: 'Carbon',
    prefix: 'carbon',
    icons: ['home', 'search', 'settings', 'favorite', 'trash-can', 'add', 'edit', 'share', 'user', 'notification', 'checkmark-filled', 'information', 'warning', 'error-filled', 'view'],
  },
];

const allIcons = [
  'material-symbols:home', 'material-symbols:search', 'material-symbols:settings',
  'material-symbols:favorite', 'material-symbols:delete', 'material-symbols:add',
  'material-symbols:edit', 'material-symbols:share', 'material-symbols:person',
  'material-symbols:notifications', 'material-symbols:check-circle', 'material-symbols:info',
  'material-symbols:warning', 'material-symbols:error', 'material-symbols:visibility',
  'material-symbols:lock', 'material-symbols:mail', 'material-symbols:phone',
  'material-symbols:calendar-today', 'material-symbols:schedule',
  'material-symbols:cloud-upload', 'material-symbols:cloud-download',
  'material-symbols:refresh', 'material-symbols:close', 'material-symbols:menu',
  'material-symbols:arrow-back', 'material-symbols:arrow-forward',
  'material-symbols:star', 'material-symbols:star-outline',
  'material-symbols:thumb-up', 'material-symbols:thumb-down',
  'material-symbols:chat', 'material-symbols:send',
  'material-symbols:image', 'material-symbols:videocam',
  'material-symbols:download', 'material-symbols:upload',
  'material-symbols:save', 'material-symbols:content-copy',
  'material-symbols:dashboard', 'material-symbols:bar-chart',
  'material-symbols:code', 'material-symbols:terminal',
];

const filteredIcons = computed(() => {
  if (!searchQuery.value) return allIcons;
  const q = searchQuery.value.toLowerCase();
  return allIcons.filter(icon => icon.toLowerCase().includes(q));
});

function copyIconName(name: string) {
  navigator.clipboard.writeText(name).then(() => {
    showToast({ message: `Copied: ${name}`, position: 'bottom' });
  });
}
</script>

<style scoped>
.icon-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(72px, 1fr));
  gap: 4px;
  padding: 8px 12px;
}

.icon-cell {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 4px;
  padding: 10px 2px;
  border-radius: 8px;
  cursor: pointer;
  transition: background-color 0.15s;
}

.icon-cell:active {
  background-color: rgba(25, 137, 250, 0.08);
}

.icon-label {
  font-size: 9px;
  color: var(--van-text-color-2, #969799);
  text-align: center;
  word-break: break-all;
  line-height: 1.2;
}

.size-row {
  display: flex;
  align-items: flex-end;
  gap: 20px;
  padding: 16px;
}

.size-item {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
}

.size-label {
  font-size: 10px;
  color: var(--van-text-color-3, #c8c9cc);
}

.color-row {
  display: flex;
  align-items: center;
  gap: 20px;
  padding: 16px;
}
</style>
