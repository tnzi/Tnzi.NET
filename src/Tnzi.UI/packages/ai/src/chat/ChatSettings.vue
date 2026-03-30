<script setup lang="ts">
/**
 * ChatSettings — Settings panel: model, temperature, system prompt, skills
 */

import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
import { Button } from '@/primitives/ui/button';
import { Input } from '@/primitives/ui/input';
import { Textarea } from '@/primitives/ui/textarea';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/primitives/ui/select';
import { Separator } from '@/primitives/ui/separator';
import { ScrollArea } from '@/primitives/ui/scroll-area';

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

function updateField<K extends keyof ChatSettingsData>(key: K, value: ChatSettingsData[K]): void {
  emit('update:settings', { ...props.settings, [key]: value });
}
</script>

<template>
  <div class="flex h-full flex-col">
    <div class="flex items-center justify-between border-b px-4 py-2">
      <span class="text-sm font-medium">{{ t.admin.settings }}</span>
      <Button variant="ghost" size="icon-sm" @click="$emit('close')">
        <Icon icon="lucide:x" class="size-4" />
      </Button>
    </div>
    <ScrollArea class="flex-1 p-4">
      <div class="space-y-4">
        <!-- Model -->
        <div class="space-y-1.5">
          <label class="text-xs font-medium">{{ t.model.select }}</label>
          <Select :model-value="settings.modelId" @update:model-value="updateField('modelId', String($event))">
            <SelectTrigger class="h-8">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem
                v-for="model in availableModels"
                :key="model.id"
                :value="model.id"
              >
                {{ model.name }}
              </SelectItem>
            </SelectContent>
          </Select>
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

        <Separator />

        <!-- System prompt -->
        <div class="space-y-1.5">
          <label class="text-xs font-medium">{{ t.admin.systemPrompt }}</label>
          <Textarea
            :model-value="settings.systemPrompt"
            rows="6"
            class="text-xs resize-none"
            @update:model-value="updateField('systemPrompt', String($event))"
          />
        </div>
      </div>
    </ScrollArea>
  </div>
</template>
