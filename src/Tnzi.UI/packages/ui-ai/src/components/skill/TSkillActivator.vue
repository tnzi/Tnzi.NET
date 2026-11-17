<script setup lang="ts">
/**
 * TSkillActivator - Skill activation config panel
 */

import { computed, ref } from 'vue';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '../../i18n/index';
import TButton from '../controls/TButton.vue';
import TField from '../controls/TField.vue';
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

// `required` is enforced here, not only drawn as a mark: a required parameter
// left empty, or holding only whitespace, keeps Activate disabled. The value
// is passed on as typed, not trimmed.
const missingRequired = computed(() =>
  (props.params ?? []).some((p) => p.required && (paramValues.value[p.key] ?? '').trim() === ''),
);

function handleActivate(): void {
  if (missingRequired.value) return;
  emit('activate', props.skill.slug, { ...paramValues.value });
}
</script>

<template>
  <div class="t-skill-activator">
    <div class="t-skill-activator__head">
      <Icon :icon="skill.icon ?? 'lucide:zap'" class="t-skill-activator__icon" aria-hidden="true" />
      <div class="min-w-0">
        <div class="t-skill-activator__name">{{ skill.name }}</div>
        <div v-if="skill.description" class="t-skill-activator__desc">{{ skill.description }}</div>
      </div>
    </div>
    <div v-if="params?.length" class="t-skill-activator__params">
      <TField v-for="param in params" :key="param.key" :label="param.label" :required="param.required">
        <template #default="{ id }">
          <input
            :id="id"
            v-model="paramValues[param.key]"
            type="text"
            class="t-input"
            :required="param.required"
            :inputmode="param.type === 'number' ? 'numeric' : undefined"
          />
        </template>
      </TField>
    </div>
    <div class="t-skill-activator__actions">
      <TButton @click="$emit('cancel')">{{ t.common.cancel }}</TButton>
      <TButton variant="primary" :disabled="missingRequired" @click="handleActivate">{{ t.skill.activate }}</TButton>
    </div>
  </div>
</template>

<style scoped>
/* The package's card: surface, one hairline, the radius of TResourceCard. */
.t-skill-activator {
  display: flex;
  flex-direction: column;
  gap: 16px;
  padding: 16px;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 14px;
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text);
}
.t-skill-activator__head {
  display: flex;
  align-items: center;
  gap: 10px;
}
.t-skill-activator__icon {
  flex-shrink: 0;
  width: 24px;
  height: 24px;
  color: var(--tnzi-ai-accent-text);
}
.t-skill-activator__name {
  font-size: 15px;
  font-weight: 500;
}
.t-skill-activator__desc {
  font-size: 13px;
  line-height: 1.5;
  color: var(--tnzi-ai-text-secondary);
}
.t-skill-activator__params {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.t-skill-activator__actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
</style>
