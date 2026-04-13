<script setup lang="ts">
/**
 * AgentRunMonitor — Agent run list with status badges, cancel/retry actions
 */

import {
  Button,
  Card,
  CardHeader,
  CardTitle,
  CardContent,
  Badge,
} from '../../primitives';
import type { BadgeVariants } from '../../primitives';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
import AdminPageShell from '../AdminPageShell.vue';

const t = useAiI18n();

export interface AgentRunItem {
  id: string;
  agentName: string;
  status: 'running' | 'completed' | 'failed' | 'cancelled' | 'pending';
  startedAt: string;
  duration?: number;
  tokenCount?: number;
}

defineProps<{
  runs: AgentRunItem[];
  isLoading?: boolean;
}>();

const emit = defineEmits<{
  cancel: [id: string];
  retry: [id: string];
  'view-detail': [id: string];
  refresh: [];
}>();

const statusVariant: Record<string, NonNullable<BadgeVariants['variant']>> = {
  running: 'default',
  completed: 'secondary',
  failed: 'destructive',
  cancelled: 'outline',
  pending: 'outline',
};

const statusIcon: Record<string, string> = {
  running: 'lucide:loader-2',
  completed: 'lucide:check-circle-2',
  failed: 'lucide:alert-circle',
  cancelled: 'lucide:x-circle',
  pending: 'lucide:clock',
};
</script>

<template>
  <AdminPageShell :is-loading="isLoading">
    <template #header>
      <div class="flex items-center justify-between">
        <h2 class="text-lg font-semibold">{{ t.admin.agentRuns }}</h2>
        <Button variant="outline" size="sm" @click="emit('refresh')">
          <Icon icon="lucide:refresh-cw" class="mr-1.5 size-4" />
          {{ t.common.retry }}
        </Button>
      </div>
    </template>

    <div v-if="runs.length === 0" class="py-12 text-center text-muted-foreground">
      {{ t.admin.noData }}
    </div>
    <div v-else class="space-y-2">
      <Card
        v-for="run in runs"
        :key="run.id"
        class="cursor-pointer hover:shadow-sm transition-shadow"
        @click="emit('view-detail', run.id)"
      >
        <CardHeader class="py-3">
          <div class="flex items-center justify-between">
            <div class="flex items-center gap-2">
              <Icon :icon="statusIcon[run.status] ?? 'lucide:circle'" class="size-4" />
              <CardTitle class="text-sm">{{ run.agentName }}</CardTitle>
              <Badge :variant="statusVariant[run.status] ?? 'outline'" class="text-[10px]">
                {{ run.status }}
              </Badge>
            </div>
            <div class="flex items-center gap-1">
              <Button
                v-if="run.status === 'running'"
                variant="ghost"
                size="icon-sm"
                @click.stop="emit('cancel', run.id)"
              >
                <Icon icon="lucide:square" class="size-3.5" />
              </Button>
              <Button
                v-if="run.status === 'failed' || run.status === 'cancelled'"
                variant="ghost"
                size="icon-sm"
                @click.stop="emit('retry', run.id)"
              >
                <Icon icon="lucide:rotate-ccw" class="size-3.5" />
              </Button>
            </div>
          </div>
        </CardHeader>
        <CardContent class="pt-0 pb-3">
          <div class="flex items-center gap-4 text-xs text-muted-foreground">
            <span>{{ run.startedAt }}</span>
            <span v-if="run.duration">{{ run.duration }}ms</span>
            <span v-if="run.tokenCount">{{ run.tokenCount }} tokens</span>
          </div>
        </CardContent>
      </Card>
    </div>
  </AdminPageShell>
</template>
