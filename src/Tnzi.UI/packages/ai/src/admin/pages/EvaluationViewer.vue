<script setup lang="ts">
/**
 * EvaluationViewer — Evaluation run list with view/delete actions
 */

import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
import { Button } from '@/primitives/ui/button';
import { Card, CardHeader, CardTitle, CardContent } from '@/primitives/ui/card';
import { Badge } from '@/primitives/ui/badge';
import type { BadgeVariants } from '@/primitives/ui/badge';
import AdminPageShell from '../AdminPageShell.vue';

const t = useAiI18n();

export interface EvaluationItem {
  id: string;
  name: string;
  status: 'running' | 'completed' | 'failed';
  score?: number;
  totalCases: number;
  passedCases: number;
  createdAt: string;
}

defineProps<{
  evaluations: EvaluationItem[];
  isLoading?: boolean;
}>();

const emit = defineEmits<{
  'view-detail': [id: string];
  delete: [id: string];
  'run-new': [];
  refresh: [];
}>();

const statusVariant: Record<string, NonNullable<BadgeVariants['variant']>> = {
  running: 'default',
  completed: 'secondary',
  failed: 'destructive',
};
</script>

<template>
  <AdminPageShell :is-loading="isLoading">
    <template #header>
      <div class="flex items-center justify-between">
        <h2 class="text-lg font-semibold">{{ t.admin.evaluations }}</h2>
        <Button size="sm" @click="emit('run-new')">
          <Icon icon="lucide:play" class="mr-1.5 size-4" />
          Run Evaluation
        </Button>
      </div>
    </template>

    <div v-if="evaluations.length === 0" class="py-12 text-center text-muted-foreground">
      {{ t.admin.noData }}
    </div>
    <div v-else class="space-y-2">
      <Card
        v-for="evaluation in evaluations"
        :key="evaluation.id"
        class="cursor-pointer hover:shadow-sm transition-shadow"
        @click="emit('view-detail', evaluation.id)"
      >
        <CardHeader class="py-3">
          <div class="flex items-center justify-between">
            <div class="flex items-center gap-2">
              <Icon icon="lucide:clipboard-check" class="size-4 text-primary" />
              <CardTitle class="text-sm">{{ evaluation.name }}</CardTitle>
              <Badge :variant="statusVariant[evaluation.status] ?? 'outline'" class="text-[10px]">
                {{ evaluation.status }}
              </Badge>
            </div>
            <Button
              variant="ghost"
              size="icon-sm"
              class="text-destructive"
              @click.stop="emit('delete', evaluation.id)"
            >
              <Icon icon="lucide:trash-2" class="size-3.5" />
            </Button>
          </div>
        </CardHeader>
        <CardContent class="pt-0 pb-3">
          <div class="flex items-center gap-4 text-xs text-muted-foreground">
            <span v-if="evaluation.score != null" class="font-medium text-foreground">
              Score: {{ (evaluation.score * 100).toFixed(1) }}%
            </span>
            <span>{{ evaluation.passedCases }}/{{ evaluation.totalCases }} passed</span>
            <span>{{ evaluation.createdAt }}</span>
          </div>
        </CardContent>
      </Card>
    </div>
  </AdminPageShell>
</template>
