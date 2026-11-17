<script setup lang="ts">
/**
 * TSkillCard - Skill card with icon, description, and activate button
 *
 * The whole card is clickable (`click`); the Activate / Deactivate button in
 * the footer stops its click so it does not also open the skill. The card is
 * a focusable group rather than a `<button>`, because it holds a button of
 * its own: Enter or Space on the card itself is the same as a click.
 */

import { Icon } from '@iconify/vue';
import { useAiI18n } from '../../i18n/index';
import TButton from '../controls/TButton.vue';
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
  <div
    class="t-skill-card"
    tabindex="0"
    @click="$emit('click', skill)"
    @keydown.enter.self="$emit('click', skill)"
    @keydown.space.self.prevent="$emit('click', skill)"
  >
    <div class="t-skill-card__head">
      <Icon :icon="skill.icon ?? 'lucide:zap'" class="t-skill-card__icon" aria-hidden="true" />
      <span class="t-skill-card__name">{{ skill.name }}</span>
      <span v-if="skill.isBuiltIn" class="t-skill-card__badge-builtin">{{ t.skill.builtIn }}</span>
    </div>
    <div v-if="skill.description" class="t-skill-card__desc">{{ skill.description }}</div>
    <div class="t-skill-card__foot">
      <slot name="footer">
        <span v-if="skill.category" class="t-skill-card__badge-category">{{ skill.category }}</span>
        <span v-else class="flex-1" />
        <TButton
          v-if="skill.isActive"
          variant="secondary"
          class="t-skill-card__action"
          @click.stop="$emit('deactivate', skill.slug)"
        >{{ t.skill.deactivate }}</TButton>
        <TButton
          v-else
          variant="soft"
          class="t-skill-card__action"
          @click.stop="$emit('activate', skill.slug)"
        >{{ t.skill.activate }}</TButton>
      </slot>
    </div>
  </div>
</template>

<style scoped>
/* The package's card: surface, one hairline, the radius of TResourceCard. */
.t-skill-card {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
  padding: 12px;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 14px;
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text);
  cursor: pointer;
  transition: border-color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
              box-shadow var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-skill-card:hover {
  border-color: var(--tnzi-ai-border-strong);
  box-shadow: 0 2px 10px rgba(0, 0, 0, 0.04);
}
.t-skill-card:focus-visible {
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: 2px;
}
.t-skill-card__head {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}
.t-skill-card__icon {
  flex-shrink: 0;
  width: 20px;
  height: 20px;
  color: var(--tnzi-ai-accent-text);
}
.t-skill-card__name {
  flex: 1;
  min-width: 0;
  font-size: 14px;
  font-weight: 500;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-skill-card__desc {
  font-size: 12px;
  line-height: 1.5;
  color: var(--tnzi-ai-text-secondary);
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
.t-skill-card__foot {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  margin-top: auto;
}
.t-skill-card__badge-builtin,
.t-skill-card__badge-category {
  flex-shrink: 0;
  font-size: 11px;
  padding: 1px 6px;
  border-radius: 999px;
  white-space: nowrap;
  color: var(--tnzi-ai-text-secondary);
}
.t-skill-card__badge-builtin { background: var(--tnzi-ai-hover); }
.t-skill-card__badge-category {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  border: 1px solid var(--tnzi-ai-border);
}
.t-skill-card__action { flex-shrink: 0; }
</style>
