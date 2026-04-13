<script setup lang="ts">
/**
 * PersonaSelector — Agent persona/role selection dropdown
 */

import { DropdownMenu, Button } from '../../primitives';
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

const options = computed(() => [
  {
    key: 'header',
    type: 'render',
    render: () => h('div', { class: 'px-2 py-1.5 text-xs font-semibold text-muted-foreground' }, t.value.persona.select),
  },
  { type: 'divider', key: 'd1' },
  ...(props.personas.length === 0
    ? [{
        key: 'empty',
        type: 'render' as const,
        render: () => h('div', { class: 'py-4 text-center text-sm text-muted-foreground' }, t.value.persona.noPersonas),
      }]
    : props.personas.map((persona) => ({
        key: persona.id,
        label: persona.name,
        icon: () =>
          persona.avatarUrl
            ? h('img', { src: persona.avatarUrl, class: 'size-5 rounded-full' })
            : h('span', { class: 'inline-flex size-5 items-center justify-center rounded-full bg-muted text-[10px] font-medium' }, getInitials(persona.name)),
        props: {
          class: persona.id === props.selectedId ? 'text-primary' : '',
        },
      }))),
]);

function handleSelect(key: string): void {
  emit('select', key);
}
</script>

<template>
  <DropdownMenu :options="options" trigger="click" @select="handleSelect">
    <span class="inline-flex">
      <Button variant="outline" size="sm" class="gap-1.5">
        <Icon icon="lucide:user-circle" class="size-4" />
        {{ t.persona.select }}
      </Button>
    </span>
  </DropdownMenu>
</template>
