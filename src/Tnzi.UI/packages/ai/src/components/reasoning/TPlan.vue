<script setup lang="ts">
/**
 * TPlan — Collapsible plan display card
 *
 * Shows a plan with shimmer-animated title during streaming.
 * Uses Card + Collapsible from shadcn-vue.
 */

import { ref } from 'vue';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/primitives/ui/collapsible';
import { Card, CardHeader, CardTitle, CardDescription, CardContent, CardFooter } from '@/primitives/ui/card';
import { Button } from '@/primitives/ui/button';
import TShimmer from '../streaming/TShimmer.vue';

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
  <Collapsible v-model:open="isOpen">
    <Card class="overflow-hidden">
      <CardHeader class="flex flex-row items-center justify-between space-y-0 pb-2">
        <div class="space-y-1">
          <CardTitle class="text-sm font-medium">
            <TShimmer v-if="isStreaming">{{ title }}</TShimmer>
            <template v-else>{{ title }}</template>
          </CardTitle>
          <CardDescription v-if="description" class="text-xs text-balance">
            <TShimmer v-if="isStreaming">{{ description }}</TShimmer>
            <template v-else>{{ description }}</template>
          </CardDescription>
        </div>
        <CollapsibleTrigger as-child>
          <Button variant="ghost" size="icon-sm">
            <Icon
              icon="lucide:chevrons-up-down"
              class="size-4 text-muted-foreground"
            />
            <span class="sr-only">{{ t.plan.collapse }}</span>
          </Button>
        </CollapsibleTrigger>
      </CardHeader>
      <CollapsibleContent class="data-[state=closed]:animate-out data-[state=open]:animate-in data-[state=open]:slide-in-from-top-2 data-[state=closed]:slide-out-to-top-2 data-[state=closed]:fade-out-0">
        <CardContent class="pt-0">
          <slot />
        </CardContent>
        <CardFooter v-if="$slots.footer" class="pt-0">
          <slot name="footer" />
        </CardFooter>
      </CollapsibleContent>
    </Card>
  </Collapsible>
</template>
