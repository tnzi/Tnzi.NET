<template>
  <div class="space-y-4">
    <!-- TMenu -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">TMenu</CardTitle>
        <CardDescription>Menu component with nested items, badges, and icons.</CardDescription>
      </CardHeader>
      <CardContent>
        <div class="grid gap-6 md:grid-cols-2">
          <div>
            <p class="mb-2 text-sm font-medium">Vertical</p>
            <div class="w-56 rounded-md border p-2">
              <TMenu
                :items="demoMenuItems"
                :active-key="menuActiveKey"
                @select="onMenuSelect"
              />
            </div>
          </div>
          <div>
            <p class="mb-2 text-sm font-medium">Horizontal</p>
            <div class="rounded-md border p-2">
              <TMenu
                :items="demoMenuItems"
                :active-key="menuActiveKey"
                :horizontal="true"
                @select="onMenuSelect"
              />
            </div>
          </div>
        </div>
      </CardContent>
    </Card>

    <!-- TNavBar -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">TNavBar</CardTitle>
        <CardDescription>Navigation bar with back, close, and custom slots.</CardDescription>
      </CardHeader>
      <CardContent class="space-y-4">
        <div class="rounded-md border">
          <TNavBar
            title="Page Title"
            :show-back="true"
            @back="onNavBack"
          />
        </div>
        <div class="rounded-md border">
          <TNavBar
            title="With Close"
            :show-back="true"
            :show-close="true"
            @back="onNavBack"
            @close="onNavClose"
          >
            <template #right>
              <span class="text-xs">Save</span>
            </template>
          </TNavBar>
        </div>
      </CardContent>
    </Card>

    <!-- Tab Bar -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">Tab Bar</CardTitle>
        <CardDescription>Tab bar for bottom navigation with icons.</CardDescription>
      </CardHeader>
      <CardContent>
        <div class="mx-auto max-w-sm rounded-lg border">
          <nav class="grid grid-cols-4 gap-1 p-1">
            <button
              v-for="tab in tabBarItems"
              :key="tab.key"
              type="button"
              class="flex flex-col items-center gap-0.5 rounded-md py-2 text-[11px] transition-colors"
              :class="tabActiveKey === tab.key ? 'bg-accent text-primary font-medium' : 'text-muted-foreground hover:text-foreground'"
              @click="onTabClick(tab)"
            >
              <Icon :icon="tab.icon" :width="20" :height="20" />
              <span>{{ tab.label }}</span>
            </button>
          </nav>
        </div>
        <p v-if="tabActiveKey" class="mt-2 text-center text-xs text-muted-foreground">
          Active: {{ tabActiveKey }}
        </p>
      </CardContent>
    </Card>

    <!-- TBreadcrumb -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="text-base">TBreadcrumb</CardTitle>
        <CardDescription>Breadcrumb navigation with click to navigate.</CardDescription>
      </CardHeader>
      <CardContent>
        <div class="rounded-md border">
          <TBreadcrumb
            :items="demoBreadcrumbs"
            @item-click="onBreadcrumbClick"
          />
        </div>
      </CardContent>
    </Card>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { Icon } from '@iconify/vue';
import {
  Card, CardHeader, CardTitle, CardDescription, CardContent,
  TMenu, TNavBar, TBreadcrumb,
  useShadcnMessage,
} from '@tnzi/shadcn';
import { demoMenuItems, demoBreadcrumbs } from '@tnzi/core/playground';

const message = useShadcnMessage();

const menuActiveKey = ref('dashboard');
const tabActiveKey = ref('home');

const onMenuSelect = (key: string, item: any) => {
  menuActiveKey.value = key;
  console.log('[TMenu] select:', key, item);
  message.show(`Menu: ${item.label}`, 'info');
};

const onNavBack = () => {
  console.log('[TNavBar] back');
  message.show('Back clicked', 'info');
};

const onNavClose = () => {
  console.log('[TNavBar] close');
  message.show('Close clicked', 'info');
};

const tabBarItems = [
  { key: 'home', label: 'Home', icon: 'material-symbols:home' },
  { key: 'orders', label: 'Orders', icon: 'material-symbols:receipt-long' },
  { key: 'messages', label: 'Messages', icon: 'material-symbols:chat' },
  { key: 'profile', label: 'Profile', icon: 'material-symbols:person' },
];

const onTabClick = (tab: { key: string; label: string }) => {
  tabActiveKey.value = tab.key;
  message.show(`Tab: ${tab.label}`, 'info');
};

const onBreadcrumbClick = (item: any, index: number) => {
  console.log('[TBreadcrumb] click:', item, index);
  message.show(`Breadcrumb: ${item.label}`, 'info');
};
</script>
