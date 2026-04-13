<script setup lang="ts">
/**
 * QuotaManagement — User quota list with set/reset actions
 */

import { Button, Card, CardContent, Badge } from '../../primitives';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
import AdminPageShell from '../AdminPageShell.vue';

const t = useAiI18n();

export interface QuotaItem {
  id: string;
  userId: string;
  userName: string;
  limit: number;
  used: number;
  resetDate?: string;
}

defineProps<{
  quotas: QuotaItem[];
  isLoading?: boolean;
}>();

const emit = defineEmits<{
  'set-quota': [userId: string];
  'reset-quota': [userId: string];
  refresh: [];
}>();

function usagePercent(item: QuotaItem): number {
  if (item.limit <= 0) return 0;
  return Math.min(100, Math.round((item.used / item.limit) * 100));
}
</script>

<template>
  <AdminPageShell :is-loading="isLoading">
    <template #header>
      <div class="flex items-center justify-between">
        <h2 class="text-lg font-semibold">{{ t.admin.quotas }}</h2>
        <Button variant="outline" size="sm" @click="emit('refresh')">
          <Icon icon="lucide:refresh-cw" class="mr-1.5 size-4" />
          {{ t.common.retry }}
        </Button>
      </div>
    </template>

    <div v-if="quotas.length === 0" class="py-12 text-center text-muted-foreground">
      {{ t.admin.noData }}
    </div>
    <div v-else>
      <Card>
        <CardContent class="p-0">
          <table class="w-full text-sm">
            <thead>
              <tr class="border-b text-left text-xs text-muted-foreground">
                <th class="p-3 font-medium">User</th>
                <th class="p-3 font-medium text-right">{{ t.quota.usage }}</th>
                <th class="p-3 font-medium text-right">{{ t.quota.limit }}</th>
                <th class="p-3 font-medium text-right">%</th>
                <th class="p-3 font-medium">{{ t.quota.resetDate.replace('{date}', '') }}</th>
                <th class="p-3 font-medium text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="quota in quotas" :key="quota.id" class="border-b last:border-0">
                <td class="p-3">
                  <div class="font-medium">{{ quota.userName }}</div>
                  <div class="text-xs text-muted-foreground">{{ quota.userId }}</div>
                </td>
                <td class="p-3 text-right tabular-nums">{{ quota.used.toLocaleString() }}</td>
                <td class="p-3 text-right tabular-nums">{{ quota.limit.toLocaleString() }}</td>
                <td class="p-3 text-right">
                  <Badge
                    :variant="usagePercent(quota) >= 90 ? 'destructive' : usagePercent(quota) >= 70 ? 'secondary' : 'outline'"
                    class="text-[10px] tabular-nums"
                  >
                    {{ usagePercent(quota) }}%
                  </Badge>
                </td>
                <td class="p-3 text-xs text-muted-foreground">{{ quota.resetDate ?? '-' }}</td>
                <td class="p-3 text-right">
                  <div class="flex items-center justify-end gap-1">
                    <Button variant="ghost" size="sm" class="text-xs" @click="emit('set-quota', quota.userId)">
                      {{ t.admin.setQuota }}
                    </Button>
                    <Button variant="ghost" size="sm" class="text-xs" @click="emit('reset-quota', quota.userId)">
                      {{ t.admin.resetQuota }}
                    </Button>
                  </div>
                </td>
              </tr>
            </tbody>
          </table>
        </CardContent>
      </Card>
    </div>
  </AdminPageShell>
</template>
