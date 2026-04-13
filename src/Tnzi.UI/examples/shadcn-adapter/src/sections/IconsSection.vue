<template>
  <div class="space-y-8">
    <div>
      <h2 class="text-2xl font-bold tracking-tight">Icons</h2>
      <p class="text-sm text-muted-foreground mt-1">
        Tnzi UI uses <a href="https://iconify.design" target="_blank" rel="noopener" class="text-primary hover:underline">Iconify</a> for unified icon access.
        Over 200,000 icons from 200+ collections, loaded on-demand.
      </p>
    </div>

    <!-- Icon Collections -->
    <div class="space-y-3">
      <h3 class="text-lg font-semibold">Icon Collections</h3>
      <p class="text-xs text-muted-foreground">Popular icon sets available through Iconify. Click to copy.</p>
      <div class="rounded-lg border bg-card p-4">
        <div class="flex gap-2 mb-4">
          <Button v-for="col in collections" :key="col.prefix" :variant="activeCollection === col.prefix ? 'default' : 'outline'" size="sm" class="text-xs" @click="activeCollection = col.prefix">
            {{ col.name }}
          </Button>
        </div>
        <div class="grid grid-cols-5 sm:grid-cols-8 md:grid-cols-10 gap-2">
          <button v-for="icon in currentCollectionIcons" :key="icon" class="flex flex-col items-center gap-1.5 rounded-lg p-3 transition hover:bg-accent" @click="copyIconName(`${activeCollection}:${icon}`)">
            <Icon :icon="`${activeCollection}:${icon}`" :width="22" :height="22" />
            <span class="text-[10px] text-muted-foreground leading-tight text-center break-all">{{ icon }}</span>
          </button>
        </div>
      </div>
    </div>

    <!-- Sizes -->
    <div class="space-y-3">
      <h3 class="text-lg font-semibold">Sizes</h3>
      <p class="text-xs text-muted-foreground">Control icon size via width/height props.</p>
      <div class="rounded-lg border bg-card p-6">
        <div class="flex items-end gap-8">
          <div v-for="size in [16, 20, 24, 32, 48]" :key="size" class="flex flex-col items-center gap-2">
            <Icon icon="material-symbols:favorite" :width="size" :height="size" class="text-primary" />
            <span class="text-[10px] text-muted-foreground">{{ size }}px</span>
          </div>
        </div>
      </div>
    </div>

    <!-- Colors -->
    <div class="space-y-3">
      <h3 class="text-lg font-semibold">Colors</h3>
      <p class="text-xs text-muted-foreground">Apply color via Tailwind utility classes.</p>
      <div class="rounded-lg border bg-card p-6">
        <div class="flex items-center gap-6">
          <Icon icon="material-symbols:favorite" :width="28" :height="28" class="text-primary" />
          <Icon icon="material-symbols:check-circle" :width="28" :height="28" class="text-green-500" />
          <Icon icon="material-symbols:warning" :width="28" :height="28" class="text-yellow-500" />
          <Icon icon="material-symbols:error" :width="28" :height="28" class="text-destructive" />
          <Icon icon="material-symbols:info" :width="28" :height="28" class="text-blue-500" />
          <Icon icon="material-symbols:star" :width="28" :height="28" class="text-purple-500" />
        </div>
      </div>
    </div>

    <!-- Animated Icons -->
    <div class="space-y-3">
      <h3 class="text-lg font-semibold">Animated Icons</h3>
      <p class="text-xs text-muted-foreground">Apply CSS animation to create spinning or pulsing icons.</p>
      <div class="rounded-lg border bg-card p-6">
        <div class="flex items-center gap-8">
          <div class="flex flex-col items-center gap-2">
            <Icon icon="material-symbols:refresh" :width="28" :height="28" class="icon-spin" />
            <span class="text-[10px] text-muted-foreground">Spin</span>
          </div>
          <div class="flex flex-col items-center gap-2">
            <Icon icon="material-symbols:settings" :width="28" :height="28" class="icon-spin" />
            <span class="text-[10px] text-muted-foreground">Spin</span>
          </div>
          <div class="flex flex-col items-center gap-2">
            <Icon icon="material-symbols:notifications" :width="28" :height="28" class="icon-pulse" />
            <span class="text-[10px] text-muted-foreground">Pulse</span>
          </div>
          <div class="flex flex-col items-center gap-2">
            <Icon icon="material-symbols:arrow-downward" :width="28" :height="28" class="icon-bounce" />
            <span class="text-[10px] text-muted-foreground">Bounce</span>
          </div>
        </div>
      </div>
    </div>

    <!-- With Components -->
    <div class="space-y-3">
      <h3 class="text-lg font-semibold">With Components</h3>
      <p class="text-xs text-muted-foreground">Icons integrated with shadcn components.</p>
      <div class="rounded-lg border bg-card p-6">
        <div class="flex flex-wrap items-center gap-3">
          <Button size="sm">
            <Icon icon="material-symbols:add" :width="16" :height="16" class="mr-1" />
            Create
          </Button>
          <Button variant="destructive" size="sm">
            <Icon icon="material-symbols:delete-outline" :width="16" :height="16" class="mr-1" />
            Delete
          </Button>
          <Button variant="outline" size="sm">
            <Icon icon="material-symbols:download" :width="16" :height="16" class="mr-1" />
            Export
          </Button>
          <Button variant="ghost" size="icon" class="h-8 w-8">
            <Icon icon="material-symbols:search" :width="16" :height="16" />
          </Button>
          <Button variant="ghost" size="icon" class="h-8 w-8">
            <Icon icon="material-symbols:settings" :width="16" :height="16" />
          </Button>
          <div class="relative">
            <Icon icon="material-symbols:search" :width="14" :height="14" class="absolute left-2.5 top-2.5 text-muted-foreground" />
            <input class="flex h-9 w-48 rounded-md border border-input bg-transparent pl-8 pr-3 text-sm shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring" placeholder="Search..." />
          </div>
        </div>
      </div>
    </div>

    <!-- Icon Search -->
    <div class="space-y-3">
      <h3 class="text-lg font-semibold">Icon Search</h3>
      <p class="text-xs text-muted-foreground">Search and browse available icons. Click to copy the icon name.</p>
      <div class="rounded-lg border bg-card p-4">
        <div class="relative mb-4">
          <Icon icon="material-symbols:search" :width="14" :height="14" class="absolute left-2.5 top-2.5 text-muted-foreground" />
          <input v-model="searchQuery" class="flex h-9 w-full rounded-md border border-input bg-transparent pl-8 pr-3 text-sm shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring" placeholder="Search icons..." />
        </div>
        <div class="grid grid-cols-5 sm:grid-cols-8 md:grid-cols-10 gap-2">
          <button v-for="icon in filteredIcons" :key="icon" class="flex flex-col items-center gap-1.5 rounded-lg p-3 transition hover:bg-accent" @click="copyIconName(icon)">
            <Icon :icon="icon" :width="22" :height="22" />
            <span class="text-[10px] text-muted-foreground leading-tight text-center break-all">{{ icon.split(':')[1] }}</span>
          </button>
        </div>
        <p v-if="filteredIcons.length === 0" class="text-center text-sm text-muted-foreground py-8">
          No icons found for "{{ searchQuery }}"
        </p>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue';
import { Icon } from '@iconify/vue';
import { Button, useMessage } from '@tnzi/ui';

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

const currentCollectionIcons = computed(() => {
  const col = collections.find(c => c.prefix === activeCollection.value);
  return col?.icons ?? [];
});

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
.icon-spin {
  animation: spin 1.5s linear infinite;
}
.icon-pulse {
  animation: pulse 1.2s ease-in-out infinite;
}
.icon-bounce {
  animation: bounce 0.8s ease-in-out infinite;
}
</style>
