<template>
  <div class="section-page">
    <div class="section-header">
      <h1 class="section-title">Icons</h1>
      <p class="section-desc">
        Tnzi UI uses <a href="https://iconify.design" target="_blank" rel="noopener">Iconify</a> for unified icon access.
        Over 200,000 icons from 200+ collections, loaded on-demand.
      </p>
    </div>

    <!-- Icon Collections -->
    <div class="demo-block">
      <h2>Icon Collections</h2>
      <p class="demo-desc">Popular icon sets available through Iconify.</p>
      <PreviewBox :no-badge="true">
        <div style="width: 100%; padding: 16px;">
          <n-tabs type="segment" size="small" v-model:value="activeCollection">
            <n-tab-pane v-for="col in collections" :key="col.prefix" :name="col.prefix" :tab="col.name">
              <div class="icon-grid">
                <div v-for="icon in col.icons" :key="icon" class="icon-item" @click="copyIconName(`${col.prefix}:${icon}`)">
                  <n-icon :size="24"><Icon :icon="`${col.prefix}:${icon}`" /></n-icon>
                  <span class="icon-name">{{ icon }}</span>
                </div>
              </div>
            </n-tab-pane>
          </n-tabs>
        </div>
      </PreviewBox>
    </div>

    <!-- Sizes -->
    <div class="demo-block">
      <h2>Sizes</h2>
      <p class="demo-desc">Control icon size through the <code>size</code> prop on <code>&lt;n-icon&gt;</code>.</p>
      <PreviewBox>
        <div style="display: flex; align-items: center; gap: 24px;">
          <div v-for="size in [16, 20, 24, 32, 48]" :key="size" style="text-align: center;">
            <n-icon :size="size"><Icon icon="material-symbols:favorite" /></n-icon>
            <div class="size-label">{{ size }}px</div>
          </div>
        </div>
      </PreviewBox>
    </div>

    <!-- Colors -->
    <div class="demo-block">
      <h2>Colors</h2>
      <p class="demo-desc">Apply color via the <code>color</code> prop on <code>&lt;n-icon&gt;</code>.</p>
      <PreviewBox>
        <div style="display: flex; align-items: center; gap: 24px;">
          <n-icon :size="28" color="#6366f1"><Icon icon="material-symbols:favorite" /></n-icon>
          <n-icon :size="28" color="#10b981"><Icon icon="material-symbols:check-circle" /></n-icon>
          <n-icon :size="28" color="#f59e0b"><Icon icon="material-symbols:warning" /></n-icon>
          <n-icon :size="28" color="#ef4444"><Icon icon="material-symbols:error" /></n-icon>
          <n-icon :size="28" color="#3b82f6"><Icon icon="material-symbols:info" /></n-icon>
          <n-icon :size="28" color="#8b5cf6"><Icon icon="material-symbols:star" /></n-icon>
        </div>
      </PreviewBox>
    </div>

    <!-- Animated Icons -->
    <div class="demo-block">
      <h2>Animated Icons</h2>
      <p class="demo-desc">Apply CSS animation to create spinning or pulsing icons.</p>
      <PreviewBox>
        <div style="display: flex; align-items: center; gap: 24px;">
          <div style="text-align: center;">
            <n-icon :size="28" class="icon-spin"><Icon icon="material-symbols:refresh" /></n-icon>
            <div class="size-label">Spin</div>
          </div>
          <div style="text-align: center;">
            <n-icon :size="28" class="icon-spin"><Icon icon="material-symbols:settings" /></n-icon>
            <div class="size-label">Spin</div>
          </div>
          <div style="text-align: center;">
            <n-icon :size="28" class="icon-pulse"><Icon icon="material-symbols:notifications" /></n-icon>
            <div class="size-label">Pulse</div>
          </div>
          <div style="text-align: center;">
            <n-icon :size="28" class="icon-bounce"><Icon icon="material-symbols:arrow-downward" /></n-icon>
            <div class="size-label">Bounce</div>
          </div>
        </div>
      </PreviewBox>
    </div>

    <!-- With Components -->
    <div class="demo-block">
      <h2>With Components</h2>
      <p class="demo-desc">Icons integrated with Naive UI components.</p>
      <PreviewBox>
        <div style="display: flex; flex-wrap: wrap; align-items: center; gap: 12px;">
          <!-- Buttons with icons -->
          <n-button type="primary">
            <template #icon><n-icon><Icon icon="material-symbols:add" /></n-icon></template>
            Create
          </n-button>
          <n-button type="error">
            <template #icon><n-icon><Icon icon="material-symbols:delete-outline" /></n-icon></template>
            Delete
          </n-button>
          <n-button circle type="info">
            <template #icon><n-icon><Icon icon="material-symbols:search" /></n-icon></template>
          </n-button>
          <!-- Tags with icons -->
          <n-tag type="success">
            <template #icon><n-icon><Icon icon="material-symbols:check-circle" /></n-icon></template>
            Active
          </n-tag>
          <n-tag type="warning">
            <template #icon><n-icon><Icon icon="material-symbols:schedule" /></n-icon></template>
            Pending
          </n-tag>
          <!-- Input with prefix icon -->
          <n-input placeholder="Search..." style="width: 200px;">
            <template #prefix>
              <n-icon :size="16"><Icon icon="material-symbols:search" /></n-icon>
            </template>
          </n-input>
        </div>
      </PreviewBox>
    </div>

    <!-- Icon Search -->
    <div class="demo-block">
      <h2>Icon Search</h2>
      <p class="demo-desc">Search and browse available icons. Click to copy the icon name.</p>
      <PreviewBox :no-badge="true" :full-width="true">
        <div style="width: 100%; padding: 16px;">
          <n-input v-model:value="searchQuery" placeholder="Search icons..." clearable style="margin-bottom: 16px;">
            <template #prefix>
              <n-icon :size="16"><Icon icon="material-symbols:search" /></n-icon>
            </template>
          </n-input>
          <div class="icon-grid">
            <div v-for="icon in filteredIcons" :key="icon" class="icon-item" @click="copyIconName(icon)">
              <n-icon :size="24"><Icon :icon="icon" /></n-icon>
              <span class="icon-name">{{ icon.split(':')[1] }}</span>
            </div>
          </div>
          <n-text v-if="filteredIcons.length === 0" depth="3" style="display: block; text-align: center; padding: 32px;">
            No icons found for "{{ searchQuery }}"
          </n-text>
        </div>
      </PreviewBox>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue';
import { Icon } from '@iconify/vue';
import { useMessage } from 'naive-ui';
import PreviewBox from '../components/PreviewBox.vue';

const message = useMessage();
const activeCollection = ref('material-symbols');
const searchQuery = ref('');

const collections = [
  {
    name: 'Material Symbols',
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
  {
    name: 'Tabler',
    prefix: 'tabler',
    icons: ['home', 'search', 'settings', 'heart', 'trash', 'plus', 'edit', 'share', 'user', 'bell', 'circle-check', 'info-circle', 'alert-triangle', 'circle-x', 'eye'],
  },
];

// Searchable icon list
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
  'material-symbols:expand-more', 'material-symbols:expand-less',
  'material-symbols:more-vert', 'material-symbols:more-horiz',
  'material-symbols:star', 'material-symbols:star-outline',
  'material-symbols:thumb-up', 'material-symbols:thumb-down',
  'material-symbols:chat', 'material-symbols:send', 'material-symbols:attach-file',
  'material-symbols:image', 'material-symbols:videocam', 'material-symbols:mic',
  'material-symbols:play-arrow', 'material-symbols:pause', 'material-symbols:stop',
  'material-symbols:skip-next', 'material-symbols:skip-previous',
  'material-symbols:volume-up', 'material-symbols:volume-off',
  'material-symbols:download', 'material-symbols:upload', 'material-symbols:print',
  'material-symbols:save', 'material-symbols:content-copy', 'material-symbols:content-paste',
  'material-symbols:undo', 'material-symbols:redo',
  'material-symbols:filter-list', 'material-symbols:sort',
  'material-symbols:dashboard', 'material-symbols:bar-chart',
  'material-symbols:pie-chart', 'material-symbols:show-chart',
  'material-symbols:code', 'material-symbols:terminal', 'material-symbols:bug-report',
];

const filteredIcons = computed(() => {
  if (!searchQuery.value) return allIcons;
  const q = searchQuery.value.toLowerCase();
  return allIcons.filter(icon => icon.toLowerCase().includes(q));
});

function copyIconName(name: string) {
  navigator.clipboard.writeText(name).then(() => {
    message.success(`Copied: ${name}`);
  });
}
</script>

<style scoped>
.section-page { max-width: 960px; }
.section-header { margin-bottom: 32px; }
.section-title { font-size: 32px; font-weight: 800; color: var(--pg-text); margin: 0 0 8px; }
.section-desc { font-size: 14px; color: var(--pg-text-muted); margin: 0; line-height: 1.6; }
.section-desc a { color: var(--pg-primary); text-decoration: none; }
.section-desc a:hover { text-decoration: underline; }
.demo-block { margin-bottom: 32px; }
.demo-block h2 { font-size: 18px; font-weight: 700; color: var(--pg-text); margin: 0 0 8px; }
.demo-desc { font-size: 13px; color: var(--pg-text-muted); margin: 0 0 12px; }

.icon-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(100px, 1fr));
  gap: 8px;
}

.icon-item {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
  padding: 12px 4px;
  border-radius: 8px;
  cursor: pointer;
  transition: background-color 0.15s;
  color: var(--pg-text);
}

.icon-item:hover {
  background-color: rgba(var(--pg-primary-rgb), 0.08);
}

.icon-name {
  font-size: 10px;
  color: var(--pg-text-muted);
  text-align: center;
  word-break: break-all;
  line-height: 1.2;
}

.size-label {
  font-size: 11px;
  color: var(--pg-text-muted);
  margin-top: 4px;
}

@keyframes spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}

@keyframes pulse {
  0%, 100% { transform: scale(1); }
  50% { transform: scale(1.2); }
}

@keyframes bounce {
  0%, 100% { transform: translateY(0); }
  50% { transform: translateY(4px); }
}

:deep(.icon-spin) {
  animation: spin 1.5s linear infinite;
}

:deep(.icon-pulse) {
  animation: pulse 1.2s ease-in-out infinite;
}

:deep(.icon-bounce) {
  animation: bounce 0.8s ease-in-out infinite;
}
</style>
