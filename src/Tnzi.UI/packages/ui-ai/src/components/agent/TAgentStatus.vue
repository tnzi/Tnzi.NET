<script setup lang="ts">
/**
 * TAgentStatus - Agent run status indicator
 */

import { computed } from 'vue';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
const t = useAiI18n();

const props = defineProps<{
  name: string;
  status: 'idle' | 'running' | 'completed' | 'error' | 'cancelled';
  icon?: string;
}>();

const statusConfig = computed(() => {
  switch (props.status) {
    case 'running': return { icon: 'lucide:loader-2', cls: 't-agent-status--running', label: t.value.agent.running };
    case 'completed': return { icon: 'lucide:check-circle-2', cls: 't-agent-status--completed', label: t.value.agent.completed };
    case 'error': return { icon: 'lucide:alert-circle', cls: 't-agent-status--error', label: t.value.agent.failed };
    case 'cancelled': return { icon: 'lucide:x-circle', cls: 't-agent-status--muted', label: t.value.agent.cancelled };
    default: return { icon: 'lucide:circle', cls: 't-agent-status--muted', label: '' };
  }
});
</script>

<template>
  <div class="inline-flex items-center gap-2 text-sm">
    <Icon :icon="icon ?? 'lucide:bot'" class="size-4 t-agent-status__icon" />
    <span class="font-medium">{{ name }}</span>
    <span class="t-agent-status__badge">
      <Icon :icon="statusConfig.icon" class="size-3" :class="statusConfig.cls" />
      <span v-if="statusConfig.label">{{ statusConfig.label }}</span>
    </span>
  </div>
</template>

<style scoped>
.t-agent-status__icon {
  color: var(--tnzi-primary);
}
.t-agent-status__badge {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 12px;
  border: 1px solid var(--tnzi-border);
  border-radius: 4px;
  padding: 1px 6px;
}
.t-agent-status--running {
  color: var(--tnzi-primary);
  animation: spin 1s linear infinite;
}
.t-agent-status--completed {
  color: var(--tnzi-ai-node-completed);
}
.t-agent-status--error {
  color: var(--tnzi-error);
}
.t-agent-status--muted {
  color: var(--tnzi-base-text-muted);
}
@keyframes spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}
</style>
