<script setup lang="ts">
/**
 * AgentPlan — Collapsible plan display card
 *
 * Shows a plan with shimmer-animated title during streaming.
 * Uses Card + Collapsible from shadcn-vue.
 */

import {
  Card,
  CardHeader,
  CardTitle,
  CardDescription,
  CardContent,
  CardFooter,
  Button,
} from '../../primitives';
import { ref } from 'vue';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
import Shimmer from '../streaming/Shimmer.vue';

const t = useAiI18n();

const props = withDefaults(defineProps<{
  /** Plan title. */
  title: string;
  /** Plan description. */
  description?: string;
  /** Whether the plan is currently being streamed. */
  isStreaming?: boolean;
  /** Default open state. */
  defaultOpen?: boolean;
}>(), {
  isStreaming: false,
  defaultOpen: true,
});

const isOpen = ref(props.defaultOpen);
</script>

<template>
  <Card class="overflow-hidden">
    <CardHeader class="flex flex-row items-center justify-between space-y-0 pb-2">
      <div class="space-y-1">
        <CardTitle class="text-sm font-medium">
          <Shimmer v-if="isStreaming">{{ title }}</Shimmer>
          <template v-else>{{ title }}</template>
        </CardTitle>
        <CardDescription v-if="description" class="text-xs text-balance">
          <Shimmer v-if="isStreaming">{{ description }}</Shimmer>
          <template v-else>{{ description }}</template>
        </CardDescription>
      </div>
      <Button variant="ghost" size="icon-sm" @click="isOpen = !isOpen">
        <Icon
          icon="lucide:chevrons-up-down"
          class="size-4 text-muted-foreground"
        />
        <span class="sr-only">{{ t.plan.collapse }}</span>
      </Button>
    </CardHeader>
    <div v-show="isOpen">
      <CardContent class="pt-0">
        <slot />
      </CardContent>
      <CardFooter v-if="$slots.footer" class="pt-0">
        <slot name="footer" />
      </CardFooter>
    </div>
  </Card>
</template>
