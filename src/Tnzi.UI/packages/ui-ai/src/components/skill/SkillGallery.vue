<script setup lang="ts">
/**
 * SkillGallery — Skill store browsing with category filter and search
 */

import { Input, ScrollArea, Badge } from '../../primitives';
import { ref, computed } from 'vue';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
import { useLocalSearch } from '@/composables/useLocalSearch';
import SkillCard, { type SkillInfo } from './SkillCard.vue';

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
      <Icon icon="lucide:search" class="absolute left-2.5 top-2.5 size-4 text-muted-foreground" />
      <Input v-model="searchQuery" :placeholder="t.skill.search" class="pl-9" />
    </div>
    <div v-if="categories?.length" class="flex flex-wrap gap-1">
      <Badge :variant="selectedCategory === null ? 'default' : 'outline'" class="cursor-pointer text-xs" @click="selectedCategory = null">All</Badge>
      <Badge v-for="cat in categories" :key="cat" :variant="selectedCategory === cat ? 'default' : 'outline'" class="cursor-pointer text-xs" @click="selectedCategory = selectedCategory === cat ? null : cat">{{ cat }}</Badge>
    </div>
    <ScrollArea class="max-h-[500px]">
      <div v-if="filtered.length === 0" class="py-8 text-center text-sm text-muted-foreground">{{ t.skill.noResults }}</div>
      <div v-else class="grid grid-cols-2 gap-2">
        <SkillCard v-for="skill in filtered" :key="skill.id" :skill="skill" @activate="emit('activate', $event)" @deactivate="emit('deactivate', $event)" @click="emit('skill-click', $event)" />
      </div>
    </ScrollArea>
  </div>
</template>
