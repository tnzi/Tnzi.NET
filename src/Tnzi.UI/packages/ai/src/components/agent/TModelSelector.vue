<script setup lang="ts">
/**
 * TModelSelector — Model selection dialog with Command search
 */

import { computed } from 'vue';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogTrigger } from '@/primitives/ui/dialog';
import { Command, CommandInput, CommandList, CommandEmpty, CommandGroup, CommandItem, CommandSeparator } from '@/primitives/ui/command';
import { Button } from '@/primitives/ui/button';

const t = useAiI18n();

export interface ModelOption {
  id: string;
  name: string;
  provider: string;
  group?: string;
}

const props = withDefaults(defineProps<{
  models: ModelOption[];
  modelValue?: string;
  open?: boolean;
}>(), {
  open: false,
});

const emit = defineEmits<{
  'update:modelValue': [modelId: string];
  'update:open': [open: boolean];
}>();

const grouped = computed(() => {
  const groups = new Map<string, ModelOption[]>();
  for (const model of props.models) {
    const key = model.group ?? model.provider;
    const existing = groups.get(key) ?? [];
    groups.set(key, [...existing, model]);
  }
  return groups;
});

function selectModel(modelId: string): void {
  emit('update:modelValue', modelId);
  emit('update:open', false);
}

function getProviderLogoUrl(provider: string): string {
  return `https://models.dev/logos/${provider}.svg`;
}
</script>

<template>
  <Dialog :open="open" @update:open="emit('update:open', $event)">
    <DialogTrigger as-child>
      <slot name="trigger">
        <Button variant="outline" size="sm" class="gap-2">
          <Icon icon="lucide:cpu" class="size-4" />
          {{ t.model.select }}
        </Button>
      </slot>
    </DialogTrigger>
    <DialogContent class="max-w-md p-0">
      <DialogHeader class="sr-only">
        <DialogTitle>{{ t.model.select }}</DialogTitle>
      </DialogHeader>
      <Command>
        <CommandInput :placeholder="t.model.search" class="py-3.5" />
        <CommandList class="max-h-[300px]">
          <CommandEmpty>{{ t.model.noResults }}</CommandEmpty>
          <template v-for="[group, models] in grouped" :key="group">
            <CommandGroup :heading="group">
              <CommandItem
                v-for="model in models"
                :key="model.id"
                :value="model.id"
                class="flex items-center gap-2"
                @select="selectModel(model.id)"
              >
                <img :src="getProviderLogoUrl(model.provider)" :alt="model.provider" class="size-4 dark:invert" loading="lazy" />
                <span class="flex-1 truncate text-xs">{{ model.name }}</span>
                <Icon v-if="model.id === modelValue" icon="lucide:check" class="size-3.5 text-primary" />
              </CommandItem>
            </CommandGroup>
            <CommandSeparator />
          </template>
        </CommandList>
      </Command>
    </DialogContent>
  </Dialog>
</template>
