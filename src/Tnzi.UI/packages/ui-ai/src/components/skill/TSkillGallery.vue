<script setup lang="ts">
/**
 * TSkillGallery - Skill store browsing with category filter and search
 *
 * The search matches name and description; a category chip narrows the
 * result further, and pressing the selected chip again clears it. The cards
 * scroll inside the gallery (500px at most).
 */

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
  <div class="t-skill-gallery">
    <div class="t-skill-gallery__search">
      <Icon icon="lucide:search" class="t-skill-gallery__search-icon" aria-hidden="true" />
      <input
        v-model="searchQuery"
        type="text"
        class="t-input t-skill-gallery__search-input"
        :placeholder="t.skill.search"
        :aria-label="t.skill.search"
      />
    </div>
    <div v-if="categories?.length" class="t-skill-gallery__cats" role="group" :aria-label="t.skill.category">
      <button
        type="button"
        class="t-skill-gallery__cat"
        :class="{ 't-skill-gallery__cat--active': selectedCategory === null }"
        :aria-pressed="selectedCategory === null"
        @click="selectedCategory = null"
      >{{ t.common.all }}</button>
      <button
        v-for="cat in categories"
        :key="cat"
        type="button"
        class="t-skill-gallery__cat"
        :class="{ 't-skill-gallery__cat--active': selectedCategory === cat }"
        :aria-pressed="selectedCategory === cat"
        @click="selectedCategory = selectedCategory === cat ? null : cat"
      >{{ cat }}</button>
    </div>
    <div class="t-ai-scroll t-skill-gallery__list">
      <div v-if="filtered.length === 0" class="t-skill-gallery__empty">{{ t.skill.noResults }}</div>
      <div v-else class="t-skill-gallery__grid">
        <TSkillCard
          v-for="skill in filtered"
          :key="skill.id"
          :skill="skill"
          @activate="emit('activate', $event)"
          @deactivate="emit('deactivate', $event)"
          @click="emit('skill-click', $event)"
        />
      </div>
    </div>
  </div>
</template>

<style scoped>
.t-skill-gallery {
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-width: 0;
}
.t-skill-gallery__search { position: relative; }
.t-skill-gallery__search-icon {
  position: absolute;
  left: 10px;
  top: 50%;
  transform: translateY(-50%);
  width: 14px;
  height: 14px;
  color: var(--tnzi-ai-text-tertiary);
  pointer-events: none;
}
.t-skill-gallery__search-input { padding-left: 30px; }
.t-skill-gallery__cats {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
.t-skill-gallery__cat {
  height: 24px;
  padding: 0 10px;
  border: 1px solid var(--tnzi-ai-border);
  border-radius: 999px;
  background: transparent;
  color: var(--tnzi-ai-text-secondary);
  font: inherit;
  font-size: 12px;
  cursor: pointer;
  transition: background-color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing),
              color var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}
.t-skill-gallery__cat:hover { background: var(--tnzi-ai-hover); }
.t-skill-gallery__cat:focus-visible {
  outline: var(--tnzi-ai-focus-outline);
  outline-offset: 2px;
}
.t-skill-gallery__cat--active,
.t-skill-gallery__cat--active:hover {
  border-color: var(--tnzi-ai-accent);
  background: var(--tnzi-ai-accent);
  color: var(--tnzi-ai-on-accent);
}
.t-skill-gallery__list {
  position: relative;
  max-height: 500px;
  overflow-y: auto;
  overscroll-behavior: contain;
}
.t-skill-gallery__grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px;
  padding-right: 4px;
}
/* One column where two cards side by side would cut both names short. */
@media (max-width: 640px) {
  .t-skill-gallery__grid { grid-template-columns: minmax(0, 1fr); }
}
.t-skill-gallery__empty {
  padding: 32px 0;
  text-align: center;
  font-size: 13px;
  color: var(--tnzi-ai-text-secondary);
}
</style>
