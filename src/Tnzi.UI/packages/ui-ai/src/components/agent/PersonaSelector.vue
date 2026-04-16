<script setup lang="ts">
/**
 * PersonaSelector — Agent persona/role selection dropdown
 */

import { NDropdown, NButton } from 'naive-ui';
import { h, computed } from 'vue';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
const t = useAiI18n();

export interface PersonaOption {
  id: string;
  name: string;
  slug: string;
  description?: string;
  avatarUrl?: string;
}

const props = defineProps<{
  personas: PersonaOption[];
  selectedId?: string;
}>();

const emit = defineEmits<{
  select: [personaId: string];
}>();

function getInitials(name: string): string {
  return name.slice(0, 2).toUpperCase();
}

const options = computed(() =>
  props.personas.length === 0
    ? [{ key: 'empty', type: 'render' as const, render: () => h('div', { style: 'padding: 16px; text-align: center; font-size: 14px; color: var(--tnzi-base-text-muted)' }, t.value.persona.noPersonas) }]
    : props.personas.map((persona) => ({
        key: persona.id,
        label: persona.name,
        icon: () =>
          persona.avatarUrl
            ? h('img', { src: persona.avatarUrl, style: 'width: 20px; height: 20px; border-radius: 50%' })
            : h('span', { style: 'display: inline-flex; width: 20px; height: 20px; align-items: center; justify-content: center; border-radius: 50%; background: var(--tnzi-container-bg); font-size: 10px; font-weight: 500' }, getInitials(persona.name)),
        class: persona.id === props.selectedId ? 't-persona-selected' : '',
      })),
);

function handleSelect(key: string): void {
  emit('select', key);
}
</script>

<template>
  <NDropdown :options="options" trigger="click" @select="handleSelect">
    <NButton secondary size="small">
      <template #icon><Icon icon="lucide:user-circle" class="size-4" /></template>
      {{ t.persona.select }}
    </NButton>
  </NDropdown>
</template>

<style>
/* global — NDropdown renders outside component scope */
.t-persona-selected { color: var(--tnzi-primary); }
</style>
