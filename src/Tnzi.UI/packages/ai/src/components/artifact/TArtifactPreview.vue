<script setup lang="ts">
/**
 * TArtifactPreview — iframe preview with address bar and console
 */

import { ref, watch } from 'vue';
import { Icon } from '@iconify/vue';
import { cn } from '@/lib/utils';
import { useAiI18n } from '@/locale/index';
import { Input } from '@/primitives/ui/input';
import { Button } from '@/primitives/ui/button';
import { Tooltip, TooltipContent, TooltipTrigger } from '@/primitives/ui/tooltip';
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/primitives/ui/collapsible';

const t = useAiI18n();

const props = defineProps<{
  src?: string;
  srcdoc?: string;
  logs?: Array<{ level: 'log' | 'warn' | 'error'; message: string; timestamp?: string }>;
}>();

const localUrl = ref(props.src ?? '');
const consoleOpen = ref(false);

watch(() => props.src, (v) => { if (v) localUrl.value = v; });

function openExternal(): void {
  window.open(localUrl.value || props.src, '_blank', 'noopener,noreferrer');
}

const logLevelClass: Record<string, string> = {
  log: 'text-foreground',
  warn: 'text-yellow-500',
  error: 'text-destructive',
};
</script>

<template>
  <div class="flex h-full flex-col">
    <div class="flex items-center gap-1 border-b px-2 py-1.5">
      <Tooltip>
        <TooltipTrigger as-child>
          <Button variant="ghost" size="icon-sm" @click="localUrl = src ?? ''">
            <Icon icon="lucide:refresh-cw" class="size-3.5" />
          </Button>
        </TooltipTrigger>
        <TooltipContent>{{ t.common.refresh }}</TooltipContent>
      </Tooltip>
      <Input v-model="localUrl" class="h-7 text-xs font-mono bg-muted" :readonly="!!srcdoc" @keydown.enter="localUrl = ($event.target as HTMLInputElement).value" />
      <Tooltip>
        <TooltipTrigger as-child>
          <Button variant="ghost" size="icon-sm" @click="openExternal">
            <Icon icon="lucide:square-arrow-out-up-right" class="size-3.5" />
          </Button>
        </TooltipTrigger>
        <TooltipContent>{{ t.artifact.openExternal }}</TooltipContent>
      </Tooltip>
    </div>
    <div class="relative flex-1 min-h-0">
      <iframe v-if="srcdoc" :srcdoc="srcdoc" class="h-full w-full border-0" sandbox="allow-scripts allow-same-origin allow-forms allow-popups allow-presentation" />
      <iframe v-else-if="localUrl" :src="localUrl" class="h-full w-full border-0" sandbox="allow-scripts allow-same-origin allow-forms allow-popups allow-presentation" />
      <div v-else class="flex h-full items-center justify-center text-sm text-muted-foreground">{{ t.artifact.noPreview }}</div>
    </div>
    <Collapsible v-if="logs?.length" v-model:open="consoleOpen">
      <CollapsibleTrigger class="flex w-full items-center gap-1.5 border-t px-3 py-1 text-xs text-muted-foreground hover:bg-accent/50 transition-colors">
        <Icon icon="lucide:terminal" class="size-3" />
        <span>{{ t.artifact.console }}</span>
        <span class="ml-auto tabular-nums">{{ logs.length }}</span>
        <Icon icon="lucide:chevron-down" :class="cn('size-3 transition-transform', consoleOpen && 'rotate-180')" />
      </CollapsibleTrigger>
      <CollapsibleContent class="max-h-[120px] overflow-auto border-t bg-muted/30 p-2 font-mono text-[11px]">
        <div v-for="(log, i) in logs" :key="i" :class="cn('py-0.5', logLevelClass[log.level])">{{ log.message }}</div>
      </CollapsibleContent>
    </Collapsible>
  </div>
</template>
