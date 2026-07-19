<script setup lang="ts">
/**
 * TSkillCard — Skill card with icon, description, and activate button
 */

import { NCard, NButton } from 'naive-ui';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
const t = useAiI18n();

export interface SkillInfo {
  id: string;
  slug: string;
  name: string;
  description?: string;
  icon?: string;
  category?: string;
  isActive?: boolean;
  isBuiltIn?: boolean;
}

defineProps<{
  skill: SkillInfo;
}>();

defineEmits<{
  activate: [slug: string];
  deactivate: [slug: string];
  click: [skill: SkillInfo];
}>();
</script>

<template>
  <NCard
    size="small"
    hoverable
    class="t-skill-card"
    @click="$emit('click', skill)"
  >
    <div class="flex items-center gap-2 mb-1">
      <Icon :icon="skill.icon ?? 'lucide:zap'" class="size-5 shrink-0 t-skill-card__icon" />
      <span class="text-sm font-medium truncate flex-1">{{ skill.name }}</span>
      <span v-if="skill.isBuiltIn" class="t-skill-card__badge-builtin">{{ t.skill.builtIn }}</span>
    </div>
    <div v-if="skill.description" class="text-xs t-skill-card__desc line-clamp-2 mb-2">{{ skill.description }}</div>
    <div class="flex items-center justify-between">
      <slot name="footer">
        <span v-if="skill.category" class="t-skill-card__badge-category">{{ skill.category }}</span>
        <span v-else class="flex-1" />
        <NButton
          v-if="skill.isActive"
          size="tiny"
          secondary
          @click.stop="$emit('deactivate', skill.slug)"
        >{{ t.skill.deactivate }}</NButton>
        <NButton
          v-else
          size="tiny"
          type="primary"
          @click.stop="$emit('activate', skill.slug)"
        >{{ t.skill.activate }}</NButton>
      </slot>
    </div>
  </NCard>
</template>

<style scoped>
.t-skill-card { cursor: pointer; }
.t-skill-card__icon { color: var(--tnzi-primary); }
.t-skill-card__desc { color: var(--tnzi-base-text-muted); }
.t-skill-card__badge-builtin {
  font-size: 10px;
  padding: 1px 5px;
  border-radius: 4px;
  background-color: var(--tnzi-border);
  color: var(--tnzi-base-text-muted);
  white-space: nowrap;
  flex-shrink: 0;
}
.t-skill-card__badge-category {
  font-size: 10px;
  padding: 1px 5px;
  border-radius: 4px;
  border: 1px solid var(--tnzi-border);
  color: var(--tnzi-base-text-muted);
}
</style>
