<script setup lang="ts">
/**
 * TMcpToolPanel - MCP tool status panel
 *
 * A button with the connection's state and the number of tools; it opens a
 * `TPopover` (non-modal, Escape or a press outside closes it) listing the
 * tools, the list scrolling inside 250px.
 */

import { Icon } from '@iconify/vue';
import { useAiI18n } from '../../i18n/index';
import TButton from '../controls/TButton.vue';
import TTag from '../controls/TTag.vue';
import TPopover from '../overlay/TPopover.vue';
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
  connected: { icon: 'lucide:plug', cls: 't-mcp-status--connected', tone: 'success' },
  disconnected: { icon: 'lucide:unplug', cls: 't-mcp-status--disconnected', tone: 'neutral' },
  error: { icon: 'lucide:alert-circle', cls: 't-mcp-status--error', tone: 'danger' },
} as const;
</script>

<template>
  <div class="t-mcp-tool-panel">
    <TPopover :label="t.mcp.tools" align="end" :width="280">
      <template #trigger="{ toggle, attrs }">
        <TButton v-bind="attrs" @click="toggle">
          <span class="t-mcp-trigger">
            <Icon :icon="statusMap[status].icon" class="t-mcp-status" :class="statusMap[status].cls" aria-hidden="true" />
            {{ t.mcp.tools }}
            <span class="t-mcp-count">{{ tools.length }}</span>
          </span>
        </TButton>
      </template>
      <div class="t-mcp-panel__head">
        <span class="t-mcp-panel__title">{{ t.mcp.tools }}</span>
        <TTag :tone="statusMap[status].tone" size="sm" dot>{{ t.mcp[status] }}</TTag>
      </div>
      <ul class="t-mcp-panel__list t-ai-scroll">
        <li v-for="tool in tools" :key="tool.name" class="t-mcp-tool">
          <Icon
            :icon="tool.status === 'error' ? 'lucide:alert-circle' : 'lucide:wrench'"
            class="t-mcp-tool__icon"
            :class="tool.status === 'error' ? 't-mcp-tool--error' : 't-mcp-tool--ok'"
            aria-hidden="true"
          />
          <div class="t-mcp-tool__text">
            <div class="t-mcp-tool__name">{{ tool.name }}</div>
            <div v-if="tool.description" class="t-mcp-tool__desc">{{ tool.description }}</div>
          </div>
        </li>
      </ul>
    </TPopover>
  </div>
</template>

<style scoped>
.t-mcp-tool-panel {
  display: inline-flex;
}
/* The panel draws its own header and rows edge to edge. */
.t-mcp-tool-panel :deep(.t-popover) {
  padding: 0;
  overflow: hidden;
}
.t-mcp-trigger {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}
.t-mcp-status {
  font-size: 14px;
}
.t-mcp-status--connected { color: var(--tnzi-ai-success); }
.t-mcp-status--disconnected { color: var(--tnzi-ai-text-tertiary); }
.t-mcp-status--error { color: var(--tnzi-ai-danger); }
.t-mcp-count {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 16px;
  height: 16px;
  padding: 0 4px;
  border-radius: 4px;
  background: var(--tnzi-ai-hover);
  color: var(--tnzi-ai-text-secondary);
  font-size: 10px;
  font-variant-numeric: tabular-nums;
}
.t-mcp-panel__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  padding: 8px 12px;
  border-bottom: 1px solid var(--tnzi-ai-border);
}
.t-mcp-panel__title {
  font-size: 13px;
  font-weight: 500;
}
.t-mcp-panel__list {
  position: relative;
  max-height: 250px;
  margin: 0;
  padding: 0;
  list-style: none;
  overflow-y: auto;
}
.t-mcp-tool {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  padding: 8px 12px;
}
.t-mcp-tool + .t-mcp-tool {
  border-top: 1px solid var(--tnzi-ai-border);
}
.t-mcp-tool__icon {
  flex-shrink: 0;
  margin-top: 2px;
  font-size: 14px;
}
.t-mcp-tool--error { color: var(--tnzi-ai-danger); }
.t-mcp-tool--ok { color: var(--tnzi-ai-text-tertiary); }
.t-mcp-tool__text {
  min-width: 0;
}
.t-mcp-tool__name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-family: var(--tnzi-ai-font-mono);
  font-size: 12px;
  font-weight: 500;
}
.t-mcp-tool__desc {
  display: -webkit-box;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
  overflow: hidden;
  font-size: 11px;
  color: var(--tnzi-ai-text-secondary);
}
</style>
