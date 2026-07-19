<script setup lang="ts">
/**
 * TSkillActivator - Skill activation config panel
 */

import { NCard, NButton, NInput } from 'naive-ui';
import { ref } from 'vue';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
import type { SkillInfo } from './TSkillCard.vue';

const t = useAiI18n();

export interface SkillParam {
  key: string;
  label: string;
  type: 'string' | 'number' | 'boolean';
  required?: boolean;
  defaultValue?: string;
}

const props = defineProps<{
  skill: SkillInfo;
  params?: SkillParam[];
}>();

const emit = defineEmits<{
  activate: [slug: string, config: Record<string, string>];
  cancel: [];
}>();

const paramValues = ref<Record<string, string>>(
  Object.fromEntries((props.params ?? []).map((p) => [p.key, p.defaultValue ?? ''])),
);

function handleActivate(): void {
  emit('activate', props.skill.slug, { ...paramValues.value });
}
</script>

<template>
  <NCard>
    <div class="flex items-center gap-2 mb-3">
      <Icon :icon="skill.icon ?? 'lucide:zap'" class="size-6 t-skill-activator__icon" />
      <div>
        <div class="text-base font-medium">{{ skill.name }}</div>
        <div v-if="skill.description" class="text-sm t-skill-activator__desc">{{ skill.description }}</div>
      </div>
    </div>
    <div v-if="params?.length" class="space-y-3 mb-4">
      <div v-for="param in params" :key="param.key" class="space-y-1">
        <label class="text-xs font-medium">
          {{ param.label }}
          <span v-if="param.required" class="t-skill-activator__required ml-1">*</span>
        </label>
        <NInput v-model:value="paramValues[param.key]" size="small" :inputmode="param.type === 'number' ? 'numeric' : undefined" />
      </div>
    </div>
    <div class="flex justify-end gap-2">
      <NButton secondary size="small" @click="$emit('cancel')">{{ t.common.cancel }}</NButton>
      <NButton type="primary" size="small" @click="handleActivate">{{ t.skill.activate }}</NButton>
    </div>
  </NCard>
</template>
