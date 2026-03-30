<script setup lang="ts">
/**
 * TMcpToolPanel — MCP tool status panel
 */

import { Icon } from '@iconify/vue';
import { cn } from '@/lib/utils';
import { useAiI18n } from '@/locale/index';
import { Popover, PopoverTrigger, PopoverContent } from '@/primitives/ui/popover';
import { Button } from '@/primitives/ui/button';
import { Badge } from '@/primitives/ui/badge';
import { ScrollArea } from '@/primitives/ui/scroll-area';

const t = useAiI18n();

export interface McpTool {
  name: string;
  description?: string;
  status: 'available' | 'error';
}

defineProps<{
  status: 'connected' | 'disconnected' | 'error';
  tools: McpTool[];
}>();

const statusMap = {
  connected: { icon: 'lucide:plug', cls: 'text-green-500' },
  disconnected: { icon: 'lucide:plug-off', cls: 'text-muted-foreground' },
  error: { icon: 'lucide:alert-circle', cls: 'text-destructive' },
} as const;
</script>

<template>
  <Popover>
    <PopoverTrigger as-child>
      <Button variant="outline" size="sm" class="gap-1.5 text-xs">
        <Icon :icon="statusMap[status].icon" :class="cn('size-3.5', statusMap[status].cls)" />
        {{ t.mcp.tools }}
        <Badge variant="secondary" class="ml-1 text-[10px] px-1 py-0">{{ tools.length }}</Badge>
      </Button>
    </PopoverTrigger>
    <PopoverContent class="w-[280px] p-0" align="end">
      <div class="flex items-center justify-between border-b px-3 py-2">
        <span class="text-sm font-medium">{{ t.mcp.tools }}</span>
        <Badge :variant="status === 'connected' ? 'default' : 'destructive'" class="text-[10px]">
          {{ t.mcp[status] }}
        </Badge>
      </div>
      <ScrollArea class="max-h-[250px]">
        <div class="divide-y divide-border">
          <div v-for="tool in tools" :key="tool.name" class="flex items-start gap-2 px-3 py-2">
            <Icon :icon="tool.status === 'error' ? 'lucide:alert-circle' : 'lucide:wrench'" :class="cn('size-3.5 mt-0.5 shrink-0', tool.status === 'error' ? 'text-destructive' : 'text-muted-foreground')" />
            <div class="min-w-0">
              <div class="text-xs font-medium font-mono truncate">{{ tool.name }}</div>
              <div v-if="tool.description" class="text-[11px] text-muted-foreground line-clamp-2">{{ tool.description }}</div>
            </div>
          </div>
        </div>
      </ScrollArea>
    </PopoverContent>
  </Popover>
</template>
