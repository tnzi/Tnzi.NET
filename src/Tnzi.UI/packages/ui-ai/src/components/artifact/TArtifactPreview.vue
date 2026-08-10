<script setup lang="ts">
/**
 * TArtifactPreview - iframe preview with address bar and console
 */

import { NInput, NButton, NTooltip } from 'naive-ui';
import { ref, watch } from 'vue';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '../../i18n/index';
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
  log: '',
  warn: 't-preview-log--warn',
  error: 't-preview-log--error',
};
</script>

<template>
  <div class="flex h-full flex-col">
    <div class="flex items-center gap-1 border-b px-2 py-1.5">
      <NTooltip>
        <template #trigger>
          <NButton quaternary size="tiny" @click="localUrl = src ?? ''">
            <template #icon><Icon icon="lucide:refresh-cw" /></template>
          </NButton>
        </template>
        {{ t.common.refresh }}
      </NTooltip>
      <NInput v-model:value="localUrl" size="small" :readonly="!!srcdoc" class="font-mono" style="height: 28px" @keydown.enter="localUrl = ($event.target as HTMLInputElement).value" />
      <NTooltip>
        <template #trigger>
          <NButton quaternary size="tiny" @click="openExternal">
            <template #icon><Icon icon="lucide:square-arrow-out-up-right" /></template>
          </NButton>
        </template>
        {{ t.artifact.openExternal }}
      </NTooltip>
    </div>
    <div class="relative flex-1 min-h-0">
      <iframe v-if="srcdoc" :srcdoc="srcdoc" class="h-full w-full border-0" sandbox="allow-scripts allow-same-origin allow-forms allow-popups allow-presentation" />
      <iframe v-else-if="localUrl" :src="localUrl" class="h-full w-full border-0" sandbox="allow-scripts allow-same-origin allow-forms allow-popups allow-presentation" />
      <div v-else class="flex h-full items-center justify-center text-sm text-tnzi-muted">{{ t.artifact.noPreview }}</div>
    </div>
    <div v-if="logs?.length">
      <button class="flex w-full items-center gap-1.5 border-t px-3 py-1 text-xs text-tnzi-muted hover:bg-tnzi-layout/50 transition-colors" @click="consoleOpen = !consoleOpen">
        <Icon icon="lucide:terminal" class="size-3" />
        <span>{{ t.artifact.console }}</span>
        <span class="ml-auto tabular-nums">{{ logs.length }}</span>
        <Icon icon="lucide:chevron-down" class="size-3 transition-transform" :class="{ 'rotate-180': consoleOpen }" />
      </button>
      <div v-show="consoleOpen" class="max-h-[120px] overflow-auto border-t bg-tnzi-layout/30 p-2 font-mono text-[11px]">
        <div v-for="(log, i) in logs" :key="i" class="py-0.5" :class="logLevelClass[log.level]">{{ log.message }}</div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.t-preview-log--warn {
  color: var(--tnzi-warning);
}
.t-preview-log--error {
  color: var(--tnzi-error);
}
</style>
