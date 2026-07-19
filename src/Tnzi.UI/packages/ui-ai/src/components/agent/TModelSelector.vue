<script setup lang="ts">
/**
 * TModelSelector — Model selection dialog with search filtering
 */

import { NModal, NInput, NButton } from 'naive-ui';
import { computed, ref } from 'vue';
import { Icon } from '@iconify/vue';
import { useAiI18n } from '@/locale/index';
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

const searchQuery = ref('');

const filtered = computed(() => {
  const query = searchQuery.value.toLowerCase().trim();
  if (!query) return props.models;
  return props.models.filter(
    (m) =>
      m.name.toLowerCase().includes(query) ||
      m.provider.toLowerCase().includes(query) ||
      m.id.toLowerCase().includes(query),
  );
});

const grouped = computed(() => {
  const groups = new Map<string, ModelOption[]>();
  for (const model of filtered.value) {
    const key = model.group ?? model.provider;
    const existing = groups.get(key) ?? [];
    groups.set(key, [...existing, model]);
  }
  return groups;
});

const showModal = computed({
  get: () => props.open,
  set: (val) => emit('update:open', val),
});

function selectModel(modelId: string): void {
  emit('update:modelValue', modelId);
  emit('update:open', false);
}

function handleAfterEnter(): void {
  searchQuery.value = '';
}

function getProviderLogoUrl(provider: string): string {
  return `https://models.dev/logos/${provider}.svg`;
}
</script>

<template>
  <div @click="showModal = true">
    <slot name="trigger">
      <NButton secondary size="small">
        <template #icon><Icon icon="lucide:cpu" /></template>
        {{ t.model.select }}
      </NButton>
    </slot>
  </div>

  <NModal
    v-model:show="showModal"
    preset="card"
    :title="t.model.select"
    class="!max-w-md"
    :auto-focus="false"
    @after-enter="handleAfterEnter"
  >
    <div class="flex flex-col gap-2">
      <NInput
        v-model:value="searchQuery"
        :placeholder="t.model.search"
        clearable
        size="large"
      >
        <template #prefix>
          <Icon icon="lucide:search" class="size-4 text-muted-foreground" />
        </template>
      </NInput>

      <div class="max-h-[300px] overflow-y-auto">
        <template v-if="filtered.length === 0">
          <div class="py-6 text-center text-sm text-muted-foreground">
            {{ t.model.noResults }}
          </div>
        </template>
        <template v-else>
          <template v-for="[group, models] in grouped" :key="group">
            <div class="px-2 py-1.5 text-xs font-medium text-muted-foreground">
              {{ group }}
            </div>
            <div
              v-for="model in models"
              :key="model.id"
              class="flex cursor-pointer items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent"
              @click="selectModel(model.id)"
            >
              <img
                :src="getProviderLogoUrl(model.provider)"
                :alt="model.provider"
                class="size-4 t-model-logo"
                loading="lazy"
              />
              <span class="flex-1 truncate text-xs">{{ model.name }}</span>
              <Icon
                v-if="model.id === modelValue"
                icon="lucide:check"
                class="size-3.5 text-primary"
              />
            </div>
            <div class="my-1 h-px bg-border last:hidden" />
          </template>
        </template>
      </div>
    </div>
  </NModal>
</template>

<style scoped>
/* Provider logos that need dark-mode inversion */
.dark .t-model-logo,
[data-theme='dark'] .t-model-logo {
  filter: invert(1);
}
</style>
