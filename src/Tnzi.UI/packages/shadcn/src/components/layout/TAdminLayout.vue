<script setup lang="ts">
import { computed, ref } from 'vue';
import type { ILayoutProps, IMenuItem } from '@tnzi/core/components';

interface AdminLayoutProps extends ILayoutProps {
  sidebarItems?: IMenuItem[];
  logo?: string;
  logoText?: string;
}

const props = withDefaults(defineProps<AdminLayoutProps>(), {
  sidebarItems: () => [],
  showSidebar: true,
  showHeader: true,
  showFooter: false,
  mode: 'fluid',
});

const sidebarCollapsed = ref(false);

const showSidebar = computed(() => props.showSidebar !== false);
const showHeader = computed(() => props.showHeader !== false);
const showFooter = computed(() => props.showFooter === true);

const handleSidebarCollapse = (collapsed: boolean) => {
  sidebarCollapsed.value = collapsed;
};
</script>

<template>
  <div class="flex min-h-screen bg-background" :class="props.class" :style="props.style">
    <aside v-if="showSidebar" class="shrink-0 border-r bg-card" :class="sidebarCollapsed ? 'w-16' : 'w-64'">
      <TSidebar
        :items="props.sidebarItems"
        :collapsed="sidebarCollapsed"
        :logo="props.logo"
        :logo-text="props.logoText"
        @collapse-change="handleSidebarCollapse"
      />
    </aside>

    <div class="flex min-w-0 flex-1 flex-col">
      <header v-if="showHeader" class="shrink-0 border-b bg-card px-4 py-3">
        <slot name="header" />
      </header>

      <main class="min-h-0 flex-1 overflow-auto p-4">
        <slot />
      </main>

      <footer v-if="showFooter" class="shrink-0 border-t bg-card px-4 py-3">
        <slot name="footer" />
      </footer>
    </div>
  </div>
</template>
