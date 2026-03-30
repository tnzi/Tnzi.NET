<script setup lang="ts">
/**
 * TSkillCard — Skill card with icon, description, and activate button
 */

import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
import { Card, CardHeader, CardTitle, CardDescription, CardContent, CardFooter } from '@/primitives/ui/card';
import { Button } from '@/primitives/ui/button';
import { Badge } from '@/primitives/ui/badge';

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
  <Card class="cursor-pointer transition-colors hover:bg-accent/50" @click="$emit('click', skill)">
    <CardHeader class="p-3 pb-1">
      <div class="flex items-center gap-2">
        <Icon :icon="skill.icon ?? 'lucide:zap'" class="size-5 shrink-0 text-primary" />
        <CardTitle class="text-sm truncate">{{ skill.name }}</CardTitle>
        <Badge v-if="skill.isBuiltIn" variant="secondary" class="text-[10px] px-1.5 py-0 ml-auto shrink-0">{{ t.skill.builtIn }}</Badge>
      </div>
    </CardHeader>
    <CardContent class="p-3 pt-0">
      <CardDescription v-if="skill.description" class="text-xs line-clamp-2">{{ skill.description }}</CardDescription>
    </CardContent>
    <CardFooter class="p-3 pt-0 justify-between">
      <slot name="footer">
        <Badge v-if="skill.category" variant="outline" class="text-[10px]">{{ skill.category }}</Badge>
        <Button v-if="skill.isActive" variant="outline" size="sm" class="h-6 text-xs" @click.stop="$emit('deactivate', skill.slug)">{{ t.skill.deactivate }}</Button>
        <Button v-else size="sm" class="h-6 text-xs" @click.stop="$emit('activate', skill.slug)">{{ t.skill.activate }}</Button>
      </slot>
    </CardFooter>
  </Card>
</template>
