<script setup lang="ts">
/**
 * AdminLayout — Admin dashboard frame with collapsible sidebar navigation
 */

import { ref, computed } from 'vue';
import { Icon } from '@iconify/vue';
import { cn } from '@/lib/utils';
import { useAiI18n } from '@/locale/index';
import { Button } from '@/primitives/ui/button';
import { ScrollArea } from '@/primitives/ui/scroll-area';
import { Separator } from '@/primitives/ui/separator';

const t = useAiI18n();

export interface AdminNavItem {
  id: string;
  label: string;
  icon: string;
}

const props = withDefaults(defineProps<{
  /** Currently active page ID. */
  activePage?: string;
  /** Custom navigation items (overrides default). */
  navItems?: AdminNavItem[];
}>(), {
  activePage: 'agents',
});

const emit = defineEmits<{
  navigate: [pageId: string];
}>();

const defaultNavItems = computed<AdminNavItem[]>(() => [
  { id: 'agents', label: t.value.admin.agents, icon: 'lucide:bot' },
  { id: 'agent-runs', label: t.value.admin.agentRuns, icon: 'lucide:play' },
  { id: 'workflows', label: t.value.admin.workflows, icon: 'lucide:git-branch' },
  { id: 'skills', label: t.value.admin.skills, icon: 'lucide:zap' },
  { id: 'providers', label: t.value.admin.providers, icon: 'lucide:cloud' },
  { id: 'usage', label: t.value.admin.usage, icon: 'lucide:bar-chart-3' },
  { id: 'knowledge', label: t.value.admin.knowledge, icon: 'lucide:library' },
  { id: 'mcp', label: t.value.admin.mcp, icon: 'lucide:plug' },
  { id: 'quotas', label: t.value.admin.quotas, icon: 'lucide:gauge' },
  { id: 'personas', label: t.value.admin.personas, icon: 'lucide:user-circle' },
  { id: 'evaluations', label: t.value.admin.evaluations, icon: 'lucide:clipboard-check' },
]);

const items = computed(() => props.navItems ?? defaultNavItems.value);
const sidebarCollapsed = ref(false);
</script>

<template>
  <div class="flex h-full">
    <!-- Sidebar -->
    <div :class="cn('flex flex-col border-r bg-muted/30 transition-all', sidebarCollapsed ? 'w-14' : 'w-52')">
      <div class="flex items-center justify-between p-3">
        <span v-if="!sidebarCollapsed" class="text-sm font-semibold">{{ t.admin.dashboard }}</span>
        <Button variant="ghost" size="icon-sm" @click="sidebarCollapsed = !sidebarCollapsed">
          <Icon :icon="sidebarCollapsed ? 'lucide:panel-right' : 'lucide:panel-left'" class="size-4" />
        </Button>
      </div>
      <Separator />
      <slot name="header" />
      <ScrollArea class="flex-1">
        <nav class="space-y-0.5 p-2">
          <Button
            v-for="item in items"
            :key="item.id"
            :variant="item.id === activePage ? 'secondary' : 'ghost'"
            :class="cn('w-full justify-start gap-2', sidebarCollapsed && 'justify-center px-2')"
            size="sm"
            @click="emit('navigate', item.id)"
          >
            <Icon :icon="item.icon" class="size-4 shrink-0" />
            <span v-if="!sidebarCollapsed" class="truncate">{{ item.label }}</span>
          </Button>
        </nav>
      </ScrollArea>
      <slot name="sidebar-extra" />
      <slot name="footer" />
    </div>

    <!-- Content -->
    <div class="flex-1 overflow-auto">
      <slot />
    </div>
  </div>
</template>
