<script setup lang="ts">
/**
 * ChatSettings — Settings panel: model, temperature, system prompt, skills
 */

import { computed } from 'vue';
import { NButton, NSelect, NInput, NDivider, NScrollbar } from 'naive-ui';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
const t = useAiI18n();

export interface ChatSettingsData {
  modelId: string;
  temperature: number;
  systemPrompt: string;
}

const props = defineProps<{
  settings: ChatSettingsData;
  availableModels?: Array<{ id: string; name: string }>;
}>();

const emit = defineEmits<{
  'update:settings': [settings: ChatSettingsData];
  close: [];
}>();

const modelOptions = computed(() =>
  (props.availableModels ?? []).map((m) => ({ label: m.name, value: m.id })),
);

function updateField<K extends keyof ChatSettingsData>(key: K, value: ChatSettingsData[K]): void {
  emit('update:settings', { ...props.settings, [key]: value });
}
</script>

<template>
  <div class="flex h-full flex-col">
    <div class="flex items-center justify-between border-b px-4 py-2">
      <span class="text-sm font-medium">{{ t.admin.settings }}</span>
      <NButton quaternary size="small" @click="$emit('close')">
        <template #icon><Icon icon="lucide:x" /></template>
      </NButton>
    </div>
    <NScrollbar class="flex-1 p-4">
      <div class="space-y-4">
        <!-- Model -->
        <div class="space-y-1.5">
          <label class="text-xs font-medium">{{ t.model.select }}</label>
          <NSelect
            :value="settings.modelId"
            :options="modelOptions"
            size="small"
            @update:value="updateField('modelId', String($event))"
          />
        </div>

        <!-- Temperature -->
        <div class="space-y-1.5">
          <label class="text-xs font-medium">{{ t.admin.temperature }}</label>
          <div class="flex items-center gap-2">
            <input
              type="range"
              :value="settings.temperature"
              min="0"
              max="2"
              step="0.1"
              class="flex-1 h-1.5 accent-primary"
              @input="updateField('temperature', parseFloat(($event.target as HTMLInputElement).value))"
            />
            <span class="text-xs tabular-nums w-8 text-right">{{ settings.temperature.toFixed(1) }}</span>
          </div>
        </div>

        <NDivider style="margin: 8px 0" />

        <!-- System prompt -->
        <div class="space-y-1.5">
          <label class="text-xs font-medium">{{ t.admin.systemPrompt }}</label>
          <NInput
            type="textarea"
            :value="settings.systemPrompt"
            :rows="6"
            class="text-xs"
            :resizable="false"
            @update:value="updateField('systemPrompt', String($event))"
          />
        </div>
      </div>
    </NScrollbar>
  </div>
</template>
