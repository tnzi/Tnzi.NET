<script setup lang="ts">
/**
 * AgentStatus — Agent run status indicator
 */

import { Badge } from '../../primitives';
import { computed } from 'vue';
import { Icon } from '@iconify/vue';
import { cn } from '@/lib/utils';
import { useAiI18n } from '@/locale/index';
const t = useAiI18n();

const props = defineProps<{
  name: string;
  status: 'idle' | 'running' | 'completed' | 'error' | 'cancelled';
  icon?: string;
}>();

const statusConfig = computed(() => {
  switch (props.status) {
    case 'running': return { icon: 'lucide:loader-2', cls: 'text-primary animate-spin', label: t.value.agent.running };
    case 'completed': return { icon: 'lucide:check-circle-2', cls: 'text-green-500', label: t.value.agent.completed };
    case 'error': return { icon: 'lucide:alert-circle', cls: 'text-destructive', label: t.value.agent.failed };
    case 'cancelled': return { icon: 'lucide:x-circle', cls: 'text-muted-foreground', label: t.value.agent.cancelled };
    default: return { icon: 'lucide:circle', cls: 'text-muted-foreground', label: '' };
  }
});
</script>

<template>
  <div class="inline-flex items-center gap-2 text-sm">
    <Icon :icon="icon ?? 'lucide:bot'" class="size-4 text-primary" />
    <span class="font-medium">{{ name }}</span>
    <Badge variant="outline" class="gap-1 text-xs">
      <Icon :icon="statusConfig.icon" :class="cn('size-3', statusConfig.cls)" />
      <span v-if="statusConfig.label">{{ statusConfig.label }}</span>
    </Badge>
  </div>
</template>
