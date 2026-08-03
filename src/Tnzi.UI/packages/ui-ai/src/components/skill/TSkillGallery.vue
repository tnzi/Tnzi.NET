<script setup lang="ts">
/**
 * TSkillGallery - Skill store browsing with category filter and search
 */

import { NInput, NScrollbar } from 'naive-ui';
import { ref, computed } from 'vue';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '../../i18n/index';
import { useLocalSearch } from '../../headless/useLocalSearch';
import TSkillCard, { type SkillInfo } from './TSkillCard.vue';

const t = useAiI18n();

const props = defineProps<{
  skills: SkillInfo[];
  categories?: string[];
}>();

const emit = defineEmits<{
  activate: [slug: string];
  deactivate: [slug: string];
  'skill-click': [skill: SkillInfo];
}>();

const selectedCategory = ref<string | null>(null);

const { query: searchQuery, filtered: textFiltered } = useLocalSearch(
  () => props.skills,
  ['name', 'description'],
);

const filtered = computed(() => {
  if (!selectedCategory.value) return textFiltered.value;
  return textFiltered.value.filter((s) => s.category === selectedCategory.value);
});
</script>

<template>
  <div class="space-y-3">
    <div class="relative">
      <Icon icon="lucide:search" class="t-skill-gallery__search-icon" />
      <NInput v-model:value="searchQuery" :placeholder="t.skill.search" size="small" />
    </div>
    <div v-if="categories?.length" class="flex flex-wrap gap-1">
      <span
        class="t-skill-gallery__cat-badge"
        :class="{ 't-skill-gallery__cat-badge--active': selectedCategory === null }"
        @click="selectedCategory = null"
      >All</span>
      <span
        v-for="cat in categories"
        :key="cat"
        class="t-skill-gallery__cat-badge"
        :class="{ 't-skill-gallery__cat-badge--active': selectedCategory === cat }"
        @click="selectedCategory = selectedCategory === cat ? null : cat"
      >{{ cat }}</span>
    </div>
    <NScrollbar style="max-height: 500px">
      <div v-if="filtered.length === 0" class="py-8 text-center text-sm t-skill-gallery__empty">{{ t.skill.noResults }}</div>
      <div v-else class="grid grid-cols-2 gap-2 pr-1">
        <TSkillCard
          v-for="skill in filtered"
          :key="skill.id"
          :skill="skill"
          @activate="emit('activate', $event)"
          @deactivate="emit('deactivate', $event)"
          @click="emit('skill-click', $event)"
        />
      </div>
    </NScrollbar>
  </div>
</template>

<style scoped>
.t-skill-gallery__search-icon {
  position: absolute;
  left: 8px;
  top: 50%;
  transform: translateY(-50%);
  width: 14px;
  height: 14px;
  color: var(--tnzi-base-text-muted);
  pointer-events: none;
  z-index: 1;
}
.t-skill-gallery__empty { color: var(--tnzi-base-text-muted); }
.t-skill-gallery__cat-badge {
  font-size: 11px;
  padding: 2px 8px;
  border-radius: 4px;
  border: 1px solid var(--tnzi-border);
  cursor: pointer;
  color: var(--tnzi-base-text-muted);
  transition: background-color 0.15s, color 0.15s;
}
.t-skill-gallery__cat-badge--active {
  background-color: var(--tnzi-primary);
  border-color: var(--tnzi-primary);
  color: var(--tnzi-ai-on-accent);
}
</style>
