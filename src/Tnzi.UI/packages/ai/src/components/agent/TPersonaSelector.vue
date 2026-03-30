<script setup lang="ts">
/**
 * TPersonaSelector — Agent persona/role selection dropdown
 */

import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
import { DropdownMenu, DropdownMenuTrigger, DropdownMenuContent, DropdownMenuItem, DropdownMenuLabel, DropdownMenuSeparator } from '@/primitives/ui/dropdown-menu';
import { Button } from '@/primitives/ui/button';
import { Avatar, AvatarFallback, AvatarImage } from '@/primitives/ui/avatar';

const t = useAiI18n();

export interface PersonaOption {
  id: string;
  name: string;
  slug: string;
  description?: string;
  avatarUrl?: string;
}

defineProps<{
  personas: PersonaOption[];
  selectedId?: string;
}>();

const emit = defineEmits<{
  select: [personaId: string];
}>();

function getInitials(name: string): string {
  return name.slice(0, 2).toUpperCase();
}
</script>

<template>
  <DropdownMenu>
    <DropdownMenuTrigger as-child>
      <Button variant="outline" size="sm" class="gap-1.5">
        <Icon icon="lucide:user-circle" class="size-4" />
        {{ t.persona.select }}
      </Button>
    </DropdownMenuTrigger>
    <DropdownMenuContent align="start" class="w-[240px]">
      <DropdownMenuLabel>{{ t.persona.select }}</DropdownMenuLabel>
      <DropdownMenuSeparator />
      <template v-if="personas.length === 0">
        <div class="py-4 text-center text-sm text-muted-foreground">{{ t.persona.noPersonas }}</div>
      </template>
      <DropdownMenuItem v-for="persona in personas" :key="persona.id" class="gap-2" @click="emit('select', persona.id)">
        <Avatar class="size-6">
          <AvatarImage v-if="persona.avatarUrl" :src="persona.avatarUrl" />
          <AvatarFallback class="text-[10px]">{{ getInitials(persona.name) }}</AvatarFallback>
        </Avatar>
        <div class="flex-1 min-w-0">
          <div class="text-sm font-medium truncate">{{ persona.name }}</div>
          <div v-if="persona.description" class="text-xs text-muted-foreground truncate">{{ persona.description }}</div>
        </div>
        <Icon v-if="persona.id === selectedId" icon="lucide:check" class="size-3.5 text-primary shrink-0" />
      </DropdownMenuItem>
    </DropdownMenuContent>
  </DropdownMenu>
</template>
