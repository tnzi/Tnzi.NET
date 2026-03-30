<script setup lang="ts">
/**
 * McpServerPanel — MCP server status + tool list
 */

import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
import { Button } from '@/primitives/ui/button';
import { Card, CardHeader, CardTitle, CardContent } from '@/primitives/ui/card';
import { Badge } from '@/primitives/ui/badge';
import type { BadgeVariants } from '@/primitives/ui/badge';
import AdminPageShell from '../AdminPageShell.vue';

const t = useAiI18n();

export interface McpServerItem {
  id: string;
  name: string;
  url: string;
  status: 'connected' | 'disconnected' | 'error';
  toolCount: number;
  tools?: McpToolItem[];
}

export interface McpToolItem {
  name: string;
  description?: string;
}

defineProps<{
  servers: McpServerItem[];
  isLoading?: boolean;
}>();

const emit = defineEmits<{
  connect: [id: string];
  disconnect: [id: string];
  refresh: [];
  'view-tools': [id: string];
}>();

const statusConfig: Record<string, { variant: NonNullable<BadgeVariants['variant']>; icon: string }> = {
  connected: { variant: 'default', icon: 'lucide:check-circle-2' },
  disconnected: { variant: 'outline', icon: 'lucide:circle-off' },
  error: { variant: 'destructive', icon: 'lucide:alert-circle' },
};
</script>

<template>
  <AdminPageShell :is-loading="isLoading">
    <template #header>
      <div class="flex items-center justify-between">
        <h2 class="text-lg font-semibold">{{ t.admin.mcp }}</h2>
        <Button variant="outline" size="sm" @click="emit('refresh')">
          <Icon icon="lucide:refresh-cw" class="mr-1.5 size-4" />
          {{ t.common.retry }}
        </Button>
      </div>
    </template>

    <div v-if="servers.length === 0" class="py-12 text-center text-muted-foreground">
      {{ t.admin.noData }}
    </div>
    <div v-else class="space-y-3">
      <Card v-for="server in servers" :key="server.id">
        <CardHeader class="pb-2">
          <div class="flex items-center justify-between">
            <div class="flex items-center gap-2">
              <Icon icon="lucide:plug" class="size-4 text-primary" />
              <CardTitle class="text-sm">{{ server.name }}</CardTitle>
            </div>
            <Badge :variant="statusConfig[server.status]?.variant ?? 'outline'" class="text-[10px]">
              <Icon :icon="statusConfig[server.status]?.icon ?? 'lucide:circle'" class="mr-1 size-3" />
              {{ t.mcp[server.status] ?? server.status }}
            </Badge>
          </div>
        </CardHeader>
        <CardContent class="pb-3">
          <div class="flex items-center justify-between">
            <div class="text-xs text-muted-foreground">
              <div>{{ server.url }}</div>
              <div class="mt-1">{{ server.toolCount }} tools</div>
            </div>
            <div class="flex items-center gap-1">
              <Button
                v-if="server.status === 'disconnected' || server.status === 'error'"
                variant="outline"
                size="sm"
                @click="emit('connect', server.id)"
              >
                Connect
              </Button>
              <Button
                v-if="server.status === 'connected'"
                variant="ghost"
                size="sm"
                @click="emit('disconnect', server.id)"
              >
                Disconnect
              </Button>
              <Button variant="ghost" size="icon-sm" @click="emit('view-tools', server.id)">
                <Icon icon="lucide:list" class="size-3.5" />
              </Button>
            </div>
          </div>

          <!-- Tool list (expandable) -->
          <div v-if="server.tools && server.tools.length > 0" class="mt-3 border-t pt-2">
            <div
              v-for="tool in server.tools"
              :key="tool.name"
              class="flex items-center gap-2 py-1 text-xs"
            >
              <Icon icon="lucide:wrench" class="size-3 text-muted-foreground" />
              <span class="font-medium">{{ tool.name }}</span>
              <span v-if="tool.description" class="text-muted-foreground truncate">
                {{ tool.description }}
              </span>
            </div>
          </div>
        </CardContent>
      </Card>
    </div>
  </AdminPageShell>
</template>
