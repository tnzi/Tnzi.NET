<script setup lang="ts">
/**
 * PersonaSelector — Agent persona/role selection dropdown
 */

import { NDropdown, NButton } from 'naive-ui';
import { h, computed } from 'vue';
import { Icon } from '@iconify/vue';
import { TAvatar } from '@tnzi/ui';
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

const options = computed(() =>
  props.personas.length === 0
    ? [{ key: 'empty', type: 'render' as const, render: () => h('div', { style: 'padding: 16px; text-align: center; font-size: 14px; color: var(--tnzi-base-text-muted)' }, t.value.persona.noPersonas) }]
    : props.personas.map((persona) => ({
        key: persona.id,
        label: persona.name,
        icon: () => h(TAvatar, { src: persona.avatarUrl, name: persona.name, size: 20, maxInitials: 2 }),
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
