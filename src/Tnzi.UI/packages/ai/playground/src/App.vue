<script setup lang="ts">
/**
 * @tnzi/ai Playground — Full feature showcase
 *
 * Tab-based navigation showing all component groups:
 * Chat, Components, Workflow, Admin, Embed
 */

import { ref, defineAsyncComponent } from 'vue';
import { createAiI18n, useAiI18n, en, zhCn } from '@tnzi/ai/locale';
import { applyAiTheme, lightTokens, darkTokens } from '@tnzi/ai/themes';
import { Icon } from '@iconify/vue';
import { TooltipProvider } from '@/primitives/ui/tooltip';

// i18n
const messagesRef = createAiI18n(en);
const isDark = ref(false);
const isZh = ref(false);

function toggleTheme() {
  isDark.value = !isDark.value;
  document.documentElement.classList.toggle('dark', isDark.value);
  applyAiTheme(isDark.value ? darkTokens : lightTokens);
}

function toggleLocale() {
  isZh.value = !isZh.value;
  messagesRef.value = isZh.value ? zhCn : en;
}

// Tabs — lazy-load demo pages
const tabs = [
  { id: 'chat', label: 'Chat', icon: 'lucide:message-circle' },
  { id: 'components', label: 'Components', icon: 'lucide:layout-grid' },
  { id: 'workflow', label: 'Workflow', icon: 'lucide:git-branch' },
  { id: 'admin', label: 'Admin', icon: 'lucide:shield' },
  { id: 'embed', label: 'Embed', icon: 'lucide:code-2' },
] as const;

type TabId = (typeof tabs)[number]['id'];
const activeTab = ref<TabId>('chat');

const demoComponents: Record<TabId, ReturnType<typeof defineAsyncComponent>> = {
  chat: defineAsyncComponent(() => import('./demos/ChatDemo.vue')),
  components: defineAsyncComponent(() => import('./demos/ComponentsDemo.vue')),
  workflow: defineAsyncComponent(() => import('./demos/WorkflowDemo.vue')),
  admin: defineAsyncComponent(() => import('./demos/AdminDemo.vue')),
  embed: defineAsyncComponent(() => import('./demos/EmbedDemo.vue')),
};
</script>

<template>
  <TooltipProvider>
  <div class="flex h-screen flex-col bg-background text-foreground">
    <!-- Top navigation -->
    <header class="flex items-center border-b px-4 h-12 shrink-0">
      <!-- Logo -->
      <div class="flex items-center gap-2 mr-6">
        <Icon icon="lucide:bot" class="size-5 text-primary" />
        <span class="text-sm font-bold">@tnzi/ai</span>
        <span class="text-xs text-muted-foreground">playground</span>
      </div>

      <!-- Tab nav -->
      <nav class="flex items-center gap-1">
        <button
          v-for="tab in tabs"
          :key="tab.id"
          class="flex items-center gap-1.5 rounded-md px-3 py-1.5 text-sm transition-colors"
          :class="activeTab === tab.id
            ? 'bg-primary text-primary-foreground'
            : 'text-muted-foreground hover:bg-accent hover:text-accent-foreground'"
          @click="activeTab = tab.id"
        >
          <Icon :icon="tab.icon" class="size-3.5" />
          {{ tab.label }}
        </button>
      </nav>

      <!-- Right actions -->
      <div class="ml-auto flex items-center gap-1.5">
        <button
          class="flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-xs hover:bg-accent transition-colors"
          @click="toggleLocale"
        >
          <Icon icon="lucide:languages" class="size-3.5" />
          {{ isZh ? 'EN' : '中文' }}
        </button>
        <button
          class="flex size-8 items-center justify-center rounded-md hover:bg-accent transition-colors"
          @click="toggleTheme"
        >
          <Icon :icon="isDark ? 'lucide:sun' : 'lucide:moon'" class="size-4" />
        </button>
      </div>
    </header>

    <!-- Demo content -->
    <main class="flex-1 min-h-0">
      <Suspense>
        <component :is="demoComponents[activeTab]" />
        <template #fallback>
          <div class="flex h-full items-center justify-center text-muted-foreground">
            <Icon icon="lucide:loader-2" class="size-5 animate-spin mr-2" />
            Loading...
          </div>
        </template>
      </Suspense>
    </main>
  </div>
  </TooltipProvider>
</template>
