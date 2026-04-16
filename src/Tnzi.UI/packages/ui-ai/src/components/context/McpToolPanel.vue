<script setup lang="ts">
/**
 * McpToolPanel — MCP tool status panel
 */

import { NPopover, NButton, NScrollbar } from 'naive-ui';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
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
  connected: { icon: 'lucide:plug', cls: 't-mcp-status--connected' },
  disconnected: { icon: 'lucide:plug-off', cls: 't-mcp-status--disconnected' },
  error: { icon: 'lucide:alert-circle', cls: 't-mcp-status--error' },
} as const;
</script>

<template>
  <NPopover trigger="click" placement="bottom-end" style="width: 280px; padding: 0">
    <template #trigger>
      <span>
        <NButton secondary size="small" class="gap-1.5 text-xs">
          <template #icon>
            <Icon :icon="statusMap[status].icon" class="size-3.5" :class="statusMap[status].cls" />
          </template>
          {{ t.mcp.tools }}
          <span class="t-mcp-count">{{ tools.length }}</span>
        </NButton>
      </span>
    </template>
    <div class="flex items-center justify-between border-b px-3 py-2">
      <span class="text-sm font-medium">{{ t.mcp.tools }}</span>
      <span class="t-mcp-badge" :class="status === 'connected' ? 't-mcp-badge--connected' : 't-mcp-badge--error'">
        {{ t.mcp[status] }}
      </span>
    </div>
    <NScrollbar style="max-height: 250px">
      <div class="divide-y">
        <div v-for="tool in tools" :key="tool.name" class="flex items-start gap-2 px-3 py-2">
          <Icon
            :icon="tool.status === 'error' ? 'lucide:alert-circle' : 'lucide:wrench'"
            class="size-3.5 mt-0.5 shrink-0"
            :class="tool.status === 'error' ? 't-mcp-tool--error' : 't-mcp-tool--ok'"
          />
          <div class="min-w-0">
            <div class="text-xs font-medium font-mono truncate">{{ tool.name }}</div>
            <div v-if="tool.description" class="t-mcp-tool__desc line-clamp-2">{{ tool.description }}</div>
          </div>
        </div>
      </div>
    </NScrollbar>
  </NPopover>
</template>

<style scoped>
.t-mcp-status--connected { color: var(--tnzi-success); }
.t-mcp-status--disconnected { color: var(--tnzi-base-text-muted); }
.t-mcp-status--error { color: var(--tnzi-error); }
.t-mcp-count {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  font-size: 10px;
  padding: 0 4px;
  border-radius: 4px;
  background-color: var(--tnzi-border);
  margin-left: 4px;
}
.t-mcp-badge {
  font-size: 10px;
  padding: 1px 6px;
  border-radius: 4px;
}
.t-mcp-badge--connected { background-color: var(--tnzi-success); color: #fff; }
.t-mcp-badge--error { background-color: var(--tnzi-error); color: #fff; }
.t-mcp-tool--error { color: var(--tnzi-error); }
.t-mcp-tool--ok { color: var(--tnzi-base-text-muted); }
.t-mcp-tool__desc { font-size: 11px; color: var(--tnzi-base-text-muted); }
</style>
