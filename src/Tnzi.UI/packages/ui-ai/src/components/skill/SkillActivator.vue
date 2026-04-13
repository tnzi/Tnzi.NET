<script setup lang="ts">
/**
 * SkillActivator — Skill activation config panel
 */

import {
  Card,
  CardHeader,
  CardTitle,
  CardDescription,
  CardContent,
  CardFooter,
  Button,
  Input,
  Badge,
} from '../../primitives';
import { ref } from 'vue';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
import type { SkillInfo } from './SkillCard.vue';

const t = useAiI18n();

export interface SkillParam {
  key: string;
  label: string;
  type: 'string' | 'number' | 'boolean';
  required?: boolean;
  defaultValue?: string;
}

const props = defineProps<{
  skill: SkillInfo;
  params?: SkillParam[];
}>();

const emit = defineEmits<{
  activate: [slug: string, config: Record<string, string>];
  cancel: [];
}>();

const paramValues = ref<Record<string, string>>(
  Object.fromEntries((props.params ?? []).map((p) => [p.key, p.defaultValue ?? ''])),
);

function handleActivate(): void {
  emit('activate', props.skill.slug, { ...paramValues.value });
}
</script>

<template>
  <Card>
    <CardHeader class="pb-2">
      <div class="flex items-center gap-2">
        <Icon :icon="skill.icon ?? 'lucide:zap'" class="size-6 text-primary" />
        <div>
          <CardTitle class="text-base">{{ skill.name }}</CardTitle>
          <CardDescription v-if="skill.description">{{ skill.description }}</CardDescription>
        </div>
      </div>
    </CardHeader>
    <CardContent v-if="params?.length" class="space-y-3">
      <div v-for="param in params" :key="param.key" class="space-y-1">
        <label class="text-xs font-medium text-foreground">
          {{ param.label }}
          <Badge v-if="param.required" variant="destructive" class="ml-1 text-[9px] px-1 py-0">*</Badge>
        </label>
        <Input v-model="paramValues[param.key]" :input-props="param.type === 'number' ? { inputmode: 'numeric' } : undefined" class="h-8" />
      </div>
    </CardContent>
    <CardFooter class="gap-2 justify-end">
      <Button variant="outline" size="sm" @click="$emit('cancel')">{{ t.common.cancel }}</Button>
      <Button size="sm" @click="handleActivate">{{ t.skill.activate }}</Button>
    </CardFooter>
  </Card>
</template>
