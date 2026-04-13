<script setup lang="ts">
/**
 * UsageAnalytics — Summary stat cards (total requests, tokens, cost, active agents)
 */

import {
  Card,
  CardHeader,
  CardTitle,
  CardContent,
  ScrollArea,
} from '../../primitives';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
import { formatCompactNumber } from '@/lib/utils';
const t = useAiI18n();

export interface UsageSummary {
  totalRequests: number;
  totalTokens: number;
  totalCost: number;
  activeAgents: number;
}

export interface UsageEntry {
  id: string;
  agentName: string;
  date: string;
  requests: number;
  tokens: number;
  cost: number;
}

defineProps<{
  summary?: UsageSummary;
  entries: UsageEntry[];
  isLoading?: boolean;
}>();

defineEmits<{
  'date-range-change': [from: string, to: string];
  refresh: [];
}>();

const statCards = [
  { key: 'totalRequests', icon: 'lucide:activity', labelKey: 'totalRequests' },
  { key: 'totalTokens', icon: 'lucide:hash', labelKey: 'totalTokens' },
  { key: 'totalCost', icon: 'lucide:dollar-sign', labelKey: 'totalCost' },
  { key: 'activeAgents', icon: 'lucide:bot', labelKey: 'activeAgents' },
] as const;
</script>

<template>
  <div class="p-6 space-y-6">
    <h2 class="text-lg font-semibold">{{ t.admin.usage }}</h2>

    <!-- Summary cards -->
    <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
      <Card v-for="stat in statCards" :key="stat.key">
        <CardHeader class="flex flex-row items-center justify-between space-y-0 pb-2">
          <CardTitle class="text-xs font-medium text-muted-foreground">{{ t.admin[stat.labelKey] }}</CardTitle>
          <Icon :icon="stat.icon" class="size-4 text-muted-foreground" />
        </CardHeader>
        <CardContent>
          <div v-if="isLoading" class="h-7 w-20 animate-pulse rounded bg-muted" />
          <div v-else class="text-2xl font-bold tabular-nums">
            {{ stat.key === 'totalCost' ? `$${(summary?.[stat.key] ?? 0).toFixed(2)}` : formatCompactNumber(summary?.[stat.key] ?? 0) }}
          </div>
        </CardContent>
      </Card>
    </div>

    <!-- Usage table -->
    <Card>
      <CardHeader>
        <CardTitle class="text-sm">{{ t.admin.usageByAgent }}</CardTitle>
      </CardHeader>
      <CardContent>
        <ScrollArea class="max-h-[calc(100vh-400px)]">
          <div v-if="isLoading" class="py-8 text-center text-muted-foreground">
            {{ t.common.loading }}
          </div>
          <div v-else-if="entries.length === 0" class="py-8 text-center text-muted-foreground">
            {{ t.admin.noData }}
          </div>
          <table v-else class="w-full text-sm">
            <thead>
              <tr class="border-b text-left text-xs text-muted-foreground">
                <th class="pb-2 font-medium">Agent</th>
                <th class="pb-2 font-medium">Date</th>
                <th class="pb-2 font-medium text-right">Requests</th>
                <th class="pb-2 font-medium text-right">Tokens</th>
                <th class="pb-2 font-medium text-right">Cost</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="entry in entries" :key="entry.id" class="border-b last:border-0">
                <td class="py-2">{{ entry.agentName }}</td>
                <td class="py-2 text-muted-foreground">{{ entry.date }}</td>
                <td class="py-2 text-right tabular-nums">{{ formatCompactNumber(entry.requests) }}</td>
                <td class="py-2 text-right tabular-nums">{{ formatCompactNumber(entry.tokens) }}</td>
                <td class="py-2 text-right tabular-nums">${{ entry.cost.toFixed(4) }}</td>
              </tr>
            </tbody>
          </table>
        </ScrollArea>
      </CardContent>
    </Card>
  </div>
</template>
